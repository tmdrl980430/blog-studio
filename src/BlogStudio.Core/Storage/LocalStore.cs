using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BlogStudio.Core.Images;
using BlogStudio.Core.Posts;

namespace BlogStudio.Core.Storage;

public sealed class AppSettings
{
    public string ThemeSlug { get; set; } = "tech";
    public string Author { get; set; } = "";
    public string NaverBlogId { get; set; } = "";
    public string TistoryName { get; set; } = "";

    /// <summary>Claude Code에 넘길 모델 별칭 (sonnet / opus / haiku). 비우면 테마의 model_tier를 따름.</summary>
    public string ClaudeModel { get; set; } = "sonnet";

    /// <summary>요일별 트랙 덮어쓰기 (비우면 테마 기본값).</summary>
    public Dictionary<string, string> Schedule { get; set; } = new();

    // ── 이미지 ──
    /// <summary>썸네일 아래에 들어갈 블로그 이름 (비우면 작성자 이름).</summary>
    public string BlogDisplayName { get; set; } = "";
    public bool ThumbnailEnabled { get; set; } = true;
    public bool DiagramsEnabled { get; set; } = true;

    /// <summary>Pexels API 키 (ISecretProtector로 암호화된 값).</summary>
    public string PexelsApiKey { get; set; } = "";

    /// <summary>OpenAI API 키 (암호화된 값). AI 이미지 생성에만 사용, 쓴 만큼 과금.</summary>
    public string OpenAiApiKey { get; set; } = "";
    public bool AiImagesEnabled { get; set; }
    public string OpenAiImageModel { get; set; } = "gpt-image-2";
    public string OpenAiImageQuality { get; set; } = "low";
}

/// <summary>API 키 같은 비밀 값을 디스크에 쓸 때 암호화 (Windows: DPAPI, Mac: 키체인 예정).</summary>
public interface ISecretProtector
{
    string Protect(string plain);
    string Unprotect(string protectedValue);
}

/// <summary>암호화 없음 (테스트·개발용).</summary>
public sealed class PlainSecretProtector : ISecretProtector
{
    public string Protect(string plain) => plain;
    public string Unprotect(string protectedValue) => protectedValue;
}

/// <summary>저장된 글 한 편: 본문 + 글 폴더 + 이미지 목록.</summary>
public sealed record SavedPost(PostDraft Post, string Folder, IReadOnlyList<PostImage> Images);

public sealed class StoreState
{
    public Dictionary<string, int> Progress { get; set; } = new();
    public List<HistoryEntry> History { get; set; } = new();
}

/// <summary>
/// 이 PC에만 저장하는 데이터 (Supabase 연동 전 단계). 기본 위치: %APPDATA%\BlogStudio
/// settings.json · state.json · posts\*.md
/// </summary>
public sealed class LocalStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Root { get; }
    public string PostsDirectory => Path.Combine(Root, "posts");
    private string SettingsPath => Path.Combine(Root, "settings.json");
    private string StatePath => Path.Combine(Root, "state.json");

    private readonly ISecretProtector _protector;

    public LocalStore(string? root = null, ISecretProtector? protector = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlogStudio");
        _protector = protector ?? new PlainSecretProtector();
        Directory.CreateDirectory(PostsDirectory);
    }

    public string Conceal(string plain) => string.IsNullOrEmpty(plain) ? "" : _protector.Protect(plain);

    public string Reveal(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return "";
        try { return _protector.Unprotect(protectedValue); }
        catch (Exception) { return ""; }   // 다른 PC·계정에서 암호화된 값
    }

    public AppSettings LoadSettings() => Read<AppSettings>(SettingsPath) ?? new AppSettings();
    public void SaveSettings(AppSettings settings) => Write(SettingsPath, settings);

    public StoreState LoadState() => Read<StoreState>(StatePath) ?? new StoreState();

    public int NextIndex(string trackKey) => LoadState().Progress.GetValueOrDefault(trackKey);

    /// <summary>글 한 편의 폴더를 만든다: posts\날짜_트랙_제목\ (같은 이름이 있으면 _2, _3…).</summary>
    public string CreatePostFolder(PostDraft post)
    {
        var baseName = $"{post.Date:yyyy-MM-dd}_{post.TrackKey}_{Slug(post.Title)}";
        var name = baseName;
        for (var i = 2; Directory.Exists(Path.Combine(PostsDirectory, name)); i++) name = $"{baseName}_{i}";
        var folder = Path.Combine(PostsDirectory, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>글을 저장하고 기록을 남긴다. 커리큘럼 주제였으면 진행 위치를 한 칸 넘긴다.</summary>
    public string SavePost(PostDraft post, bool advanceProgress) =>
        SavePost(post, advanceProgress, CreatePostFolder(post), Array.Empty<PostImage>());

    public string SavePost(PostDraft post, bool advanceProgress, string folder, IReadOnlyList<PostImage> images)
    {
        var path = Path.Combine(folder, "post.md");
        var tags = string.Join(", ", post.Tags);
        File.WriteAllText(path,
            $"---\ntitle: \"{Esc(post.Title)}\"\ntags: [{tags}]\nsummary: \"{Esc(post.Summary)}\"\n" +
            $"topic: \"{Esc(post.Topic)}\"\ntrack: {post.TrackKey}\ndate: {post.Date:yyyy-MM-dd}\n---\n\n{post.BodyMarkdown}\n");
        File.WriteAllText(Path.Combine(folder, "images.json"), JsonSerializer.Serialize(images, Json));

        var state = LoadState();
        if (advanceProgress)
            state.Progress[post.TrackKey] = state.Progress.GetValueOrDefault(post.TrackKey) + 1;
        var relative = Path.GetRelativePath(PostsDirectory, path).Replace('\\', '/');
        state.History.Add(new HistoryEntry(post.Date, post.TrackKey, post.Topic, post.Title, relative));
        Write(StatePath, state);
        return path;
    }

    public SavedPost? LoadLatest()
    {
        var latest = LoadState().History.LastOrDefault();
        if (latest is null) return null;
        var path = Path.Combine(PostsDirectory, latest.FileName.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return null;
        try
        {
            var post = PostParser.Parse(File.ReadAllText(path), latest.Topic, latest.TrackKey, latest.Date);
            var folder = Path.GetDirectoryName(path)!;
            var images = Read<List<PostImage>>(Path.Combine(folder, "images.json")) ?? new List<PostImage>();
            return new SavedPost(post, folder, images);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public PostDraft? LoadLatestPost() => LoadLatest()?.Post;

    private static string Esc(string s) => s.Replace("\"", "'");

    internal static string Slug(string title)
    {
        var s = Regex.Replace(title, @"[\\/:*?""<>|#\[\]]", "");
        s = Regex.Replace(s, @"\s+", "_").Trim('_');
        return s.Length == 0 ? "post" : s.Length > 40 ? s[..40] : s;
    }

    private static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json); }
        catch (JsonException) { return null; }
    }

    private static void Write<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, path, overwrite: true);
    }
}

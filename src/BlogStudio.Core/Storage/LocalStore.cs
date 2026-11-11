using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
}

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

    public LocalStore(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlogStudio");
        Directory.CreateDirectory(PostsDirectory);
    }

    public AppSettings LoadSettings() => Read<AppSettings>(SettingsPath) ?? new AppSettings();
    public void SaveSettings(AppSettings settings) => Write(SettingsPath, settings);

    public StoreState LoadState() => Read<StoreState>(StatePath) ?? new StoreState();

    public int NextIndex(string trackKey) => LoadState().Progress.GetValueOrDefault(trackKey);

    /// <summary>글을 posts\에 저장하고 기록을 남긴다. 커리큘럼 주제였으면 진행 위치를 한 칸 넘긴다.</summary>
    public string SavePost(PostDraft post, bool advanceProgress)
    {
        var fileName = $"{post.Date:yyyy-MM-dd}_{post.TrackKey}_{Slug(post.Title)}.md";
        var path = Path.Combine(PostsDirectory, fileName);
        var tags = string.Join(", ", post.Tags);
        File.WriteAllText(path,
            $"---\ntitle: \"{post.Title.Replace("\"", "'")}\"\ntags: [{tags}]\nsummary: \"{post.Summary.Replace("\"", "'")}\"\n" +
            $"topic: \"{post.Topic.Replace("\"", "'")}\"\ntrack: {post.TrackKey}\ndate: {post.Date:yyyy-MM-dd}\n---\n\n{post.BodyMarkdown}\n");

        var state = LoadState();
        if (advanceProgress)
            state.Progress[post.TrackKey] = state.Progress.GetValueOrDefault(post.TrackKey) + 1;
        state.History.Add(new HistoryEntry(post.Date, post.TrackKey, post.Topic, post.Title, fileName));
        Write(StatePath, state);
        return path;
    }

    public PostDraft? LoadLatestPost()
    {
        var latest = LoadState().History.LastOrDefault();
        if (latest is null) return null;
        var path = Path.Combine(PostsDirectory, latest.FileName);
        if (!File.Exists(path)) return null;
        try
        {
            return PostParser.Parse(File.ReadAllText(path), latest.Topic, latest.TrackKey, latest.Date);
        }
        catch (FormatException)
        {
            return null;
        }
    }

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

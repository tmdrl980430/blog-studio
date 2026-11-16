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

    /// <summary>글 쓰는 AI: "claude"(Claude Pro · Claude Code) 또는 "chatgpt"(ChatGPT 구독 · Codex).</summary>
    public string Engine { get; set; } = "claude";

    /// <summary>ChatGPT(Codex) 모델. 비우면 codex 기본 모델.</summary>
    public string ChatGptModel { get; set; } = "";

    /// <summary>
    /// 사용량 절약: Claude Code의 기본 지시문·도구 설명을 빼고, ChatGPT는 추론을 낮게,
    /// 제목·카드 문구 같은 가벼운 일은 작은 모델로.
    /// </summary>
    public bool SaveUsage { get; set; } = true;

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

    // ── 글 품질 ──
    /// <summary>내가 직접 쓴 글 일부. 말투·어미를 따라 하게 프롬프트에 넣는다 (최대 1,500자).</summary>
    public string StyleSample { get; set; } = "";

    /// <summary>글 속 파이썬 코드 예제를 이 PC에서 실행해 오류를 찾는다 (파일·네트워크 쓰는 코드는 건너뜀).</summary>
    public bool CodeCheckEnabled { get; set; } = true;

    // ── 매일 자동 작성 ──
    /// <summary>켜면 매일 DailyTime에 앱이 열려 오늘의 기술 글을 만든다 (Windows 작업 스케줄러).</summary>
    public bool DailyAutoEnabled { get; set; }
    public string DailyTime { get; set; } = "07:30";

    // ── 자동 발행 (매일 자동 작성 다음 단계) ──
    /// <summary>
    /// 켜면 매일 자동 작성 후 편집자 검수 → 의견 반영 → 발행 기준을 통과한 글을 앱 안 브라우저로 발행까지 한다.
    /// 공식 글쓰기 API가 없어 화면을 대신 조작하므로 기본은 꺼 둔다.
    /// </summary>
    public bool AutoPublishEnabled { get; set; }
    public bool AutoPublishNaver { get; set; } = true;
    public bool AutoPublishTistory { get; set; } = true;
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
public sealed record SavedPost(PostDraft Post, string Folder, IReadOnlyList<PostImage> Images, string FileName = "");

public sealed class StoreState
{
    public Dictionary<string, int> Progress { get; set; } = new();

    /// <summary>사용자가 고친 "앞으로 쓸 주제" 순서 (트랙별). 없으면 테마 커리큘럼 순서.</summary>
    public Dictionary<string, List<string>> TopicQueues { get; set; } = new();

    /// <summary>TopicQueues를 저장할 때의 진도. 그 뒤로 쓴 만큼 목록 앞에서 빠진다.</summary>
    public Dictionary<string, int> TopicQueueBase { get; set; } = new();
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

    /// <summary>
    /// 사용자가 정한 주제 순서를 트랙에 반영: 이미 쓴 만큼(진도)은 그대로 두고 그 뒤를 사용자 목록으로 바꾼다.
    /// 진도가 한 칸 넘어가면 자연스럽게 목록의 다음 주제가 된다.
    /// </summary>
    public void ApplyTopicQueues(Themes.ThemePack theme)
    {
        foreach (var track in theme.Curriculum.Values)
        {
            if (UpcomingQueue(track.Key) is not { } upcoming) continue;
            var done = NextIndex(track.Key);
            track.Topics = track.Topics.Take(done)
                .Concat(Enumerable.Repeat("", Math.Max(0, done - track.Topics.Count)))
                .Concat(upcoming).ToList();
        }
    }

    /// <summary>사용자가 정한 "앞으로 쓸 주제" 중 아직 안 쓴 것. 정한 적 없으면 null.</summary>
    public IReadOnlyList<string>? UpcomingQueue(string trackKey)
    {
        var state = LoadState();
        if (!state.TopicQueues.TryGetValue(trackKey, out var queue)) return null;
        var used = state.Progress.GetValueOrDefault(trackKey) - state.TopicQueueBase.GetValueOrDefault(trackKey);
        return queue.Skip(Math.Max(0, used)).ToList();
    }

    /// <summary>앞으로 쓸 주제 목록 저장 (지금 진도 기준). null이면 원래 커리큘럼으로 되돌림.</summary>
    public void SaveTopicQueue(string trackKey, IReadOnlyList<string>? upcoming)
    {
        var state = LoadState();
        if (upcoming is null)
        {
            state.TopicQueues.Remove(trackKey);
            state.TopicQueueBase.Remove(trackKey);
        }
        else
        {
            state.TopicQueues[trackKey] = upcoming.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            state.TopicQueueBase[trackKey] = state.Progress.GetValueOrDefault(trackKey);
        }
        Write(StatePath, state);
    }

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

    /// <summary>post.md 형식 (front matter + 본문). 수정 요청 때 Claude에게도 이 형식으로 보낸다.</summary>
    public static string ToFileText(PostDraft post) =>
        $"---\ntitle: \"{Esc(post.Title)}\"\ntags: [{string.Join(", ", post.Tags)}]\nsummary: \"{Esc(post.Summary)}\"\n" +
        $"topic: \"{Esc(post.Topic)}\"\ntrack: {post.TrackKey}\ndate: {post.Date:yyyy-MM-dd}\n---\n\n{post.BodyMarkdown}\n";

    /// <summary>이미 저장한 글을 고친 내용으로 덮어쓴다 (진도·이미지는 그대로, 목록의 제목만 갱신).</summary>
    public void UpdatePost(string fileName, PostDraft post)
    {
        var path = Path.Combine(PostsDirectory, fileName.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) throw new FileNotFoundException("글 파일이 없어요", path);
        File.WriteAllText(path, ToFileText(post));
        var state = LoadState();
        var index = state.History.FindLastIndex(h => h.FileName == fileName);
        if (index >= 0)
        {
            state.History[index] = state.History[index] with { Title = post.Title };
            Write(StatePath, state);
        }
    }

    public string SavePost(PostDraft post, bool advanceProgress, string folder, IReadOnlyList<PostImage> images)
    {
        var path = Path.Combine(folder, "post.md");
        File.WriteAllText(path, ToFileText(post));
        File.WriteAllText(Path.Combine(folder, "images.json"), JsonSerializer.Serialize(images, Json));

        var state = LoadState();
        if (advanceProgress)
            state.Progress[post.TrackKey] = state.Progress.GetValueOrDefault(post.TrackKey) + 1;
        var relative = RelativeFileName(path);
        state.History.Add(new HistoryEntry(post.Date, post.TrackKey, post.Topic, post.Title, relative));
        Write(StatePath, state);
        return path;
    }

    public SavedPost? LoadLatest()
    {
        var latest = LoadState().History.LastOrDefault();
        return latest is null ? null : LoadPost(latest);
    }

    /// <summary>지난 글 하나 다시 열기. 파일이 지워졌거나 깨졌으면 null.</summary>
    public SavedPost? LoadPost(HistoryEntry entry)
    {
        var path = Path.Combine(PostsDirectory, entry.FileName.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return null;
        try
        {
            var post = PostParser.Parse(File.ReadAllText(path), entry.Topic, entry.TrackKey, entry.Date);
            var folder = Path.GetDirectoryName(path)!;
            var images = Read<List<PostImage>>(Path.Combine(folder, "images.json")) ?? new List<PostImage>();
            return new SavedPost(post, folder, images, entry.FileName);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>최근 글부터 (지워진 파일은 뺌).</summary>
    public IReadOnlyList<HistoryEntry> RecentPosts(int count) =>
        LoadState().History.AsEnumerable().Reverse()
            .Where(h => File.Exists(Path.Combine(PostsDirectory, h.FileName.Replace('/', Path.DirectorySeparatorChar))))
            .Take(count).ToList();

    /// <summary>"네이버에 올림"·"티스토리에 올림" 표시를 켜고 끈다.</summary>
    public HistoryEntry? SetPublished(string fileName, string blog, bool published)
    {
        var state = LoadState();
        var index = state.History.FindLastIndex(h => h.FileName == fileName);
        if (index < 0) return null;
        var entry = state.History[index];
        var set = new SortedSet<string>(entry.PublishedTo ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (published) set.Add(blog); else set.Remove(blog);
        state.History[index] = entry = entry with { PublishedTo = set.ToList() };
        Write(StatePath, state);
        return entry;
    }

    /// <summary>이 날짜에 이 트랙 글을 이미 만들었는지 (매일 자동 작성이 두 번 쓰지 않게).</summary>
    public bool HasPostOn(DateOnly date, IEnumerable<string> trackKeys)
    {
        var keys = trackKeys.ToHashSet(StringComparer.Ordinal);
        return LoadState().History.Any(h => h.Date == date && keys.Contains(h.TrackKey));
    }

    public PostDraft? LoadLatestPost() => LoadLatest()?.Post;

    public string RelativeFileName(string postPath) =>
        Path.GetRelativePath(PostsDirectory, postPath).Replace('\\', '/');

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

using System.Text;
using BlogStudio.Core;
using BlogStudio.Core.Ai;
using BlogStudio.Core.Posts;
using BlogStudio.Core.Prompting;
using BlogStudio.Core.Rendering;
using BlogStudio.Core.Scheduling;
using BlogStudio.Core.Storage;
using BlogStudio.Core.Themes;

namespace BlogStudio.Core.Tests;

internal static class Fixtures
{
    public static ThemeRepository Themes() => new(ThemeRepository.LocateSharedRoot());

    public const string SampleResponse = """
        ---
        title: "BFS로 미로 최단 거리 구하기"
        tags: [BFS, 파이썬, 코딩테스트]
        summary: "deque로 BFS를 구현하는 템플릿"
        ---
        ## 오늘 공부할 내용
        - [ ] BFS가 최단 거리를 보장하는 이유

        **BFS**는 `deque`를 씁니다.

        | 구분 | BFS |
        |---|---|
        | 자료구조 | 큐 |

        ```python
        from collections import deque
        ```
        """;
}

public class ThemeRepositoryTests
{
    [Fact]
    public void Loads_tech_theme_with_curriculum()
    {
        var theme = Fixtures.Themes().Load("tech");
        Assert.Equal("기술 공부 블로그", theme.Name);
        Assert.Contains("algorithm", theme.Curriculum.Keys);
        Assert.Equal(30, theme.Curriculum["algorithm"].Topics.Count);
        Assert.Equal("algorithm", theme.DefaultSchedule["sun"]);
    }

    [Fact]
    public void Lists_all_theme_packs()
    {
        var slugs = Fixtures.Themes().ListThemeSlugs();
        Assert.Equal(new[] { "fashion", "review", "tech" }, slugs);
    }

    [Fact]
    public void Base_prompt_has_placeholders_and_no_leading_comment()
    {
        var prompt = Fixtures.Themes().LoadBasePrompt();
        Assert.DoesNotContain("<!--", prompt[..10]);
        Assert.Contains("{author}", prompt);
    }
}

public class PostParserTests
{
    [Fact]
    public void Parses_front_matter_and_body()
    {
        var post = PostParser.Parse(Fixtures.SampleResponse, "BFS", "algorithm", new DateOnly(2026, 11, 11));
        Assert.Equal("BFS로 미로 최단 거리 구하기", post.Title);
        Assert.Equal(new[] { "BFS", "파이썬", "코딩테스트" }, post.Tags);
        Assert.StartsWith("## 오늘 공부할 내용", post.BodyMarkdown);
        Assert.Equal("#BFS #파이썬 #코딩테스트", post.TagLine);
    }

    [Fact]
    public void Unwraps_whole_markdown_fence()
    {
        var wrapped = "```markdown\n" + Fixtures.SampleResponse + "\n```";
        var post = PostParser.Parse(wrapped, "BFS", "algorithm", new DateOnly(2026, 11, 11));
        Assert.Equal("BFS로 미로 최단 거리 구하기", post.Title);
    }

    [Fact]
    public void Skips_chatter_before_front_matter()
    {
        var post = PostParser.Parse("네, 작성했습니다.\n\n" + Fixtures.SampleResponse, "BFS", "algorithm", new DateOnly(2026, 11, 11));
        Assert.Equal(3, post.Tags.Count);
    }

    [Fact]
    public void Throws_when_front_matter_missing() =>
        Assert.Throws<FormatException>(() => PostParser.Parse("그냥 본문", "x", "cs", new DateOnly(2026, 1, 1)));
}

public class PromptBuilderTests
{
    [Fact]
    public void Puts_fixed_parts_in_system_and_topic_history_in_user()
    {
        var repo = Fixtures.Themes();
        var theme = repo.Load("tech");
        var track = theme.Curriculum["cs"];
        var history = Enumerable.Range(1, 20)
            .Select(i => new HistoryEntry(new DateOnly(2026, 10, i), "cs", $"t{i}", $"글{i}", $"f{i}.md"))
            .Append(new HistoryEntry(new DateOnly(2026, 10, 25), "algorithm", "x", "다른 트랙 글", "x.md"));

        var prompt = new PromptBuilder(repo.LoadBasePrompt()).Build(theme, track, "TCP vs UDP", history, "김승기", "표 꼭 넣기");

        Assert.Contains("김승기", prompt.System);
        Assert.Contains("[글 구조]", prompt.System);
        Assert.DoesNotContain("TCP vs UDP", prompt.System);
        Assert.Contains("[오늘의 주제] TCP vs UDP", prompt.User);
        Assert.Contains("[추가 요청] 표 꼭 넣기", prompt.User);
        Assert.Contains("글20", prompt.User);
        Assert.DoesNotContain("글5 ", prompt.User + " ");      // 최근 15편만
        Assert.DoesNotContain("다른 트랙 글", prompt.User);
    }

    [Fact]
    public void Uses_auto_topic_when_curriculum_done()
    {
        var repo = Fixtures.Themes();
        var theme = repo.Load("tech");
        var prompt = new PromptBuilder(repo.LoadBasePrompt()).Build(theme, theme.Curriculum["cs"], null, [], "");
        Assert.Contains(PromptBuilder.AutoTopic, prompt.User);
        Assert.Contains("블로그 주인", prompt.System);
    }
}

public class RendererTests
{
    [Fact]
    public void Adds_inline_styles_and_keeps_code_blocks_separate()
    {
        var post = PostParser.Parse(Fixtures.SampleResponse, "BFS", "algorithm", new DateOnly(2026, 11, 11));
        var html = BlogHtmlRenderer.ToBlogHtml(post.BodyMarkdown);

        Assert.Contains("<h2 style=\"font-size:22px", html);
        Assert.Contains("☐ BFS가", html);
        Assert.Contains($"<code style=\"{BlogHtmlRenderer.InlineCode}\">deque</code>", html);
        Assert.Contains($"<pre style=\"{BlogHtmlRenderer.CodeBlock}\"><code data-block=\"true\" class=\"language-python\">", html);
        Assert.Contains("<td style=", html);
        Assert.DoesNotContain("<pre style=\"font-size:16px", html);   // <p> 규칙이 <pre>에 붙지 않음
    }

    [Fact]
    public void Clipboard_offsets_count_utf8_bytes()
    {
        const string fragment = "<p>한글 본문</p>";
        var cf = ClipboardHtml.Build(fragment);
        var bytes = Encoding.UTF8.GetBytes(cf);

        int Read(string key)
        {
            var i = cf.IndexOf(key + ":", StringComparison.Ordinal) + key.Length + 1;
            return int.Parse(cf.Substring(i, 10));
        }

        var start = Read("StartFragment");
        var end = Read("EndFragment");
        Assert.Equal(fragment, Encoding.UTF8.GetString(bytes, start, end - start));
        Assert.Equal(bytes.Length, Read("EndHTML"));
        Assert.StartsWith("<html>", Encoding.UTF8.GetString(bytes, Read("StartHTML"), 6));
    }

    [Fact]
    public void Blog_targets_build_write_urls()
    {
        Assert.Equal("https://blog.naver.com/tmdrl?Redirect=Write", BlogTargets.NaverWriteUrl(" tmdrl "));
        Assert.Equal("https://seungki.tistory.com/manage/newpost", BlogTargets.TistoryWriteUrl("seungki"));
        Assert.Null(BlogTargets.NaverWriteUrl(""));
    }
}

public class TopicPlannerTests
{
    private static ThemePack Theme() => Fixtures.Themes().Load("tech");

    [Fact]
    public void Wednesday_is_csharp_and_sunday_is_algorithm()
    {
        var planner = new TopicPlanner(Theme());
        Assert.Equal("csharp_windows", planner.TrackFor(new DateOnly(2026, 11, 11))!.Key);   // 수요일
        Assert.Equal("algorithm", planner.TrackFor(new DateOnly(2026, 11, 15))!.Key);        // 일요일
    }

    [Fact]
    public void Plan_uses_progress_index()
    {
        var theme = Theme();
        var plan = new TopicPlanner(theme).Plan(new DateOnly(2026, 11, 12), _ => 2)!;   // 목요일 algorithm
        Assert.Equal(theme.Curriculum["algorithm"].Topics[2], plan.Topic);
    }

    [Fact]
    public void Upcoming_advances_each_track_independently()
    {
        var theme = Theme();
        // 월(11/9)부터: 월 알고 → 화 CS → 수 C# → 목 알고
        var list = new TopicPlanner(theme).Upcoming(new DateOnly(2026, 11, 9), _ => 0, 3);
        Assert.Equal(new[] { "cs", "csharp_windows", "algorithm" }, list.Select(p => p.Track.Key));
        Assert.Equal(theme.Curriculum["algorithm"].Topics[1], list[2].Topic);   // 오늘 월요일 글 다음 칸
    }

    [Fact]
    public void Rest_day_has_no_plan()
    {
        var schedule = new Dictionary<string, string> { ["wed"] = "rest" };
        Assert.Null(new TopicPlanner(Theme(), schedule).Plan(new DateOnly(2026, 11, 11), _ => 0));
    }
}

public class LocalStoreTests
{
    [Fact]
    public void Saves_post_advances_progress_and_reloads_latest()
    {
        var root = Directory.CreateTempSubdirectory("bs-test-").FullName;
        var store = new LocalStore(root);
        var post = PostParser.Parse(Fixtures.SampleResponse, "BFS", "algorithm", new DateOnly(2026, 11, 11));

        store.SavePost(post, advanceProgress: true);
        store.SavePost(post with { Date = new DateOnly(2026, 11, 12) }, advanceProgress: false);

        Assert.Equal(1, store.NextIndex("algorithm"));
        Assert.Equal(2, store.LoadState().History.Count);
        var latest = store.LoadLatestPost()!;
        Assert.Equal(post.Title, latest.Title);
        Assert.Equal(new DateOnly(2026, 11, 12), latest.Date);
    }

    [Fact]
    public void Settings_round_trip_keeps_korean()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-test-").FullName);
        store.SaveSettings(new AppSettings { Author = "김승기", NaverBlogId = "abc" });
        Assert.Equal("김승기", store.LoadSettings().Author);
        Assert.Contains("김승기", File.ReadAllText(Path.Combine(store.Root, "settings.json")));
    }

    [Fact]
    public void Slug_removes_unsafe_characters() =>
        Assert.Equal("C_값_형식_vs_참조", LocalStore.Slug("C# 값 형식: vs 참조?"));
}

public class ClaudeCodeProviderTests
{
    private sealed class FakeRunner : IProcessRunner
    {
        public ProcessSpec? Last;
        public ProcessResult Result = new(0, Fixtures.SampleResponse, "");

        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
        {
            Last = spec;
            return Task.FromResult(Result);
        }
    }

    [Fact]
    public async Task Runs_claude_headless_with_prompt_on_stdin_and_strips_api_key()
    {
        var runner = new FakeRunner();
        var provider = new ClaudeCodeProvider(runner, () => "/usr/bin/claude");

        var result = await provider.GenerateAsync(new AiRequest("시스템", "사용자", "main"));

        Assert.Equal("/usr/bin/claude", runner.Last!.FileName);
        Assert.Equal(new[] { "-p", "--output-format", "text", "--model", "sonnet" }, runner.Last.Arguments);
        Assert.Contains("시스템", runner.Last.StandardInput);
        Assert.Contains("사용자", runner.Last.StandardInput);
        Assert.Contains("ANTHROPIC_API_KEY", runner.Last.RemoveEnvironmentVariables);
        Assert.False(Directory.Exists(runner.Last.WorkingDirectory));   // 임시 폴더 정리됨
        Assert.Equal("sonnet", result.Model);
    }

    [Fact]
    public async Task Light_tier_uses_haiku_unless_user_picked_model()
    {
        var runner = new FakeRunner();
        var provider = new ClaudeCodeProvider(runner, () => "claude");
        await provider.GenerateAsync(new AiRequest("s", "u", "light"));
        Assert.Equal("haiku", runner.Last!.Arguments[^1]);
        await provider.GenerateAsync(new AiRequest("s", "u", "light", "opus"));
        Assert.Equal("opus", runner.Last!.Arguments[^1]);
    }

    [Fact]
    public async Task Failure_and_missing_cli_raise_friendly_errors()
    {
        var runner = new FakeRunner { Result = new ProcessResult(1, "", "Not logged in") };
        var failing = new ClaudeCodeProvider(runner, () => "claude");
        var ex = await Assert.ThrowsAsync<AiProviderException>(() => failing.GenerateAsync(new AiRequest("s", "u")));
        Assert.Contains("Not logged in", ex.Message);

        var missing = new ClaudeCodeProvider(runner, () => null);
        await Assert.ThrowsAsync<AiProviderException>(() => missing.GenerateAsync(new AiRequest("s", "u")));
        Assert.Null(await missing.CheckAsync());
    }

    [Fact]
    public void Locator_checks_path_then_local_bin()
    {
        var sep = Path.PathSeparator;
        var found = ClaudeCodeLocator.Find($"/a{sep}/b", "/home/me", "/appdata", windows: false,
            exists: p => p == Path.Combine("/home/me", ".local", "bin", "claude"));
        Assert.Equal(Path.Combine("/home/me", ".local", "bin", "claude"), found);

        var onPath = ClaudeCodeLocator.Find($"/a{sep}/b", "/home/me", "/appdata", windows: true,
            exists: p => p == Path.Combine("/b", "claude.cmd"));
        Assert.Equal(Path.Combine("/b", "claude.cmd"), onPath);
    }
}

using BlogStudio.Core.Ai;
using BlogStudio.Core.Storage;
using BlogStudio.Presentation;

namespace BlogStudio.Core.Tests;

public class MainViewModelTests
{
    private sealed class FakeProvider : IAiProvider
    {
        public AiRequest? Last;
        public string Name => "Fake";
        public Task<string?> CheckAsync(CancellationToken ct = default) => Task.FromResult<string?>("1.0");

        public Task<AiResult> GenerateAsync(AiRequest request, CancellationToken ct = default)
        {
            Last = request;
            return Task.FromResult(new AiResult(Fixtures.SampleResponse, Name, "sonnet"));
        }
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Html, Text;
        public void SetHtml(string html, string plainText) { Html = html; Text = plainText; }
        public void SetText(string text) => Text = text;
    }

    private sealed class FakeAutoFill : IAutoFillLauncher
    {
        public AutoFillPayload? Last;
        public void Open(AutoFillPayload payload) => Last = payload;
    }

    [Fact]
    public async Task Auto_fill_opens_write_page_with_post_and_images_in_order()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-af-").FullName);
        store.SaveSettings(new AppSettings { TistoryName = "seunggi" });
        var autoFill = new FakeAutoFill();
        var vm = new MainViewModel(Fixtures.Themes(), store, new FakeProvider(), new FakeClipboard(), new FakeBrowser(),
            () => new DateOnly(2026, 11, 11), autoFill: autoFill);
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.False(vm.AutoFillTistoryCommand.CanExecute(null));

        await vm.GenerateCommand.ExecuteAsync(null);
        vm.AutoFillTistoryCommand.Execute(null);

        var p = autoFill.Last!;
        Assert.Equal(BlogPlatform.Tistory, p.Platform);
        Assert.Equal("https://seunggi.tistory.com/manage/newpost", p.WriteUrl);
        Assert.Equal("BFS로 미로 최단 거리 구하기", p.Title);
        Assert.Contains("[이미지 1:", p.BodyHtml);
        Assert.Equal(vm.Images.Select(i => i.FullPath), p.ImagePaths);

        autoFill.Last = null;
        vm.AutoFillNaverCommand.Execute(null);                          // 네이버 ID 없음 → 안내만
        Assert.Null(autoFill.Last);
        Assert.Contains("네이버 블로그 ID", vm.StatusText);
    }

    private sealed class FakePicker : IFilePicker
    {
        public List<string> Next = new();
        public IReadOnlyList<string> PickImages() => Next;
    }

    [Fact]
    public async Task Life_category_writes_memo_review_with_my_photos_without_advancing_ideas()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-life-").FullName);
        var provider = new FakeProvider();
        var picker = new FakePicker();
        var vm = new MainViewModel(Fixtures.Themes(), store, provider, new FakeClipboard(), new FakeBrowser(),
            () => new DateOnly(2026, 11, 11), files: picker);
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "tech", "food", "travel", "fashion", "product" }, vm.Categories.Select(c => c.Key));
        Assert.False(vm.IsMemoCategory);

        vm.SelectedCategory = vm.Categories.Single(c => c.Key == "food");
        Assert.True(vm.IsMemoCategory);
        var idea = vm.TopicTitle;
        Assert.Contains("가게 이름", vm.MemoHint);

        var photo = Path.Combine(Directory.CreateTempSubdirectory("bs-life-p-").FullName, "ramen.jpg");
        File.WriteAllBytes(photo, new byte[] { 1, 2, 3 });
        picker.Next = new() { photo, photo };
        vm.AddPhotosCommand.Execute(null);
        Assert.Single(vm.MemoPhotos);                                           // 같은 파일은 한 번만

        vm.MemoName = "연남동 라멘집";
        vm.MemoText = "돈코츠 맛있음";
        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Contains("[내 메모", provider.Last!.User);
        Assert.Contains("[사진 1: 설명]", provider.Last.User);
        Assert.Contains(vm.Images, i => i.FullPath.EndsWith("_my.jpg"));          // 자리 표시가 없어도 내 사진은 들어감
        Assert.Equal(0, store.NextIndex("food"));                               // 후기는 아이디어 순서를 넘기지 않음
        Assert.Equal(idea, vm.TopicTitle);
        Assert.Equal("", vm.MemoText);
        Assert.Empty(vm.MemoPhotos);
        Assert.Equal("food", store.LoadState().History.Last().TrackKey);

        await vm.GenerateCommand.ExecuteAsync(null);                            // 메모 없이 → 정보 글
        Assert.Contains("[오늘의 주제] " + idea, provider.Last.User);
        Assert.Equal(1, store.NextIndex("food"));

        vm.SelectedCategory = vm.Categories[0];
        Assert.False(vm.IsMemoCategory);
        Assert.Contains("·", vm.TrackName);
    }

    [Fact]
    public async Task History_reopens_posts_and_remembers_published_marks()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-hist-").FullName);
        var vm = new MainViewModel(Fixtures.Themes(), store, new FakeProvider(), new FakeClipboard(), new FakeBrowser(),
            () => new DateOnly(2026, 11, 11));
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.Empty(vm.RecentPosts);

        await vm.GenerateCommand.ExecuteAsync(null);
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.RecentPosts.Count);
        Assert.Contains("아직 안 올림", vm.RecentPosts[0].Meta);

        vm.IsPublishedTistory = true;                                         // 지금 보고 있는 글(최신)에 표시
        Assert.Contains("티스토리✓", vm.RecentPosts[0].Meta);
        Assert.DoesNotContain("티스토리✓", vm.RecentPosts[1].Meta);

        vm.OpenPostCommand.Execute(vm.RecentPosts[1]);                        // 예전 글 열기 → 표시 없음
        Assert.False(vm.IsPublishedTistory);
        vm.IsPublishedNaver = true;
        Assert.Contains("네이버✓", vm.RecentPosts[1].Meta);

        vm.OpenPostCommand.Execute(vm.RecentPosts[0]);
        Assert.True(vm.IsPublishedTistory);
        Assert.False(vm.IsPublishedNaver);
        Assert.True(store.LoadState().History[0].IsPublishedTo("naver"));
    }

    [Fact]
    public async Task Auto_generate_writes_todays_tech_post_once()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-auto-").FullName);
        var provider = new FakeProvider();
        var vm = new MainViewModel(Fixtures.Themes(), store, provider, new FakeClipboard(), new FakeBrowser(),
            () => new DateOnly(2026, 11, 11));
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedCategory = vm.Categories.Single(c => c.Key == "food");      // 일상 카테고리를 보고 있어도

        Assert.True(await vm.AutoGenerateAsync());
        Assert.Equal(MainViewModel.TechCategory, vm.SelectedCategory!.Key);
        Assert.Single(store.LoadState().History);

        provider.Last = null;
        Assert.False(await vm.AutoGenerateAsync());                          // 오늘 글이 있으면 건너뜀
        Assert.Null(provider.Last);
        Assert.Contains("이미 있어요", vm.StatusText);
    }

    private sealed class FakeBrowser : IBrowserLauncher
    {
        public string? Url;
        public void Open(string url) => Url = url;
    }

    [Fact]
    public async Task Generate_saves_post_advances_topic_and_copies_for_naver()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-vm-").FullName);
        store.SaveSettings(new AppSettings { Author = "김승기", NaverBlogId = "myblog" });
        var provider = new FakeProvider();
        var clipboard = new FakeClipboard();
        var browser = new FakeBrowser();
        var vm = new MainViewModel(Fixtures.Themes(), store, provider, clipboard, browser,
            () => new DateOnly(2026, 11, 11));   // 수요일 → C# 트랙

        await vm.InitializeCommand.ExecuteAsync(null);
        var firstTopic = vm.TopicTitle;
        Assert.True(vm.IsProviderReady);
        Assert.False(vm.CopyForNaverCommand.CanExecute(null));

        vm.ExtraRequest = "예제 많이";
        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.Contains("김승기", provider.Last!.System);
        Assert.Contains("[추가 요청] 예제 많이", provider.Last.User);
        Assert.Equal("BFS로 미로 최단 거리 구하기", vm.PostTitle);
        Assert.Contains("<h2 style=", vm.PreviewHtml);
        Assert.Equal(1, store.NextIndex("csharp_windows"));
        Assert.NotEqual(firstTopic, vm.TopicTitle);
        Assert.Equal("", vm.ExtraRequest);

        vm.CopyForNaverCommand.Execute(null);
        Assert.Contains("<h2 style=", clipboard.Html);
        Assert.Equal("https://blog.naver.com/myblog?Redirect=Write", browser.Url);

        vm.CopyForTistoryCommand.Execute(null);                        // 티스토리: HTML 소스를 텍스트로
        Assert.Contains("<h2 style=", clipboard.Text);
        Assert.Contains("[이미지 1:", clipboard.Text);                  // 썸네일 자리 표시
        Assert.Contains(vm.Images, i => i.KindLabel == "썸네일" && File.Exists(i.FullPath));

        vm.ShowMarkdownCommand.Execute(null);
        Assert.Contains("## 오늘 공부할 내용", vm.PreviewHtml);
        Assert.True(vm.IsMarkdownMode);
    }

    [Fact]
    public async Task Blog_view_is_disabled_without_post_and_always_redraws()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-vm2-").FullName);
        var browser = new FakeBrowser();
        var vm = new MainViewModel(Fixtures.Themes(), store, new FakeProvider(), new FakeClipboard(), browser,
            () => new DateOnly(2026, 11, 11));
        var redraws = 0;
        vm.PreviewRefreshRequested += (_, _) => redraws++;

        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.False(vm.ShowRenderedCommand.CanExecute(null));      // 글이 없으면 비활성
        Assert.False(vm.OpenPreviewInBrowserCommand.CanExecute(null));

        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.True(vm.ShowRenderedCommand.CanExecute(null));

        vm.ShowRenderedCommand.Execute(null);                       // 이미 블로그 보기여도 다시 그림
        vm.ShowRenderedCommand.Execute(null);
        Assert.Equal(2, redraws);
        Assert.True(vm.IsRenderedMode);
        Assert.Contains("<h2 style=", vm.PreviewHtml);

        vm.OpenPreviewInBrowserCommand.Execute(null);
        Assert.StartsWith("file:", browser.Url);
        Assert.Contains("<h2 style=", File.ReadAllText(Path.Combine(store.Root, "preview.html")));
    }
}

public class ClaudeSetupFlowTests
{
    private sealed class SwitchableProvider : IAiProvider
    {
        public bool Installed;
        public string Name => "Fake";
        public Task<string?> CheckAsync(CancellationToken ct = default) => Task.FromResult(Installed ? "2.1.0" : null);
        public Task<AiResult> GenerateAsync(AiRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeSetup(SwitchableProvider provider) : IClaudeCodeSetup
    {
        public bool LoginOpened;
        public Task<bool> InstallAsync(CancellationToken ct = default) { provider.Installed = true; return Task.FromResult(true); }
        public void OpenLoginTerminal() => LoginOpened = true;
    }

    private sealed class Noop : IClipboardService, IBrowserLauncher
    {
        public void SetHtml(string html, string plainText) { }
        public void SetText(string text) { }
        public void Open(string url) { }
    }

    [Fact]
    public async Task Missing_claude_shows_setup_then_install_opens_login()
    {
        var provider = new SwitchableProvider();
        var setup = new FakeSetup(provider);
        var noop = new Noop();
        var vm = new MainViewModel(Fixtures.Themes(), new LocalStore(Directory.CreateTempSubdirectory("bs-setup-").FullName),
            provider, noop, noop, () => new DateOnly(2026, 11, 11), setup);

        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.True(vm.ShowClaudeSetup);
        Assert.True(vm.InstallClaudeCodeCommand.CanExecute(null));

        await vm.InstallClaudeCodeCommand.ExecuteAsync(null);
        Assert.True(vm.IsProviderReady);
        Assert.False(vm.ShowClaudeSetup);
        Assert.True(setup.LoginOpened);
        Assert.False(vm.InstallClaudeCodeCommand.CanExecute(null));
    }
}


public class UpdateFlowTests
{
    private sealed class FakeUpdates : IUpdateService
    {
        public bool Applied;
        public bool IsInstalled { get; init; } = true;
        public AppUpdate? Next { get; init; } = new("1.0.7");
        public Task<AppUpdate?> CheckAsync(CancellationToken ct = default) => Task.FromResult(Next);
        public Task DownloadAsync(Action<int> progress, CancellationToken ct = default) { progress(100); return Task.CompletedTask; }
        public void ApplyAndRestart() => Applied = true;
    }

    private sealed class Ready : IAiProvider, IClipboardService, IBrowserLauncher
    {
        public string Name => "Fake";
        public Task<string?> CheckAsync(CancellationToken ct = default) => Task.FromResult<string?>("1.0");
        public Task<AiResult> GenerateAsync(AiRequest r, CancellationToken ct = default) => throw new NotSupportedException();
        public void SetHtml(string html, string plainText) { }
        public void SetText(string text) { }
        public void Open(string url) { }
    }

    private static MainViewModel Vm(IUpdateService updates)
    {
        var r = new Ready();
        return new MainViewModel(Fixtures.Themes(), new LocalStore(Directory.CreateTempSubdirectory("bs-up-").FullName),
            r, r, r, () => new DateOnly(2026, 11, 11), updates: updates);
    }

    [Fact]
    public async Task Shows_banner_and_applies_update()
    {
        var updates = new FakeUpdates();
        var vm = Vm(updates);
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.True(vm.IsUpdateAvailable);
        Assert.Contains("1.0.7", vm.UpdateBannerText);
        await vm.ApplyUpdateCommand.ExecuteAsync(null);
        Assert.True(updates.Applied);
    }

    [Fact]
    public async Task No_banner_when_not_installed_or_up_to_date()
    {
        var zipRun = Vm(new FakeUpdates { IsInstalled = false });
        await zipRun.InitializeCommand.ExecuteAsync(null);
        Assert.False(zipRun.IsUpdateAvailable);

        var latest = Vm(new FakeUpdates { Next = null });
        await latest.InitializeCommand.ExecuteAsync(null);
        Assert.False(latest.ApplyUpdateCommand.CanExecute(null));
    }
}

public class SettingsViewModelTests
{
    private sealed class FakeScheduler : IDailyScheduler
    {
        public List<(bool, TimeOnly)> Calls = new();
        public bool Fail;
        public void Apply(bool enabled, TimeOnly time)
        {
            if (Fail) throw new InvalidOperationException("액세스가 거부되었습니다");
            Calls.Add((enabled, time));
        }
    }

    [Fact]
    public void Daily_schedule_is_registered_only_when_changed_and_errors_keep_dialog_open()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-sched-").FullName);
        var scheduler = new FakeScheduler();
        var vm = new SettingsViewModel(store, scheduler);
        var saved = 0;
        vm.Saved += (_, _) => saved++;

        vm.DailyTime = "7시";
        vm.SaveCommand.Execute(null);
        Assert.Equal(0, saved);
        Assert.Contains("24시간", vm.ErrorText);

        vm.DailyAutoEnabled = true;
        vm.DailyTime = "6:05";
        vm.SaveCommand.Execute(null);
        Assert.Equal(1, saved);
        Assert.Equal((true, new TimeOnly(6, 5)), scheduler.Calls.Single());
        Assert.Equal("06:05", store.LoadSettings().DailyTime);

        var again = new SettingsViewModel(store, scheduler);
        again.SaveCommand.Execute(null);                                       // 바뀐 게 없으면 다시 등록 안 함
        Assert.Single(scheduler.Calls);

        scheduler.Fail = true;
        again.DailyAutoEnabled = false;
        again.SaveCommand.Execute(null);
        Assert.Contains("등록하지 못했어요", again.ErrorText);
        Assert.True(store.LoadSettings().DailyAutoEnabled);
    }
}

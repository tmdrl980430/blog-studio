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
        Assert.StartsWith("<h2 style=", clipboard.Text);

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

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

        vm.ShowMarkdownCommand.Execute(null);
        Assert.Contains("## 오늘 공부할 내용", vm.PreviewHtml);
    }
}

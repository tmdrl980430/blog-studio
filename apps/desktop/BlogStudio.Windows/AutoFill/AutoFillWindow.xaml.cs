using System.Windows;
using BlogStudio.Presentation;
using Microsoft.Web.WebView2.Core;

namespace BlogStudio.Windows.AutoFill;

public partial class AutoFillWindow : Window
{
    private readonly AutoFillPayload _post;
    private readonly IClipboardService _clipboard;
    private AutoFillRunner? _runner;
    private CancellationTokenSource? _cts;
    private string _diagnostics = "";
    private static readonly System.Net.Http.HttpClient Http = new();
    private readonly TaskCompletionSource<AutoPublishResult>? _published;

    /// <param name="published">자동 발행일 때: 발행이 확인되면 성공, 실패면 멈춘 단계·이유, 창 닫힘이면 이유 모름.</param>
    public AutoFillWindow(AutoFillPayload post, IClipboardService clipboard, TaskCompletionSource<AutoPublishResult>? published = null)
    {
        InitializeComponent();
        _post = post;
        _clipboard = clipboard;
        _published = published;
        var blog = post.Platform == BlogPlatform.Naver ? "네이버 블로그" : "티스토리";
        Title = $"자동 채우기 · {blog}";
        Heading.Text = $"{blog}에 채우기";
        PostTitle.Text = $"{post.Title}\n이미지 {post.ImagePaths.Count}장 · 태그 {post.Tags.Count}개";
        ImagesButton.IsEnabled = post.ImagePaths.Count > 0;

        // 메인 미리보기와 같은 폴더 → 한 번 로그인하면 다음에도 유지
        Web.CreationProperties = new Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
        {
            UserDataFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlogStudio", "WebView2"),
        };
        Loaded += async (_, _) => await StartAsync();
        Closing += (_, _) => { _cts?.Cancel(); _published?.TrySetResult(new AutoPublishResult(false)); };
    }

    private async Task StartAsync()
    {
        try
        {
            await Web.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            Write("앱 안 브라우저를 시작하지 못했어요: " + ex.Message);
            RunAllButton.IsEnabled = false;
            _published?.TrySetResult(new AutoPublishResult(false, AutoPublishStage.Fill, "앱 안 브라우저를 시작하지 못함"));
            return;
        }
        var data = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlogStudio");
        _diagnostics = System.IO.Path.Combine(data, "autofill-logs");
        var script = await BlogStudio.Core.AutoFill.AutoFillScriptSource.LoadAsync(
            System.IO.Path.Combine(BlogStudio.Core.Themes.ThemeRepository.LocateSharedRoot(), "autofill", "autofill.js"),
            System.IO.Path.Combine(data, "autofill", "autofill.js"), Http);
        Write($"채우기 규칙 v{script.Version} ({script.Source})");
        _runner = new AutoFillRunner(Web.CoreWebView2, _clipboard, _post, Write, () => Web.Focus(), script.Text, _diagnostics);
        // 새 창(팝업) 요청은 같은 화면에서 연다 (카카오·네이버 로그인 창 등)
        Web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; Web.CoreWebView2.Navigate(e.Uri); };
        Web.CoreWebView2.Navigate(_post.WriteUrl);
        Write("글쓰기 화면을 여는 중…");
        if (_published is not null) await AutoPublishAsync();
    }

    private async Task AutoPublishAsync()
    {
        Write("자동 발행: 채우기 → 발행");
        var ok = false;
        await RunAsync(async (r, ct) => ok = await r.RunAndPublishAsync(ct));
        _published!.TrySetResult(ok ? new AutoPublishResult(true) : _runner?.LastFailure ?? new AutoPublishResult(false));
        if (!ok)
        {
            Write("자동 발행을 멈췄어요 — 이 창에서 확인 후 직접 발행할 수 있어요");
            return;
        }
        await Task.Delay(3000);
        Close();
    }

    private void Write(string line)
    {
        Log.Items.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        Log.ScrollIntoView(Log.Items[^1]);
    }

    private async Task RunAsync(Func<AutoFillRunner, CancellationToken, Task> step)
    {
        if (_runner is null) return;
        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            await step(_runner, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            Write("멈췄어요");
        }
        catch (Exception ex)
        {
            Write("문제가 생겼어요: " + ex.Message + " — 남은 부분은 직접 붙여넣어 주세요");
            if (_runner is not null && await _runner.SaveDiagnosticsAsync("error") is not null)
                Write("진단 기록을 남겼어요 — '진단 기록 열기'로 볼 수 있어요");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RunAllButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
    }

    private async void RunAll_Click(object sender, RoutedEventArgs e) => await RunAsync((r, ct) => r.RunAllAsync(ct));
    private async void Title_Click(object sender, RoutedEventArgs e) => await RunAsync((r, _) => r.FillTitleAsync());
    private async void Body_Click(object sender, RoutedEventArgs e) => await RunAsync((r, _) => r.FillBodyAsync());
    private async void Images_Click(object sender, RoutedEventArgs e) => await RunAsync((r, ct) => r.FillImagesAsync(ct));
    private async void Tags_Click(object sender, RoutedEventArgs e) => await RunAsync((r, _) => r.FillTagsAsync());
    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        var target = _runner?.LastDiagnostics ?? _diagnostics;
        System.IO.Directory.CreateDirectory(target);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        Web.CoreWebView2?.Navigate(_post.WriteUrl);
        Write("글쓰기 화면을 다시 열었어요");
    }
}

/// <summary>MainViewModel이 부르는 자동 채우기 창 열기.</summary>
public sealed class WpfAutoFillLauncher : IAutoFillLauncher
{
    private readonly IClipboardService _clipboard;

    public WpfAutoFillLauncher(IClipboardService clipboard) => _clipboard = clipboard;

    public void Open(AutoFillPayload payload) =>
        new AutoFillWindow(payload, _clipboard) { Owner = Application.Current.MainWindow }.Show();

    public async Task<bool> PublishAsync(AutoFillPayload payload) => (await PublishWithReasonAsync(payload)).Ok;

    public Task<AutoPublishResult> PublishWithReasonAsync(AutoFillPayload payload)
    {
        var done = new TaskCompletionSource<AutoPublishResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // 매일 자동 작성은 메인 창을 최소화한 채 돌므로 소유 창으로 두지 않는다 (같이 숨겨지지 않게)
        new AutoFillWindow(payload, _clipboard, done) { ShowActivated = false }.Show();
        return done.Task;
    }
}

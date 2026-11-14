using System.Windows;
using BlogStudio.Core.Ai;
using BlogStudio.Core.Storage;
using BlogStudio.Core.Themes;
using BlogStudio.Presentation;
using BlogStudio.Windows.Services;
using Velopack;

namespace BlogStudio.Windows;

public partial class App : Application
{
    /// <summary>
    /// WPF가 자동으로 만드는 Main 대신 사용 (csproj의 StartupObject).
    /// 설치·업데이트·제거 때 Velopack이 특별한 인자로 앱을 실행하므로 가장 먼저 VelopackApp을 돌린다.
    /// </summary>
    private static Mutex? _single;

    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        // 매일 자동 작성으로 실행됐는데 이미 앱이 열려 있으면 조용히 끝낸다 (두 번 쓰지 않게)
        _single = new Mutex(true, @"Local\BlogStudio.Main", out var first);
        if (!first && args.Contains(Services.WindowsDailyScheduler.AutoArgument)) return;

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Blog Studio 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            var themes = new ThemeRepository(ThemeRepository.LocateSharedRoot());
            var store = new LocalStore(protector: new DpapiSecretProtector());
            var provider = new ClaudeCodeProvider(new SystemProcessRunner());
            var clipboard = new WpfClipboardService();
            var viewModel = new MainViewModel(themes, store, provider, clipboard, new ShellBrowserLauncher(),
                setup: new WindowsClaudeCodeSetup(), updates: new VelopackUpdateService(),
                imageServices: new BlogStudio.Core.Images.ImageServices(store),
                autoFill: new AutoFill.WpfAutoFillLauncher(clipboard), files: new WpfFilePicker());

            var auto = e.Args.Contains(Services.WindowsDailyScheduler.AutoArgument);
            var window = new MainWindow(viewModel, store, auto);
            if (auto) window.WindowState = WindowState.Minimized;   // 작업 표시줄에서 확인
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("시작하지 못했어요: " + ex.Message, "Blog Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}

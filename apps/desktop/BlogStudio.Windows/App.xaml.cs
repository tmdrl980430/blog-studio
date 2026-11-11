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
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();
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
            var store = new LocalStore();
            var provider = new ClaudeCodeProvider(new SystemProcessRunner());
            var viewModel = new MainViewModel(themes, store, provider, new WpfClipboardService(), new ShellBrowserLauncher(),
                setup: new WindowsClaudeCodeSetup(), updates: new VelopackUpdateService());

            var window = new MainWindow(viewModel, store);
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

using System.Windows;
using BlogStudio.Core.Ai;
using BlogStudio.Core.Storage;
using BlogStudio.Core.Themes;
using BlogStudio.Presentation;
using BlogStudio.Windows.Services;

namespace BlogStudio.Windows;

public partial class App : Application
{
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
            var viewModel = new MainViewModel(themes, store, provider, new WpfClipboardService(), new ShellBrowserLauncher());

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

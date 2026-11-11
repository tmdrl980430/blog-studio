using System.ComponentModel;
using System.Reflection;
using System.Windows;
using BlogStudio.Core.Storage;
using BlogStudio.Presentation;

namespace BlogStudio.Windows;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly LocalStore _store;
    private bool _webReady;

    public MainWindow(MainViewModel vm, LocalStore store)
    {
        InitializeComponent();
        _vm = vm;
        _store = store;
        DataContext = vm;
        VersionText.Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");

        // WebView2 캐시는 설치 폴더가 아니라 %LOCALAPPDATA%\BlogStudio 아래에 둔다.
        Preview.CreationProperties = new Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
        {
            UserDataFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlogStudio", "WebView2"),
        };

        vm.PropertyChanged += OnViewModelChanged;
        vm.PreviewRefreshRequested += (_, _) => ShowPreview();
        Loaded += async (_, _) =>
        {
            try
            {
                await Preview.EnsureCoreWebView2Async();
                _webReady = true;
                ShowPreview();
            }
            catch (Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
            {
                // Windows 10/11 대부분에 이미 있지만, 없으면 Microsoft 공식 설치 프로그램으로 안내
                var answer = MessageBox.Show(
                    "미리보기에 필요한 Microsoft Edge WebView2 런타임이 없어요.\n지금 설치 페이지를 열까요? 설치 후 앱을 다시 시작해 주세요.",
                    "Blog Studio", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (answer == MessageBoxResult.Yes)
                    new Services.ShellBrowserLauncher().Open("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
                ShowFallback("WebView2 런타임이 없어요. 설치 후 앱을 다시 시작하거나, 아래 버튼으로 브라우저에서 보세요.");
            }
            catch (Exception ex)
            {
                ShowFallback("WebView2를 시작하지 못했어요: " + ex.Message);
            }
            await _vm.InitializeCommand.ExecuteAsync(null);
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PreviewHtml))
            ShowPreview();
    }

    private void ShowPreview()
    {
        if (!_webReady) return;
        try
        {
            // 파일로 써서 열어야 글 폴더의 이미지(file:///)가 미리보기에 보인다.
            var dir = System.IO.Path.Combine(_store.Root, "preview");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "current.html");
            System.IO.File.WriteAllText(path, _vm.PreviewHtml, new System.Text.UTF8Encoding(false));
            Preview.CoreWebView2.Navigate(new Uri(path).AbsoluteUri + "?v=" + DateTime.Now.Ticks);
        }
        catch (Exception ex)
        {
            ShowFallback("미리보기를 그리지 못했어요: " + ex.Message);
        }
    }

    private void ShowFallback(string message)
    {
        _webReady = false;
        _vm.PreviewUnavailableMessage = message;
        _vm.StatusText = message;
        Preview.Visibility = Visibility.Collapsed;
        PreviewFallback.Visibility = Visibility.Visible;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(new SettingsViewModel(_store)) { Owner = this };
        if (window.ShowDialog() == true)
            _vm.ReloadSettings();
    }
}

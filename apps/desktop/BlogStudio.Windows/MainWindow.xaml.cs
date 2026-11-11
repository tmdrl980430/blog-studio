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
        Loaded += async (_, _) =>
        {
            try
            {
                await Preview.EnsureCoreWebView2Async();
                _webReady = true;
                Preview.NavigateToString(_vm.PreviewHtml);
            }
            catch (Exception ex)
            {
                _vm.StatusText = "미리보기(WebView2)를 시작하지 못했어요: " + ex.Message;
            }
            await _vm.InitializeCommand.ExecuteAsync(null);
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PreviewHtml) && _webReady)
            Preview.NavigateToString(_vm.PreviewHtml);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(new SettingsViewModel(_store)) { Owner = this };
        if (window.ShowDialog() == true)
            _vm.ReloadSettings();
    }
}

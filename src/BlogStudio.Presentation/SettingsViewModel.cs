using BlogStudio.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogStudio.Presentation;

/// <summary>설정 창: 블로그 주소, 작성자 이름, Claude 모델.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly LocalStore _store;
    private readonly AppSettings _settings;

    public SettingsViewModel(LocalStore store)
    {
        _store = store;
        _settings = store.LoadSettings();
        _author = _settings.Author;
        _naverBlogId = _settings.NaverBlogId;
        _tistoryName = _settings.TistoryName;
        _claudeModel = string.IsNullOrWhiteSpace(_settings.ClaudeModel) ? "sonnet" : _settings.ClaudeModel;
    }

    public IReadOnlyList<string> ModelOptions { get; } = new[] { "sonnet", "opus", "haiku" };

    public string DataFolder => _store.Root;

    [ObservableProperty] private string _author;
    [ObservableProperty] private string _naverBlogId;
    [ObservableProperty] private string _tistoryName;
    [ObservableProperty] private string _claudeModel;

    /// <summary>저장 후 창을 닫으라는 신호.</summary>
    public event EventHandler? Saved;

    [RelayCommand]
    private void Save()
    {
        _settings.Author = Author.Trim();
        _settings.NaverBlogId = NaverBlogId.Trim();
        _settings.TistoryName = TistoryName.Trim();
        _settings.ClaudeModel = ClaudeModel;
        _store.SaveSettings(_settings);
        Saved?.Invoke(this, EventArgs.Empty);
    }
}

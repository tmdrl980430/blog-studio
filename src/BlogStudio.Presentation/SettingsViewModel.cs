using BlogStudio.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogStudio.Presentation;

/// <summary>설정 창: 블로그 주소, 작성자 이름, Claude 모델, 이미지(썸네일·그림·사진·AI 이미지).</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly LocalStore _store;
    private readonly AppSettings _settings;
    private readonly IDailyScheduler? _scheduler;

    public SettingsViewModel(LocalStore store, IDailyScheduler? scheduler = null)
    {
        _store = store;
        _scheduler = scheduler;
        _settings = store.LoadSettings();
        _author = _settings.Author;
        _naverBlogId = _settings.NaverBlogId;
        _tistoryName = _settings.TistoryName;
        _claudeModel = string.IsNullOrWhiteSpace(_settings.ClaudeModel) ? "sonnet" : _settings.ClaudeModel;
        _blogDisplayName = _settings.BlogDisplayName;
        _thumbnailEnabled = _settings.ThumbnailEnabled;
        _diagramsEnabled = _settings.DiagramsEnabled;
        _aiImagesEnabled = _settings.AiImagesEnabled;
        _dailyAutoEnabled = _settings.DailyAutoEnabled;
        _dailyTime = string.IsNullOrWhiteSpace(_settings.DailyTime) ? "07:30" : _settings.DailyTime;
        _openAiImageQuality = string.IsNullOrWhiteSpace(_settings.OpenAiImageQuality) ? "low" : _settings.OpenAiImageQuality;
        PexelsKeyStatus = Mask(store.Reveal(_settings.PexelsApiKey));
        OpenAiKeyStatus = Mask(store.Reveal(_settings.OpenAiApiKey));
    }

    /// <summary>이 기기에서 매일 자동 작성을 쓸 수 있는지 (Windows만).</summary>
    public bool CanScheduleDaily => _scheduler is not null;

    [ObservableProperty] private bool _dailyAutoEnabled;
    [ObservableProperty] private string _dailyTime;
    [ObservableProperty] private string _errorText = "";

    public IReadOnlyList<string> ModelOptions { get; } = new[] { "sonnet", "opus", "haiku" };
    public IReadOnlyList<string> QualityOptions { get; } = new[] { "low", "medium", "high" };

    public string DataFolder => _store.Root;

    [ObservableProperty] private string _author;
    [ObservableProperty] private string _naverBlogId;
    [ObservableProperty] private string _tistoryName;
    [ObservableProperty] private string _claudeModel;
    [ObservableProperty] private string _blogDisplayName;
    [ObservableProperty] private bool _thumbnailEnabled;
    [ObservableProperty] private bool _diagramsEnabled;
    [ObservableProperty] private bool _aiImagesEnabled;
    [ObservableProperty] private string _openAiImageQuality;

    /// <summary>새로 입력한 키 (비워 두면 기존 키 유지).</summary>
    [ObservableProperty] private string _pexelsKeyInput = "";
    [ObservableProperty] private string _openAiKeyInput = "";

    public string PexelsKeyStatus { get; private set; }
    public string OpenAiKeyStatus { get; private set; }

    internal static string Mask(string key) => key.Length == 0 ? "저장된 키 없음" : $"저장됨 ••••{key[^Math.Min(4, key.Length)..]}";

    /// <summary>저장 후 창을 닫으라는 신호.</summary>
    public event EventHandler? Saved;

    [RelayCommand]
    private void ClearPexelsKey()
    {
        _settings.PexelsApiKey = "";
        PexelsKeyInput = "";
        PexelsKeyStatus = Mask("");
        OnPropertyChanged(nameof(PexelsKeyStatus));
    }

    [RelayCommand]
    private void ClearOpenAiKey()
    {
        _settings.OpenAiApiKey = "";
        OpenAiKeyInput = "";
        OpenAiKeyStatus = Mask("");
        OnPropertyChanged(nameof(OpenAiKeyStatus));
    }

    [RelayCommand]
    private void Save()
    {
        if (!TimeOnly.TryParseExact(DailyTime.Trim(), new[] { "HH:mm", "H:mm" }, out var time))
        {
            ErrorText = "자동 작성 시각은 07:30처럼 24시간 형식으로 적어 주세요";
            return;
        }
        if (_scheduler is not null && (DailyAutoEnabled != _settings.DailyAutoEnabled || time.ToString("HH:mm") != _settings.DailyTime))
        {
            try
            {
                _scheduler.Apply(DailyAutoEnabled, time);
            }
            catch (Exception ex)
            {
                ErrorText = "매일 자동 작성을 등록하지 못했어요: " + ex.Message;
                return;
            }
        }
        _settings.DailyAutoEnabled = DailyAutoEnabled && _scheduler is not null;
        _settings.DailyTime = time.ToString("HH:mm");
        _settings.Author = Author.Trim();
        _settings.NaverBlogId = NaverBlogId.Trim();
        _settings.TistoryName = TistoryName.Trim();
        _settings.ClaudeModel = ClaudeModel;
        _settings.BlogDisplayName = BlogDisplayName.Trim();
        _settings.ThumbnailEnabled = ThumbnailEnabled;
        _settings.DiagramsEnabled = DiagramsEnabled;
        _settings.AiImagesEnabled = AiImagesEnabled;
        _settings.OpenAiImageQuality = OpenAiImageQuality;
        if (!string.IsNullOrWhiteSpace(PexelsKeyInput)) _settings.PexelsApiKey = _store.Conceal(PexelsKeyInput.Trim());
        if (!string.IsNullOrWhiteSpace(OpenAiKeyInput)) _settings.OpenAiApiKey = _store.Conceal(OpenAiKeyInput.Trim());
        _store.SaveSettings(_settings);
        Saved?.Invoke(this, EventArgs.Empty);
    }
}

using System.Collections.ObjectModel;
using BlogStudio.Core;
using BlogStudio.Core.Ai;
using BlogStudio.Core.Images;
using BlogStudio.Core.Posts;
using BlogStudio.Core.Prompting;
using BlogStudio.Core.Rendering;
using BlogStudio.Core.Review;
using BlogStudio.Core.Scheduling;
using BlogStudio.Core.Storage;
using BlogStudio.Core.Themes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogStudio.Presentation;

public enum PreviewMode { Rendered, Markdown }

public sealed record UpcomingTopicItem(string Label);

/// <summary>미리보기 아래 이미지 목록의 한 장. Number는 본문의 [이미지 N] 자리 표시와 같다.</summary>
public sealed record ImageItem(int Number, string KindLabel, string Caption, string FullPath, string? Credit)
{
    public string Title => $"이미지 {Number} · {KindLabel}";
}

/// <summary>글 종류: 기술 공부(요일별 커리큘럼) 또는 일상·리뷰 트랙(메모 후기 / 정보 글).</summary>
public sealed record CategoryItem(string Key, string Label, bool IsMemoCategory);

/// <summary>왼쪽 "지난 글" 목록 한 줄.</summary>
public sealed record HistoryItem(HistoryEntry Entry, string Title, string Meta);

public sealed record MemoPhoto(string FullPath)
{
    public string Name => Path.GetFileName(FullPath);
}

/// <summary>메인 화면: 오늘의 주제 → 생성 → 미리보기 → 복사. WPF·Avalonia가 같이 쓴다.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ThemeRepository _themes;
    private readonly LocalStore _store;
    private readonly IAiProvider _provider;
    private readonly IClipboardService _clipboard;
    private readonly IBrowserLauncher _browser;
    private readonly Func<DateOnly> _today;
    private readonly IClaudeCodeSetup? _setup;
    private readonly IUpdateService? _updates;
    private readonly IImageServices? _imageServices;
    private string _folder = "";
    private string _previewBodyHtml = "";

    private ThemePack? _theme;
    private ThemePack? _life;
    private readonly IFilePicker? _files;
    public const string TechCategory = "tech";
    public const string LifeTheme = "life";
    public const int MaxMemoPhotos = 10;
    private PromptBuilder? _prompts;
    private AppSettings _settings = new();
    private PlannedTopic? _plan;
    private PostDraft? _post;
    private string _bodyHtml = "";

    public MainViewModel(ThemeRepository themes, LocalStore store, IAiProvider provider,
        IClipboardService clipboard, IBrowserLauncher browser, Func<DateOnly>? today = null,
        IClaudeCodeSetup? setup = null, IUpdateService? updates = null, IImageServices? imageServices = null,
        IAutoFillLauncher? autoFill = null, IFilePicker? files = null)
    {
        _files = files;
        _autoFill = autoFill;
        _imageServices = imageServices;
        _setup = setup;
        _updates = updates;
        _themes = themes;
        _store = store;
        _provider = provider;
        _clipboard = clipboard;
        _browser = browser;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
        PreviewHtml = BlogHtmlRenderer.EmptyPage("오늘의 주제로 글을 생성하면 여기에 미리보기가 나타나요.");
    }

    private readonly IAutoFillLauncher? _autoFill;

    public bool CanAutoFill => _autoFill is not null;

    public ObservableCollection<UpcomingTopicItem> UpcomingTopics { get; } = new();

    public ObservableCollection<CategoryItem> Categories { get; } = new();

    // ── 지난 글 · 올림 표시 ──
    public const int HistoryCount = 30;
    public ObservableCollection<HistoryItem> RecentPosts { get; } = new();
    private string _currentFile = "";
    private bool _loadingPost;

    [ObservableProperty] private bool _isPublishedNaver;
    [ObservableProperty] private bool _isPublishedTistory;

    partial void OnIsPublishedNaverChanged(bool value) => SavePublished("naver", value);
    partial void OnIsPublishedTistoryChanged(bool value) => SavePublished("tistory", value);

    private void SavePublished(string blog, bool value)
    {
        if (_loadingPost || string.IsNullOrEmpty(_currentFile)) return;
        _store.SetPublished(_currentFile, blog, value);
        RefreshHistory();
    }

    private string TrackLabel(string key) =>
        _theme?.Curriculum.GetValueOrDefault(key)?.Name ?? _life?.Curriculum.GetValueOrDefault(key)?.Name ?? key;

    private void RefreshHistory()
    {
        RecentPosts.Clear();
        foreach (var h in _store.RecentPosts(HistoryCount))
        {
            var marks = string.Join(" ", new[] { ("naver", "네이버✓"), ("tistory", "티스토리✓") }
                .Where(m => h.IsPublishedTo(m.Item1)).Select(m => m.Item2));
            RecentPosts.Add(new HistoryItem(h, h.Title,
                $"{h.Date:MM/dd} · {TrackLabel(h.TrackKey)}{(marks.Length > 0 ? " · " + marks : " · 아직 안 올림")}"));
        }
    }

    [RelayCommand]
    private void OpenPost(HistoryItem? item)
    {
        if (item is null || IsGenerating) return;
        if (_store.LoadPost(item.Entry) is not { } saved)
        {
            StatusText = "글 파일을 찾지 못했어요 (지워졌을 수 있어요)";
            RefreshHistory();
            return;
        }
        ShowPost(saved);
        StatusText = $"{item.Entry.Date:MM/dd} 글을 열었어요";
    }

    /// <summary>
    /// 매일 자동 작성(작업 스케줄러가 "--auto-generate"로 실행)에서 부른다. 오늘의 기술 글을 만들고,
    /// 오늘 이미 만든 글이 있으면 건너뛴다. 만들었으면 true.
    /// </summary>
    public async Task<bool> AutoGenerateAsync()
    {
        if (_theme is null) return false;
        if (SelectedCategory?.Key != TechCategory) SelectedCategory = Categories.FirstOrDefault(c => c.Key == TechCategory);
        if (_store.HasPostOn(_today(), _theme.Tracks))
        {
            StatusText = "매일 자동 작성: 오늘 글은 이미 있어요";
            return false;
        }
        if (!IsProviderReady)
        {
            StatusText = "매일 자동 작성: Claude Code를 쓸 수 없어 건너뛰었어요 — 로그인 상태를 확인해 주세요";
            return false;
        }
        if (_plan is null)
        {
            StatusText = "매일 자동 작성: 오늘은 쉬는 날이에요";
            return false;
        }
        await GenerateCommand.ExecuteAsync(null);
        return PostReady && _store.HasPostOn(_today(), _theme.Tracks);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMemoCategory))]
    private CategoryItem? _selectedCategory;

    partial void OnSelectedCategoryChanged(CategoryItem? value) => RefreshPlan();

    /// <summary>메모·사진 입력칸을 보여 줄지 (일상·리뷰 카테고리).</summary>
    public bool IsMemoCategory => SelectedCategory?.IsMemoCategory == true;

    [ObservableProperty] private string _memoName = "";
    [ObservableProperty] private string _memoText = "";
    [ObservableProperty] private string _memoHint = "";

    public ObservableCollection<MemoPhoto> MemoPhotos { get; } = new();

    private ThemePack? ActiveTheme => IsMemoCategory ? _life : _theme;

    [RelayCommand]
    private void AddPhotos()
    {
        if (_files is null) return;
        foreach (var path in _files.PickImages())
        {
            if (MemoPhotos.Count >= MaxMemoPhotos) { StatusText = $"사진은 {MaxMemoPhotos}장까지 넣을 수 있어요"; break; }
            if (MemoPhotos.All(p => p.FullPath != path)) MemoPhotos.Add(new MemoPhoto(path));
        }
    }

    [RelayCommand]
    private void RemovePhoto(MemoPhoto? photo)
    {
        if (photo is not null) MemoPhotos.Remove(photo);
    }

    public ObservableCollection<ImageItem> Images { get; } = new();

    [ObservableProperty] private bool _hasImages;

    [ObservableProperty] private string _themeName = "";
    [ObservableProperty] private string _dateLabel = "";
    [ObservableProperty] private string _trackName = "";
    [ObservableProperty] private string _topicTitle = "";
    [ObservableProperty] private string _extraRequest = "";
    [ObservableProperty] private string _statusText = "준비 중…";
    [ObservableProperty] private string _providerStatus = "Claude Code 확인 중…";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallClaudeCodeCommand))]
    private bool _isProviderReady;

    /// <summary>확인을 마쳤는데 Claude Code를 쓸 수 없을 때 설치 안내 카드를 보여 준다.</summary>
    [ObservableProperty] private bool _showClaudeSetup;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallClaudeCodeCommand))]
    private bool _isInstalling;
    [ObservableProperty] private string _previewHtml = "";
    [ObservableProperty] private string _postTitle = "";
    [ObservableProperty] private string _postTags = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenderedMode), nameof(IsMarkdownMode))]
    private PreviewMode _previewMode = PreviewMode.Rendered;

    /// <summary>내용이 같아도 화면을 다시 그려 달라는 신호 (PreviewHtml 값이 같으면 PropertyChanged가 오지 않으므로).</summary>
    public event EventHandler? PreviewRefreshRequested;

    public bool IsRenderedMode => PreviewMode == PreviewMode.Rendered;
    public bool IsMarkdownMode => PreviewMode == PreviewMode.Markdown;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyUpdateCommand))]
    private bool _isUpdateAvailable;

    [ObservableProperty] private string _updateBannerText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyUpdateCommand))]
    private bool _isUpdating;

    /// <summary>앱 안 미리보기(WebView2)를 쓸 수 없을 때 안내 문구. 비어 있으면 정상.</summary>
    [ObservableProperty] private string _previewUnavailableMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPost))]
    [NotifyCanExecuteChangedFor(nameof(CopyForNaverCommand), nameof(CopyForTistoryCommand),
        nameof(AutoFillNaverCommand), nameof(AutoFillTistoryCommand),
        nameof(CopyMarkdownCommand), nameof(CopyTitleAndTagsCommand),
        nameof(ShowRenderedCommand), nameof(ShowMarkdownCommand), nameof(OpenPreviewInBrowserCommand),
        nameof(ReviseCommand), nameof(StartEditCommand))]
    private bool _postReady;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(ReviseCommand), nameof(StartEditCommand))]
    private bool _isGenerating;

    // ── 발행 전 점검 ──
    public ObservableCollection<CheckItem> Checks { get; } = new();
    [ObservableProperty] private string _checkSummary = "";

    private void RunChecks(PostDraft post)
    {
        Checks.Clear();
        foreach (var c in PostChecker.Check(post)) Checks.Add(c);
        var warn = Checks.Count(c => c.Level == CheckLevel.Warn);
        CheckSummary = warn == 0 ? "발행 전 점검 · 모두 좋아요" : $"발행 전 점검 · {warn}개 확인";
    }

    // ── Claude로 고치기 ──
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReviseCommand))]
    private string _revisionRequest = "";

    private IReadOnlyList<PostImage> _savedImages = Array.Empty<PostImage>();

    private bool CanRevise() => PostReady && !IsGenerating && !IsEditing && !string.IsNullOrWhiteSpace(RevisionRequest);

    [RelayCommand(CanExecute = nameof(CanRevise), IncludeCancelCommand = true)]
    private async Task ReviseAsync(CancellationToken ct)
    {
        if (_post is null || string.IsNullOrEmpty(_currentFile)) return;
        IsGenerating = true;
        StatusText = $"{_provider.Name}로 고치는 중… (1~2분)";
        try
        {
            var prompt = PromptBuilder.BuildRevision(LocalStore.ToFileText(_post), RevisionRequest, _settings.Author);
            var model = string.IsNullOrWhiteSpace(_settings.ClaudeModel) ? null : _settings.ClaudeModel;
            var result = await _provider.GenerateAsync(new AiRequest(prompt.System, prompt.User, "main", model), ct);
            var revised = PostParser.Parse(result.Text, _post.Topic, _post.TrackKey, _post.Date);
            revised = revised with
            {
                Topic = _post.Topic,
                ThumbnailLines = _post.ThumbnailLines,
                PhotoQuery = _post.PhotoQuery,
                IllustrationPrompt = _post.IllustrationPrompt,
            };
            SaveCurrent(revised);
            RevisionRequest = "";
            var lost = BlogHtmlRenderer.ImageSourcesInOrder(_post!.BodyMarkdown).Count < _savedImages.Count;
            StatusText = lost ? "고쳤어요 — 이미지 일부가 빠졌어요. 직접 편집으로 확인해 주세요" : "고쳤어요";
        }
        catch (OperationCanceledException)
        {
            StatusText = "고치기를 취소했어요";
        }
        catch (Exception ex) when (ex is AiProviderException or FormatException)
        {
            StatusText = "고치지 못했어요: " + ex.Message;
        }
        finally
        {
            IsGenerating = false;
        }
    }

    // ── 직접 편집 ──
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReviseCommand), nameof(StartEditCommand))]
    private bool _isEditing;

    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private string _editTags = "";
    [ObservableProperty] private string _editBody = "";

    private bool CanStartEdit() => PostReady && !IsGenerating && !IsEditing;

    [RelayCommand(CanExecute = nameof(CanStartEdit))]
    private void StartEdit()
    {
        if (_post is null) return;
        EditTitle = _post.Title;
        EditTags = string.Join(", ", _post.Tags);
        EditBody = _post.BodyMarkdown;
        IsEditing = true;
    }

    [RelayCommand]
    private void SaveEdit()
    {
        if (_post is null || string.IsNullOrEmpty(_currentFile)) return;
        if (string.IsNullOrWhiteSpace(EditTitle) || string.IsNullOrWhiteSpace(EditBody))
        {
            StatusText = "제목과 본문은 비울 수 없어요";
            return;
        }
        var tags = EditTags.Split(new[] { ',', '#' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct().ToList();
        IsEditing = false;
        SaveCurrent(_post with { Title = EditTitle.Trim(), Tags = tags, BodyMarkdown = EditBody.TrimEnd() });
        StatusText = "편집한 내용을 저장했어요";
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    /// <summary>지금 글을 같은 폴더에 덮어쓰고 미리보기·점검·목록을 새로 고친다.</summary>
    private void SaveCurrent(PostDraft post)
    {
        _store.UpdatePost(_currentFile, post);
        ShowPost(new SavedPost(post, _folder, _savedImages, _currentFile));
        RefreshHistory();
    }

    public bool HasPost => PostReady;

    [RelayCommand]
    private async Task InitializeAsync()
    {
        _settings = _store.LoadSettings();
        _theme = _themes.Load(_settings.ThemeSlug);
        _prompts = new PromptBuilder(_themes.LoadBasePrompt());
        ThemeName = _theme.Name;
        LoadCategories();
        RefreshHistory();

        if (_store.LoadLatest() is { } latest)
        {
            ShowPost(latest);
            StatusText = $"최근 글을 불러왔어요 · {latest.Post.Date:MM/dd}";
        }
        else
        {
            StatusText = "준비 완료";
        }

        await CheckProviderAsync();
        await CheckForUpdatesAsync();
    }

    /// <summary>시작할 때 한 번 새 버전을 확인한다. 네트워크 오류는 조용히 넘긴다.</summary>
    private async Task CheckForUpdatesAsync()
    {
        if (_updates is null || !_updates.IsInstalled) return;
        try
        {
            if (await _updates.CheckAsync() is { } update)
            {
                UpdateBannerText = $"새 버전 {update.Version}이 준비됐어요";
                IsUpdateAvailable = true;
            }
        }
        catch (Exception)
        {
            // 오프라인 등 — 다음 실행 때 다시 확인
        }
    }

    private bool CanApplyUpdate() => IsUpdateAvailable && !IsUpdating;

    [RelayCommand(CanExecute = nameof(CanApplyUpdate))]
    private async Task ApplyUpdateAsync()
    {
        if (_updates is null) return;
        IsUpdating = true;
        try
        {
            await _updates.DownloadAsync(p => UpdateBannerText = $"업데이트 받는 중… {p}%");
            UpdateBannerText = "재시작하는 중…";
            _updates.ApplyAndRestart();
        }
        catch (Exception ex)
        {
            UpdateBannerText = "업데이트 실패 — 다음 실행 때 다시 시도해요";
            StatusText = "업데이트 실패: " + ex.Message;
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private async Task CheckProviderAsync()
    {
        ProviderStatus = "Claude Code 확인 중…";
        var version = await _provider.CheckAsync();
        IsProviderReady = version is not null;
        ShowClaudeSetup = !IsProviderReady;
        ProviderStatus = IsProviderReady
            ? $"{_provider.Name} 연결됨 · {version}"
            : "Claude Code가 없어요 — 가운데 안내에서 자동 설치를 눌러 주세요";
    }

    private bool CanInstallClaudeCode() => _setup is not null && !IsProviderReady && !IsInstalling;

    /// <summary>공식 설치 스크립트 실행 → 다시 확인 → 설치됐으면 로그인 창을 연다.</summary>
    [RelayCommand(CanExecute = nameof(CanInstallClaudeCode))]
    private async Task InstallClaudeCodeAsync(CancellationToken ct)
    {
        if (_setup is null) return;
        IsInstalling = true;
        StatusText = "Claude Code 설치 중… 새로 열린 창이 끝날 때까지 기다려 주세요";
        try
        {
            var ok = await _setup.InstallAsync(ct);
            await CheckProviderAsync();
            if (IsProviderReady)
            {
                StatusText = "설치 완료 — 열린 창에서 Claude Pro 계정으로 로그인해 주세요";
                _setup.OpenLoginTerminal();
            }
            else
            {
                StatusText = ok
                    ? "설치는 끝났지만 claude를 찾지 못했어요. 앱을 다시 시작해 보세요"
                    : "설치에 실패했어요. 설치 창의 메시지를 확인해 주세요";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "설치를 취소했어요";
        }
        catch (Exception ex)
        {
            StatusText = "설치를 시작하지 못했어요: " + ex.Message;
        }
        finally
        {
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private void OpenClaudeLogin()
    {
        _setup?.OpenLoginTerminal();
        StatusText = "열린 창에서 로그인을 마친 뒤 \"다시 확인\"을 눌러 주세요";
    }

    /// <summary>설정이 바뀐 뒤 다시 읽기.</summary>
    public void ReloadSettings()
    {
        _settings = _store.LoadSettings();
        _theme = _themes.Load(_settings.ThemeSlug);
        ThemeName = _theme.Name;
        RefreshPlan();
    }

    private void LoadCategories()
    {
        _life = _themes.ListThemeSlugs().Contains(LifeTheme) ? _themes.Load(LifeTheme) : null;
        var keep = SelectedCategory?.Key;
        Categories.Clear();
        Categories.Add(new CategoryItem(TechCategory, "기술 공부 · 오늘의 주제", false));
        if (_life is not null)
            foreach (var key in _life.Tracks.Where(_life.Curriculum.ContainsKey))
                Categories.Add(new CategoryItem(key, _life.Curriculum[key].Name, true));
        var next = Categories.FirstOrDefault(c => c.Key == keep) ?? Categories[0];
        if (ReferenceEquals(next, SelectedCategory) || next == SelectedCategory) RefreshPlan();
        else SelectedCategory = next;
    }

    /// <summary>일상·리뷰 카테고리: 메모가 있으면 후기, 없으면 트랙의 주제 아이디어를 순서대로.</summary>
    private void RefreshMemoPlan(DateOnly today)
    {
        var track = _life!.Curriculum[SelectedCategory!.Key];
        var index = _store.NextIndex(track.Key);
        var topic = index < track.Topics.Count ? track.Topics[index] : null;
        _plan = new PlannedTopic(today, track, topic, index);
        TrackName = $"{track.Name} · 메모를 쓰면 후기, 비우면 정보 글";
        TopicTitle = topic ?? "주제 아이디어를 다 썼어요 — 비워 두면 Claude가 골라요";
        MemoHint = string.IsNullOrWhiteSpace(track.MemoHint) ? "직접 겪은 내용을 자유롭게" : track.MemoHint;

        UpcomingTopics.Clear();
        foreach (var next in track.Topics.Skip(index + 1).Take(3))
            UpcomingTopics.Add(new UpcomingTopicItem($"{track.Name} 정보 글 — {next}"));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    private void RefreshPlan()
    {
        if (_theme is null) return;
        var today = _today();
        if (IsMemoCategory && _life is not null)
        {
            DateLabel = $"{today:M월 d일} {DayName(today)}요일";
            RefreshMemoPlan(today);
            return;
        }
        var planner = new TopicPlanner(_theme, _settings.Schedule);
        DateLabel = $"{today:M월 d일} {DayName(today)}요일";

        _plan = planner.Plan(today, _store.NextIndex);
        if (_plan is null)
        {
            TrackName = "오늘은 쉬는 날";
            TopicTitle = "설정에서 요일별 트랙을 바꿀 수 있어요";
        }
        else
        {
            TrackName = $"{_plan.Track.Name} · {_plan.Index + 1} / {_plan.Track.Topics.Count}";
            TopicTitle = _plan.Topic ?? "커리큘럼 완료 — 다음 심화 주제를 Claude가 골라요";
        }

        UpcomingTopics.Clear();
        foreach (var p in planner.Upcoming(today, _store.NextIndex, 3))
            UpcomingTopics.Add(new UpcomingTopicItem($"{p.DayLabel} · {p.Track.Name} — {p.Topic ?? "자동 심화 주제"}"));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    private bool CanGenerate() => !IsGenerating && _plan is not null;

    private bool CanCopy() => PostReady;

    [RelayCommand(CanExecute = nameof(CanGenerate), IncludeCancelCommand = true)]
    private async Task GenerateAsync(CancellationToken ct)
    {
        var theme = ActiveTheme;
        if (theme is null || _prompts is null || _plan is null) return;
        var inputs = IsMemoCategory ? new PostInputs(MemoName, MemoText, MemoPhotos.Count) : null;
        var fromMemo = inputs?.HasMemo == true;
        var photos = MemoPhotos.Select(p => p.FullPath).Where(File.Exists).ToList();
        IsGenerating = true;
        StatusText = $"{_provider.Name}로 작성 중… (1~3분)";
        try
        {
            var history = _store.LoadState().History;
            var prompt = _prompts.Build(theme, _plan.Track, _plan.Topic, history, _settings.Author, ExtraRequest, inputs);
            var model = string.IsNullOrWhiteSpace(_settings.ClaudeModel) ? null : _settings.ClaudeModel;
            var result = await _provider.GenerateAsync(new AiRequest(prompt.System, prompt.User, theme.ModelTier, model), ct);

            var topicLabel = fromMemo
                ? (string.IsNullOrWhiteSpace(MemoName) ? "메모 후기" : MemoName.Trim())
                : _plan.Topic ?? "(자동 선정)";
            var post = PostParser.Parse(result.Text, topicLabel, _plan.Track.Key, _today());
            if (!fromMemo && _plan.Topic is null) post = post with { Topic = post.Title };

            StatusText = "이미지 만드는 중… (썸네일·그림·사진)";
            var folder = _store.CreatePostFolder(post);
            var images = await ImagePipeline.RunAsync(post, folder, BuildImageOptions(_plan, theme, fromMemo, photos), ct);
            post = post with { BodyMarkdown = images.BodyMarkdown };
            var savedPath = _store.SavePost(post, !fromMemo && _plan.Topic is not null, folder, images.Images);
            if (IsMemoCategory)
            {
                MemoName = "";
                MemoText = "";
                MemoPhotos.Clear();
            }

            ShowPost(new SavedPost(post, folder, images.Images, _store.RelativeFileName(savedPath)));
            RefreshHistory();
            ExtraRequest = "";
            RefreshPlan();
            StatusText = images.Warnings.Count == 0
                ? $"완료 · {result.Model}로 작성, 이미지 {images.Images.Count}장"
                : $"완료 · 이미지 {images.Images.Count}장 (확인: {string.Join(" / ", images.Warnings)})";
        }
        catch (OperationCanceledException)
        {
            StatusText = "생성을 취소했어요";
        }
        catch (Exception ex) when (ex is AiProviderException or FormatException)
        {
            StatusText = "실패: " + ex.Message;
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private ImagePipelineOptions BuildImageOptions(PlannedTopic plan, ThemePack theme, bool fromMemo,
        IReadOnlyList<string> photos) => new()
    {
        Thumbnail = _settings.ThumbnailEnabled,
        ThumbnailLabel = fromMemo ? $"{plan.Track.Name} · 후기"
            : IsMemoCategory ? plan.Track.Name
            : $"{plan.Track.Name} · {plan.Index + 1}",
        ThumbnailColor = theme.ColorFor(plan.Track.Key),
        UserPhotos = photos,
        BlogName = string.IsNullOrWhiteSpace(_settings.BlogDisplayName) ? _settings.Author : _settings.BlogDisplayName,
        Diagrams = _settings.DiagramsEnabled,
        Photos = _imageServices?.Photos(_settings),
        Generator = _imageServices?.Generator(_settings),
    };

    private void ShowPost(SavedPost saved)
    {
        _currentFile = saved.FileName;
        var entry = _store.LoadState().History.LastOrDefault(h => h.FileName == saved.FileName);
        _loadingPost = true;
        IsPublishedNaver = entry?.IsPublishedTo("naver") == true;
        IsPublishedTistory = entry?.IsPublishedTo("tistory") == true;
        _loadingPost = false;
        var post = saved.Post;
        _post = post;
        _folder = saved.Folder;
        _savedImages = saved.Images;
        IsEditing = false;
        RunChecks(post);
        // 붙여넣기용: 이미지 자리는 "[이미지 N]" 표시 / 미리보기용: 실제 이미지
        _bodyHtml = BlogHtmlRenderer.ToBlogHtml(BlogHtmlRenderer.ReplaceImagesWithPlaceholders(post.BodyMarkdown));
        _previewBodyHtml = BlogHtmlRenderer.AbsolutizeImages(BlogHtmlRenderer.ToBlogHtml(post.BodyMarkdown), saved.Folder);

        Images.Clear();
        var number = 0;
        foreach (var src in BlogHtmlRenderer.ImageSourcesInOrder(post.BodyMarkdown))
        {
            number++;
            var meta = saved.Images.FirstOrDefault(i => i.RelativePath == src);
            var full = Path.Combine(saved.Folder, src.Replace('/', Path.DirectorySeparatorChar));
            Images.Add(new ImageItem(number, meta?.KindLabel ?? "이미지", meta?.Caption ?? "", full, meta?.Credit));
        }
        HasImages = Images.Count > 0;

        PostTitle = post.Title;
        PostTags = post.TagLine;
        PostReady = true;
        RenderPreview();
    }

    [RelayCommand]
    private void CopyImage(ImageItem? item)
    {
        if (item is null || !File.Exists(item.FullPath)) return;
        _clipboard.SetImage(item.FullPath);
        StatusText = $"이미지 {item.Number}을 복사했어요 — 글쓰기 화면의 [이미지 {item.Number}] 자리에서 Ctrl+V";
    }

    [RelayCommand]
    private void OpenImagesFolder()
    {
        var dir = Path.Combine(_folder, ImagePipeline.ImagesDir);
        if (Directory.Exists(dir)) _browser.Open(dir);
    }

    partial void OnPreviewModeChanged(PreviewMode value) => RenderPreview();

    private void RenderPreview()
    {
        if (_post is null) return;
        PreviewHtml = PreviewMode == PreviewMode.Markdown
            ? BlogHtmlRenderer.ToMarkdownPage(_post.BodyMarkdown)
            : BlogHtmlRenderer.ToPreviewPage(_post, _previewBodyHtml);
    }

    /// <summary>같은 모드를 다시 눌러도 항상 다시 그린다 (이전엔 값이 같으면 아무 일도 없었음).</summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void ShowRendered()
    {
        PreviewMode = PreviewMode.Rendered;
        RenderPreview();
        PreviewRefreshRequested?.Invoke(this, EventArgs.Empty);
        StatusText = "블로그 보기";
    }

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void ShowMarkdown()
    {
        PreviewMode = PreviewMode.Markdown;
        RenderPreview();
        PreviewRefreshRequested?.Invoke(this, EventArgs.Empty);
        StatusText = "마크다운 보기";
    }

    /// <summary>앱 안 미리보기가 안 될 때를 대비해 기본 브라우저로 연다.</summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void OpenPreviewInBrowser()
    {
        if (_post is null) return;
        var path = Path.Combine(_store.Root, "preview.html");
        File.WriteAllText(path, BlogHtmlRenderer.ToPreviewPage(_post, _previewBodyHtml));
        _browser.Open(new Uri(path).AbsoluteUri);
        StatusText = "미리보기를 브라우저로 열었어요";
    }

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void CopyForNaver() => CopyBodyAndOpen(BlogTargets.NaverWriteUrl(_settings.NaverBlogId), "네이버");

    /// <summary>
    /// 티스토리 기본 모드는 붙여넣은 표의 구조·스타일을 깨뜨리므로, HTML 소스를 텍스트로 복사해
    /// 에디터를 HTML 모드로 바꾼 뒤 붙여넣게 한다 (표·코드 블록이 그대로 유지됨).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void CopyForTistory()
    {
        if (_post is null) return;
        _clipboard.SetText(_bodyHtml);
        var url = BlogTargets.TistoryWriteUrl(_settings.TistoryName);
        if (url is not null) _browser.Open(url);
        StatusText = "HTML을 복사했어요 — 티스토리 글쓰기 오른쪽 위 '기본모드'를 'HTML'로 바꾼 뒤 Ctrl+V";
    }

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void AutoFillTistory() =>
        OpenAutoFill(BlogPlatform.Tistory, BlogTargets.TistoryWriteUrl(_settings.TistoryName), "티스토리", "티스토리 이름");

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void AutoFillNaver() =>
        OpenAutoFill(BlogPlatform.Naver, BlogTargets.NaverWriteUrl(_settings.NaverBlogId), "네이버", "네이버 블로그 ID");

    private void OpenAutoFill(BlogPlatform platform, string? url, string blog, string settingName)
    {
        if (_post is null) return;
        if (_autoFill is null)
        {
            StatusText = "이 기기에서는 자동 채우기를 쓸 수 없어요 — 복사 버튼을 써 주세요";
            return;
        }
        if (url is null)
        {
            StatusText = $"설정에 {settingName}을 넣어야 자동 채우기를 쓸 수 있어요";
            return;
        }
        _autoFill.Open(new AutoFillPayload(platform, url, _post.Title, _post.Tags, _bodyHtml,
            Images.OrderBy(i => i.Number).Select(i => i.FullPath).ToList()));
        StatusText = $"{blog} 글쓰기 창을 열었어요 — '모두 채우기' 후 내용을 확인하고 직접 발행하세요";
    }

    private void CopyBodyAndOpen(string? url, string blog)
    {
        if (_post is null) return;
        _clipboard.SetHtml(_bodyHtml, _post.BodyMarkdown);
        if (url is not null)
        {
            _browser.Open(url);
            StatusText = $"본문을 복사했어요 — {blog} 글쓰기 화면 본문에 Ctrl+V";
        }
        else
        {
            StatusText = $"본문을 복사했어요 — 설정에 {blog} 블로그 주소를 넣으면 글쓰기 화면도 열려요";
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void CopyMarkdown()
    {
        if (_post is null) return;
        _clipboard.SetText(BlogHtmlRenderer.ReplaceImagesWithPlaceholders(_post.BodyMarkdown) + "\n\n" + _post.TagLine);
        StatusText = "마크다운을 복사했어요 (티스토리 마크다운 모드에 붙여넣기)";
    }

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void CopyTitleAndTags()
    {
        if (_post is null) return;
        _clipboard.SetText(_post.Title + "\n" + string.Join(", ", _post.Tags));
        StatusText = "제목과 태그를 복사했어요 (첫 줄 제목, 둘째 줄 태그)";
    }

    private static string DayName(DateOnly d) => d.DayOfWeek switch
    {
        DayOfWeek.Monday => "월", DayOfWeek.Tuesday => "화", DayOfWeek.Wednesday => "수",
        DayOfWeek.Thursday => "목", DayOfWeek.Friday => "금", DayOfWeek.Saturday => "토", _ => "일",
    };
}

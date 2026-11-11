using System.Collections.ObjectModel;
using BlogStudio.Core;
using BlogStudio.Core.Ai;
using BlogStudio.Core.Posts;
using BlogStudio.Core.Prompting;
using BlogStudio.Core.Rendering;
using BlogStudio.Core.Scheduling;
using BlogStudio.Core.Storage;
using BlogStudio.Core.Themes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogStudio.Presentation;

public enum PreviewMode { Rendered, Markdown }

public sealed record UpcomingTopicItem(string Label);

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

    private ThemePack? _theme;
    private PromptBuilder? _prompts;
    private AppSettings _settings = new();
    private PlannedTopic? _plan;
    private PostDraft? _post;
    private string _bodyHtml = "";

    public MainViewModel(ThemeRepository themes, LocalStore store, IAiProvider provider,
        IClipboardService clipboard, IBrowserLauncher browser, Func<DateOnly>? today = null,
        IClaudeCodeSetup? setup = null)
    {
        _setup = setup;
        _themes = themes;
        _store = store;
        _provider = provider;
        _clipboard = clipboard;
        _browser = browser;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
        PreviewHtml = BlogHtmlRenderer.EmptyPage("오늘의 주제로 글을 생성하면 여기에 미리보기가 나타나요.");
    }

    public ObservableCollection<UpcomingTopicItem> UpcomingTopics { get; } = new();

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
    [ObservableProperty] private PreviewMode _previewMode = PreviewMode.Rendered;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPost))]
    [NotifyCanExecuteChangedFor(nameof(CopyForNaverCommand), nameof(CopyForTistoryCommand),
        nameof(CopyMarkdownCommand), nameof(CopyTitleAndTagsCommand))]
    private bool _postReady;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private bool _isGenerating;

    public bool HasPost => PostReady;

    [RelayCommand]
    private async Task InitializeAsync()
    {
        _settings = _store.LoadSettings();
        _theme = _themes.Load(_settings.ThemeSlug);
        _prompts = new PromptBuilder(_themes.LoadBasePrompt());
        ThemeName = _theme.Name;
        RefreshPlan();

        if (_store.LoadLatestPost() is { } latest)
        {
            ShowPost(latest);
            StatusText = $"최근 글을 불러왔어요 · {latest.Date:MM/dd}";
        }
        else
        {
            StatusText = "준비 완료";
        }

        await CheckProviderAsync();
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

    private void RefreshPlan()
    {
        if (_theme is null) return;
        var today = _today();
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
        if (_theme is null || _prompts is null || _plan is null) return;
        IsGenerating = true;
        StatusText = $"{_provider.Name}로 작성 중… (1~3분)";
        try
        {
            var history = _store.LoadState().History;
            var prompt = _prompts.Build(_theme, _plan.Track, _plan.Topic, history, _settings.Author, ExtraRequest);
            var model = string.IsNullOrWhiteSpace(_settings.ClaudeModel) ? null : _settings.ClaudeModel;
            var result = await _provider.GenerateAsync(new AiRequest(prompt.System, prompt.User, _theme.ModelTier, model), ct);

            var post = PostParser.Parse(result.Text, _plan.Topic ?? "(자동 선정)", _plan.Track.Key, _today());
            if (_plan.Topic is null) post = post with { Topic = post.Title };
            _store.SavePost(post, advanceProgress: _plan.Topic is not null);

            ShowPost(post);
            ExtraRequest = "";
            RefreshPlan();
            StatusText = $"완료 · {result.Model}로 작성했어요";
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

    private void ShowPost(PostDraft post)
    {
        _post = post;
        _bodyHtml = BlogHtmlRenderer.ToBlogHtml(post.BodyMarkdown);
        PostTitle = post.Title;
        PostTags = post.TagLine;
        PostReady = true;
        RenderPreview();
    }

    partial void OnPreviewModeChanged(PreviewMode value) => RenderPreview();

    private void RenderPreview()
    {
        if (_post is null) return;
        PreviewHtml = PreviewMode == PreviewMode.Markdown
            ? BlogHtmlRenderer.ToMarkdownPage(_post.BodyMarkdown)
            : BlogHtmlRenderer.ToPreviewPage(_post, _bodyHtml);
    }

    [RelayCommand]
    private void ShowRendered() => PreviewMode = PreviewMode.Rendered;

    [RelayCommand]
    private void ShowMarkdown() => PreviewMode = PreviewMode.Markdown;

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void CopyForNaver() => CopyBodyAndOpen(BlogTargets.NaverWriteUrl(_settings.NaverBlogId), "네이버");

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void CopyForTistory() => CopyBodyAndOpen(BlogTargets.TistoryWriteUrl(_settings.TistoryName), "티스토리");

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
        _clipboard.SetText(_post.BodyMarkdown + "\n\n" + _post.TagLine);
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

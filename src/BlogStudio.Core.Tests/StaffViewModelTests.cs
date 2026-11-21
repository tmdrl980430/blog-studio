using BlogStudio.Core.Ai;
using BlogStudio.Core.Storage;
using BlogStudio.Presentation;

namespace BlogStudio.Core.Tests;

/// <summary>앱 안 AI 직원(기획자·편집자)이 메인 화면 흐름에 맞게 동작하는지.</summary>
public class StaffViewModelTests
{
    /// <summary>요청 내용으로 직원을 구분해 정해진 답을 돌려준다.</summary>
    private sealed class StaffProvider : IAiProvider
    {
        public readonly List<AiRequest> Requests = new();
        public string Review = "판정: 수정 후 발행\n- 첫 문단에 결론을 먼저\n- 표 아래 문장이 너무 김";
        public string Write = Fixtures.SampleResponse;
        public string Analyst = "CS 기초 | TCP 다음으로 HTTP 기초 | 2주 넘게 글 없음\n맛집·카페 | 동네 카페 메모 모으기 | 아직 글 없음\n블록체인 | 입문 | 새 트랙";
        public string Name => "Fake";
        public Task<string?> CheckAsync(CancellationToken ct = default) => Task.FromResult<string?>("1.0");

        public Task<AiResult> GenerateAsync(AiRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            var text = request.System.Contains("성과 분석가") ? Analyst
                : request.System.Contains("주제 기획자")
                ? "파이썬 deque 시간복잡도 정리 | 파이썬 deque | BFS 다음 단계\n투 포인터 기본 패턴 | 투 포인터 | 코테 단골"
                : request.User.StartsWith("[검수할 글]") ? Review
                : Write;
            return Task.FromResult(new AiResult(text, Name, "sonnet"));
        }
    }

    private sealed class NoClipboard : IClipboardService
    {
        public void SetHtml(string html, string plainText) { }
        public void SetText(string text) { }
    }

    private sealed class NoBrowser : IBrowserLauncher
    {
        public void Open(string url) { }
    }

    private sealed class FakePublisher : IAutoFillLauncher
    {
        public readonly List<AutoFillPayload> Published = new();
        public bool Result = true;
        public void Open(AutoFillPayload payload) { }

        public Task<bool> PublishAsync(AutoFillPayload payload)
        {
            Published.Add(payload);
            return Task.FromResult(Result);
        }
    }

    /// <summary>발행 기준(본문 길이)을 넘는 글.</summary>
    private static readonly string LongPost =
        "---\ntitle: \"파이썬 deque 시간복잡도 정리\"\ntags: [파이썬, deque, 자료구조, 코딩테스트, 큐]\nsummary: \"deque 연산별 시간복잡도\"\n---\n" +
        string.Join("\n\n", Enumerable.Range(1, 4).Select(i =>
            $"## 소제목 {i}\n" + string.Concat(Enumerable.Repeat("deque는 양쪽 끝에서 넣고 빼는 연산이 모두 상수 시간이라 큐로 쓰기 좋습니다. ", 12))));

    private static async Task<(MainViewModel Vm, LocalStore Store, StaffProvider Ai)> StartAsync(
        AppSettings? settings = null, IAutoFillLauncher? autoFill = null)
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-staff-").FullName);
        if (settings is not null) store.SaveSettings(settings);
        var ai = new StaffProvider();
        var vm = new MainViewModel(Fixtures.Themes(), store, ai, new NoClipboard(), new NoBrowser(),
            () => new DateOnly(2026, 11, 11), autoFill: autoFill);
        await vm.InitializeCommand.ExecuteAsync(null);
        return (vm, store, ai);
    }

    [Fact]
    public async Task Planner_topic_is_written_with_keyword_and_does_not_advance_curriculum()
    {
        var (vm, store, ai) = await StartAsync();
        var curriculumTopic = vm.TopicTitle;

        await vm.AskPlannerCommand.ExecuteAsync(null);
        Assert.Equal("light", ai.Requests[^1].ModelTier);
        Assert.Equal(2, vm.TopicIdeas.Count);

        vm.PickTopicIdeaCommand.Execute(vm.TopicIdeas[0]);
        Assert.Equal("파이썬 deque 시간복잡도 정리", vm.TopicTitle);
        Assert.Empty(vm.TopicIdeas);

        await vm.GenerateCommand.ExecuteAsync(null);
        var write = ai.Requests[^1];
        Assert.Contains("[오늘의 주제] 파이썬 deque 시간복잡도 정리", write.User);
        Assert.Contains("핵심 검색 키워드 \"파이썬 deque\"", write.User);

        var state = store.LoadState();
        Assert.Single(state.History);
        Assert.Empty(state.Progress.Where(p => p.Value > 0));                // 커리큘럼은 그대로
        Assert.Equal(curriculumTopic, vm.TopicTitle);                           // 다음 글은 다시 커리큘럼 주제
    }

    [Fact]
    public async Task Editor_review_shows_notes_and_applies_them_through_revision()
    {
        var (vm, store, ai) = await StartAsync();
        Assert.False(vm.ReviewWithEditorCommand.CanExecute(null));               // 글이 없으면 못 누름
        await vm.GenerateCommand.ExecuteAsync(null);

        await vm.ReviewWithEditorCommand.ExecuteAsync(null);
        Assert.Equal("편집자 판정 · 수정 후 발행", vm.EditorVerdict);
        Assert.Equal(2, vm.EditorNotes.Count);
        Assert.True(vm.ApplyEditorNotesCommand.CanExecute(null));

        await vm.ApplyEditorNotesCommand.ExecuteAsync(null);
        var revise = ai.Requests[^1];
        Assert.Contains("[고칠 점]\n편집자 검수 의견대로", revise.User);
        Assert.Contains("- 표 아래 문장이 너무 김", revise.User);
        Assert.False(vm.HasEditorReview);                                        // 고친 글은 다시 검수 받기
        Assert.Single(store.LoadState().History);
    }

    [Fact]
    public async Task Editor_with_no_notes_says_publishable()
    {
        var (vm, _, ai) = await StartAsync();
        ai.Review = "판정: 발행 가능";
        await vm.GenerateCommand.ExecuteAsync(null);
        await vm.ReviewWithEditorCommand.ExecuteAsync(null);

        Assert.True(vm.HasEditorReview);
        Assert.False(vm.HasEditorNotes);
        Assert.False(vm.ApplyEditorNotesCommand.CanExecute(null));
        Assert.Contains("발행해도 좋아요", vm.StatusText);
    }

    private static AppSettings AutoPublish() =>
        new() { TistoryName = "seunggi", AutoPublishEnabled = true, AutoPublishNaver = false, AutoPublishTistory = true };

    [Fact]
    public async Task Auto_generate_reviews_applies_notes_and_publishes_when_gate_passes()
    {
        var publisher = new FakePublisher();
        var (vm, store, ai) = await StartAsync(AutoPublish(), publisher);
        ai.Write = LongPost;

        Assert.True(await vm.AutoGenerateAsync());

        Assert.Contains(ai.Requests, r => r.User.StartsWith("[검수할 글]"));
        Assert.Contains(ai.Requests, r => r.User.Contains("편집자 검수 의견대로"));
        var post = Assert.Single(publisher.Published);
        Assert.Equal(BlogPlatform.Tistory, post.Platform);
        Assert.Equal("https://seunggi.tistory.com/manage/newpost", post.WriteUrl);
        Assert.True(vm.IsPublishedTistory);
        Assert.True(store.LoadState().History[0].IsPublishedTo("tistory"));
        Assert.Contains("티스토리 발행 ✓", File.ReadAllText(Path.Combine(store.Root, "auto-log.txt")));
        var logged = Assert.Single(vm.LoadAutoLog());
        Assert.Equal(BlogStudio.Core.Stats.AutoLogOutcome.Published, logged.Outcome);
        Assert.Equal(vm.AutoLogPath, Path.Combine(store.Root, "auto-log.txt"));
    }

    [Fact]
    public async Task Auto_generate_holds_post_when_editor_says_rewrite_or_body_is_thin()
    {
        var publisher = new FakePublisher();
        var (vm, store, ai) = await StartAsync(AutoPublish(), publisher);
        ai.Write = LongPost;
        ai.Review = "판정: 다시 쓰기\n- 주제와 본문이 맞지 않음";
        Assert.True(await vm.AutoGenerateAsync());
        Assert.Empty(publisher.Published);
        Assert.DoesNotContain(ai.Requests, r => r.User.Contains("편집자 검수 의견대로"));   // 다시 쓰기는 고치지 않음
        Assert.Contains("발행 보류 — 편집자 판정: 다시 쓰기", vm.StatusText);

        var (vm2, _, ai2) = await StartAsync(AutoPublish(), publisher);
        ai2.Review = "판정: 발행 가능";                                               // 짧은 기본 응답
        Assert.True(await vm2.AutoGenerateAsync());
        Assert.Empty(publisher.Published);
        Assert.Contains("너무 짧음", vm2.StatusText);
    }

    [Fact]
    public async Task Auto_generate_logs_skip_reason_and_warns_when_nothing_was_published()
    {
        var publisher = new FakePublisher();
        var (vm, store, ai) = await StartAsync(AutoPublish(), publisher);
        Assert.False(vm.HasAutoPublishWarning);                                  // 기록이 없으면 경고하지 않음

        ai.Write = LongPost;
        ai.Review = "판정: 다시 쓰기\n- 주제와 본문이 맞지 않음";
        Assert.True(await vm.AutoGenerateAsync());                               // 보류 → 발행 0건
        Assert.True(vm.HasAutoPublishWarning);
        Assert.Contains("자동 발행 0건", vm.AutoPublishWarning);
        Assert.Contains("발행 보류", vm.AutoPublishWarning);

        Assert.False(await vm.AutoGenerateAsync());                              // 오늘 글이 이미 있음 → 건너뜀도 기록
        var latest = vm.LoadAutoLog()[0];
        Assert.Equal(BlogStudio.Core.Stats.AutoLogOutcome.Skipped, latest.Outcome);
        Assert.Contains("오늘 글이 이미 있음", latest.Result);
        Assert.Contains("오늘 글이 이미 있음", vm.AutoPublishWarning);

        store.SaveSettings(new AppSettings { TistoryName = "seunggi" });         // 자동 발행을 끄면 경고 숨김
        vm.ReloadSettings();
        Assert.False(vm.HasAutoPublishWarning);
    }

    [Fact]
    public async Task Auto_generate_reviews_but_does_not_publish_when_auto_publish_is_off()
    {
        var publisher = new FakePublisher();
        var (vm, _, ai) = await StartAsync(new AppSettings { TistoryName = "seunggi" }, publisher);
        ai.Write = LongPost;
        ai.Review = "판정: 발행 가능";
        Assert.True(await vm.AutoGenerateAsync());
        Assert.Contains(ai.Requests, r => r.User.StartsWith("[검수할 글]"));
        Assert.Empty(publisher.Published);
        Assert.Contains("자동 발행 꺼짐", vm.StatusText);
    }

    [Fact]
    public async Task Analyst_lists_tech_and_life_tracks_and_keeps_only_known_ones()
    {
        var (vm, _, ai) = await StartAsync();
        Assert.True(vm.AskAnalystCommand.CanExecute(null));

        await vm.AskAnalystCommand.ExecuteAsync(null);
        var ask = ai.Requests[^1];
        Assert.Equal("light", ask.ModelTier);
        Assert.Contains("[트랙별 기록]", ask.User);
        Assert.Contains("CS 기초: 아직 글 없음", ask.User);                     // 기술 트랙
        Assert.Contains("맛집·카페: 아직 글 없음", ask.User);                   // 일상·리뷰 트랙
        Assert.Equal(new[] { "cs", "food" }, vm.AnalystPicks.Select(p => p.TrackKey));   // 목록 밖 트랙은 버림
        Assert.Contains("기획자", vm.AnalystStatus);
        Assert.False(vm.IsGenerating);

        ai.Analyst = "잘 모르겠어요";
        await vm.AskAnalystCommand.ExecuteAsync(null);
        Assert.Empty(vm.AnalystPicks);
        Assert.Contains("내지 못했어요", vm.AnalystStatus);
    }

    [Fact]
    public async Task Analyst_pick_on_tech_track_opens_planner_for_that_track_without_moving_schedule()
    {
        var (vm, store, ai) = await StartAsync();                              // 11/11 수요일 = C# 트랙
        Assert.StartsWith("C# · Windows 개발", vm.TrackName);
        await vm.AskAnalystCommand.ExecuteAsync(null);

        await vm.PlanAnalystPickCommand.ExecuteAsync(vm.AnalystPicks[0]);      // CS 기초
        Assert.StartsWith("CS 기초", vm.TrackName);
        Assert.Contains("분석가 추천", vm.TrackName);
        Assert.Equal("TCP 다음으로 HTTP 기초", vm.ExtraRequest);              // 비어 있던 추가 요청에 방향
        var plan = ai.Requests[^1];
        Assert.Contains("주제 기획자", plan.System);
        Assert.Contains("[트랙] CS 기초", plan.User);
        Assert.Contains("TCP 다음으로 HTTP 기초", plan.User);
        Assert.Equal(2, vm.TopicIdeas.Count);
        Assert.Empty(vm.AnalystPicks);

        vm.PickTopicIdeaCommand.Execute(vm.TopicIdeas[0]);
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Equal("cs", store.LoadState().History.Single().TrackKey);
        Assert.Empty(store.LoadState().Progress.Where(p => p.Value > 0));     // 커리큘럼·요일 일정 그대로
        Assert.StartsWith("C# · Windows 개발", vm.TrackName);
    }

    [Fact]
    public async Task Analyst_pick_on_life_track_switches_category_and_keeps_written_request()
    {
        var (vm, _, ai) = await StartAsync();
        await vm.AskAnalystCommand.ExecuteAsync(null);
        vm.ExtraRequest = "가성비 위주로";

        await vm.PlanAnalystPickCommand.ExecuteAsync(vm.AnalystPicks[1]);      // 맛집·카페
        Assert.Equal("food", vm.SelectedCategory!.Key);
        Assert.True(vm.IsMemoCategory);
        Assert.Equal("가성비 위주로", vm.ExtraRequest);                       // 직접 적은 요청은 덮지 않음
        Assert.Contains("[트랙] 맛집·카페", ai.Requests[^1].User);
    }
}

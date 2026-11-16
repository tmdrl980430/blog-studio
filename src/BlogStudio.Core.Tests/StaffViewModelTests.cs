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
        public string Name => "Fake";
        public Task<string?> CheckAsync(CancellationToken ct = default) => Task.FromResult<string?>("1.0");

        public Task<AiResult> GenerateAsync(AiRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            var text = request.System.Contains("주제 기획자")
                ? "파이썬 deque 시간복잡도 정리 | 파이썬 deque | BFS 다음 단계\n투 포인터 기본 패턴 | 투 포인터 | 코테 단골"
                : request.User.StartsWith("[검수할 글]") ? Review
                : Fixtures.SampleResponse;
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

    private static async Task<(MainViewModel Vm, LocalStore Store, StaffProvider Ai)> StartAsync()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-staff-").FullName);
        var ai = new StaffProvider();
        var vm = new MainViewModel(Fixtures.Themes(), store, ai, new NoClipboard(), new NoBrowser(),
            () => new DateOnly(2026, 11, 11));
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
}

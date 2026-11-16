using BlogStudio.Core.Posts;
using BlogStudio.Core.Staff;

namespace BlogStudio.Core.Tests;

public class StaffTests
{
    [Fact]
    public void Shared_staff_prompts_load_without_leading_comment()
    {
        var themes = Fixtures.Themes();
        var planner = themes.LoadStaffPrompt("planner");
        var editor = themes.LoadStaffPrompt("editor");
        Assert.StartsWith("당신은", planner);
        Assert.StartsWith("당신은", editor);
        Assert.Contains("{author}", editor);
        Assert.Equal("", themes.LoadStaffPrompt("no-such-staff"));
    }

    [Fact]
    public void Topic_ideas_prompt_has_track_and_recent_titles_only_from_that_track()
    {
        var theme = Fixtures.Themes().Load("tech");
        var track = theme.Curriculum["algorithm"];
        var history = new[]
        {
            new HistoryEntry(new DateOnly(2026, 11, 1), "algorithm", "BFS", "BFS로 최단 거리", "a.md"),
            new HistoryEntry(new DateOnly(2026, 11, 2), "cs", "TCP", "TCP 3-way handshake", "b.md"),
        };
        var p = StaffPrompts.BuildTopicIdeas("기획자 지시", theme, track, history, "초보용으로");
        Assert.Equal("기획자 지시", p.System);
        Assert.Contains(track.Name, p.User);
        Assert.Contains("BFS로 최단 거리", p.User);
        Assert.DoesNotContain("TCP 3-way", p.User);
        Assert.Contains("[요청] 초보용으로", p.User);

        Assert.Contains("주제 기획자", StaffPrompts.BuildTopicIdeas("", theme, track, history).System);   // 파일이 없어도 동작
    }

    [Fact]
    public void Parses_topic_ideas_lines()
    {
        var ideas = StaffPrompts.ParseTopicIdeas("""
            주제 | 핵심 검색 키워드 | 이유
            1. 파이썬 deque와 list 속도 차이 | 파이썬 deque | 지난 BFS 글 다음 단계
            - "다익스트라 우선순위 큐 구현" | 다익스트라 파이썬 | 코테 단골
            이분 탐색 lower bound
            파이썬 deque와 list 속도 차이 | 중복 | 중복
            이 줄은 주제라고 하기엔 너무 길어서 육십 자를 넘어가 버리는 아주아주아주아주아주아주아주아주아주아주아주아주 긴 줄입니다
            """);
        Assert.Equal(3, ideas.Count);
        Assert.Equal(new TopicIdea("파이썬 deque와 list 속도 차이", "파이썬 deque", "지난 BFS 글 다음 단계"), ideas[0]);
        Assert.Equal("다익스트라 우선순위 큐 구현", ideas[1].Topic);
        Assert.Equal(new TopicIdea("이분 탐색 lower bound", "", ""), ideas[2]);
        Assert.Equal("이분 탐색 lower bound", ideas[2].Label);
    }

    [Fact]
    public void Editor_prompt_fills_author()
    {
        var p = StaffPrompts.BuildEditorReview("{author}의 글을 검수", "---\ntitle: x\n---\n본문", "승기");
        Assert.Equal("승기의 글을 검수", p.System);
        Assert.Contains("본문", p.User);
    }

    [Fact]
    public void Parses_editor_review()
    {
        var review = StaffPrompts.ParseEditorReview("""
            **판정: 수정 후 발행**
            - 첫 문단이 질문에 바로 답하지 않음 — 결론을 앞으로
            - 2번째 소제목의 가격은 (확인 필요)로
            """);
        Assert.Equal(StaffPrompts.NeedsEdit, review.Verdict);
        Assert.Equal(2, review.Notes.Count);
        Assert.True(review.NeedsFix);
        Assert.Contains("- 2번째 소제목의 가격은 (확인 필요)로", StaffPrompts.RevisionFromReview(review));
    }

    [Fact]
    public void Editor_review_verdict_is_consistent_with_notes()
    {
        var clean = StaffPrompts.ParseEditorReview("판정: 발행 가능");
        Assert.Equal(StaffPrompts.Publishable, clean.Verdict);
        Assert.False(clean.NeedsFix);

        // 발행 가능이라면서 고칠 점이 있으면 수정 후 발행으로
        Assert.Equal(StaffPrompts.NeedsEdit, StaffPrompts.ParseEditorReview("판정: 발행 가능\n- 오타 하나").Verdict);

        // 형식 안내를 그대로 따라 쓴 판정 줄은 무시하고 고칠 점으로 정함
        Assert.Equal(StaffPrompts.Publishable, StaffPrompts.ParseEditorReview("판정: 발행 가능 | 수정 후 발행 | 다시 쓰기").Verdict);

        Assert.Equal(StaffPrompts.Rewrite, StaffPrompts.ParseEditorReview("판정: 다시 쓰기\n- 주제와 본문이 맞지 않음").Verdict);

        var many = StaffPrompts.ParseEditorReview(string.Join("\n", Enumerable.Range(1, 10).Select(i => $"- 의견 {i}")));
        Assert.Equal(StaffPrompts.NoteMax, many.Notes.Count);
    }
}

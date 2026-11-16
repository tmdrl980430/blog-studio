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
    public void Planner_prompt_asks_for_category_as_fourth_column()
    {
        var planner = Fixtures.Themes().LoadStaffPrompt("planner");
        var format = planner.Split('\n').Select(l => l.Trim()).Last(l => l.Length > 0);
        var columns = format.Split('|').Select(c => c.Trim()).ToArray();
        Assert.Equal(4, columns.Length);
        Assert.Equal("주제", columns[0]);
        Assert.Equal("카테고리", columns[3]);
        Assert.Contains("[카테고리 목록]", planner);
        Assert.Contains("[이 트랙 기본 카테고리]", planner);
    }

    [Fact]
    public void Topic_ideas_prompt_lists_categories_and_track_default()
    {
        var theme = Fixtures.Themes().Load("tech");
        var track = theme.Curriculum["algorithm"];
        var p = StaffPrompts.BuildTopicIdeas("", theme, track, Array.Empty<HistoryEntry>());
        Assert.Contains("[카테고리 목록]\n- 개발 > 알고리즘·코테\n- 개발 > CS 기초", p.User);
        Assert.Contains("[이 트랙 기본 카테고리] 개발 > 알고리즘·코테", p.User);
        Assert.Contains("카테고리", p.System);   // 지시문 파일이 없어도 카테고리를 고르게 함

        var custom = BlogCategories.FromLines(new[] { "공부 > 코테" });
        var q = StaffPrompts.BuildTopicIdeas("", theme, track, Array.Empty<HistoryEntry>(), null, custom);
        Assert.Contains("- 공부 > 코테", q.User);
        Assert.DoesNotContain("CS 기초", q.User);
        Assert.Contains("[이 트랙 기본 카테고리] (없음", q.User);   // 목록에서 지운 트랙 기본값은 쓰지 않음
    }

    [Fact]
    public void Parses_topic_idea_category_only_from_the_list()
    {
        var ideas = StaffPrompts.ParseTopicIdeas("""
            주제 | 핵심 검색 키워드 | 이유 | 카테고리
            파이썬 deque와 list 속도 차이 | 파이썬 deque | 지난 BFS 글 다음 단계 | 개발 > 알고리즘·코테
            TCP 혼잡 제어 정리 | TCP 혼잡 제어 | 면접 단골 | CS 기초
            블록체인 입문 | 블록체인 | 요즘 관심 | 개발 > 블록체인
            다익스트라 구현 | 다익스트라 | 코테 단골
            """);
        Assert.Equal(4, ideas.Count);
        Assert.Equal("개발 > 알고리즘·코테", ideas[0].Category);
        Assert.Equal("개발 > CS 기초", ideas[1].Category);   // 하위 이름만 와도 맞춤
        Assert.Equal("", ideas[2].Category);                 // 목록에 없으면 만들지 않음 → 트랙 기본값
        Assert.Equal("", ideas[3].Category);                 // 예전 3칸 형식도 그대로 읽음

        var custom = BlogCategories.FromLines(new[] { "공부 > 코테" });
        Assert.Equal("공부 > 코테", StaffPrompts.ParseTopicIdeas("주제 A | 키 | 이유 | 코테", custom)[0].Category);
        Assert.Equal("", StaffPrompts.ParseTopicIdeas("주제 A | 키 | 이유 | 개발 > CS 기초", custom)[0].Category);
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

public class PublishGateTests
{
    private static PostDraft Post(string body, string title = "파이썬 deque 시간복잡도 정리") =>
        new(title, new[] { "파이썬" }, "", body, "t", "algorithm", new DateOnly(2026, 11, 11));

    private static readonly string Body = string.Concat(Enumerable.Repeat("deque는 양쪽 끝 연산이 상수 시간입니다. ", 60));

    [Fact]
    public void Passes_only_clean_reviewed_posts()
    {
        Assert.True(PublishGate.Check(Post(Body), StaffPrompts.NeedsEdit, notesLeft: false, hasCodeErrors: false).Ok);
        Assert.False(PublishGate.Check(Post(Body), null, false, false).Ok);
        Assert.False(PublishGate.Check(Post(Body), StaffPrompts.Rewrite, false, false).Ok);
        Assert.False(PublishGate.Check(Post(Body), StaffPrompts.Publishable, notesLeft: true, false).Ok);
        Assert.False(PublishGate.Check(Post(Body), StaffPrompts.Publishable, false, hasCodeErrors: true).Ok);

        var unknown = PublishGate.Check(Post(Body + "\n가격은 (확인 필요)"), StaffPrompts.Publishable, false, false);
        Assert.False(unknown.Ok);
        Assert.Contains("확인 필요", unknown.Reason);
        Assert.False(PublishGate.Check(Post(Body + "\n[사진 1: 외관]"), StaffPrompts.Publishable, false, false).Ok);
        Assert.Contains("너무 짧음", PublishGate.Check(Post("짧은 글"), StaffPrompts.Publishable, false, false).Reason);
    }
}

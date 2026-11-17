using BlogStudio.Core.Posts;
using BlogStudio.Core.Staff;
using BlogStudio.Core.Stats;

namespace BlogStudio.Core.Tests;

public class AnalystTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    private static readonly KeyValuePair<string, string>[] Tracks =
    {
        new("algorithm", "알고리즘·코테"),
        new("cs", "CS 기초"),
        new("food", "맛집·카페"),
    };

    private static readonly HistoryEntry[] History =
    {
        new(new DateOnly(2026, 11, 16), "algorithm", "BFS", "BFS로 최단 거리", "a.md", new[] { "naver" }),
        new(new DateOnly(2026, 11, 10), "algorithm", "DFS", "DFS 정리", "b.md"),
        new(new DateOnly(2026, 11, 1), "cs", "TCP", "TCP 3-way handshake", "c.md", new[] { "naver", "tistory" }),
        new(new DateOnly(2026, 10, 1), "old", "지운 트랙", "지운 트랙 글", "d.md"),
    };

    [Fact]
    public void Shared_analyst_prompt_loads_and_asks_for_three_columns()
    {
        var analyst = Fixtures.Themes().LoadStaffPrompt("analyst");
        Assert.StartsWith("당신은", analyst);
        Assert.Contains("[트랙별 기록]", analyst);
        var format = analyst.Split('\n').Select(l => l.Trim()).Last(l => l.Length > 0);
        var columns = format.Split('|').Select(c => c.Trim()).ToArray();
        Assert.Equal(3, columns.Length);
        Assert.Equal("트랙", columns[0]);
        Assert.Equal("다음 주제 방향", columns[1]);
    }

    [Fact]
    public void Records_count_posts_per_track_including_empty_tracks()
    {
        var records = AnalystPrompts.Records(History, Tracks);
        Assert.Equal(new[] { "algorithm", "cs", "food" }, records.Select(r => r.Key));   // 지운 트랙은 빠짐, 순서 유지
        Assert.Equal(new TrackRecord("algorithm", "알고리즘·코테", 2, 1, new DateOnly(2026, 11, 16)), records[0]);
        Assert.Equal(new TrackRecord("cs", "CS 기초", 1, 1, new DateOnly(2026, 11, 1)), records[1]);
        Assert.Equal(new TrackRecord("food", "맛집·카페", 0, 0, null), records[2]);
    }

    [Fact]
    public void Prompt_has_totals_and_track_lines()
    {
        var stats = BlogStats.From(History, Today, k => k);
        var records = AnalystPrompts.Records(History, Tracks);
        var p = AnalystPrompts.Build("분석가 지시", stats, records, Today, "이번 주는 생활 위주");
        Assert.Equal("분석가 지시", p.System);
        Assert.Contains("[전체] 글 4편", p.User);
        Assert.Contains("아직 안 올린 글 2편", p.User);
        Assert.Contains("- 알고리즘·코테: 글 2편 · 올림 1편 · 마지막 글 1일 전", p.User);
        Assert.Contains("- CS 기초: 글 1편 · 올림 1편 · 마지막 글 16일 전", p.User);
        Assert.Contains("- 맛집·카페: 아직 글 없음", p.User);
        Assert.Contains("[요청] 이번 주는 생활 위주", p.User);

        var fallback = AnalystPrompts.Build("", stats, records, Today);
        Assert.Contains("성과 분석가", fallback.System);   // 지시문 파일이 없어도 동작
        Assert.DoesNotContain("[요청]", fallback.User);
    }

    [Fact]
    public void Parses_priorities_only_for_known_tracks()
    {
        var records = AnalystPrompts.Records(History, Tracks);
        var picks = AnalystPrompts.Parse("""
            트랙 | 다음 주제 방향 | 이유
            1. CS 기초 | 운영체제 프로세스·스레드 | 16일째 쉬는 중
            - **알고리즘 · 코테 | 다익스트라 최단 경로 | 꾸준히 올리는 트랙
            블록체인 | 비트코인 입문 | 새 트랙
            CS 기초 | 중복 | 중복
            맛집·카페
            food | 동네 카페 메모 모으기 | 아직 글 없음
            algorithm | 넘침 | 세 개 넘음
            """, records);
        Assert.Equal(3, picks.Count);
        Assert.Equal(new TrackPriority("cs", "CS 기초", "운영체제 프로세스·스레드", "16일째 쉬는 중"), picks[0]);
        Assert.Equal("algorithm", picks[1].TrackKey);           // 공백·가운뎃점이 달라도 맞춤
        Assert.Equal("다익스트라 최단 경로", picks[1].Direction);
        Assert.Equal("food", picks[2].TrackKey);                // 키로 와도 맞춤
        Assert.Equal("맛집·카페", picks[2].TrackName);

        Assert.Empty(AnalystPrompts.Parse("", records));
        Assert.Empty(AnalystPrompts.Parse("블록체인 | 입문 | 새 트랙", records));   // 목록 밖 트랙은 만들지 않음
    }
}

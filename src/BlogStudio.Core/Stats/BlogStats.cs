using BlogStudio.Core.Posts;

namespace BlogStudio.Core.Stats;

public sealed record CategoryCount(string Name, int Count, int Published);

public sealed record DayCount(DateOnly Date, int Count);

/// <summary>글 기록으로 보는 블로그 성과 (조회수는 블로그 쪽 통계라 여기서는 "쓰고 올린 것"만).</summary>
public sealed record BlogStats(
    int Total, int ThisMonth, int ThisWeek, int StreakDays, int BestStreak,
    int PublishedNaver, int PublishedTistory, int Unpublished,
    IReadOnlyList<CategoryCount> ByCategory, IReadOnlyList<DayCount> Last30Days)
{
    public static BlogStats From(IReadOnlyList<HistoryEntry> history, DateOnly today, Func<string, string> categoryName)
    {
        var days = history.Select(h => h.Date).ToHashSet();
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        // 연속 기록: 오늘 아직 안 썼으면 어제부터 센다
        var streak = 0;
        for (var d = days.Contains(today) ? today : today.AddDays(-1); days.Contains(d); d = d.AddDays(-1)) streak++;

        var best = 0;
        var run = 0;
        DateOnly? prev = null;
        foreach (var d in days.OrderBy(d => d))
        {
            run = prev is { } p && p.AddDays(1) == d ? run + 1 : 1;
            best = Math.Max(best, run);
            prev = d;
        }

        bool Any(HistoryEntry h) => h.PublishedTo is { Count: > 0 };
        return new BlogStats(
            history.Count,
            history.Count(h => h.Date.Year == today.Year && h.Date.Month == today.Month),
            history.Count(h => h.Date >= monday && h.Date <= today),
            streak, best,
            history.Count(h => h.IsPublishedTo("naver")),
            history.Count(h => h.IsPublishedTo("tistory")),
            history.Count(h => !Any(h)),
            history.GroupBy(h => categoryName(h.TrackKey))
                .Select(g => new CategoryCount(g.Key, g.Count(), g.Count(Any)))
                .OrderByDescending(c => c.Count).ThenBy(c => c.Name).ToList(),
            Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29))
                .Select(d => new DayCount(d, history.Count(h => h.Date == d))).ToList());
    }
}

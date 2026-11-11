using BlogStudio.Core.Themes;

namespace BlogStudio.Core.Scheduling;

public sealed record PlannedTopic(DateOnly Date, Track Track, string? Topic, int Index)
{
    public bool IsCurriculumExhausted => Topic is null;
    public string DayLabel => Date.DayOfWeek switch
    {
        DayOfWeek.Monday => "월", DayOfWeek.Tuesday => "화", DayOfWeek.Wednesday => "수",
        DayOfWeek.Thursday => "목", DayOfWeek.Friday => "금", DayOfWeek.Saturday => "토", _ => "일",
    };
}

/// <summary>요일별 트랙과 커리큘럼 진행 위치로 "오늘/다음에 쓸 주제"를 정한다.</summary>
public sealed class TopicPlanner
{
    private readonly ThemePack _theme;
    private readonly IReadOnlyDictionary<string, string> _schedule;

    public TopicPlanner(ThemePack theme, IReadOnlyDictionary<string, string>? scheduleOverride = null)
    {
        _theme = theme;
        _schedule = scheduleOverride is { Count: > 0 } ? scheduleOverride : theme.DefaultSchedule;
    }

    internal static string DayKey(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "mon", DayOfWeek.Tuesday => "tue", DayOfWeek.Wednesday => "wed",
        DayOfWeek.Thursday => "thu", DayOfWeek.Friday => "fri", DayOfWeek.Saturday => "sat", _ => "sun",
    };

    /// <summary>그 날의 트랙. 쉬는 날이거나 트랙이 없으면 null.</summary>
    public Track? TrackFor(DateOnly date)
    {
        if (!_schedule.TryGetValue(DayKey(date.DayOfWeek), out var key) || key == "rest") return null;
        return _theme.Curriculum.GetValueOrDefault(key);
    }

    public PlannedTopic? Plan(DateOnly date, Func<string, int> nextIndex) =>
        TrackFor(date) is { } track ? Make(date, track, nextIndex(track.Key)) : null;

    /// <summary>오늘 다음날부터 count일 동안 쓸 주제 (그 사이 진행이 한 칸씩 넘어간다고 가정).</summary>
    public IReadOnlyList<PlannedTopic> Upcoming(DateOnly today, Func<string, int> nextIndex, int count, bool includeToday = false)
    {
        var offsets = new Dictionary<string, int>();
        var result = new List<PlannedTopic>();
        for (var day = 0; result.Count < count && day < count * 7 + 7; day++)
        {
            var date = today.AddDays(day);
            if (TrackFor(date) is not { } track) continue;
            var offset = offsets.GetValueOrDefault(track.Key);
            offsets[track.Key] = offset + 1;
            if (day == 0 && !includeToday) continue;
            result.Add(Make(date, track, nextIndex(track.Key) + offset));
        }
        return result;
    }

    private static PlannedTopic Make(DateOnly date, Track track, int index) =>
        new(date, track, index < track.Topics.Count ? track.Topics[index] : null, index);
}

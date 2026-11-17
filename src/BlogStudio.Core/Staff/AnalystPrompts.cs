using System.Text;
using BlogStudio.Core.Posts;
using BlogStudio.Core.Prompting;
using BlogStudio.Core.Stats;

namespace BlogStudio.Core.Staff;

/// <summary>성과 분석가에게 보내는 트랙 한 줄 (글 기록으로 센 값).</summary>
public sealed record TrackRecord(string Key, string Name, int Posts, int Published, DateOnly? LastDate);

/// <summary>성과 분석가가 낸 우선순위 하나. TrackKey는 보낸 트랙 목록 안의 값.</summary>
public sealed record TrackPriority(string TrackKey, string TrackName, string Direction, string Reason);

/// <summary>
/// 성과 분석가(shared/staff/analyst.md): 통계(트랙별 글 수·올림 표시·마지막 글)를 보고
/// 다음에 힘줄 트랙과 주제 방향을 최대 3개 낸다. 조회수는 앱에 없으므로 보내지 않는다.
/// </summary>
public static class AnalystPrompts
{
    public const int PriorityMax = 3;

    private const string Fallback =
        "당신은 한국어 블로그의 성과 분석가입니다. [트랙별 기록]의 숫자만 근거로, 다음에 글을 더 쓸 트랙을 우선순위 순서로 최대 3줄 \"트랙 | 다음 주제 방향 | 이유\" 형식으로만 출력합니다. 트랙 이름은 목록에 있는 그대로 씁니다.";

    /// <summary>트랙별 글 수·올림 수·마지막 글 날짜. 글이 없는 트랙도 0편으로 넣고, tracks 순서를 따른다.
    /// 목록에 없는 트랙(지운 트랙)의 글은 세지 않는다.</summary>
    public static IReadOnlyList<TrackRecord> Records(IEnumerable<HistoryEntry> history,
        IEnumerable<KeyValuePair<string, string>> tracks)
    {
        var byKey = history.GroupBy(h => h.TrackKey).ToDictionary(g => g.Key, g => g.ToList());
        return tracks
            .DistinctBy(t => t.Key)
            .Select(t => byKey.TryGetValue(t.Key, out var list)
                ? new TrackRecord(t.Key, t.Value, list.Count, list.Count(h => h.PublishedTo is { Count: > 0 }), list.Max(h => h.Date))
                : new TrackRecord(t.Key, t.Value, 0, 0, null))
            .ToList();
    }

    public static PromptPair Build(string staffPrompt, BlogStats stats, IReadOnlyList<TrackRecord> tracks,
        DateOnly today, string? hint = null)
    {
        var user = new StringBuilder()
            .Append("[오늘] ").Append(today.ToString("yyyy-MM-dd"))
            .Append("\n[전체] 글 ").Append(stats.Total).Append("편 · 이번 주 ").Append(stats.ThisWeek)
            .Append("편 · 이번 달 ").Append(stats.ThisMonth).Append("편 · 최근 30일 ").Append(stats.Last30Days.Sum(d => d.Count))
            .Append("편 · 연속 ").Append(stats.StreakDays).Append("일 (최고 ").Append(stats.BestStreak).Append("일)")
            .Append("\n[올림] 네이버 ").Append(stats.PublishedNaver).Append("편 · 티스토리 ").Append(stats.PublishedTistory)
            .Append("편 · 아직 안 올린 글 ").Append(stats.Unpublished).Append("편")
            .Append("\n\n[트랙별 기록]\n")
            .Append(tracks.Count > 0 ? string.Join("\n", tracks.Select(t => "- " + Line(t, today))) : "(없음)");
        if (!string.IsNullOrWhiteSpace(hint))
            user.Append("\n\n[요청] ").Append(hint.Trim());
        return new PromptPair(string.IsNullOrWhiteSpace(staffPrompt) ? Fallback : staffPrompt, user.ToString());
    }

    private static string Line(TrackRecord t, DateOnly today)
    {
        if (t.LastDate is not { } last) return $"{t.Name}: 아직 글 없음";
        var ago = today.DayNumber - last.DayNumber;
        var when = ago <= 0 ? "오늘" : $"{ago}일 전";
        return $"{t.Name}: 글 {t.Posts}편 · 올림 {t.Published}편 · 마지막 글 {when}";
    }

    /// <summary>
    /// "트랙 | 방향 | 이유" 줄을 읽는다. 트랙은 보낸 목록 안의 이름(공백·가운뎃점 무시)이나 키로 맞추고,
    /// 목록에 없는 트랙은 버린다(새로 만들지 않음). 같은 트랙은 처음 것만, 최대 3개.
    /// </summary>
    public static IReadOnlyList<TrackPriority> Parse(string response, IReadOnlyList<TrackRecord> tracks)
    {
        var picks = new List<TrackPriority>();
        foreach (var raw in response.Split('\n'))
        {
            var line = StaffPrompts.StripListMark(raw.Trim().Trim('*').Trim()).Trim('*').Trim();
            if (line.Length == 0 || line.StartsWith("트랙 |", StringComparison.Ordinal)) continue;
            var parts = line.Split('|').Select(p => p.Trim().Trim('*', '"', '“', '”', '\'').Trim()).ToArray();
            if (parts.Length < 2 || parts[1].Length == 0) continue;
            var track = Match(parts[0], tracks);
            if (track is null || picks.Any(p => p.TrackKey == track.Key)) continue;
            picks.Add(new TrackPriority(track.Key, track.Name, parts[1], parts.Length > 2 ? parts[2] : ""));
            if (picks.Count == PriorityMax) break;
        }
        return picks;
    }

    private static TrackRecord? Match(string name, IReadOnlyList<TrackRecord> tracks)
    {
        var n = Normalize(name);
        if (n.Length == 0) return null;
        return tracks.FirstOrDefault(t => Normalize(t.Name) == n)
            ?? tracks.FirstOrDefault(t => string.Equals(t.Key, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string s) =>
        new(s.Where(c => !char.IsWhiteSpace(c) && c is not ('·' or '•' or '・' or '/' or ',')).ToArray());
}

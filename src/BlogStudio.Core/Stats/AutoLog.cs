using System.Globalization;

namespace BlogStudio.Core.Stats;

/// <summary>자동 발행 한 줄의 결과 종류 (통계 창 색·아이콘용).</summary>
public enum AutoLogOutcome
{
    /// <summary>고른 블로그 모두 발행 성공.</summary>
    Published,
    /// <summary>일부만 성공 (예: 티스토리 ✓, 네이버 실패).</summary>
    Partial,
    /// <summary>발행 기준(PublishGate)에 걸려 보류.</summary>
    Held,
    /// <summary>자동 발행이 꺼져 있어 작성·검수만 함.</summary>
    Off,
    /// <summary>발행 실패·주소 없음 등.</summary>
    Failed,
    /// <summary>자동 작성을 건너뜀 (오늘 글 있음·AI 로그인 필요·쉬는 날 등).</summary>
    Skipped,
    /// <summary>형식을 알 수 없는 줄 (원문 그대로 보여 줌).</summary>
    Other,
}

/// <summary>auto-log.txt 한 줄. 형식: "yyyy-MM-dd HH:mm 제목 | 편집자 판정 | 결과".</summary>
public sealed record AutoLogEntry(DateTime? Time, string Title, string Editor, string Result, AutoLogOutcome Outcome)
{
    public string TimeLabel => Time is { } t ? t.ToString("M/d HH:mm", CultureInfo.InvariantCulture) : "";
}

/// <summary>매일 자동 작성·발행 기록(auto-log.txt) 읽기·쓰기. 앱은 이 파일에 한 줄씩 덧붙이기만 한다.</summary>
public static class AutoLog
{
    public const string FileName = "auto-log.txt";
    private const string EditorPrefix = "편집자 ";

    public static string PathIn(string root) => Path.Combine(root, FileName);

    /// <summary>한 줄 덧붙이기. 기록 실패는 무시한다(자동 작성 흐름을 막지 않음).</summary>
    public static void Append(string root, string line, DateTime now)
    {
        try
        {
            File.AppendAllText(PathIn(root), $"{now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} {line}\n");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>최근 기록부터 최대 <paramref name="max"/>줄. 파일이 없거나 못 읽으면 빈 목록.</summary>
    public static IReadOnlyList<AutoLogEntry> ReadRecent(string root, int max = 20)
    {
        string text;
        try
        {
            var path = PathIn(root);
            if (!File.Exists(path)) return Array.Empty<AutoLogEntry>();
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return Array.Empty<AutoLogEntry>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<AutoLogEntry>();
        }
        return Parse(text).Reverse().Take(Math.Max(0, max)).ToList();
    }

    /// <summary>파일 내용 → 줄 목록 (파일 순서 그대로, 빈 줄은 버림).</summary>
    public static IEnumerable<AutoLogEntry> Parse(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length > 0) yield return ParseLine(line);
        }
    }

    public static AutoLogEntry ParseLine(string line)
    {
        DateTime? time = null;
        var rest = line;
        if (line.Length >= 16 && DateTime.TryParseExact(line[..16], "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
        {
            time = t;
            rest = line[16..].Trim();
        }

        // 제목에 " | "가 들어갈 수 있으니 뒤에서부터 나눈다: 마지막 = 결과, 그 앞 = "편집자 …"
        var parts = rest.Split(" | ");
        if (parts.Length >= 3 && parts[^2].StartsWith(EditorPrefix, StringComparison.Ordinal))
        {
            var title = string.Join(" | ", parts[..^2]).Trim();
            var editor = parts[^2][EditorPrefix.Length..].Trim();
            var result = parts[^1].Trim();
            return new AutoLogEntry(time, title, editor, result, Classify(result));
        }
        return new AutoLogEntry(time, rest, "", "", AutoLogOutcome.Other);
    }

    /// <summary>건너뜀·실패 원인을 남길 때 결과 칸 앞에 붙는 말.</summary>
    public const string SkippedPrefix = "건너뜀 — ";

    /// <summary>
    /// 한 줄로 쓸 결과 문구 만들기: 줄바꿈과 " | "(칸 구분자)를 지워 읽을 때 칸이 어긋나지 않게 한다.
    /// </summary>
    public static string Line(string title, string? editor, string result) =>
        $"{Clean(title)} | {EditorPrefix}{Clean(editor ?? "-")} | {Clean(result)}";

    private static string Clean(string text) =>
        string.Join(" ", text.Replace(" | ", " / ").Split('\n', '\r').Select(s => s.Trim()).Where(s => s.Length > 0));

    /// <summary>
    /// 최근 <paramref name="hours"/>시간 동안 자동 발행 성공(일부 성공 포함)이 0건이면 경고 문구, 아니면 null.
    /// 기록이 하나도 없으면(아직 자동 작성을 돌린 적 없음) 경고하지 않는다.
    /// 마지막 기록도 그보다 오래됐으면 "자동 작성이 실행되지 않음"(PC 꺼짐·작업 스케줄러)으로 알린다.
    /// </summary>
    public static string? NoPublishWarning(IEnumerable<AutoLogEntry> entries, DateTime now, int hours = 24)
    {
        var timed = entries.Where(e => e.Time is not null).OrderByDescending(e => e.Time).ToList();
        if (timed.Count == 0) return null;
        var since = now.AddHours(-hours);
        if (timed.Any(e => e.Time >= since && e.Outcome is AutoLogOutcome.Published or AutoLogOutcome.Partial)) return null;

        var latest = timed[0];
        var head = $"최근 {hours}시간 자동 발행 0건";
        if (latest.Time < since)
            return $"{head} — {latest.TimeLabel} 이후 자동 작성이 실행되지 않았어요 (PC 전원·작업 스케줄러를 확인해 주세요)";
        var cause = latest.Outcome == AutoLogOutcome.Other ? latest.Title : latest.Result;
        return $"{head} — 마지막 기록({latest.TimeLabel}): {cause}";
    }

    /// <summary>결과 문구 → 종류. MainViewModel이 쓰는 문구 기준.</summary>
    public static AutoLogOutcome Classify(string result)
    {
        if (result.StartsWith("발행 보류", StringComparison.Ordinal)) return AutoLogOutcome.Held;
        if (result.StartsWith(SkippedPrefix, StringComparison.Ordinal)) return AutoLogOutcome.Skipped;
        if (result.EndsWith("자동 발행 꺼짐", StringComparison.Ordinal)) return AutoLogOutcome.Off;
        var pieces = result.Split(", ");
        var ok = pieces.Count(p => p.EndsWith("발행 ✓", StringComparison.Ordinal));
        if (ok > 0) return ok == pieces.Length ? AutoLogOutcome.Published : AutoLogOutcome.Partial;
        return AutoLogOutcome.Failed;
    }
}

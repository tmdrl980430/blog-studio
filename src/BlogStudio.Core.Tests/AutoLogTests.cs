using BlogStudio.Core.Stats;

namespace BlogStudio.Core.Tests;

public class AutoLogTests
{
    [Fact]
    public void Parses_time_title_editor_and_result()
    {
        var e = AutoLog.ParseLine("2026-11-18 07:05 BFS 최단거리 정리 | 편집자 발행 가능 | 티스토리 발행 ✓");
        Assert.Equal(new DateTime(2026, 11, 18, 7, 5, 0), e.Time);
        Assert.Equal("BFS 최단거리 정리", e.Title);
        Assert.Equal("발행 가능", e.Editor);
        Assert.Equal("티스토리 발행 ✓", e.Result);
        Assert.Equal(AutoLogOutcome.Published, e.Outcome);
        Assert.Equal("11/18 07:05", e.TimeLabel);
    }

    [Fact]
    public void Title_with_pipe_is_kept_whole()
    {
        var e = AutoLog.ParseLine("2026-11-18 07:05 A | B 비교 | 편집자 - | 발행 보류 — 본문이 짧아요");
        Assert.Equal("A | B 비교", e.Title);
        Assert.Equal("-", e.Editor);
        Assert.Equal(AutoLogOutcome.Held, e.Outcome);
    }

    [Theory]
    [InlineData("티스토리 발행 ✓, 네이버 발행 ✓", AutoLogOutcome.Published)]
    [InlineData("티스토리 발행 ✓, 네이버 발행 실패 (창에서 확인)", AutoLogOutcome.Partial)]
    [InlineData("네이버 발행 실패 (창에서 확인)", AutoLogOutcome.Failed)]
    [InlineData("네이버 주소 없음", AutoLogOutcome.Failed)]
    [InlineData("발행할 블로그를 고르지 않음", AutoLogOutcome.Failed)]
    [InlineData("발행 보류 — 편집자 다시 쓰기", AutoLogOutcome.Held)]
    [InlineData("발행 기준 통과 · 자동 발행 꺼짐", AutoLogOutcome.Off)]
    [InlineData("건너뜀 — 오늘 글이 이미 있음", AutoLogOutcome.Skipped)]
    [InlineData("글 생성 실패 — Claude Code 응답 없음", AutoLogOutcome.Failed)]
    public void Classifies_result_text(string result, AutoLogOutcome expected) =>
        Assert.Equal(expected, AutoLog.Classify(result));

    [Fact]
    public void Unknown_line_is_shown_as_is()
    {
        var e = AutoLog.ParseLine("손으로 적은 메모");
        Assert.Null(e.Time);
        Assert.Equal("손으로 적은 메모", e.Title);
        Assert.Equal(AutoLogOutcome.Other, e.Outcome);
    }

    [Fact]
    public void Read_recent_returns_newest_first_and_handles_missing_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "bs-autolog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Empty(AutoLog.ReadRecent(root));
            AutoLog.Append(root, "첫 글 | 편집자 발행 가능 | 티스토리 발행 ✓", new DateTime(2026, 11, 17, 7, 0, 0));
            File.AppendAllText(AutoLog.PathIn(root), "\r\n");
            AutoLog.Append(root, "둘째 글 | 편집자 고쳐서 발행 | 발행 보류 — 의견 반영 실패", new DateTime(2026, 11, 18, 7, 0, 0));
            AutoLog.Append(root, "셋째 글 | 편집자 발행 가능 | 발행 기준 통과 · 자동 발행 꺼짐", new DateTime(2026, 11, 19, 7, 0, 0));

            var all = AutoLog.ReadRecent(root);
            Assert.Equal(new[] { "셋째 글", "둘째 글", "첫 글" }, all.Select(e => e.Title));
            var two = AutoLog.ReadRecent(root, 2);
            Assert.Equal(2, two.Count);
            Assert.Equal(AutoLogOutcome.Off, two[0].Outcome);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Line_keeps_columns_when_reason_has_pipe_or_newline()
    {
        var line = AutoLog.Line("매일 자동 작성", null, AutoLog.SkippedPrefix + "로그인 | 만료\n다시 확인");
        Assert.Equal("매일 자동 작성 | 편집자 - | 건너뜀 — 로그인 / 만료 다시 확인", line);
        var e = AutoLog.ParseLine("2026-11-21 07:00 " + line);
        Assert.Equal("매일 자동 작성", e.Title);
        Assert.Equal(AutoLogOutcome.Skipped, e.Outcome);
    }

    private static readonly DateTime Now = new(2026, 11, 22, 9, 0, 0);

    private static AutoLogEntry[] Log(params string[] lines) => lines.Select(AutoLog.ParseLine).ToArray();

    [Fact]
    public void No_warning_without_records_or_with_recent_publish()
    {
        Assert.Null(AutoLog.NoPublishWarning(Array.Empty<AutoLogEntry>(), Now));
        Assert.Null(AutoLog.NoPublishWarning(Log(
            "2026-11-21 10:00 글 | 편집자 발행 가능 | 티스토리 발행 ✓, 네이버 발행 실패 (창에서 확인)",
            "2026-11-22 07:00 매일 자동 작성 | 편집자 - | 건너뜀 — 오늘 글이 이미 있음"), Now));
    }

    [Fact]
    public void Warns_with_latest_cause_when_no_publish_in_a_day()
    {
        var w = AutoLog.NoPublishWarning(Log(
            "2026-11-20 07:00 글 | 편집자 발행 가능 | 티스토리 발행 ✓",
            "2026-11-22 07:00 BFS | 편집자 - | 티스토리 발행 실패 (창에서 확인)"), Now);
        Assert.Equal("최근 24시간 자동 발행 0건 (발행 실패) — 마지막 기록(11/22 07:00): 티스토리 발행 실패 (창에서 확인)", w);
    }

    [Fact]
    public void Warns_that_auto_run_did_not_happen_when_log_is_stale()
    {
        var w = AutoLog.NoPublishWarning(Log("2026-11-19 07:40 WPF async | 편집자 발행 가능 | 티스토리 발행 ✓"), Now);
        Assert.NotNull(w);
        Assert.Contains("11/19 07:40 이후 자동 작성이 실행되지 않았어요", w);
        Assert.Contains("(" + AutoLog.AppNotRun + ")", w);
    }

    [Theory]
    [InlineData("티스토리 로그인 필요 — 로그인이 풀려 있어 자동 발행을 멈췄어요", "로그인 필요")]
    [InlineData("티스토리 채우기 실패 — 본문 칸을 찾지 못했어요", "채우기 실패")]
    [InlineData("티스토리 발행 실패 — 발행 확인 버튼을 찾지 못했어요", "발행 실패")]
    [InlineData("티스토리 발행 실패 (창에서 확인)", "발행 실패")]
    [InlineData("네이버 채우기 실패 — 이미지를 모두 넣지 못해 발행하지 않았어요, 티스토리 발행 실패 — 발행 버튼을 찾지 못했어요", "채우기 실패")]
    [InlineData("네이버 주소 없음", "설정 확인")]
    [InlineData("글 생성 실패 — Claude Code 응답 없음", "글 생성 실패")]
    [InlineData("발행 보류 — 본문이 짧아요", "발행 기준 보류")]
    [InlineData("건너뜀 — 오늘은 쉬는 날 (요일 일정)", "자동 작성 건너뜀")]
    public void Cause_separates_login_fill_and_publish(string result, string cause)
    {
        var e = AutoLog.ParseLine("2026-11-22 07:00 글 | 편집자 - | " + result);
        Assert.Equal(cause, AutoLog.CauseOf(e));
    }

    [Fact]
    public void Warning_names_fill_failure_as_cause()
    {
        var w = AutoLog.NoPublishWarning(Log(
            "2026-11-22 07:10 BFS | 편집자 발행 가능 | 티스토리 채우기 실패 — 본문 칸을 찾지 못했어요"), Now);
        Assert.Equal("최근 24시간 자동 발행 0건 (채우기 실패) — 마지막 기록(11/22 07:10): 티스토리 채우기 실패 — 본문 칸을 찾지 못했어요", w);
    }
}

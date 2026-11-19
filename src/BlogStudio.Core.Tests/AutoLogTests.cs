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
}

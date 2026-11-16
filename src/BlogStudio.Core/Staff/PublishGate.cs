using BlogStudio.Core.Posts;
using BlogStudio.Core.Review;

namespace BlogStudio.Core.Staff;

public sealed record GateResult(bool Ok, string Reason);

/// <summary>
/// 자동 발행 기준. 편집자 검수와 기계 점검을 모두 통과한 글만 사람 확인 없이 발행한다.
/// 하나라도 걸리면 발행하지 않고 이유를 남긴다 (글은 그대로 두어 직접 확인 후 올릴 수 있게).
/// </summary>
public static class PublishGate
{
    /// <param name="verdict">편집자 판정 (검수를 못 했으면 null).</param>
    /// <param name="notesLeft">의견 반영 뒤에도 남은 편집자 의견이 있는지.</param>
    public static GateResult Check(PostDraft post, string? verdict, bool notesLeft, bool hasCodeErrors)
    {
        if (verdict is null) return new(false, "편집자 검수를 하지 못함");
        if (verdict == StaffPrompts.Rewrite) return new(false, "편집자 판정: 다시 쓰기");
        if (notesLeft) return new(false, "편집자 의견을 반영하지 못함");
        if (hasCodeErrors) return new(false, "코드 실행 오류가 남아 있음");
        var issues = PostChecker.BlockingIssues(post);
        return issues.Count > 0 ? new(false, string.Join(", ", issues)) : new(true, "발행 기준 통과");
    }
}

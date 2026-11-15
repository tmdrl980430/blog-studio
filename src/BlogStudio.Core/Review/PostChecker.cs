using System.Text.RegularExpressions;
using BlogStudio.Core.Posts;

namespace BlogStudio.Core.Review;

public enum CheckLevel { Ok, Warn }

public sealed record CheckItem(CheckLevel Level, string Text)
{
    public string Icon => Level == CheckLevel.Ok ? "✓" : "!";
}

/// <summary>
/// 발행 전 점검. 검색에 잘 걸리고 실수 없이 올라가도록 기계적으로 볼 수 있는 것만 본다.
/// 기준값은 네이버·티스토리 블로그에서 흔히 쓰는 경험치 (제목이 검색 결과에서 잘리지 않는 길이 등).
/// </summary>
public static class PostChecker
{
    public const int TitleMin = 12, TitleMax = 40;
    public const int TagsMin = 5, TagsMax = 10;
    public const int BodyMin = 1500;

    private static readonly Regex NeedsCheck = new(@"\(확인 필요\)");
    private static readonly Regex Leftovers = new(@"(?m)^[ \t]*(\[사진\s*\d+\s*[:：]|```svg)");
    private static readonly Regex H2 = new(@"(?m)^## ");
    private static readonly Regex Table = new(@"(?m)^\|.+\|[ \t]*\r?\n\|[ \t:|-]+\|");
    private static readonly Regex Image = new(@"!\[[^\]]*\]\([^)]+\)");
    private static readonly Regex Markup = new(@"```[\s\S]*?```|!\[[^\]]*\]\([^)]+\)|\[([^\]]*)\]\([^)]+\)|[#>*_`|\-]");

    /// <summary>코드 블록·이미지·마크다운 기호를 뺀 글자 수 (공백 제외).</summary>
    public static int TextLength(string markdown) =>
        Markup.Replace(markdown, m => m.Groups[1].Success ? m.Groups[1].Value : "").Count(c => !char.IsWhiteSpace(c));

    public static IReadOnlyList<CheckItem> Check(PostDraft post)
    {
        var list = new List<CheckItem>();
        void Add(bool ok, string good, string bad) => list.Add(ok ? new(CheckLevel.Ok, good) : new(CheckLevel.Warn, bad));

        var t = post.Title.Trim().Length;
        Add(t is >= TitleMin and <= TitleMax, $"제목 {t}자",
            t > TitleMax ? $"제목 {t}자 — 검색 결과에서 잘려요. {TitleMax}자 이하로" : $"제목 {t}자 — 너무 짧아요. 검색어가 들어가게 {TitleMin}자 이상");

        var tags = post.Tags.Count;
        Add(tags is >= TagsMin and <= TagsMax, $"태그 {tags}개",
            tags < TagsMin ? $"태그 {tags}개 — {TagsMin}개 이상이면 검색 노출에 유리해요" : $"태그 {tags}개 — {TagsMax}개 이하로 추리기");

        var len = TextLength(post.BodyMarkdown);
        Add(len >= BodyMin, $"본문 {len:N0}자", $"본문 {len:N0}자 — {BodyMin:N0}자 이상이면 좋아요");

        var h2 = H2.Matches(post.BodyMarkdown).Count;
        Add(h2 >= 3, $"소제목 {h2}개", $"소제목 {h2}개 — 3개 이상으로 나누면 읽기 쉬워요");

        var images = Image.Matches(post.BodyMarkdown).Count;
        Add(images > 0, $"이미지 {images}장", "이미지 없음 — 대표 이미지(썸네일)가 있으면 클릭이 늘어요");

        var unknown = NeedsCheck.Matches(post.BodyMarkdown).Count + NeedsCheck.Matches(post.Title).Count;
        Add(unknown == 0, "확인할 정보 없음", $"'(확인 필요)' {unknown}곳 — 발행 전에 채우거나 지우기");

        var leftovers = Leftovers.Matches(post.BodyMarkdown).Count;
        if (leftovers > 0) list.Add(new(CheckLevel.Warn, $"처리 안 된 사진·그림 자리 {leftovers}곳 — 직접 편집에서 지우기"));

        if (Table.IsMatch(post.BodyMarkdown)) list.Add(new(CheckLevel.Ok, "표 있음"));
        return list;
    }
}

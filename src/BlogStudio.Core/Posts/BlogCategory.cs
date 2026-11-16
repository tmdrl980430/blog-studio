namespace BlogStudio.Core.Posts;

/// <summary>블로그 카테고리 하나 ("상위 > 하위"). 하위가 없으면 상위만.</summary>
public sealed record BlogCategory(string Parent, string Child = "")
{
    public const string Separator = " > ";

    public override string ToString() => string.IsNullOrEmpty(Child) ? Parent : Parent + Separator + Child;

    /// <summary>"개발 > CS 기초", "개발>CS 기초", "개발 / CS 기초"를 읽는다. 빈 값이면 null.</summary>
    public static BlogCategory? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(new[] { '>', '/' }, 2, StringSplitOptions.TrimEntries);
        var parent = parts[0].Trim('"', '\'').Trim();
        var child = parts.Length > 1 ? parts[1].Trim('"', '\'').Trim() : "";
        if (parent.Length == 0) return child.Length == 0 ? null : new BlogCategory(child);
        return new BlogCategory(parent, child);
    }
}

/// <summary>
/// 블로그 카테고리 체계 (이슈 #19). 지금은 기본 체계만 있고, 설정에서 편집하는 칸은 다음 단계.
/// 트랙마다 기본 카테고리가 정해져 있어 자동 작성 글도 카테고리를 갖는다.
/// </summary>
public static class BlogCategories
{
    public static readonly IReadOnlyList<BlogCategory> Default = new[]
    {
        new BlogCategory("개발", "알고리즘·코테"),
        new BlogCategory("개발", "CS 기초"),
        new BlogCategory("개발", "C#·Windows"),
        new BlogCategory("생활", "맛집·카페"),
        new BlogCategory("생활", "여행·일상"),
        new BlogCategory("생활", "패션·코디"),
        new BlogCategory("생활", "제품 리뷰"),
    };

    private static readonly IReadOnlyDictionary<string, int> TrackDefaults = new Dictionary<string, int>
    {
        ["algorithm"] = 0,
        ["cs"] = 1,
        ["csharp_windows"] = 2,
        ["food"] = 3,
        ["travel"] = 4,
        ["fashion"] = 5,
        ["product"] = 6,
    };

    /// <summary>트랙의 기본 카테고리. 모르는 트랙이면 null (블로그 기본 카테고리로 발행).</summary>
    public static BlogCategory? ForTrack(string trackKey) =>
        TrackDefaults.TryGetValue(trackKey, out var i) ? Default[i] : null;

    /// <summary>
    /// 글의 카테고리를 목록 안의 것으로 맞춘다. 공백·구분자 차이는 무시하고, 하위 이름만 맞아도 찾는다.
    /// 목록에 없으면 null — 새로 만들지 않는다.
    /// </summary>
    public static BlogCategory? Match(string? text, IReadOnlyList<BlogCategory>? list = null)
    {
        var wanted = BlogCategory.Parse(text);
        if (wanted is null) return null;
        list ??= Default;
        static string Key(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var exact = list.FirstOrDefault(c => Key(c.Parent) == Key(wanted.Parent) && Key(c.Child) == Key(wanted.Child));
        if (exact is not null) return exact;
        // "CS 기초"처럼 하위만 온 경우
        if (wanted.Child.Length == 0)
        {
            var byChild = list.Where(c => Key(c.Child) == Key(wanted.Parent)).ToList();
            if (byChild.Count == 1) return byChild[0];
        }
        return null;
    }

    /// <summary>글에 저장할 카테고리: 글에 적힌 값이 목록에 있으면 그것, 아니면 트랙 기본값, 둘 다 없으면 "".</summary>
    public static string Resolve(string? written, string trackKey, IReadOnlyList<BlogCategory>? list = null) =>
        (Match(written, list) ?? ForTrack(trackKey))?.ToString() ?? "";
}

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
/// 블로그 카테고리 체계 (이슈 #19). 설정의 "블로그 카테고리 목록"에서 고칠 수 있고, 비워 두면 기본 체계.
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

    /// <summary>
    /// 트랙의 기본 카테고리. 모르는 트랙이거나, 목록을 줬는데 그 목록에서 지운 카테고리면 null
    /// (블로그 기본 카테고리로 발행).
    /// </summary>
    public static BlogCategory? ForTrack(string trackKey, IReadOnlyList<BlogCategory>? list = null)
    {
        if (!TrackDefaults.TryGetValue(trackKey, out var i)) return null;
        return list is null ? Default[i] : Match(Default[i].ToString(), list);
    }

    /// <summary>
    /// 설정에 저장된 줄들("상위 > 하위" 한 줄에 하나)을 목록으로 읽는다.
    /// 빈 줄·중복(공백 차이 무시)은 버리고, 남는 게 없으면 기본 체계.
    /// </summary>
    public static IReadOnlyList<BlogCategory> FromLines(IEnumerable<string>? lines)
    {
        var list = new List<BlogCategory>();
        var seen = new HashSet<string>();
        foreach (var line in lines ?? Array.Empty<string>())
        {
            var c = BlogCategory.Parse(line);
            if (c is not null && seen.Add(Key(c.ToString()))) list.Add(c);
        }
        return list.Count == 0 ? Default : list;
    }

    /// <summary>여러 줄 글(설정 창 입력칸)을 목록으로 읽는다.</summary>
    public static IReadOnlyList<BlogCategory> FromText(string? text) =>
        FromLines((text ?? "").Split('\n'));

    /// <summary>설정 창 입력칸에 보여 줄 글 (한 줄에 하나).</summary>
    public static string ToText(IReadOnlyList<BlogCategory> list) => string.Join(Environment.NewLine, list);

    /// <summary>기본 체계와 같은 목록인지 (같으면 settings.json에 따로 적지 않는다).</summary>
    public static bool IsDefault(IReadOnlyList<BlogCategory> list) => list.SequenceEqual(Default);

    private static string Key(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>
    /// 글의 카테고리를 목록 안의 것으로 맞춘다. 공백·구분자 차이는 무시하고, 하위 이름만 맞아도 찾는다.
    /// 목록에 없으면 null — 새로 만들지 않는다.
    /// </summary>
    public static BlogCategory? Match(string? text, IReadOnlyList<BlogCategory>? list = null)
    {
        var wanted = BlogCategory.Parse(text);
        if (wanted is null) return null;
        list ??= Default;
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
        (Match(written, list) ?? ForTrack(trackKey, list))?.ToString() ?? "";
}

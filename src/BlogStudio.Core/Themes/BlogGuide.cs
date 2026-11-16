using System.Net;
using System.Text.RegularExpressions;
using BlogStudio.Core.Rendering;

namespace BlogStudio.Core.Themes;

/// <summary>블로그 준비 가이드 안의 복사용 코드 (```css / ```html 블록).</summary>
public sealed record GuideCodeBlock(string Language, string Code)
{
    /// <summary>복사 버튼 옆에 보여 줄 이름: "CSS · /* 블로그 스튜디오 글 서식 */".</summary>
    public string Label
    {
        get
        {
            var first = Code.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            if (first.Length > 48) first = first[..48] + "…";
            return $"{Language.ToUpperInvariant()} · {first}";
        }
    }
}

/// <summary>shared/guides/&lt;key&gt;.md 한 개를 테마 값으로 채운 결과.</summary>
public sealed record BlogGuide(string Key, string Title, string Markdown, IReadOnlyList<GuideCodeBlock> CodeBlocks)
{
    /// <summary>앱 안 브라우저(WebView2)로 보여 줄 전체 페이지.</summary>
    public string ToHtmlPage() => $$"""
        <!DOCTYPE html>
        <html lang="ko"><head><meta charset="utf-8">
        <style>
          body{margin:0;background:#fff;color:#15171a;font-family:'Noto Sans KR','Malgun Gothic',sans-serif}
          .wrap{max-width:760px;margin:0 auto;padding:24px 28px 48px}
          h1.title{font-size:22px;line-height:1.4;margin:0 0 18px}
        </style></head>
        <body><div class="wrap">
          <h1 class="title">{{WebUtility.HtmlEncode(Title)}}</h1>
          {{BlogHtmlRenderer.ToBlogHtml(Markdown)}}
        </div></body></html>
        """;
}

/// <summary>
/// "블로그 준비" 가이드 읽기: 머리말(---)과 HTML 주석을 떼고, {theme.*} 자리에 테마 팩의 design 값을 채운다.
/// 가이드 파일 형식은 웹·Mac 앱도 같이 쓰므로 바꾸지 않는다 (읽기만 함).
/// </summary>
public static class BlogGuides
{
    private static readonly Regex FrontMatter = new(@"\A---\s*\n(?<yaml>.*?)\n---\s*\n", RegexOptions.Singleline);
    private static readonly Regex Comment = new(@"<!--.*?-->\s*", RegexOptions.Singleline);
    private static readonly Regex Placeholder = new(@"\{theme\.(?<key>[a-z_]+)\}");
    private static readonly Regex CopyBlock = new(@"^```(?<lang>css|html)[ \t]*\n(?<code>.*?)\n```[ \t]*$",
        RegexOptions.Singleline | RegexOptions.Multiline);

    /// <summary>테마의 design.guides 순서대로 읽는다. 없는 파일은 건너뛴다.</summary>
    public static IReadOnlyList<BlogGuide> Load(string sharedRoot, ThemePack theme)
    {
        var keys = theme.Design.Guides.Count > 0 ? theme.Design.Guides : new List<string> { "naver", "tistory", "design" };
        var list = new List<BlogGuide>();
        foreach (var key in keys)
        {
            var path = Path.Combine(sharedRoot, "guides", key + ".md");
            if (File.Exists(path)) list.Add(Parse(key, File.ReadAllText(path), theme));
        }
        return list;
    }

    public static BlogGuide Parse(string key, string text, ThemePack theme)
    {
        text = text.Replace("\r\n", "\n");
        var title = key;
        var front = FrontMatter.Match(text);
        if (front.Success)
        {
            var line = front.Groups["yaml"].Value.Split('\n')
                .FirstOrDefault(l => l.StartsWith("title:", StringComparison.Ordinal));
            if (line is not null) title = line["title:".Length..].Trim().Trim('"');
            text = text[front.Length..];
        }

        var body = Fill(Comment.Replace(text, ""), theme).Trim();
        var blocks = CopyBlock.Matches(body)
            .Select(m => new GuideCodeBlock(m.Groups["lang"].Value, m.Groups["code"].Value))
            .ToList();
        return new BlogGuide(key, title, body, blocks);
    }

    /// <summary>{theme.categories} 같은 자리를 채운다. 테마에 값이 없거나 모르는 이름이면 그대로 둔다.</summary>
    public static string Fill(string text, ThemePack theme) =>
        Placeholder.Replace(text, m => ValueFor(m.Groups["key"].Value, theme) ?? m.Value);

    private static string? ValueFor(string key, ThemePack theme)
    {
        var d = theme.Design;
        var value = key switch
        {
            "name" => theme.Name,
            "accent" => d.Accent,
            "layout" => d.Layout,
            "categories" => string.Join(" / ", d.Categories),
            "tistory_skin" => d.TistorySkin,
            _ => null,
        };
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

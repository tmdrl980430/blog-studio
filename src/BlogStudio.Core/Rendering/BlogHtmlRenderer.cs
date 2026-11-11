using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using BlogStudio.Core.Posts;

namespace BlogStudio.Core.Rendering;

/// <summary>
/// 마크다운 → 블로그 붙여넣기용 HTML. 네이버·티스토리 에디터는 &lt;style&gt;을 버리므로 모든 스타일을 태그에 직접 넣는다.
/// 스타일 값은 shared/styles 와 웹 렌더러가 같은 값을 쓰도록 맞춘다.
/// </summary>
public static class BlogHtmlRenderer
{
    internal static readonly IReadOnlyDictionary<string, string> TagStyles = new Dictionary<string, string>
    {
        ["h2"] = "font-size:22px;font-weight:700;margin:36px 0 14px;padding-bottom:6px;border-bottom:2px solid #222;",
        ["h3"] = "font-size:18px;font-weight:700;margin:26px 0 10px;",
        ["h4"] = "font-size:16px;font-weight:700;margin:20px 0 8px;",
        ["p"] = "font-size:16px;line-height:1.8;margin:0 0 14px;",
        ["li"] = "font-size:16px;line-height:1.8;margin:2px 0;",
        ["ul"] = "margin:0 0 14px;padding-left:22px;",
        ["ol"] = "margin:0 0 14px;padding-left:22px;",
        ["blockquote"] = "margin:16px 0;padding:10px 16px;border-left:4px solid #3b82f6;background:#f3f7ff;color:#333;",
        ["table"] = "border-collapse:collapse;margin:14px 0;font-size:15px;",
        ["th"] = "border:1px solid #ccc;background:#f5f5f5;padding:8px 12px;text-align:left;",
        ["td"] = "border:1px solid #ccc;padding:8px 12px;",
        ["hr"] = "border:none;border-top:1px solid #ddd;margin:28px 0;",
    };

    internal const string InlineCode =
        "background:#f1f1f1;color:#c7254e;padding:2px 5px;border-radius:4px;font-family:Consolas,monospace;font-size:14px;";
    internal const string CodeBlock =
        "background:#f8f8f8;border:1px solid #e5e5e5;border-radius:6px;padding:14px 16px;overflow-x:auto;" +
        "font-family:Consolas,'D2Coding',monospace;font-size:14px;line-height:1.6;margin:0 0 16px;white-space:pre;";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .Build();

    /// <summary>블로그 에디터는 체크박스 input을 지우므로 기호로 바꾼다.</summary>
    internal static string ReplaceTaskBoxes(string markdown)
    {
        markdown = Regex.Replace(markdown, @"^(\s*)[-*] \[ \] ", "$1- ☐ ", RegexOptions.Multiline);
        return Regex.Replace(markdown, @"^(\s*)[-*] \[[xX]\] ", "$1- ☑ ", RegexOptions.Multiline);
    }

    public static string ToBlogHtml(string markdown)
    {
        var html = Markdown.ToHtml(ReplaceTaskBoxes(markdown.Replace("\r\n", "\n")), Pipeline);

        // 코드 블록: <pre><code class="language-x"> / <pre><code>
        html = Regex.Replace(html, @"<pre><code(?<attrs>[^>]*)>",
            m => $"<pre style=\"{CodeBlock}\"><code data-block=\"true\"{m.Groups["attrs"].Value}>");
        // 인라인 코드 (속성 없는 <code>만)
        html = html.Replace("<code>", $"<code style=\"{InlineCode}\">");

        foreach (var (tag, style) in TagStyles)
            html = Regex.Replace(html, $"<{tag}(?<attrs>\\s[^>]*)?>",
                m => $"<{tag} style=\"{style}\"{m.Groups["attrs"].Value}>");
        return html.Trim();
    }

    /// <summary>앱 안 미리보기(WebView2)용 전체 페이지. 본문은 복사용 HTML과 같다.</summary>
    public static string ToPreviewPage(PostDraft post, string bodyHtml)
    {
        var tags = WebUtility.HtmlEncode(post.TagLine);
        return $$"""
            <!DOCTYPE html>
            <html lang="ko"><head><meta charset="utf-8">
            <style>
              body{margin:0;background:#fff;color:#15171a;font-family:'Noto Sans KR','Malgun Gothic',sans-serif}
              .wrap{max-width:760px;margin:0 auto;padding:28px 32px 48px}
              .meta{font-size:13px;color:#5b6270;margin:0 0 6px}
              h1.title{font-size:24px;line-height:1.4;margin:0 0 8px}
              .tags{font-size:13px;color:#2f5bea;margin:0 0 24px}
            </style></head>
            <body><div class="wrap">
              <p class="meta">{{WebUtility.HtmlEncode(post.Topic)}} · {{post.Date:yyyy-MM-dd}}</p>
              <h1 class="title">{{WebUtility.HtmlEncode(post.Title)}}</h1>
              <p class="tags">{{tags}}</p>
              <div id="content">{{bodyHtml}}</div>
            </div></body></html>
            """;
    }

    /// <summary>마크다운 보기 모드용 페이지.</summary>
    public static string ToMarkdownPage(string markdown) => $$"""
        <!DOCTYPE html><html lang="ko"><head><meta charset="utf-8"></head>
        <body style="margin:0;padding:24px;font-family:Consolas,'D2Coding',monospace;font-size:13px;line-height:1.6;white-space:pre-wrap;color:#15171a">{{WebUtility.HtmlEncode(markdown)}}</body></html>
        """;

    public static string EmptyPage(string message) => $$"""
        <!DOCTYPE html><html lang="ko"><head><meta charset="utf-8"></head>
        <body style="margin:0;height:100vh;display:flex;align-items:center;justify-content:center;font-family:'Malgun Gothic',sans-serif;color:#5b6270;font-size:14px">{{WebUtility.HtmlEncode(message)}}</body></html>
        """;
}

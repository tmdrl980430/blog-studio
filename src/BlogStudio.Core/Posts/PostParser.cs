using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace BlogStudio.Core.Posts;

/// <summary>AI 응답(YAML 머리말 + 마크다운 본문)을 PostDraft로 바꾼다.</summary>
public static class PostParser
{
    private static readonly Regex WholeFence = new(@"^```(?:markdown|md)?\s*\n(?<body>.*)\n```\s*$", RegexOptions.Singleline);
    private static readonly Regex FrontMatter = new(@"^---\s*\n(?<meta>.*?)\n---\s*\n(?<body>.*)$", RegexOptions.Singleline);
    private static readonly IDeserializer Yaml = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    public static PostDraft Parse(string response, string topic, string trackKey, DateOnly date)
    {
        var text = response.Replace("\r\n", "\n").Trim();

        // 앞에 잡담이 붙은 경우 첫 머리말부터 사용
        var start = text.IndexOf("---\n", StringComparison.Ordinal);
        var fence = WholeFence.Match(text);
        if (fence.Success) text = fence.Groups["body"].Value.Trim();
        else if (start > 0) text = text[start..];

        var m = FrontMatter.Match(text);
        if (!m.Success)
            throw new FormatException("응답에서 YAML 머리말(---)을 찾지 못했어요. 응답 앞부분: " + Preview(text));

        Dictionary<string, object?> meta;
        try
        {
            meta = Yaml.Deserialize<Dictionary<string, object?>>(m.Groups["meta"].Value) ?? new();
        }
        catch (Exception ex)
        {
            throw new FormatException("머리말을 읽지 못했어요: " + ex.Message, ex);
        }

        var title = AsString(meta, "title");
        return new PostDraft(
            Title: string.IsNullOrWhiteSpace(title) ? topic : title,
            Tags: AsTags(meta.GetValueOrDefault("tags")),
            Summary: AsString(meta, "summary"),
            BodyMarkdown: m.Groups["body"].Value.Trim(),
            Topic: topic,
            TrackKey: trackKey,
            Date: date);
    }

    private static string AsString(Dictionary<string, object?> meta, string key) =>
        meta.GetValueOrDefault(key)?.ToString()?.Trim() ?? "";

    private static IReadOnlyList<string> AsTags(object? value) => value switch
    {
        null => Array.Empty<string>(),
        string s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(t => t.TrimStart('#')).ToList(),
        IEnumerable<object> list => list.Select(o => o?.ToString()?.Trim().TrimStart('#') ?? "")
                                        .Where(t => t.Length > 0).ToList(),
        _ => new[] { value.ToString() ?? "" }
    };

    private static string Preview(string text) => text.Length <= 200 ? text : text[..200];
}

namespace BlogStudio.Core.Posts;

/// <summary>AI가 쓴 글 한 편. 본문은 마크다운 한 벌만 보관하고, 네이버·티스토리용 HTML은 렌더러가 만든다.</summary>
public sealed record PostDraft(
    string Title,
    IReadOnlyList<string> Tags,
    string Summary,
    string BodyMarkdown,
    string Topic,
    string TrackKey,
    DateOnly Date,
    IReadOnlyList<string>? ThumbnailLines = null,
    string PhotoQuery = "",
    string IllustrationPrompt = "")
{
    public string TagLine => string.Join(" ", Tags.Select(t => "#" + t));
}

/// <summary>지금까지 쓴 글 기록 (state.json).</summary>
/// <summary>저장한 글 한 편. PublishedTo: 사용자가 "올림"으로 표시한 블로그 ("naver", "tistory").</summary>
public sealed record HistoryEntry(DateOnly Date, string TrackKey, string Topic, string Title, string FileName,
    IReadOnlyList<string>? PublishedTo = null)
{
    public bool IsPublishedTo(string blog) => PublishedTo?.Contains(blog) == true;
}

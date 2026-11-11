namespace BlogStudio.Core;

/// <summary>블로그 글쓰기 화면 주소.</summary>
public static class BlogTargets
{
    public static string? NaverWriteUrl(string? blogId) =>
        string.IsNullOrWhiteSpace(blogId) ? null : $"https://blog.naver.com/{Uri.EscapeDataString(blogId.Trim())}?Redirect=Write";

    public static string? TistoryWriteUrl(string? blogName) =>
        string.IsNullOrWhiteSpace(blogName) ? null : $"https://{blogName.Trim()}.tistory.com/manage/newpost";
}

using System.Text.RegularExpressions;
using BlogStudio.Core.Posts;

namespace BlogStudio.Core.Images;

public sealed class ImagePipelineOptions
{
    public bool Thumbnail { get; init; } = true;
    public string ThumbnailLabel { get; init; } = "";
    public string ThumbnailColor { get; init; } = "#2F5BEA";
    public string BlogName { get; init; } = "";

    public bool Diagrams { get; init; } = true;

    /// <summary>null이면 사진 단계 건너뜀 (Pexels 키 없음).</summary>
    public IPhotoSearch? Photos { get; init; }

    /// <summary>null이면 AI 이미지 단계 건너뜀 (꺼져 있거나 OpenAI 키 없음).</summary>
    public IImageGenerator? Generator { get; init; }
}

public sealed record ImagePipelineResult(string BodyMarkdown, IReadOnlyList<PostImage> Images, IReadOnlyList<string> Warnings);

/// <summary>
/// 글이 만들어진 뒤 이미지를 붙인다: SVG 다이어그램 → PNG, 타이포 썸네일, Pexels 사진, AI 일러스트.
/// 한 단계가 실패해도 글은 살리고 경고만 남긴다. 파일은 글 폴더의 images\ 에 저장.
/// </summary>
public static class ImagePipeline
{
    public const string ImagesDir = "images";
    private static readonly Regex SvgBlock = new(@"```svg[^\n]*\n(?<svg>.*?)\n```", RegexOptions.Singleline);
    private static readonly Regex H2 = new(@"^## ", RegexOptions.Multiline);

    public static async Task<ImagePipelineResult> RunAsync(PostDraft post, string postFolder, ImagePipelineOptions options,
        CancellationToken ct = default)
    {
        var dir = Path.Combine(postFolder, ImagesDir);
        Directory.CreateDirectory(dir);
        var images = new List<PostImage>();
        var warnings = new List<string>();
        var body = post.BodyMarkdown.Replace("\r\n", "\n");
        var n = 1;

        // 1) SVG 다이어그램 → PNG (렌더링 실패한 그림은 본문에서 뺀다)
        body = SvgBlock.Replace(body, m =>
        {
            if (!options.Diagrams) return "";
            var svg = m.Groups["svg"].Value;
            var caption = SvgDiagramRenderer.ReadCaption(svg, "개념 그림");
            try
            {
                var file = $"{n:00}_diagram.png";
                File.WriteAllBytes(Path.Combine(dir, file), SvgDiagramRenderer.RenderPng(svg));
                var rel = $"{ImagesDir}/{file}";
                images.Add(new PostImage(ImageKind.Diagram, rel, caption));
                n++;
                return $"![{caption}]({rel})\n\n*그림. {caption}*";
            }
            catch (Exception ex)
            {
                warnings.Add($"다이어그램 '{caption}'을 그리지 못해 뺐어요: {ex.Message}");
                return "";
            }
        });

        // 2) 사진 (Pexels) — 첫 소제목 섹션 뒤
        if (options.Photos is not null && !string.IsNullOrWhiteSpace(post.PhotoQuery))
        {
            try
            {
                if (await options.Photos.SearchAsync(post.PhotoQuery, ct) is { } photo)
                {
                    var file = $"{n:00}_photo.jpg";
                    await File.WriteAllBytesAsync(Path.Combine(dir, file), await options.Photos.DownloadAsync(photo, ct), ct);
                    var rel = $"{ImagesDir}/{file}";
                    var alt = string.IsNullOrWhiteSpace(photo.Alt) ? post.PhotoQuery : photo.Alt;
                    images.Add(new PostImage(ImageKind.Photo, rel, alt, photo.Photographer, photo.PageUrl));
                    n++;
                    body = InsertBeforeH2(body, 1,
                        $"![{alt}]({rel})\n\n사진: [{photo.Photographer}]({photo.PhotographerUrl}) · [Pexels]({photo.PageUrl})");
                }
                else warnings.Add($"'{post.PhotoQuery}'에 맞는 사진을 찾지 못했어요.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add("사진을 가져오지 못했어요: " + ex.Message);
            }
        }

        // 3) AI 일러스트 — "오늘의 정리" 앞
        if (options.Generator is not null && !string.IsNullOrWhiteSpace(post.IllustrationPrompt))
        {
            try
            {
                var file = $"{n:00}_illustration.png";
                await File.WriteAllBytesAsync(Path.Combine(dir, file), await options.Generator.GenerateAsync(post.IllustrationPrompt, ct), ct);
                var rel = $"{ImagesDir}/{file}";
                images.Add(new PostImage(ImageKind.Illustration, rel, post.Title + " 일러스트", "AI 생성 이미지"));
                n++;
                body = InsertBeforeHeading(body, "## 오늘의 정리", $"![{post.Title} 일러스트]({rel})\n\n*AI로 생성한 이미지*");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add("AI 이미지를 만들지 못했어요: " + ex.Message);
            }
        }

        // 4) 썸네일 — 본문 맨 위 (네이버는 첫 이미지를 대표 이미지로 씀)
        if (options.Thumbnail)
        {
            try
            {
                var lines = post.ThumbnailLines is { Count: > 0 } given ? given : ThumbnailRenderer.SplitTitle(post.Title);
                var png = ThumbnailRenderer.RenderPng(options.ThumbnailLabel, lines, options.BlogName, options.ThumbnailColor);
                const string file = "00_thumbnail.png";
                await File.WriteAllBytesAsync(Path.Combine(dir, file), png, ct);
                var rel = $"{ImagesDir}/{file}";
                images.Insert(0, new PostImage(ImageKind.Thumbnail, rel, post.Title));
                body = $"![{post.Title}]({rel})\n\n" + body.TrimStart();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add("썸네일을 만들지 못했어요: " + ex.Message);
            }
        }

        body = Regex.Replace(body, @"\n{3,}", "\n\n").Trim();
        return new ImagePipelineResult(body, images, warnings);
    }

    /// <summary>index번째(0부터) 소제목 바로 앞에 넣는다. 소제목이 모자라면 맨 끝.</summary>
    internal static string InsertBeforeH2(string body, int index, string block)
    {
        var matches = H2.Matches(body);
        if (matches.Count <= index) return body.TrimEnd() + "\n\n" + block + "\n";
        var at = matches[index].Index;
        return body[..at] + block + "\n\n" + body[at..];
    }

    internal static string InsertBeforeHeading(string body, string heading, string block)
    {
        var at = body.IndexOf("\n" + heading, StringComparison.Ordinal);
        if (at < 0) return InsertBeforeH2(body, Math.Max(H2.Matches(body).Count - 1, 0), block);
        return body[..(at + 1)] + block + "\n\n" + body[(at + 1)..];
    }
}

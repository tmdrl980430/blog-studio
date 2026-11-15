using System.Text;
using BlogStudio.Core.Images;
using BlogStudio.Core.Posts;
using BlogStudio.Core.Rendering;
using BlogStudio.Core.Storage;

namespace BlogStudio.Core.Tests;

public class ImageTests
{
    private const string Svg = """
        <!-- caption: BFS 탐색 순서 -->
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 720 300">
          <rect width="720" height="300" fill="#FFFFFF"/>
          <circle cx="120" cy="150" r="40" fill="#2F5BEA"/>
          <text x="360" y="160" font-size="20" font-family="Malgun Gothic, sans-serif">큐에 넣기</text>
          <script>alert(1)</script>
        </svg>
        """;

    private static bool IsPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G';

    private static (int W, int H) PngSize(byte[] b) =>
        ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);

    [Fact]
    public void Thumbnail_is_1200x630_png()
    {
        var png = ThumbnailRenderer.RenderPng("알고리즘", ThumbnailRenderer.SplitTitle("BFS로 미로 최단 거리 구하기"), "승기의 공부 블로그", "#2F5BEA");
        Assert.True(IsPng(png));
        Assert.Equal((1200, 630), PngSize(png));
    }

    [Fact]
    public void Split_title_keeps_lines_short() =>
        Assert.All(ThumbnailRenderer.SplitTitle("BFS로 미로 최단 거리 구하기 완전 정복"), l => Assert.True(l.Length <= 12));

    [Fact]
    public void Svg_diagram_renders_and_reads_caption()
    {
        Assert.Equal("BFS 탐색 순서", SvgDiagramRenderer.ReadCaption(Svg, "x"));
        var png = SvgDiagramRenderer.RenderPng(Svg);
        Assert.True(IsPng(png));
        Assert.Equal(1440, PngSize(png).W);
    }

    private sealed class FakePhotos : IPhotoSearch
    {
        public Task<StockPhoto?> SearchAsync(string query, CancellationToken ct = default) =>
            Task.FromResult<StockPhoto?>(new StockPhoto("https://x/p.jpg", "Jane", "https://pexels.com/@jane", "https://pexels.com/photo/1", "maze"));
        public Task<byte[]> DownloadAsync(StockPhoto photo, CancellationToken ct = default) => Task.FromResult(new byte[] { 1, 2, 3 });
    }

    private sealed class FakeGenerator : IImageGenerator
    {
        public Task<byte[]> GenerateAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult(ThumbnailRenderer.RenderPng("x", new[] { "y" }, "z", null));
    }

    private sealed class BrokenGenerator : IImageGenerator
    {
        public Task<byte[]> GenerateAsync(string prompt, CancellationToken ct = default) => throw new HttpRequestException("401");
    }

    private static PostDraft Post() => new(
        "BFS로 미로 최단 거리 구하기", new[] { "BFS" }, "요약",
        "## 오늘 공부할 내용\n본문\n\n```svg\n" + Svg + "\n```\n\n## 구현\n코드\n\n## 오늘의 정리\n끝",
        "BFS", "algorithm", new DateOnly(2026, 11, 11),
        PhotoQuery: "maze puzzle", IllustrationPrompt: "a robot exploring a maze");

    [Fact]
    public async Task Pipeline_adds_all_images_in_order()
    {
        var folder = Directory.CreateTempSubdirectory("bs-img-").FullName;
        var result = await ImagePipeline.RunAsync(Post(), folder, new ImagePipelineOptions
        {
            ThumbnailLabel = "알고리즘", BlogName = "테스트", Photos = new FakePhotos(), Generator = new FakeGenerator(),
        });

        Assert.Empty(result.Warnings);
        Assert.Equal(new[] { ImageKind.Thumbnail, ImageKind.Diagram, ImageKind.Photo, ImageKind.Illustration },
            result.Images.Select(i => i.Kind).OrderBy(k => k));
        Assert.All(result.Images, i => Assert.True(File.Exists(Path.Combine(folder, i.RelativePath))));
        Assert.DoesNotContain("```svg", result.BodyMarkdown);
        Assert.StartsWith("![", result.BodyMarkdown);                                  // 썸네일이 맨 앞
        Assert.Contains("사진: [Jane](https://pexels.com/@jane)", result.BodyMarkdown);   // 출처 표기
        Assert.True(result.BodyMarkdown.IndexOf("_illustration", StringComparison.Ordinal)
                    < result.BodyMarkdown.IndexOf("## 오늘의 정리", StringComparison.Ordinal));

        // 본문 순서 = 앱의 이미지 번호 순서
        var sources = BlogHtmlRenderer.ImageSourcesInOrder(result.BodyMarkdown);
        Assert.Equal(4, sources.Count);
        Assert.EndsWith("00_thumbnail.png", sources[0]);
    }

    [Fact]
    public async Task Pipeline_failures_become_warnings_and_disabled_steps_are_skipped()
    {
        var folder = Directory.CreateTempSubdirectory("bs-img2-").FullName;
        var result = await ImagePipeline.RunAsync(Post(), folder, new ImagePipelineOptions
        {
            Thumbnail = false, Diagrams = false, Generator = new BrokenGenerator(),
        });

        Assert.Empty(result.Images);
        Assert.Single(result.Warnings);
        Assert.DoesNotContain("```svg", result.BodyMarkdown);
        Assert.Contains("## 오늘의 정리", result.BodyMarkdown);
    }

    [Fact]
    public async Task User_photos_fill_slots_and_leftovers_go_before_summary()
    {
        var src = Directory.CreateTempSubdirectory("bs-photos-").FullName;
        var photos = Enumerable.Range(1, 3).Select(i =>
        {
            var path = Path.Combine(src, $"p{i}.JPG");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, (byte)i });
            return path;
        }).ToList();
        var post = Post() with
        {
            BodyMarkdown = "도입\n\n[사진 2: 라멘 한 그릇]\n\n## 메뉴\n맛있음\n[사진 9: 없는 번호]\n\n## 한눈에 정리\n끝",
        };
        var folder = Directory.CreateTempSubdirectory("bs-img5-").FullName;

        var result = await ImagePipeline.RunAsync(post, folder, new ImagePipelineOptions
        {
            Thumbnail = false, UserPhotos = photos, Photos = new FakePhotos(),
        });

        var body = result.BodyMarkdown;
        Assert.DoesNotMatch(@"(?m)^\[사진", body);                               // 자리 표시 줄은 모두 사라짐
        Assert.Contains("![라멘 한 그릇](images/01_my.jpg)", body);
        Assert.True(body.IndexOf("![사진 1]", StringComparison.Ordinal) < body.IndexOf("## 한눈에 정리", StringComparison.Ordinal));
        Assert.Contains("![사진 3]", body);
        Assert.Equal(3, result.Images.Count);                                   // Pexels 사진은 안 넣음
        Assert.All(result.Images, i => Assert.True(File.Exists(Path.Combine(folder, i.RelativePath))));
        Assert.Contains("\n\n## 메뉴", body);                                  // 자리 표시 정리 후에도 문단 유지
    }

    [Fact]
    public void Pexels_and_openai_responses_parse()
    {
        var photo = PexelsPhotoSearch.ParseFirst("""
            {"photos":[{"url":"https://pexels.com/photo/1","photographer":"Jane","photographer_url":"https://pexels.com/@jane",
              "alt":"maze","src":{"large":"https://img/l.jpg","large2x":"https://img/l2.jpg"}}]}
            """)!;
        Assert.Equal("https://img/l2.jpg", photo.DownloadUrl);
        Assert.Equal("Jane", photo.Photographer);
        Assert.Null(PexelsPhotoSearch.ParseFirst("""{"photos":[]}"""));

        var bytes = OpenAiImageGenerator.ParseImage("""{"data":[{"b64_json":"AQID"}]}""");
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
    }

    [Fact]
    public void Blog_copy_uses_placeholders_and_preview_uses_files()
    {
        const string md = "![썸네일](images/00_thumbnail.png)\n\n## 제목\n| a | b |\n|---|---|\n| 1 | 2 |";
        var copy = BlogHtmlRenderer.ToBlogHtml(BlogHtmlRenderer.ReplaceImagesWithPlaceholders(md));
        Assert.Contains("[이미지 1: 썸네일]", copy);
        Assert.DoesNotContain("<img", copy);
        Assert.Contains("<table", copy);

        var folder = Directory.CreateTempSubdirectory("bs-img3-").FullName;
        var preview = BlogHtmlRenderer.AbsolutizeImages(BlogHtmlRenderer.ToBlogHtml(md), folder);
        Assert.Contains("src=\"file:///", preview);
    }

    [Fact]
    public void Store_keeps_post_folder_images_and_conceals_keys()
    {
        var store = new LocalStore(Directory.CreateTempSubdirectory("bs-img4-").FullName, new ReversingProtector());
        var post = Post();
        var folder = store.CreatePostFolder(post);
        Assert.NotEqual(folder, store.CreatePostFolder(post));                 // 이름이 겹치면 _2

        var images = new[] { new PostImage(ImageKind.Photo, "images/01_photo.jpg", "maze", "Jane", "https://p") };
        store.SavePost(post, advanceProgress: true, folder, images);
        var saved = store.LoadLatest()!;
        Assert.Equal(folder, saved.Folder);
        Assert.Equal("Jane", saved.Images.Single().Credit);

        var hidden = store.Conceal("sk-secret");
        Assert.NotEqual("sk-secret", hidden);
        Assert.Equal("sk-secret", store.Reveal(hidden));
    }

    private sealed class ReversingProtector : ISecretProtector
    {
        public string Protect(string plain) => new(plain.Reverse().ToArray());
        public string Unprotect(string value) => new(value.Reverse().ToArray());
    }
}

namespace BlogStudio.Core.Images;

public enum ImageKind { Thumbnail, Diagram, Photo, Illustration }

/// <summary>글에 붙는 이미지 한 장. 파일은 글 폴더의 images\ 아래에 있다.</summary>
/// <param name="RelativePath">글 폴더 기준 경로 (예: images/01_diagram.png) — 마크다운에 그대로 쓰임.</param>
public sealed record PostImage(ImageKind Kind, string RelativePath, string Caption, string? Credit = null, string? CreditUrl = null)
{
    public string KindLabel => Kind switch
    {
        ImageKind.Thumbnail => "썸네일",
        ImageKind.Diagram => "개념 그림",
        ImageKind.Photo => "사진",
        _ => "AI 이미지",
    };
}

/// <summary>무료 사진 검색 결과 (Pexels).</summary>
public sealed record StockPhoto(string DownloadUrl, string Photographer, string PhotographerUrl, string PageUrl, string Alt);

public interface IPhotoSearch
{
    Task<StockPhoto?> SearchAsync(string query, CancellationToken ct = default);
    Task<byte[]> DownloadAsync(StockPhoto photo, CancellationToken ct = default);
}

public interface IImageGenerator
{
    /// <summary>PNG 바이트를 돌려준다.</summary>
    Task<byte[]> GenerateAsync(string prompt, CancellationToken ct = default);
}

/// <summary>설정에 따라 사진 검색·이미지 생성 서비스를 만든다 (키가 없거나 꺼져 있으면 null).</summary>
public interface IImageServices
{
    IPhotoSearch? Photos(Storage.AppSettings settings);
    IImageGenerator? Generator(Storage.AppSettings settings);
}

public sealed class ImageServices : IImageServices
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly Storage.LocalStore _store;

    public ImageServices(Storage.LocalStore store) => _store = store;

    public IPhotoSearch? Photos(Storage.AppSettings settings) =>
        _store.Reveal(settings.PexelsApiKey) is { Length: > 0 } key ? new PexelsPhotoSearch(Http, key) : null;

    public IImageGenerator? Generator(Storage.AppSettings settings) =>
        settings.AiImagesEnabled && _store.Reveal(settings.OpenAiApiKey) is { Length: > 0 } key
            ? new OpenAiImageGenerator(Http, key, settings.OpenAiImageModel, settings.OpenAiImageQuality)
            : null;
}

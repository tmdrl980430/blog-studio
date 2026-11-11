using System.Net.Http.Headers;
using System.Text.Json;

namespace BlogStudio.Core.Images;

/// <summary>
/// Pexels 무료 사진 (https://www.pexels.com/api/). 사진은 내려받아 블로그에 올려도 되며,
/// 가이드라인에 따라 사진가와 Pexels 링크를 글에 함께 표시한다.
/// (Unsplash는 API 사진을 Unsplash 주소로 핫링크해야 해서 블로그 업로드 용도에 맞지 않음)
/// </summary>
public sealed class PexelsPhotoSearch : IPhotoSearch
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public PexelsPhotoSearch(HttpClient http, string apiKey)
    {
        _http = http;
        _apiKey = apiKey;
    }

    public async Task<StockPhoto?> SearchAsync(string query, CancellationToken ct = default)
    {
        var url = $"https://api.pexels.com/v1/search?query={Uri.EscapeDataString(query)}&orientation=landscape&per_page=5";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", _apiKey);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Pexels 검색 실패 ({(int)response.StatusCode}). API 키를 확인해 주세요.");
        return ParseFirst(await response.Content.ReadAsStringAsync(ct));
    }

    internal static StockPhoto? ParseFirst(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("photos", out var photos) || photos.GetArrayLength() == 0) return null;
        var p = photos[0];
        var src = p.GetProperty("src");
        var download = src.TryGetProperty("large2x", out var l2) ? l2.GetString() : src.GetProperty("large").GetString();
        return new StockPhoto(
            DownloadUrl: download ?? "",
            Photographer: p.GetProperty("photographer").GetString() ?? "",
            PhotographerUrl: p.GetProperty("photographer_url").GetString() ?? "https://www.pexels.com",
            PageUrl: p.GetProperty("url").GetString() ?? "https://www.pexels.com",
            Alt: p.TryGetProperty("alt", out var alt) ? alt.GetString() ?? "" : "");
    }

    public async Task<byte[]> DownloadAsync(StockPhoto photo, CancellationToken ct = default) =>
        await _http.GetByteArrayAsync(photo.DownloadUrl, ct);
}

/// <summary>
/// OpenAI 이미지 생성 (사용자 본인의 API 키, 쓴 만큼 과금). 기본 모델 gpt-image-2, 1536×1024, 품질 low.
/// </summary>
public sealed class OpenAiImageGenerator : IImageGenerator
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _quality;

    public OpenAiImageGenerator(HttpClient http, string apiKey, string model = "gpt-image-2", string quality = "low")
    {
        _http = http;
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "gpt-image-2" : model;
        _quality = string.IsNullOrWhiteSpace(quality) ? "low" : quality;
    }

    internal static string BuildPrompt(string prompt) =>
        prompt.Trim() + " Clean flat editorial illustration for a Korean tech blog, soft colors, no text, no letters, no watermark.";

    public async Task<byte[]> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = _model,
            prompt = BuildPrompt(prompt),
            size = "1536x1024",
            quality = _quality,
            n = 1,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/generations")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        using var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI 이미지 생성 실패 ({(int)response.StatusCode}): {Trim(json, 300)}");
        return ParseImage(json);
    }

    internal static byte[] ParseImage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty("data")[0];
        if (first.TryGetProperty("b64_json", out var b64) && b64.GetString() is { Length: > 0 } s)
            return Convert.FromBase64String(s);
        throw new FormatException("이미지 데이터(b64_json)가 응답에 없어요.");
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

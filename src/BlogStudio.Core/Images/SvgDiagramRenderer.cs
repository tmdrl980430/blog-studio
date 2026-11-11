using System.Text.RegularExpressions;
using SkiaSharp;
using Svg.Skia;

namespace BlogStudio.Core.Images;

/// <summary>AI가 본문에 넣은 ```svg 다이어그램을 PNG로 바꾼다 (블로그 에디터는 SVG를 받지 않으므로).</summary>
public static class SvgDiagramRenderer
{
    private static readonly Regex Caption = new(@"<!--\s*caption:\s*(?<c>.*?)\s*-->", RegexOptions.Singleline);
    private static readonly Regex Unsafe = new(@"<(script|foreignObject)\b.*?</\1>|<image\b[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    public static string ReadCaption(string svg, string fallback)
    {
        var m = Caption.Match(svg);
        return m.Success && m.Groups["c"].Value.Length > 0 ? m.Groups["c"].Value : fallback;
    }

    /// <summary>가로 targetWidth 픽셀로 맞춰 렌더링 (기본 1440 = 720 viewBox의 2배, 선명하게).</summary>
    public static byte[] RenderPng(string svgText, int targetWidth = 1440)
    {
        var cleaned = Unsafe.Replace(svgText, "");
        using var svg = SKSvg.CreateFromSvg(cleaned);
        var picture = svg.Picture ?? throw new FormatException("SVG를 해석하지 못했어요.");
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new FormatException("SVG 크기가 0이에요.");

        var scale = targetWidth / bounds.Width;
        var width = targetWidth;
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

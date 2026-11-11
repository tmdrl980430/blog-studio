using SkiaSharp;

namespace BlogStudio.Core.Images;

/// <summary>
/// 타이포 카드 썸네일 (shared/guides/design.md): 1200×630, 단색 배경, 위 라벨 · 가운데 제목 2줄 · 아래 블로그 이름.
/// 핵심 글자는 가운데 630px 안에 두어 목록에서 정사각형으로 잘려도 보이게 한다.
/// </summary>
public static class ThumbnailRenderer
{
    public const int Width = 1200, Height = 630;
    private static readonly string[] KoreanFonts = { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans KR", "NanumGothic" };

    internal static SKTypeface Typeface(SKFontStyle style)
    {
        foreach (var family in KoreanFonts)
        {
            var tf = SKTypeface.FromFamilyName(family, style);
            if (tf is not null && string.Equals(tf.FamilyName, family, StringComparison.OrdinalIgnoreCase)) return tf;
        }
        // 한글 글리프가 있는 아무 글꼴
        return SKFontManager.Default.MatchCharacter('가') ?? SKTypeface.Default;
    }

    internal static SKColor ParseColor(string? hex, SKColor fallback) =>
        hex is not null && SKColor.TryParse(hex, out var c) ? c : fallback;

    public static byte[] RenderPng(string label, IReadOnlyList<string> titleLines, string footer, string? backgroundHex)
    {
        var background = ParseColor(backgroundHex, new SKColor(0x2F, 0x5B, 0xEA));
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        var canvas = surface.Canvas;
        canvas.Clear(background);

        // 은은한 원 장식 (배경과 같은 계열)
        using (var deco = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 18) })
            canvas.DrawCircle(120, 120, 180, deco);
        using (var deco = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 26) })
            canvas.DrawCircle(Width - 140, Height + 90, 210, deco);

        using var bold = Typeface(SKFontStyle.Bold);
        using var regular = Typeface(SKFontStyle.Normal);
        using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };

        var lines = titleLines.Where(l => !string.IsNullOrWhiteSpace(l)).Take(2).ToList();
        var titleSize = FitTitleSize(bold, lines, maxWidth: 620, start: 60f);
        var lineHeight = titleSize * 1.3f;
        var blockHeight = 26 + 28 + lineHeight * Math.Max(lines.Count, 1) + 28 + 24;
        var y = (Height - blockHeight) / 2f;

        // 라벨 (반투명 알약)
        using (var labelFont = new SKFont(regular, 26))
        {
            var w = labelFont.MeasureText(label);
            using var pill = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 46) };
            var rect = SKRect.Create((Width - w) / 2 - 22, y - 6, w + 44, 44);
            canvas.DrawRoundRect(rect, 22, 22, pill);
            canvas.DrawText(label, Width / 2f, y + 26, SKTextAlign.Center, labelFont, white);
        }

        y += 26 + 28 + 18;
        using (var titleFont = new SKFont(bold, titleSize))
        {
            foreach (var line in lines)
            {
                y += lineHeight * 0.78f;
                canvas.DrawText(line, Width / 2f, y, SKTextAlign.Center, titleFont, white);
                y += lineHeight * 0.22f;
            }
        }

        y += 28 + 24;
        using (var footerFont = new SKFont(regular, 24))
        using (var dim = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 220) })
            canvas.DrawText(footer, Width / 2f, y, SKTextAlign.Center, footerFont, dim);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>가장 긴 줄이 maxWidth 안에 들어가도록 글자 크기를 줄인다 (최소 40).</summary>
    internal static float FitTitleSize(SKTypeface typeface, IReadOnlyList<string> lines, float maxWidth, float start)
    {
        var size = start;
        while (size > 40)
        {
            using var font = new SKFont(typeface, size);
            if (lines.All(l => font.MeasureText(l) <= maxWidth)) break;
            size -= 2;
        }
        return size;
    }

    /// <summary>제목을 두 줄로 나눈다 (AI가 thumbnail 줄을 주지 않았을 때).</summary>
    public static IReadOnlyList<string> SplitTitle(string title, int maxPerLine = 10)
    {
        title = title.Trim();
        if (title.Length <= maxPerLine) return new[] { title };
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = "";
        foreach (var w in words)
        {
            var next = first.Length == 0 ? w : first + " " + w;
            if (next.Length > maxPerLine && first.Length > 0) break;
            first = next;
        }
        if (first.Length == 0 || first.Length > maxPerLine + 4) first = title[..maxPerLine];
        var rest = title[first.Length..].Trim();
        if (rest.Length > maxPerLine + 4) rest = rest[..(maxPerLine + 3)] + "…";
        return rest.Length == 0 ? new[] { first } : new[] { first, rest };
    }
}

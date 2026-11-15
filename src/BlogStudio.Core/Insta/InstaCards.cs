using System.Text.Json;
using System.Text.Json.Serialization;
using BlogStudio.Core.Images;
using BlogStudio.Core.Prompting;
using SkiaSharp;

namespace BlogStudio.Core.Insta;

public sealed record InstaSlide(string Heading, IReadOnlyList<string> Lines);

/// <summary>인스타 카드에 들어갈 글 (Claude가 블로그 글을 요약해 만든다).</summary>
public sealed record InstaCardText(
    string Cover, string Subtitle, IReadOnlyList<InstaSlide> Slides, string Caption, IReadOnlyList<string> Hashtags)
{
    public string CaptionWithTags =>
        Caption.Trim() + "\n\n" + string.Join(" ", Hashtags.Select(t => "#" + t.Trim().TrimStart('#').Replace(" ", "")));
}

public static class InstaCards
{
    public const int Width = 1080, Height = 1350;   // 인스타 피드 세로형 4:5
    public const int MaxSlides = 4;
    public const string Folder = "insta";

    public static PromptPair BuildPrompt(string postFileText, string author) => new(
        $$"""
        당신은 블로그 글을 인스타그램 카드 뉴스로 바꾸는 에디터입니다. 블로그 주인: {{(string.IsNullOrWhiteSpace(author) ? "글쓴이" : author)}}.
        아래 JSON 하나만 출력합니다 (설명·코드 펜스 금지):
        {"cover": "표지 제목 (줄바꿈 \n 포함 최대 2줄, 줄당 12자 이내)", "subtitle": "표지 아래 한 줄 (20자 이내)",
         "slides": [{"heading": "카드 제목 (14자 이내)", "lines": ["한 줄 22자 이내", "…최대 4줄"]}],
         "caption": "피드 캡션 3~5문장, 첫 문장은 훅, 마지막은 '자세한 내용은 블로그에' 안내", "hashtags": ["태그", "…8~12개, # 없이"]}
        규칙: slides는 2~{{MaxSlides}}장. 글에 있는 내용만 쓰고 사실을 지어내지 않습니다. 이모지는 캡션에만 1~3개.
        """,
        "[블로그 글]\n" + postFileText);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class Raw
    {
        public string? Cover { get; set; }
        public string? Subtitle { get; set; }
        public List<RawSlide>? Slides { get; set; }
        public string? Caption { get; set; }
        public List<string>? Hashtags { get; set; }
    }

    private sealed class RawSlide
    {
        public string? Heading { get; set; }
        public List<string>? Lines { get; set; }
    }

    /// <summary>Claude 응답에서 JSON을 꺼낸다 (앞뒤 설명이나 코드 펜스가 붙어도).</summary>
    public static InstaCardText Parse(string response)
    {
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end <= start) throw new FormatException("카드 내용(JSON)을 찾지 못했어요");
        Raw raw;
        try
        {
            raw = JsonSerializer.Deserialize<Raw>(response[start..(end + 1)], Json) ?? throw new FormatException("빈 응답");
        }
        catch (JsonException ex)
        {
            throw new FormatException("카드 내용(JSON)을 읽지 못했어요: " + ex.Message);
        }
        var slides = (raw.Slides ?? new()).Where(s => !string.IsNullOrWhiteSpace(s.Heading))
            .Take(MaxSlides)
            .Select(s => new InstaSlide(s.Heading!.Trim(), (s.Lines ?? new()).Where(l => !string.IsNullOrWhiteSpace(l)).Take(5).ToList()))
            .ToList();
        if (string.IsNullOrWhiteSpace(raw.Cover) || slides.Count == 0) throw new FormatException("표지나 카드 내용이 비었어요");
        return new InstaCardText(raw.Cover.Trim(), raw.Subtitle?.Trim() ?? "", slides, raw.Caption?.Trim() ?? "",
            (raw.Hashtags ?? new()).Where(t => !string.IsNullOrWhiteSpace(t)).Take(15).ToList());
    }

    /// <summary>표지(사진이 있으면 사진 위에 글씨) + 내용 카드 + 마지막 안내 카드.</summary>
    public static IReadOnlyList<byte[]> Render(InstaCardText text, string? coverPhotoPath, string accentHex, string handle)
    {
        var accent = ThumbnailRenderer.ParseColor(accentHex, new SKColor(0x2F, 0x5B, 0xEA));
        var total = text.Slides.Count + 2;
        var pages = new List<byte[]> { RenderCover(text, coverPhotoPath, accent, handle) };
        for (var i = 0; i < text.Slides.Count; i++)
            pages.Add(RenderSlide(text.Slides[i], accent, i + 2, total, handle));
        pages.Add(RenderEnd(text, accent, handle));
        return pages;
    }

    private static byte[] Encode(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] RenderCover(InstaCardText text, string? photo, SKColor accent, string handle)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        var canvas = surface.Canvas;
        canvas.Clear(accent);

        using var bitmap = photo is not null && File.Exists(photo) ? SKBitmap.Decode(photo) : null;
        if (bitmap is not null)
        {
            // 화면을 꽉 채우게 잘라서 (center crop)
            var scale = Math.Max((float)Width / bitmap.Width, (float)Height / bitmap.Height);
            var w = bitmap.Width * scale;
            var h = bitmap.Height * scale;
            using var img = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(img, SKRect.Create((Width - w) / 2, (Height - h) / 2, w, h),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            // 아래쪽 글씨가 읽히게 어둡게
            using var shade = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, Height * 0.35f), new SKPoint(0, Height),
                    new[] { new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 200) }, SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(0, 0, Width, Height, shade);
        }

        using var bold = ThumbnailRenderer.Typeface(SKFontStyle.Bold);
        using var regular = ThumbnailRenderer.Typeface(SKFontStyle.Normal);
        using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };
        using var dim = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 220) };

        const float left = 84, maxWidth = Width - 168;
        using var titleFont = new SKFont(bold, 92);
        using var subFont = new SKFont(regular, 40);
        var titleLines = text.Cover.Split('\n').SelectMany(l => Wrap(l, titleFont, maxWidth)).Take(3).ToList();
        var subLines = Wrap(text.Subtitle, subFont, maxWidth).Take(2).ToList();

        var y = bitmap is not null ? Height - 150 - subLines.Count * 56 - titleLines.Count * 112 : Height / 2f - titleLines.Count * 56;
        foreach (var line in titleLines) { y += 100; canvas.DrawText(line, left, y, SKTextAlign.Left, titleFont, white); y += 12; }
        y += 24;
        foreach (var line in subLines) { y += 48; canvas.DrawText(line, left, y, SKTextAlign.Left, subFont, dim); y += 8; }

        using var handleFont = new SKFont(regular, 30);
        canvas.DrawText(handle, left, Height - 72, SKTextAlign.Left, handleFont, dim);
        DrawSwipe(canvas, regular, dim);
        return Encode(surface);
    }

    private static byte[] RenderSlide(InstaSlide slide, SKColor accent, int page, int total, string handle)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xFA, 0xF8, 0xF5));

        using var bold = ThumbnailRenderer.Typeface(SKFontStyle.Bold);
        using var regular = ThumbnailRenderer.Typeface(SKFontStyle.Normal);
        using var ink = new SKPaint { IsAntialias = true, Color = new SKColor(0x15, 0x17, 0x1A) };
        using var muted = new SKPaint { IsAntialias = true, Color = new SKColor(0x6B, 0x72, 0x80) };
        using var accentPaint = new SKPaint { IsAntialias = true, Color = accent };

        const float left = 96, maxWidth = Width - 192;
        using var pageFont = new SKFont(bold, 34);
        canvas.DrawText($"{page:00}", left, 170, SKTextAlign.Left, pageFont, accentPaint);
        canvas.DrawRect(left, 196, 64, 8, accentPaint);

        using var headFont = new SKFont(bold, 72);
        var y = 300f;
        foreach (var line in Wrap(slide.Heading, headFont, maxWidth).Take(2)) { canvas.DrawText(line, left, y, SKTextAlign.Left, headFont, ink); y += 92; }

        y += 56;
        using var bodyFont = new SKFont(regular, 46);
        foreach (var item in slide.Lines)
        {
            var wrapped = Wrap(item, bodyFont, maxWidth - 44).Take(3).ToList();
            canvas.DrawCircle(left + 10, y - 15, 8, accentPaint);
            foreach (var line in wrapped) { canvas.DrawText(line, left + 44, y, SKTextAlign.Left, bodyFont, ink); y += 66; }
            y += 34;
            if (y > Height - 200) break;
        }

        using var footFont = new SKFont(regular, 30);
        canvas.DrawText(handle, left, Height - 80, SKTextAlign.Left, footFont, muted);
        canvas.DrawText($"{page} / {total}", Width - left, Height - 80, SKTextAlign.Right, footFont, muted);
        return Encode(surface);
    }

    private static byte[] RenderEnd(InstaCardText text, SKColor accent, string handle)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        var canvas = surface.Canvas;
        canvas.Clear(accent);
        using var bold = ThumbnailRenderer.Typeface(SKFontStyle.Bold);
        using var regular = ThumbnailRenderer.Typeface(SKFontStyle.Normal);
        using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };
        using var dim = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 220) };
        using var big = new SKFont(bold, 76);
        using var small = new SKFont(regular, 42);
        canvas.DrawText("자세한 내용은", Width / 2f, Height / 2f - 70, SKTextAlign.Center, big, white);
        canvas.DrawText("블로그에서", Width / 2f, Height / 2f + 30, SKTextAlign.Center, big, white);
        canvas.DrawText("프로필 링크 확인", Width / 2f, Height / 2f + 130, SKTextAlign.Center, small, dim);
        canvas.DrawText(handle, Width / 2f, Height - 110, SKTextAlign.Center, small, dim);
        return Encode(surface);
    }

    private static void DrawSwipe(SKCanvas canvas, SKTypeface regular, SKPaint paint)
    {
        using var font = new SKFont(regular, 30);
        canvas.DrawText("넘겨 보기 →", Width - 84, Height - 72, SKTextAlign.Right, font, paint);
    }

    /// <summary>글자 폭을 재서 줄바꿈 (띄어쓰기 우선, 긴 단어는 글자 단위).</summary>
    public static IEnumerable<string> Wrap(string text, SKFont font, float maxWidth)
    {
        var line = "";
        foreach (var word in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (font.MeasureText(candidate) <= maxWidth) { line = candidate; continue; }
            if (line.Length > 0) { yield return line; line = ""; }
            foreach (var ch in word)
            {
                if (font.MeasureText(line + ch) > maxWidth && line.Length > 0) { yield return line; line = ""; }
                line += ch;
            }
        }
        if (line.Length > 0) yield return line;
    }

    /// <summary>글 폴더의 insta\ 에 01.png… 와 caption.txt 를 쓴다. 쓴 파일 경로들.</summary>
    public static IReadOnlyList<string> Save(string postFolder, IReadOnlyList<byte[]> pages, InstaCardText text)
    {
        var dir = Path.Combine(postFolder, Folder);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var files = new List<string>();
        for (var i = 0; i < pages.Count; i++)
        {
            var path = Path.Combine(dir, $"{i + 1:00}.png");
            File.WriteAllBytes(path, pages[i]);
            files.Add(path);
        }
        File.WriteAllText(Path.Combine(dir, "caption.txt"), text.CaptionWithTags);
        return files;
    }
}

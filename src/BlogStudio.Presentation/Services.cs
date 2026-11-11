namespace BlogStudio.Presentation;

/// <summary>플랫폼별 클립보드 (WPF·Avalonia가 각각 구현).</summary>
public interface IClipboardService
{
    /// <summary>서식 있는 HTML + 일반 텍스트를 함께 복사.</summary>
    void SetHtml(string html, string plainText);

    void SetText(string text);
}

/// <summary>기본 브라우저로 주소 열기.</summary>
public interface IBrowserLauncher
{
    void Open(string url);
}

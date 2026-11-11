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

/// <summary>Claude Code 설치·로그인을 앱 안에서 돕는다 (플랫폼별 구현).</summary>
public interface IClaudeCodeSetup
{
    /// <summary>공식 설치 스크립트를 보이는 창에서 실행하고 끝날 때까지 기다린다.</summary>
    Task<bool> InstallAsync(CancellationToken ct = default);

    /// <summary>claude를 새 터미널 창에서 실행해 사용자가 Claude Pro 계정으로 로그인하게 한다.</summary>
    void OpenLoginTerminal();
}

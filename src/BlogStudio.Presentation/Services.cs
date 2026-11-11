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

public sealed record AppUpdate(string Version);

/// <summary>앱 자동 업데이트 (Windows·Mac 모두 Velopack + GitHub Releases).</summary>
public interface IUpdateService
{
    /// <summary>설치 프로그램으로 설치된 앱인지 (zip으로 실행하거나 개발 중이면 false → 업데이트 확인 안 함).</summary>
    bool IsInstalled { get; }

    Task<AppUpdate?> CheckAsync(CancellationToken ct = default);

    Task DownloadAsync(Action<int> progress, CancellationToken ct = default);

    /// <summary>받아 둔 업데이트를 적용하고 앱을 다시 시작한다 (돌아오지 않음).</summary>
    void ApplyAndRestart();
}

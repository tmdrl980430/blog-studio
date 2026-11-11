namespace BlogStudio.Presentation;

/// <summary>플랫폼별 클립보드 (WPF·Avalonia가 각각 구현).</summary>
public interface IClipboardService
{
    /// <summary>서식 있는 HTML + 일반 텍스트를 함께 복사.</summary>
    void SetHtml(string html, string plainText);

    void SetText(string text);

    /// <summary>이미지 파일을 클립보드에 (블로그 에디터에 바로 붙여넣기 → 업로드).</summary>
    void SetImage(string path) { }
}

public enum BlogPlatform { Naver, Tistory }

/// <summary>자동 채우기에 넘길 글. BodyHtml의 [이미지 N] 자리 순서 = ImagePaths 순서.</summary>
public sealed record AutoFillPayload(
    BlogPlatform Platform, string WriteUrl, string Title, IReadOnlyList<string> Tags,
    string BodyHtml, IReadOnlyList<string> ImagePaths);

/// <summary>
/// 앱 안 브라우저에서 블로그 글쓰기 화면을 열고 제목·본문·이미지·태그를 채운다.
/// 발행은 사용자가 직접 누른다 (공식 글쓰기 API가 없어서 반자동).
/// </summary>
public interface IAutoFillLauncher
{
    void Open(AutoFillPayload payload);
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

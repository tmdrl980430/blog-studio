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

    /// <summary>
    /// 자동 발행: 채우기를 모두 마치면 발행까지 누른다. 발행이 확인되면 true.
    /// 지원하지 않는 기기는 false (아무것도 하지 않음).
    /// </summary>
    Task<bool> PublishAsync(AutoFillPayload payload) => Task.FromResult(false);

    /// <summary>
    /// 자동 발행 + 멈춘 단계·이유. 기본 구현은 <see cref="PublishAsync"/> 결과만 옮긴다(이유 모름).
    /// </summary>
    async Task<AutoPublishResult> PublishWithReasonAsync(AutoFillPayload payload) =>
        new(await PublishAsync(payload));
}

/// <summary>자동 발행이 멈춘 단계 (auto-log에서 원인을 나눠 보이기 위함).</summary>
public enum AutoPublishStage
{
    /// <summary>앱 안 브라우저 로그인이 풀림.</summary>
    Login,
    /// <summary>글쓰기 화면 찾기·제목·본문·이미지·태그 채우기 실패 (발행 버튼은 누르지 않음).</summary>
    Fill,
    /// <summary>발행 버튼·확인 버튼·발행 완료 확인 실패.</summary>
    Publish,
}

/// <summary>자동 발행 결과. 실패면 <see cref="FailedAt"/>·<see cref="Reason"/>을 알 수 있을 때만 채운다.</summary>
public sealed record AutoPublishResult(bool Ok, AutoPublishStage? FailedAt = null, string? Reason = null)
{
    /// <summary>auto-log 결과 칸에 쓸 문구. 성공은 예전과 같은 "○○ 발행 ✓".</summary>
    public string LogText(string blog)
    {
        if (Ok) return $"{blog} 발행 ✓";
        // 결과 칸은 ", "로 블로그를 나누므로 이유 안의 쉼표는 바꾼다
        var why = string.IsNullOrWhiteSpace(Reason) ? "" : " — " + Reason.Trim().Replace(", ", " · ");
        return FailedAt switch
        {
            AutoPublishStage.Login => $"{blog} 로그인 필요{why}",
            AutoPublishStage.Fill => $"{blog} 채우기 실패{why}",
            AutoPublishStage.Publish => $"{blog} 발행 실패{why}",
            _ => $"{blog} 발행 실패 (창에서 확인)",
        };
    }
}

/// <summary>매일 정해진 시각에 앱을 "--auto-generate"로 실행 (Windows: 작업 스케줄러).</summary>
public interface IDailyScheduler
{
    /// <summary>켜면 등록·시각 변경, 끄면 삭제. 실패하면 예외.</summary>
    void Apply(bool enabled, TimeOnly time);
}

/// <summary>사진 파일 고르기 (플랫폼별 파일 대화상자).</summary>
public interface IFilePicker
{
    IReadOnlyList<string> PickImages();
}

/// <summary>기본 브라우저로 주소 열기.</summary>
public interface IBrowserLauncher
{
    void Open(string url);
}

/// <summary>Claude Code 설치·로그인을 앱 안에서 돕는다 (플랫폼별 구현).</summary>
/// <summary>글 쓰는 AI 도구(Claude Code 또는 Codex) 설치·로그인. 어느 쪽인지는 설정의 Engine으로 정한다.</summary>
public interface IClaudeCodeSetup
{
    /// <summary>안내 문구에 쓸 이름 (예: "Claude Code", "Codex").</summary>
    string ToolName => "Claude Code";

    /// <summary>로그인할 계정 (예: "Claude Pro", "ChatGPT").</summary>
    string AccountName => "Claude Pro";

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

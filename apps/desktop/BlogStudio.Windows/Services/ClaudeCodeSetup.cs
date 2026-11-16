using System.Diagnostics;
using BlogStudio.Core.Ai;
using BlogStudio.Presentation;

namespace BlogStudio.Windows.Services;

/// <summary>
/// 설정의 "글 쓰는 AI"에 맞춰 설치·로그인을 돕는다.
/// Claude: 공식 설치 스크립트(PowerShell) → %USERPROFILE%\.local\bin\claude.exe
/// ChatGPT: Node.js가 없으면 winget으로 설치한 뒤 npm으로 Codex CLI(@openai/codex) 설치 → %APPDATA%\npm\codex.cmd
/// 둘 다 진행이 보이는 창에서 실행한다.
/// </summary>
public sealed class WindowsClaudeCodeSetup : IClaudeCodeSetup
{
    private readonly Func<string> _engine;

    public WindowsClaudeCodeSetup(Func<string>? engine = null) => _engine = engine ?? (() => EngineRouter.Claude);

    private bool IsChatGpt => _engine() == EngineRouter.ChatGpt;

    public string ToolName => IsChatGpt ? "Codex" : "Claude Code";
    public string AccountName => IsChatGpt ? "ChatGPT" : "Claude Pro";

    private const string ClaudeScript =
        "irm https://claude.ai/install.ps1 | iex; " +
        "Write-Host ''; Write-Host '설치가 끝났어요. 5초 뒤 이 창이 닫힙니다.'; Start-Sleep -Seconds 5";

    private const string CodexScript =
        "if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { " +
        "Write-Host 'Node.js를 먼저 설치해요…'; " +
        "winget install -e --id OpenJS.NodeJS.LTS --accept-source-agreements --accept-package-agreements; " +
        "$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User') }; " +
        "npm install -g @openai/codex; " +
        "Write-Host ''; Write-Host '설치가 끝났어요. 5초 뒤 이 창이 닫힙니다.'; Start-Sleep -Seconds 5";

    public async Task<bool> InstallAsync(CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = true,   // 진행 상황이 보이는 새 창
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{(IsChatGpt ? CodexScript : ClaudeScript)}\"",
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell을 시작하지 못했어요.");
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0;
    }

    public void OpenLoginTerminal()
    {
        if (IsChatGpt)
        {
            // codex login → 브라우저에서 ChatGPT 계정으로 로그인 ("Sign in with ChatGPT")
            var codex = ClaudeCodeLocator.Find("codex") ?? "codex";
            Process.Start(new ProcessStartInfo("cmd.exe", $"/k \"\"{codex}\" login\"") { UseShellExecute = true });
            return;
        }
        var exe = ClaudeCodeLocator.Find() ?? "claude";
        // 새 콘솔 창에서 claude 실행 → 첫 실행 시 로그인 안내가 나온다.
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }
}

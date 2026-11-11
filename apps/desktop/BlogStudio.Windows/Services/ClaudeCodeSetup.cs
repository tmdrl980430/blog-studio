using System.Diagnostics;
using BlogStudio.Core.Ai;
using BlogStudio.Presentation;

namespace BlogStudio.Windows.Services;

/// <summary>
/// Claude Code 공식 설치 스크립트(PowerShell)를 보이는 창에서 실행한다.
/// 설치 위치는 %USERPROFILE%\.local\bin 이고, ClaudeCodeLocator가 PATH와 무관하게 찾는다.
/// </summary>
public sealed class WindowsClaudeCodeSetup : IClaudeCodeSetup
{
    private const string InstallScript =
        "irm https://claude.ai/install.ps1 | iex; " +
        "Write-Host ''; Write-Host '설치가 끝났어요. 5초 뒤 이 창이 닫힙니다.'; Start-Sleep -Seconds 5";

    public async Task<bool> InstallAsync(CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = true,   // 진행 상황이 보이는 새 창
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{InstallScript}\"",
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell을 시작하지 못했어요.");
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0;
    }

    public void OpenLoginTerminal()
    {
        var exe = ClaudeCodeLocator.Find() ?? "claude";
        // 새 콘솔 창에서 claude 실행 → 첫 실행 시 로그인 안내가 나온다.
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }
}

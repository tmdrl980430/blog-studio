using System.Diagnostics;
using System.Text;

namespace BlogStudio.Core.Ai;

public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StandardInput,
    string WorkingDirectory,
    IReadOnlyCollection<string> RemoveEnvironmentVariables,
    TimeSpan Timeout);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>외부 프로세스 실행을 감싼 인터페이스 (테스트에서 가짜로 바꿔 끼움).</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default);
}

public sealed class SystemProcessRunner : IProcessRunner
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(spec.FileName)
        {
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = spec.StandardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
        };
        if (spec.StandardInput is not null) psi.StandardInputEncoding = Utf8NoBom;
        foreach (var arg in spec.Arguments) psi.ArgumentList.Add(arg);
        foreach (var name in spec.RemoveEnvironmentVariables) psi.Environment.Remove(name);

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new AiProviderException($"프로세스를 시작하지 못했어요: {spec.FileName}");

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        if (spec.StandardInput is not null)
        {
            await process.StandardInput.WriteAsync(spec.StandardInput.AsMemory(), ct);
            process.StandardInput.Close();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(spec.Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 이미 종료 */ }
            if (ct.IsCancellationRequested) throw;
            throw new AiProviderException($"{spec.Timeout.TotalMinutes:0}분 안에 끝나지 않아 중단했어요.");
        }

        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }
}

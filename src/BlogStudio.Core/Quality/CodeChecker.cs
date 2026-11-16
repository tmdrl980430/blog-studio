using System.Text.RegularExpressions;
using BlogStudio.Core.Ai;

namespace BlogStudio.Core.Quality;

public enum CodeStatus { Ok, Error, Skipped }

public sealed record CodeCheck(int Number, CodeStatus Status, string Message)
{
    public string Label => Status switch
    {
        CodeStatus.Ok => $"코드 {Number} 실행 확인",
        CodeStatus.Error => $"코드 {Number} 오류: {Message}",
        _ => $"코드 {Number} 건너뜀: {Message}",
    };
}

/// <summary>
/// 글 속 파이썬 코드 예제를 실제로 실행해 본다. 임시 빈 폴더에서, 짧은 시간 제한으로,
/// 파일·네트워크·프로세스를 건드리는 코드는 실행하지 않고 건너뛴다 (Claude가 쓴 코드를 내 PC에서 돌리는 것이므로).
/// </summary>
public sealed class CodeChecker
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly Regex Block = new(@"(?ms)^```(?<lang>[A-Za-z0-9_+-]*)[ \t]*\r?\n(?<code>.*?)^```[ \t]*\r?$");
    private static readonly Regex Risky = new(
        @"\b(import\s+(os|shutil|subprocess|socket|requests|urllib|http|pathlib|ctypes|multiprocessing|threading|asyncio|webbrowser|tkinter)\b|from\s+(os|shutil|subprocess|socket|requests|urllib|http|pathlib|ctypes)\b|open\s*\(|__import__|eval\s*\(|exec\s*\(|compile\s*\()");
    private static readonly Regex NeedsInput = new(@"\binput\s*\(|sys\.stdin|stdin\.read");

    private readonly IProcessRunner _runner;
    private string? _python;
    private bool _looked;

    public CodeChecker(IProcessRunner runner) => _runner = runner;

    /// <summary>```python / ```py 블록 (글에 나온 순서, 번호는 1부터).</summary>
    public static IReadOnlyList<string> PythonBlocks(string markdown) =>
        Block.Matches(markdown)
            .Where(m => m.Groups["lang"].Value.ToLowerInvariant() is "python" or "py" or "python3")
            .Select(m => m.Groups["code"].Value)
            .ToList();

    /// <summary>실행하지 않을 이유 (없으면 null).</summary>
    public static string? SkipReason(string code)
    {
        if (Risky.IsMatch(code)) return "파일·네트워크·프로세스를 쓰는 코드라 실행하지 않았어요";
        if (NeedsInput.IsMatch(code)) return "입력이 필요한 코드예요";
        if (!code.Contains('\n') && code.Trim().Length < 3) return "너무 짧아요";
        return null;
    }

    private async Task<string?> FindPythonAsync(CancellationToken ct)
    {
        if (_looked) return _python;
        _looked = true;
        foreach (var exe in new[] { "python", "py", "python3" })
        {
            try
            {
                var r = await _runner.RunAsync(new ProcessSpec(exe, new[] { "--version" }, null, Path.GetTempPath(),
                    Array.Empty<string>(), TimeSpan.FromSeconds(5)), ct);
                // Windows의 "python" 앱 실행 별칭은 스토어 안내만 띄우고 실패한다
                if (r.ExitCode == 0 && (r.StandardOutput + r.StandardError).Contains("Python 3")) return _python = exe;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 다음 후보
            }
        }
        return null;
    }

    /// <summary>파이썬이 없으면 null (점검 목록에 "파이썬이 없어 건너뜀" 하나만 표시).</summary>
    public async Task<IReadOnlyList<CodeCheck>?> CheckAsync(string markdown, CancellationToken ct = default)
    {
        var blocks = PythonBlocks(markdown);
        if (blocks.Count == 0) return Array.Empty<CodeCheck>();
        if (await FindPythonAsync(ct) is not { } python) return null;

        var results = new List<CodeCheck>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var n = i + 1;
            if (SkipReason(blocks[i]) is { } why) { results.Add(new(n, CodeStatus.Skipped, why)); continue; }

            var dir = Directory.CreateTempSubdirectory("bs-code-").FullName;
            try
            {
                var file = Path.Combine(dir, "snippet.py");
                await File.WriteAllTextAsync(file, blocks[i], ct);
                var args = python == "py" ? new[] { "-3", "-I", "-X", "utf8", file } : new[] { "-I", "-X", "utf8", file };
                var r = await _runner.RunAsync(new ProcessSpec(python, args, "", dir, Array.Empty<string>(), Timeout), ct);
                results.Add(r.ExitCode == 0
                    ? new(n, CodeStatus.Ok, "")
                    : Describe(n, r.StandardError));
            }
            catch (AiProviderException ex) when (!ct.IsCancellationRequested)
            {
                // ProcessRunner는 시간 초과도 이 예외로 알린다
                results.Add(new(n, CodeStatus.Error, ex.Message.Contains("끝나지 않아")
                    ? $"{Timeout.TotalSeconds:0}초 안에 끝나지 않아요 (무한 반복?)"
                    : ex.Message));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }
        return results;
    }

    private static CodeCheck Describe(int n, string stderr)
    {
        var last = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "알 수 없는 오류";
        if (last.StartsWith("ModuleNotFoundError")) return new(n, CodeStatus.Skipped, "외부 라이브러리가 필요해요 (" + last + ")");
        var line = Regex.Match(stderr, @"line (\d+)\D*$", RegexOptions.Multiline);
        return new(n, CodeStatus.Error, last.Length > 120 ? last[..120] + "…" : last + (line.Success ? $" ({line.Groups[1].Value}번째 줄)" : ""));
    }
}

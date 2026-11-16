namespace BlogStudio.Core.Ai;

/// <summary>
/// 내 PC에 설치된 Claude Code(claude)를 헤드리스(-p)로 실행해 Claude Pro 구독 사용량으로 글을 쓴다.
/// 앱은 Claude Code를 실행만 하고, 로그인 토큰을 읽거나 다른 곳으로 옮기지 않는다 (본인 PC·본인 용도 전용).
/// </summary>
public sealed class ClaudeCodeProvider : IAiProvider
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    private readonly IProcessRunner _runner;
    private readonly Func<string?> _locate;
    private readonly TimeSpan _timeout;
    private readonly Func<bool> _leanWanted;
    private bool _leanUnsupported;

    /// <param name="lean">
    /// 사용량 절약: Claude Code의 긴 기본 지시문과 도구 설명을 빼고(--system-prompt-file, --tools "")
    /// 글쓰기에 필요한 내용만 보낸다. 설치된 Claude Code가 이 옵션을 모르면 예전 방식으로 한 번 더 실행한다.
    /// </param>
    public ClaudeCodeProvider(IProcessRunner runner, Func<string?>? locate = null, TimeSpan? timeout = null, Func<bool>? lean = null)
    {
        _runner = runner;
        _locate = locate ?? ClaudeCodeLocator.Find;
        _timeout = timeout ?? DefaultTimeout;
        _leanWanted = lean ?? (() => true);
    }

    public string Name => "Claude Code (Claude Pro)";

    /// <summary>지금 절약 모드로 실행하는지 (예전 Claude Code라 꺼졌으면 false).</summary>
    public bool IsLean => _leanWanted() && !_leanUnsupported;

    /// <summary>ANTHROPIC_API_KEY가 있으면 Claude Code가 구독 대신 API 과금으로 바뀌므로 자식 프로세스에서 지운다.</summary>
    internal static readonly string[] StrippedVariables = { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN" };

    internal const string AnswerOnly = "(파일을 만들거나 도구를 쓰지 말고, 위 출력 형식의 글 본문만 그대로 답하세요.)";

    internal static string ModelFor(AiRequest request) =>
        !string.IsNullOrWhiteSpace(request.PreferredModel) ? request.PreferredModel!
        : request.ModelTier == "light" ? "haiku" : "sonnet";

    internal static string ComposePrompt(AiRequest request) =>
        $"{request.System}\n\n=====\n\n{request.User}\n\n{AnswerOnly}";

    internal static IReadOnlyList<string> LeanArguments(string model, string systemFile) => new[]
    {
        "-p", "--output-format", "text", "--model", model,
        "--system-prompt-file", systemFile,
        "--tools", "",
        "--no-session-persistence",
    };

    internal static IReadOnlyList<string> LegacyArguments(string model) =>
        new[] { "-p", "--output-format", "text", "--model", model };

    /// <summary>예전 Claude Code가 새 옵션을 모를 때의 오류.</summary>
    internal static bool IsUnknownOption(ProcessResult r) =>
        r.ExitCode != 0 && System.Text.RegularExpressions.Regex.IsMatch(r.StandardError + r.StandardOutput,
            @"unknown option|unknown argument|unexpected argument|error: option", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public async Task<string?> CheckAsync(CancellationToken ct = default)
    {
        var exe = _locate();
        if (exe is null) return null;
        try
        {
            var r = await _runner.RunAsync(new ProcessSpec(exe, new[] { "--version" }, null,
                Path.GetTempPath(), StrippedVariables, TimeSpan.FromSeconds(30)), ct);
            return r.ExitCode == 0 ? r.StandardOutput.Trim() : null;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<AiResult> GenerateAsync(AiRequest request, CancellationToken ct = default)
    {
        var exe = _locate() ?? throw new AiProviderException(
            "Claude Code(claude)를 찾지 못했어요. 설치 후 터미널에서 claude를 한 번 실행해 Claude Pro 계정으로 로그인해 주세요.");
        var model = ModelFor(request);

        // 빈 임시 폴더에서 실행해 다른 파일을 건드리지 않게 한다.
        var workDir = Directory.CreateTempSubdirectory("blogstudio-").FullName;
        try
        {
            ProcessResult result;
            if (IsLean)
            {
                var systemFile = Path.Combine(workDir, "system.md");
                await File.WriteAllTextAsync(systemFile, request.System, ct);
                result = await _runner.RunAsync(new ProcessSpec(exe, LeanArguments(model, systemFile),
                    $"{request.User}\n\n{AnswerOnly}", workDir, StrippedVariables, _timeout), ct);
                if (IsUnknownOption(result)) _leanUnsupported = true;     // 예전 버전 → 아래에서 예전 방식으로
                else return Check(result, model);
            }
            result = await _runner.RunAsync(new ProcessSpec(exe, LegacyArguments(model), ComposePrompt(request),
                workDir, StrippedVariables, _timeout), ct);
            return Check(result, model);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* 무시 */ }
        }
    }

    private AiResult Check(ProcessResult result, string model)
    {
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new AiProviderException(
                "Claude Code 실행에 실패했어요. 로그인 상태(터미널에서 claude → /login)나 사용량 한도를 확인해 주세요.\n"
                + Trim(detail, 800));
        }
        return new AiResult(result.StandardOutput, Name, model);
    }

    internal static string Trim(string s, int max) => s.Length <= max ? s.Trim() : s[..max].Trim() + "…";
}

/// <summary>claude 실행 파일 찾기: PATH → 공식 설치 위치(~/.local/bin) → npm 전역 설치 위치.</summary>
public static class ClaudeCodeLocator
{
    public static string? Find() => Find("claude");

    /// <summary>같은 규칙으로 다른 CLI(codex 등)도 찾는다.</summary>
    public static string? Find(string command) => Find(command,
        Environment.GetEnvironmentVariable("PATH") ?? "",
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        OperatingSystem.IsWindows(),
        File.Exists);

    internal static string? Find(string pathVar, string home, string appData, bool windows, Func<string, bool> exists) =>
        Find("claude", pathVar, home, appData, windows, exists);

    internal static string? Find(string command, string pathVar, string home, string appData, bool windows, Func<string, bool> exists)
    {
        var names = windows ? new[] { command + ".exe", command + ".cmd" } : new[] { command };
        var dirs = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        dirs.Add(Path.Combine(home, ".local", "bin"));
        if (windows) dirs.Add(Path.Combine(appData, "npm"));

        foreach (var dir in dirs)
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir.Trim('"'), name);
                if (exists(candidate)) return candidate;
            }
        return null;
    }
}

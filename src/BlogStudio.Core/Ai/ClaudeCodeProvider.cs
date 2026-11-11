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

    public ClaudeCodeProvider(IProcessRunner runner, Func<string?>? locate = null, TimeSpan? timeout = null)
    {
        _runner = runner;
        _locate = locate ?? ClaudeCodeLocator.Find;
        _timeout = timeout ?? DefaultTimeout;
    }

    public string Name => "Claude Code (Claude Pro)";

    /// <summary>ANTHROPIC_API_KEY가 있으면 Claude Code가 구독 대신 API 과금으로 바뀌므로 자식 프로세스에서 지운다.</summary>
    internal static readonly string[] StrippedVariables = { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN" };

    internal static string ModelFor(AiRequest request) =>
        !string.IsNullOrWhiteSpace(request.PreferredModel) ? request.PreferredModel!
        : request.ModelTier == "light" ? "haiku" : "sonnet";

    internal static string ComposePrompt(AiRequest request) =>
        $"{request.System}\n\n=====\n\n{request.User}\n\n(파일을 만들거나 도구를 쓰지 말고, 위 출력 형식의 글 본문만 그대로 답하세요.)";

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
            var result = await _runner.RunAsync(new ProcessSpec(
                exe,
                new[] { "-p", "--output-format", "text", "--model", model },
                ComposePrompt(request),
                workDir,
                StrippedVariables,
                _timeout), ct);

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
                throw new AiProviderException(
                    "Claude Code 실행에 실패했어요. 로그인 상태(터미널에서 claude → /login)나 사용량 한도를 확인해 주세요.\n"
                    + Trim(detail, 800));
            }
            return new AiResult(result.StandardOutput, Name, model);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* 무시 */ }
        }
    }

    private static string Trim(string s, int max) => s.Length <= max ? s.Trim() : s[..max].Trim() + "…";
}

/// <summary>claude 실행 파일 찾기: PATH → 공식 설치 위치(~/.local/bin) → npm 전역 설치 위치.</summary>
public static class ClaudeCodeLocator
{
    public static string? Find() => Find(
        Environment.GetEnvironmentVariable("PATH") ?? "",
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        OperatingSystem.IsWindows(),
        File.Exists);

    internal static string? Find(string pathVar, string home, string appData, bool windows, Func<string, bool> exists)
    {
        var names = windows ? new[] { "claude.exe", "claude.cmd" } : new[] { "claude" };
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

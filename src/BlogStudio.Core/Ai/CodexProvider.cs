namespace BlogStudio.Core.Ai;

/// <summary>
/// ChatGPT 구독으로 글쓰기: 내 PC의 OpenAI Codex CLI(codex)를 "codex exec"(비대화형)로 실행한다.
/// codex에 ChatGPT 계정으로 로그인해 두면 ChatGPT 요금제에 포함된 사용량으로 돈다 (API 과금 아님).
/// 앱은 codex를 실행만 하고 로그인 정보를 읽거나 옮기지 않는다 (본인 PC·본인 용도 전용).
/// </summary>
public sealed class CodexProvider : IAiProvider
{
    private readonly IProcessRunner _runner;
    private readonly Func<string?> _locate;
    private readonly TimeSpan _timeout;
    private readonly Func<bool> _saveUsage;

    public CodexProvider(IProcessRunner runner, Func<string?>? locate = null, TimeSpan? timeout = null, Func<bool>? saveUsage = null)
    {
        _runner = runner;
        _locate = locate ?? (() => ClaudeCodeLocator.Find("codex"));
        _timeout = timeout ?? ClaudeCodeProvider.DefaultTimeout;
        _saveUsage = saveUsage ?? (() => true);
    }

    public string Name => "Codex (ChatGPT 구독)";

    /// <summary>API 키가 있으면 codex가 구독 대신 API 과금으로 바뀌므로 자식 프로세스에서 지운다.</summary>
    internal static readonly string[] StrippedVariables = { "OPENAI_API_KEY", "CODEX_API_KEY" };

    /// <summary>
    /// 생각(추론)을 얼마나 할지. 가벼운 일(제목·카드 문구)과 절약 모드는 low, 그 밖은 medium.
    /// 추론 토큰도 사용량에 들어가서, 블로그 글처럼 정답이 정해지지 않은 일에는 낮게 둔다.
    /// </summary>
    internal static string EffortFor(AiRequest request, bool saveUsage) =>
        request.ModelTier == "light" || saveUsage ? "low" : "medium";

    /// <summary>Claude 모델 이름(sonnet 등)은 codex에 넘기지 않는다 → codex 기본 모델.</summary>
    internal static string? ModelFor(AiRequest request) =>
        string.IsNullOrWhiteSpace(request.PreferredModel) || request.PreferredModel is "sonnet" or "opus" or "haiku"
            ? null : request.PreferredModel;

    internal static IReadOnlyList<string> Arguments(string outputFile, string effort, string? model)
    {
        var args = new List<string>
        {
            "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "--color", "never",
            "-c", $"model_reasoning_effort={effort}",
            "-o", outputFile,
        };
        if (model is not null) args.AddRange(new[] { "-m", model });
        args.Add("-");                                    // 프롬프트는 표준 입력으로
        return args;
    }

    internal static string ComposePrompt(AiRequest request) =>
        $"{request.System}\n\n=====\n\n{request.User}\n\n" +
        "(명령을 실행하거나 파일을 읽고 쓰지 말고, 위 출력 형식의 답만 그대로 출력하세요.)";

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
            "Codex(codex)를 찾지 못했어요. 앱 위쪽 안내에서 자동 설치 후 ChatGPT 계정으로 로그인해 주세요.");
        var workDir = Directory.CreateTempSubdirectory("blogstudio-codex-").FullName;
        try
        {
            var output = Path.Combine(workDir, "answer.md");
            var model = ModelFor(request);
            var result = await _runner.RunAsync(new ProcessSpec(exe,
                Arguments(output, EffortFor(request, _saveUsage()), model),
                ComposePrompt(request), workDir, StrippedVariables, _timeout), ct);

            var text = File.Exists(output) ? await File.ReadAllTextAsync(output, ct) : "";
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(text))
            {
                var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
                throw new AiProviderException(
                    "Codex 실행에 실패했어요. ChatGPT 로그인(로그인 창 열기)이나 사용량 한도를 확인해 주세요.\n"
                    + ClaudeCodeProvider.Trim(detail, 800));
            }
            return new AiResult(text, Name, model ?? "codex 기본");
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* 무시 */ }
        }
    }
}

/// <summary>설정의 "글 쓰는 AI"에 따라 Claude Code 또는 Codex로 보낸다 (바꾸면 바로 적용).</summary>
public sealed class EngineRouter : IAiProvider
{
    public const string Claude = "claude";
    public const string ChatGpt = "chatgpt";

    private readonly IAiProvider _claude;
    private readonly IAiProvider _chatGpt;
    private readonly Func<Storage.AppSettings> _settings;

    public EngineRouter(IAiProvider claude, IAiProvider chatGpt, Func<Storage.AppSettings> settings)
    {
        _claude = claude;
        _chatGpt = chatGpt;
        _settings = settings;
    }

    public bool IsChatGpt => _settings().Engine == ChatGpt;
    private IAiProvider Current => IsChatGpt ? _chatGpt : _claude;

    public string Name => Current.Name;
    public Task<string?> CheckAsync(CancellationToken ct = default) => Current.CheckAsync(ct);

    public Task<AiResult> GenerateAsync(AiRequest request, CancellationToken ct = default)
    {
        if (!IsChatGpt) return _claude.GenerateAsync(request, ct);
        // ChatGPT: 본 글에는 설정의 ChatGPT 모델, 가벼운 일은 codex 기본 모델 + 낮은 추론
        var model = _settings().ChatGptModel;
        return _chatGpt.GenerateAsync(request with
        {
            PreferredModel = request.ModelTier == "light" || string.IsNullOrWhiteSpace(model) ? null : model.Trim(),
        }, ct);
    }
}

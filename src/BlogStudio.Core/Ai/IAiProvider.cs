namespace BlogStudio.Core.Ai;

/// <param name="ModelTier">테마의 model_tier: main(주력) / light(가벼운 모델).</param>
/// <param name="PreferredModel">사용자 설정 모델 이름(없으면 제공자 기본값).</param>
public sealed record AiRequest(string System, string User, string ModelTier = "main", string? PreferredModel = null);

/// <param name="InputTokens">제공자가 알려 주지 않으면 null (구독 CLI 등).</param>
public sealed record AiResult(string Text, string ProviderName, string Model, int? InputTokens = null, int? OutputTokens = null);

/// <summary>AI 제공자 어댑터. 제공자가 늘어나도 앱 코드는 이 인터페이스만 안다.</summary>
public interface IAiProvider
{
    string Name { get; }

    /// <summary>사용 가능하면 버전·상태 문자열, 아니면 null.</summary>
    Task<string?> CheckAsync(CancellationToken ct = default);

    Task<AiResult> GenerateAsync(AiRequest request, CancellationToken ct = default);
}

public sealed class AiProviderException : Exception
{
    public AiProviderException(string message, Exception? inner = null) : base(message, inner) { }
}

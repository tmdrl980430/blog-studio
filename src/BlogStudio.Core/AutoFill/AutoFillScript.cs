using System.Text.RegularExpressions;

namespace BlogStudio.Core.AutoFill;

public sealed record AutoFillScript(string Text, int Version, string Source);

/// <summary>
/// 자동 채우기 스크립트(shared/autofill/autofill.js) 고르기: 앱에 든 것, 예전에 받아 둔 것, GitHub main의 최신 것 중
/// api가 앱과 같고 version이 가장 큰 것. 네이버·티스토리 화면이 바뀌어도 앱 업데이트 없이 선택자만 고쳐 내보낼 수 있다.
/// </summary>
public static class AutoFillScriptSource
{
    /// <summary>앱이 부르는 window.__bs 함수 묶음의 버전. 스크립트의 api와 같아야 쓴다.</summary>
    public const int Api = 2;

    public const string RemoteUrl =
        "https://raw.githubusercontent.com/tmdrl980430/blog-studio/main/shared/autofill/autofill.js";

    private static readonly Regex ApiLine = new(@"\bapi:\s*(\d+)");
    private static readonly Regex VersionLine = new(@"\bversion:\s*(\d+)");

    /// <summary>쓸 수 있는 스크립트면 version, 아니면 null.</summary>
    public static int? Validate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("window.__bs")) return null;
        var api = ApiLine.Match(text);
        var version = VersionLine.Match(text);
        if (!api.Success || !version.Success || int.Parse(api.Groups[1].Value) != Api) return null;
        return int.Parse(version.Groups[1].Value);
    }

    public static async Task<AutoFillScript> LoadAsync(string bundledPath, string cachePath, HttpClient? http,
        CancellationToken ct = default)
    {
        var bundledText = await File.ReadAllTextAsync(bundledPath, ct);
        var best = new AutoFillScript(bundledText, Validate(bundledText) ?? 0, "앱 기본");

        if (File.Exists(cachePath))
        {
            var cached = await File.ReadAllTextAsync(cachePath, ct);
            if (Validate(cached) is { } v && v > best.Version) best = new AutoFillScript(cached, v, "받아 둔 최신");
        }

        if (http is not null)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var remote = await http.GetStringAsync(RemoteUrl, timeout.Token);
                if (Validate(remote) is { } v && v > best.Version)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    await File.WriteAllTextAsync(cachePath, remote, ct);
                    best = new AutoFillScript(remote, v, "GitHub 최신");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && !ct.IsCancellationRequested)
            {
                // 오프라인 등 — 가진 것으로
            }
        }
        return best;
    }
}

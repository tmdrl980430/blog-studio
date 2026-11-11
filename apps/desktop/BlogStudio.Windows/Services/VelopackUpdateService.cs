using BlogStudio.Presentation;
using Velopack;
using Velopack.Sources;

namespace BlogStudio.Windows.Services;

/// <summary>
/// GitHub Releases(공개 저장소)에서 새 버전을 확인·적용한다.
/// 릴리스는 main에 merge될 때 .github/workflows/release.yml이 vpk로 만들어 올린다.
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    public const string RepoUrl = "https://github.com/tmdrl980430/blog-studio";

    private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _pending;

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<AppUpdate?> CheckAsync(CancellationToken ct = default)
    {
        if (!_manager.IsInstalled) return null;
        _pending = await _manager.CheckForUpdatesAsync();
        return _pending is null ? null : new AppUpdate(_pending.TargetFullRelease.Version.ToString());
    }

    public Task DownloadAsync(Action<int> progress, CancellationToken ct = default) =>
        _pending is null ? Task.CompletedTask : _manager.DownloadUpdatesAsync(_pending, progress, ct);

    public void ApplyAndRestart()
    {
        if (_pending is not null)
            _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}

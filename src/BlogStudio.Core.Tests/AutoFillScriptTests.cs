using System.Net;
using BlogStudio.Core.AutoFill;

namespace BlogStudio.Core.Tests;

public class AutoFillScriptTests
{
    private static string Js(int api, int version) => $"(() => {{ window.__bs = {{ api: {api}, version: {version} }}; }})();";

    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Respond = () => new HttpResponseMessage(HttpStatusCode.OK);
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(AutoFillScriptSource.RemoteUrl, request.RequestUri!.ToString());
            return Task.FromResult(Respond());
        }
    }

    private static (string Bundled, string Cache) Files()
    {
        var dir = Directory.CreateTempSubdirectory("bs-afs-").FullName;
        var bundled = Path.Combine(dir, "autofill.js");
        File.WriteAllText(bundled, Js(AutoFillScriptSource.Api, 2));
        return (bundled, Path.Combine(dir, "cache", "autofill.js"));
    }

    [Fact]
    public void Shipped_script_matches_app_api()
    {
        var shipped = File.ReadAllText(Path.Combine(Themes.ThemeRepository.LocateSharedRoot(), "autofill", "autofill.js"));
        Assert.NotNull(AutoFillScriptSource.Validate(shipped));
        Assert.Null(AutoFillScriptSource.Validate(Js(AutoFillScriptSource.Api + 1, 9)));
        Assert.Null(AutoFillScriptSource.Validate("<html>404</html>"));
    }

    [Fact]
    public async Task Newer_remote_script_is_used_and_cached_then_used_offline()
    {
        var (bundled, cache) = Files();
        var handler = new Handler { Respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Js(AutoFillScriptSource.Api, 5)) } };

        var s = await AutoFillScriptSource.LoadAsync(bundled, cache, new HttpClient(handler));
        Assert.Equal((5, "GitHub 최신"), (s.Version, s.Source));
        Assert.True(File.Exists(cache));

        handler.Respond = () => throw new HttpRequestException("offline");
        s = await AutoFillScriptSource.LoadAsync(bundled, cache, new HttpClient(handler));
        Assert.Equal((5, "받아 둔 최신"), (s.Version, s.Source));
    }

    [Fact]
    public async Task Incompatible_or_older_remote_is_ignored()
    {
        var (bundled, cache) = Files();
        foreach (var body in new[] { Js(AutoFillScriptSource.Api + 1, 50), Js(AutoFillScriptSource.Api, 1), "Not Found" })
        {
            var handler = new Handler { Respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) } };
            var s = await AutoFillScriptSource.LoadAsync(bundled, cache, new HttpClient(handler));
            Assert.Equal((2, "앱 기본"), (s.Version, s.Source));
        }
        Assert.False(File.Exists(cache));

        var none = await AutoFillScriptSource.LoadAsync(bundled, cache, null);
        Assert.Equal("앱 기본", none.Source);
    }
}

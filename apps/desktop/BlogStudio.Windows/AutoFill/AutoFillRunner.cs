using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlogStudio.Core.Themes;
using BlogStudio.Presentation;
using Microsoft.Web.WebView2.Core;

namespace BlogStudio.Windows.AutoFill;

/// <summary>
/// 글쓰기 화면을 채우는 순서. 위치 찾기는 autofill.js, 클릭·타이핑·붙여넣기는 DevTools Input 이벤트
/// (사람이 누른 것과 같은 진짜 입력)로 보낸다. 발행 버튼은 누르지 않는다.
/// </summary>
public sealed class AutoFillRunner
{
    private readonly CoreWebView2 _web;
    private readonly IClipboardService _clipboard;
    private readonly AutoFillPayload _post;
    private readonly Action<string> _log;
    private readonly Action _focusWebView;
    private static string? _script;

    public AutoFillRunner(CoreWebView2 web, IClipboardService clipboard, AutoFillPayload post,
        Action<string> log, Action focusWebView)
    {
        _web = web;
        _clipboard = clipboard;
        _post = post;
        _log = log;
        _focusWebView = focusWebView;
    }

    private static string Script => _script ??= File.ReadAllText(
        Path.Combine(ThemeRepository.LocateSharedRoot(), "autofill", "autofill.js"));

    public sealed record PageState(bool Login, bool Title, bool Body, bool Tags, bool Blocked, string Url);

    // ---------- 페이지 쪽 호출 ----------

    private async Task<JsonNode?> Js(string expression)
    {
        await _web.ExecuteScriptAsync(Script);
        var json = await _web.ExecuteScriptAsync(expression);
        return string.IsNullOrEmpty(json) || json == "null" ? null : JsonNode.Parse(json);
    }

    private async Task<(int X, int Y)?> Point(string call)
    {
        var node = await Js($"window.__bs.{call}");
        return node is JsonObject o ? ((int)o["x"]!, (int)o["y"]!) : null;
    }

    public async Task<PageState> StateAsync()
    {
        var s = await Js("window.__bs.state()") as JsonObject;
        if (s is null) return new PageState(false, false, false, false, false, _web.Source);
        return new PageState((bool)s["login"]!, (bool)s["title"]!, (bool)s["body"]!, (bool)s["tags"]!,
            (bool)s["blocked"]!, (string?)s["url"] ?? "");
    }

    /// <summary>에디터가 다 뜰 때까지 기다린다 (최대 timeout).</summary>
    public async Task<PageState> WaitReadyAsync(TimeSpan timeout, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        PageState state;
        do
        {
            state = await StateAsync();
            if (state.Login || (state.Title && state.Body)) return state;
            await Task.Delay(500, ct);
        } while (DateTime.UtcNow < until);
        return state;
    }

    // ---------- 진짜 입력 ----------

    private Task Cdp(string method, object args) =>
        _web.CallDevToolsProtocolMethodAsync(method, JsonSerializer.Serialize(args));

    private async Task ClickAsync((int X, int Y) p, int clicks = 1)
    {
        _focusWebView();
        await Cdp("Input.dispatchMouseEvent", new { type = "mouseMoved", x = p.X, y = p.Y });
        for (var i = 1; i <= clicks; i++)
        {
            await Cdp("Input.dispatchMouseEvent", new { type = "mousePressed", x = p.X, y = p.Y, button = "left", clickCount = i });
            await Cdp("Input.dispatchMouseEvent", new { type = "mouseReleased", x = p.X, y = p.Y, button = "left", clickCount = i });
        }
        await Task.Delay(250);
    }

    private Task TypeAsync(string text) => Cdp("Input.insertText", new { text });

    private async Task KeyAsync(string key, string code, int vk, int modifiers = 0, string[]? commands = null, string? text = null)
    {
        await Cdp("Input.dispatchKeyEvent", new
        {
            type = text is null ? "rawKeyDown" : "keyDown", key, code, windowsVirtualKeyCode = vk, modifiers,
            commands = commands ?? Array.Empty<string>(), text = text ?? "",
        });
        await Cdp("Input.dispatchKeyEvent", new { type = "keyUp", key, code, windowsVirtualKeyCode = vk, modifiers });
    }

    /// <summary>Ctrl+V — 에디터가 클립보드 내용을 직접 받아 처리한다 (이미지면 블로그 서버로 업로드).</summary>
    private async Task PasteAsync()
    {
        _focusWebView();
        await KeyAsync("v", "KeyV", 0x56, modifiers: 2, commands: new[] { "paste" });
    }

    // ---------- 단계 ----------

    public async Task<bool> FillTitleAsync()
    {
        await Js("window.__bs.dismiss()");
        if (await Point("titlePoint()") is not { } p) { _log("제목 칸을 찾지 못했어요 — 제목을 직접 붙여넣어 주세요 (제목은 클립보드에 있어요)"); _clipboard.SetText(_post.Title); return false; }
        await ClickAsync(p);
        if (_post.Platform == BlogPlatform.Tistory) await KeyAsync("a", "KeyA", 0x41, modifiers: 2, commands: new[] { "selectAll" });
        await TypeAsync(_post.Title);
        _log("제목 ✓");
        return true;
    }

    public async Task<bool> FillBodyAsync()
    {
        if (_post.Platform == BlogPlatform.Tistory)
        {
            var ok = await Js($"window.__bs.setBodyHtml({JsonSerializer.Serialize(_post.BodyHtml)})");
            if (ok?.GetValue<bool>() == true) { _log("본문 ✓ (표·코드 서식 유지)"); return true; }
        }

        if (await Point("bodyPoint()") is not { } p) { _log("본문 칸을 찾지 못했어요 — 본문을 클릭하고 Ctrl+V (본문은 클립보드에 있어요)"); _clipboard.SetHtml(_post.BodyHtml, _post.Title); return false; }
        await ClickAsync(p);
        _clipboard.SetHtml(_post.BodyHtml, _post.Title);
        await PasteAsync();
        await Task.Delay(1500);
        _log("본문 ✓");
        return true;
    }

    public async Task<int> FillImagesAsync(CancellationToken ct)
    {
        var done = 0;
        for (var i = 0; i < _post.ImagePaths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var n = i + 1;
            if (await Point($"selectPlaceholder({n})") is null) { _log($"이미지 {n}: 자리 표시를 찾지 못해 건너뜀"); continue; }

            _clipboard.SetImage(_post.ImagePaths[i]);
            _focusWebView();
            await Task.Delay(150, ct);
            await PasteAsync();

            // 업로드가 끝나 자리 표시가 사라질 때까지 기다린다. 이미지만 들어가고 글자가 남으면 지운다.
            var gone = false;
            for (var t = 0; t < 20 && !gone; t++)
            {
                await Task.Delay(500, ct);
                gone = (await Js($"window.__bs.hasPlaceholder({n})"))?.GetValue<bool>() != true;
            }
            if (!gone && await Point($"selectPlaceholder({n})") is not null)
                await KeyAsync("Delete", "Delete", 0x2E, commands: new[] { "deleteForward" });

            done++;
            _log($"이미지 {n}/{_post.ImagePaths.Count} ✓");
        }
        return done;
    }

    public async Task<bool> FillTagsAsync()
    {
        if (_post.Tags.Count == 0) return true;
        if (await Point("tagsPoint()") is not { } p)
        {
            _clipboard.SetText(string.Join(", ", _post.Tags));
            _log(_post.Platform == BlogPlatform.Naver
                ? "태그는 발행 창에서 넣어요 — 태그를 클립보드에 복사했어요"
                : "태그 칸을 찾지 못했어요 — 태그를 클립보드에 복사했어요");
            return false;
        }
        await ClickAsync(p);
        foreach (var tag in _post.Tags)
        {
            await TypeAsync(tag);
            await KeyAsync("Enter", "Enter", 0x0D, text: "\r");
            await Task.Delay(120);
        }
        _log("태그 ✓");
        return true;
    }

    public async Task RunAllAsync(CancellationToken ct)
    {
        var state = await WaitReadyAsync(TimeSpan.FromSeconds(20), ct);
        if (state.Login) { _log("먼저 로그인해 주세요. 로그인 후 '모두 채우기'를 다시 누르면 돼요"); return; }
        if (state.Blocked) { _log("에디터에 팝업이 떠 있어요 — 팝업을 닫고 다시 눌러 주세요"); return; }
        if (!state.Title && !state.Body) { _log("글쓰기 화면을 찾지 못했어요. 화면이 다 뜬 뒤 다시 눌러 주세요"); return; }

        await FillTitleAsync();
        await FillBodyAsync();
        if (_post.ImagePaths.Count > 0) await FillImagesAsync(ct);
        await FillTagsAsync();
        _log("완료 — 내용을 확인하고 블로그의 '발행'을 직접 눌러 주세요");
    }
}

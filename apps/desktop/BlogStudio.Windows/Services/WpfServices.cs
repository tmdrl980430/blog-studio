using System.Diagnostics;
using System.Windows;
using BlogStudio.Core.Rendering;
using BlogStudio.Presentation;

namespace BlogStudio.Windows.Services;

public sealed class WpfClipboardService : IClipboardService
{
    public void SetHtml(string html, string plainText)
    {
        var data = new DataObject();
        data.SetData(DataFormats.Html, ClipboardHtml.Build(html));
        data.SetData(DataFormats.UnicodeText, plainText);
        Clipboard.SetDataObject(data, copy: true);
    }

    public void SetText(string text) => Clipboard.SetText(text);
}

public sealed class ShellBrowserLauncher : IBrowserLauncher
{
    public void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}

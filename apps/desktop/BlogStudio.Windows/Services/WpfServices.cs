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

    public void SetImage(string path)
    {
        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;   // 파일을 잠그지 않게
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        Clipboard.SetImage(bitmap);
    }
}

public sealed class WpfFilePicker : IFilePicker
{
    public IReadOnlyList<string> PickImages()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "글에 넣을 사진 고르기",
            Filter = "사진|*.jpg;*.jpeg;*.png;*.webp;*.gif|모든 파일|*.*",
            Multiselect = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }
}

/// <summary>API 키를 현재 Windows 사용자만 풀 수 있게 DPAPI로 암호화.</summary>
public sealed class DpapiSecretProtector : BlogStudio.Core.Storage.ISecretProtector
{
    private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("BlogStudio.v1");

    public string Protect(string plain) => Convert.ToBase64String(
        System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(plain), Entropy,
            System.Security.Cryptography.DataProtectionScope.CurrentUser));

    public string Unprotect(string protectedValue) => System.Text.Encoding.UTF8.GetString(
        System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy,
            System.Security.Cryptography.DataProtectionScope.CurrentUser));
}

public sealed class ShellBrowserLauncher : IBrowserLauncher
{
    public void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}

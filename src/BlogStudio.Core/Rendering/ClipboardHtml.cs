using System.Text;

namespace BlogStudio.Core.Rendering;

/// <summary>
/// Windows 클립보드 "HTML Format"(CF_HTML) 문자열을 만든다. 오프셋은 UTF-8 바이트 기준이어야
/// 한글이 깨지지 않고 네이버·티스토리 에디터가 서식을 그대로 받는다.
/// </summary>
public static class ClipboardHtml
{
    private const string HeaderTemplate =
        "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
    private const string Prefix = "<html><head><meta charset=\"utf-8\"></head><body><!--StartFragment-->";
    private const string Suffix = "<!--EndFragment--></body></html>";

    public static string Build(string fragmentHtml)
    {
        var encoding = Encoding.UTF8;
        var headerLength = encoding.GetByteCount(string.Format(HeaderTemplate, 0, 0, 0, 0));
        var startHtml = headerLength;
        var startFragment = startHtml + encoding.GetByteCount(Prefix);
        var endFragment = startFragment + encoding.GetByteCount(fragmentHtml);
        var endHtml = endFragment + encoding.GetByteCount(Suffix);
        return string.Format(HeaderTemplate, startHtml, endHtml, startFragment, endFragment)
               + Prefix + fragmentHtml + Suffix;
    }
}

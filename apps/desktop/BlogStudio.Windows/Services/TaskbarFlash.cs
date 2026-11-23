using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BlogStudio.Windows.Services;

/// <summary>
/// 작업 표시줄 버튼 깜빡이기 — 매일 자동 작성은 창을 최소화한 채 돌므로,
/// 발행이 안 됐을 때 사용자가 돌아와서 바로 알아채게 한다. 창을 열면 멈춘다.
/// </summary>
public static class TaskbarFlash
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    private const uint FlashTray = 0x2;
    private const uint FlashUntilForeground = 0xC;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    public static void Start(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var info = new FlashInfo
            {
                Size = (uint)Marshal.SizeOf<FlashInfo>(),
                Hwnd = hwnd,
                Flags = FlashTray | FlashUntilForeground,
            };
            FlashWindowEx(ref info);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // 알림은 덤 — 실패해도 상단 경고는 그대로 보인다
        }
    }
}

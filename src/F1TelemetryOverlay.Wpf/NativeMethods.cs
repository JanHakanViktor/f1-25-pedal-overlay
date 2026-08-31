using System.Runtime.InteropServices;

namespace F1TelemetryOverlay.Wpf;

internal static class NativeMethods
{
    // Windows 11 exposes these DWM attributes for tinting the native title
    // bar. Older Windows builds return E_INVALIDARG for the newer attributes;
    // callers intentionally ignore that result and keep the normal chrome.
    internal const int DwmwaUseImmersiveDarkMode = 20;
    internal const int DwmwaBorderColor = 34;
    internal const int DwmwaCaptionColor = 35;
    internal const int DwmwaTextColor = 36;

    internal const int WmHotKey = 0x0312;
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint ModWin = 0x0008;
    internal const uint WmUser = 0x0400;

    internal static readonly int ShowOverlayMessage = RegisterWindowMessage("F1TelemetryOverlay.ShowOverlay");

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    internal static extern int DwmSetWindowAttribute(
        IntPtr hWnd,
        int dwAttribute,
        ref uint pvAttribute,
        int cbAttribute);

    internal static void ShowExistingOverlay()
    {
        IntPtr hwnd = FindWindow(null, App.OverlayTitle);
        if (hwnd != IntPtr.Zero)
        {
            PostMessage(hwnd, unchecked((uint)ShowOverlayMessage), IntPtr.Zero, IntPtr.Zero);
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AiFrame.Native;

/// <summary>
/// DWM-based window cosmetic helpers. Used by every top-level window in
/// ai-frame so the WindowStyle="None" custom chrome still gets the thin
/// system border (matching Windows Terminal) and dark-themed resize edges.
/// </summary>
internal static partial class WindowEffects
{
    // DWMWINDOWATTRIBUTE values — see <dwmapi.h>.
    // DWMWA_USE_IMMERSIVE_DARK_MODE switches the system-drawn resize border /
    // shadow over to the dark variant; DWMWA_BORDER_COLOR (Win11 22000+)
    // paints a 1-pixel border in the requested color.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    /// <summary>
    /// Enables a thin DWM border on the given window. Safe to call on
    /// older Windows versions: DwmSetWindowAttribute simply returns a
    /// non-zero HRESULT for unsupported attributes and we ignore it.
    ///
    /// <paramref name="colorBgr"/> is a Win32 COLORREF (0x00BBGGRR). The
    /// default 0x00444444 is a neutral gray that reads the same in BGR
    /// and RGB and stays subtle against both light and dark backgrounds.
    /// </summary>
    public static void EnableThinBorder(Window window, uint colorBgr = 0x00444444)
    {
        if (window is null) return;

        // Attribute calls require a live HWND; SourceInitialized is the
        // earliest WPF event after the HWND is created.
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                int dark = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

                int border = unchecked((int)colorBgr);
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
            catch
            {
                // Older Windows or restricted environment — ignore.
            }
        };
    }
}

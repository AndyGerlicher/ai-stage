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
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    // DWM_WINDOW_CORNER_PREFERENCE values.
    private const int DWMWCP_ROUND = 2;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    /// <summary>
    /// Enables a thin, dark-themed system frame on the given window.
    ///
    /// Sets DWMWA_USE_IMMERSIVE_DARK_MODE so the system-drawn resize edge,
    /// drop shadow, and caption fall back to the dark variant. DWMWA_BORDER_COLOR
    /// is intentionally not used — when the window has GlassFrameThickness=0
    /// (as our windows do), DWM has no non-client area to paint into, so that
    /// attribute is ineffective. The visible 1-pixel border itself is rendered
    /// in WPF via a &lt;Border&gt; wrapper inside each window.
    ///
    /// Safe to call on older Windows versions: DwmSetWindowAttribute simply
    /// returns a non-zero HRESULT for unsupported attributes and we ignore it.
    /// </summary>
    public static void EnableThinBorder(Window window)
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

                // Standard Windows 11 rounded corners (~8px). No-ops on Windows 10.
                int round = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            }
            catch
            {
                // Older Windows or restricted environment — ignore.
            }
        };
    }
}

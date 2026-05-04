using System.Runtime.InteropServices;

namespace Frame.Native;

internal static partial class Win32
{
    // Window relationship
    [LibraryImport("user32.dll")]
    public static partial nint SetParent(nint hWndChild, nint hWndNewParent);

    [LibraryImport("user32.dll")]
    public static partial nint GetParent(nint hWnd);

    // Window style
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    // Window position and size
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveWindow(nint hWnd, int x, int y, int width, int height,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hWnd, int nCmdShow);

    // Focus
    [LibraryImport("user32.dll")]
    public static partial nint SetFocus(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    // Window enumeration
    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static unsafe partial int GetWindowTextW(nint hWnd, char* lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    public static partial int GetWindowTextLengthW(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hWnd);

    // Constants - Window Styles
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public const nint WS_CAPTION = 0x00C00000;
    public const nint WS_THICKFRAME = 0x00040000;
    public const nint WS_CHILD = 0x40000000;
    public static readonly nint WS_POPUP = new(-2147483648); // 0x80000000
    public const nint WS_VISIBLE = 0x10000000;
    public const nint WS_SYSMENU = 0x00080000;
    public const nint WS_MINIMIZEBOX = 0x00020000;
    public const nint WS_MAXIMIZEBOX = 0x00010000;

    public const nint WS_EX_APPWINDOW = 0x00040000;
    public const nint WS_EX_TOOLWINDOW = 0x00000080;

    // Constants - ShowWindow
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SW_RESTORE = 9;

    // Window position
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    // Messages
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    // Constants - Messages
    public const uint WM_CLOSE = 0x0010;

    // Constants - SetWindowPos flags
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;

    /// <summary>
    /// Find a top-level window whose title contains the given marker string.
    /// </summary>
    public static nint FindWindowByTitleMarker(string marker)
    {
        nint found = nint.Zero;
        EnumWindows((hWnd, _) =>
        {
            int len = GetWindowTextLengthW(hWnd);
            if (len <= 0) return true;

            unsafe
            {
                char* buf = stackalloc char[len + 1];
                GetWindowTextW(hWnd, buf, len + 1);
                string title = new(buf, 0, len);

                if (title.Contains(marker, StringComparison.Ordinal))
                {
                    found = hWnd;
                    return false;
                }
            }
            return true;
        }, nint.Zero);
        return found;
    }

    /// <summary>
    /// Strip window chrome (caption, borders) and make it a child-style window.
    /// </summary>
    public static void StripChromeAndMakeChild(nint hWnd)
    {
        nint style = GetWindowLongPtr(hWnd, GWL_STYLE);
        style &= ~WS_CAPTION;
        style &= ~WS_THICKFRAME;
        style &= ~WS_SYSMENU;
        style &= ~WS_MINIMIZEBOX;
        style &= ~WS_MAXIMIZEBOX;
        style &= ~WS_POPUP;
        style |= WS_CHILD;
        SetWindowLongPtr(hWnd, GWL_STYLE, style);

        // Remove from taskbar
        nint exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
        exStyle &= ~WS_EX_APPWINDOW;
        exStyle |= WS_EX_TOOLWINDOW;
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, exStyle);
    }
}

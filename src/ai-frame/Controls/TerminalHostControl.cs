using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AiFrame.Native;
using AiFrame.Services;

namespace AiFrame.Controls;

/// <summary>
/// A WPF control that hosts a Windows Terminal window inside the WPF layout.
/// Uses HwndHost to bridge the Win32 HWND into the WPF visual tree.
/// The WT window is positioned with a negative Y offset to push its
/// tab bar / title bar out of the visible area.
/// </summary>
internal sealed class TerminalHostControl : HwndHost
{
    // Height of WT's title/tab bar in pixels at 96 DPI.
    // We shift the embedded window up by this amount to clip out WT's chrome.
    private const int WtChromeHeight96Dpi = 40;

    private readonly TerminalProcessManager _processManager;
    private nint _hostHwnd;
    private bool _attached;
    private DispatcherTimer? _resizeDebounce;

    public string TabName { get; }
    public bool IsTerminalAttached => _attached;

    public event EventHandler? TerminalLost;

    // Suppress unused event warning — will be used when crash detection is implemented
    private void OnTerminalLost() => TerminalLost?.Invoke(this, EventArgs.Empty);

    public TerminalHostControl(string tabName, string windowName)
    {
        TabName = tabName;
        _processManager = new TerminalProcessManager(windowName);
    }

    /// <summary>
    /// Launch the terminal process and embed it once the host HWND is ready.
    /// Call this after the control is loaded and BuildWindowCore has run.
    /// </summary>
    public async Task AttachTerminalAsync(string workingDirectory, string? vsDevCmdPath, TerminalLaunchSpec spec)
    {
        if (_hostHwnd == nint.Zero)
            throw new InvalidOperationException("Host HWND not yet created. Wait for control to load.");

        try
        {
            nint wtHwnd = await _processManager.LaunchAsync(workingDirectory, vsDevCmdPath, spec);

            // Strip WT chrome styles and force Windows to recalculate the frame
            Win32.StripChromeAndMakeChild(wtHwnd);
            Win32.SetWindowPos(wtHwnd, nint.Zero, 0, 0, 0, 0,
                Win32.SWP_FRAMECHANGED | Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER);

            // Reparent into our host
            Win32.SetParent(wtHwnd, _hostHwnd);

            // Fill the host area (with negative Y offset to hide WT tab bar)
            ResizeEmbeddedWindow();

            Win32.ShowWindow(wtHwnd, Win32.SW_SHOW);
            _attached = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ai-frame] Failed to attach terminal '{TabName}': {ex.Message}");
            _attached = false;
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hostHwnd = CreateHostWindow(hwndParent.Handle);
        return new HandleRef(this, _hostHwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _processManager.Dispose();
        _attached = false;
    }

    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_SIZE = 0x0005;
        if (msg == WM_SIZE && _attached)
        {
            DebouncedResize();
            handled = true;
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    /// <summary>
    /// Show or hide the embedded terminal window.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (!_attached || _processManager.Hwnd == nint.Zero) return;

        Win32.ShowWindow(_processManager.Hwnd, visible ? Win32.SW_SHOW : Win32.SW_HIDE);

        if (visible)
        {
            ResizeEmbeddedWindow();
            Win32.SetFocus(_processManager.Hwnd);
        }
    }

    /// <summary>
    /// Give focus to the embedded terminal.
    /// </summary>
    public void FocusTerminal()
    {
        if (_attached && _processManager.Hwnd != nint.Zero)
        {
            Win32.SetForegroundWindow(_processManager.Hwnd);
            Win32.SetFocus(_processManager.Hwnd);
        }
    }

    private void DebouncedResize()
    {
        if (_resizeDebounce is null)
        {
            _resizeDebounce = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60fps
            };
            _resizeDebounce.Tick += (_, _) =>
            {
                _resizeDebounce.Stop();
                ResizeEmbeddedWindow();
            };
        }

        _resizeDebounce.Stop();
        _resizeDebounce.Start();
    }

    private void ResizeEmbeddedWindow()
    {
        if (_processManager.Hwnd == nint.Zero || _hostHwnd == nint.Zero) return;

        // Convert WPF DIPs to physical pixels (DPI-aware)
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return;

        var transform = source.CompositionTarget.TransformToDevice;
        int pixelWidth = (int)(ActualWidth * transform.M11);
        int pixelHeight = (int)(ActualHeight * transform.M22);

        if (pixelWidth <= 0 || pixelHeight <= 0) return;

        // Scale the chrome offset for the current DPI
        int chromeOffset = (int)(WtChromeHeight96Dpi * transform.M22);

        // Position the WT window at negative Y to push its tab bar above the clip rect.
        // The host window clips children to its client area, so the tab bar is hidden.
        Win32.MoveWindow(_processManager.Hwnd, 0, -chromeOffset,
            pixelWidth, pixelHeight + chromeOffset, true);
    }

    private static nint CreateHostWindow(nint parentHwnd)
    {
        var parameters = new HwndSourceParameters("FrameTerminalHost")
        {
            ParentWindow = parentHwnd,
            WindowStyle = (int)(Win32.WS_CHILD | Win32.WS_VISIBLE),
            Width = 0,
            Height = 0,
        };
        var source = new HwndSource(parameters);
        return source.Handle;
    }
}

using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace AgentSessions.Internal;

/// <summary>
/// Minimal P/Invoke surface for window enumeration, focus, and parent-process
/// resolution. Kept private to the library.
/// </summary>
internal static class Win32
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    public const int SW_RESTORE = 9;
    public const uint GW_OWNER = 4;

    /// <summary>
    /// Returns the PID of the parent of <paramref name="processId"/>, or 0 if
    /// the parent can't be determined.
    /// </summary>
    public static int GetParentProcessId(int processId)
    {
        if (processId <= 0) return 0;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    return Convert.ToInt32(obj["ParentProcessId"]);
                }
            }
        }
        catch
        {
            // Process gone, access denied, or WMI unavailable — return 0.
        }
        return 0;
    }
}

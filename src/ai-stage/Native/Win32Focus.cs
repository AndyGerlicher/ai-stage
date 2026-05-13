using System;
using System.Runtime.InteropServices;

namespace AiStage.Native;

/// <summary>
/// P/Invoke surface for bringing another process's top-level window to the
/// foreground, plus a managed wrapper around <c>CommandLineToArgvW</c> for
/// matching ai-frame.exe instances by their folder argument.
/// </summary>
internal static partial class Win32Focus
{
    public const int SW_RESTORE = 9;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("shell32.dll", EntryPoint = "CommandLineToArgvW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr LocalFree(IntPtr hMem);

    /// <summary>
    /// Splits a Windows command-line string into argv tokens using the same
    /// rules CRTs use to populate <c>argv</c>. Returns an empty array on
    /// failure or null/empty input; never throws.
    /// </summary>
    public static string[] ParseCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return Array.Empty<string>();

        IntPtr argv = IntPtr.Zero;
        try
        {
            argv = CommandLineToArgvW(commandLine, out int argc);
            if (argv == IntPtr.Zero || argc <= 0)
                return Array.Empty<string>();

            var result = new string[argc];
            for (int i = 0; i < argc; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                result[i] = Marshal.PtrToStringUni(p) ?? string.Empty;
            }
            return result;
        }
        catch
        {
            return Array.Empty<string>();
        }
        finally
        {
            if (argv != IntPtr.Zero)
                LocalFree(argv);
        }
    }
}

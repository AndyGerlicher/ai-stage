using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using AiStage.Native;

namespace AiStage.Services;

/// <summary>
/// Brings a running <c>ai-frame.exe</c> window to the foreground by matching
/// the requested folder against the folder argument on each ai-frame
/// instance's command line.
///
/// This is the primary focus strategy for ai-frame-hosted sessions because
/// the agent process is *not* a descendant of ai-frame (ai-frame spawns
/// <c>wt.exe</c>, which COM-activates the packaged Windows Terminal under
/// <c>svchost.exe</c>) and the embedded Terminal HWND has been re-parented
/// into the WPF window via <c>SetParent</c>, which makes
/// <c>SetForegroundWindow</c> on it a no-op. The ai-frame top-level window
/// is the one the user actually sees and the one we need to activate.
/// </summary>
internal static class FrameWindowActivator
{
    /// <summary>
    /// If any running ai-frame.exe has <paramref name="folder"/> as its
    /// positional folder argument, restores and foregrounds its main
    /// window and returns true. Returns false on no match, no visible
    /// window yet, or any inspection failure.
    /// </summary>
    public static bool TryFocus(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;

        string target = NormalizePath(folder);
        if (target.Length == 0) return false;

        Process[] candidates;
        try
        {
            candidates = Process.GetProcessesByName("ai-frame");
        }
        catch
        {
            return false;
        }

        try
        {
            foreach (var p in candidates)
            {
                try
                {
                    if (p.HasExited) continue;
                    if (!FolderArgMatches(p.Id, target)) continue;

                    IntPtr hwnd = p.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;

                    if (Win32Focus.IsIconic(hwnd))
                        Win32Focus.ShowWindow(hwnd, Win32Focus.SW_RESTORE);
                    return Win32Focus.SetForegroundWindow(hwnd);
                }
                catch
                {
                    // Process exited mid-inspection / access denied / etc.
                }
            }
        }
        finally
        {
            foreach (var p in candidates)
            {
                try { p.Dispose(); } catch { /* best effort */ }
            }
        }

        return false;
    }

    /// <summary>
    /// Reads a process's command line via WMI and checks whether its first
    /// positional argument (after argv[0]) resolves to <paramref name="targetNormalized"/>.
    /// Treats any token starting with <c>--</c> as a flag that consumes the
    /// next token as its value — same shape every flag in
    /// <c>App.xaml.cs</c> uses, so values that themselves start with
    /// <c>--</c> (e.g. <c>--agent-launch-commands "copilot --allow-all-tools"</c>)
    /// round-trip correctly.
    /// </summary>
    private static bool FolderArgMatches(int pid, string targetNormalized)
    {
        string? commandLine = TryGetCommandLine(pid);
        if (string.IsNullOrEmpty(commandLine)) return false;

        string[] tokens = Win32Focus.ParseCommandLine(commandLine);
        // Skip argv[0] (executable path).
        for (int i = 1; i < tokens.Length; i++)
        {
            string t = tokens[i];
            if (t.StartsWith("--", StringComparison.Ordinal))
            {
                // Flag — consume value.
                i++;
                continue;
            }

            // First positional. Match folder; bail either way.
            string norm = NormalizePath(t);
            return norm.Length > 0
                && string.Equals(norm, targetNormalized, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static string? TryGetCommandLine(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    return obj["CommandLine"] as string;
                }
            }
        }
        catch (ManagementException) { }
        catch (Win32Exception) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidOperationException) { }
        return null;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        try
        {
            string full = Path.GetFullPath(path);
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}

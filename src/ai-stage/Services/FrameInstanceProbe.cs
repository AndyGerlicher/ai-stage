using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace AiStage.Services;

/// <summary>
/// Lightweight description of a running ai-frame.exe instance that came from this install.
/// </summary>
internal sealed record FrameInstance(int Pid, string ExePath, string MainWindowTitle);

/// <summary>
/// Enumerates ai-frame.exe processes spawned from the same install as the running
/// ai-stage.exe. Used to warn the user before closing ai-stage (or before kicking off a
/// Velopack apply-and-restart) — replacing the ai-frame binary on disk while a frame
/// process is alive is a recipe for torn state.
/// </summary>
internal static class FrameInstanceProbe
{
    /// <summary>
    /// Returns ai-frame.exe processes whose <c>MainModule.FileName</c> resolves to a path
    /// underneath one of our known install roots. Processes we can't inspect (e.g. higher
    /// IL than ours, or already exiting) are skipped silently. Never throws.
    /// </summary>
    public static IReadOnlyList<FrameInstance> FindRunning()
    {
        var roots = CollectInstallRoots();
        if (roots.Count == 0) return Array.Empty<FrameInstance>();

        var matches = new List<FrameInstance>();
        Process[] candidates;
        try
        {
            candidates = Process.GetProcessesByName("ai-frame");
        }
        catch
        {
            return Array.Empty<FrameInstance>();
        }

        foreach (var p in candidates)
        {
            try
            {
                string? exePath = TryGetExePath(p);
                if (string.IsNullOrEmpty(exePath)) continue;
                if (!IsUnderAnyRoot(exePath, roots)) continue;

                string title = SafeGetTitle(p);
                matches.Add(new FrameInstance(p.Id, exePath, title));
            }
            catch
            {
                // skip — process may have exited between enumeration and inspection.
            }
            finally
            {
                try { p.Dispose(); } catch { /* best effort */ }
            }
        }

        return matches;
    }

    /// <summary>
    /// Collects directories we'd consider "our" ai-frame: the directory containing the
    /// running ai-stage.exe (where the build copies ai-frame.exe via CopyFrameToOutput),
    /// and the directory of whatever <see cref="FrameLauncher.ResolveFrame"/> finds (which
    /// covers the dev <c>bin\ai-frame\ai-frame.exe</c> hop). Falls back to an empty list
    /// if neither resolves — in which case the probe returns no matches and the caller
    /// proceeds without prompting.
    /// </summary>
    private static List<string> CollectInstallRoots()
    {
        var roots = new List<string>();

        try
        {
            string? stageExe = Environment.ProcessPath;
            string? stageDir = stageExe is null ? null : Path.GetDirectoryName(stageExe);
            if (!string.IsNullOrEmpty(stageDir))
                roots.Add(NormalizeDir(stageDir));
        }
        catch { /* ignore */ }

        try
        {
            string? resolved = FrameLauncher.ResolveFrame();
            string? resolvedDir = resolved is null ? null : Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(resolvedDir))
            {
                string norm = NormalizeDir(resolvedDir);
                if (!roots.Contains(norm, StringComparer.OrdinalIgnoreCase))
                    roots.Add(norm);
            }
        }
        catch { /* ignore */ }

        return roots;
    }

    private static bool IsUnderAnyRoot(string exePath, List<string> roots)
    {
        string full;
        try { full = Path.GetFullPath(exePath); }
        catch { return false; }

        foreach (var root in roots)
        {
            // Match exact-dir or any nested dir. Compare with trailing separator so
            // "C:\App2" doesn't match root "C:\App".
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string NormalizeDir(string dir)
    {
        string full = Path.GetFullPath(dir);
        if (!full.EndsWith(Path.DirectorySeparatorChar) && !full.EndsWith(Path.AltDirectorySeparatorChar))
            full += Path.DirectorySeparatorChar;
        return full;
    }

    private static string? TryGetExePath(Process p)
    {
        try
        {
            // MainModule throws Win32Exception if the target is higher-IL or 32-bit while
            // we're 64-bit (or vice versa). Process.GetProcessesByName + MainModule is
            // still the simplest reliable path identity for processes we own.
            return p.MainModule?.FileName;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
            return null;
        }
    }

    private static string SafeGetTitle(Process p)
    {
        try
        {
            string title = p.MainWindowTitle;
            return string.IsNullOrWhiteSpace(title) ? $"PID {p.Id}" : title;
        }
        catch
        {
            return $"PID {p.Id}";
        }
    }
}

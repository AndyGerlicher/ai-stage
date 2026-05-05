using System.Diagnostics;
using static AgentSessions.Internal.Win32;

namespace AgentSessions;

/// <summary>
/// Brings the terminal window hosting an agent CLI process to the foreground.
///
/// The agent CLI runs as a console process inside a terminal host
/// (Windows Terminal / ai-frame / conhost). The session's PID points at the
/// agent executable, which has no window. We walk the parent chain looking
/// for the outermost ancestor whose process is a known terminal host and
/// has a main window — that is the window the user actually sees.
/// </summary>
public static class AgentSessionLauncher
{
    private static readonly string[] s_terminalHostNames =
    {
        "WindowsTerminal",
        "wt",
        "ai-frame",
        "conhost",
        "OpenConsole",
    };

    /// <summary>Brings the terminal window hosting <paramref name="session"/> to the front.</summary>
    public static bool TryFocus(AgentSession session) => TryFocus(session.ProcessId);

    /// <summary>Brings the terminal window hosting the agent process to the front.</summary>
    public static bool TryFocus(int agentPid)
    {
        if (agentPid <= 0) return false;

        var ancestors = new List<int>();
        int cursor = agentPid;
        for (int i = 0; i < 8 && cursor > 0; i++)
        {
            ancestors.Add(cursor);
            int next = GetParentProcessId(cursor);
            if (next == cursor || next == 0) break;
            cursor = next;
        }

        IntPtr best = IntPtr.Zero;
        int bestDepth = -1;

        for (int depth = 0; depth < ancestors.Count; depth++)
        {
            int pid = ancestors[depth];
            IntPtr hwnd;
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) continue;
                string name = p.ProcessName;
                bool isHost = false;
                foreach (var n in s_terminalHostNames)
                {
                    if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                    {
                        isHost = true;
                        break;
                    }
                }
                if (!isHost) continue;

                hwnd = p.MainWindowHandle;
            }
            catch
            {
                continue;
            }

            if (hwnd == IntPtr.Zero) continue;
            if (!IsWindowVisible(hwnd)) continue;

            if (depth > bestDepth)
            {
                best = hwnd;
                bestDepth = depth;
            }
        }

        if (best == IntPtr.Zero) return false;

        ShowWindow(best, SW_RESTORE);
        return SetForegroundWindow(best);
    }
}

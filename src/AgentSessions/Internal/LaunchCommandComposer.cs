namespace AgentSessions.Internal;

/// <summary>
/// Shared helpers for composing an <see cref="IAgentProvider"/>'s
/// <c>BuildLaunchCommand</c> output from a user-supplied multi-line launch
/// script. Each provider parses the script the same way (blank lines and
/// <c>#</c> comments ignored, lines chained with <c>&amp;&amp;</c>) and only
/// differs in how it wraps the final interactive command when an initial
/// prompt file is present.
/// </summary>
internal static class LaunchCommandComposer
{
    /// <summary>
    /// Splits <paramref name="commands"/> on newlines, trims, and drops
    /// blank lines and <c>#</c>-prefixed comments. Returns the surviving
    /// lines in input order.
    /// </summary>
    public static List<string> ParseLines(string? commands)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(commands)) return result;

        foreach (string raw in commands.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            result.Add(line);
        }
        return result;
    }

    /// <summary>
    /// Joins <paramref name="lines"/> with <c>&amp;&amp;</c>, leaving the
    /// final line as-is. The final line is the persistent interactive
    /// process; earlier lines run sequentially and a non-zero exit aborts
    /// the chain (cmd.exe semantics, matching the worktree-reset pattern).
    /// Returns an empty string when no lines are supplied.
    /// </summary>
    public static string Chain(List<string> lines)
    {
        return lines.Count == 0 ? "" : string.Join(" && ", lines);
    }

    /// <summary>
    /// Joins the first <c>n-1</c> lines with <c>&amp;&amp;</c> and returns
    /// the prefix (including the trailing <c>" &amp;&amp; "</c> separator)
    /// the caller should prepend before its provider-specific wrapper for
    /// the final line. Returns the empty string when there is no preamble.
    /// </summary>
    public static string ChainPreamble(List<string> lines)
    {
        if (lines.Count <= 1) return "";
        return string.Join(" && ", lines.Take(lines.Count - 1)) + " && ";
    }
}

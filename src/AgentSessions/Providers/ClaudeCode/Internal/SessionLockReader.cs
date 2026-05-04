using System.IO;
using System.Text.Json;

namespace AgentSessions.Providers.ClaudeCode.Internal;

/// <summary>
/// Reads a Claude Code per-process lock file (<c>~/.claude/sessions/&lt;pid&gt;.json</c>).
/// Each running interactive session writes one of these and updates it as the
/// session progresses. We only need a handful of top-level fields.
/// </summary>
internal static class SessionLockReader
{
    public sealed record SessionLockInfo(
        int Pid,
        string SessionId,
        string Cwd,
        string? Name,
        string? Status);

    /// <summary>
    /// Parses the lock file at <paramref name="path"/>. Returns null if the
    /// file is missing, unreadable, malformed, or missing required fields.
    /// </summary>
    public static SessionLockInfo? TryRead(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            // Share read+write+delete: Claude is the writer, we are the reader,
            // and the file may be deleted out from under us when the process exits.
            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            using var doc = JsonDocument.Parse(fs);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            int pid = TryGetInt(root, "pid") ?? 0;
            string? sessionId = TryGetString(root, "sessionId");
            string? cwd = TryGetString(root, "cwd");
            string? name = TryGetString(root, "name");
            string? status = TryGetString(root, "status");

            if (pid <= 0 || string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(cwd))
                return null;

            return new SessionLockInfo(pid, sessionId, cwd, name, status);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    private static string? TryGetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static int? TryGetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int v)
            ? v
            : null;
}

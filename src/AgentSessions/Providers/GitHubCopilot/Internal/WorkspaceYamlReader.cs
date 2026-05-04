using System.IO;

namespace AgentSessions.Providers.GitHubCopilot.Internal;

/// <summary>
/// Tiny purpose-built reader for the workspace.yaml that the Copilot CLI
/// writes alongside every session. We only need a flat key/value scan and
/// don't want a YAML dependency.
/// </summary>
internal static class WorkspaceYamlReader
{
    public sealed record WorkspaceInfo(
        string? Cwd,
        string? GitRoot,
        string? Branch,
        string? Repository,
        string? Summary);

    public static WorkspaceInfo? TryRead(string path)
    {
        if (!File.Exists(path))
            return null;

        string? cwd = null, gitRoot = null, branch = null, repo = null, summary = null;

        try
        {
            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            string? line;
            while ((line = sr.ReadLine()) is not null)
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line[..colon].Trim();
                string value = line[(colon + 1)..].Trim();
                switch (key)
                {
                    case "cwd": cwd = value; break;
                    case "git_root": gitRoot = value; break;
                    case "branch": branch = value; break;
                    case "repository": repo = value; break;
                    case "summary": summary = value; break;
                }
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        if (string.IsNullOrEmpty(cwd))
            return null;

        return new WorkspaceInfo(cwd, gitRoot, branch, repo, summary);
    }
}

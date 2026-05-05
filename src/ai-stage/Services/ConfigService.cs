using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AgentSessions;

namespace AiStage.Services;

internal sealed class StageConfig
{
    public const string DefaultBranchPrefix = "";

    /// <summary>Default reset commands run by ai-stage's "Reset worktree" action,
    /// one per line. <c>&lt;new-branch&gt;</c> is substituted with the resolved
    /// (prefix + suffix) branch name. Each line is executed via <c>cmd.exe /c</c>
    /// inside the worktree directory; the first non-zero exit aborts.</summary>
    public const string DefaultWorktreeResetCommands =
        "git fetch\n" +
        "git checkout -B <new-branch> origin/main\n" +
        "git clean -fdx";

    public string RootPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Optional registered agent provider id (e.g. <c>"claude-code"</c>) that
    /// ai-stage forwards to ai-frame via <c>--agent &lt;id&gt;</c> when opening a
    /// folder that has no live session. Null = let ai-frame use its own default
    /// (currently GitHub Copilot). When a folder already has a live session
    /// from any provider, that session's provider always wins.
    /// </summary>
    public string? DefaultAgentProvider { get; set; }

    /// <summary>
    /// Prefix applied to all worktree branch names created by ai-stage
    /// (e.g. <c>"dev/angerlic/"</c> produces branches like
    /// <c>dev/angerlic/feature-x</c>).
    /// <para>
    /// JSON semantics: <b>missing key, <c>null</c>, or empty string</b> means
    /// no prefix (branches use the suffix as-is); any other value is
    /// normalized (leading <c>/</c> stripped, trailing <c>/</c> appended)
    /// by <see cref="ConfigService.Load"/>.
    /// </para>
    /// </summary>
    public string? BranchPrefix { get; set; }

    /// <summary>
    /// Multi-line list of commands ai-stage runs when the user resets a
    /// worktree, one per line. The token <c>&lt;new-branch&gt;</c> is replaced
    /// with the full branch name (prefix + suffix) at run time. Each line is
    /// executed via <c>cmd.exe /c</c> in the worktree's working directory; the
    /// first non-zero exit aborts and the failing command's stderr is shown.
    /// <para>Null falls back to <see cref="DefaultWorktreeResetCommands"/>.</para>
    /// </summary>
    public string? WorktreeResetCommands { get; set; }

    /// <summary>
    /// Which shell ai-frame's Console tab launches:
    /// <c>"VsDevCmd"</c> (default; cmd.exe + VS Developer Command Prompt),
    /// <c>"PowerShell"</c>, or <c>"Cmd"</c>.
    /// </summary>
    public string? ConsoleShell { get; set; }

    /// <summary>
    /// Optional command line that runs in the Console tab after the chosen
    /// shell finishes its own startup.
    /// </summary>
    public string? ConsoleInitCommand { get; set; }

    /// <summary>
    /// Per-provider CLI argument overrides, keyed by <see cref="IAgentProvider.Id"/>.
    /// Each value is the extra args ai-frame appends to that provider's
    /// invocation (e.g. <c>"--allow-all-tools"</c> for github-copilot).
    /// <para>
    /// Lookup semantics: <b>missing key</b> means "use the provider's
    /// <see cref="IAgentProvider.DefaultExtraArgs"/>"; an <b>explicit empty
    /// string</b> means "pass nothing extra" so users can opt out of the
    /// default flags entirely.
    /// </para>
    /// </summary>
    public Dictionary<string, string> AgentArgs { get; set; } = new();
}

internal static class ConfigService
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ai-stage", "config.json");

    /// <summary>
    /// True when a persisted config file already exists on disk. Used by
    /// <c>App.OnStartup</c> to detect first run and show the settings dialog.
    /// </summary>
    public static bool Exists() => File.Exists(StorePath);

    public static StageConfig Load()
    {
        StageConfig config;
        try
        {
            config = File.Exists(StorePath)
                ? JsonSerializer.Deserialize<StageConfig>(File.ReadAllText(StorePath)) ?? new StageConfig()
                : new StageConfig();
        }
        catch
        {
            config = new StageConfig();
        }

        config.BranchPrefix = NormalizeBranchPrefix(config.BranchPrefix);
        return config;
    }

    private static string NormalizeBranchPrefix(string? value)
    {
        string trimmed = (value ?? "").Trim().TrimStart('/');
        if (trimmed.Length == 0)
            return "";
        return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
    }

    public static void Save(StageConfig config)
    {
        try
        {
            string dir = Path.GetDirectoryName(StorePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best effort — don't crash if we can't write
        }
    }
}

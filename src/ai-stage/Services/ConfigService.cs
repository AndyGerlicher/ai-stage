using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AgentSessions;

namespace AiStage.Services;

internal sealed class StageConfig
{
    public const string DefaultBranchPrefix = "";

    /// <summary>
    /// Fallback branch name used when <see cref="DefaultBranch"/> is null/empty.
    /// Kept as <c>"main"</c> so existing installs keep working unchanged.
    /// </summary>
    public const string DefaultBranchFallback = "main";

    /// <summary>Default reset commands run by ai-stage's "Reset worktree" action,
    /// one per line. Substitutions:
    /// <list type="bullet">
    /// <item><c>&lt;new-branch&gt;</c> — local branch name to land on (full,
    /// prefix-included for "new branch" mode; the chosen remote branch name
    /// for "existing"/"default" modes).</item>
    /// <item><c>&lt;target-ref&gt;</c> — git ref to reset onto, typically
    /// <c>origin/&lt;DefaultBranch&gt;</c> for new/default modes or
    /// <c>origin/&lt;chosen-branch&gt;</c> for existing mode.</item>
    /// </list>
    /// Each line is executed via <c>cmd.exe /c</c> inside the worktree
    /// directory; the first non-zero exit aborts.</summary>
    public const string DefaultWorktreeResetCommands =
        "git fetch\n" +
        "git checkout -B <new-branch> <target-ref>\n" +
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
    /// Name of the branch ai-stage syncs new and reset worktrees to (defaults
    /// to <c>main</c> when null/empty). Referenced as <c>origin/&lt;DefaultBranch&gt;</c>
    /// in git operations. Used by both the "New worktree" flow and the
    /// "New branch" / "Default branch" reset modes.
    /// </summary>
    public string? DefaultBranch { get; set; }

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
    /// Command name ai-frame's "Open in editor" toolbar button invokes.
    /// Null = default (<c>"code-insiders"</c>); <c>"code"</c> selects stable VS Code.
    /// Persisted as null when the default is chosen so future default changes flow through.
    /// </summary>
    public string? PreferredEditor { get; set; }

    /// <summary>
    /// Per-provider launch-command scripts, keyed by <see cref="IAgentProvider.Id"/>.
    /// Each value is a multi-line script (one command per line; blank lines
    /// and <c>#</c> comments are ignored) that ai-frame runs in the agent
    /// terminal. Non-empty lines are chained with <c>&amp;&amp;</c> in order
    /// — the last line is the persistent interactive agent process; earlier
    /// lines (e.g. <c>copilot update</c>) run first and short-circuit on
    /// non-zero exit (cmd.exe semantics, matching worktree-reset).
    /// <para>
    /// Lookup semantics: <b>missing key</b> means "use the provider's
    /// <see cref="IAgentProvider.DefaultLaunchCommands"/>"; a non-null value
    /// always replaces the default in full.
    /// </para>
    /// </summary>
    public Dictionary<string, string> AgentLaunchCommands { get; set; } = new();

    /// <summary>
    /// Legacy per-provider CLI argument overrides (pre-launch-commands).
    /// Read by <see cref="ConfigService.Load"/> and migrated into
    /// <see cref="AgentLaunchCommands"/> on first load, then dropped on the
    /// next save. Kept on the type so existing JSON deserializes cleanly;
    /// new code should always use <see cref="AgentLaunchCommands"/>.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("AgentArgs")]
    public Dictionary<string, string>? LegacyAgentArgs { get; set; }
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
        config.DefaultBranch = NormalizeDefaultBranch(config.DefaultBranch);
        MigrateLegacyAgentArgs(config);
        return config;
    }

    /// <summary>
    /// One-shot migration: translate any legacy <c>AgentArgs</c> entries into
    /// <see cref="StageConfig.AgentLaunchCommands"/> by reconstructing the
    /// historical command shape (<c>copilot &lt;args&gt;</c> / <c>claude
    /// &lt;args&gt;</c>) for known provider ids. Unknown ids are passed
    /// through verbatim as bare args (best-effort, no data loss).
    /// New entries in <c>AgentLaunchCommands</c> always win — we only fill
    /// in keys the user hasn't already set via the new schema.
    /// After migration the legacy field is cleared so the next
    /// <see cref="Save"/> drops it from disk.
    /// </summary>
    private static void MigrateLegacyAgentArgs(StageConfig config)
    {
        // Guard against malformed JSON that explicitly sets AgentLaunchCommands
        // to null (deserializer won't apply the property initializer in that case).
        config.AgentLaunchCommands ??= new Dictionary<string, string>();

        if (config.LegacyAgentArgs is null)
            return;

        foreach (var (id, args) in config.LegacyAgentArgs)
        {
            if (config.AgentLaunchCommands.ContainsKey(id))
                continue; // user already set the new field for this provider

            string baseCmd = id switch
            {
                "github-copilot" => "copilot",
                "claude-code" => "claude",
                _ => id, // unknown — preserve whatever the user had
            };

            string trimmed = (args ?? "").Trim();
            config.AgentLaunchCommands[id] = trimmed.Length == 0 ? baseCmd : $"{baseCmd} {trimmed}";
        }

        // Always clear so the legacy "AgentArgs" key drops out of the next
        // save, whether it had entries or was just an empty dict left over.
        config.LegacyAgentArgs = null;
    }

    private static string NormalizeBranchPrefix(string? value)
    {
        string trimmed = (value ?? "").Trim().TrimStart('/');
        if (trimmed.Length == 0)
            return "";
        return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
    }

    private static string NormalizeDefaultBranch(string? value)
    {
        string trimmed = (value ?? "").Trim().Trim('/');
        // Tolerate a user who typed "origin/main" — strip the redundant
        // remote name so callers can build "origin/<DefaultBranch>" without
        // producing "origin/origin/main".
        if (trimmed.StartsWith("origin/", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["origin/".Length..].Trim('/');
        return trimmed.Length == 0 ? StageConfig.DefaultBranchFallback : trimmed;
    }

    public static void Save(StageConfig config)
    {
        try
        {
            string dir = Path.GetDirectoryName(StorePath)!;
            Directory.CreateDirectory(dir);
            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                // Drop the legacy AgentArgs key (and any other null-valued
                // properties) from the on-disk JSON once migration has run.
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            };
            File.WriteAllText(StorePath, JsonSerializer.Serialize(config, opts));
        }
        catch
        {
            // Best effort — don't crash if we can't write
        }
    }
}

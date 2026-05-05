using System.IO;
using System.Text.Json;

namespace Stage.Services;

internal sealed class StageConfig
{
    public const string DefaultBranchPrefix = "dev/angerlic/";

    public string RootPath { get; set; } = @"D:\src";

    /// <summary>
    /// Optional registered agent provider id (e.g. <c>"claude-code"</c>) that
    /// Stage forwards to Frame via <c>--agent &lt;id&gt;</c> when opening a
    /// folder that has no live session. Null = let Frame use its own default
    /// (currently GitHub Copilot). When a folder already has a live session
    /// from any provider, that session's provider always wins.
    /// </summary>
    public string? DefaultAgentProvider { get; set; }

    /// <summary>
    /// Prefix applied to all worktree branch names created by Stage
    /// (e.g. <c>"dev/angerlic/"</c> produces branches like
    /// <c>dev/angerlic/feature-x</c>).
    /// <para>
    /// JSON semantics: <b>missing key or <c>null</c></b> falls back to
    /// <see cref="DefaultBranchPrefix"/>; <b>empty string <c>""</c></b> is
    /// honored as "no prefix" (branches use the suffix as-is); any other
    /// value is normalized (leading <c>/</c> stripped, trailing <c>/</c>
    /// appended) by <see cref="ConfigService.Load"/>.
    /// </para>
    /// </summary>
    public string? BranchPrefix { get; set; }
}

internal static class ConfigService
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Stage", "config.json");

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
        // null = key absent (or explicit JSON null) → fall back to default.
        // Empty string = explicit "no prefix" → honored as-is.
        if (value is null)
            return StageConfig.DefaultBranchPrefix;
        if (value.Length == 0)
            return "";

        string trimmed = value.Trim().TrimStart('/');
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

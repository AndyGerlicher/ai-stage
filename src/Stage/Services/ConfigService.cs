using System.IO;
using System.Text.Json;

namespace Stage.Services;

internal sealed class StageConfig
{
    public string RootPath { get; set; } = @"D:\src";

    /// <summary>
    /// Optional registered agent provider id (e.g. <c>"claude-code"</c>) that
    /// Stage forwards to Frame via <c>--agent &lt;id&gt;</c> when opening a
    /// folder that has no live session. Null = let Frame use its own default
    /// (currently GitHub Copilot). When a folder already has a live session
    /// from any provider, that session's provider always wins.
    /// </summary>
    public string? DefaultAgentProvider { get; set; }
}

internal static class ConfigService
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Stage", "config.json");

    public static StageConfig Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return new StageConfig();

            string json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<StageConfig>(json) ?? new StageConfig();
        }
        catch
        {
            return new StageConfig();
        }
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

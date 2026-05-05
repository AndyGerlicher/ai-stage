using System.IO;
using System.Text.Json;

namespace AiFrame.Services;

/// <summary>
/// Tracks recently opened folders, persisted to %LOCALAPPDATA%\ai-frame\recent.json.
/// </summary>
internal sealed class RecentFoldersService
{
    private const int MaxEntries = 15;

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ai-frame", "recent.json");

    public List<string> GetRecent()
    {
        try
        {
            if (!File.Exists(StorePath))
                return [];

            string json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Add(string folderPath)
    {
        folderPath = Path.GetFullPath(folderPath);

        var list = GetRecent();

        // Remove existing entry (case-insensitive) so it moves to top
        list.RemoveAll(f => string.Equals(f, folderPath, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, folderPath);

        // Trim to max
        if (list.Count > MaxEntries)
            list.RemoveRange(MaxEntries, list.Count - MaxEntries);

        try
        {
            string dir = Path.GetDirectoryName(StorePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best effort — don't crash if we can't write history
        }
    }
}

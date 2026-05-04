using System.IO;
using System.Text;

namespace Stage.Services;

/// <summary>
/// Persists initial-prompt text to %LOCALAPPDATA%\Stage\prompts\&lt;guid&gt;.txt
/// so Stage can hand a path off to Frame via --initial-prompt-file.
/// Files are consumed (deleted) by Frame after use.
/// </summary>
internal static class PromptFileService
{
    private static readonly string PromptsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Stage", "prompts");

    /// <summary>
    /// Writes <paramref name="promptText"/> to a new UTF-8 (no BOM) file and returns its absolute path.
    /// Returns null if the prompt is empty/whitespace or the write fails.
    /// </summary>
    public static string? Write(string? promptText)
    {
        if (string.IsNullOrWhiteSpace(promptText))
            return null;

        try
        {
            Directory.CreateDirectory(PromptsDir);
            string path = Path.Combine(PromptsDir, $"{Guid.NewGuid():N}.txt");
            File.WriteAllText(path, promptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch
        {
            return null;
        }
    }
}

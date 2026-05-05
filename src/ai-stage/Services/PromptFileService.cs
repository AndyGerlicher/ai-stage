using System.IO;
using System.Text;

namespace AiStage.Services;

/// <summary>
/// Persists initial-prompt text to %LOCALAPPDATA%\ai-stage\prompts\&lt;guid&gt;.txt
/// so ai-stage can hand a path off to ai-frame via --initial-prompt-file.
/// Files are consumed (deleted) by ai-frame after use.
/// </summary>
internal static class PromptFileService
{
    private static readonly string PromptsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ai-stage", "prompts");

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

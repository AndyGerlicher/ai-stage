namespace AiFrame.Services;

/// <summary>Which shell ai-frame's Console tab launches.</summary>
public enum ConsoleShell
{
    /// <summary>cmd.exe with VS Developer Command Prompt env loaded (default).</summary>
    VsDevCmd,
    /// <summary>pwsh.exe (PowerShell).</summary>
    Pwsh,
    /// <summary>powershell.exe (Old PowerShell).</summary>
    PowerShell,
    /// <summary>cmd.exe (no VsDevCmd env).</summary>
    Cmd,
}

/// <summary>
/// Describes how a single terminal tab should launch: which shell to host
/// inside Windows Terminal, and an optional command line to execute after
/// the shell finishes its own startup.
/// </summary>
internal sealed record TerminalLaunchSpec(ConsoleShell Shell, string? InitCommand)
{
    /// <summary>Default Console-tab spec (VsDevCmd, no init command).</summary>
    public static TerminalLaunchSpec ConsoleDefault { get; } = new(ConsoleShell.VsDevCmd, null);

    /// <summary>Spec for the agent tab: VsDevCmd shell, agent CLI as the init command.</summary>
    public static TerminalLaunchSpec ForAgent(ConsoleShell consoleShell, string agentCommand) =>
        new(consoleShell, agentCommand);
}

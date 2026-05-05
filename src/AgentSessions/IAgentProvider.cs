namespace AgentSessions;

/// <summary>
/// Describes one supported agent CLI (e.g. GitHub Copilot CLI, Claude Code).
/// Hosts use this to launch the agent and to monitor its sessions without
/// hard-coding any provider-specific knowledge.
/// </summary>
public interface IAgentProvider
{
    /// <summary>Stable identifier, e.g. <c>"github-copilot"</c>. Used as a CLI flag value.</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in UIs (e.g. used as the ai-frame tab title).</summary>
    string DisplayName { get; }

    /// <summary>
    /// The provider's recommended default flags appended to its CLI invocation
    /// when the host doesn't override (e.g. <c>"--allow-all-tools"</c> for
    /// GitHub Copilot, <c>"--dangerously-skip-permissions"</c> for Claude).
    /// Used by the settings UI to pre-fill defaults and by
    /// <see cref="GetLaunchCommand"/> when <c>extraArgs</c> is null.
    /// </summary>
    string DefaultExtraArgs { get; }

    /// <summary>
    /// Returns the full shell command line ai-frame should run inside its
    /// terminal to start the agent CLI in interactive mode. Providers own
    /// their own quoting and are responsible for handling the optional
    /// <paramref name="initialPromptFile"/> (typically a UTF-8 text file the
    /// host wants the agent to consume on startup).
    ///
    /// <para><paramref name="extraArgs"/> is an optional, host-supplied
    /// argument string the provider should append to its CLI invocation.
    /// When null, providers fall back to <see cref="DefaultExtraArgs"/>;
    /// an explicit empty string means "no extra args".</para>
    /// </summary>
    string GetLaunchCommand(string? initialPromptFile, string? extraArgs = null);

    /// <summary>
    /// Creates a new session store for this provider. The caller owns the
    /// returned store and must dispose it. Implementations should return a
    /// fresh store on every call. Snapshots returned by the store and
    /// payloads passed to <see cref="IAgentSessionStore.SnapshotChanged"/>
    /// must be immutable point-in-time views.
    /// </summary>
    IAgentSessionStore CreateSessionStore();
}


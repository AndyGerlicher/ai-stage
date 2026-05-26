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
    /// The provider's default multi-line launch script — what the agent tab
    /// runs when the user hasn't customized it. One command per line; blank
    /// lines and <c>#</c> comments are ignored. The last non-empty line is
    /// the persistent interactive process (e.g. <c>"copilot --allow-all-tools"</c>
    /// for GitHub Copilot, <c>"claude"</c> for Claude Code).
    /// </summary>
    string DefaultLaunchCommands { get; }

    /// <summary>
    /// Returns the full shell command line ai-frame should run inside its
    /// terminal to start the agent CLI in interactive mode.
    ///
    /// <para><paramref name="commands"/> is the multi-line launch script
    /// (one command per line). Non-empty / non-comment lines are chained with
    /// <c>&amp;&amp;</c> so a failing earlier line short-circuits the chain;
    /// the last line is the persistent interactive process.</para>
    ///
    /// <para>When <paramref name="initialPromptFile"/> is non-null and exists,
    /// the provider wraps only the final command in a shim that reads the
    /// prompt file, deletes it, and forwards the text to the agent in the
    /// provider-appropriate form (e.g. <c>-i</c> for Copilot, positional for
    /// Claude). Earlier commands run unmodified.</para>
    /// </summary>
    string BuildLaunchCommand(string? initialPromptFile, string commands);

    /// <summary>
    /// Creates a new session store for this provider. The caller owns the
    /// returned store and must dispose it. Implementations should return a
    /// fresh store on every call. Snapshots returned by the store and
    /// payloads passed to <see cref="IAgentSessionStore.SnapshotChanged"/>
    /// must be immutable point-in-time views.
    /// </summary>
    IAgentSessionStore CreateSessionStore();
}


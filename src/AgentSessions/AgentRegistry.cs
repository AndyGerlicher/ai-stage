using AgentSessions.Providers.ClaudeCode;
using AgentSessions.Providers.GitHubCopilot;

namespace AgentSessions;

/// <summary>
/// Static registry of <see cref="IAgentProvider"/>s. The GitHub Copilot
/// provider is registered automatically as <see cref="Default"/>; the Claude
/// Code provider is also registered and can be selected per-session by id
/// (or made the default by hosts via <see cref="SetDefault"/>).
/// </summary>
public static class AgentRegistry
{
    private static readonly object s_gate = new();
    private static readonly List<IAgentProvider> s_providers = new();
    private static IAgentProvider? s_default;

    static AgentRegistry()
    {
        // The GitHub Copilot provider is the default and is always registered.
        var ghCopilot = new GitHubCopilotProvider();
        s_providers.Add(ghCopilot);
        s_default = ghCopilot;

        // Claude Code is also always registered. It is selected by id, either
        // via the ai-frame --agent flag or by a host calling SetDefault.
        s_providers.Add(new ClaudeCodeProvider());
    }

    /// <summary>The default provider used when no explicit selection is made.</summary>
    public static IAgentProvider Default
    {
        get
        {
            lock (s_gate)
            {
                return s_default ?? throw new InvalidOperationException("No default provider registered.");
            }
        }
    }

    /// <summary>All currently registered providers, in registration order.</summary>
    public static IReadOnlyList<IAgentProvider> Providers
    {
        get
        {
            lock (s_gate)
            {
                return s_providers.ToArray();
            }
        }
    }

    /// <summary>Looks up a provider by its <see cref="IAgentProvider.Id"/> (case-insensitive).</summary>
    public static IAgentProvider? Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (s_gate)
        {
            foreach (var p in s_providers)
            {
                if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
        }
        return null;
    }

    /// <summary>
    /// Registers an additional provider. Throws if another provider with the
    /// same id is already registered.
    /// </summary>
    public static void Register(IAgentProvider provider)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));
        if (string.IsNullOrEmpty(provider.Id))
            throw new ArgumentException("Provider Id must be non-empty.", nameof(provider));

        lock (s_gate)
        {
            foreach (var p in s_providers)
            {
                if (string.Equals(p.Id, provider.Id, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"An agent provider with id '{provider.Id}' is already registered.");
            }
            s_providers.Add(provider);
        }
    }

    /// <summary>
    /// Replaces the default provider. The new default must already be registered.
    /// </summary>
    public static void SetDefault(string id)
    {
        var p = Get(id) ?? throw new InvalidOperationException(
            $"No registered agent provider with id '{id}'.");
        lock (s_gate) s_default = p;
    }

    /// <summary>
    /// Creates a single <see cref="IAgentSessionStore"/> that aggregates the
    /// session stores of every registered provider. The returned store owns
    /// the inner stores and disposes them when disposed.
    /// </summary>
    public static IAgentSessionStore CreateAggregateStore()
    {
        IAgentProvider[] providers;
        lock (s_gate)
        {
            providers = s_providers.ToArray();
        }

        var inner = new IAgentSessionStore[providers.Length];
        for (int i = 0; i < providers.Length; i++)
            inner[i] = providers[i].CreateSessionStore();

        return new AggregateAgentSessionStore(inner);
    }
}

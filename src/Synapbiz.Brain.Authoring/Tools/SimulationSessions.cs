using System.Globalization;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Authoring;

/// <summary>A made-up case being played on a draft, kept in memory.</summary>
/// <param name="Token">The token the assistant uses to refer to the session.</param>
/// <param name="DraftId">The draft the case runs on.</param>
/// <param name="DraftHash">The content hash of the draft text the case runs on; the case keeps running on that text even if the draft is saved again.</param>
/// <param name="Simulation">The simulation.</param>
/// <param name="LastUsed">When the session was last used, by the injected clock.</param>
internal sealed record SimulationSession(string Token, string DraftId, string DraftHash, FlowSimulation Simulation, DateTimeOffset LastUsed);

/// <summary>
/// The simulation sessions of one tool set. They live in memory only and expire after a time of not being used; the time comes from the
/// injected <see cref="TimeProvider"/>, never from the machine clock. The number of sessions is bounded.
/// </summary>
internal sealed class SimulationSessions
{
    private readonly object gate = new();
    private readonly Dictionary<string, SimulationSession> sessions = new(StringComparer.Ordinal);
    private readonly TimeProvider clock;
    private readonly TimeSpan lifetime;
    private readonly int capacity;
    private readonly Func<string>? ids;
    private int counter;

    internal SimulationSessions(TimeProvider clock, TimeSpan lifetime, int capacity, Func<string>? ids = null)
    {
        this.ids = ids;
        this.clock = clock;
        this.lifetime = lifetime;
        this.capacity = capacity;
    }

    internal int Count
    {
        get
        {
            lock (gate)
            {
                Purge();
                return sessions.Count;
            }
        }
    }

    /// <summary>Stores a new session; null when the limit of live sessions is reached.</summary>
    internal SimulationSession? Add(string draftId, string draftHash, FlowSimulation simulation)
    {
        lock (gate)
        {
            Purge();
            if (sessions.Count >= capacity)
            {
                return null;
            }

            string token;
            do
            {
                counter++;
                token = ids is null ? "sim-" + counter.ToString(CultureInfo.InvariantCulture) : ids();
            }
            while (sessions.ContainsKey(token));
            var session = new SimulationSession(token, draftId, draftHash, simulation, clock.GetUtcNow());
            sessions[token] = session;
            return session;
        }
    }

    /// <summary>The live session of the token, marked as used now; null when it is unknown or expired.</summary>
    internal SimulationSession? Touch(string token)
    {
        lock (gate)
        {
            Purge();
            if (!sessions.TryGetValue(token, out var session))
            {
                return null;
            }

            var touched = session with { LastUsed = clock.GetUtcNow() };
            sessions[token] = touched;
            return touched;
        }
    }

    internal bool Remove(string token)
    {
        lock (gate)
        {
            return sessions.Remove(token);
        }
    }

    private void Purge()
    {
        var now = clock.GetUtcNow();
        foreach (var token in sessions.Where(pair => now - pair.Value.LastUsed > lifetime).Select(pair => pair.Key).ToList())
        {
            sessions.Remove(token);
        }
    }
}

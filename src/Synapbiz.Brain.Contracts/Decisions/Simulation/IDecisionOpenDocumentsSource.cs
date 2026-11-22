using Nuvora.Decisions.Contracts.Kinds;

namespace Nuvora.Decisions.Contracts.Simulation;

/// <summary>
/// Counts the open documents a domain key (for example a sales channel) still carries, so a change proposal can warn
/// that those documents keep the decision they were pinned with. Registered by the module that owns the kind.
/// </summary>
public interface IDecisionOpenDocumentsSource
{
    /// <summary>The kind the source serves.</summary>
    DecisionKindKey KindKey { get; }

    /// <summary>Number of open documents for <paramref name="domainKey"/>.</summary>
    Task<int> CountOpenAsync(string domainKey, CancellationToken ct = default);
}

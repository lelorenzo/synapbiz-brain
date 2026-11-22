using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Nuvora.Decisions.Contracts.Materialization;

/// <summary>One member of the finite domain of a Materialized kind: its key, its facts and who decided it last.</summary>
/// <param name="DomainKey">Stable key of the member inside the owning module (for example a sales channel id).</param>
/// <param name="Facts">The fact values the kind's table is evaluated against for this member.</param>
/// <param name="DecidedByVersionId">The ruleset version that decided the member's stored row, or null when none did yet.</param>
public sealed record DecisionDomainEntry(
    string DomainKey,
    IReadOnlyDictionary<string, DecisionValue> Facts,
    Guid? DecidedByVersionId);

/// <summary>
/// Enumerates the finite domain of one Materialized decision kind for the current tenant (D-07). Implemented and
/// registered by the module that owns the kind; the reconciler compares each entry's decided version with the Active one.
/// </summary>
public interface IDecisionDomainProvider
{
    /// <summary>The kind whose domain this provider enumerates.</summary>
    DecisionKindKey KindKey { get; }

    /// <summary>Every member of the domain with the version that decided it last.</summary>
    Task<IReadOnlyList<DecisionDomainEntry>> EnumerateAsync(CancellationToken cancellationToken);
}

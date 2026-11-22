using Nuvora.Decisions.Contracts.Kinds;

namespace Nuvora.Decisions.Contracts.Materialization;

/// <summary>The version a materializer must decide with.</summary>
/// <param name="VersionId">The Active version.</param>
/// <param name="VersionNumber">Its number inside the ruleset.</param>
public sealed record MaterializationTarget(Guid VersionId, int VersionNumber);

/// <summary>
/// Evaluates the Active version over the domain of a Materialized kind and writes the decided rows (D-07). It is the
/// only writer of those rows and is idempotent: deciding the same key with the same version twice leaves the same row.
/// Implemented and registered by the module that owns the kind.
/// </summary>
public interface IDecisionMaterializer
{
    /// <summary>The kind this materializer serves.</summary>
    DecisionKindKey KindKey { get; }

    /// <summary>
    /// Decides <paramref name="domainKeys"/> (null means the whole domain) with <paramref name="active"/> and returns,
    /// for each key it materialized, the version id that decided it.
    /// </summary>
    Task<IReadOnlyDictionary<string, Guid>> MaterializeAsync(
        MaterializationTarget active,
        IReadOnlyCollection<string>? domainKeys,
        CancellationToken cancellationToken);
}

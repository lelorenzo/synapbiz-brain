using Nuvora.Decisions.Contracts.Kinds;

namespace Nuvora.Decisions.Contracts.Evaluation;

/// <summary>
/// Immutable record of one decision evaluation (D-05/D-10): which kind and
/// version decided it, which row matched, the canonical fact snapshot and its
/// hash, and the traces of any composed child evaluations (D-14). Persisted
/// by the owning module's schema in the same unit of work as the consequence
/// it explains (D-06) — this type itself carries no persistence concern.
/// </summary>
public sealed record DecisionTrace
{
    public required DecisionKindKey KindKey { get; init; }

    /// <summary>Null in this pure engine (no ruleset versions yet — wired by P2-20.3).</summary>
    public Guid? VersionId { get; init; }

    /// <summary>Null in this pure engine (no ruleset versions yet — wired by P2-20.3).</summary>
    public int? VersionNumber { get; init; }

    /// <summary>The matched row's key, or null for <see cref="NoDecision"/>.</summary>
    public string? MatchedRowKey { get; init; }

    /// <summary>Every matched row key in stored row order; empty when no decision was made.</summary>
    public IReadOnlyList<string> MatchedRowKeys { get; init; } = [];

    public required bool IsDecided { get; init; }

    /// <summary>The matched row's outcome (canonical JSON), or null when not decided.</summary>
    public string? OutcomeJson { get; init; }

    /// <summary>Canonical JSON of the fact values actually used to evaluate (CanonicalFacts, step P2-20.2.2).</summary>
    public required string FactsJson { get; init; }

    /// <summary>SHA-256 hex digest of <see cref="FactsJson"/>'s UTF-8 bytes.</summary>
    public required string FactsHash { get; init; }

    /// <summary>Traces of composed child evaluations (D-14); empty when the table has no composed facts.</summary>
    public IReadOnlyList<DecisionTrace> Children { get; init; } = [];
}

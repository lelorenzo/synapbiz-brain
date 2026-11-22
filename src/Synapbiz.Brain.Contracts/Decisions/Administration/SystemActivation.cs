using System.Diagnostics.CodeAnalysis;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Contracts.Validation;

namespace Nuvora.Decisions.Contracts.Administration;

/// <summary>The Active version of a kind.</summary>
/// <param name="VersionId">The version id.</param>
/// <param name="Number">The version number inside the ruleset.</param>
/// <param name="EffectiveFrom">First instant the version applies.</param>
public sealed record ActiveDecisionVersion(Guid VersionId, int Number, DateTimeOffset EffectiveFrom);

/// <summary>Why a system activation happens; it becomes the provenance of the version.</summary>
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "Ordinals are pinned at PlatformDefault=1/Migration=2 and by project convention (same documented CA1008 suppression as HitPolicy and EvaluationMode in this assembly): default(SystemActivationOrigin) == 0 is deliberately not a valid origin.")]
public enum SystemActivationOrigin
{
    /// <summary>The platform default version of a kind.</summary>
    PlatformDefault = 1,

    /// <summary>A version produced by a proved migration of a legacy configuration.</summary>
    Migration = 2,
}

/// <summary>A request to activate a first version of a kind without a human approver.</summary>
/// <param name="Kind">The decision kind.</param>
/// <param name="Origin">Provenance of the version.</param>
/// <param name="Table">The decision table.</param>
/// <param name="Goldens">The golden set the version must pass.</param>
/// <param name="Reason">Why the activation happens (recorded for the audit trail).</param>
public sealed record SystemActivationRequest(
    DecisionKindKey Kind,
    SystemActivationOrigin Origin,
    DecisionTable Table,
    IReadOnlyList<GoldenCase> Goldens,
    string Reason);

/// <summary>Outcome of a system activation.</summary>
[SuppressMessage(
    "Design",
    "CA1034:Nested types should not be visible",
    Justification = "A closed, pattern-matchable result union (Activated | AlreadyActive | Refused) whose cases read as SystemActivationResult.Activated at every call site; the public shape is fixed by the P2-20.5 contract.")]
public abstract record SystemActivationResult
{
    /// <summary>The version was created and activated.</summary>
    /// <param name="VersionId">The new version.</param>
    /// <param name="Number">Its number.</param>
    public sealed record Activated(Guid VersionId, int Number) : SystemActivationResult;

    /// <summary>A version was already Active; nothing was written.</summary>
    /// <param name="VersionId">The Active version.</param>
    /// <param name="Number">Its number.</param>
    public sealed record AlreadyActive(Guid VersionId, int Number) : SystemActivationResult;

    /// <summary>Validation or a golden refused the content; nothing was written.</summary>
    /// <param name="Issues">Every validation issue.</param>
    /// <param name="FailedGoldenNames">The names of the golden cases that failed.</param>
    public sealed record Refused(IReadOnlyList<ValidationIssue> Issues, IReadOnlyList<string> FailedGoldenNames) : SystemActivationResult;
}

/// <summary>One row, with the golden case that proves it, to add to the open Draft of a kind.</summary>
/// <param name="Kind">The decision kind.</param>
/// <param name="Row">The row; an existing row with the same key is replaced.</param>
/// <param name="Golden">The golden case; an existing case with the same name is replaced.</param>
/// <param name="Actor">The proposing user.</param>
public sealed record DraftRowProposal(DecisionKindKey Kind, Row Row, GoldenCase Golden, string Actor);

/// <summary>Where a proposal landed.</summary>
/// <param name="DraftVersionId">The Draft that now holds the row.</param>
/// <param name="VersionNumber">Its number.</param>
/// <param name="CreatedDraft">True when the proposal opened a new Draft.</param>
public sealed record DraftProposalResult(Guid DraftVersionId, int VersionNumber, bool CreatedDraft);

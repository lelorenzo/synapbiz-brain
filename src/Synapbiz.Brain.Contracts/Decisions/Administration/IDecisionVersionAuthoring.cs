using System.Diagnostics.CodeAnalysis;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Contracts.Validation;

namespace Nuvora.Decisions.Contracts.Administration;

/// <summary>Lifecycle status of the open (not yet decided) version of a kind.</summary>
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "Ordinals are pinned at Draft=1/Submitted=2, matching DecisionVersionStatus, and by project convention (same documented CA1008 suppression as SystemActivationOrigin in this folder): default(DecisionAuthoringOpenStatus) == 0 is deliberately not a valid status.")]
public enum DecisionAuthoringOpenStatus
{
    /// <summary>Being authored, or returned to draft, or a submission the validators refused.</summary>
    Draft = 1,

    /// <summary>Validated and frozen, waiting for a distinct approver.</summary>
    Submitted = 2,
}

/// <summary>The Active version of a kind, with the table it applies.</summary>
/// <param name="VersionId">The version id.</param>
/// <param name="Number">The version number inside the ruleset.</param>
/// <param name="Table">The decision table in force.</param>
public sealed record DecisionAuthoringActive(Guid VersionId, int Number, DecisionTable Table);

/// <summary>The newest Draft or Submitted version of a kind.</summary>
/// <param name="VersionId">The version id.</param>
/// <param name="Number">The version number inside the ruleset.</param>
/// <param name="Status">Draft or Submitted.</param>
/// <param name="SubmittedBy">The submitter's id; null while the version is a Draft.</param>
public sealed record DecisionAuthoringOpen(Guid VersionId, int Number, DecisionAuthoringOpenStatus Status, string? SubmittedBy);

/// <summary>What a host module needs to know before it proposes a new table: the table in force and the open version, if any.</summary>
/// <param name="Active">The Active version, or null when the kind has none.</param>
/// <param name="Open">The open Draft or Submitted version, or null when there is none.</param>
public sealed record DecisionAuthoringState(DecisionAuthoringActive? Active, DecisionAuthoringOpen? Open);

/// <summary>A whole table, with the golden cases that prove it, to submit for approval.</summary>
/// <param name="Kind">The decision kind.</param>
/// <param name="Table">The table.</param>
/// <param name="Goldens">The golden set the version must pass.</param>
/// <param name="Actor">The submitting user; recorded as editor and submitter, so the same user cannot approve it.</param>
public sealed record DecisionTableSubmission(
    DecisionKindKey Kind,
    DecisionTable Table,
    IReadOnlyList<GoldenCase> Goldens,
    string Actor);

/// <summary>Outcome of <see cref="IDecisionVersionAuthoring.SubmitTableAsync"/>.</summary>
[SuppressMessage(
    "Design",
    "CA1034:Nested types should not be visible",
    Justification = "A closed, pattern-matchable result union (Submitted | OpenVersionExists | Refused) whose cases read as DecisionTableSubmissionResult.Submitted at every call site, the same shape as SystemActivationResult in this folder.")]
public abstract record DecisionTableSubmissionResult
{
    /// <summary>The table was stored as a new version and submitted; it now waits for a distinct approver.</summary>
    /// <param name="VersionId">The new version.</param>
    /// <param name="Number">Its number.</param>
    public sealed record Submitted(Guid VersionId, int Number) : DecisionTableSubmissionResult;

    /// <summary>A Draft or Submitted version is already open; nothing was written.</summary>
    /// <param name="VersionId">The open version.</param>
    /// <param name="Number">Its number.</param>
    /// <param name="Status">Draft or Submitted.</param>
    public sealed record OpenVersionExists(Guid VersionId, int Number, DecisionAuthoringOpenStatus Status) : DecisionTableSubmissionResult;

    /// <summary>
    /// The kernel refused the content (table validator, kind outcome validator, golden runner or a content cap). The Draft
    /// it was written to is left open, so the caller sees it as the open version.
    /// </summary>
    /// <param name="Issues">Every finding.</param>
    public sealed record Refused(IReadOnlyList<ValidationIssue> Issues) : DecisionTableSubmissionResult;
}

/// <summary>
/// The kernel operations a host module needs to let a tenant author a whole decision table (the approval-rules cards)
/// without referencing the kernel assembly (AT-DEC-03b). It submits through the same lifecycle commands the decisions
/// panel uses and never approves: activation stays four-eyes at the panel. Distinct from
/// <see cref="IDecisionRulesetAdministration"/>, whose row proposal amends one row of an Active version and never submits.
/// </summary>
public interface IDecisionVersionAuthoring
{
    /// <summary>The Active version of <paramref name="kind"/> in the current tenant, and its newest open Draft or Submitted version.</summary>
    Task<DecisionAuthoringState> GetAuthoringStateAsync(DecisionKindKey kind, CancellationToken ct = default);

    /// <summary>
    /// Stores <see cref="DecisionTableSubmission.Table"/> as a new version of the kind (creating the ruleset when
    /// missing; the new version records the Active one as its source) and submits it. Nothing is written when a Draft
    /// or Submitted version is already open. Never approves.
    /// </summary>
    Task<DecisionTableSubmissionResult> SubmitTableAsync(DecisionTableSubmission submission, CancellationToken ct = default);
}

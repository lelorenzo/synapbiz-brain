using Nuvora.Decisions.Contracts.Kinds;

namespace Nuvora.Decisions.Contracts.Administration;

/// <summary>
/// The kernel operations a host module needs to drive its own decision kind without referencing the kernel assembly
/// (AT-DEC-03): a system activation of a first version and a proposal of one row as a Draft. The four-eyes approval
/// stays with the decisions panel; nothing here submits or approves.
/// </summary>
public interface IDecisionRulesetAdministration
{
    /// <summary>The version that is Active for <paramref name="kind"/> in the current tenant, or null when there is none.</summary>
    Task<ActiveDecisionVersion?> GetActiveVersionAsync(DecisionKindKey kind, CancellationToken ct = default);

    /// <summary>
    /// Activates a first version without a human approver, validated exactly as a submission is (table validator, kind
    /// outcome validator, golden runner). Idempotent: refused with nothing written when it cannot pass, a no-op when a
    /// version is already Active.
    /// </summary>
    Task<SystemActivationResult> ActivateSystemVersionAsync(SystemActivationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Adds <paramref name="proposal"/> to the open Draft of the kind (creating one from the Active version when none
    /// is open). Never submits or approves.
    /// </summary>
    Task<DraftProposalResult> ProposeRowAsync(DraftRowProposal proposal, CancellationToken ct = default);
}

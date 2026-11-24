using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>Checks a proposal document against the loader, the tests and the approved base.</summary>
public static class FlowProposalCheck
{
    /// <summary>The proposal says a process needs no review; every process needs one.</summary>
    public const string ErrorNeedsReview = "authoring.proposal.needs_review_false";

    /// <summary>The document does not hash to <c>documentHash</c>.</summary>
    public const string ErrorHash = "authoring.proposal.hash_mismatch";

    /// <summary>The loader refuses the document; the message carries its code and path.</summary>
    public const string ErrorDocument = "authoring.proposal.document_invalid";

    /// <summary>The id or version of the document is not the one the proposal names.</summary>
    public const string ErrorIdentity = "authoring.proposal.identity_mismatch";

    /// <summary>A test of the document does not pass.</summary>
    public const string ErrorTests = "authoring.proposal.tests_failing";

    /// <summary>The approved document is no longer the base the proposal came from.</summary>
    public const string ErrorBaseStale = "authoring.proposal.base_stale";

    /// <summary>The new version is not greater than the base version.</summary>
    public const string ErrorVersion = "authoring.proposal.version_not_newer";

    /// <summary>Every code this check reports.</summary>
    public static IReadOnlyList<string> Codes { get; } = [ErrorNeedsReview, ErrorHash, ErrorDocument, ErrorIdentity, ErrorTests, ErrorBaseStale, ErrorVersion];

    /// <summary>Checks the proposal. An empty list means it is consistent.</summary>
    /// <param name="proposal">The proposal.</param>
    /// <param name="context">What the document is loaded against.</param>
    /// <param name="approvedText">The text of the approved document with the id of the proposal now, or null when there is none or it is not known.</param>
    /// <param name="baseId">The id of the approved document the proposal was made from, or null when it has no base. A proposal keeps the id of its base.</param>
    public static IReadOnlyList<WorkbenchError> Against(FlowProposal proposal, ProcessLoadContext context, string? approvedText = null, string? baseId = null)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);
        var findings = new List<WorkbenchError>();
        if (!proposal.NeedsReview)
        {
            findings.Add(new WorkbenchError(ErrorNeedsReview, "/needsReview", "A process proposal always needs review."));
        }

        if (!string.Equals(FlowProposalJson.HashOf(proposal.DocumentJson), proposal.DocumentHash, StringComparison.Ordinal))
        {
            findings.Add(new WorkbenchError(ErrorHash, "/documentHash", "The document does not have the hash the proposal states."));
        }

        var loaded = BrainProcessLoader.Load(proposal.DocumentJson, context);
        if (loaded.Process is not { } process)
        {
            findings.AddRange(loaded.Errors.Select(error => new WorkbenchError(ErrorDocument, "/document" + error.Path, error.Code + " at " + error.Path)));
        }
        else
        {
            if (!string.Equals(process.Id, proposal.ProcessId, StringComparison.Ordinal) || process.Version != proposal.Version)
            {
                findings.Add(new WorkbenchError(ErrorIdentity, "/process", "The document has another id or version than the proposal states."));
            }

            if (process.RunScenarios().Results.Any(result => !result.Passed))
            {
                findings.Add(new WorkbenchError(ErrorTests, "/tests", "A test of the document does not pass."));
            }
        }

        if (baseId is not null && !string.Equals(baseId, proposal.ProcessId, StringComparison.Ordinal))
        {
            findings.Add(new WorkbenchError(ErrorIdentity, "/process/id", "The proposal is for another process than the approved one it was made from."));
        }

        if (proposal.BaseVersion is { } baseVersion && proposal.Version <= baseVersion)
        {
            findings.Add(new WorkbenchError(ErrorVersion, "/process/version", "The version must be greater than the base version."));
        }

        if (approvedText is not null && proposal.BaseContentHash is { } baseHash
            && !string.Equals(AuthoringJson.Hash(DocumentKind.Flow, approvedText), baseHash, StringComparison.Ordinal))
        {
            findings.Add(new WorkbenchError(ErrorBaseStale, "/process/baseContentHash", "The approved process changed after the proposal was made."));
        }

        return findings;
    }
}

namespace Synapbiz.Brain.Workbench;

/// <summary>
/// A draft revision sent from the person who made it to the rule's maintainers: a message bound to the exact base text by
/// content hash. It changes no rule.
/// </summary>
/// <param name="RuleId">The id of the rule the draft revises.</param>
/// <param name="BaseVersion">The version of the base revision.</param>
/// <param name="BaseContentHash">The <see cref="RuleReviewJson.ContentHash(string)"/> of the exact base text; the caller computes it.</param>
/// <param name="Author">The author's name as they want it written. It must not identify a real person in a sample.</param>
/// <param name="CreatedAt">When the proposal was made; the library has no clock, so the caller supplies it.</param>
/// <param name="Note">The author's explanation.</param>
/// <param name="DraftJson">The text of the draft document, a JSON object.</param>
public sealed record RuleProposal(
    string RuleId,
    long BaseVersion,
    string BaseContentHash,
    string Author,
    DateTimeOffset CreatedAt,
    string Note,
    string DraftJson);

/// <summary>The result of reading a proposal document.</summary>
/// <param name="Proposal">The proposal; null when <paramref name="Errors"/> is not empty.</param>
/// <param name="Errors">Every defect found, ordered by path.</param>
public sealed record RuleProposalParseResult(RuleProposal? Proposal, IReadOnlyList<WorkbenchError> Errors)
{
    /// <summary>True when the document was read without errors.</summary>
    public bool IsValid => Errors.Count == 0;
}

using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Workbench;

/// <summary>The reviewer's verdict on one part of a rule.</summary>
public enum ReviewState
{
    /// <summary>Not looked at yet.</summary>
    Unreviewed = 0,

    /// <summary>The reviewer agrees.</summary>
    Confirmed = 1,

    /// <summary>The reviewer wants a change.</summary>
    NeedsFix = 2,

    /// <summary>The reviewer has a question.</summary>
    Question = 3,
}

/// <summary>A note on the rule, a row or a test case.</summary>
/// <param name="Target">What the note is about. Free text for the library; the convention is <c>rule</c>, <c>row:&lt;row id&gt;</c> or <c>golden:&lt;name&gt;</c>.</param>
/// <param name="State">The reviewer's verdict.</param>
/// <param name="Note">The reviewer's comment. It must not identify a real person.</param>
public sealed record ReviewItem(string Target, ReviewState State, string Note);

/// <summary>A test case the reviewer proposes.</summary>
/// <param name="Name">The case name. It must not identify a real person.</param>
/// <param name="Facts">The fact values of the case.</param>
/// <param name="ExpectNoDecision">True when the case expects no decision.</param>
/// <param name="ExpectedOutcomeJson">The expected outcome as JSON text (an object, or an array of objects for a <c>collect</c> rule); null with <paramref name="ExpectNoDecision"/>.</param>
/// <param name="ExpectedRowId">The row id the decision should come from; null when the case does not pin one.</param>
public sealed record ProposedCase(
    string Name,
    IReadOnlyDictionary<string, DecisionValue> Facts,
    bool ExpectNoDecision,
    string? ExpectedOutcomeJson,
    string? ExpectedRowId);

/// <summary>A review of one exact text of one rule: a message from the reviewer to the rule's maintainers. It changes no rule.</summary>
/// <param name="RuleId">The reviewed rule's id.</param>
/// <param name="RuleVersion">The reviewed rule's version.</param>
/// <param name="RuleContentHash">The <see cref="RuleReviewJson.ContentHash(string)"/> of the exact rule text that was reviewed.</param>
/// <param name="Reviewer">The reviewer's name as they want it written.</param>
/// <param name="CreatedAt">When the review was made; the library has no clock, so the caller supplies it.</param>
/// <param name="Items">The notes.</param>
/// <param name="ProposedCases">The proposed test cases.</param>
public sealed record RuleReview(
    string RuleId,
    long RuleVersion,
    string RuleContentHash,
    string Reviewer,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ReviewItem> Items,
    IReadOnlyList<ProposedCase> ProposedCases);

/// <summary>The result of reading a review document.</summary>
/// <param name="Review">The review; null when <paramref name="Errors"/> is not empty.</param>
/// <param name="Errors">Every defect found, ordered by path.</param>
public sealed record RuleReviewParseResult(RuleReview? Review, IReadOnlyList<WorkbenchError> Errors)
{
    /// <summary>True when the document was read without errors.</summary>
    public bool IsValid => Errors.Count == 0;
}

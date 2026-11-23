using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>Whether the host has approved a rule. The workbench does not know it: the host says.</summary>
public enum RuleApprovalState
{
    /// <summary>Not approved yet.</summary>
    Draft = 0,

    /// <summary>Approved by the host's own process.</summary>
    Approved = 1,
}

/// <summary>The four tabs of the rule panel.</summary>
public enum WorkbenchTab
{
    /// <summary>The rows as sentences.</summary>
    Read = 0,

    /// <summary>Example data in, explained result out.</summary>
    Simulate = 1,

    /// <summary>The rule's test cases and the ones the reviewer proposes.</summary>
    Tests = 2,

    /// <summary>The reviewer's notes and the export.</summary>
    Review = 3,

    /// <summary>What a draft of the rule changes, and the export of the proposal. Shown only while a draft exists.</summary>
    Changes = 4,
}

/// <summary>One rule of the list, as the host supplies it.</summary>
public sealed record WorkbenchRule
{
    private static readonly IReadOnlyDictionary<string, string> NoTexts = new Dictionary<string, string>(0, StringComparer.Ordinal);

    private IReadOnlyDictionary<string, string> rowTexts = NoTexts;

    /// <summary>A stable key, unique in the list and chosen by the host. It is never shown.</summary>
    public required string Key { get; init; }

    /// <summary>The heading the rule is listed under. Rules keep the order given; a heading is drawn each time the group changes.</summary>
    public string? Group { get; init; }

    /// <summary>The loaded rule; null when the rule could not be read.</summary>
    public BrainRule? Rule { get; init; }

    /// <summary>The exact text of the rule document, for the content hash of an exported review. A rule with no text counts as unreadable.</summary>
    public string? RuleText { get; init; }

    /// <summary>The title to show when <see cref="Rule"/> is null.</summary>
    public string? FallbackTitle { get; init; }

    /// <summary>The display names of the rule for the current locale.</summary>
    public RuleLabels Labels { get; init; } = RuleLabels.Empty;

    /// <summary>
    /// The names of the outcome fields the result heading shows, in order. Null or empty shows the first field of the kind's
    /// outcome fields that the decided outcome sets. The definition list under the heading always lists every field.
    /// </summary>
    public IReadOnlyList<string>? HeadlineFields { get; init; }

    /// <summary>The approval state the host reports.</summary>
    public RuleApprovalState State { get; init; }

    /// <summary>A sentence the host writes under the title; null shows the default line of the state.</summary>
    public string? StateLine { get; init; }

    /// <summary>A text the host writes after the state line, for example where the rule comes from; null draws nothing.</summary>
    public string? SourceLine { get; init; }

    /// <summary>
    /// Loads a draft text with the host's contracts, exactly as the host loads its rules. Null turns guided editing off for this
    /// rule: guided editing is offered only when <see cref="Rule"/>, <see cref="RuleText"/> and this member are all present.
    /// </summary>
    public Func<string, BrainRuleLoadResult>? LoadDraft { get; init; }

    /// <summary>
    /// For outcome fields whose values are texts the host knows: the field name to the allowed values and the sentence shown for
    /// each, in order. A field listed here is edited with a select of the sentences.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<WorkbenchOutcomeText>>? OutcomeTexts { get; init; }

    /// <summary>True when guided editing is offered for this rule.</summary>
    internal bool CanDraft => Rule is not null && RuleText is not null && LoadDraft is not null;

    /// <summary>Row id to the secondary line of the row, for example the text a person sees when the row decides. Never null.</summary>
    public IReadOnlyDictionary<string, string> RowTexts
    {
        get => rowTexts;
        init => rowTexts = value ?? NoTexts;
    }
}

/// <summary>One allowed value of an outcome field the host knows in words, and the sentence shown for it.</summary>
/// <param name="Value">The typed value an edit carries.</param>
/// <param name="Text">The sentence shown for it.</param>
public sealed record WorkbenchOutcomeText(DecisionValue Value, string Text);

/// <summary>One file of an export: the review of one rule, or the proposal of one draft. Exactly one of the two is set.</summary>
/// <param name="SuggestedName">A file name the host may use, <c>review-&lt;rule id&gt;-v&lt;version&gt;.json</c> or <c>proposal-&lt;rule id&gt;-v&lt;version&gt;.json</c>.</param>
/// <param name="Json">The <c>brain.rule-review@1</c> or <c>brain.rule-proposal@1</c> document.</param>
/// <param name="Review">The review the document was written from; null for a proposal.</param>
/// <param name="Proposal">The proposal the document was written from; null for a review.</param>
public sealed record WorkbenchExportFile(string SuggestedName, string Json, RuleReview? Review, RuleProposal? Proposal = null);

/// <summary>What the reviewer exports: one file for each rule that has a note, a state or a proposed case, or the one proposal of a draft.</summary>
/// <param name="Files">The files, in the order of the rule list.</param>
public sealed record WorkbenchExport(IReadOnlyList<WorkbenchExportFile> Files);

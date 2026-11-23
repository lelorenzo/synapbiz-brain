using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Validation;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>What a step of a draft is, so that an undo knows which steps belong to a row or to a case.</summary>
internal enum StepKind
{
    /// <summary>The form of an existing row: conditions, outcome or name.</summary>
    Row = 0,

    /// <summary>A new row.</summary>
    Add = 1,

    /// <summary>A row removed.</summary>
    Remove = 2,

    /// <summary>A row moved up or down.</summary>
    Move = 3,

    /// <summary>The reviewer accepted the new result of a test case that changed.</summary>
    CaseAccept = 4,

    /// <summary>The reviewer changed what a test case expects.</summary>
    CaseEdit = 5,

    /// <summary>A test case added to the draft.</summary>
    CaseAdd = 6,

    /// <summary>A test case removed from the draft.</summary>
    CaseRemove = 7,
}

/// <summary>A list of edits applied as one unit, with what the unit is and what it concerns.</summary>
/// <param name="Kind">What the step is.</param>
/// <param name="Key">The row key or the case name the step concerns.</param>
/// <param name="Edits">The edits, in order.</param>
internal sealed record DraftStep(StepKind Kind, string Key, IReadOnlyList<RuleEdit> Edits)
{
    /// <summary>True for the steps that decide on a test case.</summary>
    internal bool IsCaseDecision => Kind is StepKind.CaseAccept or StepKind.CaseEdit;

    /// <summary>True for the steps that concern a test case.</summary>
    internal bool IsCaseStep => Kind is >= StepKind.CaseAccept;
}

/// <summary>What the engine's table validator found in a rule: the rows that never decide, the pairs that overlap and the gap in coverage.</summary>
/// <param name="Unreachable">The keys of the rows that never decide.</param>
/// <param name="Incomplete">True when some data is decided by no row.</param>
/// <param name="Overlaps">The pairs of row keys that can match at once, each as the two keys in ordinal order joined by a bar.</param>
internal sealed record DraftFindings(HashSet<string> Unreachable, bool Incomplete, HashSet<string> Overlaps)
{
    /// <summary>Reads the engine's report of a rule.</summary>
    internal static DraftFindings Of(BrainRule rule)
    {
        var report = rule.ValidateTable();
        var all = report.Errors.Concat(report.Warnings).ToList();
        return new DraftFindings(
            [.. all.Where(item => item.Code == TableValidator.CodeUnreachable).SelectMany(item => item.RowKeys)],
            all.Any(item => item.Code == TableValidator.CodeIncomplete),
            [.. all.Where(item => item.Code == TableValidator.CodeOverlap).Select(item => PairOf(item.RowKeys))]);
    }

    /// <summary>The two keys of a pair as one text, in ordinal order.</summary>
    internal static string PairOf(IReadOnlyList<string> keys) => string.Join('|', keys.Order(StringComparer.Ordinal));
}

/// <summary>The draft of one rule that one reviewer is working on. It lives in the session, in memory, and nowhere else.</summary>
internal sealed class DraftState
{
    private RuleView? view;
    private BrainRule? viewRule;
    private RuleLabels? viewLabels;
    private WorkbenchLocale viewLocale;

    private RuleLabels? labels;
    private RuleLabels? labelsHost;
    private WorkbenchLocale labelsLocale;

    private RuleChangeView? change;
    private RuleLabels? changeLabels;
    private RuleLabels? changeBaseLabels;
    private WorkbenchLocale changeLocale;

    internal DraftState(string baseText, BrainRule baseRule)
    {
        BaseText = baseText;
        BaseRule = baseRule;
        BaseHash = RuleReviewJson.ContentHash(baseText);
        BaseFindings = DraftFindings.Of(baseRule);
    }

    /// <summary>The exact text of the rule the draft was started from.</summary>
    internal string BaseText { get; }

    /// <summary>The rule the draft was started from.</summary>
    internal BrainRule BaseRule { get; }

    /// <summary>The content hash of <see cref="BaseText"/>; a proposal is bound to it.</summary>
    internal string BaseHash { get; }

    /// <summary>What the engine's validator found in the base, so that only findings the draft adds are shown as new.</summary>
    internal DraftFindings BaseFindings { get; }

    /// <summary>The steps, oldest first. The draft is always the base with the edits of every step applied in order.</summary>
    internal List<DraftStep> Steps { get; private set; } = [];

    /// <summary>The text of the draft; null when the draft could not be read.</summary>
    internal string? Text { get; private set; }

    /// <summary>The draft as loaded; null when the draft could not be read.</summary>
    internal BrainRule? Rule { get; private set; }

    /// <summary>The engine's findings in the draft; null when the draft could not be read.</summary>
    internal DraftFindings? Findings { get; private set; }

    /// <summary>The test cases of the draft run on the engine; null when the draft could not be read.</summary>
    internal TestRunView? Run { get; private set; }

    /// <summary>Why the draft could not be read, as the loader said it; empty when it can.</summary>
    internal IReadOnlyList<DraftProblem> Problems { get; private set; } = [];

    /// <summary>True while the panels show the draft; false while they show the rule as it is.</summary>
    internal bool ViewDraft { get; set; } = true;

    /// <summary>Counts every change of the draft, so that a later change can be told from none.</summary>
    internal int Revision { get; private set; }

    /// <summary>The revision that was exported; null when the draft was never exported.</summary>
    internal int? ExportedRevision { get; set; }

    private int keyNumber;

    /// <summary>A number never given before in this draft, from which the key of a new row is made.</summary>
    internal int NextKeyNumber() => ++keyNumber;

    /// <summary>The reason for the change, as the reviewer is writing it for the proposal.</summary>
    internal string Reason { get; set; } = string.Empty;

    /// <summary>True when an export was tried with no reason.</summary>
    internal bool ReasonError { get; set; }

    /// <summary>True when an export was tried with no name.</summary>
    internal bool NameError { get; set; }

    /// <summary>Where the host said the last proposal was saved; null when it said nothing.</summary>
    internal string? ExportPlace { get; set; }

    /// <summary>The row form that is open; null when none is.</summary>
    internal RowForm? Form { get; set; }

    /// <summary>The refusal of the last undo or menu action that is shown under its row or at the top of the draft.</summary>
    internal DraftRefusal? Refusal { get; set; }

    /// <summary>True when the draft has at least one step.</summary>
    internal bool HasChanges => Steps.Count > 0;

    /// <summary>True when the draft was exported and nothing changed since.</summary>
    internal bool IsExported => ExportedRevision == Revision;

    /// <summary>True when the draft was exported and then changed.</summary>
    internal bool ChangedAfterExport => ExportedRevision is not null && ExportedRevision != Revision;

    /// <summary>True when the draft was never exported or changed after it was, and has something to lose.</summary>
    internal bool IsUnexported => HasChanges && !IsExported;

    /// <summary>The number of steps that concern test cases.</summary>
    internal int CaseStepCount => Steps.Count(step => step.IsCaseStep);

    /// <summary>The names of the test cases the reviewer decided on: accepted or changed what they expect.</summary>
    internal HashSet<string> DecidedCases => [.. Steps.Where(step => step.IsCaseDecision).Select(step => step.Key)];

    /// <summary>Keeps the result of a rebuild that the loader read as the draft.</summary>
    internal void Commit(IReadOnlyList<DraftStep> steps, RebuildResult result)
    {
        Steps = [.. steps];
        Text = result.Text;
        Rule = result.Rule;
        Findings = result.Findings;
        Run = RuleTester.Run(result.Rule!);
        Problems = [];
        Revision++;
        view = null;
        change = null;
        labels = null;
    }

    /// <summary>Runs the test cases of the draft again.</summary>
    internal void RunAgain() => Run = RuleTester.Run(Rule!);

    /// <summary>Keeps the reasons the first draft could not be read; the draft then has no rule.</summary>
    internal void Unreadable(RebuildResult result)
    {
        Problems = result.Problems;
        Text = null;
        Rule = null;
        Findings = null;
        Run = null;
        Revision++;
        view = null;
        change = null;
    }

    /// <summary>
    /// The labels of the draft: the host's labels, with the row titles of the draft's own <c>meta</c> (a row the draft named, or
    /// whose name it cleared). The same instance is returned until the draft, the host's labels or the locale change.
    /// </summary>
    internal RuleLabels Labels(WorkbenchLocale locale, RuleLabels host)
    {
        if (labels is null || !ReferenceEquals(labelsHost, host) || labelsLocale != locale)
        {
            var rows = MergedRows(BaseRule, Rule!, host, locale);
            labels = host with { Rows = rows };
            labelsHost = host;
            labelsLocale = locale;
        }

        return labels;
    }

    /// <summary>The row titles of a candidate: the host's, with those the candidate's own <c>meta</c> names or clears.</summary>
    internal static RuleLabels LabelsOf(BrainRule baseRule, BrainRule candidate, WorkbenchLocale locale, RuleLabels host) =>
        host with { Rows = MergedRows(baseRule, candidate, host, locale) };

    private static Dictionary<string, string> MergedRows(BrainRule baseRule, BrainRule candidate, RuleLabels host, WorkbenchLocale locale)
    {
        var before = RuleLabels.FromMeta(baseRule.Meta, locale).Rows;
        var after = RuleLabels.FromMeta(candidate.Meta, locale).Rows;
        var rows = host.Rows.Where(pair => !before.ContainsKey(pair.Key) || after.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var (id, title) in after)
        {
            rows[id] = title;
        }

        return rows;
    }

    /// <summary>The draft read as sentences; computed again when the rule, the labels or the locale changed.</summary>
    internal RuleView View(WorkbenchLocale locale, RuleLabels labels)
    {
        if (view is null || !ReferenceEquals(viewRule, Rule) || !ReferenceEquals(viewLabels, labels) || viewLocale != locale)
        {
            view = RuleDescriber.Describe(Rule!, locale, labels);
            viewRule = Rule;
            viewLabels = labels;
            viewLocale = locale;
        }

        return view;
    }

    /// <summary>What the draft changes in the base, from the headless library; computed again when the draft, the labels or the locale changed.</summary>
    internal RuleChangeView Change(WorkbenchLocale locale, RuleLabels baseLabels, RuleLabels draftLabels)
    {
        if (change is null || !ReferenceEquals(changeLabels, draftLabels) || !ReferenceEquals(changeBaseLabels, baseLabels) || changeLocale != locale)
        {
            change = RuleChangeDescriber.Describe(BaseRule, Rule!, baseLabels, draftLabels, locale);
            changeLabels = draftLabels;
            changeBaseLabels = baseLabels;
            changeLocale = locale;
        }

        return change;
    }
}

/// <summary>A refusal that is shown outside a form: the lines to show and the row they belong to.</summary>
/// <param name="RowKey">The key of the row to show it under; null shows it at the top of the draft.</param>
/// <param name="Lines">The reasons, as sentences.</param>
internal sealed record DraftRefusal(string? RowKey, IReadOnlyList<string> Lines);

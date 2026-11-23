using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Workbench;

/// <summary>
/// One change to a rule, in the terms of the person who owns it. The set of edits is closed: <see cref="RuleDrafter"/> applies
/// them, in order, to the text of a <c>brain.rule@1</c> document. A row is always named by its <c>legacyKey</c>, the key
/// <c>BrainRule.RowIdsByKey</c> and <see cref="RowView"/> use. Conditions are the engine's own <see cref="Condition"/>
/// (a fact name, an operator and its operands), so no parallel condition type exists.
/// </summary>
public abstract record RuleEdit
{
    private protected RuleEdit()
    {
    }
}

/// <summary>
/// A condition on one fact of one row. It replaces the row's condition on that fact, or is added after the row's other
/// conditions when the row has none. A row never ends up with two conditions on one fact.
/// </summary>
/// <param name="RowKey">The row's key.</param>
/// <param name="Condition">The condition; its <see cref="Condition.FactName"/> names the fact.</param>
public sealed record SetCondition(string RowKey, Condition Condition) : RuleEdit;

/// <summary>Removes the row's condition on a fact, so the fact accepts any value. It is an error when the row has none.</summary>
/// <param name="RowKey">The row's key.</param>
/// <param name="Fact">The fact name.</param>
public sealed record ClearCondition(string RowKey, string Fact) : RuleEdit;

/// <summary>Sets one outcome field of a row. A null value removes the field, which only an optional field allows (the loader decides).</summary>
/// <param name="RowKey">The row's key.</param>
/// <param name="Field">The outcome field name.</param>
/// <param name="Value">The new value; null removes the field.</param>
public sealed record SetOutcome(string RowKey, string Field, DecisionValue? Value) : RuleEdit;

/// <summary>Adds a row, with version 1. Its id and key are both <paramref name="RowKey"/>.</summary>
/// <param name="RowKey">The new row's key; it must be a valid section id and not used by any row.</param>
/// <param name="AfterRowKey">The row to put it after; null puts it first.</param>
/// <param name="Conditions">The conditions of the row; may be empty.</param>
/// <param name="Outcome">The outcome fields of the row.</param>
public sealed record AddRow(
    string RowKey,
    string? AfterRowKey,
    IReadOnlyList<Condition> Conditions,
    IReadOnlyDictionary<string, DecisionValue> Outcome) : RuleEdit;

/// <summary>Removes a row and its labels. Test cases that expect the row are left as they are, so they fail when run.</summary>
/// <param name="RowKey">The row's key.</param>
public sealed record RemoveRow(string RowKey) : RuleEdit;

/// <summary>Moves a row within the table. The row keeps its version.</summary>
/// <param name="RowKey">The row's key.</param>
/// <param name="AfterRowKey">The row to put it after; null puts it first.</param>
public sealed record MoveRow(string RowKey, string? AfterRowKey) : RuleEdit;

/// <summary>Sets one outcome field of the default row. It is an error when the rule has no default row.</summary>
/// <param name="Field">The outcome field name.</param>
/// <param name="Value">The new value; null removes the field.</param>
public sealed record SetDefaultOutcome(string Field, DecisionValue? Value) : RuleEdit;

/// <summary>
/// Changes what a test case expects, in place: the case keeps its position and every member but <c>expect</c>. The edit can say
/// every expectation a case can hold:
/// <list type="bullet">
/// <item>a decision with the given <see cref="Outcome"/>, from a row or the default row (<see cref="RowKey"/>), or from no
/// particular row when it is null; under <c>collect</c> the outcome is the only result;</item>
/// <item>no decision: <see cref="NoDecision"/>;</item>
/// <item>under <c>collect</c>, any number of results: <see cref="Results"/> (zero, one or several).</item>
/// </list>
/// </summary>
/// <param name="CaseName">The case name.</param>
/// <param name="RowKey">The key of the row the decision should come from (a row or the default row); null leaves the row unpinned.</param>
/// <param name="Outcome">The expected outcome fields. Under <c>collect</c> they are the only result, unless <see cref="Results"/> is set.</param>
public sealed record SetCaseExpectation(
    string CaseName,
    string? RowKey,
    IReadOnlyDictionary<string, DecisionValue> Outcome) : RuleEdit
{
    /// <summary>
    /// True when the case expects that no row decides. <see cref="RowKey"/>, <see cref="Outcome"/> and <see cref="Results"/> are
    /// then ignored. Use <see cref="NoDecision"/> to build it.
    /// </summary>
    public bool ExpectNoDecision { get; init; }

    /// <summary>
    /// The expected results of a <c>collect</c> rule, in order: zero, one or several outcomes, which replace
    /// <see cref="Outcome"/>. Null (the default) keeps the one-result form. The edit is refused with
    /// <c>workbench.draft.caseNotValid</c> when it is set and the rule's hit policy is not <c>collect</c>.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, DecisionValue>>? Results { get; init; }

    /// <summary>An edit that makes the case expect that no row decides.</summary>
    /// <param name="caseName">The case name.</param>
    public static SetCaseExpectation NoDecision(string caseName) =>
        new(caseName, null, new Dictionary<string, DecisionValue>(0, StringComparer.Ordinal)) { ExpectNoDecision = true };
}

/// <summary>Adds a test case after the existing ones. The case name must not be in use.</summary>
/// <param name="Case">The case; <see cref="ProposedCase.ExpectedRowId"/> is a row id, as in a review.</param>
public sealed record AddCase(ProposedCase Case) : RuleEdit;

/// <summary>Removes a test case by name.</summary>
/// <param name="CaseName">The case name.</param>
public sealed record RemoveCase(string CaseName) : RuleEdit;

/// <summary>Sets or clears the display name of a row for one locale, under <c>meta.display.&lt;locale&gt;.rows</c>.</summary>
/// <param name="RowKey">The row's key; the label is stored under the row's id.</param>
/// <param name="Locale">The locale of the label.</param>
/// <param name="Text">The label; null clears it. Empty or white-space text is refused.</param>
public sealed record SetRowLabel(string RowKey, WorkbenchLocale Locale, string? Text) : RuleEdit;

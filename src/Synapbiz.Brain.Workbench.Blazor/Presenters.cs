using System.Globalization;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>How one row of a simulation reads: the word at its right and the tone of that word.</summary>
internal sealed record RowWord(string Key, string Tone);

/// <summary>Words for a simulation result. It only arranges what the headless library reports; it decides nothing.</summary>
internal static class SimulationPresenter
{
    /// <summary>
    /// A rule with more rows than this (the default row counts) folds the rows that do not apply away until the reviewer asks for
    /// them. The deciding rows and every row that matched are always shown.
    /// </summary>
    internal const int FoldAfter = 6;

    /// <summary>The number of rows of a rule, the default row included.</summary>
    internal static int RowCount(RuleView view) => view.Rows.Count + (view.DefaultRow is null ? 0 : 1);

    /// <summary>
    /// True when a condition accepts any value and so is neither listed nor named: an <c>Any</c> condition, or a range with no
    /// bound. It reads the rule's own condition and compares nothing.
    /// </summary>
    internal static bool AcceptsAny(BrainRule rule, RowView row, ConditionView condition)
    {
        if (condition.Operator == Operator.Any)
        {
            return true;
        }

        if (condition.Operator != Operator.Range)
        {
            return false;
        }

        var source = rule.Table.Rows.Concat(rule.Table.DefaultRow is null ? [] : [rule.Table.DefaultRow])
            .FirstOrDefault(item => string.Equals(item.Key, row.Key, StringComparison.Ordinal));
        return source?.Conditions.ElementAtOrDefault(condition.Index) is { Value: null, UpperValue: null };
    }

    /// <summary>
    /// True when the value the reviewer typed is added to the text of a condition: only for the operators whose text does not
    /// already say it. An equals, one-of or empty condition reads the same without the value.
    /// </summary>
    internal static bool AddsTypedValue(Operator op) =>
        op is Operator.NotEquals or Operator.NotIn or Operator.Range or Operator.StartsWith or Operator.IsNotNull;

    internal static RowView? RowByKey(RuleView view, string key) =>
        view.Rows.Concat(view.DefaultRow is null ? [] : [view.DefaultRow]).FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.Ordinal));

    /// <summary>The number a row shows in its disc: its place in decision order, from 1.</summary>
    internal static string Number(RowView row) => row.IsDefault ? "–" : (row.Index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>The numbers of rows as a list in words: <c>2</c>, <c>2 y 4</c>, <c>2, 4 y 6</c>.</summary>
    internal static string RowNumbers(UiText text, RuleView view, IEnumerable<string> keys)
    {
        var numbers = keys.Select(key => RowByKey(view, key)).OfType<RowView>().Select(Number).ToList();
        return JoinWords(text, numbers);
    }

    internal static string JoinWords(UiText text, IReadOnlyList<string> words) => words.Count switch
    {
        0 => string.Empty,
        1 => words[0],
        _ => string.Join(", ", words.Take(words.Count - 1)) + text.Plain("why.and") + words[^1],
    };

    /// <summary>The row keys a decided result reports, in table order.</summary>
    internal static IReadOnlyList<string> DecidingKeys(SimulationResult result) =>
        [.. result.Rows.Where(row => row.Selected).Select(row => row.Key)];

    /// <summary>The sentence that says which row decided and why; null when the rule did not decide.</summary>
    internal static string? Why(UiText text, BrainRule rule, RuleView view, RuleLabels labels, SimulationResult result)
    {
        if (result.Outcome is not Decided)
        {
            return null;
        }

        var selected = result.Rows.Where(row => row.Selected).ToList();
        if (selected.Count == 0)
        {
            return null;
        }

        if (selected.All(row => row.IsDefault))
        {
            return text.Plain("why.default");
        }

        if (view.HitPolicy == HitPolicy.Collect && selected.Count > 1)
        {
            return text.Plain("why.rows", ("rows", RowNumbers(text, view, selected.Select(row => row.Key))));
        }

        var winner = selected[0];
        var row = RowByKey(view, winner.Key);
        if (row is null)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var condition in row.Conditions.Where(condition => !AcceptsAny(rule, row, condition)))
        {
            var check = winner.Conditions.FirstOrDefault(item => item.Index == condition.Index);
            if (check is { Passed: true })
            {
                parts.Add(Condition(condition, check, labels, text.Locale));
            }
        }

        var because = parts.Count == 0 ? text.Plain("why.always") : string.Join(text.Plain("why.and"), parts);
        return text.Plain("why.row", ("n", Number(row)), ("conditions", because));
    }

    private static string Condition(ConditionView condition, ConditionCheck check, RuleLabels labels, WorkbenchLocale locale)
    {
        var typed = AddsTypedValue(condition.Operator) ? WorkbenchText.FormatValue(check.Actual, condition.FactName, locale, labels) : string.Empty;
        if (typed.Length == 0)
        {
            return condition.Text;
        }

        return condition.Text.StartsWith(condition.FactLabel, StringComparison.Ordinal)
            ? condition.FactLabel + " (" + typed + ")" + condition.Text[condition.FactLabel.Length..]
            : condition.Text + " (" + typed + ")";
    }

    /// <summary>The word at the right of a row and its tone.</summary>
    internal static RowWord Word(RowExplanation row)
    {
        if (row.Selected)
        {
            return new RowWord("row.decides", "ok");
        }

        return row.Matched && !row.IsDefault ? new RowWord("row.matchesNoDecide", "neutral") : new RowWord("row.notApply", "neutral");
    }

    /// <summary>The first letter in upper case.</summary>
    internal static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    /// <summary>The row as the condition half of its sentence, <c>If ...</c> with the three dots; the whole sentence when there is no condition to show.</summary>
    internal static string ShortSentence(UiText text, BrainRule rule, RowView row)
    {
        var parts = row.Conditions.Where(condition => !AcceptsAny(rule, row, condition)).Select(condition => condition.Text).ToList();
        if (row.IsDefault || parts.Count == 0)
        {
            return row.Sentence;
        }

        var joined = string.Join(text.Plain("why.and"), parts);
        return text.Plain("sim.rowIf", ("conditions", joined));
    }
}

using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>One result a proposed case may expect: the outcome of a row of the rule, in words.</summary>
/// <param name="Index">Position in the list of options.</param>
/// <param name="Text">The outcome in words.</param>
/// <param name="OutcomeJson">The outcome as the row writes it.</param>
internal sealed record ExpectedOption(int Index, string Text, string OutcomeJson);

/// <summary>The add-case form that is open on the Tests tab.</summary>
internal sealed class CaseForm
{
    internal CaseForm(string ruleKey) => RuleKey = ruleKey;

    internal string RuleKey { get; }

    internal string Name { get; set; } = string.Empty;

    internal FactForm Facts { get; } = new();

    internal bool ExpectNone { get; set; }

    /// <summary>The indexes of the chosen <see cref="ExpectedOption"/>s: at most one unless the rule collects.</summary>
    internal SortedSet<int> Chosen { get; } = [];

    /// <summary>The engine key of the row the case pins, when it comes from the simulator or from an uncovered row.</summary>
    internal string? PinnedRowKey { get; set; }

    internal string? NameError { get; set; }

    internal string? ExpectedError { get; set; }

    /// <summary>The distinct outcomes of the rule's rows, the default row last, each once, in the order of the rows.</summary>
    internal static List<ExpectedOption> Options(BrainRule rule, RuleView view, RuleLabels labels, WorkbenchLocale locale)
    {
        var options = new List<ExpectedOption>();
        var rows = rule.Table.DefaultRow is null ? rule.Table.Rows : [.. rule.Table.Rows, rule.Table.DefaultRow];
        foreach (var outcome in rows.Select(row => row.Outcome).Where(json => options.All(option => !string.Equals(option.OutcomeJson, json, StringComparison.Ordinal))))
        {
            var groups = OutcomeFormat.Groups(view, labels, locale, outcome);
            options.Add(new ExpectedOption(options.Count, OutcomeFormat.Summary(groups), outcome));
        }

        return options;
    }

    /// <summary>The index of the option that holds the outcome of the row with this engine key; null when there is none.</summary>
    internal static int? OptionOf(BrainRule rule, IReadOnlyList<ExpectedOption> options, string rowKey)
    {
        var row = rule.Table.Rows.Concat(rule.Table.DefaultRow is null ? [] : [rule.Table.DefaultRow])
            .FirstOrDefault(item => string.Equals(item.Key, rowKey, StringComparison.Ordinal));
        var found = row is null ? null : options.FirstOrDefault(option => string.Equals(option.OutcomeJson, row.Outcome, StringComparison.Ordinal));
        return found?.Index;
    }

    /// <summary>The expectation the form describes; null when nothing is chosen.</summary>
    internal GoldenExpectation? Expectation(IReadOnlyList<ExpectedOption> options, HitPolicy policy)
    {
        if (ExpectNone)
        {
            return new ExpectedNoDecision();
        }

        var outcomes = Chosen.Where(index => index < options.Count).Select(index => options[index].OutcomeJson).ToList();
        if (outcomes.Count == 0)
        {
            return null;
        }

        return policy == HitPolicy.Collect
            ? new ExpectedDecided("[" + string.Join(",", outcomes) + "]")
            : new ExpectedDecided(outcomes[0], PinnedRowKey);
    }
}

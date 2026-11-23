using System.Text;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>The reviewer's verdict and note on one target.</summary>
internal sealed class ReviewEntry
{
    internal ReviewState State { get; set; }

    internal string Note { get; set; } = string.Empty;

    /// <summary>True when the entry says something and goes into an export.</summary>
    internal bool IsExported => State != ReviewState.Unreviewed || Note.Length > 0;
}

/// <summary>A test case the reviewer proposed: its facts, what it expects and, when it pins one, the engine key of the row.</summary>
internal sealed record ProposedCaseEntry(string Name, IReadOnlyDictionary<string, DecisionValue> Facts, GoldenExpectation Expected, string? ExpectedRowKey);

/// <summary>Everything one reviewer's session holds for one rule. It lives in memory and nowhere else.</summary>
internal sealed class RuleWork
{
    private RuleView? view;
    private BrainRule? viewRule;
    private RuleLabels? viewLabels;
    private WorkbenchLocale viewLocale;

    private TestRunView? tests;
    private BrainRule? testsRule;
    private int testsVersion = -1;

    internal Dictionary<string, ReviewEntry> Reviews { get; } = new(StringComparer.Ordinal);

    internal List<ProposedCaseEntry> Cases { get; } = [];

    /// <summary>Targets whose note field the reviewer opened by hand.</summary>
    internal HashSet<string> OpenNotes { get; } = new(StringComparer.Ordinal);

    internal FactForm Simulator { get; } = new();

    internal SimulationResult? Result { get; set; }

    internal IReadOnlyDictionary<string, DecisionValue>? ResultFacts { get; set; }

    internal bool Stale { get; set; }

    /// <summary>The position of the case the simulator fields were loaded from, as the select shows it; empty when none.</summary>
    internal string LoadedCase { get; set; } = string.Empty;

    internal bool ShowAllRows { get; set; }

    /// <summary>The engine code of a refusal the reviewer added to the review; it goes into the exported note, never on screen.</summary>
    internal string? RefusalCode { get; set; }

    /// <summary>What the last export contained, so a later change can be told from none.</summary>
    internal string ExportedSignature { get; set; } = string.Empty;

    /// <summary>Counts the cases that changed, so the cached run knows when to run again.</summary>
    internal int CasesVersion { get; private set; }

    internal ReviewEntry Review(string target) => Reviews.TryGetValue(target, out var entry) ? entry : new ReviewEntry();

    internal ReviewEntry ReviewForEdit(string target)
    {
        if (!Reviews.TryGetValue(target, out var entry))
        {
            entry = new ReviewEntry();
            Reviews[target] = entry;
        }

        return entry;
    }

    internal void AddCase(ProposedCaseEntry entry)
    {
        Cases.Add(entry);
        CasesVersion++;
    }

    internal RuleView View(WorkbenchRule rule, WorkbenchLocale locale)
    {
        if (view is null || !ReferenceEquals(viewRule, rule.Rule) || !ReferenceEquals(viewLabels, rule.Labels) || viewLocale != locale)
        {
            view = RuleDescriber.Describe(rule.Rule!, locale, rule.Labels);
            viewRule = rule.Rule;
            viewLabels = rule.Labels;
            viewLocale = locale;
        }

        return view;
    }

    internal TestRunView Tests(BrainRule rule, bool force = false)
    {
        if (force || tests is null || !ReferenceEquals(testsRule, rule) || testsVersion != CasesVersion)
        {
            tests = RuleTester.Run(rule, [.. Cases.Select(entry => new GoldenCase(entry.Name, entry.Facts, entry.Expected))]);
            testsRule = rule;
            testsVersion = CasesVersion;
        }

        return tests;
    }

    /// <summary>The exported part of the work, as one text; two equal texts mean the same export.</summary>
    internal string Signature()
    {
        var text = new StringBuilder();
        foreach (var (target, entry) in Reviews.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (entry.IsExported)
            {
                text.Append(target).Append('|').Append((int)entry.State).Append('|').Append(entry.Note).Append('\n');
            }
        }

        foreach (var entry in Cases)
        {
            text.Append("case|").Append(entry.Name).Append('|').Append(entry.ExpectedRowKey).Append('|').Append(entry.Expected);
            foreach (var (name, value) in entry.Facts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                text.Append('|').Append(name).Append('=').Append(value);
            }

            text.Append('\n');
        }

        if (RefusalCode is not null)
        {
            text.Append("refusal|").Append(RefusalCode).Append('\n');
        }

        return text.ToString();
    }

    internal bool HasExportable => Reviews.Values.Any(entry => entry.IsExported) || Cases.Count > 0;

    internal bool HasUnexportedChanges => !string.Equals(Signature(), ExportedSignature, StringComparison.Ordinal);
}

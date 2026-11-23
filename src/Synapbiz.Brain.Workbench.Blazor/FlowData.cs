using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>What a test of the document does: its name, whether it passes and how it fails, and the steps it goes through.</summary>
internal sealed record ScenarioLine(string Name, bool Passed, string? Failure, IReadOnlyList<string> Trace);

/// <summary>Everything the headless library says about one readable flow in one language, computed once and kept while the list and the language stay as they are.</summary>
internal sealed class FlowData
{
    private FlowData()
    {
    }

    internal required BrainProcess Process { get; init; }

    internal required ProcessOutline Outline { get; init; }

    internal required FlowEntry Entry { get; init; }

    internal required FlowView View { get; init; }

    internal required FlowGraph Graph { get; init; }

    internal required FlowCoverageResult Coverage { get; init; }

    internal required FlowWords Words { get; init; }

    internal required IReadOnlyList<ScenarioLine> Scenarios { get; init; }

    /// <summary>The routes no scenario takes, as <c>step/label</c>.</summary>
    internal required HashSet<string> Untried { get; init; }

    /// <summary>Every target of a review of this flow, in the order of the export: the flow, its triggers, stages, steps, routes and scenarios.</summary>
    internal required IReadOnlyList<string> Targets { get; init; }

    /// <summary>The ids of the triggers, as review targets are named, in document order.</summary>
    internal required IReadOnlyList<string> TriggerIds { get; init; }

    private DiagramLayout? layout;

    /// <summary>The diagram of the flow; the label of the band of the steps with no stage is the host's text, so it is given once.</summary>
    internal DiagramLayout LayoutFor(string looseLabel) =>
        layout ??= DiagramLayout.Of(Graph, Outline.Stages.Count, Untried, View.Stages.Select(stage => stage.Label).ToList(), looseLabel);

    internal static FlowData Build(BrainProcess process, WorkbenchLocale locale, IReadOnlyList<BrainProcess> known, FlowEntry entry, FlowScenarioReport report)
    {
        var outline = ProcessOutline.From(process);
        var graph = FlowGraph.From(process, locale, known);
        var words = FlowWords.For(process, locale, known);
        var scenarios = report.Scenarios
            .Select(result => new ScenarioLine(result.Name, result.Passed, result.Failure, [.. result.Trace.Select(words.Step)]))
            .ToList();
        var coverage = report.Coverage;
        return new FlowData
        {
            Process = process,
            Outline = outline,
            Entry = entry,
            View = FlowDescriber.Describe(process, locale, known),
            Graph = graph,
            Coverage = coverage,
            Words = words,
            Scenarios = scenarios,
            Untried = [.. coverage.NeverTaken.Select(route => route.Step + "/" + route.Label)],
            Targets = FlowReviewTargets.Of(process),
            TriggerIds = FlowReviewTargets.TriggerIds(process),
        };
    }

    internal static string RouteTarget(string step, string label) => FlowReviewTargets.Route(step, label);
}

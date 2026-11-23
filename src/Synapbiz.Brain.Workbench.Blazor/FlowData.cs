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

    internal required DiagramLayout Layout { get; init; }

    /// <summary>The routes no scenario takes, as <c>step/label</c>.</summary>
    internal required HashSet<string> Untried { get; init; }

    /// <summary>Every target of a review of this flow, in the order of the export: the flow, its stages, steps, routes and scenarios.</summary>
    internal required IReadOnlyList<string> Targets { get; init; }

    internal static FlowData Build(BrainProcess process, WorkbenchLocale locale, IReadOnlyList<BrainProcess> known, FlowEntry entry)
    {
        var outline = ProcessOutline.From(process);
        var graph = FlowGraph.From(process, locale, known);
        var results = process.RunScenarios().Results.ToDictionary(result => result.Name, StringComparer.Ordinal);
        var words = FlowWords.For(process, locale, known);
        var scenarios = outline.Scenarios.Select(name =>
        {
            var result = results.GetValueOrDefault(name);
            var trace = FlowSimulation.Replay(process, name).History
                .Where(item => item.Kind == ProcessHistoryKind.StepEntered && item.Step is not null)
                .Select(item => words.Step(item.Step!))
                .ToList();
            return new ScenarioLine(name, result?.Passed ?? false, result?.Failure, trace);
        }).ToList();
        var coverage = FlowCoverage.Of(process, null, locale);
        var targets = new List<string> { "process" };
        targets.AddRange(outline.Stages.Select(stage => "stage:" + stage));
        targets.AddRange(outline.Steps.Select(step => "step:" + step.Id));
        targets.AddRange(outline.Steps.SelectMany(step => step.Routes.Select(route => RouteTarget(step.Id, route.Label))));
        targets.AddRange(outline.Scenarios.Select(name => "scenario:" + name));
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
            Layout = DiagramLayout.Of(graph, outline.Stages.Count),
            Untried = [.. coverage.NeverTaken.Select(route => route.Step + "/" + route.Label)],
            Targets = targets,
        };
    }

    internal static string RouteTarget(string step, string label) => "route:" + step + "/" + label;
}

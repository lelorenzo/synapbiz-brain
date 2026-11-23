using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>What one embedded scenario does to its flow.</summary>
/// <param name="Name">The name of the scenario.</param>
/// <param name="Passed">True when the scenario runs to its own <c>expect</c>.</param>
/// <param name="Failure">What went wrong in the words of the scenario player; null when it passed.</param>
/// <param name="Trace">The ids of the steps the scenario enters, in order.</param>
public sealed record FlowScenarioResult(string Name, bool Passed, string? Failure, IReadOnlyList<string> Trace);

/// <summary>
/// Every embedded scenario of a flow played once: its result and the steps it goes through, and the route coverage they give together.
/// A screen that needs the results, the traces and the coverage of a flow (and the list entry of <see cref="FlowLibrary"/>) takes them
/// from one report, so that no scenario is played twice. It depends on the document alone, never on the other flows or the language,
/// except for the language of the text of a coverage problem.
/// </summary>
public sealed class FlowScenarioReport
{
    private FlowScenarioReport(BrainProcess process, IReadOnlyList<FlowScenarioResult> scenarios, FlowCoverageResult coverage, IReadOnlyList<FlowSimulation> sessions)
    {
        Process = process;
        Scenarios = scenarios;
        Coverage = coverage;
        Sessions = sessions;
    }

    /// <summary>The process the report is about.</summary>
    public BrainProcess Process { get; }

    /// <summary>One result for each embedded scenario, in document order.</summary>
    public IReadOnlyList<FlowScenarioResult> Scenarios { get; }

    /// <summary>The route coverage of the embedded scenarios.</summary>
    public FlowCoverageResult Coverage { get; }

    /// <summary>True when every scenario passes (also true when there are none).</summary>
    public bool AllPass => Scenarios.All(result => result.Passed);

    /// <summary>How many scenarios were played to make the report: one for each embedded scenario, never more.</summary>
    public int ScenariosPlayed => Scenarios.Count;

    /// <summary>The sessions of the played scenarios, in document order, for a caller that adds its own runs to the coverage.</summary>
    internal IReadOnlyList<FlowSimulation> Sessions { get; }

    /// <summary>Plays one scenario that is not in the document (a proposed one) and says what it does; null when it is not valid for the document.</summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="name">The name to give the result.</param>
    /// <param name="scenarioJson">The scenario as a JSON object.</param>
    public static FlowScenarioResult? ResultOf(BrainProcess process, string name, string scenarioJson)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(scenarioJson);
        var scenario = FlowJson.TryObject(scenarioJson);
        if (scenario is null || ProcessOutline.CheckScenario(process, scenario).Count > 0)
        {
            return null;
        }

        var run = FlowSimulation.Play(process, scenario);
        return new FlowScenarioResult(name, run.Failure is null, run.Failure, run.Session.EnteredSteps);
    }

    /// <summary>Plays every embedded scenario once and reads results, traces and coverage from those runs.</summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="locale">The language of the text of a coverage problem.</param>
    public static FlowScenarioReport Of(BrainProcess process, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(process);
        var outline = ProcessOutline.From(process);
        var runs = FlowCoverage.PlayScenarios(process, outline);
        var results = new ReadOnlyCollection<FlowScenarioResult>([.. runs.Select(run => new FlowScenarioResult(run.Session.Name, run.Failure is null, run.Failure, run.Session.EnteredSteps))]);
        var sessions = new ReadOnlyCollection<FlowSimulation>([.. runs.Select(run => run.Session)]);
        return new FlowScenarioReport(process, results, FlowCoverage.Compute(outline, sessions, locale), sessions);
    }
}

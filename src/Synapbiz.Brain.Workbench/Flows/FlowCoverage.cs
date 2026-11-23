using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>How often the runs took one route out of a step.</summary>
/// <param name="Step">The step the route leaves.</param>
/// <param name="Label">The label of the route (<c>next</c>, <c>onError</c>, <c>on:approved</c>, <c>interrupt</c> and so on).</param>
/// <param name="Target">The step the route leads to.</param>
/// <param name="Times">How many times the runs took the route.</param>
/// <param name="Scenarios">The names of the runs that took it, in the order the runs were played, each once.</param>
public sealed record FlowRouteCoverage(string Step, string Label, string Target, int Times, IReadOnlyList<string> Scenarios);

/// <summary>The routes the embedded scenarios (and any extra runs) took out of the steps of a flow.</summary>
public sealed record FlowCoverageResult
{
    /// <summary>One entry per route of the document, in document order.</summary>
    public required IReadOnlyList<FlowRouteCoverage> Routes { get; init; }

    /// <summary>The routes no run took.</summary>
    public required IReadOnlyList<FlowRouteCoverage> NeverTaken { get; init; }

    /// <summary>The ids of the steps no run entered.</summary>
    public required IReadOnlyList<string> StepsNeverEntered { get; init; }

    /// <summary>The number of routes of the document.</summary>
    public int TotalRoutes => Routes.Count;

    /// <summary>The number of routes at least one run took.</summary>
    public int CoveredRoutes => Routes.Count - NeverTaken.Count;

    /// <summary>The number of steps of the document.</summary>
    public required int TotalSteps { get; init; }

    /// <summary>The number of steps at least one run entered.</summary>
    public int EnteredSteps => TotalSteps - StepsNeverEntered.Count;

    /// <summary>The runs whose history the routes could not explain: code <c>workbench.flow.route_unexplained</c>, the path is the name of the run.</summary>
    public required IReadOnlyList<WorkbenchError> Problems { get; init; }
}

/// <summary>Route coverage of a flow: which way each run left each step, read from the history of the run.</summary>
public static class FlowCoverage
{
    /// <summary>A history the routes of the document cannot explain.</summary>
    public const string ErrorRouteUnexplained = "workbench.flow.route_unexplained";

    /// <summary>
    /// Plays every embedded scenario, reads from the history which route each run took out of each step, and adds the runs given. A
    /// history the routes cannot explain is reported as <see cref="ErrorRouteUnexplained"/> for that run and never thrown; the routes
    /// that could be read before it still count.
    /// </summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="extraRuns">Other runs of the same flow, for example a simulation a reader made; their <see cref="FlowSimulation.Name"/> names them.</param>
    public static FlowCoverageResult Of(BrainProcess process, IEnumerable<FlowSimulation>? extraRuns = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        var outline = ProcessOutline.From(process);
        var runs = outline.Scenarios.Select(name => FlowSimulation.Replay(process, name)).Concat(extraRuns ?? []).ToList();
        var taken = new Dictionary<(string Step, string Label), List<string>>();
        var entered = new HashSet<string>(StringComparer.Ordinal);
        var problems = new List<WorkbenchError>();
        foreach (var run in runs)
        {
            entered.UnionWith(run.History.Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => entry.Step!));
            var read = RouteReader.Taken(outline, run.History);
            foreach (var route in read.Routes)
            {
                if (!taken.TryGetValue(route, out var names))
                {
                    taken[route] = names = [];
                }

                names.Add(run.Name);
            }

            if (read.Problem is { } problem)
            {
                problems.Add(new WorkbenchError(ErrorRouteUnexplained, run.Name, FlowTemplates.Text(WorkbenchLocale.En, "error.route_unexplained", ("step", problem.Step), ("to", problem.To))));
            }
        }

        var routes = outline.Steps
            .SelectMany(step => step.Routes.Select(route =>
            {
                var names = taken.TryGetValue((step.Id, route.Label), out var found) ? found : [];
                return new FlowRouteCoverage(step.Id, route.Label, route.Target, names.Count, [.. names.Distinct(StringComparer.Ordinal)]);
            }))
            .ToList();
        return new FlowCoverageResult
        {
            Routes = routes,
            NeverTaken = [.. routes.Where(route => route.Times == 0)],
            StepsNeverEntered = [.. outline.Steps.Select(step => step.Id).Where(id => !entered.Contains(id))],
            TotalSteps = outline.Steps.Count,
            Problems = problems,
        };
    }
}

/// <summary>Reads, from the history of a run, which route it took out of each step: the logic a test helper of the process library used, built on the outline.</summary>
internal static class RouteReader
{
    internal sealed record Unexplained(string Step, string To);

    internal sealed record Reading(IReadOnlyList<(string Step, string Label)> Routes, Unexplained? Problem);

    /// <summary>Every (step, label) the run took, in order, up to the first step the history cannot explain.</summary>
    internal static Reading Taken(ProcessOutline outline, IReadOnlyList<ProcessHistoryEntry> history)
    {
        var taken = new List<(string Step, string Label)>();
        var entered = Enumerable.Range(0, history.Count).Where(index => history[index].Kind == ProcessHistoryKind.StepEntered).ToList();
        for (var position = 0; position + 1 < entered.Count; position++)
        {
            var from = history[entered[position]].Step!;
            var to = history[entered[position + 1]].Step!;
            var segment = history.Skip(entered[position] + 1).Take(entered[position + 1] - entered[position] - 1).ToList();
            if (outline.Step(from) is not { } step)
            {
                return new Reading(taken, new Unexplained(from, to));
            }

            if (string.Equals(from, to, StringComparison.Ordinal) && segment.Any(entry => entry.Kind is ProcessHistoryKind.Suspended or ProcessHistoryKind.Resumed))
            {
                continue;
            }

            var label = Resolve(step, segment, to);
            if (label is null)
            {
                return new Reading(taken, new Unexplained(from, to));
            }

            taken.Add((from, label));
        }

        return new Reading(taken, null);
    }

    // The label the history points at, when the route it names leads where the run went; else the one declared route that does.
    private static string? Resolve(OutlineStep step, List<ProcessHistoryEntry> segment, string to)
    {
        string? inferred;
        try
        {
            inferred = Label(step, segment, to);
        }
        catch (Exception error) when (error is InvalidOperationException or NullReferenceException or FormatException or KeyNotFoundException or System.Text.Json.JsonException)
        {
            inferred = null;
        }

        if (inferred is not null && step.Routes.Any(route => route.Label == inferred && route.Target == to))
        {
            return inferred;
        }

        var candidates = step.Routes.Where(route => string.Equals(route.Target, to, StringComparison.Ordinal)).Select(route => route.Label).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static string? Label(OutlineStep step, List<ProcessHistoryEntry> segment, string to) => step.Kind switch
    {
        OutlineStepKind.Read or OutlineStepKind.Action or OutlineStepKind.Ai or OutlineStepKind.Poll => EffectLabel(step, segment),
        OutlineStepKind.Decide => DecideLabel(step, segment),
        OutlineStepKind.HumanTask => segment.Any(entry => entry.Kind == ProcessHistoryKind.TimedOut)
            ? "onTimeout"
            : "on:" + Data(segment.Last(entry => entry.Kind == ProcessHistoryKind.TaskCompleted))["outcome"]!.GetValue<string>(),
        OutlineStepKind.Wait => segment.Any(entry => entry.Kind == ProcessHistoryKind.TimedOut) ? "onTimeout" : "next",
        OutlineStepKind.Timer => "next",
        OutlineStepKind.Loop => LoopLabel(step, segment, to),
        OutlineStepKind.Subprocess => SubprocessLabel(step, segment),
        _ => null,
    };

    private static string LoopLabel(OutlineStep step, List<ProcessHistoryEntry> segment, string to)
    {
        if (segment.Any(entry => entry.Kind == ProcessHistoryKind.LoopExhausted))
        {
            return "onExhausted";
        }

        return step.Routes.Any(route => route.Label == "body" && route.Target == to) ? "body" : "next";
    }

    private static string? EffectLabel(OutlineStep step, List<ProcessHistoryEntry> segment)
    {
        if (segment.Any(entry => entry.Kind == ProcessHistoryKind.WindowInterrupted))
        {
            return "interrupt";
        }

        var isPoll = step.Kind == OutlineStepKind.Poll;
        string? label = null;
        for (var index = 0; index < segment.Count; index++)
        {
            var entry = segment[index];
            var following = index + 1 < segment.Count ? segment[index + 1].Kind : (ProcessHistoryKind?)null;
            switch (entry.Kind)
            {
                case ProcessHistoryKind.EffectCompleted:
                    label = "next";
                    break;
                case ProcessHistoryKind.Decided when isPoll && entry.Code is not null:
                    label = "onError";
                    break;
                case ProcessHistoryKind.Decided when isPoll:
                    label = Data(entry)["outcome"] is JsonObject outcome && Ends(step, outcome) ? "next" : null;
                    break;
                case ProcessHistoryKind.PollExpired:
                    label = "onExpired";
                    break;
                case ProcessHistoryKind.EffectFailed when following is ProcessHistoryKind.RetryScheduled:
                case ProcessHistoryKind.EffectFailed when following is ProcessHistoryKind.EffectRequested && entry.Code == "process.ai.output_invalid":
                    break;
                case ProcessHistoryKind.EffectFailed:
                    label = FailureLabel(step, entry);
                    break;
                default:
                    break;
            }
        }

        return label;
    }

    private static bool Ends(OutlineStep poll, JsonObject outcome)
    {
        var value = outcome[poll.Poll!.Field]!.ToJsonString().Trim('"');
        return poll.Poll.Until.Contains(value, StringComparer.Ordinal);
    }

    private static string FailureLabel(OutlineStep step, ProcessHistoryEntry entry)
    {
        if (entry.Code == "process.ai.output_invalid")
        {
            return "onInvalid";
        }

        var kind = entry.DataJson is { } text ? JsonNode.Parse(text)?["kind"]?.GetValue<string>() : null;
        var specific = kind switch
        {
            "NotAvailable" => "onNotAvailable",
            "NotEntitled" => "onNotEntitled",
            "Unknown" => "onUnknown",
            _ => null,
        };
        return specific is not null && step.Routes.Any(route => route.Label == specific) ? specific : "onError";
    }

    private static string DecideLabel(OutlineStep step, List<ProcessHistoryEntry> segment)
    {
        var decided = segment.Last(entry => entry.Kind == ProcessHistoryKind.Decided);
        if (decided.Code is not null)
        {
            return "onError";
        }

        var data = Data(decided);
        if (!data["decided"]!.GetValue<bool>())
        {
            return "onNoDecision";
        }

        if (step.RouteField is not { } route)
        {
            return "next";
        }

        var key = data["outcome"]![route]!.ToJsonString().Trim('"');
        return step.Routes.Any(candidate => candidate.Label == "on:" + key) ? "on:" + key : "onError";
    }

    private static string SubprocessLabel(OutlineStep step, List<ProcessHistoryEntry> segment)
    {
        if (segment.Any(entry => entry.Kind == ProcessHistoryKind.ChildFailed))
        {
            return "onError";
        }

        if (step.Routes.All(route => !route.Label.StartsWith("on:", StringComparison.Ordinal)))
        {
            return "next";
        }

        var result = Data(segment.Last(entry => entry.Kind == ProcessHistoryKind.ChildCompleted))["result"]!.GetValue<string>();
        return step.Routes.Any(route => route.Label == "on:" + result) ? "on:" + result : "onError";
    }

    private static JsonObject Data(ProcessHistoryEntry entry) => JsonNode.Parse(entry.DataJson!)!.AsObject();
}

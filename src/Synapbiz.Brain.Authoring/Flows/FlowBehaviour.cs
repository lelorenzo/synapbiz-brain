using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Authoring;

/// <summary>How a test of the earlier document behaves on the later one.</summary>
public enum FlowBehaviourKind
{
    /// <summary>The case ends in the same place through the same steps.</summary>
    Same = 0,

    /// <summary>The case ends in the same place but through other steps.</summary>
    DifferentPath = 1,

    /// <summary>The case ends somewhere else: another result, or it waits, stops or is cancelled where it used to end.</summary>
    DifferentEnding = 2,

    /// <summary>The test names something the later document does not have, so it cannot be played.</summary>
    NoLongerApplies = 3,
}

/// <summary>Where a replayed case ended up.</summary>
/// <param name="Status">The status of the case: <c>Completed</c>, <c>Waiting</c>, <c>Suspended</c> or <c>Cancelled</c>.</param>
/// <param name="Result">The result, for a completed case.</param>
/// <param name="Step">The step the case is at, for a case that is not completed.</param>
public sealed record FlowEnding(string Status, string? Result, string? Step);

/// <summary>What happened to one test of the earlier document when it was replayed on the later one.</summary>
/// <param name="Scenario">The name of the test.</param>
/// <param name="Kind">How it behaves now.</param>
/// <param name="Before">Where it ended on the earlier document; null when it could not be played there.</param>
/// <param name="After">Where it ends on the later document; null when it no longer applies.</param>
public sealed record FlowBehaviourChange(string Scenario, FlowBehaviourKind Kind, FlowEnding? Before, FlowEnding? After);

/// <summary>Replays every test of the earlier document on the later one and names those that now end differently.</summary>
public static class FlowBehaviour
{
    /// <summary>Plays each test of <paramref name="before"/> on both documents. The ids of renamed steps are matched through <paramref name="diff"/>.</summary>
    /// <param name="before">The earlier document.</param>
    /// <param name="after">The later document.</param>
    /// <param name="diff">The difference between them, for the renames.</param>
    public static IReadOnlyList<FlowBehaviourChange> Compare(BrainProcess before, BrainProcess after, FlowDiff diff)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(diff);
        var renames = diff.Changes.Where(change => change.Kind == FlowChangeKind.StepRenamed)
            .ToDictionary(change => change.Subject, change => (string)change.After!, StringComparer.Ordinal);
        var outline = ProcessOutline.From(before);
        var results = new List<FlowBehaviourChange>();
        foreach (var name in outline.Scenarios.Distinct(StringComparer.Ordinal))
        {
            var json = outline.ScenarioJson(name)!;
            var scenario = AuthoringJson.TryObject(json);
            var was = Play(before, json);
            if (scenario is null || ProcessOutline.CheckScenario(after, scenario).Count > 0)
            {
                results.Add(new FlowBehaviourChange(name, FlowBehaviourKind.NoLongerApplies, was?.Ending, null));
                continue;
            }

            var now = Play(after, json);
            if (was is null || now is null)
            {
                results.Add(new FlowBehaviourChange(name, FlowBehaviourKind.NoLongerApplies, was?.Ending, now?.Ending));
                continue;
            }

            var mapped = was.Ending with { Step = was.Ending.Step is null ? null : renames.GetValueOrDefault(was.Ending.Step, was.Ending.Step) };
            var samePath = was.Trace.Select(step => renames.GetValueOrDefault(step, step)).SequenceEqual(now.Trace, StringComparer.Ordinal);
            var kind = FlowBehaviourKind.DifferentEnding;
            if (mapped == now.Ending)
            {
                kind = samePath ? FlowBehaviourKind.Same : FlowBehaviourKind.DifferentPath;
            }

            results.Add(new FlowBehaviourChange(name, kind, was.Ending, now.Ending));
        }

        return results;
    }

    private sealed record Run(FlowEnding Ending, IReadOnlyList<string> Trace);

    private static Run? Play(BrainProcess process, string scenarioJson)
    {
        try
        {
            var replay = FlowReplay.StartJson(process, scenarioJson);
            _ = replay.PlayToEnd();
            var session = replay.Session;
            var status = session.Status.ToString();
            return new Run(
                new FlowEnding(status, session.State.Result, session.Status == ProcessStatus.Completed ? null : session.CurrentStep),
                session.EnteredSteps);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

public sealed partial class FlowSimulation
{
    /// <summary>
    /// Plays an embedded scenario command by command, as the scenario player of the process library does, and returns the session, so
    /// that a screen can show the scenario step by step. The session ends where the scenario takes the case; whether that is where the
    /// scenario expects is <see cref="BrainProcess.RunScenarios"/>' question, and the replay of a scenario that passes ends in the
    /// state, the trace and the variables it expects. The answer source and the child documents, when given, are those of the session, so
    /// that the reader can go on by hand from where the scenario ended (ask a source, open a child); with neither given the replay is the
    /// one it always was.
    /// </summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="scenarioName">The name of an embedded scenario.</param>
    /// <param name="answerSource">Who answers effects when <see cref="RunAnswersAsync"/> or <see cref="AskAsync"/> is called on the session, or null.</param>
    /// <param name="childProcesses">Child documents for <c>subprocess</c> steps, or null.</param>
    /// <exception cref="ArgumentException">The document has no scenario with that name.</exception>
    public static FlowSimulation Replay(BrainProcess process, string scenarioName, IFlowAnswerSource? answerSource = null, IReadOnlyCollection<BrainProcess>? childProcesses = null)
    {
        var replay = FlowReplay.Start(process, scenarioName, answerSource, childProcesses);
        _ = replay.PlayToEnd();
        return replay.Session;
    }

    /// <summary>The session after a scenario, and what went wrong; null when the scenario ran to its own <c>expect</c>.</summary>
    internal sealed record ScenarioRun(FlowSimulation Session, string? Failure);

    /// <summary>Plays a scenario in the embedded format (it must be valid for the process) and checks its <c>expect</c>.</summary>
    internal static ScenarioRun Play(BrainProcess process, JsonObject scenario)
    {
        var replay = new FlowReplay(process, scenario, null, null);
        _ = replay.PlayToEnd();
        return new ScenarioRun(replay.Session, replay.Failure);
    }

    /// <summary>Starts the case of a scenario: the session with the scenario's start, zones and input, and the scripted answers of its commands.</summary>
    internal static (FlowSimulation Session, ScriptedAnswerSource Script) BeginScenario(BrainProcess process, JsonObject scenario, IFlowAnswerSource? answerSource, IReadOnlyCollection<BrainProcess>? childProcesses)
    {
        var start = scenario["start"]!.AsObject();
        var zones = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        foreach (var (id, offset) in scenario["zones"] as JsonObject ?? [])
        {
            zones[id] = Offset(offset!.GetValue<string>());
        }

        var setup = new FlowSimulationSetup
        {
            At = DateTimeOffset.Parse(start["at"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None),
            Today = DateOnly.ParseExact(start["today"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            InputJson = start["input"] is JsonObject input ? FlowJson.Canonical(input) : null,
            Zones = zones,
            AnswerSource = answerSource,
            ChildProcesses = childProcesses,
        };
        var session = Begin(process, setup, "scenario", ScriptedSource);
        session.Name = scenario["name"]!.GetValue<string>();
        return (session, Script(scenario["events"]!.AsArray()));
    }

    private static TimeSpan Offset(string text)
    {
        var match = OffsetPattern().Match(text);
        var magnitude = new TimeSpan(
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
            0);
        return match.Groups[1].Value == "-" ? -magnitude : magnitude;
    }

    [GeneratedRegex(@"\A([+-])([0-9]{2}):([0-5][0-9])\z", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetPattern();

    // The answers of the scenario's complete and fail commands, as a scripted source holds them: by step, in order.
    private static ScriptedAnswerSource Script(JsonArray events)
    {
        var source = new ScriptedAnswerSource();
        foreach (var item in events.Select(node => node!.AsObject()))
        {
            if (item["complete"] is { } complete)
            {
                source.ForStep(complete.GetValue<string>(), FlowAnswer.Output(FlowJson.Canonical(item["output"] ?? new JsonObject())));
            }
            else if (item["fail"] is { } fail)
            {
                var kind = Enum.Parse<ProcessErrorKind>(item["error"]!.GetValue<string>());
                TimeSpan? retryAfter = item["retryAfter"] is { } hint ? ParseDuration(hint.GetValue<string>()) : null;
                source.ForStep(fail.GetValue<string>(), FlowAnswer.Failure(kind, item["code"]!.GetValue<string>(), retryAfter));
            }
        }

        return source;
    }

    private static TimeSpan ParseDuration(string text) => System.Xml.XmlConvert.ToTimeSpan(text);

    // The first thing a command did that the interpreter did not apply, in the words of the scenario player.
    internal static string? Problem(IEnumerable<FlowSimulationEntry> entries) =>
        entries.Where(entry => entry.Disposition != AdvanceDisposition.Applied)
            .Select(entry => $"{entry.Kind} was {entry.Disposition}: {entry.Code}")
            .FirstOrDefault();

    internal string? Apply(JsonObject item, ScriptedAnswerSource script)
    {
        if (item["complete"] is { } complete)
        {
            return Pending(complete.GetValue<string>(), ProcessPendingKind.Effect, id =>
                Answer(id, complete.GetValue<string>(), script));
        }

        if (item["fail"] is { } fail)
        {
            return Pending(fail.GetValue<string>(), ProcessPendingKind.Effect, id =>
                Answer(id, fail.GetValue<string>(), script));
        }

        if (item["advance"] is { } advance)
        {
            return Problem(AdvanceBy(ParseDuration(advance.GetValue<string>()), ScriptedSource));
        }

        if (item["event"] is { } name)
        {
            var payload = FlowJson.Canonical(item["payload"] ?? new JsonObject());
            return Problem(SendEvent(name.GetValue<string>(), item["correlation"]?.GetValue<string>(), payload, ScriptedSource));
        }

        return ApplyOther(item);
    }

    private string? ApplyOther(JsonObject item)
    {
        if (item["task"] is { } task)
        {
            return Pending(task.GetValue<string>(), ProcessPendingKind.Task, id =>
                Problem(CompleteTask(id, item["outcome"]!.GetValue<string>(), FlowJson.Canonical(item["data"] ?? new JsonObject()), ScriptedSource)));
        }

        if (item["child"] is { } child)
        {
            return Pending(child.GetValue<string>(), ProcessPendingKind.Child, id =>
                Problem(CompleteChild(id, item["result"]!.GetValue<string>(), FlowJson.Canonical(item["output"] ?? new JsonObject()), ScriptedSource)));
        }

        if (item["childFailed"] is { } failed)
        {
            return Pending(failed.GetValue<string>(), ProcessPendingKind.Child, id =>
                Problem(FailChild(id, item["code"]!.GetValue<string>(), ScriptedSource)));
        }

        return item["cancel"] is { } reason
            ? Problem(Cancel(reason.GetValue<string>(), ScriptedSource))
            : Problem(Resume(ScriptedSource));
    }

    private string? Answer(string effectId, string step, ScriptedAnswerSource script)
    {
        var port = effects.TryGetValue(effectId, out var request) ? request.Port : string.Empty;
        var answer = script.Next(step, port);
        if (answer is null)
        {
            return $"there is no scripted answer for step '{step}'";
        }

        return Problem(answer.IsFailure
            ? FailEffect(effectId, answer.Kind, answer.Code!, answer.RetryAfter, ScriptedSource)
            : CompleteEffect(effectId, answer.OutputJson!, ScriptedSource));
    }

    private string? Pending(string step, ProcessPendingKind kind, Func<string, string?> send)
    {
        if (state.CurrentStep != step || state.Pending is not { } pending || pending.Kind != kind)
        {
            return $"the event addresses {kind} of step '{step}' but the instance is {state.Status} on '{state.CurrentStep}'";
        }

        return send(pending.Id);
    }

    internal static string? Check(FlowSimulation session, JsonObject expect)
    {
        var state = session.state;
        var status = expect["status"]!.GetValue<string>();
        if (!string.Equals(state.Status.ToString(), status, StringComparison.Ordinal))
        {
            return $"expected status {status} but it was {state.Status}";
        }

        if (expect["result"]?.GetValue<string>() is { } result && !string.Equals(state.Result, result, StringComparison.Ordinal))
        {
            return $"expected result '{result}' but it was '{state.Result}'";
        }

        var trace = session.history.Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => entry.Step!).ToList();
        if (expect["trace"] is JsonArray expected
            && !expected.Select(step => step!.GetValue<string>()).SequenceEqual(trace, StringComparer.Ordinal))
        {
            return $"expected trace [{string.Join(", ", expected.Select(step => step!.GetValue<string>()))}] but it was [{string.Join(", ", trace)}]";
        }

        return expect["variables"] is JsonObject variables ? CheckVariables(state, variables) : null;
    }

    private static string? CheckVariables(ProcessState state, JsonObject expected)
    {
        foreach (var (name, value) in expected)
        {
            var actual = state.Variables.TryGetValue(name, out var text) ? JsonNode.Parse(text) : null;
            if (!FlowJson.Equal(value, actual))
            {
                return $"expected variable '{name}' to be {FlowJson.Canonical(value)} but it was {(actual is null ? "absent" : FlowJson.Canonical(actual))}";
            }
        }

        return null;
    }
}

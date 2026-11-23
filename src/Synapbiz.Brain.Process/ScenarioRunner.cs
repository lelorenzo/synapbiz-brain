using System.Globalization;
using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Process;

/// <summary>The result of one scenario.</summary>
/// <param name="Name">The name of the scenario.</param>
/// <param name="Passed">True when the scenario ran as expected.</param>
/// <param name="Failure">What went wrong, in English; null when the scenario passed.</param>
public sealed record ScenarioResult(string Name, bool Passed, string? Failure);

/// <summary>The result of running the scenarios of a process.</summary>
/// <param name="Results">One result per scenario, in document order.</param>
public sealed record ScenarioRunResult(IReadOnlyList<ScenarioResult> Results)
{
    /// <summary>True when every scenario passed; also true when the process has no scenarios.</summary>
    public bool IsOk => Results.All(result => result.Passed);
}

/// <summary>Runs the scenarios embedded in a process on a virtual clock (section 8 of the specification).</summary>
internal static class ScenarioRunner
{
    internal static ScenarioRunResult Run(BrainProcess process, ProcessDefinition definition)
    {
        var results = new List<ScenarioResult>();
        foreach (var node in definition.Scenarios)
        {
            var scenario = node!.AsObject();
            var failure = new ScenarioExecution(process).Run(scenario);
            results.Add(new ScenarioResult(JsonText.Text(scenario["name"])!, failure is null, failure));
        }

        return new ScenarioRunResult(results);
    }
}

/// <summary>One run of one scenario: the virtual clock, the outstanding timers, the state and the trace.</summary>
internal sealed class ScenarioExecution(BrainProcess process)
{
    private readonly Dictionary<string, DateTimeOffset> timers = new(StringComparer.Ordinal);
    private readonly List<string> trace = [];
    private DateTimeOffset startedAt;
    private DateTimeOffset now;
    private DateOnly startDay;
    private ProcessState state = new();

    /// <summary>Runs the scenario; null when it passed, else the first thing that went wrong.</summary>
    internal string? Run(JsonObject scenario)
    {
        var start = scenario["start"]!.AsObject();
        VariableValues.TryInstant(JsonText.Text(start["at"])!, out startedAt);
        VariableValues.TryDate(JsonText.Text(start["today"])!, out startDay);
        now = startedAt;
        var started = process.Start("scenario", start["input"] as JsonObject, Clock());
        if (started.Disposition != AdvanceDisposition.Applied)
        {
            return $"start was {started.Disposition}: {started.Code}";
        }

        Absorb(started);
        foreach (var item in scenario["events"]!.AsArray())
        {
            var failure = Apply(item!.AsObject());
            if (failure is not null)
            {
                return failure;
            }
        }

        return Check(scenario["expect"]!.AsObject());
    }

    private ProcessClock Clock() =>
        new(now, startDay.AddDays((int)((now - startedAt).Ticks / TimeSpan.TicksPerDay)));

    private void Absorb(AdvanceResult result)
    {
        state = result.State;
        foreach (var request in result.Requests)
        {
            if (request is TimerRequest timer)
            {
                timers[timer.TimerId] = timer.DueAt;
            }
            else if (request is CancelTimerRequest cancel)
            {
                timers.Remove(cancel.TimerId);
            }
        }

        trace.AddRange(result.History.Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => entry.Step!));
    }

    private string? Send(ProcessTrigger trigger)
    {
        var advanced = process.Advance(state, trigger, Clock());
        if (advanced.Disposition != AdvanceDisposition.Applied)
        {
            return $"{trigger.GetType().Name} was {advanced.Disposition}: {advanced.Code}";
        }

        Absorb(advanced);
        return null;
    }

    private string? Apply(JsonObject item)
    {
        if (item["complete"] is { } complete)
        {
            return Pending(JsonText.Text(complete)!, ProcessPendingKind.Effect, id =>
                Send(new EffectCompleted(id, JsonText.Canonical(item["output"] ?? new JsonObject()))));
        }

        if (item["fail"] is { } fail)
        {
            var kind = Enum.Parse<ProcessErrorKind>(JsonText.Text(item["error"])!);
            TimeSpan? retryAfter = null;
            if (JsonText.Text(item["retryAfter"]) is { } text && Durations.TryParse(text, false, out var span))
            {
                retryAfter = span;
            }

            return Pending(JsonText.Text(fail)!, ProcessPendingKind.Effect, id =>
                Send(new EffectFailed(id, kind, JsonText.Text(item["code"])!, retryAfter)));
        }

        if (item["advance"] is { } advance)
        {
            Durations.TryParse(JsonText.Text(advance), false, out var span);
            return Advance(span);
        }

        if (item["event"] is { } name)
        {
            return Send(new EventReceived(JsonText.Text(name)!, JsonText.Text(item["correlation"]), JsonText.Canonical(item["payload"] ?? new JsonObject())));
        }

        return ApplyOther(item);
    }

    private string? ApplyOther(JsonObject item)
    {
        if (item["task"] is { } task)
        {
            return Pending(JsonText.Text(task)!, ProcessPendingKind.Task, id =>
                Send(new TaskCompleted(id, JsonText.Text(item["outcome"])!, JsonText.Canonical(item["data"] ?? new JsonObject()))));
        }

        if (item["child"] is { } child)
        {
            return Pending(JsonText.Text(child)!, ProcessPendingKind.Child, id =>
                Send(new ChildCompleted(id, JsonText.Text(item["result"])!, JsonText.Canonical(item["output"] ?? new JsonObject()))));
        }

        if (item["childFailed"] is { } failed)
        {
            return Pending(JsonText.Text(failed)!, ProcessPendingKind.Child, id =>
                Send(new ChildFailed(id, JsonText.Text(item["code"])!)));
        }

        return item["cancel"] is { } reason ? Send(new CancelRequested(JsonText.Text(reason)!)) : Send(new ResumeRequested());
    }

    private string? Pending(string step, ProcessPendingKind kind, Func<string, string?> send)
    {
        if (state.CurrentStep != step || state.Pending is not { } pending || pending.Kind != kind)
        {
            return $"the event addresses {kind} of step '{step}' but the instance is {state.Status} on '{state.CurrentStep}'";
        }

        return send(pending.Id);
    }

    private string? Advance(TimeSpan span)
    {
        var target = Durations.Add(now, span);
        while (true)
        {
            var due = timers.Where(timer => timer.Value <= target)
                .OrderBy(timer => timer.Value).ThenBy(timer => timer.Key, StringComparer.Ordinal)
                .Select(timer => (KeyValuePair<string, DateTimeOffset>?)timer)
                .FirstOrDefault();
            if (due is not { } next)
            {
                break;
            }

            timers.Remove(next.Key);
            if (next.Value > now)
            {
                now = next.Value;
            }

            var failure = Send(new TimerFired(next.Key));
            if (failure is not null)
            {
                return failure;
            }
        }

        now = target;
        return null;
    }

    private string? Check(JsonObject expect)
    {
        var status = JsonText.Text(expect["status"])!;
        if (!string.Equals(state.Status.ToString(), status, StringComparison.Ordinal))
        {
            return $"expected status {status} but it was {state.Status}";
        }

        if (JsonText.Text(expect["result"]) is { } result && !string.Equals(state.Result, result, StringComparison.Ordinal))
        {
            return $"expected result '{result}' but it was '{state.Result}'";
        }

        if (expect["trace"] is JsonArray expected
            && !expected.Select(step => JsonText.Text(step)).SequenceEqual(trace, StringComparer.Ordinal))
        {
            return $"expected trace [{string.Join(", ", expected.Select(step => JsonText.Text(step)))}] but it was [{string.Join(", ", trace)}]";
        }

        return expect["variables"] is JsonObject variables ? CheckVariables(variables) : null;
    }

    private string? CheckVariables(JsonObject expected)
    {
        foreach (var (name, value) in expected)
        {
            var actual = state.Variables.TryGetValue(name, out var text) ? JsonNode.Parse(text) : null;
            if (!JsonText.Equal(value, actual))
            {
                return $"expected variable '{name}' to be {JsonText.Canonical(value)} but it was {(actual is null ? "absent" : JsonText.Canonical(actual))}";
            }
        }

        return null;
    }
}

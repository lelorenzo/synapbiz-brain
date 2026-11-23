using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

public sealed partial class FlowSimulation
{
    internal static FlowSimulation Begin(BrainProcess process, FlowSimulationSetup setup, string instanceId, string source)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(setup);
        JsonObject? input = null;
        if (!string.IsNullOrWhiteSpace(setup.InputJson))
        {
            input = FlowJson.TryObject(setup.InputJson) ?? throw new ArgumentException("The input must be a JSON object that repeats no member name.", nameof(setup));
        }

        foreach (var (id, offset) in setup.Zones)
        {
            if (string.IsNullOrEmpty(id) || Math.Abs(offset.TotalHours) > 14 || offset.Ticks % TimeSpan.TicksPerMinute != 0 || !ZoneIdIsValid(process, id))
            {
                throw new ArgumentException($"The zone '{id}' needs an id an embedded scenario accepts and a fixed offset of at most 14 hours in whole minutes.", nameof(setup));
            }
        }

        var simulation = new FlowSimulation(process, setup, input is null ? "{}" : FlowJson.Canonical(input));
        var result = process.Start(instanceId, input, simulation.Clock());
        simulation.state = result.State;
        simulation.Record(FlowSimulationEntryKind.Started, source, null, instanceId, result);
        if (result.Disposition == AdvanceDisposition.Applied)
        {
            simulation.Absorb(result, source);
        }
        else
        {
            simulation.Started = false;
            simulation.StartCode = result.Code;
        }

        return simulation;
    }

    // The instant cut to whole seconds.
    private static DateTimeOffset WholeSeconds(DateTimeOffset instant) => instant.AddTicks(-(instant.Ticks % TimeSpan.TicksPerSecond));

    // A positive span rounded up to whole seconds (a span the clock cannot hold is left for AdvanceBy to refuse).
    private static TimeSpan WholeSecondsUp(TimeSpan span)
    {
        var rest = span.Ticks % TimeSpan.TicksPerSecond;
        return rest == 0 || span.Ticks > TimeSpan.MaxValue.Ticks - TimeSpan.TicksPerSecond ? span : span + TimeSpan.FromTicks(TimeSpan.TicksPerSecond - rest);
    }

    // The rules of the scenario validator, asked of the validator itself: a minimal scenario with the zone (or the name) and the errors at its path.
    private static bool ZoneIdIsValid(BrainProcess process, string id) => !ScenarioErrors(process, "zone check", new JsonObject { [id] = "+00:00" }).Any(error => error.Path.StartsWith("/zones", StringComparison.Ordinal));

    private static bool NameIsValid(BrainProcess process, string name) => !ScenarioErrors(process, name, null).Any(error => error.Path.StartsWith("/name", StringComparison.Ordinal));

    private static IReadOnlyList<Synapbiz.Brain.Spec.BrainSpecError> ScenarioErrors(BrainProcess process, string name, JsonObject? zones)
    {
        var scenario = new JsonObject
        {
            ["name"] = name,
            ["start"] = new JsonObject { ["at"] = "2027-01-04T14:00:00Z", ["today"] = "2027-01-04" },
            ["events"] = new JsonArray(),
            ["expect"] = new JsonObject { ["status"] = "Completed" },
        };
        if (zones is not null)
        {
            scenario["zones"] = zones;
        }

        return ProcessOutline.CheckScenario(process, scenario);
    }

    // A command given to a session that never started: refused, nothing changes. True when the command must stop.
    private bool Refused(FlowSimulationEntryKind kind, string source, string? subject)
    {
        if (Started)
        {
            return false;
        }

        Reject(kind, source, null, subject, ErrorNotStarted);
        return true;
    }

    internal IReadOnlyList<FlowSimulationEntry> CompleteEffect(string effectId, string outputJson, string source)
    {
        ArgumentNullException.ThrowIfNull(effectId);
        ArgumentNullException.ThrowIfNull(outputJson);
        var before = log.Count;
        if (Refused(FlowSimulationEntryKind.EffectAnswered, source, effectId))
        {
            return Since(before);
        }

        var step = effects.TryGetValue(effectId, out var request) ? request.Step : null;
        var output = FlowJson.TryObject(outputJson);
        FlowJson.TryParse(outputJson, out var usable);
        if (!usable)
        {
            Reject(FlowSimulationEntryKind.EffectAnswered, source, step, effectId, ErrorJsonInvalid);
            return Since(before);
        }

        var scenario = output is null ? null : new JsonObject { ["complete"] = step, ["output"] = FlowJson.Sorted(output) };
        Dispatch(new EffectCompleted(effectId, output is null ? outputJson : FlowJson.Canonical(output)), FlowSimulationEntryKind.EffectAnswered, source, step, effectId, step is null ? null : scenario, output is null && step is not null);
        return Since(before);
    }

    internal IReadOnlyList<FlowSimulationEntry> FailEffect(string effectId, ProcessErrorKind kind, string code, TimeSpan? retryAfter, string source)
    {
        ArgumentNullException.ThrowIfNull(effectId);
        ArgumentNullException.ThrowIfNull(code);
        var before = log.Count;
        if (Refused(FlowSimulationEntryKind.EffectFailed, source, effectId))
        {
            return Since(before);
        }

        var step = effects.TryGetValue(effectId, out var request) ? request.Step : null;
        var hint = retryAfter is { } span && span > TimeSpan.Zero && span.Ticks % TimeSpan.TicksPerSecond == 0 && span <= LongestAdvance ? span : (TimeSpan?)null;
        if (retryAfter is not null && hint is null)
        {
            Reject(FlowSimulationEntryKind.EffectFailed, source, step, effectId, ErrorDurationInvalid);
            return Since(before);
        }

        var scenario = new JsonObject { ["code"] = code, ["error"] = kind.ToString(), ["fail"] = step };
        if (hint is { } valid)
        {
            scenario["retryAfter"] = FlowJson.Duration(valid);
        }

        Dispatch(new EffectFailed(effectId, kind, code, hint), FlowSimulationEntryKind.EffectFailed, source, step, effectId, step is null ? null : scenario, false);
        return Since(before);
    }

    internal IReadOnlyList<FlowSimulationEntry> CompleteTask(string taskId, string outcome, string? dataJson, string source)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        ArgumentNullException.ThrowIfNull(outcome);
        var before = log.Count;
        if (Refused(FlowSimulationEntryKind.TaskCompleted, source, taskId))
        {
            return Since(before);
        }

        var step = tasks.TryGetValue(taskId, out var request) ? request.Step : null;
        if (!TryPayload(dataJson, out var data))
        {
            Reject(FlowSimulationEntryKind.TaskCompleted, source, step, taskId, ErrorJsonInvalid);
            return Since(before);
        }

        var scenario = new JsonObject { ["data"] = FlowJson.Sorted(data), ["outcome"] = outcome, ["task"] = step };
        Dispatch(new TaskCompleted(taskId, outcome, FlowJson.Canonical(data)), FlowSimulationEntryKind.TaskCompleted, source, step, taskId, step is null ? null : scenario, false);
        return Since(before);
    }

    internal IReadOnlyList<FlowSimulationEntry> SendEvent(string name, string? correlation, string? payloadJson, string source)
    {
        ArgumentNullException.ThrowIfNull(name);
        var before = log.Count;
        if (Refused(FlowSimulationEntryKind.EventSent, source, name))
        {
            return Since(before);
        }

        if (!TryPayload(payloadJson, out var payload))
        {
            Reject(FlowSimulationEntryKind.EventSent, source, state.CurrentStep, name, ErrorJsonInvalid);
            return Since(before);
        }

        var scenario = new JsonObject { ["event"] = name, ["payload"] = FlowJson.Sorted(payload) };
        if (correlation is not null)
        {
            scenario["correlation"] = correlation;
        }

        Dispatch(new EventReceived(name, correlation, FlowJson.Canonical(payload)), FlowSimulationEntryKind.EventSent, source, state.CurrentStep, name, scenario, false);
        return Since(before);
    }

    internal IReadOnlyList<FlowSimulationEntry> AdvanceBy(TimeSpan duration, string source)
    {
        var before = log.Count;
        if (Refused(FlowSimulationEntryKind.ClockAdvanced, source, null))
        {
            return Since(before);
        }

        if (duration <= TimeSpan.Zero || duration > LongestAdvance || duration.Ticks % TimeSpan.TicksPerSecond != 0 || now.UtcTicks > DateTimeOffset.MaxValue.UtcTicks - duration.Ticks)
        {
            Reject(FlowSimulationEntryKind.ClockAdvanced, source, null, null, ErrorDurationInvalid);
            return Since(before);
        }

        var target = now + duration;
        while (timers.Where(timer => timer.Value <= target).OrderBy(timer => timer.Value).ThenBy(timer => timer.Key, StringComparer.Ordinal)
                   .Select(timer => (KeyValuePair<string, DateTimeOffset>?)timer).FirstOrDefault() is { } due)
        {
            timers.Remove(due.Key);
            var isWindow = windowTimers.Remove(due.Key);
            if (due.Value > now)
            {
                now = due.Value;
            }

            Dispatch(
                new TimerFired(due.Key),
                isWindow ? FlowSimulationEntryKind.WindowOpened : FlowSimulationEntryKind.TimerFired,
                source,
                timerSteps.GetValueOrDefault(due.Key),
                due.Key,
                null,
                false);
        }

        now = target;
        scenarioEvents.Add(new JsonObject { ["advance"] = FlowJson.Duration(duration) });
        log.Add(new FlowSimulationEntry(log.Count, now, FlowSimulationEntryKind.ClockAdvanced, source, null, FlowJson.Duration(duration), AdvanceDisposition.Applied, null, []));
        return Since(before);
    }

    internal IReadOnlyList<FlowSimulationEntry> CompleteChild(string childKey, string result, string? outputJson, string source)
    {
        ArgumentNullException.ThrowIfNull(childKey);
        ArgumentNullException.ThrowIfNull(result);
        var before = log.Count;
        if (Refused(FlowSimulationEntryKind.ChildFinished, source, childKey))
        {
            return Since(before);
        }

        var step = children.TryGetValue(childKey, out var request) ? request.Step : null;
        if (!TryPayload(outputJson, out var output))
        {
            Reject(FlowSimulationEntryKind.ChildFinished, source, step, childKey, ErrorJsonInvalid);
            return Since(before);
        }

        var scenario = new JsonObject { ["child"] = step, ["output"] = FlowJson.Sorted(output), ["result"] = result };
        Dispatch(new ChildCompleted(childKey, result, FlowJson.Canonical(output)), FlowSimulationEntryKind.ChildFinished, source, step, childKey, step is null ? null : scenario, false);
        return Since(before);
    }

    internal IReadOnlyList<FlowSimulationEntry> FailChild(string childKey, string code, string source)
    {
        ArgumentNullException.ThrowIfNull(childKey);
        ArgumentNullException.ThrowIfNull(code);
        var before = log.Count;
        if (Refused(FlowSimulationEntryKind.ChildFailed, source, childKey))
        {
            return Since(before);
        }

        var step = children.TryGetValue(childKey, out var request) ? request.Step : null;
        var scenario = new JsonObject { ["childFailed"] = step, ["code"] = code };
        Dispatch(new ChildFailed(childKey, code), FlowSimulationEntryKind.ChildFailed, source, step, childKey, step is null ? null : scenario, false);
        return Since(before);
    }

    internal IReadOnlyList<FlowSimulationEntry> Cancel(string reason, string source)
    {
        var before = log.Count;
        if (Refused(FlowSimulationEntryKind.Cancelled, source, reason))
        {
            return Since(before);
        }

        Dispatch(new CancelRequested(reason), FlowSimulationEntryKind.Cancelled, source, state.CurrentStep, reason, new JsonObject { ["cancel"] = reason }, false);
        return Since(before);
    }

    internal IReadOnlyList<FlowSimulationEntry> Resume(string source)
    {
        var before = log.Count;
        if (Refused(FlowSimulationEntryKind.Resumed, source, null))
        {
            return Since(before);
        }

        Dispatch(new ResumeRequested(), FlowSimulationEntryKind.Resumed, source, state.CurrentStep, null, new JsonObject { ["resume"] = true }, false);
        return Since(before);
    }

    private static bool TryPayload(string? text, out JsonObject payload)
    {
        payload = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (FlowJson.TryObject(text) is not { } parsed)
        {
            return false;
        }

        payload = parsed;
        return true;
    }

    internal ProcessClock Clock() => new(now, setup.Today.AddDays((int)((now - setup.At).Ticks / TimeSpan.TicksPerDay)));

    private IReadOnlyList<FlowSimulationEntry> Since(int count) => [.. log.Skip(count)];

    // Sends one trigger. A trigger the interpreter applies is absorbed (and its scenario event kept); any other changes nothing.
    // `unrepresentable` marks a command whose payload the scenario format cannot hold, so that ToScenarioJson can refuse.
    private void Dispatch(ProcessTrigger trigger, FlowSimulationEntryKind kind, string source, string? step, string? subject, JsonObject? scenario, bool unrepresentable)
    {
        var result = process.Advance(state, trigger, Clock());
        Record(kind, source, step, subject, result);
        if (result.Disposition != AdvanceDisposition.Applied)
        {
            return;
        }

        if (scenario is not null)
        {
            scenarioEvents.Add(scenario);
        }

        representable &= !unrepresentable;
        Absorb(result, source);
    }

    private void Record(FlowSimulationEntryKind kind, string source, string? step, string? subject, AdvanceResult result) =>
        log.Add(new FlowSimulationEntry(log.Count, now, kind, source, step, subject, result.Disposition, result.Code, result.History));

    private void Reject(FlowSimulationEntryKind kind, string source, string? step, string? subject, string code) =>
        log.Add(new FlowSimulationEntry(log.Count, now, kind, source, step, subject, AdvanceDisposition.Rejected, code, []));

    // The timers and cancellations of a result are recorded, then every delivery window is answered the way a host would: a timer at
    // the next opening (fired at once when the window is open now), or a failure for a zone the setup does not list.
    private void Absorb(AdvanceResult result, string source)
    {
        state = result.State;
        history.AddRange(result.History);
        foreach (var request in result.Requests)
        {
            Remember(request);
        }

        foreach (var window in result.Requests.OfType<WindowRequest>())
        {
            AnswerWindow(window, source);
        }
    }

    private void Remember(ProcessRequest request)
    {
        switch (request)
        {
            case EffectRequest effect:
                effects[effect.EffectId] = effect;
                break;
            case TaskRequest task:
                tasks[task.TaskId] = task;
                break;
            case WaitRequest wait:
                waits[wait.WaitId] = wait;
                break;
            case ChildStartRequest child:
                children[child.ChildKey] = child;
                break;
            case TimerRequest timer:
                timers[timer.TimerId] = timer.DueAt;
                timerSteps[timer.TimerId] = timer.Step;
                break;
            case CancelTimerRequest cancel:
                timers.Remove(cancel.TimerId);
                windowTimers.Remove(cancel.TimerId);
                break;
            case CancelWaitRequest cancelWait:
                waits.Remove(cancelWait.WaitId);
                break;
            case CancelTaskRequest cancelTask:
                tasks.Remove(cancelTask.TaskId);
                break;
            case WindowRequest window:
                windowTimers.Add(window.TimerId);
                timerSteps[window.TimerId] = window.Step;
                break;
            default:
                break;
        }
    }

    private void AnswerWindow(WindowRequest window, string source)
    {
        if (!zones.TryGetValue(window.Zone, out var zone))
        {
            windowTimers.Remove(window.TimerId);
            Dispatch(new WindowFailed(window.TimerId, ZoneUnknown), FlowSimulationEntryKind.WindowFailed, source, window.Step, window.TimerId, null, false);
            return;
        }

        var opens = DeliveryWindows.NextOpen(now, zone, window.Days, window.From, window.To);
        if (opens > now)
        {
            timers[window.TimerId] = opens;
            return;
        }

        windowTimers.Remove(window.TimerId);
        Dispatch(new TimerFired(window.TimerId), FlowSimulationEntryKind.WindowOpened, source, window.Step, window.TimerId, null, false);
    }

    private List<FlowOutstanding> ComputeOutstanding()
    {
        var items = new List<FlowOutstanding>();
        if (state.Status != ProcessStatus.Waiting)
        {
            return items;
        }

        if (state.Pending is { } pending)
        {
            switch (pending.Kind)
            {
                case ProcessPendingKind.Effect when effects.TryGetValue(pending.Id, out var effect):
                    items.Add(new OutstandingEffect(effect.EffectId, effect.Step, effect.Port, KindOf(effect.EffectKind), effect.InputJson, effect.Attempt));
                    break;
                case ProcessPendingKind.Task when tasks.TryGetValue(pending.Id, out var task):
                    items.Add(new OutstandingTask(task.TaskId, task.Step, task.Queue, task.Outcomes, task.ShowJson));
                    break;
                case ProcessPendingKind.Wait when waits.TryGetValue(pending.Id, out var wait):
                    items.Add(new OutstandingWait(wait.WaitId, wait.Step, wait.Event, wait.Correlation));
                    break;
                case ProcessPendingKind.Child when children.TryGetValue(pending.Id, out var child):
                    items.Add(new OutstandingChild(child.ChildKey, child.Step, child.ProcessId, child.InputJson));
                    break;
                default:
                    break;
            }

            if (pending.InterruptWaitId is { } interruptId && waits.TryGetValue(interruptId, out var interrupt))
            {
                items.Add(new OutstandingWait(interrupt.WaitId, interrupt.Step, interrupt.Event, interrupt.Correlation));
            }
        }

        foreach (var (id, due) in timers.OrderBy(timer => timer.Value).ThenBy(timer => timer.Key, StringComparer.Ordinal))
        {
            var step = timerSteps.GetValueOrDefault(id) ?? state.CurrentStep ?? string.Empty;
            items.Add(windowTimers.Contains(id) ? new OutstandingWindow(id, step, due) : new OutstandingTimer(id, step, due));
        }

        return items;
    }

    private static OutlineStepKind KindOf(ProcessEffectKind kind) => kind switch
    {
        ProcessEffectKind.Read => OutlineStepKind.Read,
        ProcessEffectKind.Action => OutlineStepKind.Action,
        _ => OutlineStepKind.Ai,
    };

    // Asks the source for one effect. A null answer or an exception leaves the effect outstanding (and out of this run).
    private async Task<FlowAnswer?> AskAsync(IFlowAnswerSource source, OutstandingEffect effect, HashSet<string> declined, CancellationToken cancellationToken)
    {
        var step = outline.Step(effect.Step);
        var isAi = step is { Kind: OutlineStepKind.Ai };
        var request = new FlowEffectRequest(
            outline.Id,
            effect.Step,
            effect.Id,
            effect.Port,
            effect.StepKind,
            effect.InputJson,
            effect.Attempt,
            isAi ? step!.Model : null,
            isAi ? step!.PromptId : null,
            isAi ? step!.PromptVersion : null,
            isAi ? step!.OutputSchemaJson : null);
        try
        {
            var answer = await source.AnswerAsync(request, cancellationToken).ConfigureAwait(false);
            if (answer is null)
            {
                declined.Add(effect.Id);
            }

            return answer;
        }
        catch (Exception error) when (!(error is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            declined.Add(effect.Id);
            log.Add(new FlowSimulationEntry(log.Count, now, FlowSimulationEntryKind.AnswerSourceFailed, source.Name, effect.Step, effect.Id, AdvanceDisposition.Rejected, ErrorAnswerSourceFailed, []));
            return null;
        }
    }
}

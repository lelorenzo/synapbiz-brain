using System.Globalization;
using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Process;

/// <summary>What the run did with a trigger before the state is handed back.</summary>
internal readonly record struct TriggerOutcome(AdvanceDisposition Disposition, string? Code)
{
    internal static TriggerOutcome Applied { get; } = new(AdvanceDisposition.Applied, null);

    internal static TriggerOutcome Stale { get; } = new(AdvanceDisposition.Ignored, "process.trigger.stale");

    internal static TriggerOutcome Reject(string code) => new(AdvanceDisposition.Rejected, code);
}

/// <summary>
/// One run of the interpreter: the state of an instance copied into mutable fields, the requests and history entries the
/// run produces, and the step semantics of section 6 of the specification. A run is created per call and never shared.
/// The status of a run is always the status the returned state has: a step that continues leaves it unchanged, a step that
/// blocks sets <see cref="ProcessStatus.Waiting"/>, <c>end</c> and suspension set their own.
/// </summary>
internal sealed partial class ProcessRun(ProcessDefinition definition, ProcessClock clock)
{
    private readonly SortedDictionary<string, string> variables = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> loopCounters = new(StringComparer.Ordinal);
    private readonly List<BufferedEvent> inbox = [];
    private readonly List<ProcessRequest> requests = [];
    private readonly List<ProcessHistoryEntry> history = [];

    private string instanceId = "";
    private ProcessStatus status = ProcessStatus.Waiting;
    private string? currentStep;
    private string? stage;
    private string? result;
    private int attempt;
    private long entry;
    private long stepCount;
    private long sequence;
    private ProcessPending? pending;
    private ProcessProblem? problem;

    internal AdvanceResult Start(string id, JsonObject? input)
    {
        instanceId = id;
        var assignments = new List<KeyValuePair<string, string?>>();
        foreach (var variable in definition.Variables.Values.Where(variable => variable.Initial is not null))
        {
            VariableValues.TryNormalize(variable, variable.Initial, out var initial);
            assignments.Add(new(variable.Name, initial));
        }

        foreach (var (name, node) in (input ?? new JsonObject()).OrderBy(member => member.Key, StringComparer.Ordinal))
        {
            if (!definition.Variables.TryGetValue(name, out var variable) || !variable.Input)
            {
                return RejectedStart("process.input.unknown");
            }

            if (!VariableValues.TryNormalize(variable, node, out var canonical))
            {
                return RejectedStart("process.variable.type_mismatch");
            }

            assignments.Add(new(name, canonical));
        }

        Apply(assignments);
        Log(ProcessHistoryKind.Started);
        RunLoop(definition.Start);
        return Finished();
    }

    internal AdvanceResult Advance(ProcessState state, ProcessTrigger trigger)
    {
        Load(state);
        var outcome = trigger switch
        {
            CancelRequested cancel => Cancel(cancel),
            ResumeRequested => Resume(),
            EventReceived received => ReceiveEvent(received),
            _ => ReceiveResult(trigger),
        };
        return outcome.Disposition == AdvanceDisposition.Applied
            ? Finished()
            : BrainProcess.Unchanged(state, outcome.Disposition, outcome.Code!);
    }

    private AdvanceResult RejectedStart(string code)
    {
        var state = new ProcessState
        {
            InstanceId = instanceId,
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            DefinitionHash = definition.Hash,
            Status = ProcessStatus.Cancelled,
        };
        return new AdvanceResult(state, AdvanceDisposition.Rejected, code, [], []);
    }

    private AdvanceResult Finished() => new(ToState(), AdvanceDisposition.Applied, null, requests, history);

    private ProcessState ToState() => new()
    {
        InstanceId = instanceId,
        DefinitionId = definition.Id,
        DefinitionVersion = definition.Version,
        DefinitionHash = definition.Hash,
        Status = status,
        CurrentStep = currentStep,
        Stage = stage,
        Result = result,
        Variables = new SortedDictionary<string, string>(variables, StringComparer.Ordinal),
        LoopCounters = new SortedDictionary<string, int>(loopCounters, StringComparer.Ordinal),
        Attempt = attempt,
        Entry = entry,
        StepCount = stepCount,
        Sequence = sequence,
        Pending = pending,
        Inbox = [.. inbox],
        Problem = problem,
    };

    private void Load(ProcessState state)
    {
        instanceId = state.InstanceId;
        status = state.Status;
        currentStep = state.CurrentStep;
        stage = state.Stage;
        result = state.Result;
        attempt = state.Attempt;
        entry = state.Entry;
        stepCount = state.StepCount;
        sequence = state.Sequence;
        pending = state.Pending;
        problem = state.Problem;
        foreach (var (name, value) in state.Variables)
        {
            variables[name] = value;
        }

        foreach (var (name, count) in state.LoopCounters)
        {
            loopCounters[name] = count;
        }

        inbox.AddRange(state.Inbox);
    }

    // The run loop of section 5: iterative, never recursive.
    private void RunLoop(string? target)
    {
        var next = target;
        while (next is not null)
        {
            next = Enter(next);
        }
    }

    private string? Enter(string id)
    {
        stepCount++;
        if (stepCount > definition.MaxSteps)
        {
            Suspend("process.limits.max_steps", null);
            return null;
        }

        var step = definition.StepsById[id];
        currentStep = id;
        entry = stepCount;
        attempt = 1;
        pending = null;
        if (step.Stage is not null)
        {
            stage = step.Stage;
        }

        Log(ProcessHistoryKind.StepEntered);
        return Execute(step);
    }

    private TriggerOutcome Cancel(CancelRequested cancel)
    {
        if (status is ProcessStatus.Completed or ProcessStatus.Cancelled)
        {
            return TriggerOutcome.Stale;
        }

        var step = currentStep!;
        switch (pending)
        {
            case { Kind: ProcessPendingKind.Timer or ProcessPendingKind.RetryDelay } timer:
                requests.Add(new CancelTimerRequest(step, timer.Id));
                break;
            case { Kind: ProcessPendingKind.Wait } wait:
                requests.Add(new CancelWaitRequest(step, wait.Id));
                break;
            case { Kind: ProcessPendingKind.Task } task:
                requests.Add(new CancelTaskRequest(step, task.Id));
                break;
            default:
                break;
        }

        CancelTimeoutTimer();
        Log(ProcessHistoryKind.Cancelled, null, Data(("reason", cancel.Reason)));
        status = ProcessStatus.Cancelled;
        currentStep = null;
        pending = null;
        problem = null;
        return TriggerOutcome.Applied;
    }

    private TriggerOutcome Resume()
    {
        if (status != ProcessStatus.Suspended)
        {
            return TriggerOutcome.Stale;
        }

        if (stepCount >= definition.MaxSteps)
        {
            return TriggerOutcome.Reject("process.limits.max_steps");
        }

        problem = null;
        status = ProcessStatus.Waiting;
        Log(ProcessHistoryKind.Resumed);
        stepCount++;
        entry = stepCount;
        attempt = 1;
        RunLoop(Execute(definition.StepsById[currentStep!]));
        return TriggerOutcome.Applied;
    }

    private TriggerOutcome ReceiveEvent(EventReceived received)
    {
        if (status is ProcessStatus.Completed or ProcessStatus.Cancelled)
        {
            return TriggerOutcome.Stale;
        }

        var payload = JsonText.ParseObject(received.PayloadJson);
        if (status == ProcessStatus.Waiting && pending is { Kind: ProcessPendingKind.Wait }
            && definition.StepsById[currentStep!] is WaitStep wait
            && string.Equals(wait.Event, received.Name, StringComparison.Ordinal)
            && CorrelationMatches(wait, received.Correlation))
        {
            RunLoop(ConsumeEvent(wait, payload));
            return TriggerOutcome.Applied;
        }

        if (!definition.Accepts.Contains(received.Name))
        {
            return TriggerOutcome.Reject("process.event.not_accepted");
        }

        if (payload is null)
        {
            return TriggerOutcome.Reject("process.variable.type_mismatch");
        }

        inbox.Add(new BufferedEvent(received.Name, received.Correlation, JsonText.Canonical(payload)));
        Log(ProcessHistoryKind.EventBuffered, null, Data(("event", received.Name)));
        return TriggerOutcome.Applied;
    }

    private bool CorrelationMatches(WaitStep wait, string? correlation) =>
        wait.Correlation is null
        || (variables.TryGetValue(wait.Correlation, out var text)
            && string.Equals(JsonText.Text(JsonNode.Parse(text)), correlation, StringComparison.Ordinal));

    private TriggerOutcome ReceiveResult(ProcessTrigger trigger)
    {
        if (status != ProcessStatus.Waiting || pending is null || currentStep is null)
        {
            return TriggerOutcome.Stale;
        }

        var step = definition.StepsById[currentStep];
        return trigger switch
        {
            EffectCompleted completed when IsPending(ProcessPendingKind.Effect, completed.EffectId) && step is EffectStep effect =>
                Applied(OnEffectCompleted(effect, completed.OutputJson)),
            EffectFailed failed when IsPending(ProcessPendingKind.Effect, failed.EffectId) && step is EffectStep effect =>
                Applied(FailEffect(effect, failed.Kind, failed.Code, failed.RetryAfter)),
            TimerFired fired => OnTimerFired(step, fired.TimerId),
            TaskCompleted task when IsPending(ProcessPendingKind.Task, task.TaskId) && step is HumanTaskStep human =>
                OnTaskCompleted(human, task),
            ChildCompleted child when IsPending(ProcessPendingKind.Child, child.ChildKey) && step is SubprocessStep subprocess =>
                Applied(OnChildCompleted(subprocess, child)),
            ChildFailed child when IsPending(ProcessPendingKind.Child, child.ChildKey) && step is SubprocessStep subprocess =>
                Applied(OnChildFailed(subprocess, child)),
            _ => TriggerOutcome.Stale,
        };
    }

    private bool IsPending(ProcessPendingKind kind, string id) =>
        pending is not null && pending.Kind == kind && string.Equals(pending.Id, id, StringComparison.Ordinal);

    private TriggerOutcome Applied(string? next)
    {
        RunLoop(next);
        return TriggerOutcome.Applied;
    }

    private TriggerOutcome OnTimerFired(StepDef step, string timerId)
    {
        if (IsPending(ProcessPendingKind.Timer, timerId) && step is TimerStep timer)
        {
            Log(ProcessHistoryKind.TimerFired, null, Data(("timerId", timerId)));
            pending = null;
            return Applied(timer.Next);
        }

        if (IsPending(ProcessPendingKind.RetryDelay, timerId) && step is EffectStep effect)
        {
            attempt++;
            pending = null;
            return Applied(RequestEffect(effect));
        }

        if (pending!.TimeoutTimerId is { } timeout && string.Equals(timeout, timerId, StringComparison.Ordinal))
        {
            return OnTimeout(step, timeout);
        }

        return TriggerOutcome.Stale;
    }

    private TriggerOutcome OnTimeout(StepDef step, string timerId)
    {
        switch (step)
        {
            case HumanTaskStep task:
                requests.Add(new CancelTaskRequest(step.Id, pending!.Id));
                Log(ProcessHistoryKind.TimedOut, null, Data(("timerId", timerId)));
                pending = null;
                return Applied(task.OnTimeout);
            case WaitStep wait:
                requests.Add(new CancelWaitRequest(step.Id, pending!.Id));
                Log(ProcessHistoryKind.TimedOut, null, Data(("timerId", timerId)));
                pending = null;
                return Applied(wait.OnTimeout);
            default:
                return TriggerOutcome.Stale;
        }
    }

    private void CancelTimeoutTimer()
    {
        if (pending?.TimeoutTimerId is { } timer)
        {
            requests.Add(new CancelTimerRequest(currentStep!, timer));
        }
    }

    private void Suspend(string code, string? detail)
    {
        CancelTimeoutTimer();
        pending = null;
        status = ProcessStatus.Suspended;
        problem = new ProcessProblem(code, currentStep!, detail);
        Log(ProcessHistoryKind.Suspended, code, detail is null ? null : Data(("detail", detail)));
    }

    private string? RouteOrSuspend(string? target, string code, string? detail)
    {
        if (target is not null)
        {
            return target;
        }

        Suspend(code, detail);
        return null;
    }

    private void Block(ProcessPending work)
    {
        pending = work;
        status = ProcessStatus.Waiting;
    }

    private void Log(ProcessHistoryKind kind, string? code = null, string? data = null)
    {
        sequence++;
        history.Add(new ProcessHistoryEntry(sequence, clock.Now, currentStep, entry, attempt, kind, code, data));
    }

    // Deterministic identifiers of section 3.1.
    private string Key(string suffix) =>
        instanceId + "/" + currentStep + "/" + entry.ToString(CultureInfo.InvariantCulture) + suffix;

    private string IdempotencyKey() => Key("");

    private string EffectId() => Key("/" + attempt.ToString(CultureInfo.InvariantCulture));

    private string RetryTimerId() => Key("/retry/" + attempt.ToString(CultureInfo.InvariantCulture));

    private string TimerId() => Key("/timer");

    private string TimeoutTimerId() => Key("/timeout");

    private string WaitId() => Key("/wait");

    private string TaskId() => Key("/task");

    private string ChildKey() => Key("/child");

    /// <summary>The canonical text of a small object of history data.</summary>
    private static string Data(params (string Key, string Value)[] members)
    {
        var data = new JsonObject();
        foreach (var (key, value) in members)
        {
            data[key] = value;
        }

        return JsonText.Canonical(data);
    }

    private void Apply(IEnumerable<KeyValuePair<string, string?>> updates)
    {
        foreach (var (name, value) in updates)
        {
            if (value is null)
            {
                variables.Remove(name);
            }
            else
            {
                variables[name] = value;
            }
        }
    }

    /// <summary>
    /// Checks the members of an object against a mapping (member to variable) without changing anything. A missing or
    /// null member clears its variable; a value that does not fit the variable fails the whole assignment.
    /// </summary>
    private bool TryAssign(Mapping mapping, JsonObject source, out List<KeyValuePair<string, string?>> updates)
    {
        updates = [];
        foreach (var (member, variable) in mapping.Entries)
        {
            if (!VariableValues.TryNormalize(definition.Variables[variable], source[member], out var canonical))
            {
                return false;
            }

            updates.Add(new(variable, canonical));
        }

        return true;
    }

    private JsonObject BuildObject(Mapping mapping)
    {
        var built = new JsonObject();
        foreach (var (key, variable) in mapping.Entries)
        {
            if (variables.TryGetValue(variable, out var text))
            {
                built[key] = JsonNode.Parse(text);
            }
        }

        return built;
    }
}

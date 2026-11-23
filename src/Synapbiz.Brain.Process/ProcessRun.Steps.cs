using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process;

/// <summary>The step executors: what each kind of step does when control arrives at it (sections 6.1 to 6.9).</summary>
internal sealed partial class ProcessRun
{
    private string? Execute(StepDef step) => step switch
    {
        PollStep poll => StartPoll(poll),
        EffectStep effect => RequestEffect(effect),
        DecideStep decide => Decide(decide),
        HumanTaskStep task => RequestTask(task),
        WaitStep wait => EnterWait(wait),
        TimerStep timer => StartTimer(timer),
        LoopStep loop => ArriveAtLoop(loop),
        SubprocessStep subprocess => StartChild(subprocess),
        EndStep end => End(end),
        _ => throw new InvalidOperationException($"Unknown step kind {step.Kind}."),
    };

    // 6.1 and 6.2: read, action and ai. An action that names a delivery window waits for it before every attempt.
    private string? RequestEffect(EffectStep step) =>
        step is ActionStep { Window: { } window } ? EnterWindow(definition.Windows[window]) : SendEffect(step);

    private string? SendEffect(EffectStep step)
    {
        var input = BuildObject(step.Input);
        if (step is AiStep ai && !IsValid(ai.InputSchema!, input))
        {
            return FailEffect(step, ProcessErrorKind.Permanent, "process.ai.input_invalid", null);
        }

        var model = step as AiStep;
        requests.Add(new EffectRequest(
            step.Id,
            EffectId(),
            IdempotencyKey(),
            step switch
            {
                AiStep => ProcessEffectKind.Ai,
                ActionStep => ProcessEffectKind.Action,
                _ => ProcessEffectKind.Read,
            },
            step.Port,
            JsonText.Canonical(input),
            attempt,
            step is ActionStep { AtMostOnce: true } ? ProcessDelivery.AtMostOnce : ProcessDelivery.AtLeastOnce,
            model?.Model,
            model?.PromptId,
            model?.PromptVersion));
        Log(ProcessHistoryKind.EffectRequested, null, Data(("effectId", EffectId())));
        Block(new ProcessPending(ProcessPendingKind.Effect, EffectId()));
        return null;
    }

    private string? FailEffect(EffectStep step, ProcessErrorKind kind, string code, TimeSpan? retryAfter)
    {
        Log(ProcessHistoryKind.EffectFailed, code, Data(("kind", kind.ToString())));
        if (kind is ProcessErrorKind.Transient or ProcessErrorKind.RateLimited && attempt < step.Retry.MaxAttempts)
        {
            ScheduleRetry(step, kind, retryAfter);
            return null;
        }

        var target = kind switch
        {
            ProcessErrorKind.NotAvailable => step.OnNotAvailable,
            ProcessErrorKind.NotEntitled => step.OnNotEntitled,
            ProcessErrorKind.Unknown => step.OnUnknown,
            _ => null,
        };
        return RouteOrSuspend(target ?? step.OnError, code, null);
    }

    private void ScheduleRetry(EffectStep step, ProcessErrorKind kind, TimeSpan? retryAfter)
    {
        var policy = step.Retry;
        var delay = policy.Exponential ? TimeSpan.FromTicks(policy.Initial.Ticks * (1L << (attempt - 1))) : policy.Initial;
        if (policy.Max is { } cap && delay > cap)
        {
            delay = cap;
        }

        if (kind == ProcessErrorKind.RateLimited && retryAfter is { } hint && hint > delay)
        {
            delay = hint;
        }

        var due = Durations.Add(clock.Now, delay);
        requests.Add(new TimerRequest(step.Id, RetryTimerId(), due));
        Log(ProcessHistoryKind.RetryScheduled, null, Data(("timerId", RetryTimerId()), ("dueAt", due.ToString("O", CultureInfo.InvariantCulture))));
        Block(new ProcessPending(ProcessPendingKind.RetryDelay, RetryTimerId()));
    }

    private string? OnEffectCompleted(EffectStep step, string outputJson)
    {
        var output = JsonText.ParseObject(outputJson);
        if (step is AiStep ai)
        {
            return OnAiCompleted(ai, output);
        }

        if (output is null)
        {
            return FailEffect(step, ProcessErrorKind.Permanent, "process.effect.output_malformed", null);
        }

        if (!TryAssign(step.Output, output, out var updates))
        {
            return FailEffect(step, ProcessErrorKind.Permanent, "process.variable.type_mismatch", null);
        }

        Apply(updates);
        Log(ProcessHistoryKind.EffectCompleted, null, Data(("effectId", EffectId())));
        return step is PollStep poll ? EvaluatePoll(poll) : step.Next;
    }

    private string? OnAiCompleted(AiStep step, JsonObject? output)
    {
        var inputHash = ContentHash.FromBytes(Encoding.UTF8.GetBytes(JsonText.Canonical(BuildObject(step.Input)))).Value;
        if (output is not null && IsValid(step.OutputSchema!, output) && TryAssign(step.Output, output, out var updates))
        {
            Apply(updates);
            Log(ProcessHistoryKind.EffectCompleted, null, AiProvenance(step, inputHash, output));
            return step.Next;
        }

        Log(ProcessHistoryKind.EffectFailed, "process.ai.output_invalid");
        if (attempt < step.MaxAttempts)
        {
            attempt++;
            pending = null;
            return RequestEffect(step);
        }

        return step.OnInvalid;
    }

    private static string AiProvenance(AiStep step, string inputHash, JsonObject output) =>
        JsonText.Canonical(new JsonObject
        {
            ["model"] = step.Model,
            ["promptId"] = step.PromptId,
            ["promptVersion"] = step.PromptVersion,
            ["inputHash"] = inputHash,
            ["output"] = output.DeepClone(),
        });

    // A schema that cannot be evaluated never throws out of the interpreter: the instance is treated as invalid.
    private static bool IsValid(JsonSchema schema, JsonNode? instance)
    {
        try
        {
            return schema.Evaluate(JsonSerializer.SerializeToElement(instance), new EvaluationOptions { OutputFormat = OutputFormat.Flag }).IsValid;
        }
        catch (Exception error) when (error is JsonSchemaException or JsonException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    // 6.3: decide.
    private string? Decide(DecideStep step)
    {
        var rule = definition.Rules[step.Rule];
        DecisionResult decision;
        try
        {
            decision = rule.Evaluate(BuildFacts(step.Facts), clock.Today);
        }
        catch (BrainException error)
        {
            Log(ProcessHistoryKind.Decided, error.ErrorCode, FailedDecision(rule, error.ErrorCode));
            return RouteOrSuspend(step.OnError, error.ErrorCode, error.Message);
        }

        Log(ProcessHistoryKind.Decided, null, DecisionProvenance(rule, decision));
        if (decision.Outcome is not Decided decided)
        {
            return step.OnNoDecision;
        }

        if (step.CollectInto is not null)
        {
            variables[step.CollectInto] = JsonText.CanonicalOf(decided.OutcomeJson);
            return step.Next;
        }

        return RouteOutcome(step, rule, JsonText.ParseObject(decided.OutcomeJson)!);
    }

    private string? RouteOutcome(DecideStep step, BrainRule rule, JsonObject outcome)
    {
        var updates = new List<KeyValuePair<string, string?>>();
        foreach (var (field, variable) in step.Output.Entries)
        {
            var type = rule.Kind.OutcomeFields.First(candidate => string.Equals(candidate.Name, field, StringComparison.Ordinal)).Type;
            string? canonical = null;
            if (outcome[field] is { } member && !VariableValues.TryFromOutcome(type, definition.Variables[variable], member, out canonical))
            {
                return RouteOrSuspend(step.OnError, "process.variable.type_mismatch", null);
            }

            updates.Add(new(variable, canonical));
        }

        Apply(updates);
        if (step.Route is null)
        {
            return step.Next;
        }

        var key = RouteKey(outcome, step.Route);
        var target = step.On!.Where(entry => string.Equals(entry.Key, key, StringComparison.Ordinal)).Select(entry => entry.Value).FirstOrDefault();
        return RouteOrSuspend(target ?? step.OnError, "process.route.unmatched", null);
    }

    private Dictionary<string, DecisionValue> BuildFacts(Mapping mapping)
    {
        var facts = new Dictionary<string, DecisionValue>(StringComparer.Ordinal);
        foreach (var (fact, variable) in mapping.Entries.Where(entry => variables.ContainsKey(entry.Value)))
        {
            facts[fact] = VariableValues.ToDecisionValue(definition.Variables[variable].Type, JsonNode.Parse(variables[variable])!);
        }

        return facts;
    }

    // The value of an outcome field as a routing key: a Boolean as "true" or "false", null for an absent field.
    private static string? RouteKey(JsonObject outcome, string field) => outcome[field] switch
    {
        null => null,
        JsonValue value when value.GetValueKind() == JsonValueKind.True => "true",
        JsonValue value when value.GetValueKind() == JsonValueKind.False => "false",
        var other => JsonText.Text(other),
    };

    private static string FailedDecision(BrainRule rule, string code) =>
        JsonText.Canonical(new JsonObject { ["rule"] = rule.Id, ["version"] = rule.Version, ["error"] = code });

    private static string DecisionProvenance(BrainRule rule, DecisionResult decision)
    {
        var builder = new StringBuilder("{\"rule\":");
        builder.Append(JsonText.Canonical(rule.Id))
            .Append(",\"version\":").Append(rule.Version.ToString(CultureInfo.InvariantCulture))
            .Append(",\"decided\":").Append(decision.Trace.IsDecided ? "true" : "false")
            .Append(",\"matchedRowKeys\":").Append(JsonText.Canonical(new JsonArray([.. decision.Trace.MatchedRowKeys.Select(key => (JsonNode?)JsonValue.Create(key))])))
            .Append(",\"outcome\":").Append(decision.Trace.OutcomeJson is null ? "null" : JsonText.CanonicalOf(decision.Trace.OutcomeJson))
            .Append(",\"factsHash\":").Append(JsonText.Canonical(decision.Trace.FactsHash))
            .Append(",\"facts\":").Append(JsonText.CanonicalOf(decision.Trace.FactsJson))
            .Append('}');
        return builder.ToString();
    }

    // 6.4: humanTask.
    private string? RequestTask(HumanTaskStep step)
    {
        var show = new JsonObject();
        foreach (var name in step.Show.Where(variables.ContainsKey))
        {
            show[name] = JsonNode.Parse(variables[name]);
        }

        requests.Add(new TaskRequest(step.Id, TaskId(), step.Queue, step.Outcomes, JsonText.Canonical(show)));
        Log(ProcessHistoryKind.TaskRequested, null, Data(("taskId", TaskId())));
        Block(new ProcessPending(ProcessPendingKind.Task, TaskId(), StartTimeout(step.Timeout)));
        return null;
    }

    private string? StartTimeout(TimeSpan? timeout)
    {
        if (timeout is not { } span)
        {
            return null;
        }

        requests.Add(new TimerRequest(currentStep!, TimeoutTimerId(), Durations.Add(clock.Now, span)));
        return TimeoutTimerId();
    }

    private TriggerOutcome OnTaskCompleted(HumanTaskStep step, TaskCompleted completed)
    {
        if (!step.Outcomes.Contains(completed.Outcome, StringComparer.Ordinal))
        {
            return TriggerOutcome.Reject("process.task.outcome_unknown");
        }

        var data = JsonText.ParseObject(completed.DataJson);
        if (data is null || !TryAssign(step.Output, data, out var updates))
        {
            return TriggerOutcome.Reject("process.variable.type_mismatch");
        }

        Apply(updates);
        CancelTimeoutTimer();
        pending = null;
        Log(ProcessHistoryKind.TaskCompleted, null, Data(("outcome", completed.Outcome)));
        return Applied(step.On.First(entry => string.Equals(entry.Key, completed.Outcome, StringComparison.Ordinal)).Value);
    }

    // 6.5: timer.
    private string? StartTimer(TimerStep step)
    {
        DateTimeOffset due;
        if (step.After is { } after)
        {
            due = Durations.Add(clock.Now, after);
        }
        else
        {
            if (!variables.TryGetValue(step.Until!, out var text))
            {
                Suspend("process.timer.until_missing", null);
                return null;
            }

            VariableValues.TryInstant(JsonText.Text(JsonNode.Parse(text))!, out var instant);
            due = Durations.Add(instant, step.Offset ?? TimeSpan.Zero);
        }

        requests.Add(new TimerRequest(step.Id, TimerId(), due));
        Log(ProcessHistoryKind.TimerStarted, null, Data(("timerId", TimerId()), ("dueAt", due.ToString("O", CultureInfo.InvariantCulture))));
        Block(new ProcessPending(ProcessPendingKind.Timer, TimerId()));
        return null;
    }

    // 6.6: wait.
    private string? EnterWait(WaitStep step)
    {
        string? correlation = null;
        if (step.Correlation is { } variable)
        {
            if (!variables.TryGetValue(variable, out var text))
            {
                Suspend("process.wait.correlation_missing", null);
                return null;
            }

            correlation = JsonText.Text(JsonNode.Parse(text));
            if (correlation?.Contains('\0', StringComparison.Ordinal) == true)
            {
                // No store keeps a text with U+0000, so no host could match this wait to an event: the step is not entered.
                Suspend("process.wait.correlation_invalid", null);
                return null;
            }
        }

        var index = inbox.FindIndex(buffered =>
            string.Equals(buffered.Name, step.Event, StringComparison.Ordinal)
            && (step.Correlation is null || string.Equals(NormalizedCorrelation(step, buffered.Correlation), correlation, StringComparison.Ordinal)));
        if (index >= 0)
        {
            var buffered = inbox[index];
            inbox.RemoveAt(index);
            return ConsumeEvent(step, JsonText.ParseObject(buffered.PayloadJson));
        }

        requests.Add(new WaitRequest(step.Id, WaitId(), step.Event, correlation));
        Log(ProcessHistoryKind.WaitStarted, null, Data(("waitId", WaitId()), ("event", step.Event)));
        Block(new ProcessPending(ProcessPendingKind.Wait, WaitId(), StartTimeout(step.Timeout)));
        return null;
    }

    private string? ConsumeEvent(WaitStep step, JsonObject? payload)
    {
        if (payload is null || !TryAssign(step.Output, payload, out var updates))
        {
            Suspend("process.variable.type_mismatch", null);
            return null;
        }

        Apply(updates);
        CancelTimeoutTimer();
        pending = null;
        Log(ProcessHistoryKind.EventConsumed, null, Data(("event", step.Event)));
        return step.Next;
    }

    // 6.7: loop.
    private string? ArriveAtLoop(LoopStep step)
    {
        loopCounters.TryGetValue(step.Id, out var previous);
        var arrival = previous + 1;
        loopCounters[step.Id] = arrival;
        JsonArray? list = null;
        if (step.ForEach is not null)
        {
            list = variables.TryGetValue(step.ForEach, out var text) ? JsonNode.Parse(text) as JsonArray : null;
            if (list is null)
            {
                Suspend("process.loop.not_a_list", null);
                return null;
            }

            if (arrival > list.Count)
            {
                return step.Next;
            }
        }

        if (arrival > step.MaxIterations)
        {
            Log(ProcessHistoryKind.LoopExhausted);
            return step.OnExhausted;
        }

        if (list is not null)
        {
            // A null element clears the item (no value).
            if (list[arrival - 1] is { } element)
            {
                variables[step.Item!] = JsonText.Canonical(element);
            }
            else
            {
                variables.Remove(step.Item!);
            }
        }

        Log(ProcessHistoryKind.LoopIteration, null, JsonText.Canonical(new JsonObject { ["iteration"] = arrival }));
        return step.Body;
    }

    // 6.8: subprocess.
    private string? StartChild(SubprocessStep step)
    {
        requests.Add(new ChildStartRequest(step.Id, ChildKey(), step.Process, JsonText.Canonical(BuildObject(step.Input))));
        Log(ProcessHistoryKind.ChildStarted, null, Data(("childKey", ChildKey())));
        Block(new ProcessPending(ProcessPendingKind.Child, ChildKey()));
        return null;
    }

    private string? OnChildCompleted(SubprocessStep step, ChildCompleted completed)
    {
        var output = JsonText.ParseObject(completed.OutputJson);
        if (output is null || !TryAssign(step.Output, output, out var updates))
        {
            Log(ProcessHistoryKind.ChildFailed, "process.variable.type_mismatch");
            return RouteOrSuspend(step.OnError, "process.variable.type_mismatch", null);
        }

        Apply(updates);
        Log(ProcessHistoryKind.ChildCompleted, null, Data(("result", completed.Result)));
        if (step.Next is not null)
        {
            return step.Next;
        }

        var target = step.On!.Where(entry => string.Equals(entry.Key, completed.Result, StringComparison.Ordinal)).Select(entry => entry.Value).FirstOrDefault();
        return RouteOrSuspend(target ?? step.OnError, "process.route.unmatched", null);
    }

    private string? OnChildFailed(SubprocessStep step, ChildFailed failed)
    {
        Log(ProcessHistoryKind.ChildFailed, failed.Code);
        return RouteOrSuspend(step.OnError, failed.Code, null);
    }

    // 6.9: end.
    private string? End(EndStep step)
    {
        Log(ProcessHistoryKind.Completed, null, Data(("result", step.Result)));
        status = ProcessStatus.Completed;
        result = step.Result;
        currentStep = null;
        pending = null;
        return null;
    }
}

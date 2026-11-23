using System.Globalization;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Evaluation;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process;

/// <summary>The steps that wait before or between effects: delivery windows on actions and the <c>poll</c> step (sections 3 and 4 of the W1-C specification).</summary>
internal sealed partial class ProcessRun
{
    // The wait for a delivery window: the effect is requested only when the host says the window is open.
    private string? EnterWindow(WindowDef window)
    {
        var zone = window.Zone;
        if (window.ZoneVariable is { } name)
        {
            if (!variables.TryGetValue(name, out var text))
            {
                Suspend("process.window.zone_missing", null);
                return null;
            }

            zone = JsonText.Text(JsonNode.Parse(text));
        }

        var timerId = WindowTimerId();
        requests.Add(new WindowRequest(currentStep!, timerId, zone!, window.Days, window.From, window.To));
        Log(ProcessHistoryKind.WindowWaiting, null, Data(("timerId", timerId), ("zone", zone!)));
        Block(new ProcessPending(ProcessPendingKind.Window, timerId));
        return null;
    }

    // The host could not resolve the zone: the step fails like an effect that failed for good, without a retry.
    private string? OnWindowFailed(ActionStep step, WindowFailed failed)
    {
        Log(ProcessHistoryKind.EffectFailed, failed.Code, Data(("timerId", failed.TimerId)));
        pending = null;
        return RouteOrSuspend(step.OnError, failed.Code, null);
    }

    // 6.10: poll. Entering the step starts round 1; the time limit counts from now.
    private string? StartPoll(PollStep step)
    {
        pollProgress = new ProcessPoll(1, clock.Now);
        return RequestEffect(step);
    }

    private ProcessPoll Progress() => pollProgress ??= new ProcessPoll(1, clock.Now);

    // The read of the round completed and its output is assigned: the rule says whether the condition holds.
    private string? EvaluatePoll(PollStep step)
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
        if (decision.Outcome is Decided decided
            && RouteKey(JsonText.ParseObject(decided.OutcomeJson)!, step.Field) is { } value
            && step.Until.Contains(value, StringComparer.Ordinal))
        {
            return step.Next;
        }

        return WaitForNextRound(step);
    }

    // The condition does not hold yet: another round after the interval, unless that would pass the time limit.
    private string? WaitForNextRound(PollStep step)
    {
        var progress = Progress();
        var due = Durations.Add(clock.Now, step.IntervalAfter(progress.Round));
        if (due > Durations.Add(progress.StartedAt, step.MaxDuration))
        {
            Log(ProcessHistoryKind.PollExpired, null, JsonText.Canonical(new JsonObject { ["rounds"] = progress.Round }));
            return step.OnExpired;
        }

        var timerId = PollTimerId();
        requests.Add(new TimerRequest(step.Id, timerId, due));
        Log(ProcessHistoryKind.TimerStarted, null, Data(("timerId", timerId), ("dueAt", due.ToString("O", CultureInfo.InvariantCulture))));
        Block(new ProcessPending(ProcessPendingKind.Timer, timerId));
        return null;
    }
}

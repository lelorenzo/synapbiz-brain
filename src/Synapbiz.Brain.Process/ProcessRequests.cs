using System.Diagnostics.CodeAnalysis;

namespace Synapbiz.Brain.Process;

/// <summary>The kind of an effect request.</summary>
public enum ProcessEffectKind
{
    /// <summary>A <c>read</c> step.</summary>
    Read,

    /// <summary>An <c>action</c> step.</summary>
    Action,

    /// <summary>An <c>ai</c> step.</summary>
    Ai,
}

/// <summary>The delivery guarantee of an effect.</summary>
public enum ProcessDelivery
{
    /// <summary>The host may deliver the effect more than once; the port de-duplicates with the idempotency key.</summary>
    AtLeastOnce,

    /// <summary>The host must not re-send the effect when its outcome is unknown.</summary>
    AtMostOnce,
}

/// <summary>Something the host must do after it has committed the new state of an instance.</summary>
/// <param name="Step">The step that produced the request.</param>
[SuppressMessage(
    "Major Code Smell",
    "S1694:An abstract class should have both abstract and concrete methods",
    Justification = "A closed, pattern-matchable hierarchy of sealed records is the point; an interface would lose record value-equality. Same reason as DecisionOutcome in Synapbiz.Brain.Contracts.")]
public abstract record ProcessRequest(string Step);

/// <summary>Run an effect on a port.</summary>
/// <param name="Step">The step that produced the request.</param>
/// <param name="EffectId">The id the result must quote.</param>
/// <param name="IdempotencyKey">The key the port uses to de-duplicate; the same for every attempt of the step entry.</param>
/// <param name="EffectKind">Read, action or ai.</param>
/// <param name="Port">The name of the port.</param>
/// <param name="InputJson">The input as the text of a JSON object.</param>
/// <param name="Attempt">The attempt number, from 1.</param>
/// <param name="Delivery">The delivery guarantee.</param>
/// <param name="Model">The model of an ai step; null otherwise.</param>
/// <param name="PromptId">The prompt id of an ai step; null otherwise.</param>
/// <param name="PromptVersion">The prompt version of an ai step; null otherwise.</param>
public sealed record EffectRequest(
    string Step,
    string EffectId,
    string IdempotencyKey,
    ProcessEffectKind EffectKind,
    string Port,
    string InputJson,
    int Attempt,
    ProcessDelivery Delivery,
    string? Model = null,
    string? PromptId = null,
    long? PromptVersion = null) : ProcessRequest(Step);

/// <summary>Start a timer.</summary>
/// <param name="Step">The step that produced the request.</param>
/// <param name="TimerId">The id the <see cref="TimerFired"/> trigger must quote.</param>
/// <param name="DueAt">When the timer is due, in UTC.</param>
public sealed record TimerRequest(string Step, string TimerId, DateTimeOffset DueAt) : ProcessRequest(Step);

/// <summary>Cancel a timer that is no longer needed.</summary>
/// <param name="Step">The step that produced the request.</param>
/// <param name="TimerId">The id of the timer.</param>
public sealed record CancelTimerRequest(string Step, string TimerId) : ProcessRequest(Step);

/// <summary>Start waiting for an external event.</summary>
/// <param name="Step">The step that produced the request.</param>
/// <param name="WaitId">The id of the wait.</param>
/// <param name="Event">The event name.</param>
/// <param name="Correlation">The correlation value the event must carry, or null.</param>
public sealed record WaitRequest(string Step, string WaitId, string Event, string? Correlation) : ProcessRequest(Step);

/// <summary>Stop waiting for an external event.</summary>
/// <param name="Step">The step that produced the request.</param>
/// <param name="WaitId">The id of the wait.</param>
public sealed record CancelWaitRequest(string Step, string WaitId) : ProcessRequest(Step);

/// <summary>Create a human task.</summary>
/// <param name="Step">The step that produced the request.</param>
/// <param name="TaskId">The id the <see cref="TaskCompleted"/> trigger must quote.</param>
/// <param name="Queue">The queue the task belongs to.</param>
/// <param name="Outcomes">The outcomes the task can end with.</param>
/// <param name="ShowJson">The text of a JSON object with the variables to show that have a value.</param>
public sealed record TaskRequest(string Step, string TaskId, string Queue, IReadOnlyList<string> Outcomes, string ShowJson) : ProcessRequest(Step);

/// <summary>Withdraw a human task that is no longer needed.</summary>
/// <param name="Step">The step that produced the request.</param>
/// <param name="TaskId">The id of the task.</param>
public sealed record CancelTaskRequest(string Step, string TaskId) : ProcessRequest(Step);

/// <summary>Start a child instance.</summary>
/// <param name="Step">The step that produced the request.</param>
/// <param name="ChildKey">The key the child triggers must quote.</param>
/// <param name="ProcessId">The id of the process to start.</param>
/// <param name="InputJson">The input of the child as the text of a JSON object.</param>
public sealed record ChildStartRequest(string Step, string ChildKey, string ProcessId, string InputJson) : ProcessRequest(Step);

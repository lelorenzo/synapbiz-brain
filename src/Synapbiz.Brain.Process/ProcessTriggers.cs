using System.Diagnostics.CodeAnalysis;

namespace Synapbiz.Brain.Process;

/// <summary>The kinds of failure a port reports for an effect.</summary>
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "Ordinals start at 1 by design and by project convention (the same documented CA1008 suppression as FactType in Synapbiz.Brain.Contracts): default(ProcessErrorKind) == 0 is deliberately not a valid kind, so an uninitialized value is never read as Transient.")]
public enum ProcessErrorKind
{
    /// <summary>A temporary failure; retried per the step's retry policy.</summary>
    Transient = 1,

    /// <summary>The port asked the caller to slow down; retried per the retry policy, honoring the retry-after hint.</summary>
    RateLimited = 2,

    /// <summary>The port is not available for this practice.</summary>
    NotAvailable = 3,

    /// <summary>The practice is not entitled to the port.</summary>
    NotEntitled = 4,

    /// <summary>A failure that retrying cannot fix.</summary>
    Permanent = 5,

    /// <summary>The outcome of the effect is not known (for example a crash between sending and recording).</summary>
    Unknown = 6,
}

/// <summary>Something that happened outside the interpreter and that <see cref="BrainProcess.Advance"/> applies to an instance.</summary>
[SuppressMessage(
    "Major Code Smell",
    "S1694:An abstract class should have both abstract and concrete methods",
    Justification = "A closed, pattern-matchable hierarchy of sealed records is the point; an interface would lose record value-equality. Same reason as DecisionOutcome in Synapbiz.Brain.Contracts.")]
public abstract record ProcessTrigger;

/// <summary>An effect finished and returned its output.</summary>
/// <param name="EffectId">The id of the effect request.</param>
/// <param name="OutputJson">The output as the text of a JSON object, or <c>"{}"</c>.</param>
public sealed record EffectCompleted(string EffectId, string OutputJson) : ProcessTrigger;

/// <summary>An effect failed.</summary>
/// <param name="EffectId">The id of the effect request.</param>
/// <param name="Kind">The kind of failure.</param>
/// <param name="Code">A stable code of the failure.</param>
/// <param name="RetryAfter">The delay a rate-limited port asked for, if any.</param>
public sealed record EffectFailed(string EffectId, ProcessErrorKind Kind, string Code, TimeSpan? RetryAfter = null) : ProcessTrigger;

/// <summary>A timer requested by the interpreter is due.</summary>
/// <param name="TimerId">The id of the timer request.</param>
public sealed record TimerFired(string TimerId) : ProcessTrigger;

/// <summary>An external event arrived for the instance.</summary>
/// <param name="Name">The event name.</param>
/// <param name="Correlation">The correlation value, if the event has one.</param>
/// <param name="PayloadJson">The payload as the text of a JSON object.</param>
public sealed record EventReceived(string Name, string? Correlation, string PayloadJson) : ProcessTrigger;

/// <summary>A human task was completed.</summary>
/// <param name="TaskId">The id of the task request.</param>
/// <param name="Outcome">The outcome chosen from the task's outcomes.</param>
/// <param name="DataJson">The data of the task as the text of a JSON object.</param>
public sealed record TaskCompleted(string TaskId, string Outcome, string DataJson) : ProcessTrigger;

/// <summary>A child instance completed.</summary>
/// <param name="ChildKey">The key of the child start request.</param>
/// <param name="Result">The result the child ended with.</param>
/// <param name="OutputJson">The output of the child as the text of a JSON object.</param>
public sealed record ChildCompleted(string ChildKey, string Result, string OutputJson) : ProcessTrigger;

/// <summary>A child instance failed.</summary>
/// <param name="ChildKey">The key of the child start request.</param>
/// <param name="Code">A stable code of the failure.</param>
public sealed record ChildFailed(string ChildKey, string Code) : ProcessTrigger;

/// <summary>An operator cancels the instance.</summary>
/// <param name="Reason">Why the instance is cancelled.</param>
public sealed record CancelRequested(string Reason) : ProcessTrigger;

/// <summary>An operator resumes a suspended instance.</summary>
public sealed record ResumeRequested : ProcessTrigger;

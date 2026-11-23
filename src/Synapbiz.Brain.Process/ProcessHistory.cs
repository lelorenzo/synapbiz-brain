namespace Synapbiz.Brain.Process;

/// <summary>The kinds of entries of the history of an instance.</summary>
public enum ProcessHistoryKind
{
    /// <summary>The instance was started.</summary>
    Started,

    /// <summary>A step was entered.</summary>
    StepEntered,

    /// <summary>An effect was requested.</summary>
    EffectRequested,

    /// <summary>An effect completed.</summary>
    EffectCompleted,

    /// <summary>An effect failed or an ai output was invalid.</summary>
    EffectFailed,

    /// <summary>A retry was scheduled.</summary>
    RetryScheduled,

    /// <summary>A rule was evaluated.</summary>
    Decided,

    /// <summary>A human task was requested.</summary>
    TaskRequested,

    /// <summary>A human task was completed.</summary>
    TaskCompleted,

    /// <summary>A wait started.</summary>
    WaitStarted,

    /// <summary>An event was consumed by a wait.</summary>
    EventConsumed,

    /// <summary>An event was buffered in the inbox.</summary>
    EventBuffered,

    /// <summary>A timer step started its timer.</summary>
    TimerStarted,

    /// <summary>A timer step's timer fired.</summary>
    TimerFired,

    /// <summary>A task or a wait timed out.</summary>
    TimedOut,

    /// <summary>A loop started an iteration.</summary>
    LoopIteration,

    /// <summary>A loop ran out of iterations.</summary>
    LoopExhausted,

    /// <summary>A child instance was started.</summary>
    ChildStarted,

    /// <summary>A child instance completed.</summary>
    ChildCompleted,

    /// <summary>A child instance failed.</summary>
    ChildFailed,

    /// <summary>The instance was suspended.</summary>
    Suspended,

    /// <summary>The instance was resumed.</summary>
    Resumed,

    /// <summary>The instance was cancelled.</summary>
    Cancelled,

    /// <summary>The instance completed.</summary>
    Completed,

    /// <summary>An action step with a delivery window waits for the window to open.</summary>
    WindowWaiting,

    /// <summary>The delivery window of an action step opened and the effect was requested.</summary>
    WindowOpened,

    /// <summary>A <c>poll</c> step ran out of time before its condition held.</summary>
    PollExpired,

    /// <summary>An event named by the <c>interruptBy</c> of an action ended the wait for its delivery window; the step went to the member <c>to</c>.</summary>
    WindowInterrupted,
}

/// <summary>One entry of the append-only history of an instance.</summary>
/// <param name="Sequence">The sequence number, increasing by one from the previous entry.</param>
/// <param name="At">The time of the clock the entry was written with.</param>
/// <param name="Step">The step the entry belongs to; null for entries outside a step.</param>
/// <param name="Entry">The value of <see cref="ProcessState.StepCount"/> when the step was entered.</param>
/// <param name="Attempt">The attempt of the step.</param>
/// <param name="Kind">The kind of entry.</param>
/// <param name="Code">A stable code, for failures and suspensions.</param>
/// <param name="DataJson">The smallest JSON object that explains the entry, or null.</param>
public sealed record ProcessHistoryEntry(
    long Sequence,
    DateTimeOffset At,
    string? Step,
    long Entry,
    int Attempt,
    ProcessHistoryKind Kind,
    string? Code,
    string? DataJson);

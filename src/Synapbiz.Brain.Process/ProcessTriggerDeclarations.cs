namespace Synapbiz.Brain.Process;

/// <summary>What a schedule trigger does when occurrences were missed because the host was down or late.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "Ordinals start at 1 by design and by project convention (the same documented CA1008 suppression as FactType in Synapbiz.Brain.Contracts): default(ProcessMisfire) == 0 is deliberately not a valid policy.")]
public enum ProcessMisfire
{
    /// <summary>Drop every missed occurrence that is not on time; fire the last one only if it is on time.</summary>
    Skip = 1,

    /// <summary>Drop every missed occurrence but the last, and fire the last one whether or not it is on time.</summary>
    FireOnce = 2,
}

/// <summary>
/// A trigger declared in the <c>triggers</c> member of a process document. The interpreter does nothing with it at run time:
/// the library exposes it (<see cref="BrainProcess.Triggers"/>) and validates it, and the host acts on it.
/// </summary>
/// <param name="Input">The mapping of the trigger: an <c>input: true</c> variable to a payload member (event trigger) or to <c>scheduledFor</c> or <c>scheduledDate</c> (schedule trigger), in ordinal order of the variable.</param>
public abstract record ProcessTriggerDeclaration(IReadOnlyDictionary<string, string> Input)
{
    /// <summary>The kind of the trigger as it is written in the document: <c>event</c> or <c>schedule</c>.</summary>
    public abstract string Kind { get; }
}

/// <summary>An event trigger: an event of the given name starts an instance of the process.</summary>
/// <param name="Event">The event name; unique among the event triggers of the document.</param>
/// <param name="BusinessKeyMember">The name of the payload member whose string value is the business key of the instance.</param>
/// <param name="Input">The mapping of an <c>input: true</c> variable to a payload member.</param>
public sealed record EventTriggerDeclaration(string Event, string BusinessKeyMember, IReadOnlyDictionary<string, string> Input)
    : ProcessTriggerDeclaration(Input)
{
    /// <inheritdoc />
    public override string Kind => "event";
}

/// <summary>A schedule trigger: an instance is started at every occurrence of a cron expression.</summary>
/// <param name="Id">The id of the schedule, <c>[a-z][a-z0-9_]{0,31}</c>; unique among the schedule triggers of the document.</param>
/// <param name="Cron">The cron expression, valid for <see cref="CronSchedule.TryParse"/>.</param>
/// <param name="Zone">The IANA id of the time zone of the expression; null when the activation supplies it.</param>
/// <param name="Misfire">What to do with occurrences that were missed.</param>
/// <param name="Input">The mapping of an <c>input: true</c> variable to <c>scheduledFor</c> (an <c>Instant</c> variable) or <c>scheduledDate</c> (a <c>Date</c> variable: the local date of the occurrence in the zone of the schedule).</param>
public sealed record ScheduleTriggerDeclaration(string Id, string Cron, string? Zone, ProcessMisfire Misfire, IReadOnlyDictionary<string, string> Input)
    : ProcessTriggerDeclaration(Input)
{
    /// <inheritdoc />
    public override string Kind => "schedule";
}

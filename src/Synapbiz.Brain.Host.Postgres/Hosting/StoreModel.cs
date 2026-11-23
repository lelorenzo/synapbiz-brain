using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>The stored status of an instance: zero while it is created and not started, then the ordinals of <see cref="ProcessStatus"/>.</summary>
internal static class StoredStatus
{
    internal const int Created = 0;

    internal static ProcessStatus ToPublic(int status) => status == Created ? ProcessStatus.Running : (ProcessStatus)status;

    internal static bool IsFinished(int status) => status is (int)ProcessStatus.Completed or (int)ProcessStatus.Cancelled;
}

/// <summary>The kind of an inbox row.</summary>
internal enum InboxKind
{
    Start = 1,
    Event = 2,
    ChildCompleted = 3,
    ChildFailed = 4,
    Cancel = 5,
    WindowFailed = 6,
}

/// <summary>The status of an inbox row.</summary>
internal enum InboxStatus
{
    Pending = 1,
    Done = 2,
    Rejected = 3,
}

/// <summary>The status of an effect row.</summary>
internal enum EffectStatus
{
    Pending = 1,
    Running = 2,
    Done = 3,
    Discarded = 4,
}

/// <summary>The status of a task row.</summary>
internal enum HumanTaskStatus
{
    Open = 1,
    Completed = 2,
    Cancelled = 3,
}

/// <summary>The tenant and id of an instance: what the store locks.</summary>
internal readonly record struct InstanceKey(string TenantId, string InstanceId);

/// <summary>An instance row as stored; the state is still protected.</summary>
internal sealed record InstanceRecord(
    string TenantId,
    string InstanceId,
    string ProcessId,
    long ProcessVersion,
    string ProcessHash,
    string BusinessKey,
    int Status,
    string? Stage,
    string? CurrentStep,
    string? Result,
    string? ProblemCode,
    string? ProtectedState,
    long Revision,
    string? ParentInstanceId,
    string? ParentChildKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    internal InstanceKey Key => new(TenantId, InstanceId);
}

/// <summary>A new instance row.</summary>
internal sealed record NewInstance(
    string TenantId,
    string InstanceId,
    string ProcessId,
    long ProcessVersion,
    string ProcessHash,
    string BusinessKey,
    string? ParentInstanceId,
    string? ParentChildKey,
    DateTimeOffset Now);

/// <summary>The outcome of inserting an instance row.</summary>
internal sealed record InsertedInstance(bool Inserted, string InstanceId);

/// <summary>The columns an advance rewrites.</summary>
internal sealed record InstanceWrite(
    int Status,
    string? Stage,
    string? CurrentStep,
    string? Result,
    string? ProblemCode,
    string? ProtectedState,
    DateTimeOffset Now);

/// <summary>One history row to insert; the data is already protected.</summary>
internal sealed record HistoryRow(long Sequence, DateTimeOffset At, string? Step, long Entry, int Attempt, ProcessHistoryKind Kind, string? Code, string? ProtectedData);

/// <summary>An inbox row as stored; the payload is still protected.</summary>
internal sealed record InboxRecord(string TenantId, long InboxId, string InstanceId, InboxKind Kind, string ProtectedPayload, InboxStatus Status, string? Code);

/// <summary>An effect row as stored; the input is still protected.</summary>
internal sealed record EffectRecord(
    string TenantId,
    string EffectId,
    string InstanceId,
    string Step,
    string IdempotencyKey,
    ProcessEffectKind Kind,
    string Port,
    string ProtectedInput,
    int Attempt,
    ProcessDelivery Delivery,
    string? Model,
    string? PromptId,
    long? PromptVersion,
    EffectStatus Status,
    DateTimeOffset DueAt,
    DateTimeOffset? LeaseUntil,
    string? LeaseOwner);

/// <summary>A task row as stored; the show text is still protected.</summary>
internal sealed record TaskRecord(
    string TenantId,
    string TaskId,
    string InstanceId,
    string Step,
    string Queue,
    IReadOnlyList<string> Outcomes,
    string ProtectedShow,
    HumanTaskStatus Status,
    DateTimeOffset CreatedAt);

/// <summary>A history row as stored; the data is still protected.</summary>
internal sealed record HistoryRecord(long Sequence, DateTimeOffset At, string? Step, long Entry, int Attempt, ProcessHistoryKind Kind, string? Code, string? ProtectedData);

/// <summary>
/// A reference to a work item found by a scan: the instance to lock and the item to re-validate under that lock. <paramref name="SortAt"/>
/// is the time the scan ordered the item by (lease expiry or due time); null for the inbox, which is ordered by its numeric id. The
/// last item of a page is the keyset cursor of the next page.
/// </summary>
/// <param name="TenantId">The tenant of the item.</param>
/// <param name="InstanceId">The instance to lock; empty for a schedule item, which belongs to no instance.</param>
/// <param name="ItemId">The id of the item: effect id, inbox id, timer id or, for a schedule, the schedule id.</param>
/// <param name="SortAt">The time the scan ordered the item by; null for the inbox.</param>
/// <param name="ProcessId">Only for a schedule item: the process id of the schedule.</param>
internal sealed record WorkRef(string TenantId, string InstanceId, string ItemId, DateTimeOffset? SortAt = null, string? ProcessId = null);

/// <summary>An instance that has a wait for an event; <paramref name="Exact"/> is false when the correlation matched only without regard to case.</summary>
internal sealed record WaitCandidate(string InstanceId, bool Exact);

/// <summary>The kind of a work item the pump scans for.</summary>
internal enum WorkKind
{
    ExpiredLease,
    Inbox,
    DueTimer,
    DueEffect,
    DueSchedule,
}

/// <summary>The tenant and process id of an activation: what the store serialises activations on.</summary>
internal readonly record struct ActivationKey(string TenantId, string ProcessId);

/// <summary>The tenant, process id and schedule id of a schedule row: what the store locks to handle a schedule.</summary>
internal readonly record struct ScheduleKey(string TenantId, string ProcessId, string ScheduleId);

/// <summary>An activation row as stored; the input is still protected.</summary>
internal sealed record ActivationRecord(string TenantId, string ProcessId, long ProcessVersion, string ProcessHash, string? Zone, string ProtectedInput, DateTimeOffset ActivatedAt)
{
    internal ActivationKey Key => new(TenantId, ProcessId);
}

/// <summary>An event trigger row: the event that starts instances of the process, the payload member that is the business key and the mapping of payload members to input variables (a JSON object of variable to member).</summary>
internal sealed record EventTriggerRow(string TenantId, string Event, string ProcessId, string BusinessKeyMember, string InputMapJson);

/// <summary>An event trigger row with the activation it belongs to, as publishing an event needs it.</summary>
internal sealed record EventTriggerRecord(string Event, string ProcessId, string BusinessKeyMember, string InputMapJson, long ProcessVersion, string ProcessHash, string ProtectedInput);

/// <summary>A schedule row as stored; <paramref name="Zone"/> is the zone the schedule is evaluated in.</summary>
internal sealed record ScheduleRow(string TenantId, string ProcessId, string ScheduleId, string Cron, string Zone, ProcessMisfire Misfire, DateTimeOffset NextFireAt)
{
    internal ScheduleKey Key => new(TenantId, ProcessId, ScheduleId);
}

/// <summary>A schedule row with the activation it belongs to: the pinned definition and the protected input of the activation.</summary>
internal sealed record ScheduleRecord(ScheduleRow Row, long ProcessVersion, string ProcessHash, string ProtectedInput);

/// <summary>An instance row for a list: no state, no hash, plus the open tasks of the instance and the suspended descendants.</summary>
internal sealed record InstanceSummaryRecord(
    string TenantId,
    string InstanceId,
    string ProcessId,
    long ProcessVersion,
    string BusinessKey,
    int Status,
    string? Stage,
    string? CurrentStep,
    string? Result,
    string? ProblemCode,
    string? ParentInstanceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int OpenTasks,
    int SuspendedDescendants);

/// <summary>The keyset cursor of an instance list: the creation instant and id of the last row of the previous page.</summary>
internal sealed record InstanceCursor(DateTimeOffset CreatedAt, string InstanceId);

/// <summary>The filters of an instance list. <paramref name="Statuses"/> holds public status ordinals; running also matches the stored created status.</summary>
internal sealed record InstanceListFilter(
    string TenantId,
    string? ProcessId,
    IReadOnlyList<int>? Statuses,
    string? Stage,
    bool RootsOnly,
    bool NeedsAttention,
    int Limit,
    InstanceCursor? After);

/// <summary>One group of the instance count; the status is the reported one.</summary>
internal sealed record InstanceCountRecord(string ProcessId, int Status, string? Stage, int Count);

/// <summary>The rows of a case, read together.</summary>
internal sealed record CaseRecord(
    InstanceSummaryRecord Instance,
    IReadOnlyList<InstanceSummaryRecord> Descendants,
    IReadOnlyList<TaskRecord> Tasks,
    DateTimeOffset? NextTimerDue,
    IReadOnlyList<string> Events);

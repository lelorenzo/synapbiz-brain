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

/// <summary>A reference to a work item found by a scan: the instance to lock and the item to re-validate under that lock.</summary>
internal sealed record WorkRef(string TenantId, string InstanceId, string ItemId);

/// <summary>The kind of a work item the pump scans for.</summary>
internal enum WorkKind
{
    ExpiredLease,
    Inbox,
    DueTimer,
    DueEffect,
}

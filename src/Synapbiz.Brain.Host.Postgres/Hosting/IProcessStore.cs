namespace Synapbiz.Brain.Process.Hosting;

/// <summary>
/// Marks the session operations that write the rows of an instance. Each one takes the <see cref="InstanceKey"/> as its first
/// argument and fails with <see cref="InvalidOperationException"/> unless the session holds the instance lock for that key. This
/// is what keeps the lock order uniform: instance row first, everything else after.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class RequiresInstanceLockAttribute : Attribute
{
}

/// <summary>
/// Marks the session operations that write an activation and the event trigger and schedule rows that belong to it. Each one takes
/// the <see cref="ActivationKey"/> as its first argument and fails with <see cref="InvalidOperationException"/> unless the session
/// holds the activation lock for that key, which serialises concurrent activations and deactivations of one process of one tenant.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class RequiresActivationLockAttribute : Attribute
{
}

/// <summary>
/// Marks the session operations that write a schedule row while the pump handles it. Each one takes the <see cref="ScheduleKey"/>
/// as its first argument and fails with <see cref="InvalidOperationException"/> unless the session holds the row lock for that key.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class RequiresScheduleLockAttribute : Attribute
{
}

/// <summary>The whole persistence surface of the host. Nothing outside an implementation of it knows a database API.</summary>
internal interface IProcessStore
{
    /// <summary>Opens a transaction.</summary>
    Task<IProcessSession> BeginAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Scans for work items of one kind, oldest first, without locking anything. One page of at most <paramref name="limit"/> items
    /// that sort strictly after <paramref name="after"/> (the last item of the previous page; null for the first page). The ordering
    /// key is total: lease expiry or due time, then tenant, then id; the inbox is ordered by its id.
    /// </summary>
    Task<IReadOnlyList<WorkRef>> ScanAsync(WorkKind kind, DateTimeOffset now, int limit, WorkRef? after, CancellationToken cancellationToken);

    /// <summary>Reads an instance row.</summary>
    Task<InstanceRecord?> GetInstanceAsync(string tenantId, string instanceId, CancellationToken cancellationToken);

    /// <summary>Reads an instance row by process id and business key.</summary>
    Task<InstanceRecord?> FindInstanceAsync(string tenantId, string processId, string businessKey, CancellationToken cancellationToken);

    /// <summary>Reads the history rows of an instance.</summary>
    Task<IReadOnlyList<HistoryRecord>> GetHistoryAsync(string tenantId, string instanceId, CancellationToken cancellationToken);

    /// <summary>Reads the open tasks of a queue.</summary>
    Task<IReadOnlyList<TaskRecord>> ListOpenTasksAsync(string tenantId, string queue, CancellationToken cancellationToken);

    /// <summary>Finds the instance a task belongs to.</summary>
    Task<string?> FindTaskInstanceAsync(string tenantId, string taskId, CancellationToken cancellationToken);
}

/// <summary>One transaction. Disposing it without <see cref="CommitAsync"/> rolls everything back.</summary>
internal interface IProcessSession : IAsyncDisposable
{
    /// <summary>Selects the instance row <c>for update</c>; with <paramref name="skipLocked"/> a row locked by someone else gives null.</summary>
    Task<InstanceRecord?> LockInstanceAsync(InstanceKey key, bool skipLocked, CancellationToken cancellationToken);

    /// <summary>Inserts an instance row in status Created, or reports the id of the one that holds the business key. The new row is locked by this transaction.</summary>
    Task<InsertedInstance> InsertInstanceAsync(NewInstance row, CancellationToken cancellationToken);

    /// <summary>Rewrites the state columns of an instance and bumps its revision.</summary>
    [RequiresInstanceLock]
    Task SaveInstanceAsync(InstanceKey key, InstanceWrite write, CancellationToken cancellationToken);

    /// <summary>Appends history rows.</summary>
    [RequiresInstanceLock]
    Task AppendHistoryAsync(InstanceKey key, IReadOnlyList<HistoryRow> rows, CancellationToken cancellationToken);

    /// <summary>Deletes a timer row; false when it was not there.</summary>
    [RequiresInstanceLock]
    Task<bool> DeleteTimerAsync(InstanceKey key, string timerId, CancellationToken cancellationToken);

    /// <summary>Inserts a timer row.</summary>
    [RequiresInstanceLock]
    Task InsertTimerAsync(InstanceKey key, string timerId, DateTimeOffset dueAt, CancellationToken cancellationToken);

    /// <summary>Deletes a wait row.</summary>
    [RequiresInstanceLock]
    Task DeleteWaitAsync(InstanceKey key, string waitId, CancellationToken cancellationToken);

    /// <summary>Inserts a wait row.</summary>
    [RequiresInstanceLock]
    Task InsertWaitAsync(InstanceKey key, string waitId, string eventName, string? correlation, CancellationToken cancellationToken);

    /// <summary>Inserts an open task row.</summary>
    [RequiresInstanceLock]
    Task InsertTaskAsync(InstanceKey key, TaskRecord task, CancellationToken cancellationToken);

    /// <summary>Reads a task row of the instance.</summary>
    Task<TaskRecord?> GetTaskAsync(InstanceKey key, string taskId, CancellationToken cancellationToken);

    /// <summary>Sets an open task to Completed or Cancelled; false when it was not open.</summary>
    [RequiresInstanceLock]
    Task<bool> CloseTaskAsync(InstanceKey key, string taskId, HumanTaskStatus status, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Inserts a pending effect row.</summary>
    [RequiresInstanceLock]
    Task InsertEffectAsync(InstanceKey key, EffectRecord effect, CancellationToken cancellationToken);

    /// <summary>Reads an effect row of the instance.</summary>
    Task<EffectRecord?> GetEffectAsync(InstanceKey key, string effectId, CancellationToken cancellationToken);

    /// <summary>Moves a pending effect to Running with a lease.</summary>
    [RequiresInstanceLock]
    Task ClaimEffectAsync(InstanceKey key, string effectId, DateTimeOffset leaseUntil, string leaseOwner, CancellationToken cancellationToken);

    /// <summary>Moves a running effect back to Pending, due at the given time, without a lease.</summary>
    [RequiresInstanceLock]
    Task RequeueEffectAsync(InstanceKey key, string effectId, DateTimeOffset dueAt, CancellationToken cancellationToken);

    /// <summary>Sets the final status (Done or Discarded) of an effect and clears its lease.</summary>
    [RequiresInstanceLock]
    Task FinishEffectAsync(InstanceKey key, string effectId, EffectStatus status, CancellationToken cancellationToken);

    /// <summary>Deletes the waits and timers of the instance, cancels its open tasks and discards its pending effects.</summary>
    [RequiresInstanceLock]
    Task ClearWorkAsync(InstanceKey key, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Discards the pending effects of the instance.</summary>
    [RequiresInstanceLock]
    Task DiscardPendingEffectsAsync(InstanceKey key, CancellationToken cancellationToken);

    /// <summary>Reads an inbox row of the instance.</summary>
    Task<InboxRecord?> GetInboxAsync(InstanceKey key, long inboxId, CancellationToken cancellationToken);

    /// <summary>The lowest pending inbox id of the instance, if any.</summary>
    Task<long?> FirstPendingInboxAsync(InstanceKey key, CancellationToken cancellationToken);

    /// <summary>Sets the final status and code of an inbox row.</summary>
    [RequiresInstanceLock]
    Task CompleteInboxAsync(InstanceKey key, long inboxId, InboxStatus status, string? code, CancellationToken cancellationToken);

    /// <summary>
    /// Appends a pending inbox row for an instance. A pure insert: it does not need the lock of the target, because it can
    /// neither block nor be blocked by the lock (a child writes to its parent's inbox while holding only its own lock).
    /// </summary>
    Task AppendInboxAsync(InstanceKey target, InboxKind kind, string protectedPayload, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The ids of the children of an instance that are not completed or cancelled.</summary>
    Task<IReadOnlyList<string>> ListLiveChildrenAsync(InstanceKey parent, CancellationToken cancellationToken);

    /// <summary>Records an event id; false when it was already recorded.</summary>
    Task<bool> TryInsertEventAsync(string tenantId, string eventId, string name, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// The instances that wait for an event, in id order: those whose wait has no correlation or a correlation equal to the given
    /// one without regard to case. Each is marked exact or not, so that the caller can ask the interpreter about the ones that matched
    /// only by case.
    /// </summary>
    Task<IReadOnlyList<WaitCandidate>> FindWaitingInstancesAsync(string tenantId, string eventName, string? correlation, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the transaction-scoped lock that serialises activations and deactivations of one process of one tenant. It touches no
    /// instance and no schedule row, so it cannot take part in a cycle with the instance lock order.
    /// </summary>
    Task LockActivationAsync(ActivationKey key, CancellationToken cancellationToken);

    /// <summary>Reads the activation of a process, if any.</summary>
    Task<ActivationRecord?> GetActivationAsync(ActivationKey key, CancellationToken cancellationToken);

    /// <summary>Inserts the activation row or replaces it.</summary>
    [RequiresActivationLock]
    Task SaveActivationAsync(ActivationKey key, ActivationRecord activation, CancellationToken cancellationToken);

    /// <summary>Deletes the activation row; false when there was none.</summary>
    [RequiresActivationLock]
    Task<bool> DeleteActivationAsync(ActivationKey key, CancellationToken cancellationToken);

    /// <summary>Replaces all the event trigger rows of the activation.</summary>
    [RequiresActivationLock]
    Task ReplaceEventTriggersAsync(ActivationKey key, IReadOnlyList<EventTriggerRow> rows, CancellationToken cancellationToken);

    /// <summary>Reads the schedule rows of the activation and locks them (waiting for a pump that is handling one), so that what is read is what a pump left.</summary>
    [RequiresActivationLock]
    Task<IReadOnlyList<ScheduleRow>> GetSchedulesForUpdateAsync(ActivationKey key, CancellationToken cancellationToken);

    /// <summary>Inserts a schedule row or replaces its definition and next fire time.</summary>
    [RequiresActivationLock]
    Task UpsertScheduleAsync(ActivationKey key, ScheduleRow row, CancellationToken cancellationToken);

    /// <summary>Deletes the schedule rows of the activation whose id is not in <paramref name="keep"/>.</summary>
    [RequiresActivationLock]
    Task DeleteSchedulesExceptAsync(ActivationKey key, IReadOnlyCollection<string> keep, CancellationToken cancellationToken);

    /// <summary>The event triggers of a tenant for an event name, with the activation each belongs to, in process id order.</summary>
    Task<IReadOnlyList<EventTriggerRecord>> FindEventTriggersAsync(string tenantId, string eventName, CancellationToken cancellationToken);

    /// <summary>
    /// Selects a schedule row <c>for update</c> together with the activation it belongs to; with <paramref name="skipLocked"/> a row
    /// locked by someone else gives null, and so does a row that is gone.
    /// </summary>
    Task<ScheduleRecord?> LockScheduleAsync(ScheduleKey key, bool skipLocked, CancellationToken cancellationToken);

    /// <summary>Sets the next fire time of a schedule row.</summary>
    [RequiresScheduleLock]
    Task SaveScheduleAsync(ScheduleKey key, DateTimeOffset nextFireAt, CancellationToken cancellationToken);

    /// <summary>Commits the transaction.</summary>
    Task CommitAsync(CancellationToken cancellationToken);
}

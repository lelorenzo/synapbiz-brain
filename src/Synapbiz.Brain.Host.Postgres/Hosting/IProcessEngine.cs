using System.Diagnostics.CodeAnalysis;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>
/// The commands and queries a consumer uses to run and look at durable process instances. Every method is scoped to one tenant:
/// a command or a query given the id of tenant A never reads or changes a row of tenant B.
/// </summary>
public interface IProcessEngine
{
    /// <summary>
    /// Starts an instance of the latest version of a process. The business key is unique per tenant and process: starting twice
    /// with the same key creates one instance and returns its id both times.
    /// </summary>
    /// <param name="request">The tenant, the process id, the business key and the input object.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The instance id and whether this call created it.</returns>
    /// <exception cref="InvalidOperationException">The catalog has no definition of the process (<c>process.definition.unavailable</c>).</exception>
    /// <exception cref="ArgumentException">An argument is invalid, or the interpreter rejected the input; the message carries the code.</exception>
    Task<StartResult> StartAsync(StartRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an external event. The event is de-duplicated by its id, routed to the instances that wait for it and applied
    /// later by the pump. Every event trigger of the tenant for that event name (see <see cref="ActivateAsync"/>) also queues the
    /// start of an instance, whose business key is read from the payload; an instance that already has that business key is left
    /// alone.
    /// </summary>
    /// <param name="processEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Whether the event was queued, was a duplicate or had no target, and for how many instances it was queued or started.</returns>
    Task<PublishResult> PublishEventAsync(ProcessEvent processEvent, CancellationToken cancellationToken = default);

    /// <summary>Completes an open human task with one of its outcomes.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="taskId">The id of the task.</param>
    /// <param name="outcome">The outcome the person chose.</param>
    /// <param name="dataJson">The data of the task as the text of a JSON object.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What the interpreter did with the trigger and its code.</returns>
    Task<CommandResult> CompleteTaskAsync(string tenantId, string taskId, string outcome, string dataJson, CancellationToken cancellationToken = default);

    /// <summary>Cancels an instance.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="instanceId">The instance to cancel.</param>
    /// <param name="reason">Why the instance is cancelled.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What the interpreter did with the trigger and its code.</returns>
    Task<CommandResult> CancelAsync(string tenantId, string instanceId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Resumes a suspended instance: its current step runs again.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="instanceId">The instance to resume.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What the interpreter did with the trigger and its code.</returns>
    Task<CommandResult> ResumeAsync(string tenantId, string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates the triggers of the latest version of a process for a tenant. Until then the <c>triggers</c> of a definition do
    /// nothing. One transaction writes the activation (the pinned version and hash, the zone and the protected input), its event
    /// trigger rows and its schedule rows, the first fire time of a schedule being its first occurrence after now. Activating the
    /// same version, zone and input again changes nothing and keeps every fire time; a new version, zone or input replaces the rows,
    /// and a schedule whose cron expression and zone did not change keeps its fire time. Instances that are running are not touched.
    /// </summary>
    /// <param name="activation">The tenant, the process, the zone for schedules that declare none, and the input every started instance begins with.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The version that is pinned, and whether this call changed anything.</returns>
    /// <exception cref="InvalidOperationException">The catalog has no definition of the process (<c>process.definition.unavailable</c>).</exception>
    /// <exception cref="ArgumentException">An argument is invalid; the input names a variable that is not an <c>input: true</c> variable or has a value of the wrong type; or a schedule has no zone, or a zone the host does not know (the message names the schedule id).</exception>
    Task<ActivationResult> ActivateAsync(ProcessActivation activation, CancellationToken cancellationToken = default);

    /// <summary>Removes the activation of a process, with its event trigger and schedule rows. Running instances are not touched.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="processId">The process id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>True when there was an activation to remove.</returns>
    Task<bool> DeactivateAsync(string tenantId, string processId, CancellationToken cancellationToken = default);

    /// <summary>Reads an instance by id.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The instance, or null when the tenant has no such instance.</returns>
    Task<ProcessInstanceInfo?> GetInstanceAsync(string tenantId, string instanceId, CancellationToken cancellationToken = default);

    /// <summary>Reads an instance by process id and business key.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="processId">The process id.</param>
    /// <param name="businessKey">The business key.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The instance, or null when the tenant has no such instance.</returns>
    Task<ProcessInstanceInfo?> FindInstanceAsync(string tenantId, string processId, string businessKey, CancellationToken cancellationToken = default);

    /// <summary>Reads the history of an instance in sequence order.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The entries; empty when the tenant has no such instance.</returns>
    Task<IReadOnlyList<ProcessHistoryEntry>> GetHistoryAsync(string tenantId, string instanceId, CancellationToken cancellationToken = default);

    /// <summary>Lists the open human tasks of a queue.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="queue">The queue name.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The open tasks, oldest first.</returns>
    Task<IReadOnlyList<ProcessTaskInfo>> ListOpenTasksAsync(string tenantId, string queue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists instances for a monitoring screen, newest first, one page at a time. Read-only: it never reads or decrypts the state of an
    /// instance. An instance in the internal <c>Created</c> status is reported, and filtered on, as <see cref="ProcessStatus.Running"/>.
    /// </summary>
    /// <param name="query">The tenant, the filters, the page size and the cursor.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page; <see cref="ProcessInstancePage.Next"/> is null on the last page.</returns>
    /// <exception cref="ArgumentException">The tenant or process id is invalid, or the cursor does not parse.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The limit is not between 1 and 500.</exception>
    Task<ProcessInstancePage> ListInstancesAsync(ProcessInstanceQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the instances of a tenant per process, reported status and stage. <c>Created</c> and <c>Running</c> rows are one group;
    /// groups with no rows are absent.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="processId">The process to count; null counts every process.</param>
    /// <param name="rootsOnly">True to count only instances that have no parent.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One entry per group, ordered by process id, status and stage, a null stage first.</returns>
    Task<IReadOnlyList<ProcessInstanceCount>> CountInstancesAsync(string tenantId, string? processId, bool rootsOnly, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a case: an instance with its descendants (the instances reachable through the parent link, to a depth of 16), their open
    /// tasks, the next timer due and the events they wait for. The parts are read in one repeatable-read, read-only transaction, so they
    /// agree with each other.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="instanceId">The instance, normally a root.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The case, or null when the tenant has no such instance.</returns>
    Task<ProcessCaseView?> GetCaseAsync(string tenantId, string instanceId, CancellationToken cancellationToken = default);

    /// <summary>Lists the direct children of an instance.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="instanceId">The parent instance.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The children ordered by creation time and then id, oldest first; empty when the tenant has no such instance.</returns>
    Task<IReadOnlyList<ProcessInstanceSummary>> ListChildrenAsync(string tenantId, string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a case completely: a root instance and all its descendants, at any depth, with their history, timers, waits, tasks,
    /// effects and inbox rows, in one transaction (all or nothing). The host keeps no copy, tombstone or archive; afterwards the
    /// business key is free and <see cref="StartAsync"/> with it creates a new instance. Nothing is cancelled through the interpreter
    /// first, no port is called and nobody is notified. A port call that a worker had already started cannot be recalled: when it
    /// comes back its result is dropped and <c>process.effect.orphaned</c> is logged. The call never waits for a row lock while it
    /// holds another: it locks the whole tree at once without waiting, and when another transaction holds a row of it gives
    /// everything back and tries again after a short pause, for at most <see cref="ProcessHostOptions.PurgeLockTimeout"/>.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="instanceId">The root instance to purge.</param>
    /// <param name="mode"><see cref="PurgeMode.FinishedOnly"/> refuses a root that is not completed or cancelled; <see cref="PurgeMode.Force"/> removes the case whatever its statuses.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Purged with the number of rows deleted per table; not found (zero counts, also for the id of another tenant) so that a retry after a crash is safe; or refused with <c>process.purge.not_root</c> (a child is purged with its root), <c>process.purge.not_finished</c>, or <c>process.purge.busy</c> (another transaction held a row of the tree for the whole <see cref="ProcessHostOptions.PurgeLockTimeout"/>; nothing changed and the call can be repeated).</returns>
    /// <exception cref="ArgumentException">An id is invalid (empty, over 200 characters or holding a control character).</exception>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not a defined value.</exception>
    Task<PurgeResult> PurgeInstanceAsync(string tenantId, string instanceId, PurgeMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// A safety net for a retention rule: purges up to <paramref name="limit"/> trees of the tenant whose root is completed or
    /// cancelled and whose root update time is earlier than <paramref name="before"/> (not equal to it), oldest first, one
    /// transaction per tree, each tried once and never waited for. A tree that another transaction holds, wholly or in part, is
    /// skipped in this call and found by a later one.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="before">The cut-off instant, exclusive; the consumer owns the rule and the date.</param>
    /// <param name="limit">The most trees to purge in this call, from 1 to 500.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The trees purged, the sum of their counts, and whether at least one further tree qualified when the call ended (a tree that was skipped because it was locked counts).</returns>
    /// <exception cref="ArgumentException">The tenant id is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The limit is not between 1 and 500.</exception>
    Task<PurgeBatchResult> PurgeFinishedBeforeAsync(string tenantId, DateTimeOffset before, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes up to <paramref name="limit"/> event ids (the rows that de-duplicate <see cref="PublishEventAsync"/>) of the tenant that
    /// were received before an instant, oldest first. An event id that was purged is unknown again: publishing it is treated as a new
    /// event, so the de-duplication window is what the consumer keeps.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="before">The cut-off instant, exclusive.</param>
    /// <param name="limit">The most rows to delete in this call, from 1 to 5000.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The number of rows deleted and whether more rows qualified when the call ended.</returns>
    /// <exception cref="ArgumentException">The tenant id is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The limit is not between 1 and 5000.</exception>
    Task<PurgeEventsResult> PurgeEventsBeforeAsync(string tenantId, DateTimeOffset before, int limit, CancellationToken cancellationToken = default);
}

/// <summary>A request to start an instance.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="ProcessId">The id of the process to start; its latest version is used.</param>
/// <param name="BusinessKey">The key that identifies the case; unique per tenant and process.</param>
/// <param name="InputJson">The input as the text of a JSON object.</param>
public sealed record StartRequest(string TenantId, string ProcessId, string BusinessKey, string InputJson);

/// <summary>The result of starting an instance.</summary>
/// <param name="InstanceId">The id of the instance.</param>
/// <param name="Created">False when an instance with that business key already existed.</param>
public sealed record StartResult(string InstanceId, bool Created);

/// <summary>The activation of the triggers of a process for a tenant.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="ProcessId">The id of the process; its latest version is pinned.</param>
/// <param name="Zone">The IANA id of the time zone of the schedules that declare none; null when every schedule declares its own.</param>
/// <param name="InputJson">The input every instance started by a trigger begins with, as the text of a JSON object; the members the trigger maps (payload members, <c>scheduledFor</c>, <c>scheduledDate</c>) are laid over it.</param>
public sealed record ProcessActivation(string TenantId, string ProcessId, string? Zone = null, string InputJson = "{}");

/// <summary>The result of activating a process.</summary>
/// <param name="ProcessVersion">The version of the definition the activation is pinned to.</param>
/// <param name="Changed">False when the same activation already existed and nothing was written.</param>
public sealed record ActivationResult(long ProcessVersion, bool Changed);

/// <summary>An external event for the instances that wait for it.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="EventId">The id that de-duplicates the event.</param>
/// <param name="Name">The event name.</param>
/// <param name="Correlation">The correlation value, if the event has one.</param>
/// <param name="PayloadJson">The payload as the text of a JSON object.</param>
/// <param name="InstanceId">The instance the event is for; null routes it to every instance that waits for it.</param>
public sealed record ProcessEvent(string TenantId, string EventId, string Name, string? Correlation, string PayloadJson, string? InstanceId = null);

/// <summary>What publishing an event did.</summary>
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "Ordinals start at 1 by design and by project convention (the same documented CA1008 suppression as FactType in Synapbiz.Brain.Contracts): default(PublishStatus) == 0 is deliberately not a valid status, so an uninitialized value is never read as a queued event.")]
public enum PublishStatus
{
    /// <summary>The event was queued for at least one instance, or started at least one instance through an event trigger.</summary>
    Queued = 1,

    /// <summary>An event with the same id was published before; nothing was written.</summary>
    Duplicate = 2,

    /// <summary>No instance waits for the event, or the named instance does not exist.</summary>
    NoTarget = 3,
}

/// <summary>The result of publishing an event.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Instances">The number of instances the event was queued for, plus the number of instances an event trigger started.</param>
public sealed record PublishResult(PublishStatus Status, int Instances);

/// <summary>The result of a command on an instance or a task.</summary>
/// <param name="Disposition">What the interpreter did with the trigger.</param>
/// <param name="Code">The code of an ignored or rejected trigger; null when it was applied.</param>
public sealed record CommandResult(AdvanceDisposition Disposition, string? Code);

/// <summary>An instance as the engine reports it.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="InstanceId">The instance id.</param>
/// <param name="ProcessId">The id of the process definition.</param>
/// <param name="ProcessVersion">The version the instance is pinned to.</param>
/// <param name="BusinessKey">The business key.</param>
/// <param name="Status">
/// The status. An instance that was created but not yet started by the pump (a child instance waiting for its start) reports
/// <see cref="ProcessStatus.Running"/>.
/// </param>
/// <param name="Stage">The current stage, if any.</param>
/// <param name="CurrentStep">The step the instance is on; null when it has finished.</param>
/// <param name="Result">The result of the end step; null while the instance has not completed.</param>
/// <param name="Problem">Why the instance is suspended, or the code that cancelled it at start.</param>
/// <param name="ParentInstanceId">The parent instance of a child process.</param>
/// <param name="CreatedAt">When the instance was created.</param>
/// <param name="UpdatedAt">When the instance was last written.</param>
/// <param name="State">The full state; null until the instance has started.</param>
public sealed record ProcessInstanceInfo(
    string TenantId,
    string InstanceId,
    string ProcessId,
    long ProcessVersion,
    string BusinessKey,
    ProcessStatus Status,
    string? Stage,
    string? CurrentStep,
    string? Result,
    ProcessProblem? Problem,
    string? ParentInstanceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ProcessState? State);

/// <summary>An open human task.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="TaskId">The task id.</param>
/// <param name="InstanceId">The instance that waits for the task.</param>
/// <param name="Step">The step that created the task.</param>
/// <param name="Queue">The queue of the task.</param>
/// <param name="Outcomes">The outcomes the task can end with.</param>
/// <param name="ShowJson">The values to show, as the text of a JSON object.</param>
/// <param name="CreatedAt">When the task was created.</param>
public sealed record ProcessTaskInfo(
    string TenantId,
    string TaskId,
    string InstanceId,
    string Step,
    string Queue,
    IReadOnlyList<string> Outcomes,
    string ShowJson,
    DateTimeOffset CreatedAt);

/// <summary>An instance as a list shows it: <see cref="ProcessInstanceInfo"/> without the state, plus two counts.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="InstanceId">The instance id.</param>
/// <param name="ProcessId">The id of the process definition.</param>
/// <param name="ProcessVersion">The version the instance is pinned to.</param>
/// <param name="BusinessKey">The business key.</param>
/// <param name="Status">The status; a created instance that is not yet started reports <see cref="ProcessStatus.Running"/>.</param>
/// <param name="Stage">The current stage, if any.</param>
/// <param name="CurrentStep">The step the instance is on; null when it has finished.</param>
/// <param name="Result">The result of the end step; null while the instance has not completed.</param>
/// <param name="ProblemCode">The stored code of the problem that suspended the instance; null when there is none.</param>
/// <param name="ParentInstanceId">The parent instance of a child process.</param>
/// <param name="CreatedAt">When the instance was created.</param>
/// <param name="UpdatedAt">When the instance was last written.</param>
/// <param name="OpenTasks">The number of open human tasks of the instance itself.</param>
/// <param name="SuspendedDescendants">The number of descendants whose status is suspended; the instance itself is not counted.</param>
public sealed record ProcessInstanceSummary(
    string TenantId,
    string InstanceId,
    string ProcessId,
    long ProcessVersion,
    string BusinessKey,
    ProcessStatus Status,
    string? Stage,
    string? CurrentStep,
    string? Result,
    string? ProblemCode,
    string? ParentInstanceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int OpenTasks,
    int SuspendedDescendants);

/// <summary>A query for a page of instances.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="ProcessId">Only instances of this process; null means every process.</param>
/// <param name="Statuses">Only instances in one of these statuses; null or empty means every status. <see cref="ProcessStatus.Running"/> also matches created instances.</param>
/// <param name="Stage">Only instances whose own stage is exactly this (case-sensitive); null means any.</param>
/// <param name="RootsOnly">True to keep only instances that have no parent.</param>
/// <param name="NeedsAttention">True to keep only instances that are suspended, have a suspended descendant, or have an open task of their own or of a descendant.</param>
/// <param name="Limit">The page size, from 1 to 500.</param>
/// <param name="After">The <see cref="ProcessInstancePage.Next"/> of the previous page; null for the first page.</param>
public sealed record ProcessInstanceQuery(
    string TenantId,
    string? ProcessId = null,
    IReadOnlyCollection<ProcessStatus>? Statuses = null,
    string? Stage = null,
    bool RootsOnly = true,
    bool NeedsAttention = false,
    int Limit = 100,
    string? After = null);

/// <summary>A page of instances, newest first.</summary>
/// <param name="Items">The instances of the page.</param>
/// <param name="Next">The opaque cursor of the next page; null on the last page.</param>
public sealed record ProcessInstancePage(IReadOnlyList<ProcessInstanceSummary> Items, string? Next);

/// <summary>The number of instances of one process in one status and stage.</summary>
/// <param name="ProcessId">The process id.</param>
/// <param name="Status">The reported status; created and running instances are one group.</param>
/// <param name="Stage">The stage; null for instances that have none.</param>
/// <param name="Count">The number of instances in the group.</param>
public sealed record ProcessInstanceCount(string ProcessId, ProcessStatus Status, string? Stage, int Count);

/// <summary>A case: an instance with its descendants and what they wait for.</summary>
/// <param name="Instance">The summary of the instance.</param>
/// <param name="Descendants">The summaries of its descendants, ordered by creation time and then id; <see cref="ProcessInstanceSummary.ParentInstanceId"/> rebuilds the tree.</param>
/// <param name="OpenTasks">The open tasks of the instance and of its descendants, oldest first and then by task id.</param>
/// <param name="NextTimerDue">The earliest due time among the timers of the instance and of its descendants; null when there is none.</param>
/// <param name="WaitingForEvents">The distinct names, ordered, of the events the instance and its descendants wait for; never a correlation value.</param>
public sealed record ProcessCaseView(
    ProcessInstanceSummary Instance,
    IReadOnlyList<ProcessInstanceSummary> Descendants,
    IReadOnlyList<ProcessTaskInfo> OpenTasks,
    DateTimeOffset? NextTimerDue,
    IReadOnlyList<string> WaitingForEvents);

/// <summary>Which trees <see cref="IProcessEngine.PurgeInstanceAsync"/> may remove.</summary>
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "Ordinals start at 1 by design and by project convention (the same documented CA1008 suppression as PublishStatus): default(PurgeMode) == 0 is deliberately not a valid mode, so an uninitialized value can never be read as the destructive mode.")]
public enum PurgeMode
{
    /// <summary>Only a tree whose root is completed or cancelled; a root in any other status is refused. Only the root is looked at.</summary>
    FinishedOnly = 1,

    /// <summary>The tree is removed whatever the statuses of its instances (the "erase now" path).</summary>
    Force = 2,
}

/// <summary>What <see cref="IProcessEngine.PurgeInstanceAsync"/> did.</summary>
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "Ordinals start at 1 by design and by project convention (the same documented CA1008 suppression as PublishStatus): default(PurgeStatus) == 0 is deliberately not a valid status.")]
public enum PurgeStatus
{
    /// <summary>The tree was removed.</summary>
    Purged = 1,

    /// <summary>The tenant has no such instance; nothing changed.</summary>
    NotFound = 2,

    /// <summary>The instance was not purged; <see cref="PurgeResult.Code"/> says why.</summary>
    Refused = 3,
}

/// <summary>The number of rows a purge deleted, per table.</summary>
/// <param name="Instances">Rows of <c>process_instance</c>: the root and its descendants.</param>
/// <param name="History">Rows of <c>process_history</c>.</param>
/// <param name="Timers">Rows of <c>process_timer</c>.</param>
/// <param name="Waits">Rows of <c>process_wait</c>.</param>
/// <param name="Tasks">Rows of <c>process_task</c>, open or closed.</param>
/// <param name="Effects">Rows of <c>process_effect</c>, in any status.</param>
/// <param name="Inbox">Rows of <c>process_inbox</c>, in any status.</param>
public sealed record PurgeCounts(int Instances, int History, int Timers, int Waits, int Tasks, int Effects, int Inbox)
{
    /// <summary>The counts of a purge that deleted nothing.</summary>
    public static PurgeCounts None { get; } = new(0, 0, 0, 0, 0, 0, 0);

    internal PurgeCounts Plus(PurgeCounts other) => new(
        Instances + other.Instances,
        History + other.History,
        Timers + other.Timers,
        Waits + other.Waits,
        Tasks + other.Tasks,
        Effects + other.Effects,
        Inbox + other.Inbox);
}

/// <summary>The result of purging one tree.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Counts">The rows deleted per table; all zero unless the status is <see cref="PurgeStatus.Purged"/>.</param>
/// <param name="Code">The reason of a refusal (<c>process.purge.not_root</c>, <c>process.purge.not_finished</c> or <c>process.purge.busy</c>); null otherwise.</param>
public sealed record PurgeResult(PurgeStatus Status, PurgeCounts Counts, string? Code = null);

/// <summary>The result of <see cref="IProcessEngine.PurgeFinishedBeforeAsync"/>.</summary>
/// <param name="Trees">The number of trees purged.</param>
/// <param name="Counts">The sum of the rows deleted per table.</param>
/// <param name="More">True when at least one further tree qualified when the call ended.</param>
public sealed record PurgeBatchResult(int Trees, PurgeCounts Counts, bool More);

/// <summary>The result of <see cref="IProcessEngine.PurgeEventsBeforeAsync"/>.</summary>
/// <param name="Events">The number of event id rows deleted.</param>
/// <param name="More">True when more rows qualified when the call ended.</param>
public sealed record PurgeEventsResult(int Events, bool More);

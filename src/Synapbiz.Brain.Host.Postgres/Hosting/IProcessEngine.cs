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
    /// later by the pump.
    /// </summary>
    /// <param name="processEvent">The event.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Whether the event was queued, was a duplicate or had no target, and for how many instances it was queued.</returns>
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
    /// <summary>The event was queued for at least one instance.</summary>
    Queued = 1,

    /// <summary>An event with the same id was published before; nothing was written.</summary>
    Duplicate = 2,

    /// <summary>No instance waits for the event, or the named instance does not exist.</summary>
    NoTarget = 3,
}

/// <summary>The result of publishing an event.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Instances">The number of instances the event was queued for.</param>
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

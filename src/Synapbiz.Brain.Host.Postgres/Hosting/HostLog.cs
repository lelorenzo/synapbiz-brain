using Microsoft.Extensions.Logging;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>
/// The log messages of the host. They carry tenant ids, instance ids, step names, ids of timers, effects and tasks, kinds and
/// codes, and nothing else: never a variable, an input, an output or a payload.
/// </summary>
internal static partial class HostLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "process.definition.unavailable: tenant {TenantId} instance {InstanceId} needs process {ProcessId} version {ProcessVersion}; the item is left for a later pass")]
    internal static partial void DefinitionUnavailable(ILogger logger, string tenantId, string instanceId, string processId, long processVersion);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "process.child.definition_unavailable: tenant {TenantId} instance {InstanceId} step {Step} asked for process {ProcessId}, which the catalog does not know")]
    internal static partial void ChildDefinitionUnavailable(ILogger logger, string tenantId, string instanceId, string step, string processId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "process.port.exception: tenant {TenantId} instance {InstanceId} effect {EffectId} port {Port} threw {ExceptionType}")]
    internal static partial void PortThrew(ILogger logger, string tenantId, string instanceId, string effectId, string port, string exceptionType);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "process.port.timeout: tenant {TenantId} instance {InstanceId} effect {EffectId} port {Port} did not answer in time")]
    internal static partial void PortTimedOut(ILogger logger, string tenantId, string instanceId, string effectId, string port);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "process.port.not_registered: tenant {TenantId} instance {InstanceId} effect {EffectId} names port {Port}, which is not registered")]
    internal static partial void PortNotRegistered(ILogger logger, string tenantId, string instanceId, string effectId, string port);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "process.effect.lease_expired: tenant {TenantId} instance {InstanceId} effect {EffectId} delivery {Delivery}")]
    internal static partial void LeaseExpired(ILogger logger, string tenantId, string instanceId, string effectId, string delivery);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "process.instance.finished: tenant {TenantId} instance {InstanceId} status {Status}")]
    internal static partial void InstanceFinished(ILogger logger, string tenantId, string instanceId, string status);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "process.instance.suspended: tenant {TenantId} instance {InstanceId} step {Step} code {Code}")]
    internal static partial void InstanceSuspended(ILogger logger, string tenantId, string instanceId, string step, string code);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "process.inbox.refused: tenant {TenantId} instance {InstanceId} inbox {InboxId} kind {Kind} code {Code}")]
    internal static partial void InboxRefused(ILogger logger, string tenantId, string instanceId, long inboxId, string kind, string code);

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "process.worker.pass_failed: a pump pass failed with {ExceptionType}; the next pass starts after the poll interval")]
    internal static partial void PassFailed(ILogger logger, string exceptionType);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "process.worker.started: the process worker started with a poll interval of {PollInterval}")]
    internal static partial void WorkerStarted(ILogger logger, TimeSpan pollInterval);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "process.item.failed: {Kind} item of tenant {TenantId} instance {InstanceId} item {ItemId} failed with {ExceptionType}; the item is left as it was and the pass goes on")]
    internal static partial void ItemFailed(ILogger logger, string kind, string tenantId, string instanceId, string itemId, string exceptionType);

    [LoggerMessage(EventId = 13, Level = LogLevel.Warning, Message = "process.trigger.no_business_key: tenant {TenantId} event {EventId} named {EventName} has no usable business key for process {ProcessId}; no instance is started for that trigger")]
    internal static partial void NoBusinessKey(ILogger logger, string tenantId, string eventId, string eventName, string processId);

    [LoggerMessage(EventId = 14, Level = LogLevel.Information, Message = "process.schedule.fired: tenant {TenantId} process {ProcessId} schedule {ScheduleId} occurrence {Occurrence} queued a start (started {Started})")]
    internal static partial void ScheduleFired(ILogger logger, string tenantId, string processId, string scheduleId, string occurrence, bool started);

    [LoggerMessage(EventId = 15, Level = LogLevel.Warning, Message = "process.schedule.misfired: tenant {TenantId} process {ProcessId} schedule {ScheduleId} policy {Policy} missed occurrences up to {Occurrence} (fired {Fired})")]
    internal static partial void ScheduleMisfired(ILogger logger, string tenantId, string processId, string scheduleId, string policy, string occurrence, bool fired);

    [LoggerMessage(EventId = 16, Level = LogLevel.Warning, Message = "process.definition.unavailable: tenant {TenantId} schedule {ScheduleId} of process {ProcessId} needs version {ProcessVersion}; the schedule is left for a later pass")]
    internal static partial void ScheduleDefinitionUnavailable(ILogger logger, string tenantId, string scheduleId, string processId, long processVersion);

    [LoggerMessage(EventId = 17, Level = LogLevel.Warning, Message = "process.window.zone_unknown: tenant {TenantId} instance {InstanceId} step {Step} timer {TimerId} names a time zone the host cannot resolve")]
    internal static partial void WindowZoneUnknown(ILogger logger, string tenantId, string instanceId, string step, string timerId);

    [LoggerMessage(EventId = 18, Level = LogLevel.Information, Message = "process.activation.saved: tenant {TenantId} process {ProcessId} version {ProcessVersion} changed {Changed}")]
    internal static partial void ActivationSaved(ILogger logger, string tenantId, string processId, long processVersion, bool changed);

    [LoggerMessage(EventId = 19, Level = LogLevel.Information, Message = "process.activation.removed: tenant {TenantId} process {ProcessId} removed {Removed}")]
    internal static partial void ActivationRemoved(ILogger logger, string tenantId, string processId, bool removed);
}

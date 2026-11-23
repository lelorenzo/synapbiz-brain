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
}

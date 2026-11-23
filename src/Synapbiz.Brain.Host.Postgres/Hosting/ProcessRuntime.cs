using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>
/// The provider-neutral half of the single transaction shape: it runs the interpreter on an instance whose lock the caller
/// holds and writes everything the result asks for into the same session. Nothing here commits.
/// </summary>
internal sealed class ProcessRuntime
{
    private const string WindowZoneUnknownCode = "process.window.zone_unknown";

    private readonly IProcessCatalog catalog;
    private readonly IPayloadProtector protector;
    private readonly IProcessCalendar calendar;
    private readonly IZoneResolver zones;
    private readonly ILogger<ProcessRuntime> logger;

    internal ProcessRuntime(
        IProcessCatalog catalog,
        IPayloadProtector protector,
        IProcessCalendar calendar,
        IZoneResolver zones,
        TimeProvider time,
        ILogger<ProcessRuntime> logger)
    {
        this.catalog = catalog;
        this.protector = protector;
        this.calendar = calendar;
        this.zones = zones;
        Time = time;
        this.logger = logger;
    }

    internal TimeProvider Time { get; }

    internal ILogger Logger => logger;

    internal string Protect(string tenantId, string text) => protector.Protect(tenantId, text);

    internal string Unprotect(string tenantId, string protectedText) => protector.Unprotect(tenantId, protectedText);

    internal ProcessClock Clock(string tenantId)
    {
        var now = Time.GetUtcNow();
        return new ProcessClock(now, calendar.Today(tenantId, now));
    }

    internal BrainProcess? Latest(string processId) => catalog.Latest(processId);

    /// <summary>The definition with the given id, version and content hash, without a warning when the catalog does not have it.</summary>
    internal BrainProcess? Find(string processId, long version, string contentHash) => catalog.Find(processId, version, contentHash);

    /// <summary>
    /// The time zone with the given IANA id, or null when the host does not know it. An id that does not have the shape of one
    /// (<c>UTC</c> or <c>Area/Location</c>: <c>localtime</c>, <c>EST5EDT</c>, <c>Factory</c>, a path, an empty text) is unknown without the
    /// zone database being asked, so that nothing a caller or a payload supplies is ever looked up as a name in it.
    /// </summary>
    internal TimeZoneInfo? FindZone(string id) => DeliveryWindows.IsZoneIdShape(id) ? zones.Find(id) : null;

    /// <summary>The definition the instance is pinned to; null (and a warning) when the catalog does not have it.</summary>
    internal BrainProcess? Resolve(InstanceRecord instance)
    {
        var process = catalog.Find(instance.ProcessId, instance.ProcessVersion, instance.ProcessHash);
        if (process is null)
        {
            HostLog.DefinitionUnavailable(logger, instance.TenantId, instance.InstanceId, instance.ProcessId, instance.ProcessVersion);
        }

        return process;
    }

    /// <summary>The state of a started instance; null while it is created and not started.</summary>
    internal ProcessState? ReadState(InstanceRecord instance) =>
        instance.ProtectedState is null ? null : ProcessState.FromJson(Unprotect(instance.TenantId, instance.ProtectedState));

    internal ProcessInstanceInfo ToInfo(InstanceRecord row)
    {
        var state = ReadState(row);
        var problem = state?.Problem ?? (row.ProblemCode is null ? null : new ProcessProblem(row.ProblemCode, row.CurrentStep ?? "", null));
        return new ProcessInstanceInfo(
            row.TenantId,
            row.InstanceId,
            row.ProcessId,
            row.ProcessVersion,
            row.BusinessKey,
            StoredStatus.ToPublic(row.Status),
            row.Stage,
            row.CurrentStep,
            row.Result,
            problem,
            row.ParentInstanceId,
            row.CreatedAt,
            row.UpdatedAt,
            state);
    }

    /// <summary>
    /// Steps 4 and 5 of the transaction shape: run <c>Advance</c> and, when it applied, write the outcome. <paramref name="whenApplied"/>
    /// runs first when the trigger applied, so that a row the trigger consumed (a task) is closed before the work of a finished
    /// instance is cleared.
    /// </summary>
    internal async Task<AdvanceResult> AdvanceAsync(
        IProcessSession session,
        InstanceRecord instance,
        BrainProcess process,
        ProcessState state,
        ProcessTrigger trigger,
        ProcessClock clock,
        CancellationToken cancellationToken,
        Func<Task>? whenApplied = null)
    {
        var result = process.Advance(state, trigger, clock);
        if (result.Disposition == AdvanceDisposition.Applied)
        {
            if (whenApplied is not null)
            {
                await whenApplied().ConfigureAwait(false);
            }

            await PersistAsync(session, instance, result, clock.Now, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>The same for the first run of an instance: <c>Start</c> instead of <c>Advance</c>.</summary>
    internal async Task<AdvanceResult> StartAsync(
        IProcessSession session,
        InstanceRecord instance,
        BrainProcess process,
        JsonObject? input,
        ProcessClock clock,
        CancellationToken cancellationToken)
    {
        var result = process.Start(instance.InstanceId, input, clock);
        if (result.Disposition == AdvanceDisposition.Applied)
        {
            await PersistAsync(session, instance, result, clock.Now, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>A start that the interpreter refused: the instance is cancelled with the code, and a parent hears of it.</summary>
    internal async Task CancelRejectedStartAsync(IProcessSession session, InstanceRecord instance, string code, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await session.SaveInstanceAsync(
            instance.Key,
            new InstanceWrite((int)ProcessStatus.Cancelled, null, null, null, code, null, now),
            cancellationToken).ConfigureAwait(false);
        if (instance.ParentInstanceId is not null && instance.ParentChildKey is not null)
        {
            await session.AppendInboxAsync(
                new InstanceKey(instance.TenantId, instance.ParentInstanceId),
                InboxKind.ChildFailed,
                Protect(instance.TenantId, InboxPayloads.ChildFailed(instance.ParentChildKey, code)),
                now,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistAsync(IProcessSession session, InstanceRecord instance, AdvanceResult result, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = instance.Key;
        var state = result.State;
        await session.SaveInstanceAsync(
            key,
            new InstanceWrite((int)state.Status, state.Stage, state.CurrentStep, state.Result, state.Problem?.Code, Protect(instance.TenantId, state.ToJson()), now),
            cancellationToken).ConfigureAwait(false);
        if (result.History.Count > 0)
        {
            var rows = result.History
                .Select(entry => new HistoryRow(
                    entry.Sequence,
                    entry.At,
                    entry.Step,
                    entry.Entry,
                    entry.Attempt,
                    entry.Kind,
                    entry.Code,
                    entry.DataJson is null ? null : Protect(instance.TenantId, entry.DataJson)))
                .ToList();
            await session.AppendHistoryAsync(key, rows, cancellationToken).ConfigureAwait(false);
        }

        foreach (var request in result.Requests)
        {
            await ApplyRequestAsync(session, instance, request, now, cancellationToken).ConfigureAwait(false);
        }

        if (state.Status is ProcessStatus.Completed or ProcessStatus.Cancelled)
        {
            await FinishAsync(session, instance, state, now, cancellationToken).ConfigureAwait(false);
        }
        else if (state.Status == ProcessStatus.Suspended)
        {
            await session.DiscardPendingEffectsAsync(key, cancellationToken).ConfigureAwait(false);
            HostLog.InstanceSuspended(logger, instance.TenantId, instance.InstanceId, state.Problem?.Step ?? state.CurrentStep ?? "", state.Problem?.Code ?? "");
        }
    }

    private async Task ApplyRequestAsync(IProcessSession session, InstanceRecord instance, ProcessRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = instance.Key;
        var tenant = instance.TenantId;
        switch (request)
        {
            case EffectRequest effect:
                await session.InsertEffectAsync(
                    key,
                    new EffectRecord(
                        tenant,
                        effect.EffectId,
                        instance.InstanceId,
                        effect.Step,
                        effect.IdempotencyKey,
                        effect.EffectKind,
                        effect.Port,
                        Protect(tenant, effect.InputJson),
                        effect.Attempt,
                        effect.Delivery,
                        effect.Model,
                        effect.PromptId,
                        effect.PromptVersion,
                        EffectStatus.Pending,
                        now,
                        null,
                        null),
                    cancellationToken).ConfigureAwait(false);
                break;
            case TimerRequest timer:
                await session.InsertTimerAsync(key, timer.TimerId, timer.DueAt, cancellationToken).ConfigureAwait(false);
                break;
            case CancelTimerRequest cancelTimer:
                await session.DeleteTimerAsync(key, cancelTimer.TimerId, cancellationToken).ConfigureAwait(false);
                break;
            case WaitRequest wait:
                await session.InsertWaitAsync(key, wait.WaitId, wait.Event, wait.Correlation, cancellationToken).ConfigureAwait(false);
                break;
            case CancelWaitRequest cancelWait:
                await session.DeleteWaitAsync(key, cancelWait.WaitId, cancellationToken).ConfigureAwait(false);
                break;
            case TaskRequest task:
                await session.InsertTaskAsync(
                    key,
                    new TaskRecord(tenant, task.TaskId, instance.InstanceId, task.Step, task.Queue, task.Outcomes, Protect(tenant, task.ShowJson), HumanTaskStatus.Open, now),
                    cancellationToken).ConfigureAwait(false);
                break;
            case CancelTaskRequest cancelTask:
                await session.CloseTaskAsync(key, cancelTask.TaskId, HumanTaskStatus.Cancelled, now, cancellationToken).ConfigureAwait(false);
                break;
            case ChildStartRequest child:
                await StartChildAsync(session, instance, child, now, cancellationToken).ConfigureAwait(false);
                break;
            case WindowRequest window:
                await ApplyWindowAsync(session, instance, window, now, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException("The interpreter produced a request of an unknown kind.");
        }
    }

    /// <summary>
    /// A delivery window: a timer is stored that falls due when the window is open (at once when it is open now). When the host cannot
    /// resolve the zone, nothing is stored and a <see cref="WindowFailed"/> trigger is queued through the inbox of the instance
    /// itself, so that it is applied in a transaction of its own, like every other trigger.
    /// </summary>
    private async Task ApplyWindowAsync(IProcessSession session, InstanceRecord instance, WindowRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var zone = FindZone(request.Zone);
        if (zone is null)
        {
            HostLog.WindowZoneUnknown(logger, instance.TenantId, instance.InstanceId, request.Step, request.TimerId);
            await session.AppendInboxAsync(
                instance.Key,
                InboxKind.WindowFailed,
                Protect(instance.TenantId, InboxPayloads.WindowFailed(request.TimerId, WindowZoneUnknownCode)),
                now,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var due = DeliveryWindows.NextOpen(now, zone, request.Days, request.From, request.To);
        await session.InsertTimerAsync(instance.Key, request.TimerId, due, cancellationToken).ConfigureAwait(false);
    }

    private async Task StartChildAsync(IProcessSession session, InstanceRecord parent, ChildStartRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var parentKey = parent.Key;
        var definition = catalog.Latest(request.ProcessId);
        if (definition is null)
        {
            HostLog.ChildDefinitionUnavailable(logger, parent.TenantId, parent.InstanceId, request.Step, request.ProcessId);
            await session.AppendInboxAsync(
                parentKey,
                InboxKind.ChildFailed,
                Protect(parent.TenantId, InboxPayloads.ChildFailed(request.ChildKey, "process.definition.unavailable")),
                now,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await QueueStartAsync(
            session,
            new NewInstance(parent.TenantId, Guid.NewGuid().ToString("N"), definition.Id, definition.Version, definition.ContentHash, request.ChildKey, parent.InstanceId, request.ChildKey, now),
            request.InputJson,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Queues the start of an instance: inserts its row in status Created (a row that already holds the business key of the process
    /// is left alone, so one business key is one instance whatever starts it) and appends the Start inbox row that the pump applies
    /// under the instance lock. The start of a child, of an event trigger and of a schedule occurrence all go through here.
    /// </summary>
    /// <returns>True when this call created the instance; false when the business key already had one.</returns>
    internal async Task<bool> QueueStartAsync(IProcessSession session, NewInstance row, string inputJson, CancellationToken cancellationToken)
    {
        var inserted = await session.InsertInstanceAsync(row, cancellationToken).ConfigureAwait(false);
        if (!inserted.Inserted)
        {
            return false;
        }

        await session.AppendInboxAsync(
            new InstanceKey(row.TenantId, row.InstanceId),
            InboxKind.Start,
            Protect(row.TenantId, InboxPayloads.Start(inputJson)),
            row.Now,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>What happens when a transaction leaves an instance completed or cancelled.</summary>
    private async Task FinishAsync(IProcessSession session, InstanceRecord instance, ProcessState state, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = instance.Key;
        await session.ClearWorkAsync(key, now, cancellationToken).ConfigureAwait(false);
        if (instance.ParentInstanceId is not null && instance.ParentChildKey is not null)
        {
            var parent = new InstanceKey(instance.TenantId, instance.ParentInstanceId);
            var payload = state.Status == ProcessStatus.Completed
                ? InboxPayloads.ChildCompleted(instance.ParentChildKey, state.Result ?? "", VariablesJson(state))
                : InboxPayloads.ChildFailed(instance.ParentChildKey, "process.child.cancelled");
            await session.AppendInboxAsync(
                parent,
                state.Status == ProcessStatus.Completed ? InboxKind.ChildCompleted : InboxKind.ChildFailed,
                Protect(instance.TenantId, payload),
                now,
                cancellationToken).ConfigureAwait(false);
        }

        if (state.Status == ProcessStatus.Cancelled)
        {
            foreach (var childId in await session.ListLiveChildrenAsync(key, cancellationToken).ConfigureAwait(false))
            {
                await session.AppendInboxAsync(
                    new InstanceKey(instance.TenantId, childId),
                    InboxKind.Cancel,
                    Protect(instance.TenantId, InboxPayloads.Cancel("process.parent.cancelled")),
                    now,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        HostLog.InstanceFinished(logger, instance.TenantId, instance.InstanceId, state.Status.ToString());
    }

    /// <summary>The JSON object of all the variables of a state, as the output of a completed child.</summary>
    internal static string VariablesJson(ProcessState state)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in state.Variables.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(name);
                writer.WriteRawValue(value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}

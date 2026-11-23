using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>
/// One pass of the work of the host. Every item is handled in one transaction of the shape of section 4: the instance row is
/// locked first, the item is re-validated under that lock, then the interpreter runs.
/// </summary>
internal sealed class ProcessPump : IProcessPump
{
    private const string OutcomeUnknown = "process.effect.outcome_unknown";

    private readonly IProcessStore store;
    private readonly ProcessRuntime runtime;
    private readonly Dictionary<string, IProcessPort> ports;
    private readonly ProcessHostOptions options;
    private readonly string owner = Guid.NewGuid().ToString("N");

    internal ProcessPump(IProcessStore store, ProcessRuntime runtime, IEnumerable<IProcessPort> ports, IOptions<ProcessHostOptions> options)
    {
        this.store = store;
        this.runtime = runtime;
        this.ports = ports.ToDictionary(port => port.Name, StringComparer.Ordinal);
        this.options = options.Value;
    }

    /// <inheritdoc />
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var handled = await SchedulesAsync(cancellationToken).ConfigureAwait(false);
        handled += await ExpiredLeasesAsync(cancellationToken).ConfigureAwait(false);
        handled += await InboxAsync(cancellationToken).ConfigureAwait(false);
        handled += await TimersAsync(cancellationToken).ConfigureAwait(false);
        handled += await EffectsAsync(cancellationToken).ConfigureAwait(false);
        return handled;
    }

    /// <summary>
    /// Opens a transaction, locks the instance of the item and runs <paramref name="work"/>; the work returns true when it handled
    /// the item (the transaction commits) and false when it leaves it (the transaction rolls back).
    /// </summary>
    private async Task<bool> WithInstanceAsync(
        WorkRef item,
        bool skipLocked,
        Func<IProcessSession, InstanceRecord, Task<bool>> work,
        CancellationToken cancellationToken)
    {
        var session = await store.BeginAsync(cancellationToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
        {
            var instance = await session.LockInstanceAsync(new InstanceKey(item.TenantId, item.InstanceId), skipLocked, cancellationToken).ConfigureAwait(false);
            if (instance is null || !await work(session, instance).ConfigureAwait(false))
            {
                return false;
            }

            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    /// <summary>
    /// The first step of a pass: the schedules that are due. Each is handled in one transaction that locks its row
    /// (<c>for update skip locked</c>) and re-reads it under the lock, so that two pumps never handle the same schedule at once, and the
    /// start it queues has a business key per occurrence, so that two pumps can never start two instances for one occurrence.
    /// </summary>
    private async Task<int> SchedulesAsync(CancellationToken cancellationToken)
    {
        return await ScanAsync(
            WorkKind.DueSchedule,
            item => FireScheduleAsync(new ScheduleKey(item.TenantId, item.ProcessId!, item.ItemId), cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> FireScheduleAsync(ScheduleKey key, CancellationToken cancellationToken)
    {
        var session = await store.BeginAsync(cancellationToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
        {
            var record = await session.LockScheduleAsync(key, true, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                return false;
            }

            var now = runtime.Time.GetUtcNow();
            var row = record.Row;
            if (row.NextFireAt > now)
            {
                // Another pump handled it between the scan and the lock.
                return true;
            }

            var process = runtime.Find(row.ProcessId, record.ProcessVersion, record.ProcessHash);
            if (process is null)
            {
                HostLog.ScheduleDefinitionUnavailable(runtime.Logger, row.TenantId, row.ScheduleId, row.ProcessId, record.ProcessVersion);
                return false;
            }

            var declaration = process.Triggers.OfType<ScheduleTriggerDeclaration>().FirstOrDefault(trigger => trigger.Id == row.ScheduleId)
                ?? throw new InvalidOperationException("The pinned definition no longer declares the schedule.");
            var zone = runtime.FindZone(row.Zone) ?? throw new InvalidOperationException("The zone of the schedule is no longer known to the host.");
            if (!CronSchedule.TryParse(row.Cron, out var cron))
            {
                throw new InvalidOperationException("The cron expression of the schedule is not valid.");
            }

            var plan = SchedulePlanner.Plan(cron, zone, row.Misfire, row.NextFireAt, now, options.ScheduleMisfireGrace);
            var started = false;
            if (plan.Fire is { } occurrence)
            {
                var input = TriggerInputs.ScheduleInput(runtime.Unprotect(row.TenantId, record.ProtectedInput), declaration, occurrence, zone);
                started = await runtime.QueueStartAsync(
                    session,
                    new NewInstance(row.TenantId, Guid.NewGuid().ToString("N"), row.ProcessId, record.ProcessVersion, record.ProcessHash, TriggerInputs.ScheduleBusinessKey(row.ScheduleId, occurrence), null, null, now),
                    input.ToJsonString(),
                    cancellationToken).ConfigureAwait(false);
                HostLog.ScheduleFired(runtime.Logger, row.TenantId, row.ProcessId, row.ScheduleId, Occurrence(occurrence), started);
            }

            if (plan.Misfired)
            {
                HostLog.ScheduleMisfired(runtime.Logger, row.TenantId, row.ProcessId, row.ScheduleId, row.Misfire.ToString(), Occurrence(plan.Last), plan.Fire is not null);
            }

            await session.SaveScheduleAsync(key, plan.NextFireAt, cancellationToken).ConfigureAwait(false);
            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    private static string Occurrence(DateTimeOffset occurrence) => occurrence.UtcDateTime.ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture);

    private async Task<int> ExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        return await ScanAsync(
            WorkKind.ExpiredLease,
            item => WithInstanceAsync(item, true, (session, instance) => ExpireLeaseAsync(session, instance, item.ItemId, cancellationToken), cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the scan of one kind page by page (keyset paging on its ordering key) and handles the items one by one, until the scan is
    /// exhausted or <see cref="ProcessHostOptions.BatchSize"/> items have been handled in this pass. Items that are left untouched
    /// (an instance locked elsewhere, a definition that is not available, an item that failed) therefore cannot hide the items
    /// behind them. An item that throws is isolated: it is logged and left as it was, and the pass goes on.
    /// </summary>
    private async Task<int> ScanAsync(WorkKind kind, Func<WorkRef, Task<bool>> handle, CancellationToken cancellationToken)
    {
        var now = runtime.Time.GetUtcNow();
        var handled = 0;
        WorkRef? after = null;
        while (handled < options.BatchSize)
        {
            var page = await store.ScanAsync(kind, now, options.BatchSize, after, cancellationToken).ConfigureAwait(false);
            foreach (var item in page)
            {
                if (handled >= options.BatchSize)
                {
                    break;
                }

                handled += await IsolatedAsync(kind.ToString(), item, async () => await handle(item).ConfigureAwait(false), cancellationToken).ConfigureAwait(false) ? 1 : 0;
            }

            if (page.Count < options.BatchSize)
            {
                break;
            }

            after = page[^1];
        }

        return handled;
    }

    /// <summary>
    /// Runs the handling of one item and turns any exception except the cancellation of the pass into a warning (item kind, tenant,
    /// ids and the exception type; never its message, which could carry a protected value). The item is then left as it was.
    /// </summary>
    private async Task<bool> IsolatedAsync(string kind, WorkRef item, Func<Task<bool>> handle, CancellationToken cancellationToken)
    {
        try
        {
            return await handle().ConfigureAwait(false);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            HostLog.ItemFailed(
                runtime.Logger,
                kind,
                item.TenantId,
                item.InstanceId,
                item.ProcessId is null ? item.ItemId : item.ProcessId + "/" + item.ItemId,
                exception.GetType().FullName ?? nameof(Exception));
            return false;
        }
    }

    private async Task<bool> ExpireLeaseAsync(IProcessSession session, InstanceRecord instance, string effectId, CancellationToken cancellationToken)
    {
        var key = instance.Key;
        var clock = runtime.Clock(instance.TenantId);
        var effect = await session.GetEffectAsync(key, effectId, cancellationToken).ConfigureAwait(false);
        if (effect is not { Status: EffectStatus.Running, LeaseUntil: { } until } || until > clock.Now)
        {
            return true;
        }

        HostLog.LeaseExpired(runtime.Logger, instance.TenantId, instance.InstanceId, effectId, effect.Delivery.ToString());
        if (effect.Delivery == ProcessDelivery.AtLeastOnce)
        {
            await session.RequeueEffectAsync(key, effectId, clock.Now, cancellationToken).ConfigureAwait(false);
            return true;
        }

        return await AdvanceEffectAsync(session, instance, effectId, new EffectFailed(effectId, ProcessErrorKind.Unknown, OutcomeUnknown), clock, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies the outcome of an effect to its instance and closes the effect row; false (nothing written) when the definition is unavailable.</summary>
    private async Task<bool> AdvanceEffectAsync(IProcessSession session, InstanceRecord instance, string effectId, ProcessTrigger trigger, ProcessClock clock, CancellationToken cancellationToken)
    {
        var state = runtime.ReadState(instance);
        if (state is not null)
        {
            var process = runtime.Resolve(instance);
            if (process is null)
            {
                return false;
            }

            await runtime.AdvanceAsync(session, instance, process, state, trigger, clock, cancellationToken).ConfigureAwait(false);
        }

        await session.FinishEffectAsync(instance.Key, effectId, EffectStatus.Done, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<int> InboxAsync(CancellationToken cancellationToken)
    {
        return await ScanAsync(
            WorkKind.Inbox,
            item =>
            {
                var inboxId = long.Parse(item.ItemId, CultureInfo.InvariantCulture);
                return WithInstanceAsync(item, true, (session, instance) => ApplyInboxAsync(session, instance, inboxId, cancellationToken), cancellationToken);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ApplyInboxAsync(IProcessSession session, InstanceRecord instance, long inboxId, CancellationToken cancellationToken)
    {
        var key = instance.Key;
        var row = await session.GetInboxAsync(key, inboxId, cancellationToken).ConfigureAwait(false);
        if (row is not { Status: InboxStatus.Pending })
        {
            return true;
        }

        if (await session.FirstPendingInboxAsync(key, cancellationToken).ConfigureAwait(false) != inboxId)
        {
            return false;
        }

        var clock = runtime.Clock(instance.TenantId);
        var state = runtime.ReadState(instance);
        if (row.Kind == InboxKind.Start)
        {
            return await ApplyStartAsync(session, instance, row, state, clock, cancellationToken).ConfigureAwait(false);
        }

        if (state is null)
        {
            await session.CompleteInboxAsync(key, inboxId, InboxStatus.Done, "process.trigger.stale", cancellationToken).ConfigureAwait(false);
            return true;
        }

        var process = runtime.Resolve(instance);
        if (process is null)
        {
            return false;
        }

        var trigger = InboxPayloads.ToTrigger(row.Kind, runtime.Unprotect(instance.TenantId, row.ProtectedPayload));
        var result = await runtime.AdvanceAsync(session, instance, process, state, trigger, clock, cancellationToken).ConfigureAwait(false);
        await CloseInboxAsync(session, instance, row, result, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ApplyStartAsync(IProcessSession session, InstanceRecord instance, InboxRecord row, ProcessState? state, ProcessClock clock, CancellationToken cancellationToken)
    {
        var key = instance.Key;
        if (state is not null || instance.Status != StoredStatus.Created)
        {
            await session.CompleteInboxAsync(key, row.InboxId, InboxStatus.Done, "process.trigger.stale", cancellationToken).ConfigureAwait(false);
            return true;
        }

        var process = runtime.Resolve(instance);
        if (process is null)
        {
            return false;
        }

        var input = JsonNode.Parse(InboxPayloads.StartInput(runtime.Unprotect(instance.TenantId, row.ProtectedPayload))) as JsonObject;
        var result = await runtime.StartAsync(session, instance, process, input, clock, cancellationToken).ConfigureAwait(false);
        if (result.Disposition == AdvanceDisposition.Applied)
        {
            await session.CompleteInboxAsync(key, row.InboxId, InboxStatus.Done, null, cancellationToken).ConfigureAwait(false);
            return true;
        }

        var code = result.Code ?? "process.input.unknown";
        await runtime.CancelRejectedStartAsync(session, instance, code, clock.Now, cancellationToken).ConfigureAwait(false);
        await session.CompleteInboxAsync(key, row.InboxId, InboxStatus.Rejected, code, cancellationToken).ConfigureAwait(false);
        HostLog.InboxRefused(runtime.Logger, instance.TenantId, instance.InstanceId, row.InboxId, row.Kind.ToString(), code);
        return true;
    }

    private async Task CloseInboxAsync(IProcessSession session, InstanceRecord instance, InboxRecord row, AdvanceResult result, CancellationToken cancellationToken)
    {
        var (status, code) = result.Disposition switch
        {
            AdvanceDisposition.Applied => (InboxStatus.Done, (string?)null),
            AdvanceDisposition.Ignored => (InboxStatus.Done, result.Code),
            _ => (InboxStatus.Rejected, result.Code),
        };
        await session.CompleteInboxAsync(instance.Key, row.InboxId, status, code, cancellationToken).ConfigureAwait(false);
        if (status == InboxStatus.Rejected)
        {
            HostLog.InboxRefused(runtime.Logger, instance.TenantId, instance.InstanceId, row.InboxId, row.Kind.ToString(), code ?? "");
        }
    }

    private async Task<int> TimersAsync(CancellationToken cancellationToken)
    {
        return await ScanAsync(
            WorkKind.DueTimer,
            item => WithInstanceAsync(item, true, (session, instance) => FireTimerAsync(session, instance, item.ItemId, cancellationToken), cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> FireTimerAsync(IProcessSession session, InstanceRecord instance, string timerId, CancellationToken cancellationToken)
    {
        if (!await session.DeleteTimerAsync(instance.Key, timerId, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        var state = runtime.ReadState(instance);
        if (state is null)
        {
            return true;
        }

        var process = runtime.Resolve(instance);
        if (process is null)
        {
            return false;
        }

        await runtime.AdvanceAsync(session, instance, process, state, new TimerFired(timerId), runtime.Clock(instance.TenantId), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Step 4 for effects. For every due effect the pump first takes a concurrency slot, then claims that one effect (its lease starts
    /// at the claim), then invokes the port at once. An effect that did not get a slot is not claimed and stays Pending, so its lease
    /// cannot run out while it waits. Once the pass token is cancelled no further effect is claimed; the calls already in flight are
    /// awaited and their results recorded whatever the token says, because the port did its work and dropping the result would turn
    /// the effect into an unknown outcome.
    /// </summary>
    private async Task<int> EffectsAsync(CancellationToken cancellationToken)
    {
        var now = runtime.Time.GetUtcNow();
        var handled = 0;
        WorkRef? after = null;
        var running = new List<Task>();
        using var gate = new SemaphoreSlim(options.MaxConcurrentEffects);
        try
        {
            while (handled < options.BatchSize && !cancellationToken.IsCancellationRequested)
            {
                var page = await store.ScanAsync(WorkKind.DueEffect, now, options.BatchSize, after, cancellationToken).ConfigureAwait(false);
                foreach (var item in page)
                {
                    if (handled >= options.BatchSize || cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    var slotHeld = true;
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var claim = await ClaimIsolatedAsync(item, cancellationToken).ConfigureAwait(false);
                        handled += claim.Handled ? 1 : 0;
                        if (claim.Claimed is not null)
                        {
                            running.Add(RunEffectAsync(claim.Claimed, gate, cancellationToken));
                            slotHeld = false;
                        }
                    }
                    finally
                    {
                        if (slotHeld)
                        {
                            gate.Release();
                        }
                    }
                }

                if (page.Count < options.BatchSize)
                {
                    break;
                }

                after = page[^1];
            }
        }
        finally
        {
            await Task.WhenAll(running).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return handled;
    }

    private async Task<(bool Handled, EffectRecord? Claimed)> ClaimIsolatedAsync(WorkRef item, CancellationToken cancellationToken)
    {
        EffectRecord? taken = null;
        var done = await IsolatedAsync(
            nameof(WorkKind.DueEffect),
            item,
            () => WithInstanceAsync(
                item,
                true,
                async (session, instance) =>
                {
                    var claim = await ClaimAsync(session, instance, item.ItemId, cancellationToken).ConfigureAwait(false);
                    taken = claim.Claimed;
                    return claim.Handled;
                },
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return (done, done ? taken : null);
    }

    /// <summary>
    /// Step 4, first half: under the instance lock the effect is discarded when the instance no longer waits for it, failed when its
    /// port is not registered, and otherwise claimed (Running, with a lease and this pump as owner). The pair says whether the
    /// item was handled and, when it was claimed, the effect to invoke.
    /// </summary>
    private async Task<(bool Handled, EffectRecord? Claimed)> ClaimAsync(IProcessSession session, InstanceRecord instance, string effectId, CancellationToken cancellationToken)
    {
        var key = instance.Key;
        var effect = await session.GetEffectAsync(key, effectId, cancellationToken).ConfigureAwait(false);
        if (effect is not { Status: EffectStatus.Pending })
        {
            return (true, null);
        }

        var state = runtime.ReadState(instance);
        if (state?.Pending is not { Kind: ProcessPendingKind.Effect } pending || pending.Id != effectId)
        {
            await session.FinishEffectAsync(key, effectId, EffectStatus.Discarded, cancellationToken).ConfigureAwait(false);
            return (true, null);
        }

        if (runtime.Resolve(instance) is null)
        {
            return (false, null);
        }

        var clock = runtime.Clock(instance.TenantId);
        if (!ports.ContainsKey(effect.Port))
        {
            HostLog.PortNotRegistered(runtime.Logger, instance.TenantId, instance.InstanceId, effectId, effect.Port);
            var trigger = new EffectFailed(effectId, ProcessErrorKind.NotAvailable, "process.port.not_registered");
            return (await AdvanceEffectAsync(session, instance, effectId, trigger, clock, cancellationToken).ConfigureAwait(false), null);
        }

        await session.ClaimEffectAsync(key, effectId, clock.Now + options.EffectLease, owner, cancellationToken).ConfigureAwait(false);
        return (true, effect);
    }

    /// <summary>
    /// Invokes the port of a claimed effect and records what came back, then gives the slot back. Nothing escapes: a failure of the
    /// recording is logged and leaves the row Running, so that its lease expires and a later pass handles it.
    /// </summary>
    private async Task RunEffectAsync(EffectRecord effect, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        try
        {
            // Isolated with no token: the pass token must not turn the recording of a result into an exception.
            await IsolatedAsync(
                nameof(WorkKind.DueEffect),
                new WorkRef(effect.TenantId, effect.InstanceId, effect.EffectId),
                async () =>
                {
                    var trigger = await InvokeAsync(effect, cancellationToken).ConfigureAwait(false);
                    if (trigger is not null)
                    {
                        await ApplyResultAsync(effect, trigger).ConfigureAwait(false);
                    }

                    return trigger is not null;
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Invokes the port outside any transaction and turns what happened into the trigger of section 7. Null when the pass was
    /// cancelled and the port gave way to it (it threw the cancellation): there is no result to record, the row stays Running, and
    /// its lease decides what a later pass does with it. A result that does arrive is returned whatever the state of the token.
    /// </summary>
    private async Task<ProcessTrigger?> InvokeAsync(EffectRecord effect, CancellationToken cancellationToken)
    {
        var port = ports[effect.Port];
        var request = new EffectRequest(
            effect.Step,
            effect.EffectId,
            effect.IdempotencyKey,
            effect.Kind,
            effect.Port,
            runtime.Unprotect(effect.TenantId, effect.ProtectedInput),
            effect.Attempt,
            effect.Delivery,
            effect.Model,
            effect.PromptId,
            effect.PromptVersion);
        var call = new PortCall(effect.TenantId, effect.InstanceId, request);
        using var timeout = new CancellationTokenSource(options.PortTimeout, runtime.Time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = timeout.Token.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), expired);

        // Task.Run turns a port that throws before it returns a task into a faulted task, so that one code path sees every failure.
        var invocation = Task.Run(() => port.InvokeAsync(call, linked.Token), CancellationToken.None);
        await Task.WhenAny(invocation, expired.Task).ConfigureAwait(false);

        if (invocation.IsCompletedSuccessfully)
        {
            return (await invocation.ConfigureAwait(false)) switch
            {
                PortCompleted { OutputJson: { } output } => new EffectCompleted(effect.EffectId, output),
                PortFailed failed => new EffectFailed(effect.EffectId, failed.Kind, failed.Code, failed.RetryAfter),
                _ => Failure(effect, "process.port.exception", "InvalidPortResult"),
            };
        }

        if (!invocation.IsCompleted)
        {
            _ = invocation.ContinueWith(static task => task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            HostLog.PortTimedOut(runtime.Logger, effect.TenantId, effect.InstanceId, effect.EffectId, effect.Port);
            return Failure(effect, "process.port.timeout", null);
        }

        var cause = invocation.Exception?.GetBaseException();
        if (timeout.IsCancellationRequested && (invocation.IsCanceled || cause is OperationCanceledException))
        {
            HostLog.PortTimedOut(runtime.Logger, effect.TenantId, effect.InstanceId, effect.EffectId, effect.Port);
            return Failure(effect, "process.port.timeout", null);
        }

        if (cancellationToken.IsCancellationRequested && (invocation.IsCanceled || cause is OperationCanceledException))
        {
            return null;
        }

        return Failure(effect, "process.port.exception", cause?.GetType().FullName ?? typeof(OperationCanceledException).FullName!);
    }

    /// <summary>The failure of a port that threw or timed out: transient for at-least-once delivery, unknown for at-most-once.</summary>
    private EffectFailed Failure(EffectRecord effect, string code, string? exceptionType)
    {
        if (exceptionType is not null)
        {
            HostLog.PortThrew(runtime.Logger, effect.TenantId, effect.InstanceId, effect.EffectId, effect.Port, exceptionType);
        }

        return effect.Delivery == ProcessDelivery.AtMostOnce
            ? new EffectFailed(effect.EffectId, ProcessErrorKind.Unknown, OutcomeUnknown)
            : new EffectFailed(effect.EffectId, ProcessErrorKind.Transient, code);
    }

    /// <summary>
    /// Step 4, second half: a section 4 transaction that re-validates the row is still Running with this owner, applies the trigger
    /// and closes the row. It waits for the instance lock and takes no cancellation token: the port has already been called, so the
    /// result must not be dropped because someone else holds the instance for a moment, or because the pass is being stopped.
    /// </summary>
    private async Task ApplyResultAsync(EffectRecord effect, ProcessTrigger trigger)
    {
        var none = CancellationToken.None;
        var item = new WorkRef(effect.TenantId, effect.InstanceId, effect.EffectId);
        await WithInstanceAsync(
            item,
            false,
            async (session, instance) =>
            {
                var current = await session.GetEffectAsync(instance.Key, effect.EffectId, none).ConfigureAwait(false);
                if (current is not { Status: EffectStatus.Running } || current.LeaseOwner != owner)
                {
                    return true;
                }

                return await AdvanceEffectAsync(session, instance, effect.EffectId, trigger, runtime.Clock(instance.TenantId), none).ConfigureAwait(false);
            },
            none).ConfigureAwait(false);
    }
}

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
        var handled = await ExpiredLeasesAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task<int> ExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        var items = await store.ScanAsync(WorkKind.ExpiredLease, runtime.Time.GetUtcNow(), options.BatchSize, cancellationToken).ConfigureAwait(false);
        var handled = 0;
        foreach (var item in items)
        {
            var done = await WithInstanceAsync(item, true, (session, instance) => ExpireLeaseAsync(session, instance, item.ItemId, cancellationToken), cancellationToken).ConfigureAwait(false);
            handled += done ? 1 : 0;
        }

        return handled;
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
        var items = await store.ScanAsync(WorkKind.Inbox, runtime.Time.GetUtcNow(), options.BatchSize, cancellationToken).ConfigureAwait(false);
        var handled = 0;
        foreach (var item in items)
        {
            var inboxId = long.Parse(item.ItemId, CultureInfo.InvariantCulture);
            var done = await WithInstanceAsync(item, true, (session, instance) => ApplyInboxAsync(session, instance, inboxId, cancellationToken), cancellationToken).ConfigureAwait(false);
            handled += done ? 1 : 0;
        }

        return handled;
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
        var items = await store.ScanAsync(WorkKind.DueTimer, runtime.Time.GetUtcNow(), options.BatchSize, cancellationToken).ConfigureAwait(false);
        var handled = 0;
        foreach (var item in items)
        {
            var done = await WithInstanceAsync(item, true, (session, instance) => FireTimerAsync(session, instance, item.ItemId, cancellationToken), cancellationToken).ConfigureAwait(false);
            handled += done ? 1 : 0;
        }

        return handled;
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

    private async Task<int> EffectsAsync(CancellationToken cancellationToken)
    {
        var items = await store.ScanAsync(WorkKind.DueEffect, runtime.Time.GetUtcNow(), options.BatchSize, cancellationToken).ConfigureAwait(false);
        var handled = 0;
        var claimed = new List<EffectRecord>();
        foreach (var item in items)
        {
            EffectRecord? taken = null;
            var done = await WithInstanceAsync(
                item,
                true,
                async (session, instance) =>
                {
                    var claim = await ClaimAsync(session, instance, item.ItemId, cancellationToken).ConfigureAwait(false);
                    taken = claim.Claimed;
                    return claim.Handled;
                },
                cancellationToken).ConfigureAwait(false);
            if (done)
            {
                handled++;
                if (taken is not null)
                {
                    claimed.Add(taken);
                }
            }
        }

        using var gate = new SemaphoreSlim(options.MaxConcurrentEffects);
        await Task.WhenAll(claimed.Select(effect => RunEffectAsync(effect, gate, cancellationToken))).ConfigureAwait(false);
        return handled;
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

    private async Task RunEffectAsync(EffectRecord effect, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var trigger = await InvokeAsync(effect, cancellationToken).ConfigureAwait(false);
            await ApplyResultAsync(effect, trigger, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Invokes the port outside any transaction and turns what happened into the trigger of section 7.</summary>
    private async Task<ProcessTrigger> InvokeAsync(EffectRecord effect, CancellationToken cancellationToken)
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
        cancellationToken.ThrowIfCancellationRequested();

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
    /// and closes the row. It waits for the instance lock: the port has already been called, so the result must not be dropped
    /// merely because someone else holds the instance for a moment.
    /// </summary>
    private async Task ApplyResultAsync(EffectRecord effect, ProcessTrigger trigger, CancellationToken cancellationToken)
    {
        var item = new WorkRef(effect.TenantId, effect.InstanceId, effect.EffectId);
        await WithInstanceAsync(
            item,
            false,
            async (session, instance) =>
            {
                var current = await session.GetEffectAsync(instance.Key, effect.EffectId, cancellationToken).ConfigureAwait(false);
                if (current is not { Status: EffectStatus.Running } || current.LeaseOwner != owner)
                {
                    return true;
                }

                return await AdvanceEffectAsync(session, instance, effect.EffectId, trigger, runtime.Clock(instance.TenantId), cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }
}

using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>The engine: every command is one transaction of the shape described in the specification.</summary>
internal sealed class ProcessEngine : IProcessEngine
{
    private const string NotFound = "process.instance.not_found";
    private const string Stale = "process.trigger.stale";
    private const string Unavailable = "process.definition.unavailable";

    private readonly IProcessStore store;
    private readonly ProcessRuntime runtime;

    internal ProcessEngine(IProcessStore store, ProcessRuntime runtime)
    {
        this.store = store;
        this.runtime = runtime;
    }

    /// <inheritdoc />
    public async Task<StartResult> StartAsync(StartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guard.Id(request.TenantId, nameof(request.TenantId));
        Guard.Id(request.ProcessId, nameof(request.ProcessId));
        Guard.Id(request.BusinessKey, nameof(request.BusinessKey));
        Guard.JsonObject(request.InputJson, nameof(request.InputJson));
        var process = runtime.Latest(request.ProcessId) ?? throw new InvalidOperationException(Unavailable);
        var input = ParseInput(request.InputJson);

        var instanceId = Guid.NewGuid().ToString("N");
        var clock = runtime.Clock(request.TenantId);
        var session = await store.BeginAsync(cancellationToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
        {
            var inserted = await session.InsertInstanceAsync(
                new NewInstance(request.TenantId, instanceId, process.Id, process.Version, process.ContentHash, request.BusinessKey, null, null, clock.Now),
                cancellationToken).ConfigureAwait(false);
            if (!inserted.Inserted)
            {
                return new StartResult(inserted.InstanceId, false);
            }

            var instance = await session.LockInstanceAsync(new InstanceKey(request.TenantId, instanceId), false, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The instance row that this transaction inserted cannot be read.");
            var result = await runtime.StartAsync(session, instance, process, input, clock, cancellationToken).ConfigureAwait(false);
            if (result.Disposition != AdvanceDisposition.Applied)
            {
                throw new ArgumentException("The interpreter rejected the input: " + result.Code, nameof(request));
            }

            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new StartResult(instanceId, true);
        }
    }

    /// <inheritdoc />
    public async Task<PublishResult> PublishEventAsync(ProcessEvent processEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processEvent);
        Guard.Id(processEvent.TenantId, nameof(processEvent.TenantId));
        Guard.Id(processEvent.EventId, nameof(processEvent.EventId));
        ArgumentNullException.ThrowIfNull(processEvent.Name);
        Guard.JsonObject(processEvent.PayloadJson, nameof(processEvent.PayloadJson));
        var tenant = processEvent.TenantId;
        var now = runtime.Time.GetUtcNow();
        var session = await store.BeginAsync(cancellationToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
        {
            if (!await session.TryInsertEventAsync(tenant, processEvent.EventId, processEvent.Name, now, cancellationToken).ConfigureAwait(false))
            {
                return new PublishResult(PublishStatus.Duplicate, 0);
            }

            IReadOnlyList<string> targets = processEvent.InstanceId is not null
                ? [processEvent.InstanceId]
                : await session.FindWaitingInstancesAsync(tenant, processEvent.Name, processEvent.Correlation, cancellationToken).ConfigureAwait(false);

            var queued = 0;
            var payload = runtime.Protect(tenant, InboxPayloads.Event(processEvent.Name, processEvent.Correlation, processEvent.PayloadJson));
            foreach (var target in targets.OrderBy(id => id, StringComparer.Ordinal))
            {
                // The targets are locked in id order, so that two publishers can never wait for each other, and so that the inbox
                // rows of one instance are numbered in the order its lock was taken.
                var key = new InstanceKey(tenant, target);
                if (await session.LockInstanceAsync(key, false, cancellationToken).ConfigureAwait(false) is null)
                {
                    continue;
                }

                await session.AppendInboxAsync(key, InboxKind.Event, payload, now, cancellationToken).ConfigureAwait(false);
                queued++;
            }

            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
            return queued == 0 ? new PublishResult(PublishStatus.NoTarget, 0) : new PublishResult(PublishStatus.Queued, queued);
        }
    }

    /// <inheritdoc />
    public async Task<CommandResult> CompleteTaskAsync(string tenantId, string taskId, string outcome, string dataJson, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        Guard.Id(taskId, nameof(taskId));
        ArgumentNullException.ThrowIfNull(outcome);
        Guard.JsonObject(dataJson, nameof(dataJson));
        var instanceId = await store.FindTaskInstanceAsync(tenantId, taskId, cancellationToken).ConfigureAwait(false);
        if (instanceId is null)
        {
            return new CommandResult(AdvanceDisposition.Rejected, NotFound);
        }

        var key = new InstanceKey(tenantId, instanceId);
        return await RunCommandAsync(
            key,
            new TaskCompleted(taskId, outcome, dataJson),
            async (session, applied) =>
            {
                if (applied)
                {
                    await session.CloseTaskAsync(key, taskId, HumanTaskStatus.Completed, runtime.Time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                }
            },
            async session =>
            {
                var task = await session.GetTaskAsync(key, taskId, cancellationToken).ConfigureAwait(false);
                return task is { Status: HumanTaskStatus.Open };
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<CommandResult> CancelAsync(string tenantId, string instanceId, string reason, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        ArgumentNullException.ThrowIfNull(instanceId);
        ArgumentNullException.ThrowIfNull(reason);
        return RunCommandAsync(new InstanceKey(tenantId, instanceId), new CancelRequested(reason), null, null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CommandResult> ResumeAsync(string tenantId, string instanceId, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        ArgumentNullException.ThrowIfNull(instanceId);
        return RunCommandAsync(new InstanceKey(tenantId, instanceId), new ResumeRequested(), null, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ProcessInstanceInfo?> GetInstanceAsync(string tenantId, string instanceId, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        ArgumentNullException.ThrowIfNull(instanceId);
        var row = await store.GetInstanceAsync(tenantId, instanceId, cancellationToken).ConfigureAwait(false);
        return row is null ? null : runtime.ToInfo(row);
    }

    /// <inheritdoc />
    public async Task<ProcessInstanceInfo?> FindInstanceAsync(string tenantId, string processId, string businessKey, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        Guard.Id(processId, nameof(processId));
        Guard.Id(businessKey, nameof(businessKey));
        var row = await store.FindInstanceAsync(tenantId, processId, businessKey, cancellationToken).ConfigureAwait(false);
        return row is null ? null : runtime.ToInfo(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProcessHistoryEntry>> GetHistoryAsync(string tenantId, string instanceId, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        ArgumentNullException.ThrowIfNull(instanceId);
        var rows = await store.GetHistoryAsync(tenantId, instanceId, cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(row => new ProcessHistoryEntry(
            row.Sequence,
            row.At,
            row.Step,
            row.Entry,
            row.Attempt,
            row.Kind,
            row.Code,
            row.ProtectedData is null ? null : runtime.Unprotect(tenantId, row.ProtectedData)))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProcessTaskInfo>> ListOpenTasksAsync(string tenantId, string queue, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        ArgumentNullException.ThrowIfNull(queue);
        var rows = await store.ListOpenTasksAsync(tenantId, queue, cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(row => new ProcessTaskInfo(
            row.TenantId,
            row.TaskId,
            row.InstanceId,
            row.Step,
            row.Queue,
            row.Outcomes,
            runtime.Unprotect(tenantId, row.ProtectedShow),
            row.CreatedAt))];
    }

    private static JsonObject ParseInput(string inputJson)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(inputJson);
        }
        catch (ArgumentException)
        {
            // A JSON object with a duplicate member name is refused by the parser; the interpreter would reject it the same way.
            throw new ArgumentException("The value must be the text of a JSON object without duplicate member names.", nameof(inputJson));
        }

        return node as JsonObject ?? throw new ArgumentException("The value must be the text of a JSON object.", nameof(inputJson));
    }

    /// <summary>
    /// Section 4 for a command: lock the instance (waiting for it), check the precondition under the lock, advance, and let the
    /// caller update its own row (the task) when the trigger applied.
    /// </summary>
    private async Task<CommandResult> RunCommandAsync(
        InstanceKey key,
        ProcessTrigger trigger,
        Func<IProcessSession, bool, Task>? afterAdvance,
        Func<IProcessSession, Task<bool>>? precondition,
        CancellationToken cancellationToken)
    {
        var session = await store.BeginAsync(cancellationToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
        {
            var instance = await session.LockInstanceAsync(key, false, cancellationToken).ConfigureAwait(false);
            if (instance is null)
            {
                return new CommandResult(AdvanceDisposition.Rejected, NotFound);
            }

            if (precondition is not null && !await precondition(session).ConfigureAwait(false))
            {
                return new CommandResult(AdvanceDisposition.Ignored, Stale);
            }

            var state = runtime.ReadState(instance);
            if (state is null)
            {
                return new CommandResult(AdvanceDisposition.Ignored, Stale);
            }

            var process = runtime.Resolve(instance) ?? throw new InvalidOperationException(Unavailable);
            var result = await runtime.AdvanceAsync(session, instance, process, state, trigger, runtime.Clock(key.TenantId), cancellationToken).ConfigureAwait(false);
            if (afterAdvance is not null)
            {
                await afterAdvance(session, result.Disposition == AdvanceDisposition.Applied).ConfigureAwait(false);
            }

            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CommandResult(result.Disposition, result.Code);
        }
    }
}

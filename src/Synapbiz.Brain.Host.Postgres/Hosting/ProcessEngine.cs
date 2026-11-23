using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>The engine: every command is one transaction of the shape described in the specification.</summary>
internal sealed class ProcessEngine : IProcessEngine
{
    private const string NotFound = "process.instance.not_found";
    private const string Stale = "process.trigger.stale";
    private const string Unavailable = "process.definition.unavailable";
    private const string NotRoot = "process.purge.not_root";
    private const string NotFinished = "process.purge.not_finished";
    private const int MaxPageSize = 500;
    private const int MaxPurgeTrees = 500;
    private const int MaxPurgeEvents = 5000;

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

            // New instances come first and the waiting instances are locked afterwards, in every publisher, so that a publisher that
            // holds a lock on an instance never waits for the business key another publisher is inserting.
            var started = await QueueTriggerStartsAsync(session, processEvent, now, cancellationToken).ConfigureAwait(false);

            IReadOnlyList<WaitCandidate> targets = processEvent.InstanceId is not null
                ? [new WaitCandidate(processEvent.InstanceId, true)]
                : await session.FindWaitingInstancesAsync(tenant, processEvent.Name, processEvent.Correlation, cancellationToken).ConfigureAwait(false);

            var queued = 0;
            var payload = runtime.Protect(tenant, InboxPayloads.Event(processEvent.Name, processEvent.Correlation, processEvent.PayloadJson));
            foreach (var target in targets.OrderBy(candidate => candidate.InstanceId, StringComparer.Ordinal))
            {
                // The targets are locked in id order, so that two publishers can never wait for each other, and so that the inbox
                // rows of one instance are numbered in the order its lock was taken.
                var key = new InstanceKey(tenant, target.InstanceId);
                var instance = await session.LockInstanceAsync(key, false, cancellationToken).ConfigureAwait(false);
                if (instance is null || (!target.Exact && !WaitTakesTheEvent(instance, processEvent)))
                {
                    continue;
                }

                await session.AppendInboxAsync(key, InboxKind.Event, payload, now, cancellationToken).ConfigureAwait(false);
                queued++;
            }

            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
            return queued + started == 0 ? new PublishResult(PublishStatus.NoTarget, 0) : new PublishResult(PublishStatus.Queued, queued + started);
        }
    }

    /// <inheritdoc />
    public async Task<ActivationResult> ActivateAsync(ProcessActivation activation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activation);
        Guard.Id(activation.TenantId, nameof(activation.TenantId));
        Guard.Id(activation.ProcessId, nameof(activation.ProcessId));
        if (activation.Zone is not null)
        {
            Guard.Id(activation.Zone, nameof(activation.Zone));
            if (!DeliveryWindows.IsZoneIdShape(activation.Zone))
            {
                throw new ArgumentException("The Zone of the activation must be UTC or an id of the form Area/Location.", nameof(activation));
            }
        }

        Guard.JsonObject(activation.InputJson, nameof(activation.InputJson));
        var process = runtime.Latest(activation.ProcessId) ?? throw new InvalidOperationException(Unavailable);
        var input = ParseInput(activation.InputJson);

        // The interpreter's own rules for an input decide whether it is valid: Start is pure, and a rejected start names the code.
        var check = process.Start("activation-check", (JsonObject)input.DeepClone(), runtime.Clock(activation.TenantId));
        if (check.Disposition != AdvanceDisposition.Applied)
        {
            throw new ArgumentException("The activation input is not valid: " + check.Code, nameof(activation));
        }

        var now = runtime.Time.GetUtcNow();
        var schedules = ResolveSchedules(process, activation, now);
        var events = process.Triggers.OfType<EventTriggerDeclaration>()
            .Select(trigger => new EventTriggerRow(activation.TenantId, trigger.Event, process.Id, trigger.BusinessKeyMember, MapJson(trigger.Input)))
            .ToList();

        var key = new ActivationKey(activation.TenantId, process.Id);
        var session = await store.BeginAsync(cancellationToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
        {
            await session.LockActivationAsync(key, cancellationToken).ConfigureAwait(false);
            var existing = await session.GetActivationAsync(key, cancellationToken).ConfigureAwait(false);
            if (existing is not null && SameActivation(existing, process, activation, input))
            {
                HostLog.ActivationSaved(runtime.Logger, activation.TenantId, process.Id, process.Version, false);
                return new ActivationResult(process.Version, false);
            }

            // Reading the schedules locks them: a pump that is handling one finishes first, so the fire time that is kept is the one it left.
            var current = (await session.GetSchedulesForUpdateAsync(key, cancellationToken).ConfigureAwait(false)).ToDictionary(row => row.ScheduleId, StringComparer.Ordinal);
            await session.SaveActivationAsync(
                key,
                new ActivationRecord(activation.TenantId, process.Id, process.Version, process.ContentHash, activation.Zone, runtime.Protect(activation.TenantId, input.ToJsonString()), now),
                cancellationToken).ConfigureAwait(false);
            await session.ReplaceEventTriggersAsync(key, events, cancellationToken).ConfigureAwait(false);
            foreach (var schedule in schedules)
            {
                var keep = current.TryGetValue(schedule.ScheduleId, out var old) && old.Cron == schedule.Cron && old.Zone == schedule.Zone;
                await session.UpsertScheduleAsync(key, keep ? schedule with { NextFireAt = old!.NextFireAt } : schedule, cancellationToken).ConfigureAwait(false);
            }

            await session.DeleteSchedulesExceptAsync(key, [.. schedules.Select(schedule => schedule.ScheduleId)], cancellationToken).ConfigureAwait(false);
            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        HostLog.ActivationSaved(runtime.Logger, activation.TenantId, process.Id, process.Version, true);
        return new ActivationResult(process.Version, true);
    }

    /// <inheritdoc />
    public async Task<bool> DeactivateAsync(string tenantId, string processId, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        Guard.Id(processId, nameof(processId));
        var key = new ActivationKey(tenantId, processId);
        var session = await store.BeginAsync(cancellationToken).ConfigureAwait(false);
        bool removed;
        await using (session.ConfigureAwait(false))
        {
            await session.LockActivationAsync(key, cancellationToken).ConfigureAwait(false);
            await session.GetSchedulesForUpdateAsync(key, cancellationToken).ConfigureAwait(false);
            await session.DeleteSchedulesExceptAsync(key, [], cancellationToken).ConfigureAwait(false);
            await session.ReplaceEventTriggersAsync(key, [], cancellationToken).ConfigureAwait(false);
            removed = await session.DeleteActivationAsync(key, cancellationToken).ConfigureAwait(false);
            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        HostLog.ActivationRemoved(runtime.Logger, tenantId, processId, removed);
        return removed;
    }

    /// <summary>
    /// Queues the start of one instance for every event trigger of the tenant that names the event: the business key is read from
    /// the payload (a trigger whose key is missing or unusable, or whose payload cannot be read, starts nothing and is logged with ids
    /// only; see <see cref="TriggerInputs.BusinessKey"/>) and the input is the input of the activation with the mapped payload members
    /// laid over it. Nothing here throws because of what a payload holds. Returns the number of instances created.
    /// </summary>
    private async Task<int> QueueTriggerStartsAsync(IProcessSession session, ProcessEvent processEvent, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tenant = processEvent.TenantId;
        var triggers = await session.FindEventTriggersAsync(tenant, processEvent.Name, cancellationToken).ConfigureAwait(false);
        if (triggers.Count == 0)
        {
            return 0;
        }

        var payload = TriggerInputs.ParsePayload(processEvent.PayloadJson);
        var started = 0;
        foreach (var trigger in triggers)
        {
            var businessKey = payload is null ? null : TriggerInputs.BusinessKey(payload, trigger.BusinessKeyMember);
            if (businessKey is null)
            {
                HostLog.NoBusinessKey(runtime.Logger, tenant, processEvent.EventId, processEvent.Name, trigger.ProcessId);
                continue;
            }

            var input = TriggerInputs.EventInput(runtime.Unprotect(tenant, trigger.ProtectedInput), trigger.InputMapJson, payload!);
            var row = new NewInstance(tenant, Guid.NewGuid().ToString("N"), trigger.ProcessId, trigger.ProcessVersion, trigger.ProcessHash, businessKey, null, null, now);
            if (await runtime.QueueStartAsync(session, row, input.ToJsonString(), cancellationToken).ConfigureAwait(false))
            {
                started++;
            }
        }

        return started;
    }

    /// <summary>The schedule rows an activation needs: the zone of each schedule (its own, else the one of the activation) resolved, and its first occurrence after now.</summary>
    private List<ScheduleRow> ResolveSchedules(BrainProcess process, ProcessActivation activation, DateTimeOffset now)
    {
        var rows = new List<ScheduleRow>();
        foreach (var trigger in process.Triggers.OfType<ScheduleTriggerDeclaration>())
        {
            var zoneId = trigger.Zone ?? activation.Zone
                ?? throw new ArgumentException("The schedule '" + trigger.Id + "' declares no time zone and the activation supplies none.", nameof(activation));
            var zone = runtime.FindZone(zoneId)
                ?? throw new ArgumentException("The time zone of the schedule '" + trigger.Id + "' is not known to the host.", nameof(activation));
            if (!CronSchedule.TryParse(trigger.Cron, out var cron))
            {
                throw new InvalidOperationException("The cron expression of the schedule '" + trigger.Id + "' is not valid.");
            }

            rows.Add(new ScheduleRow(activation.TenantId, process.Id, trigger.Id, trigger.Cron, zoneId, trigger.Misfire, cron.Next(now, zone)));
        }

        return rows;
    }

    private bool SameActivation(ActivationRecord existing, BrainProcess process, ProcessActivation activation, JsonObject input) =>
        existing.ProcessVersion == process.Version
        && existing.ProcessHash == process.ContentHash
        && existing.Zone == activation.Zone
        && JsonNode.DeepEquals(JsonNode.Parse(runtime.Unprotect(existing.TenantId, existing.ProtectedInput)), input);

    private static string MapJson(IReadOnlyDictionary<string, string> map)
    {
        var json = new JsonObject();
        foreach (var (variable, member) in map.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            json[variable] = member;
        }

        return json.ToJsonString();
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
            session => session.CloseTaskAsync(key, taskId, HumanTaskStatus.Completed, runtime.Time.GetUtcNow(), cancellationToken),
            async session => await session.GetTaskAsync(key, taskId, cancellationToken).ConfigureAwait(false) is { Status: HumanTaskStatus.Open },
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

    /// <inheritdoc />
    public async Task<ProcessInstancePage> ListInstancesAsync(ProcessInstanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guard.Id(query.TenantId, nameof(query.TenantId));
        if (query.ProcessId is not null)
        {
            Guard.Id(query.ProcessId, nameof(query.ProcessId));
        }

        Guard.NoControlCharacters(query.TenantId, nameof(query.TenantId));
        Guard.NoControlCharacters(query.ProcessId, nameof(query.ProcessId));
        Guard.NoControlCharacters(query.Stage, nameof(query.Stage));
        Guard.InRange(query.Limit, 1, MaxPageSize, nameof(query.Limit));

        var after = query.After is null ? null : InstanceCursors.Parse(query.After);
        var statuses = query.Statuses is { Count: > 0 } ? query.Statuses.Select(status => (int)status).Distinct().ToArray() : null;

        // One row more than the page tells whether there is a next page.
        var rows = await store.ListSummariesAsync(
            new InstanceListFilter(query.TenantId, query.ProcessId, statuses, query.Stage, query.RootsOnly, query.NeedsAttention, query.Limit + 1, after),
            cancellationToken).ConfigureAwait(false);
        var page = rows.Take(query.Limit).ToList();
        var next = rows.Count > query.Limit ? InstanceCursors.Format(page[^1]) : null;
        return new ProcessInstancePage([.. page.Select(ToSummary)], next);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProcessInstanceCount>> CountInstancesAsync(string tenantId, string? processId, bool rootsOnly, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        if (processId is not null)
        {
            Guard.Id(processId, nameof(processId));
        }

        Guard.NoControlCharacters(tenantId, nameof(tenantId));
        Guard.NoControlCharacters(processId, nameof(processId));
        var rows = await store.CountInstancesAsync(tenantId, processId, rootsOnly, cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(row => new ProcessInstanceCount(row.ProcessId, (ProcessStatus)row.Status, row.Stage, row.Count))];
    }

    /// <inheritdoc />
    public async Task<ProcessCaseView?> GetCaseAsync(string tenantId, string instanceId, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        Guard.Id(instanceId, nameof(instanceId));
        Guard.NoControlCharacters(tenantId, nameof(tenantId));
        Guard.NoControlCharacters(instanceId, nameof(instanceId));
        var row = await store.GetCaseAsync(tenantId, instanceId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return new ProcessCaseView(
            ToSummary(row.Instance),
            [.. row.Descendants.Select(ToSummary)],
            [.. row.Tasks.Select(task => ToTaskInfo(tenantId, task))],
            row.NextTimerDue,
            row.Events);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProcessInstanceSummary>> ListChildrenAsync(string tenantId, string instanceId, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        Guard.Id(instanceId, nameof(instanceId));
        Guard.NoControlCharacters(tenantId, nameof(tenantId));
        Guard.NoControlCharacters(instanceId, nameof(instanceId));
        var rows = await store.ListChildrenAsync(tenantId, instanceId, cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(ToSummary)];
    }

    /// <inheritdoc />
    public async Task<PurgeResult> PurgeInstanceAsync(string tenantId, string instanceId, PurgeMode mode, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        Guard.Id(instanceId, nameof(instanceId));
        Guard.NoControlCharacters(tenantId, nameof(tenantId));
        Guard.NoControlCharacters(instanceId, nameof(instanceId));
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The value must be a defined purge mode.");
        }

        var session = await store.BeginAsync(cancellationToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
        {
            // The root row is locked first, waiting for a pump that is working on it: the rule of every transaction of the host.
            var root = await session.LockInstanceAsync(new InstanceKey(tenantId, instanceId), false, cancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                return new PurgeResult(PurgeStatus.NotFound, PurgeCounts.None);
            }

            if (root.ParentInstanceId is not null)
            {
                return new PurgeResult(PurgeStatus.Refused, PurgeCounts.None, NotRoot);
            }

            if (mode == PurgeMode.FinishedOnly && !StoredStatus.IsFinished(root.Status))
            {
                return new PurgeResult(PurgeStatus.Refused, PurgeCounts.None, NotFinished);
            }

            return new PurgeResult(PurgeStatus.Purged, await PurgeLockedTreeAsync(session, root.Key, cancellationToken).ConfigureAwait(false));
        }
    }

    /// <inheritdoc />
    public async Task<PurgeBatchResult> PurgeFinishedBeforeAsync(string tenantId, DateTimeOffset before, int limit, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        Guard.NoControlCharacters(tenantId, nameof(tenantId));
        Guard.InRange(limit, 1, MaxPurgeTrees, nameof(limit));
        var cutoff = ToStoredPrecision(before);
        var trees = 0;
        var counts = PurgeCounts.None;
        FinishedRoot? after = null;
        while (trees < limit)
        {
            var page = await store.ScanFinishedRootsAsync(tenantId, cutoff, limit, after, cancellationToken).ConfigureAwait(false);
            foreach (var candidate in page)
            {
                var purged = await TryPurgeFinishedRootAsync(tenantId, candidate.InstanceId, cutoff, cancellationToken).ConfigureAwait(false);
                if (purged is not null)
                {
                    trees++;
                    counts = counts.Plus(purged);
                    if (trees == limit)
                    {
                        break;
                    }
                }
            }

            if (page.Count < limit)
            {
                break;
            }

            after = page[^1];
        }

        var further = await store.ScanFinishedRootsAsync(tenantId, cutoff, 1, null, cancellationToken).ConfigureAwait(false);
        return new PurgeBatchResult(trees, counts, further.Count > 0);
    }

    /// <inheritdoc />
    public async Task<PurgeEventsResult> PurgeEventsBeforeAsync(string tenantId, DateTimeOffset before, int limit, CancellationToken cancellationToken = default)
    {
        Guard.Id(tenantId, nameof(tenantId));
        Guard.NoControlCharacters(tenantId, nameof(tenantId));
        Guard.InRange(limit, 1, MaxPurgeEvents, nameof(limit));
        var purged = await store.PurgeEventsAsync(tenantId, ToStoredPrecision(before), limit, cancellationToken).ConfigureAwait(false);
        return new PurgeEventsResult(purged.Deleted, purged.More);
    }

    /// <summary>The instant at the precision the database stores (a microsecond), so that the scan and the check under the lock compare the same value.</summary>
    private static DateTimeOffset ToStoredPrecision(DateTimeOffset moment) => new(moment.UtcTicks - (moment.UtcTicks % 10), TimeSpan.Zero);

    /// <summary>
    /// Purges one finished root found by a scan: its row is locked with <c>skip locked</c> (a tree that someone holds is left for a later
    /// call) and everything the scan said is checked again under the lock. Null when the tree was not purged.
    /// </summary>
    private async Task<PurgeCounts?> TryPurgeFinishedRootAsync(string tenantId, string rootId, DateTimeOffset before, CancellationToken cancellationToken)
    {
        var session = await store.BeginAsync(cancellationToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
        {
            var root = await session.LockInstanceAsync(new InstanceKey(tenantId, rootId), true, cancellationToken).ConfigureAwait(false);
            if (root is null || root.ParentInstanceId is not null || !StoredStatus.IsFinished(root.Status) || root.UpdatedAt >= before)
            {
                return null;
            }

            return await PurgeLockedTreeAsync(session, root.Key, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Locks the rest of the tree of a root the session holds, deletes the rows of every table (children first, instances last) and
    /// commits; the transaction either removes every row of the tree or none. Logs one line with the counts, after the commit.
    /// </summary>
    private async Task<PurgeCounts> PurgeLockedTreeAsync(IProcessSession session, InstanceKey root, CancellationToken cancellationToken)
    {
        await session.LockTreeAsync(root, cancellationToken).ConfigureAwait(false);
        var history = await session.DeleteTreeRowsAsync(root, PurgeTable.History, cancellationToken).ConfigureAwait(false);
        var timers = await session.DeleteTreeRowsAsync(root, PurgeTable.Timers, cancellationToken).ConfigureAwait(false);
        var waits = await session.DeleteTreeRowsAsync(root, PurgeTable.Waits, cancellationToken).ConfigureAwait(false);
        var tasks = await session.DeleteTreeRowsAsync(root, PurgeTable.Tasks, cancellationToken).ConfigureAwait(false);
        var effects = await session.DeleteTreeRowsAsync(root, PurgeTable.Effects, cancellationToken).ConfigureAwait(false);
        var inbox = await session.DeleteTreeRowsAsync(root, PurgeTable.Inbox, cancellationToken).ConfigureAwait(false);
        var instances = await session.DeleteTreeRowsAsync(root, PurgeTable.Instances, cancellationToken).ConfigureAwait(false);
        await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        var counts = new PurgeCounts(instances, history, timers, waits, tasks, effects, inbox);
        HostLog.InstancePurged(runtime.Logger, root.TenantId, root.InstanceId, counts);
        return counts;
    }

    private static ProcessInstanceSummary ToSummary(InstanceSummaryRecord row) => new(
        row.TenantId,
        row.InstanceId,
        row.ProcessId,
        row.ProcessVersion,
        row.BusinessKey,
        StoredStatus.ToPublic(row.Status),
        row.Stage,
        row.CurrentStep,
        row.Result,
        row.ProblemCode,
        row.ParentInstanceId,
        row.CreatedAt,
        row.UpdatedAt,
        row.OpenTasks,
        row.SuspendedDescendants);

    private ProcessTaskInfo ToTaskInfo(string tenantId, TaskRecord row) => new(
        row.TenantId,
        row.TaskId,
        row.InstanceId,
        row.Step,
        row.Queue,
        row.Outcomes,
        runtime.Unprotect(tenantId, row.ProtectedShow),
        row.CreatedAt);

    /// <summary>
    /// Asks the interpreter, without writing anything, whether the wait of an instance whose correlation matched only without regard
    /// to case consumes the event. A <c>Reference</c> correlation compares equal whatever its case, so the interpreter takes the event;
    /// a <c>String</c> correlation that differs in case is not taken (the interpreter rejects the event or only buffers it), and the
    /// instance is then not a target. When the definition is not available the question cannot be answered and the instance stays a
    /// target: the pump decides when the definition is back.
    /// </summary>
    private bool WaitTakesTheEvent(InstanceRecord instance, ProcessEvent processEvent)
    {
        var state = runtime.ReadState(instance);
        if (state is null)
        {
            return false;
        }

        var process = runtime.Resolve(instance);
        if (process is null)
        {
            return true;
        }

        var result = process.Advance(state, new EventReceived(processEvent.Name, processEvent.Correlation, processEvent.PayloadJson), runtime.Clock(instance.TenantId));
        return result.Disposition == AdvanceDisposition.Applied && !result.History.Any(entry => entry.Kind == ProcessHistoryKind.EventBuffered);
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
    /// caller update its own row (the task) when the trigger applied, before the instance's outstanding work is cleared.
    /// </summary>
    private async Task<CommandResult> RunCommandAsync(
        InstanceKey key,
        ProcessTrigger trigger,
        Func<IProcessSession, Task>? whenApplied,
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
            var result = await runtime.AdvanceAsync(
                session,
                instance,
                process,
                state,
                trigger,
                runtime.Clock(key.TenantId),
                cancellationToken,
                whenApplied is null ? null : () => whenApplied(session)).ConfigureAwait(false);

            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CommandResult(result.Disposition, result.Code);
        }
    }
}

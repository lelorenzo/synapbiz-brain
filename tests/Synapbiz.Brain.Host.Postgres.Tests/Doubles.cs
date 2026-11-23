using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>A catalog that tests can add to and remove from while a host runs.</summary>
internal sealed class TestCatalog : IProcessCatalog
{
    private readonly ConcurrentDictionary<string, BrainProcess> latest = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<BrainProcess> all = [];

    internal void Add(params BrainProcess[] processes)
    {
        foreach (var process in processes)
        {
            all.Add(process);
            latest.AddOrUpdate(process.Id, process, (_, current) => current.Version >= process.Version ? current : process);
        }
    }

    internal void Remove(string processId)
    {
        latest.TryRemove(processId, out _);
        var kept = all.Where(process => process.Id != processId).ToList();
        all.Clear();
        foreach (var process in kept)
        {
            all.Add(process);
        }
    }

    public BrainProcess? Latest(string processId) => latest.GetValueOrDefault(processId);

    public BrainProcess? Find(string processId, long version, string contentHash) =>
        all.FirstOrDefault(process => process.Id == processId && process.Version == version && process.ContentHash == contentHash);
}

/// <summary>A port that records every call and answers with a handler the test sets.</summary>
internal sealed class FakePort : IProcessPort
{
    private readonly ConcurrentQueue<PortCall> calls = new();

    internal FakePort(string name) => Name = name;

    public string Name { get; }

    /// <summary>What the port answers; by default it completes with an empty output.</summary>
    internal Func<PortCall, CancellationToken, Task<PortResult>> Handler { get; set; } = (_, _) => Task.FromResult<PortResult>(new PortCompleted("{}"));

    internal IReadOnlyList<PortCall> Calls => [.. calls];

    internal int CountFor(string effectId) => calls.Count(call => call.Request.EffectId == effectId);

    public Task<PortResult> InvokeAsync(PortCall portCall, CancellationToken cancellationToken)
    {
        calls.Enqueue(portCall);
        return Handler(portCall, cancellationToken);
    }
}

/// <summary>A protector that prefixes a marker and reverses the text, so that nothing readable is ever stored.</summary>
internal sealed class ReversingProtector : IPayloadProtector
{
    internal const string Marker = "PROT:";

    public string Protect(string tenantId, string plaintext) => Marker + tenantId + ":" + new string([.. plaintext.Reverse()]);

    public string Unprotect(string tenantId, string protectedText)
    {
        var prefix = Marker + tenantId + ":";
        return protectedText.StartsWith(prefix, StringComparison.Ordinal)
            ? new string([.. protectedText[prefix.Length..].Reverse()])
            : throw new InvalidOperationException("The text was not protected for this tenant.");
    }
}

/// <summary>One captured log record.</summary>
internal sealed record LogEntry(LogLevel Level, EventId EventId, string Message, IReadOnlyList<KeyValuePair<string, object?>> State, string? ExceptionText);

/// <summary>Collects every log record of every category.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> entries = new();

    internal IReadOnlyList<LogEntry> Entries => [.. entries];

    public ILogger CreateLogger(string categoryName) => new Capture(entries);

    public void Dispose()
    {
    }

    private sealed class Capture : ILogger
    {
        private readonly ConcurrentQueue<LogEntry> target;

        internal Capture(ConcurrentQueue<LogEntry> target) => this.target = target;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var pairs = state is IEnumerable<KeyValuePair<string, object?>> values ? [.. values] : new List<KeyValuePair<string, object?>>();
            target.Enqueue(new LogEntry(logLevel, eventId, formatter(state, exception), pairs, exception?.ToString()));
        }
    }
}

/// <summary>
/// A store that passes everything to the real one and lets a test act between a scan and the handling of what it found, or
/// replace what the scan found: the seam that simulates another session working at the same time.
/// </summary>
internal sealed class HookedStore : IProcessStore
{
    private readonly IProcessStore inner;
    private int sessions;
    private int commits;

    internal HookedStore(IProcessStore inner) => this.inner = inner;

    /// <summary>Runs after a scan and returns the items the pump gets to see.</summary>
    internal Func<WorkKind, IReadOnlyList<WorkRef>, Task<IReadOnlyList<WorkRef>>>? AfterScan { get; set; }

    /// <summary>Runs after a scan of finished roots, before the call looks at what it found: another session changes the rows in between.</summary>
    internal Func<IReadOnlyList<FinishedRoot>, Task>? AfterFinishedScan { get; set; }

    /// <summary>Runs after a walk of a tree inside a purge session, with the number of the walk in that session (1 is the walk before the locks, 2 the walk under them) and the ids it found.</summary>
    internal Func<int, IReadOnlyList<string>, Task>? AfterTreeRead { get; set; }

    /// <summary>Runs after a session locked one instance row with <c>LockInstanceAsync</c> (a publish locks its targets this way), with the row or null: the seam that holds a publisher's locks open.</summary>
    internal Func<InstanceKey, InstanceRecord?, Task>? AfterInstanceLock { get; set; }

    /// <summary>Runs after the locks of a purge attempt were taken, with the rows locked, before the tree is walked again.</summary>
    internal Func<IReadOnlyList<LockedInstance>, Task>? AfterLock { get; set; }

    /// <summary>The number of sessions this store opened.</summary>
    internal int Sessions => Volatile.Read(ref sessions);

    /// <summary>The number of sessions that were committed.</summary>
    internal int Commits => Volatile.Read(ref commits);

    internal void CountCommit() => Interlocked.Increment(ref commits);

    public async Task<IProcessSession> BeginAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref sessions);
        return new HookedSession(await inner.BeginAsync(cancellationToken), this);
    }

    public async Task<IReadOnlyList<WorkRef>> ScanAsync(WorkKind kind, DateTimeOffset now, int limit, WorkRef? after, CancellationToken cancellationToken)
    {
        var found = await inner.ScanAsync(kind, now, limit, after, cancellationToken);
        return AfterScan is null || after is not null ? found : await AfterScan(kind, found);
    }

    public Task<InstanceRecord?> GetInstanceAsync(string tenantId, string instanceId, CancellationToken cancellationToken) => inner.GetInstanceAsync(tenantId, instanceId, cancellationToken);

    public Task<InstanceRecord?> FindInstanceAsync(string tenantId, string processId, string businessKey, CancellationToken cancellationToken) => inner.FindInstanceAsync(tenantId, processId, businessKey, cancellationToken);

    public Task<IReadOnlyList<HistoryRecord>> GetHistoryAsync(string tenantId, string instanceId, CancellationToken cancellationToken) => inner.GetHistoryAsync(tenantId, instanceId, cancellationToken);

    public Task<IReadOnlyList<TaskRecord>> ListOpenTasksAsync(string tenantId, string queue, CancellationToken cancellationToken) => inner.ListOpenTasksAsync(tenantId, queue, cancellationToken);

    public Task<string?> FindTaskInstanceAsync(string tenantId, string taskId, CancellationToken cancellationToken) => inner.FindTaskInstanceAsync(tenantId, taskId, cancellationToken);

    public Task<IReadOnlyList<InstanceSummaryRecord>> ListSummariesAsync(InstanceListFilter filter, CancellationToken cancellationToken) => inner.ListSummariesAsync(filter, cancellationToken);

    public Task<IReadOnlyList<InstanceCountRecord>> CountInstancesAsync(string tenantId, string? processId, bool rootsOnly, CancellationToken cancellationToken) => inner.CountInstancesAsync(tenantId, processId, rootsOnly, cancellationToken);

    public Task<CaseRecord?> GetCaseAsync(string tenantId, string instanceId, CancellationToken cancellationToken) => inner.GetCaseAsync(tenantId, instanceId, cancellationToken);

    public Task<IReadOnlyList<InstanceSummaryRecord>> ListChildrenAsync(string tenantId, string instanceId, CancellationToken cancellationToken) => inner.ListChildrenAsync(tenantId, instanceId, cancellationToken);

    public async Task<IReadOnlyList<FinishedRoot>> ScanFinishedRootsAsync(string tenantId, DateTimeOffset before, int limit, FinishedRoot? after, CancellationToken cancellationToken)
    {
        var found = await inner.ScanFinishedRootsAsync(tenantId, before, limit, after, cancellationToken);
        if (AfterFinishedScan is not null)
        {
            await AfterFinishedScan(found);
        }

        return found;
    }

    public Task<EventPurge> PurgeEventsAsync(string tenantId, DateTimeOffset before, int limit, CancellationToken cancellationToken) => inner.PurgeEventsAsync(tenantId, before, limit, cancellationToken);
}

/// <summary>
/// A session that passes everything to the real one and lets a test act between two steps of a purge: after the walk of the tree,
/// and after the locks. The seam that simulates another transaction committing or changing rows in between.
/// </summary>
internal sealed class HookedSession : IProcessSession
{
    private readonly IProcessSession inner;
    private readonly HookedStore owner;
    private int walks;

    internal HookedSession(IProcessSession inner, HookedStore owner)
    {
        this.inner = inner;
        this.owner = owner;
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    public async Task<IReadOnlyList<string>> ReadTreeAsync(string tenantId, string rootId, CancellationToken cancellationToken)
    {
        var ids = await inner.ReadTreeAsync(tenantId, rootId, cancellationToken);
        walks++;
        if (owner.AfterTreeRead is not null)
        {
            await owner.AfterTreeRead(walks, ids);
        }

        return ids;
    }

    public async Task<IReadOnlyList<LockedInstance>> TryLockInstancesAsync(string tenantId, IReadOnlyList<string> instanceIds, CancellationToken cancellationToken)
    {
        var rows = await inner.TryLockInstancesAsync(tenantId, instanceIds, cancellationToken);
        if (owner.AfterLock is not null)
        {
            await owner.AfterLock(rows);
        }

        return rows;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        owner.CountCommit();
        await inner.CommitAsync(cancellationToken);
    }

    public async Task<InstanceRecord?> LockInstanceAsync(InstanceKey key, bool skipLocked, CancellationToken cancellationToken)
    {
        var row = await inner.LockInstanceAsync(key, skipLocked, cancellationToken);
        if (owner.AfterInstanceLock is not null)
        {
            await owner.AfterInstanceLock(key, row);
        }

        return row;
    }
    public Task<InsertedInstance> InsertInstanceAsync(NewInstance row, CancellationToken cancellationToken) => inner.InsertInstanceAsync(row, cancellationToken);
    public Task SaveInstanceAsync(InstanceKey key, InstanceWrite write, CancellationToken cancellationToken) => inner.SaveInstanceAsync(key, write, cancellationToken);
    public Task AppendHistoryAsync(InstanceKey key, IReadOnlyList<HistoryRow> rows, CancellationToken cancellationToken) => inner.AppendHistoryAsync(key, rows, cancellationToken);
    public Task<bool> DeleteTimerAsync(InstanceKey key, string timerId, CancellationToken cancellationToken) => inner.DeleteTimerAsync(key, timerId, cancellationToken);
    public Task InsertTimerAsync(InstanceKey key, string timerId, DateTimeOffset dueAt, CancellationToken cancellationToken) => inner.InsertTimerAsync(key, timerId, dueAt, cancellationToken);
    public Task DeleteWaitAsync(InstanceKey key, string waitId, CancellationToken cancellationToken) => inner.DeleteWaitAsync(key, waitId, cancellationToken);
    public Task InsertWaitAsync(InstanceKey key, string waitId, string eventName, string? correlation, CancellationToken cancellationToken) => inner.InsertWaitAsync(key, waitId, eventName, correlation, cancellationToken);
    public Task InsertTaskAsync(InstanceKey key, TaskRecord task, CancellationToken cancellationToken) => inner.InsertTaskAsync(key, task, cancellationToken);
    public Task<TaskRecord?> GetTaskAsync(InstanceKey key, string taskId, CancellationToken cancellationToken) => inner.GetTaskAsync(key, taskId, cancellationToken);
    public Task<bool> CloseTaskAsync(InstanceKey key, string taskId, HumanTaskStatus status, DateTimeOffset now, CancellationToken cancellationToken) => inner.CloseTaskAsync(key, taskId, status, now, cancellationToken);
    public Task InsertEffectAsync(InstanceKey key, EffectRecord effect, CancellationToken cancellationToken) => inner.InsertEffectAsync(key, effect, cancellationToken);
    public Task<EffectRecord?> GetEffectAsync(InstanceKey key, string effectId, CancellationToken cancellationToken) => inner.GetEffectAsync(key, effectId, cancellationToken);
    public Task ClaimEffectAsync(InstanceKey key, string effectId, DateTimeOffset leaseUntil, string leaseOwner, CancellationToken cancellationToken) => inner.ClaimEffectAsync(key, effectId, leaseUntil, leaseOwner, cancellationToken);
    public Task RequeueEffectAsync(InstanceKey key, string effectId, DateTimeOffset dueAt, CancellationToken cancellationToken) => inner.RequeueEffectAsync(key, effectId, dueAt, cancellationToken);
    public Task FinishEffectAsync(InstanceKey key, string effectId, EffectStatus status, CancellationToken cancellationToken) => inner.FinishEffectAsync(key, effectId, status, cancellationToken);
    public Task ClearWorkAsync(InstanceKey key, DateTimeOffset now, CancellationToken cancellationToken) => inner.ClearWorkAsync(key, now, cancellationToken);
    public Task DiscardPendingEffectsAsync(InstanceKey key, CancellationToken cancellationToken) => inner.DiscardPendingEffectsAsync(key, cancellationToken);
    public Task<InboxRecord?> GetInboxAsync(InstanceKey key, long inboxId, CancellationToken cancellationToken) => inner.GetInboxAsync(key, inboxId, cancellationToken);
    public Task<long?> FirstPendingInboxAsync(InstanceKey key, CancellationToken cancellationToken) => inner.FirstPendingInboxAsync(key, cancellationToken);
    public Task CompleteInboxAsync(InstanceKey key, long inboxId, InboxStatus status, string? code, CancellationToken cancellationToken) => inner.CompleteInboxAsync(key, inboxId, status, code, cancellationToken);
    public Task AppendInboxAsync(InstanceKey target, InboxKind kind, string protectedPayload, DateTimeOffset now, CancellationToken cancellationToken) => inner.AppendInboxAsync(target, kind, protectedPayload, now, cancellationToken);
    public Task<IReadOnlyList<string>> ListLiveChildrenAsync(InstanceKey parent, CancellationToken cancellationToken) => inner.ListLiveChildrenAsync(parent, cancellationToken);
    public Task<bool> TryInsertEventAsync(string tenantId, string eventId, string name, DateTimeOffset now, CancellationToken cancellationToken) => inner.TryInsertEventAsync(tenantId, eventId, name, now, cancellationToken);
    public Task<IReadOnlyList<WaitCandidate>> FindWaitingInstancesAsync(string tenantId, string eventName, string? correlation, CancellationToken cancellationToken) => inner.FindWaitingInstancesAsync(tenantId, eventName, correlation, cancellationToken);
    public Task LockActivationAsync(ActivationKey key, CancellationToken cancellationToken) => inner.LockActivationAsync(key, cancellationToken);
    public Task<ActivationRecord?> GetActivationAsync(ActivationKey key, CancellationToken cancellationToken) => inner.GetActivationAsync(key, cancellationToken);
    public Task SaveActivationAsync(ActivationKey key, ActivationRecord activation, CancellationToken cancellationToken) => inner.SaveActivationAsync(key, activation, cancellationToken);
    public Task<bool> DeleteActivationAsync(ActivationKey key, CancellationToken cancellationToken) => inner.DeleteActivationAsync(key, cancellationToken);
    public Task ReplaceEventTriggersAsync(ActivationKey key, IReadOnlyList<EventTriggerRow> rows, CancellationToken cancellationToken) => inner.ReplaceEventTriggersAsync(key, rows, cancellationToken);
    public Task<IReadOnlyList<ScheduleRow>> GetSchedulesForUpdateAsync(ActivationKey key, CancellationToken cancellationToken) => inner.GetSchedulesForUpdateAsync(key, cancellationToken);
    public Task UpsertScheduleAsync(ActivationKey key, ScheduleRow row, CancellationToken cancellationToken) => inner.UpsertScheduleAsync(key, row, cancellationToken);
    public Task DeleteSchedulesExceptAsync(ActivationKey key, IReadOnlyCollection<string> keep, CancellationToken cancellationToken) => inner.DeleteSchedulesExceptAsync(key, keep, cancellationToken);
    public Task<IReadOnlyList<EventTriggerRecord>> FindEventTriggersAsync(string tenantId, string eventName, CancellationToken cancellationToken) => inner.FindEventTriggersAsync(tenantId, eventName, cancellationToken);
    public Task<ScheduleRecord?> LockScheduleAsync(ScheduleKey key, bool skipLocked, CancellationToken cancellationToken) => inner.LockScheduleAsync(key, skipLocked, cancellationToken);
    public Task SaveScheduleAsync(ScheduleKey key, DateTimeOffset nextFireAt, CancellationToken cancellationToken) => inner.SaveScheduleAsync(key, nextFireAt, cancellationToken);
    public Task<int> DeleteTreeRowsAsync(InstanceKey root, IReadOnlyList<string> instanceIds, PurgeTable table, CancellationToken cancellationToken) => inner.DeleteTreeRowsAsync(root, instanceIds, table, cancellationToken);
}

/// <summary>A protector that stores text as it is, except that it refuses to read anything of one tenant once it is armed.</summary>
internal sealed class FailingProtector : IPayloadProtector
{
    internal const string Message = "a message that the host must not copy anywhere";

    private readonly string failingTenant;

    internal FailingProtector(string failingTenant) => this.failingTenant = failingTenant;

    internal bool Armed { get; set; }

    public string Protect(string tenantId, string plaintext) => plaintext;

    public string Unprotect(string tenantId, string protectedText) =>
        Armed && tenantId == failingTenant ? throw new InvalidOperationException(Message) : protectedText;
}

/// <summary>
/// The zones of the tests, built without a system time zone database: the resolver the host is given instead of the default one.
/// A test adds the ids it needs; an id nobody added is unknown to the host.
/// </summary>
internal sealed class TestZoneResolver : IZoneResolver
{
    private readonly ConcurrentDictionary<string, TimeZoneInfo> zones = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> asked = new();

    /// <summary>
    /// A zone of the United States pattern: UTC-5, one hour of daylight saving from 02:00 on the second Sunday of March (the clock
    /// jumps to 03:00) to 02:00 on the first Sunday of November (the clock goes back to 01:00). In 2027 the two transitions are on 14
    /// March and on 7 November.
    /// </summary>
    internal static TimeZoneInfo Eastern { get; } = CreateEastern();

    /// <summary>A zone with a fixed offset of +05:30 and no daylight saving.</summary>
    internal static TimeZoneInfo Fixed { get; } = TimeZoneInfo.CreateCustomTimeZone("Test/Fixed", new TimeSpan(5, 30, 0), "Fixed", "Fixed");

    internal TestZoneResolver()
    {
        Add("America/New_York", Eastern);
        Add("Test/Fixed", Fixed);
        Add("UTC", TimeZoneInfo.Utc);
    }

    internal void Add(string id, TimeZoneInfo zone) => zones[id] = zone;

    internal void Remove(string id) => zones.TryRemove(id, out _);

    internal void Clear() => zones.Clear();

    /// <summary>Every id the host asked this resolver for, in order: what a test checks to show that an id of the wrong shape never got this far.</summary>
    internal IReadOnlyCollection<string> Asked => [.. asked];

    public TimeZoneInfo? Find(string id)
    {
        asked.Enqueue(id);
        return zones.GetValueOrDefault(id);
    }

    private static TimeZoneInfo CreateEastern()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified), 3, 2, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("Test/Eastern", TimeSpan.FromHours(-5), "Eastern", "Eastern standard", "Eastern daylight", [rule]);
    }
}

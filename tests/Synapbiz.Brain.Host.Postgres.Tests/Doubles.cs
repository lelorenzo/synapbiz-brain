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

    internal HookedStore(IProcessStore inner) => this.inner = inner;

    /// <summary>Runs after a scan and returns the items the pump gets to see.</summary>
    internal Func<WorkKind, IReadOnlyList<WorkRef>, Task<IReadOnlyList<WorkRef>>>? AfterScan { get; set; }

    /// <summary>Runs after a scan of finished roots, before the call looks at what it found: another session changes the rows in between.</summary>
    internal Func<IReadOnlyList<FinishedRoot>, Task>? AfterFinishedScan { get; set; }

    public Task<IProcessSession> BeginAsync(CancellationToken cancellationToken) => inner.BeginAsync(cancellationToken);

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

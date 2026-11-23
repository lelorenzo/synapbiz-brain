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

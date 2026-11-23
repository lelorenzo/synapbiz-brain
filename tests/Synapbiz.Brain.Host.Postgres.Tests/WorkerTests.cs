using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>The hosted worker, driven by a scripted pump and a fake clock. Nothing here sleeps: the clock only moves when the test moves it.</summary>
public sealed class WorkerTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task AWorkerThatFoundWorkRunsAgainAtOnceAndOneThatFoundNothingWaitsThePollInterval()
    {
        var time = new FakeTimeProvider(Rig.Start);
        var pump = new ScriptedPump(time, 3, 2, 0, 0);
        using var world = new World(pump, time);
        var worker = world.Worker;
        var logs = world.Logs;

        await worker.StartAsync(CancellationToken.None);
        await pump.Reached(3).WaitAsync(Guard);
        await AdvanceUntil(time, pump.Reached(4));
        await worker.StopAsync(CancellationToken.None);

        pump.Moments[0].Should().Be(Rig.Start);
        pump.Moments[1].Should().Be(Rig.Start, "a pass that handled work is followed by another at once");
        pump.Moments[2].Should().Be(Rig.Start, "the second pass handled work too");
        pump.Moments[3].Should().BeOnOrAfter(Rig.Start + Poll, "a pass that handled nothing is followed by a wait of the poll interval");
        logs.Entries.Should().Contain(entry => entry.Message.StartsWith("process.worker.started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedPassIsLoggedWithTheExceptionTypeOnlyAndTheLoopGoesOn()
    {
        var time = new FakeTimeProvider(Rig.Start);
        var pump = new ScriptedPump(time, new InvalidOperationException("a message that the worker must not copy anywhere"), 0);
        using var world = new World(pump, time);
        var worker = world.Worker;
        var logs = world.Logs;

        await worker.StartAsync(CancellationToken.None);
        await pump.Reached(1).WaitAsync(Guard);
        await AdvanceUntil(time, pump.Reached(2));
        await worker.StopAsync(CancellationToken.None);

        var failure = logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error).Subject;
        failure.Message.Should().StartWith("process.worker.pass_failed").And.Contain("System.InvalidOperationException");
        logs.Entries.Should().OnlyContain(entry => !entry.Message.Contains("must not copy", StringComparison.Ordinal) && entry.ExceptionText == null);
        pump.Moments[1].Should().BeOnOrAfter(Rig.Start + Poll);
    }

    [Fact]
    public async Task StoppingTheWorkerCancelsThePassInFlightAndEndsTheLoop()
    {
        var time = new FakeTimeProvider(Rig.Start);
        var pump = new ScriptedPump(time, ScriptedPump.Block);
        using var world = new World(pump, time);
        var worker = world.Worker;

        await worker.StartAsync(CancellationToken.None);
        await pump.Reached(1).WaitAsync(Guard);
        await worker.StopAsync(CancellationToken.None).WaitAsync(Guard);

        pump.Cancelled.Should().BeTrue();
        pump.Calls.Should().Be(1);
    }

    [Fact]
    public async Task StoppingTheWorkerWhileItWaitsTheIntervalEndsTheLoopWithoutAnotherPass()
    {
        var time = new FakeTimeProvider(Rig.Start);
        var pump = new ScriptedPump(time, 0, 0);
        using var world = new World(pump, time);
        var worker = world.Worker;

        await worker.StartAsync(CancellationToken.None);
        await pump.Reached(1).WaitAsync(Guard);
        await worker.StopAsync(CancellationToken.None).WaitAsync(Guard);

        pump.Calls.Should().Be(1);
    }

    private sealed class World : IDisposable
    {
        private readonly ILoggerFactory factory;

        internal World(IProcessPump pump, TimeProvider time)
        {
            Logs = new CapturingLoggerProvider();
            factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
            Worker = new ProcessWorker(pump, Options.Create(new ProcessHostOptions { PollInterval = Poll }), time, factory.CreateLogger<ProcessWorker>());
        }

        internal CapturingLoggerProvider Logs { get; }

        internal ProcessWorker Worker { get; }

        public void Dispose()
        {
            Worker.Dispose();
            factory.Dispose();
            Logs.Dispose();
        }
    }

    /// <summary>Moves the clock by the poll interval, again and again, until the awaited event happened: the worker may not have started its wait when the first advance happens.</summary>
    private static async Task AdvanceUntil(FakeTimeProvider time, Task reached)
    {
        for (var attempt = 0; attempt < 100_000 && !reached.IsCompleted; attempt++)
        {
            time.Advance(Poll);
            await Task.Yield();
        }

        await reached.WaitAsync(Guard);
    }

    /// <summary>A pump that answers with a script: a number is the count of items handled, an exception is thrown, <see cref="Block"/> waits for cancellation. After the script it answers zero.</summary>
    private sealed class ScriptedPump : IProcessPump
    {
        internal const int Block = -1;

        private readonly FakeTimeProvider time;
        private readonly object[] script;
        private readonly List<TaskCompletionSource> reached = [];
        private readonly List<DateTimeOffset> moments = [];
        private readonly object gate = new();
        private int calls;

        internal ScriptedPump(FakeTimeProvider time, params object[] script)
        {
            this.time = time;
            this.script = script;
            for (var index = 0; index < 20; index++)
            {
                reached.Add(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            }
        }

        internal bool Cancelled { get; private set; }

        internal int Calls
        {
            get
            {
                lock (gate)
                {
                    return calls;
                }
            }
        }

        internal IReadOnlyList<DateTimeOffset> Moments
        {
            get
            {
                lock (gate)
                {
                    return [.. moments];
                }
            }
        }

        internal Task Reached(int count) => reached[count - 1].Task;

        public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            int index;
            lock (gate)
            {
                index = calls++;
                moments.Add(time.GetUtcNow());
            }

            reached[index].SetResult();
            var step = index < script.Length ? script[index] : 0;
            switch (step)
            {
                case Exception failure:
                    throw failure;
                case int handled when handled == Block:
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        Cancelled = true;
                        throw;
                    }

                    return 0;
                case int handled:
                    return handled;
                default:
                    return 0;
            }
        }
    }
}

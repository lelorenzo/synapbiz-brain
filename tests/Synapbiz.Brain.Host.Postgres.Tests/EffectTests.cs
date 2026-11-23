using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

public sealed class EffectTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(120);

    /// <summary>A port that simulates a crash of the pump on its first call: it cancels the pass and then answers, so that the answer is never applied.</summary>
    private static (FakePort Port, CancellationTokenSource Crash) CrashingOnce(Rig rig, string name)
    {
        var crash = new CancellationTokenSource();
        var port = rig.Port(name);
        var calls = 0;
        port.Handler = async (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                await crash.CancelAsync();
            }

            return new PortCompleted("{}");
        };
        return (port, crash);
    }

    [Fact]
    public async Task ARetryWaitsForItsDelayAndKeepsTheIdempotencyKey()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("retry-read"));
        var read = rig.Port("t.read");
        var calls = 0;
        read.Handler = (_, _) => Task.FromResult<PortResult>(Interlocked.Increment(ref calls) == 1
            ? new PortFailed(ProcessErrorKind.Transient, "x.busy")
            : new PortCompleted("{\"amount\": 3}"));
        var id = await rig.StartAsync("host.retry", "k-1");

        await rig.DrainAsync();
        read.Calls.Should().HaveCount(1);
        (await rig.CountAsync("process_timer", "instance_id = @i", ("i", id))).Should().Be(1);

        rig.Time.Advance(TimeSpan.FromSeconds(59));
        (await rig.DrainAsync()).Should().Be(0);
        read.Calls.Should().HaveCount(1);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await rig.DrainAsync();

        read.Calls.Should().HaveCount(2);
        read.Calls[0].Request.Attempt.Should().Be(1);
        read.Calls[1].Request.Attempt.Should().Be(2);
        read.Calls[1].Request.EffectId.Should().NotBe(read.Calls[0].Request.EffectId);
        read.Calls[1].Request.IdempotencyKey.Should().Be(read.Calls[0].Request.IdempotencyKey);
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Result.Should().Be("ok");
        info.State!.Variables["amount"].Should().Be("3");
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task AFailureKindWithItsOwnRouteIsRoutedByTheInterpreter()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("retry-read"));
        rig.Port("t.read").Handler = (_, _) => Task.FromResult<PortResult>(new PortFailed(ProcessErrorKind.NotAvailable, "x.off"));
        var id = await rig.StartAsync("host.retry", "k-1");

        await rig.DrainAsync();

        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("unavailable");
    }

    [Fact]
    public async Task ARateLimitedPortIsRetriedNoEarlierThanItsRetryAfter()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("retry-read"));
        var read = rig.Port("t.read");
        var calls = 0;
        read.Handler = (_, _) => Task.FromResult<PortResult>(Interlocked.Increment(ref calls) == 1
            ? new PortFailed(ProcessErrorKind.RateLimited, "x.slow", TimeSpan.FromMinutes(5))
            : new PortCompleted("{}"));
        await rig.StartAsync("host.retry", "k-1");

        await rig.DrainAsync();
        rig.Time.Advance(TimeSpan.FromMinutes(4));
        await rig.DrainAsync();
        read.Calls.Should().HaveCount(1);

        rig.Time.Advance(TimeSpan.FromMinutes(1));
        await rig.DrainAsync();
        read.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnExpiredLeaseOfAtLeastOnceDeliveryIsInvokedAgainWithTheSameKey()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("repeat"));
        var (port, crash) = CrashingOnce(rig, "t.send");
        var id = await rig.StartAsync("host.repeat", "k-1");

        var pass = () => rig.Pump.RunOnceAsync(crash.Token);
        await pass.Should().ThrowAsync<OperationCanceledException>();
        (await rig.ColumnAsync<short>("select status from {schema}.process_effect")).Should().Equal((short)2);

        rig.Time.Advance(Lease - TimeSpan.FromSeconds(1));
        (await rig.DrainAsync()).Should().Be(0);
        port.Calls.Should().HaveCount(1);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await rig.DrainAsync();

        port.Calls.Should().HaveCount(2);
        port.Calls[1].Request.EffectId.Should().Be(port.Calls[0].Request.EffectId);
        port.Calls[1].Request.IdempotencyKey.Should().Be(port.Calls[0].Request.IdempotencyKey);
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Result.Should().Be("sent");
        (await rig.ColumnAsync<short>("select status from {schema}.process_effect")).Should().Equal((short)3);
        rig.Logs.Entries.Should().Contain(entry => entry.Message.StartsWith("process.effect.lease_expired", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnExpiredLeaseOfAtMostOnceDeliveryIsNeverInvokedAgainAndTheFlowTakesOnUnknown()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("delivery"));
        var (port, crash) = CrashingOnce(rig, "t.send");
        var id = await rig.StartAsync("host.delivery", "k-1");

        var pass = () => rig.Pump.RunOnceAsync(crash.Token);
        await pass.Should().ThrowAsync<OperationCanceledException>();
        rig.Time.Advance(Lease);
        await rig.DrainAsync();
        rig.Time.Advance(TimeSpan.FromHours(1));
        await rig.DrainAsync();

        port.Calls.Should().HaveCount(1);
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Result.Should().Be("unknown");
        (await rig.Engine.GetHistoryAsync(rig.Tenant, id)).Should().Contain(entry => entry.Kind == ProcessHistoryKind.EffectFailed && entry.Code == "process.effect.outcome_unknown");
        (await rig.ColumnAsync<short>("select status from {schema}.process_effect")).Should().Equal((short)3);
    }

    [Theory]
    [InlineData("repeat", "t.send", "process.port.exception", "failed")]
    [InlineData("delivery", "t.send", "process.effect.outcome_unknown", "unknown")]
    public async Task APortThatThrowsIsAFailureOfTheKindTheDeliveryModeDecides(string process, string port, string code, string result)
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host(process));
        rig.Port(port).Handler = (_, _) => throw new InvalidOperationException("a message that the host must not copy anywhere");
        var id = await rig.StartAsync("host." + process, "k-1");

        await rig.DrainAsync();

        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be(result);
        (await rig.Engine.GetHistoryAsync(rig.Tenant, id)).Should().Contain(entry => entry.Kind == ProcessHistoryKind.EffectFailed && entry.Code == code);
        rig.Logs.Entries.Should().Contain(entry => entry.Message.StartsWith("process.port.exception", StringComparison.Ordinal));
        rig.Logs.Entries.Should().NotContain(entry => entry.Message.Contains("must not copy", StringComparison.Ordinal) || (entry.ExceptionText ?? "").Contains("must not copy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APortThatAnswersWithNothingIsAFailure()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("repeat"));
        rig.Port("t.send").Handler = (_, _) => Task.FromResult<PortResult>(null!);
        var id = await rig.StartAsync("host.repeat", "k-1");

        await rig.DrainAsync();

        (await rig.Engine.GetHistoryAsync(rig.Tenant, id)).Should().Contain(entry => entry.Code == "process.port.exception");
    }

    [Theory]
    [InlineData("repeat", "process.port.timeout", "failed", false)]
    [InlineData("repeat", "process.port.timeout", "failed", true)]
    [InlineData("delivery", "process.effect.outcome_unknown", "unknown", false)]
    [InlineData("delivery", "process.effect.outcome_unknown", "unknown", true)]
    public async Task APortThatDoesNotAnswerInTimeIsAFailureWhetherOrNotItHonoursItsToken(string process, string code, string result, bool ignoresToken)
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host(process));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<PortResult>();
        rig.Port("t.send").Handler = async (_, token) =>
        {
            started.SetResult();
            if (ignoresToken)
            {
                return await never.Task;
            }

            await Task.Delay(Timeout.Infinite, token);
            return new PortCompleted("{}");
        };
        var id = await rig.StartAsync("host." + process, "k-1");

        var pass = rig.Pump.RunOnceAsync();
        await started.Task;
        rig.Time.Advance(TimeSpan.FromSeconds(60));
        await pass;
        await rig.DrainAsync();

        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be(result);
        (await rig.Engine.GetHistoryAsync(rig.Tenant, id)).Should().Contain(entry => entry.Kind == ProcessHistoryKind.EffectFailed && entry.Code == code);
        rig.Logs.Entries.Should().Contain(entry => entry.Message.StartsWith("process.port.timeout", StringComparison.Ordinal));
        never.SetResult(new PortCompleted("{}"));
    }

    [Fact]
    public async Task APortThatIsNotRegisteredFailsTheEffectAsNotAvailableWithoutInvokingAnything()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("repeat"));
        var other = rig.Port("t.other");
        var id = await rig.StartAsync("host.repeat", "k-1");

        await rig.DrainAsync();

        other.Calls.Should().BeEmpty();
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("failed");
        (await rig.Engine.GetHistoryAsync(rig.Tenant, id)).Should().Contain(entry => entry.Kind == ProcessHistoryKind.EffectFailed && entry.Code == "process.port.not_registered");
        (await rig.ColumnAsync<short>("select status from {schema}.process_effect")).Should().Equal((short)3);
    }

    [Fact]
    public async Task TheEffectOfACancelledInstanceIsDiscardedAndNeverInvoked()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("repeat"));
        var port = rig.Port("t.send");
        var id = await rig.StartAsync("host.repeat", "k-1");

        var cancelled = await rig.Engine.CancelAsync(rig.Tenant, id, "operator");
        await rig.DrainAsync();

        cancelled.Disposition.Should().Be(AdvanceDisposition.Applied);
        port.Calls.Should().BeEmpty();
        (await rig.ColumnAsync<short>("select status from {schema}.process_effect")).Should().Equal((short)4);
    }

    [Fact]
    public async Task AnEffectTheInstanceNoLongerWaitsForIsDiscardedAtClaimTime()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("repeat"));
        var port = rig.Port("t.send");
        var id = await rig.StartAsync("host.repeat", "k-1");
        await rig.ExecuteAsync(
            "insert into {schema}.process_effect (tenant_id, effect_id, instance_id, step, idempotency_key, kind, port, input, attempt, delivery, status, due_at) values (@t, @e, @i, 'send', 'k', 1, 't.send', '{}', 1, 0, 1, @d)",
            ("t", rig.Tenant),
            ("e", id + "/send/9/1"),
            ("i", id),
            ("d", Rig.Start));
        await rig.ExecuteAsync("update {schema}.process_effect set due_at = @d where effect_id <> @e", ("d", Rig.Start.AddDays(1)), ("e", id + "/send/9/1"));

        await rig.DrainAsync();

        port.Calls.Should().BeEmpty();
        (await rig.ScalarAsync<short>("select status from {schema}.process_effect where effect_id = @e", ("e", id + "/send/9/1"))).Should().Be(4);
        (await rig.ScalarAsync<short>("select status from {schema}.process_effect where effect_id <> @e", ("e", id + "/send/9/1"))).Should().Be(1);
    }

    [Fact]
    public async Task AResultIsDroppedWhenTheRowIsNoLongerRunningWithThisOwnerAndALaterPassRecoversIt()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("repeat"));
        var port = rig.Port("t.send");
        var calls = 0;
        port.Handler = async (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                await rig.ExecuteAsync("update {schema}.process_effect set lease_owner = 'someone-else'");
            }

            return new PortCompleted("{}");
        };
        var id = await rig.StartAsync("host.repeat", "k-1");

        await rig.DrainAsync();
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Status.Should().Be(ProcessStatus.Waiting);

        rig.Time.Advance(Lease);
        await rig.DrainAsync();

        port.Calls.Should().HaveCount(2);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("sent");
    }

    [Fact]
    public async Task NoMoreThanMaxConcurrentEffectsRunAtOnce()
    {
        await using var rig = await Rig.CreateAsync(configureHost: options => options.MaxConcurrentEffects = 2);
        rig.Catalog.Add(Fixtures.Host("repeat"));
        var active = 0;
        var peak = 0;
        rig.Port("t.send").Handler = async (_, _) =>
        {
            var now = Interlocked.Increment(ref active);
            InterlockedMax(ref peak, now);
            await Task.Yield();
            await Task.Yield();
            Interlocked.Decrement(ref active);
            return new PortCompleted("{}");
        };
        for (var index = 0; index < 8; index++)
        {
            await rig.StartAsync("host.repeat", "k-" + index);
        }

        await rig.DrainAsync();

        peak.Should().BeInRange(1, 2);
        (await rig.CountAsync("process_instance", "status = 4")).Should().Be(8);
    }

    [Fact]
    public async Task EachEffectOfABatchIsClaimedByOnePumpOnly()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("repeat"));
        var port = rig.Port("t.send");
        for (var index = 0; index < 10; index++)
        {
            await rig.StartAsync("host.repeat", "k-" + index);
        }

        var pumps = new[] { rig.Pump, rig.NewPump() };
        await Task.WhenAll(pumps.Select(pump => Task.Run(() => Rig.DrainAsync(pump))));

        port.Calls.Select(call => call.Request.EffectId).Should().OnlyHaveUniqueItems().And.HaveCount(10);
    }

    [Fact]
    public void TwoPortsWithTheSameNameAreRefused()
    {
        var twice = () => new ProcessPump(null!, null!, [new FakePort("p"), new FakePort("p")], Microsoft.Extensions.Options.Options.Create(new ProcessHostOptions()));

        twice.Should().Throw<ArgumentException>();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
        }
        while (value > current && Interlocked.CompareExchange(ref target, value, current) != current);
    }
}

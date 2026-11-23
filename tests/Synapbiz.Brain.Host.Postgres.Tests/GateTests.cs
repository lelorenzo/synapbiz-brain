using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;
using Xunit.Abstractions;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// W1-G, G2: the gate of a delivery window that an event can interrupt. The gate stores its window timer and its wait row in the
/// same step, an event that arrives while the gate is pending ends the step without a call of the port, and the opening of the window
/// ends the wait. The window is Monday to Friday, 09:00 to 18:00 in a zone of the United States pattern.
/// </summary>
public sealed class GateTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset MondayEvening = new(2027, 1, 4, 23, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TuesdayOpening = new(2027, 1, 5, 14, 0, 0, TimeSpan.Zero);

    private static ProcessEvent Reply(string eventId, string correlation = "c-1", string? instanceId = null, string tenant = "t-1", string payload = "{\"text\":\"hello\"}") =>
        new(tenant, eventId, "reply", correlation, payload, instanceId);

    private static async Task<(Rig Rig, List<DateTimeOffset> Calls)> CreateAsync()
    {
        var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("window-interrupt"));
        var calls = new List<DateTimeOffset>();
        rig.Port("t.send").Handler = (_, _) =>
        {
            lock (calls)
            {
                calls.Add(rig.Time.GetUtcNow());
            }

            return Task.FromResult<PortResult>(new PortCompleted("{}"));
        };
        return (rig, calls);
    }

    [Fact]
    public async Task TheGateStoresItsWindowTimerAndItsWaitRowInOneStep()
    {
        var (rig, calls) = await CreateAsync();
        await using var _ = rig;
        rig.Time.SetUtcNow(MondayEvening);

        var id = await rig.StartAsync("host.gate", "k-1");
        await rig.DrainAsync();

        calls.Should().BeEmpty();
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.State!.Pending!.Kind.Should().Be(ProcessPendingKind.Window);
        info.State.Pending.InterruptWaitId.Should().Be(id + "/send/1/1/wait");
        (await rig.ColumnAsync<string>("select timer_id from {schema}.process_timer")).Should().Equal(id + "/send/1/1/window");
        (await rig.ColumnAsync<DateTimeOffset>("select due_at from {schema}.process_timer")).Should().Equal(TuesdayOpening);
        (await rig.ColumnAsync<string>("select wait_id from {schema}.process_wait")).Should().Equal(id + "/send/1/1/wait");
        (await rig.ScalarAsync<string>("select event from {schema}.process_wait")).Should().Be("reply");
        (await rig.ScalarAsync<string>("select correlation from {schema}.process_wait")).Should().Be("c-1");
    }

    [Fact]
    public async Task AnEventBeforeTheWindowOpensEndsTheStepWithoutACallAndLeavesNoTimerOrWait()
    {
        var (rig, calls) = await CreateAsync();
        await using var _ = rig;
        rig.Time.SetUtcNow(MondayEvening);
        var id = await rig.StartAsync("host.gate", "k-1");
        await rig.DrainAsync();

        var published = await rig.Engine.PublishEventAsync(Reply("e-1"));
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Status.Should().Be(ProcessStatus.Completed);
        info.Result.Should().Be("interrupted");
        info.State!.Variables["answer"].Should().Be("\"hello\"");
        calls.Should().BeEmpty();
        (await rig.CountAsync("process_timer")).Should().Be(0);
        (await rig.CountAsync("process_wait")).Should().Be(0);
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
        (await rig.Engine.GetHistoryAsync(rig.Tenant, id)).Select(entry => entry.Kind)
            .Should().ContainInOrder(ProcessHistoryKind.WindowWaiting, ProcessHistoryKind.WaitStarted, ProcessHistoryKind.EventConsumed, ProcessHistoryKind.WindowInterrupted)
            .And.NotContain(ProcessHistoryKind.WindowOpened);

        rig.Time.SetUtcNow(TuesdayOpening);
        await rig.DrainAsync();
        calls.Should().BeEmpty("the window timer is gone, so the opening sends nothing");
    }

    [Fact]
    public async Task AnEventAfterTheOpeningDoesNotInterruptAndIsBufferedWhileTheEffectIsOutstanding()
    {
        var (rig, calls) = await CreateAsync();
        await using var _ = rig;
        rig.Time.SetUtcNow(MondayEvening);
        var id = await rig.StartAsync("host.gate", "k-1");
        await rig.DrainAsync();
        var second = rig.NewPump();
        rig.Ports.Single().Handler = async (_, token) =>
        {
            calls.Add(rig.Time.GetUtcNow());
            (await rig.Engine.PublishEventAsync(Reply("e-1", instanceId: id), token)).Status.Should().Be(PublishStatus.Queued);
            await Rig.DrainAsync(second);
            return new PortCompleted("{}");
        };

        rig.Time.SetUtcNow(TuesdayOpening);
        await rig.DrainAsync();

        calls.Should().Equal(TuesdayOpening);
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Status.Should().Be(ProcessStatus.Completed);
        info.Result.Should().Be("sent", "once the window has opened the event no longer interrupts");
        (await rig.CountAsync("process_wait")).Should().Be(0);
        (await rig.CountAsync("process_timer")).Should().Be(0);
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
        (await rig.CountAsync("process_inbox", "kind = 2 and status = 2 and code is null")).Should().Be(1, "the event was applied while the effect was outstanding and kept in the inbox of the instance");
        info.State!.Inbox.Select(item => item.PayloadJson).Should().Equal("{\"text\":\"hello\"}");
        (await rig.Engine.GetHistoryAsync(rig.Tenant, id)).Select(entry => entry.Kind)
            .Should().ContainInOrder(ProcessHistoryKind.WindowOpened, ProcessHistoryKind.EffectRequested).And.NotContain(ProcessHistoryKind.WindowInterrupted);
    }

    [Fact]
    public async Task CancellingTheInstanceWhileTheGateIsPendingRemovesTheTimerAndTheWait()
    {
        var (rig, _) = await CreateAsync();
        await using var _ = rig;
        rig.Time.SetUtcNow(MondayEvening);
        var id = await rig.StartAsync("host.gate", "k-1");
        await rig.DrainAsync();
        (await rig.CountAsync("process_wait")).Should().Be(1);

        var result = await rig.Engine.CancelAsync(rig.Tenant, id, "operator");
        await rig.DrainAsync();

        result.Disposition.Should().Be(AdvanceDisposition.Applied);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Status.Should().Be(ProcessStatus.Cancelled);
        (await rig.CountAsync("process_timer")).Should().Be(0);
        (await rig.CountAsync("process_wait")).Should().Be(0);
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task TheWaitOfAGateBelongsToItsTenant()
    {
        var (rig, calls) = await CreateAsync();
        await using var _ = rig;
        rig.Time.SetUtcNow(MondayEvening);
        var a = await rig.StartAsync("host.gate", "k-1", tenant: "tenant-a");
        var b = await rig.StartAsync("host.gate", "k-1", tenant: "tenant-b");
        await rig.DrainAsync();

        var published = await rig.Engine.PublishEventAsync(Reply("e-1", tenant: "tenant-a"));
        var other = await rig.Engine.PublishEventAsync(Reply("e-2", tenant: "tenant-c"));
        await rig.DrainAsync();

        published.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        other.Status.Should().Be(PublishStatus.NoTarget);
        (await rig.Engine.GetInstanceAsync("tenant-a", a))!.Result.Should().Be("interrupted");
        var waiting = await rig.Engine.GetInstanceAsync("tenant-b", b);
        waiting!.Status.Should().Be(ProcessStatus.Waiting);
        waiting.State!.Pending!.Kind.Should().Be(ProcessPendingKind.Window);
        (await rig.CountAsync("process_wait", "tenant_id = 'tenant-b'")).Should().Be(1);
        (await rig.CountAsync("process_wait", "tenant_id = 'tenant-a'")).Should().Be(0);
        (await rig.CountAsync("process_timer", "tenant_id = 'tenant-b'")).Should().Be(1);
        (await rig.CountAsync("process_timer", "tenant_id = 'tenant-a'")).Should().Be(0);
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AnEventAndTheOpeningOfTheWindowAtTheSameTimeEndInExactlyOneOutcomeAndNeverDeadlock()
    {
        var (rig, calls) = await CreateAsync();
        await using var _ = rig;
        var pumps = new[] { rig.Pump, rig.NewPump() };
        var interrupted = 0;
        var sent = 0;
        var saturday = new DateTimeOffset(2027, 1, 9, 12, 0, 0, TimeSpan.Zero);
        for (var iteration = 0; iteration < 200; iteration++)
        {
            rig.Time.SetUtcNow(saturday + TimeSpan.FromDays(iteration * 7));
            var id = await rig.StartAsync("host.gate", "k-" + iteration);
            await rig.DrainAsync();
            var due = await rig.ScalarAsync<DateTimeOffset>("select due_at from {schema}.process_timer where instance_id = @i", ("i", id));
            rig.Time.SetUtcNow(due);
            var before = calls.Count;
            var pump = pumps[iteration % 2];

            var gate = new TaskCompletionSource();
            var lead = iteration % 40;
            var publish = Task.Run(async () =>
            {
                await gate.Task;
                for (var step = 0; step < lead * 25; step++)
                {
                    await Task.Yield();
                }

                return await rig.Engine.PublishEventAsync(Reply("e-" + iteration, instanceId: id));
            });
            var fire = Task.Run(async () =>
            {
                await gate.Task;
                return await pump.RunOnceAsync();
            });
            gate.SetResult();
            await publish.WaitAsync(TimeSpan.FromMinutes(2));
            await fire.WaitAsync(TimeSpan.FromMinutes(2));
            await rig.DrainAsync();

            var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
            info!.Status.Should().Be(ProcessStatus.Completed);
            (await rig.OutstandingWorkAsync(id)).Should().Be(0);
            (await rig.CountAsync("process_wait", "instance_id = @i", ("i", id))).Should().Be(0);
            (await rig.CountAsync("process_timer", "instance_id = @i", ("i", id))).Should().Be(0);
            if (info.Result == "interrupted")
            {
                calls.Count.Should().Be(before, "an interrupted gate never calls the port");
                info.State!.Variables["answer"].Should().Be("\"hello\"");
                interrupted++;
            }
            else
            {
                info.Result.Should().Be("sent");
                calls.Count.Should().Be(before + 1, "the port is called exactly once when the window wins");
                sent++;
            }
        }

        output.WriteLine($"interrupted: {interrupted}, window opened first: {sent}");
        (interrupted + sent).Should().Be(200, "a PostgreSQL deadlock (40P01) or any other error would have thrown above");
    }
}

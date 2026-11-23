using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>When the catalog lacks the definition an instance is pinned to, the item is left untouched, a warning is logged, and a later pass applies it.</summary>
public sealed class DefinitionUnavailableTests
{
    private static bool IsWarning(LogEntry entry) =>
        entry.Message.StartsWith("process.definition.unavailable", StringComparison.Ordinal) && entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning;

    [Fact]
    public async Task ADueTimerIsLeftUntilTheDefinitionComesBack()
    {
        await using var rig = await Rig.CreateAsync();
        var process = Fixtures.Host("read-timer-action");
        rig.Catalog.Add(process);
        rig.Port("t.read").Handler = (_, _) => Task.FromResult<PortResult>(new PortCompleted("{\"amount\": 1}"));
        var act = rig.Port("t.act");
        var id = await rig.StartAsync("host.rta", "k-1");
        await rig.DrainAsync();
        rig.Catalog.Remove("host.rta");
        rig.Time.Advance(TimeSpan.FromSeconds(30));

        (await rig.DrainAsync()).Should().Be(0);

        (await rig.CountAsync("process_timer", "instance_id = @i", ("i", id))).Should().Be(1, "the timer row was not consumed");
        rig.Logs.Entries.Should().Contain(entry => IsWarning(entry));
        rig.Catalog.Add(process);
        await rig.DrainAsync();
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("ok");
        act.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task APendingEffectIsNotClaimedWhileTheDefinitionIsMissing()
    {
        await using var rig = await Rig.CreateAsync();
        var process = Fixtures.Host("repeat");
        rig.Catalog.Add(process);
        var port = rig.Port("t.send");
        var id = await rig.StartAsync("host.repeat", "k-1");
        rig.Catalog.Remove("host.repeat");

        (await rig.DrainAsync()).Should().Be(0);

        port.Calls.Should().BeEmpty("a port is never called for a result the host could not apply");
        (await rig.ColumnAsync<short>("select status from {schema}.process_effect")).Should().Equal((short)1);
        rig.Logs.Entries.Should().Contain(entry => IsWarning(entry));
        rig.Catalog.Add(process);
        await rig.DrainAsync();
        port.Calls.Should().ContainSingle();
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("sent");
    }

    [Fact]
    public async Task AnInboxRowIsLeftPendingAndLaterRowsOfTheSameInstanceWaitBehindIt()
    {
        await using var rig = await Rig.CreateAsync();
        var process = Fixtures.Host("buffer");
        rig.Catalog.Add(process);
        var id = await rig.StartAsync("host.buffer", "k-1");
        rig.Catalog.Remove("host.buffer");
        await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-1", "early", null, "{\"text\":\"first\"}", id));
        await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-2", "early", null, "{\"text\":\"second\"}", id));

        (await rig.DrainAsync()).Should().Be(0);

        (await rig.CountAsync("process_inbox", "status = 1")).Should().Be(2);
        rig.Logs.Entries.Should().Contain(entry => IsWarning(entry));
        rig.Catalog.Add(process);
        (await rig.DrainAsync()).Should().Be(2);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.State!.Inbox.Select(item => item.PayloadJson)
            .Should().Equal("{\"text\":\"first\"}", "{\"text\":\"second\"}");
    }

    [Fact]
    public async Task AnExpiredAtMostOnceLeaseIsLeftUntilTheDefinitionComesBack()
    {
        await using var rig = await Rig.CreateAsync();
        var process = Fixtures.Host("delivery");
        rig.Catalog.Add(process);
        using var crash = new CancellationTokenSource();
        var port = rig.Port("t.send");
        port.Handler = async (_, _) =>
        {
            await crash.CancelAsync();
            return new PortCompleted("{}");
        };
        var id = await rig.StartAsync("host.delivery", "k-1");
        var pass = () => rig.Pump.RunOnceAsync(crash.Token);
        await pass.Should().ThrowAsync<OperationCanceledException>();
        rig.Catalog.Remove("host.delivery");
        rig.Time.Advance(TimeSpan.FromMinutes(5));

        (await rig.DrainAsync()).Should().Be(0);

        (await rig.ColumnAsync<short>("select status from {schema}.process_effect")).Should().Equal((short)2);
        rig.Catalog.Add(process);
        await rig.DrainAsync();
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("unknown");
        port.Calls.Should().ContainSingle();
    }
}

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// One item that fails, or cannot be served, must not stop the pass or hide the items behind it. The first test makes the items of
/// one tenant fail; the others leave the first item of every kind untouched (its instance is locked by another session) and use a
/// batch size of one, which is the case in which the old scan could not see past it.
/// </summary>
public sealed class PumpIsolationTests
{
    private const string Bad = "t-bad";
    private const string Good = "t-ok";
    private const string Secret = "case-secret-41";

    [Fact]
    public async Task AnItemThatFailsIsLeftAsItWasAndTheOtherItemsOfTheSamePassAreHandled()
    {
        var protector = new FailingProtector(Bad);
        await using var rig = await Rig.CreateAsync(protector);
        rig.Catalog.Add(Fixtures.Host("task-timeout"), Fixtures.Host("wait-reply"), Fixtures.Host("repeat"));
        var port = rig.Port("t.send");
        var input = "{\"case_ref\":\"" + Secret + "\"}";
        var timers = new Dictionary<string, string>();
        var waits = new Dictionary<string, string>();
        var sends = new Dictionary<string, string>();
        foreach (var tenant in new[] { Bad, Good })
        {
            // The items of the failing tenant come first in every scan: they are created earlier and their inbox rows have lower ids.
            timers[tenant] = await rig.StartAsync("host.task", "k-task", input, tenant);
            waits[tenant] = await rig.StartAsync("host.wait", "k-wait", input, tenant);
            sends[tenant] = await rig.StartAsync("host.repeat", "k-send", input, tenant);
            await rig.Engine.PublishEventAsync(new ProcessEvent(tenant, "e-1", "reply", Secret, "{\"text\":\"" + Secret + "\"}", waits[tenant]));
            rig.Time.Advance(TimeSpan.FromSeconds(1));
        }

        rig.Time.Advance(TimeSpan.FromHours(2));
        protector.Armed = true;

        var handled = await rig.Pump.RunOnceAsync();

        protector.Armed = false;
        handled.Should().Be(3, "the timer, the inbox row and the effect of the healthy tenant");
        (await rig.Engine.GetInstanceAsync(Good, timers[Good]))!.Result.Should().Be("late");
        (await rig.Engine.GetInstanceAsync(Good, waits[Good]))!.Result.Should().Be("replied");
        (await rig.Engine.GetInstanceAsync(Good, sends[Good]))!.Result.Should().Be("sent");
        port.Calls.Should().ContainSingle().Which.TenantId.Should().Be(Good);

        (await rig.CountAsync("process_timer", "tenant_id = @t", ("t", Bad))).Should().Be(2, "the timer rows of the failing tenant (the task and the wait) were not consumed");
        (await rig.CountAsync("process_inbox", "tenant_id = @t and status = 1", ("t", Bad))).Should().Be(1);
        (await rig.CountAsync("process_effect", "tenant_id = @t and status = 1", ("t", Bad))).Should().Be(1);
        (await rig.Engine.GetInstanceAsync(Bad, timers[Bad]))!.Status.Should().Be(ProcessStatus.Waiting);

        var failures = rig.Logs.Entries.Where(entry => entry.EventId.Id == 12).ToList();
        failures.Should().HaveCount(4).And.OnlyContain(entry => entry.Level == LogLevel.Warning);
        failures.Select(entry => entry.Message).Should().OnlyContain(message => message.Contains(Bad, StringComparison.Ordinal) && message.Contains("System.InvalidOperationException", StringComparison.Ordinal));
        failures.Select(entry => KindOf(entry)).Should().BeEquivalentTo("Inbox", "DueTimer", "DueTimer", "DueEffect");
        rig.Logs.Entries.Should().OnlyContain(entry => entry.ExceptionText == null);
        rig.Logs.Entries.Should().NotContain(entry => Leaks(entry), "no message or argument carries the message of the exception or a value of the instances");

        protector.Armed = false;
        await rig.DrainAsync();
        (await rig.Engine.GetInstanceAsync(Bad, timers[Bad]))!.Result.Should().Be("late");
        (await rig.Engine.GetInstanceAsync(Bad, waits[Bad]))!.Result.Should().Be("replied");
        (await rig.Engine.GetInstanceAsync(Bad, sends[Bad]))!.Result.Should().Be("sent", "the items that failed are handled by a later pass");
    }

    private static string KindOf(LogEntry entry) => entry.State.Single(pair => pair.Key == "Kind").Value!.ToString()!;

    private static bool Leaks(LogEntry entry) =>
        entry.Message.Contains(FailingProtector.Message, StringComparison.Ordinal)
        || entry.Message.Contains(Secret, StringComparison.Ordinal)
        || entry.State.Any(pair => (pair.Value?.ToString() ?? "").Contains(Secret, StringComparison.Ordinal) || (pair.Value?.ToString() ?? "").Contains(FailingProtector.Message, StringComparison.Ordinal));

    [Fact]
    public async Task ADueTimerBehindALockedInstanceIsFiredInTheSamePassWhateverTheBatchSize()
    {
        await using var rig = await Rig.CreateAsync(configureHost: options => options.BatchSize = 1);
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var stuck = await rig.StartAsync("host.task", "k-1");
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        var healthy = await rig.StartAsync("host.task", "k-2");
        rig.Time.Advance(TimeSpan.FromHours(2));

        var held = await rig.HoldAsync(stuck);
        var handled = await rig.Pump.RunOnceAsync();

        handled.Should().Be(1);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, healthy))!.Result.Should().Be("late");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, stuck))!.Status.Should().Be(ProcessStatus.Waiting);

        await held.DisposeAsync();
        (await rig.Pump.RunOnceAsync()).Should().Be(1);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, stuck))!.Result.Should().Be("late");
    }

    [Fact]
    public async Task AnInboxRowBehindALockedInstanceIsAppliedInTheSamePassWhateverTheBatchSize()
    {
        await using var rig = await Rig.CreateAsync(configureHost: options => options.BatchSize = 1);
        rig.Catalog.Add(Fixtures.Host("wait-reply"));
        var stuck = await rig.StartAsync("host.wait", "k-1");
        var healthy = await rig.StartAsync("host.wait", "k-2");
        await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-1", "reply", "c-1", "{\"text\":\"a-1\"}", stuck));
        await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-2", "reply", "c-1", "{\"text\":\"a-2\"}", healthy));

        var held = await rig.HoldAsync(stuck);
        var handled = await rig.Pump.RunOnceAsync();

        handled.Should().Be(1);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, healthy))!.Result.Should().Be("replied");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, stuck))!.Status.Should().Be(ProcessStatus.Waiting);

        await held.DisposeAsync();
        (await rig.Pump.RunOnceAsync()).Should().Be(1);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, stuck))!.Result.Should().Be("replied");
    }

    [Fact]
    public async Task APendingEffectBehindALockedInstanceIsInvokedInTheSamePassWhateverTheBatchSize()
    {
        await using var rig = await Rig.CreateAsync(configureHost: options => options.BatchSize = 1);
        rig.Catalog.Add(Fixtures.Host("repeat"));
        var port = rig.Port("t.send");
        var stuck = await rig.StartAsync("host.repeat", "k-1");
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        var healthy = await rig.StartAsync("host.repeat", "k-2");

        var held = await rig.HoldAsync(stuck);
        var handled = await rig.Pump.RunOnceAsync();

        handled.Should().Be(1);
        port.Calls.Should().ContainSingle().Which.InstanceId.Should().Be(healthy);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, healthy))!.Result.Should().Be("sent");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, stuck))!.Status.Should().Be(ProcessStatus.Waiting);

        await held.DisposeAsync();
        (await rig.Pump.RunOnceAsync()).Should().Be(1);
        port.Calls.Select(call => call.InstanceId).Should().Equal(healthy, stuck);
    }

    [Fact]
    public async Task EveryItemOfAScanIsReadByThePagesWhenTheBatchIsSmallerThanTheNumberOfItems()
    {
        await using var rig = await Rig.CreateAsync(configureHost: options => options.BatchSize = 2);
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var ids = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            ids.Add(await rig.StartAsync("host.task", "k-" + index));
        }

        rig.Time.Advance(TimeSpan.FromHours(2));

        (await rig.Pump.RunOnceAsync()).Should().Be(2, "a pass handles at most one batch of each kind");
        (await rig.Pump.RunOnceAsync()).Should().Be(2);
        (await rig.Pump.RunOnceAsync()).Should().Be(1);
        (await rig.Pump.RunOnceAsync()).Should().Be(0);
        (await rig.CountAsync("process_instance", "result = 'late'")).Should().Be(5);
    }
}

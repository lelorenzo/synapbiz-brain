using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

public sealed class StartTests
{
    [Fact]
    public async Task AReadATimerAndAnActionRunToTheEnd()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("read-timer-action"));
        rig.Port("t.read").Handler = (_, _) => Task.FromResult<PortResult>(new PortCompleted("{\"amount\": 7}"));
        var act = rig.Port("t.act");

        var id = await rig.StartAsync("host.rta", "k-1");
        var started = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        started!.Status.Should().Be(ProcessStatus.Waiting);
        started.CurrentStep.Should().Be("read");
        (await rig.CountAsync("process_effect", "status = 1")).Should().Be(1);

        await rig.DrainAsync();
        var paused = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        paused!.CurrentStep.Should().Be("pause");
        paused.State!.Variables["amount"].Should().Be("7");
        act.Calls.Should().BeEmpty();

        rig.Time.Advance(TimeSpan.FromSeconds(29));
        (await rig.DrainAsync()).Should().Be(0);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await rig.DrainAsync();

        var done = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        done!.Status.Should().Be(ProcessStatus.Completed);
        done.Result.Should().Be("ok");
        done.CurrentStep.Should().BeNull();
        act.Calls.Should().ContainSingle().Which.Request.IdempotencyKey.Should().StartWith(id + "/act/");
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task StartingTwiceWithTheSameBusinessKeyCreatesOneInstance()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("read-timer-action"));
        var request = new StartRequest(rig.Tenant, "host.rta", "k-1", "{\"case_ref\":\"c-1\"}");

        var first = await rig.Engine.StartAsync(request);
        var second = await rig.Engine.StartAsync(request);

        first.Created.Should().BeTrue();
        second.Created.Should().BeFalse();
        second.InstanceId.Should().Be(first.InstanceId);
        (await rig.CountAsync("process_instance")).Should().Be(1);
        (await rig.CountAsync("process_effect")).Should().Be(1);
    }

    [Fact]
    public async Task TwentyConcurrentStartsCreateOneInstanceAndAllReturnItsId()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("read-timer-action"));
        var request = new StartRequest(rig.Tenant, "host.rta", "k-1", "{\"case_ref\":\"c-1\"}");

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => rig.Engine.StartAsync(request))));

        results.Select(result => result.InstanceId).Distinct().Should().ContainSingle();
        results.Count(result => result.Created).Should().Be(1);
        (await rig.CountAsync("process_instance")).Should().Be(1);
        (await rig.CountAsync("process_history", "kind = 0")).Should().Be(1);
    }

    [Fact]
    public async Task TheSameBusinessKeyOfAnotherProcessIsAnotherInstance()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("read-timer-action"), Fixtures.Host("suspend"));

        var first = await rig.StartAsync("host.rta", "k-1");
        var second = await rig.StartAsync("host.suspend", "k-1");

        second.Should().NotBe(first);
        (await rig.Engine.FindInstanceAsync(rig.Tenant, "host.suspend", "k-1"))!.InstanceId.Should().Be(second);
        (await rig.Engine.FindInstanceAsync(rig.Tenant, "host.suspend", "k-9")).Should().BeNull();
    }

    [Fact]
    public async Task AnUnknownProcessIsAnInvalidOperation()
    {
        await using var rig = await Rig.CreateAsync();

        var act = () => rig.Engine.StartAsync(new StartRequest(rig.Tenant, "nope", "k-1", "{}"));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("process.definition.unavailable");
        (await rig.CountAsync("process_instance")).Should().Be(0);
    }

    [Fact]
    public async Task InputTheInterpreterRejectsLeavesNoRowAndThrowsWithTheCode()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("read-timer-action"));

        var undeclared = () => rig.Engine.StartAsync(new StartRequest(rig.Tenant, "host.rta", "k-1", "{\"nope\":1}"));
        var mismatch = () => rig.Engine.StartAsync(new StartRequest(rig.Tenant, "host.rta", "k-1", "{\"case_ref\":5}"));

        (await undeclared.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("process.input.unknown");
        (await mismatch.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("process.variable.type_mismatch");
        (await rig.CountAsync("process_instance")).Should().Be(0);
        (await rig.Engine.StartAsync(new StartRequest(rig.Tenant, "host.rta", "k-1", "{\"case_ref\":\"c-1\"}"))).Created.Should().BeTrue();
    }

    [Fact]
    public async Task AnInstanceThatCompletesAtOnceIsStoredCompleted()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("quick"));

        var id = await rig.StartAsync("host.quick", "k-1");

        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Status.Should().Be(ProcessStatus.Completed);
        info.Result.Should().Be("instant");
        info.ProcessVersion.Should().Be(1);
        info.BusinessKey.Should().Be("k-1");
        info.CreatedAt.Should().Be(Rig.Start);
        (await rig.Engine.GetHistoryAsync(rig.Tenant, id)).Select(entry => entry.Kind)
            .Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Completed);
    }
}

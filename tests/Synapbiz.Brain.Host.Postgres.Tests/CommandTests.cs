using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

public sealed class CommandTests
{
    [Fact]
    public async Task CancelStopsTheInstanceAndClearsItsWork()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var id = await rig.StartAsync("host.task", "k-1");
        (await rig.OutstandingWorkAsync(id)).Should().Be(2);

        var first = await rig.Engine.CancelAsync(rig.Tenant, id, "operator");
        var again = await rig.Engine.CancelAsync(rig.Tenant, id, "operator");

        first.Should().Be(new CommandResult(AdvanceDisposition.Applied, null));
        again.Should().Be(new CommandResult(AdvanceDisposition.Ignored, "process.trigger.stale"));
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Status.Should().Be(ProcessStatus.Cancelled);
        info.CurrentStep.Should().BeNull();
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
        (await rig.Engine.GetHistoryAsync(rig.Tenant, id))[^1].Kind.Should().Be(ProcessHistoryKind.Cancelled);
    }

    [Fact]
    public async Task ACommandOnAnUnknownInstanceIsRejectedAsNotFound()
    {
        await using var rig = await Rig.CreateAsync();

        (await rig.Engine.CancelAsync(rig.Tenant, "nope", "operator")).Should().Be(new CommandResult(AdvanceDisposition.Rejected, "process.instance.not_found"));
        (await rig.Engine.ResumeAsync(rig.Tenant, "nope")).Should().Be(new CommandResult(AdvanceDisposition.Rejected, "process.instance.not_found"));
        (await rig.Engine.GetInstanceAsync(rig.Tenant, "nope")).Should().BeNull();
        (await rig.Engine.GetHistoryAsync(rig.Tenant, "nope")).Should().BeEmpty();
    }

    [Fact]
    public async Task ResumingASuspendedInstanceRunsItsStepAgain()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("suspend"));
        var read = rig.Port("t.read");
        var calls = 0;
        read.Handler = (_, _) => Task.FromResult<PortResult>(Interlocked.Increment(ref calls) == 1
            ? new PortFailed(ProcessErrorKind.Permanent, "x.broken")
            : new PortCompleted("{}"));
        var id = await rig.StartAsync("host.suspend", "k-1");

        await rig.DrainAsync();

        var suspended = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        suspended!.Status.Should().Be(ProcessStatus.Suspended);
        suspended.Problem.Should().Be(new ProcessProblem("x.broken", "check"));
        suspended.CurrentStep.Should().Be("check");
        (await rig.Engine.ResumeAsync(rig.Tenant, "nope")).Disposition.Should().Be(AdvanceDisposition.Rejected);

        var resumed = await rig.Engine.ResumeAsync(rig.Tenant, id);
        var resumedAgain = await rig.Engine.ResumeAsync(rig.Tenant, id);
        await rig.DrainAsync();

        resumed.Should().Be(new CommandResult(AdvanceDisposition.Applied, null));
        resumedAgain.Should().Be(new CommandResult(AdvanceDisposition.Ignored, "process.trigger.stale"));
        read.Calls.Should().HaveCount(2);
        read.Calls[1].Request.EffectId.Should().NotBe(read.Calls[0].Request.EffectId, "a resume gives the step a fresh entry number");
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Status.Should().Be(ProcessStatus.Completed);
        info.Problem.Should().BeNull();
        (await rig.Engine.GetHistoryAsync(rig.Tenant, id)).Should().Contain(entry => entry.Kind == ProcessHistoryKind.Resumed);
    }

    [Fact]
    public async Task ASuspendedInstanceKeepsNoPendingEffectAndCanBeCancelled()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("suspend"));
        rig.Port("t.read").Handler = (_, _) => Task.FromResult<PortResult>(new PortFailed(ProcessErrorKind.Permanent, "x.broken"));
        var id = await rig.StartAsync("host.suspend", "k-1");
        await rig.DrainAsync();

        (await rig.CountAsync("process_effect", "status in (1, 2)")).Should().Be(0);
        (await rig.Engine.CancelAsync(rig.Tenant, id, "operator")).Disposition.Should().Be(AdvanceDisposition.Applied);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Status.Should().Be(ProcessStatus.Cancelled);
    }

    [Fact]
    public async Task ACommandOnAnInstanceWhoseDefinitionIsGoneIsAnInvalidOperation()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var id = await rig.StartAsync("host.task", "k-1");
        rig.Catalog.Remove("host.task");

        var cancel = () => rig.Engine.CancelAsync(rig.Tenant, id, "operator");

        (await cancel.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("process.definition.unavailable");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Status.Should().Be(ProcessStatus.Waiting);
    }
}

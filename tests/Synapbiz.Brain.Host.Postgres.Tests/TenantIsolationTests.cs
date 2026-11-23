using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

public sealed class TenantIsolationTests
{
    private const string A = "tenant-a";
    private const string B = "tenant-b";

    [Fact]
    public async Task TheSameBusinessKeyAndTheSameEventIdInTwoTenantsDoNotCollide()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-reply"));
        var a = await rig.Engine.StartAsync(new StartRequest(A, "host.wait", "k-1", "{\"case_ref\":\"c-1\"}"));
        var b = await rig.Engine.StartAsync(new StartRequest(B, "host.wait", "k-1", "{\"case_ref\":\"c-1\"}"));

        var eventA = await rig.Engine.PublishEventAsync(new ProcessEvent(A, "e-1", "reply", "c-1", "{\"text\":\"from-a\"}"));
        var eventB = await rig.Engine.PublishEventAsync(new ProcessEvent(B, "e-1", "reply", "c-1", "{\"text\":\"from-b\"}"));
        await rig.DrainAsync();

        a.Created.Should().BeTrue();
        b.Created.Should().BeTrue();
        a.InstanceId.Should().NotBe(b.InstanceId);
        eventA.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        eventB.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        (await rig.Engine.GetInstanceAsync(A, a.InstanceId))!.State!.Variables["answer"].Should().Be("\"from-a\"");
        (await rig.Engine.GetInstanceAsync(B, b.InstanceId))!.State!.Variables["answer"].Should().Be("\"from-b\"");
        (await rig.Engine.PublishEventAsync(new ProcessEvent(A, "e-1", "reply", "c-1", "{}"))).Status.Should().Be(PublishStatus.Duplicate);
    }

    [Fact]
    public async Task ATenantCannotReadOrChangeAnotherTenantsInstanceTaskOrHistory()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("task-timeout"), Fixtures.Host("wait-reply"));
        var b = (await rig.Engine.StartAsync(new StartRequest(B, "host.task", "k-1", "{\"case_ref\":\"c-1\"}"))).InstanceId;
        var waiting = (await rig.Engine.StartAsync(new StartRequest(B, "host.wait", "k-2", "{\"case_ref\":\"c-2\"}"))).InstanceId;
        var task = (await rig.Engine.ListOpenTasksAsync(B, "review_q")).Single();
        var revision = await rig.ScalarAsync<long>("select revision from {schema}.process_instance where instance_id = @i", ("i", b));

        (await rig.Engine.GetInstanceAsync(A, b)).Should().BeNull();
        (await rig.Engine.FindInstanceAsync(A, "host.task", "k-1")).Should().BeNull();
        (await rig.Engine.GetHistoryAsync(A, b)).Should().BeEmpty();
        (await rig.Engine.ListOpenTasksAsync(A, "review_q")).Should().BeEmpty();
        (await rig.Engine.CompleteTaskAsync(A, task.TaskId, "approve", "{}")).Should().Be(new CommandResult(AdvanceDisposition.Rejected, "process.instance.not_found"));
        (await rig.Engine.CancelAsync(A, b, "operator")).Should().Be(new CommandResult(AdvanceDisposition.Rejected, "process.instance.not_found"));
        (await rig.Engine.ResumeAsync(A, b)).Should().Be(new CommandResult(AdvanceDisposition.Rejected, "process.instance.not_found"));
        (await rig.Engine.PublishEventAsync(new ProcessEvent(A, "e-1", "reply", "c-2", "{}", waiting))).Status.Should().Be(PublishStatus.NoTarget);
        (await rig.Engine.PublishEventAsync(new ProcessEvent(A, "e-2", "reply", "c-2", "{}"))).Status.Should().Be(PublishStatus.NoTarget);
        await rig.DrainAsync();

        var info = await rig.Engine.GetInstanceAsync(B, b);
        info!.Status.Should().Be(ProcessStatus.Waiting);
        (await rig.ScalarAsync<long>("select revision from {schema}.process_instance where instance_id = @i", ("i", b))).Should().Be(revision);
        (await rig.Engine.GetInstanceAsync(B, waiting))!.Status.Should().Be(ProcessStatus.Waiting);
        (await rig.Engine.ListOpenTasksAsync(B, "review_q")).Should().ContainSingle();
    }

    [Fact]
    public async Task ThePumpServesEveryTenantAndTellsThePortWhichTenantItIsFor()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("repeat"));
        var port = rig.Port("t.send");
        await rig.Engine.StartAsync(new StartRequest(A, "host.repeat", "k-1", "{\"case_ref\":\"c-1\"}"));
        await rig.Engine.StartAsync(new StartRequest(B, "host.repeat", "k-1", "{\"case_ref\":\"c-1\"}"));

        await rig.DrainAsync();

        port.Calls.Select(call => call.TenantId).Should().BeEquivalentTo(A, B);
        (await rig.CountAsync("process_instance", "status = 4")).Should().Be(2);
    }
}

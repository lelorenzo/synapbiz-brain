using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

public sealed class ChildProcessTests
{
    [Fact]
    public async Task TheParentWaitsForTheChildAndReceivesItsVariables()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("parent"), Fixtures.Host("child"));
        var parentId = await rig.StartAsync("host.parent", "k-1");
        var childKey = parentId + "/spawn/1/child";

        var created = await rig.Engine.FindInstanceAsync(rig.Tenant, "host.child", childKey);
        created.Should().NotBeNull();
        created!.Status.Should().Be(ProcessStatus.Running, "an instance that is created and not yet started reports Running");
        created.State.Should().BeNull();
        created.ParentInstanceId.Should().Be(parentId);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, parentId))!.State!.Pending!.Kind.Should().Be(ProcessPendingKind.Child);

        await rig.DrainAsync();

        var started = await rig.Engine.GetInstanceAsync(rig.Tenant, created.InstanceId);
        started!.Status.Should().Be(ProcessStatus.Waiting);
        started.State!.Variables["case_ref"].Should().Be("\"c-1\"");
        var task = (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "child_q")).Single();
        task.InstanceId.Should().Be(created.InstanceId);

        (await rig.Engine.CompleteTaskAsync(rig.Tenant, task.TaskId, "finish", "{\"value\":\"v-9\"}")).Disposition.Should().Be(AdvanceDisposition.Applied);
        await rig.DrainAsync();

        var child = await rig.Engine.GetInstanceAsync(rig.Tenant, created.InstanceId);
        child!.Status.Should().Be(ProcessStatus.Completed);
        child.Result.Should().Be("finished");
        var parent = await rig.Engine.GetInstanceAsync(rig.Tenant, parentId);
        parent!.Result.Should().Be("parent_done");
        parent.State!.Variables["child_value"].Should().Be("\"v-9\"");
        (await rig.OutstandingWorkAsync(parentId)).Should().Be(0);
        (await rig.OutstandingWorkAsync(created.InstanceId)).Should().Be(0);
        (await rig.CountAsync("process_inbox", "status <> 2")).Should().Be(0);
    }

    [Fact]
    public async Task AMissingChildDefinitionFailsTheChildStepWithoutCreatingAnInstance()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("parent"));
        var parentId = await rig.StartAsync("host.parent", "k-1");

        await rig.DrainAsync();

        (await rig.Engine.GetInstanceAsync(rig.Tenant, parentId))!.Result.Should().Be("child_failed");
        (await rig.CountAsync("process_instance")).Should().Be(1);
        (await rig.Engine.GetHistoryAsync(rig.Tenant, parentId)).Should().Contain(entry => entry.Kind == ProcessHistoryKind.ChildFailed && entry.Code == "process.definition.unavailable");
        rig.Logs.Entries.Should().Contain(entry => entry.Message.StartsWith("process.child.definition_unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellingTheParentCancelsTheChild()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("parent"), Fixtures.Host("child"));
        var parentId = await rig.StartAsync("host.parent", "k-1");
        await rig.DrainAsync();
        var child = (await rig.Engine.FindInstanceAsync(rig.Tenant, "host.child", parentId + "/spawn/1/child"))!;

        var cancelled = await rig.Engine.CancelAsync(rig.Tenant, parentId, "operator");
        await rig.DrainAsync();

        cancelled.Disposition.Should().Be(AdvanceDisposition.Applied);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, parentId))!.Status.Should().Be(ProcessStatus.Cancelled);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, child.InstanceId))!.Status.Should().Be(ProcessStatus.Cancelled);
        (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "child_q")).Should().BeEmpty();
        (await rig.ScalarAsync<short>("select status from {schema}.process_task")).Should().Be(3);
        (await rig.OutstandingWorkAsync(parentId)).Should().Be(0);
        (await rig.OutstandingWorkAsync(child.InstanceId)).Should().Be(0);
        (await rig.CountAsync("process_inbox", "status = 1")).Should().Be(0);
        (await rig.Engine.GetHistoryAsync(rig.Tenant, child.InstanceId)).Select(entry => entry.Kind).Should().EndWith(ProcessHistoryKind.Cancelled);
    }

    [Fact]
    public async Task CancellingTheChildFailsTheParentsChildStep()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("parent"), Fixtures.Host("child"));
        var parentId = await rig.StartAsync("host.parent", "k-1");
        await rig.DrainAsync();
        var child = (await rig.Engine.FindInstanceAsync(rig.Tenant, "host.child", parentId + "/spawn/1/child"))!;

        await rig.Engine.CancelAsync(rig.Tenant, child.InstanceId, "operator");
        await rig.DrainAsync();

        (await rig.Engine.GetInstanceAsync(rig.Tenant, parentId))!.Result.Should().Be("child_failed");
        (await rig.Engine.GetHistoryAsync(rig.Tenant, parentId)).Should().Contain(entry => entry.Kind == ProcessHistoryKind.ChildFailed && entry.Code == "process.child.cancelled");
    }

    [Fact]
    public async Task AChildWhoseStartIsRejectedIsCancelledWithTheCodeAndTheParentIsToldSo()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("parent-badinput"), Fixtures.Host("child"));
        var parentId = await rig.StartAsync("host.parentbad", "k-1");

        await rig.DrainAsync();

        var child = (await rig.Engine.FindInstanceAsync(rig.Tenant, "host.child", parentId + "/spawn/1/child"))!;
        child.Status.Should().Be(ProcessStatus.Cancelled);
        child.State.Should().BeNull();
        child.Problem!.Code.Should().Be("process.input.unknown");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, parentId))!.Result.Should().Be("child_failed");
        (await rig.Engine.GetHistoryAsync(rig.Tenant, parentId)).Should().Contain(entry => entry.Kind == ProcessHistoryKind.ChildFailed && entry.Code == "process.input.unknown");
        (await rig.CountAsync("process_inbox", "status = 3 and code = 'process.input.unknown'")).Should().Be(1);
        (await rig.Engine.CancelAsync(rig.Tenant, child.InstanceId, "operator")).Should().Be(new CommandResult(AdvanceDisposition.Ignored, "process.trigger.stale"));
    }
}

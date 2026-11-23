using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

public sealed class TaskTests
{
    [Fact]
    public async Task OpenTasksAreListedPerQueueAndTenant()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var mine = await rig.StartAsync("host.task", "k-1", "{\"case_ref\":\"c-1\"}");
        var theirs = await rig.StartAsync("host.task", "k-1", "{\"case_ref\":\"c-2\"}", tenant: "t-2");

        var listed = await rig.Engine.ListOpenTasksAsync("t-1", "review_q");

        var task = listed.Should().ContainSingle().Subject;
        task.TenantId.Should().Be("t-1");
        task.InstanceId.Should().Be(mine);
        task.Step.Should().Be("review");
        task.Queue.Should().Be("review_q");
        task.Outcomes.Should().Equal("approve", "reject");
        task.ShowJson.Should().Be("{\"case_ref\":\"c-1\"}");
        task.CreatedAt.Should().Be(Rig.Start);
        task.TaskId.Should().Be(mine + "/review/1/task");
        (await rig.Engine.ListOpenTasksAsync("t-1", "other_q")).Should().BeEmpty();
        (await rig.Engine.ListOpenTasksAsync("t-2", "review_q")).Should().ContainSingle().Which.InstanceId.Should().Be(theirs);
    }

    [Fact]
    public async Task ACompletedTaskMovesTheInstanceOnAndCannotBeCompletedTwice()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var id = await rig.StartAsync("host.task", "k-1");
        var task = (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "review_q")).Single();
        rig.Time.Advance(TimeSpan.FromMinutes(5));

        var first = await rig.Engine.CompleteTaskAsync(rig.Tenant, task.TaskId, "approve", "{\"note\":\"n-1\"}");
        var second = await rig.Engine.CompleteTaskAsync(rig.Tenant, task.TaskId, "approve", "{\"note\":\"n-2\"}");

        first.Should().Be(new CommandResult(AdvanceDisposition.Applied, null));
        second.Should().Be(new CommandResult(AdvanceDisposition.Ignored, "process.trigger.stale"));
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Result.Should().Be("approved");
        info.State!.Variables["note"].Should().Be("\"n-1\"");
        (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "review_q")).Should().BeEmpty();
        (await rig.ScalarAsync<short>("select status from {schema}.process_task")).Should().Be(2);
        (await rig.ScalarAsync<DateTimeOffset>("select closed_at from {schema}.process_task")).Should().Be(Rig.Start.AddMinutes(5));
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task AnUnknownOutcomeOrBadDataLeavesTheTaskOpen()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var id = await rig.StartAsync("host.task", "k-1");
        var task = (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "review_q")).Single();
        var revision = await rig.ScalarAsync<long>("select revision from {schema}.process_instance");

        var unknown = await rig.Engine.CompleteTaskAsync(rig.Tenant, task.TaskId, "maybe", "{}");
        var mismatch = await rig.Engine.CompleteTaskAsync(rig.Tenant, task.TaskId, "approve", "{\"note\":5}");

        unknown.Should().Be(new CommandResult(AdvanceDisposition.Rejected, "process.task.outcome_unknown"));
        mismatch.Should().Be(new CommandResult(AdvanceDisposition.Rejected, "process.variable.type_mismatch"));
        (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "review_q")).Should().ContainSingle();
        (await rig.ScalarAsync<long>("select revision from {schema}.process_instance")).Should().Be(revision);
        (await rig.Engine.CompleteTaskAsync(rig.Tenant, task.TaskId, "reject", "{}")).Disposition.Should().Be(AdvanceDisposition.Applied);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("rejected");
    }

    [Fact]
    public async Task TheTimeoutCancelsTheTaskAndAnAnswerAfterwardsIsStale()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var id = await rig.StartAsync("host.task", "k-1");
        var task = (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "review_q")).Single();

        rig.Time.Advance(TimeSpan.FromMinutes(59));
        (await rig.DrainAsync()).Should().Be(0);
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        await rig.DrainAsync();

        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("late");
        (await rig.ScalarAsync<short>("select status from {schema}.process_task")).Should().Be(3);
        (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "review_q")).Should().BeEmpty();
        (await rig.Engine.CompleteTaskAsync(rig.Tenant, task.TaskId, "approve", "{}")).Should().Be(new CommandResult(AdvanceDisposition.Ignored, "process.trigger.stale"));
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task AnUnknownTaskIsRejectedAsNotFound()
    {
        await using var rig = await Rig.CreateAsync();

        var result = await rig.Engine.CompleteTaskAsync(rig.Tenant, "no-such-task", "approve", "{}");

        result.Should().Be(new CommandResult(AdvanceDisposition.Rejected, "process.instance.not_found"));
    }
}

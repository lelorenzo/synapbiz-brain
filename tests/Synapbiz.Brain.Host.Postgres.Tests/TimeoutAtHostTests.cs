using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// W1-G, G1: a timeout tied to an instant. The clock of the rig starts at 2027-01-04T14:00:00Z; the visit is at 20:00Z, so the
/// timeout, two hours before it, is due at 18:00Z.
/// </summary>
public sealed class TimeoutAtHostTests
{
    private const string Input = "{\"case_ref\":\"c-1\",\"visit_at\":\"2027-01-04T20:00:00Z\"}";
    private static readonly DateTimeOffset Due = new(2027, 1, 4, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AWaitTimesOutAtTheInstantMinusTheOffsetAndNotBefore()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("deadline-wait"));
        var id = await rig.StartAsync("host.deadline_wait", "k-1", Input);

        (await rig.ColumnAsync<DateTimeOffset>("select due_at from {schema}.process_timer")).Should().Equal(Due);
        (await rig.ScalarAsync<string>("select timer_id from {schema}.process_timer")).Should().Be(id + "/gate/1/timeout");
        (await rig.CountAsync("process_wait")).Should().Be(1);

        rig.Time.SetUtcNow(Due - TimeSpan.FromSeconds(1));
        await rig.DrainAsync();
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Status.Should().Be(ProcessStatus.Waiting);

        rig.Time.SetUtcNow(Due);
        await rig.DrainAsync();

        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Result.Should().Be("expired");
        (await rig.CountAsync("process_wait")).Should().Be(0);
        (await rig.CountAsync("process_timer")).Should().Be(0);
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task AnEventBeforeTheInstantWinsAndRemovesTheTimer()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("deadline-wait"));
        var id = await rig.StartAsync("host.deadline_wait", "k-1", Input);

        await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-1", "reply", "c-1", "{\"text\":\"yes\"}"));
        await rig.DrainAsync();

        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("replied");
        (await rig.CountAsync("process_timer")).Should().Be(0);
        (await rig.CountAsync("process_wait")).Should().Be(0);
    }

    [Fact]
    public async Task AnInstantAlreadyPastGoesToOnTimeoutAtOnceWithoutAWaitRowOrATimer()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("deadline-wait"));

        var id = await rig.StartAsync("host.deadline_wait", "k-1", "{\"case_ref\":\"c-1\",\"visit_at\":\"2027-01-04T15:00:00Z\"}");
        await rig.DrainAsync();

        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("expired");
        (await rig.CountAsync("process_wait")).Should().Be(0);
        (await rig.CountAsync("process_timer")).Should().Be(0);
        (await rig.Engine.GetHistoryAsync(rig.Tenant, id)).Select(entry => entry.Kind).Should().Contain(ProcessHistoryKind.TimedOut);
    }

    [Fact]
    public async Task ATaskTimesOutAtTheInstantMinusTheOffset()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("deadline-task"));
        var id = await rig.StartAsync("host.deadline_task", "k-1", Input);

        (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "review_q")).Should().ContainSingle();
        (await rig.ColumnAsync<DateTimeOffset>("select due_at from {schema}.process_timer")).Should().Equal(Due);

        rig.Time.SetUtcNow(Due);
        await rig.DrainAsync();

        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("late");
        (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "review_q")).Should().BeEmpty();
        (await rig.ScalarAsync<short>("select status from {schema}.process_task")).Should().Be(3);
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task ATaskWhoseInstantIsPastCreatesNoTaskRowAndNoTimer()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("deadline-task"));

        var id = await rig.StartAsync("host.deadline_task", "k-1", "{\"case_ref\":\"c-1\",\"visit_at\":\"2027-01-04T15:00:00Z\"}");
        await rig.DrainAsync();

        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("late");
        (await rig.CountAsync("process_task")).Should().Be(0);
        (await rig.CountAsync("process_timer")).Should().Be(0);
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task AMissingInstantSuspendsTheInstanceAndResumingEntersTheStepAgain()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("deadline-wait"));

        var id = await rig.StartAsync("host.deadline_wait", "k-1", "{\"case_ref\":\"c-1\"}");
        await rig.DrainAsync();

        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Status.Should().Be(ProcessStatus.Suspended);
        info.State!.Problem!.Code.Should().Be("process.timeout.instant_missing");
        (await rig.CountAsync("process_wait")).Should().Be(0);
        (await rig.CountAsync("process_timer")).Should().Be(0);
    }
}

using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// What the pump does when the world changed between reading an item and handling it: another session holds the instance, a lease was
/// claimed again, a timer was consumed, an earlier inbox row is still pending. Each case injects the stale scan result or the
/// competing transaction explicitly, and none of them sleeps: the test waits on task completion sources, or on the state of
/// PostgreSQL itself, in a loop that yields.
/// </summary>
public sealed class RevalidationTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(120);

    /// <summary>Yields until PostgreSQL reports a session of this rig's schema that waits for a lock.</summary>
    private static async Task WaitUntilSomeoneWaitsForALockAsync(Rig rig)
    {
        async Task Wait()
        {
            while (await rig.ScalarAsync<long>(
                "select count(*) from pg_stat_activity where wait_event_type = 'Lock' and pid <> pg_backend_pid() and query like '%' || @s || '%'",
                ("s", rig.Schema)) == 0)
            {
                await Task.Yield();
            }
        }

        await Wait().WaitAsync(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task AResultIsAppliedOnceTheInstanceLockIsReleasedNotDroppedNorLeftForTheLease()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("repeat"));
        var port = rig.Port("t.send");
        IProcessSession? held = null;
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        port.Handler = async (call, _) =>
        {
            // The call is in flight and the pump holds no lock: another session takes the instance before the answer is applied.
            held = await rig.HoldAsync(call.InstanceId);
            locked.SetResult();
            return new PortCompleted("{}");
        };
        var id = await rig.StartAsync("host.repeat", "k-1");

        var pass = rig.Pump.RunOnceAsync();
        await locked.Task;
        await WaitUntilSomeoneWaitsForALockAsync(rig);

        pass.IsCompleted.Should().BeFalse("the result waits for the instance lock instead of being dropped");
        (await rig.ColumnAsync<short>("select status from {schema}.process_effect")).Should().Equal((short)2);
        await held!.DisposeAsync();
        await pass.WaitAsync(TimeSpan.FromMinutes(2));

        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("sent");
        (await rig.ColumnAsync<short>("select status from {schema}.process_effect")).Should().Equal((short)3);
        port.Calls.Should().ContainSingle();
        rig.Logs.Entries.Should().NotContain(entry => entry.Message.StartsWith("process.effect.lease_expired", StringComparison.Ordinal), "the clock never moved: the lease had nothing to do with it");
    }

    [Fact]
    public async Task AnInboxRowThatTheScanReturnedOutOfOrderIsNotAppliedBeforeItsPredecessor()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("buffer"));
        var id = await rig.StartAsync("host.buffer", "k-1");
        await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-1", "early", null, "{\"text\":\"first\"}", id));
        await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-2", "early", null, "{\"text\":\"second\"}", id));
        var rows = await rig.ColumnAsync<long>("select inbox_id from {schema}.process_inbox order by inbox_id");
        rows.Should().HaveCount(2);
        HookedStore? hooked = null;
        var pump = rig.NewPump(inner => hooked = new HookedStore(inner));
        hooked!.AfterScan = (kind, found) => Task.FromResult<IReadOnlyList<WorkRef>>(
            kind == WorkKind.Inbox ? [new WorkRef(rig.Tenant, id, rows[1].ToString(System.Globalization.CultureInfo.InvariantCulture))] : found);
        var revision = await rig.ScalarAsync<long>("select revision from {schema}.process_instance where instance_id = @i", ("i", id));

        (await pump.RunOnceAsync()).Should().Be(0);

        (await rig.ColumnAsync<short>("select status from {schema}.process_inbox order by inbox_id")).Should().Equal((short)1, (short)1);
        (await rig.ScalarAsync<long>("select revision from {schema}.process_instance where instance_id = @i", ("i", id))).Should().Be(revision);

        hooked.AfterScan = null;
        await Rig.DrainAsync(pump);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.State!.Inbox.Select(item => item.PayloadJson).Should().Equal("{\"text\":\"first\"}", "{\"text\":\"second\"}");
    }

    [Fact]
    public async Task ALeaseThatWasClaimedAgainAfterTheExpiryScanIsNotExpiredByTheStaleScanResult()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("delivery"));
        var port = rig.Port("t.send");
        var id = await rig.StartAsync("host.delivery", "k-1");
        await rig.ExecuteAsync("update {schema}.process_effect set status = 2, lease_owner = 'gone', lease_until = @u", ("u", Rig.Start.AddSeconds(-1)));
        HookedStore? hooked = null;
        var pump = rig.NewPump(inner => hooked = new HookedStore(inner));
        var reclaimed = false;
        hooked!.AfterScan = async (kind, found) =>
        {
            if (kind == WorkKind.ExpiredLease)
            {
                found.Should().ContainSingle("the lease has expired when the scan looks");
                await rig.ExecuteAsync("update {schema}.process_effect set lease_owner = 'another-pump', lease_until = @u", ("u", Rig.Start + Lease));
                reclaimed = true;
            }

            return found;
        };

        await pump.RunOnceAsync();

        reclaimed.Should().BeTrue();
        (await rig.ColumnAsync<short>("select status from {schema}.process_effect")).Should().Equal((short)2);
        (await rig.ScalarAsync<string>("select lease_owner from {schema}.process_effect")).Should().Be("another-pump");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Status.Should().Be(ProcessStatus.Waiting);
        (await rig.CountAsync("process_history", "code = 'process.effect.outcome_unknown'")).Should().Be(0);
        port.Calls.Should().BeEmpty();

        hooked.AfterScan = null;
        rig.Time.Advance(Lease);
        await Rig.DrainAsync(pump);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("unknown", "the lease that was claimed again expires on its own");
    }

    [Fact]
    public async Task ATimerThatAnotherSessionAlreadyConsumedIsNotFiredAgain()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var id = await rig.StartAsync("host.task", "k-1");
        var revision = await rig.ScalarAsync<long>("select revision from {schema}.process_instance where instance_id = @i", ("i", id));
        rig.Time.Advance(TimeSpan.FromHours(2));
        var other = rig.NewPump();
        HookedStore? hooked = null;
        var pump = rig.NewPump(inner => hooked = new HookedStore(inner));
        var fired = 0;
        hooked!.AfterScan = async (kind, found) =>
        {
            if (kind == WorkKind.DueTimer)
            {
                found.Should().ContainSingle();
                fired += await other.RunOnceAsync();
            }

            return found;
        };

        await pump.RunOnceAsync();

        fired.Should().Be(1, "the other session fired the timer between the scan and the handling");
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Result.Should().Be("late");
        (await rig.ScalarAsync<long>("select revision from {schema}.process_instance where instance_id = @i", ("i", id))).Should().Be(revision + 1, "the state changed exactly once");
        (await rig.CountAsync("process_history", "kind = @k", ("k", (short)ProcessHistoryKind.TimedOut))).Should().Be(1);
        (await rig.ColumnAsync<string>("select instance_id from {schema}.process_history group by instance_id having max(sequence) <> count(*)")).Should().BeEmpty();
    }

    [Fact]
    public async Task ATimerRowThatIsGoneIsSkippedEvenWhenTheInstanceStillWaitsForIt()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var id = await rig.StartAsync("host.task", "k-1");
        rig.Time.Advance(TimeSpan.FromHours(2));
        HookedStore? hooked = null;
        var pump = rig.NewPump(inner => hooked = new HookedStore(inner));
        hooked!.AfterScan = async (kind, found) =>
        {
            if (kind == WorkKind.DueTimer)
            {
                await rig.ExecuteAsync("delete from {schema}.process_timer");
            }

            return found;
        };

        (await pump.RunOnceAsync()).Should().Be(1, "a row that is gone counts as handled");

        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Status.Should().Be(ProcessStatus.Waiting, "the trigger of a timer that no longer exists is never delivered");
        (await rig.CountAsync("process_history", "kind = @k", ("k", (short)ProcessHistoryKind.TimedOut))).Should().Be(0);
    }

    private const string Other = "t-2";

    /// <summary>Copies the rows of an instance (and its waits, timers and tasks) to another tenant under the same ids, as only SQL can.</summary>
    private static async Task CopyToOtherTenantAsync(Rig rig, string instanceId)
    {
        var parameters = new (string Name, object? Value)[] { ("o", Other), ("t", rig.Tenant), ("i", instanceId) };
        await rig.ExecuteAsync(
            "insert into {schema}.process_instance select @o, instance_id, process_id, process_version, process_hash, business_key, status, stage, current_step, result, problem_code, state, revision, parent_instance_id, parent_child_key, created_at, updated_at from {schema}.process_instance where tenant_id = @t and instance_id = @i",
            parameters);
        await rig.ExecuteAsync("insert into {schema}.process_wait select @o, wait_id, instance_id, event, correlation from {schema}.process_wait where tenant_id = @t and instance_id = @i", parameters);
        await rig.ExecuteAsync("insert into {schema}.process_timer select @o, timer_id, instance_id, due_at from {schema}.process_timer where tenant_id = @t and instance_id = @i", parameters);
        await rig.ExecuteAsync("insert into {schema}.process_task select @o, task_id, instance_id, step, queue, outcomes, show, status, created_at, closed_at from {schema}.process_task where tenant_id = @t and instance_id = @i", parameters);
    }

    [Fact]
    public async Task TheSameInstanceIdInTwoTenantsNeverLetsAnEventOfOneReachTheWaitOfTheOther()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-reply"));
        var id = await rig.StartAsync("host.wait", "k-1");
        await CopyToOtherTenantAsync(rig, id);
        (await rig.CountAsync("process_wait", "instance_id = @i", ("i", id))).Should().Be(2);

        var forFirst = await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-1", "reply", "c-1", "{\"text\":\"a-1\"}"));
        var forThird = await rig.Engine.PublishEventAsync(new ProcessEvent("t-3", "e-1", "reply", "c-1", "{\"text\":\"a-1\"}"));
        var direct = await rig.Engine.PublishEventAsync(new ProcessEvent("t-3", "e-2", "reply", "c-1", "{\"text\":\"a-1\"}", id));
        await rig.DrainAsync();

        forFirst.Should().Be(new PublishResult(PublishStatus.Queued, 1));
        forThird.Should().Be(new PublishResult(PublishStatus.NoTarget, 0));
        direct.Should().Be(new PublishResult(PublishStatus.NoTarget, 0));
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("replied");
        (await rig.Engine.GetInstanceAsync(Other, id))!.Status.Should().Be(ProcessStatus.Waiting);
        (await rig.CountAsync("process_inbox", "tenant_id = @o", ("o", Other))).Should().Be(0);
        (await rig.CountAsync("process_wait", "tenant_id = @o", ("o", Other))).Should().Be(1, "the wait of the other tenant is still there");

        (await rig.Engine.PublishEventAsync(new ProcessEvent(Other, "e-1", "reply", "c-1", "{\"text\":\"a-9\"}"))).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        await rig.DrainAsync();
        (await rig.Engine.GetInstanceAsync(Other, id))!.State!.Variables["answer"].Should().Be("\"a-9\"");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.State!.Variables["answer"].Should().Be("\"a-1\"");
    }

    [Fact]
    public async Task TheSameTaskIdInTwoTenantsNeverLetsATaskLookupOrACompletionReachTheOther()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var id = await rig.StartAsync("host.task", "k-1");
        var task = (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "review_q")).Single();
        await CopyToOtherTenantAsync(rig, id);
        var revision = await rig.ScalarAsync<long>("select revision from {schema}.process_instance where tenant_id = @o", ("o", Other));

        var approved = await rig.Engine.CompleteTaskAsync(rig.Tenant, task.TaskId, "approve", "{}");
        var unknown = await rig.Engine.CompleteTaskAsync("t-3", task.TaskId, "approve", "{}");

        approved.Disposition.Should().Be(AdvanceDisposition.Applied);
        unknown.Should().Be(new CommandResult(AdvanceDisposition.Rejected, "process.instance.not_found"));
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("approved");
        (await rig.Engine.GetInstanceAsync(Other, id))!.Status.Should().Be(ProcessStatus.Waiting);
        (await rig.ScalarAsync<long>("select revision from {schema}.process_instance where tenant_id = @o", ("o", Other))).Should().Be(revision);
        (await rig.Engine.ListOpenTasksAsync(Other, "review_q")).Should().ContainSingle().Which.TaskId.Should().Be(task.TaskId);
        (await rig.Engine.ListOpenTasksAsync(rig.Tenant, "review_q")).Should().BeEmpty();
        (await rig.CountAsync("process_timer", "tenant_id = @o", ("o", Other))).Should().Be(1, "closing the instance of one tenant leaves the timer of the other");

        (await rig.Engine.CompleteTaskAsync(Other, task.TaskId, "reject", "{}")).Disposition.Should().Be(AdvanceDisposition.Applied);
        (await rig.Engine.GetInstanceAsync(Other, id))!.Result.Should().Be("rejected");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("approved");
    }

    [Fact]
    public async Task ThePumpServesTwoTenantsThatShareInstanceAndTimerIdsEvenWhenThePagesAreOneItemLong()
    {
        await using var rig = await Rig.CreateAsync(configureHost: options => options.BatchSize = 1);
        rig.Catalog.Add(Fixtures.Host("task-timeout"));
        var id = await rig.StartAsync("host.task", "k-1");
        await CopyToOtherTenantAsync(rig, id);
        rig.Time.Advance(TimeSpan.FromHours(2));

        (await rig.Pump.RunOnceAsync()).Should().Be(1);
        (await rig.Pump.RunOnceAsync()).Should().Be(1);
        (await rig.Pump.RunOnceAsync()).Should().Be(0);

        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("late");
        (await rig.Engine.GetInstanceAsync(Other, id))!.Result.Should().Be("late");
        (await rig.CountAsync("process_timer")).Should().Be(0);
    }
}

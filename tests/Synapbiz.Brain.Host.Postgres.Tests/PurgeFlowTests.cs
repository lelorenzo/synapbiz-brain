using FluentAssertions;
using Microsoft.Extensions.Logging;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;
using Xunit.Abstractions;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>A purge and the pump: what the pump does with work that a purge removed, and that the two never deadlock.</summary>
public sealed class PurgeFlowTests
{
    private const string Tenant = "t-1";
    private const int Rounds = 80;

    private readonly ITestOutputHelper output;

    public PurgeFlowTests(ITestOutputHelper output) => this.output = output;

    /// <summary>The work of a tree as a pump finds it, before the purge: one case per kind of work item.</summary>
    public static TheoryData<string> WorkKinds => new() { "timer", "inbox", "effect", "lease", "child-notice" };

    [Theory]
    [MemberData(nameof(WorkKinds))]
    public async Task AnItemOfEveryKindThatAPumpFoundBeforeAPurgeIsDroppedWithoutAnErrorAndNothingIsRecreated(string kind)
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("task-timeout"), Fixtures.Host("wait-reply"), Fixtures.Host("read-timer-action"), Fixtures.Host("parent"), Fixtures.Host("child"));
        var port = rig.Port("t.read");
        rig.Port("t.act");
        var (scanned, instance) = await PrepareAsync(rig, kind);
        var other = await rig.StartAsync("host.wait", "k-other");
        var purged = false;
        var pump = rig.NewPump(store => new HookedStore(store)
        {
            AfterScan = async (scan, items) =>
            {
                if (scan == scanned && items.Count > 0 && !purged)
                {
                    purged = true;
                    (await rig.Engine.PurgeInstanceAsync(Tenant, instance, PurgeMode.Force)).Status.Should().Be(PurgeStatus.Purged);
                }

                return items;
            },
        });

        var handled = await pump.RunOnceAsync();

        purged.Should().BeTrue("the pump did find the item before the purge");
        handled.Should().Be(0, "an item whose instance is gone is not handled");
        (await RowsExceptAsync(rig, other)).Should().Be(0, "nothing of the tree is recreated, and no row of it is left");
        port.Calls.Should().BeEmpty("no port is called for a tree that was purged before its effect was claimed");
        rig.Logs.Entries.Should().NotContain(entry => entry.EventId.Id == 12, "no item failed");
        (await rig.Engine.GetInstanceAsync(Tenant, other))!.Status.Should().Be(ProcessStatus.Waiting, "other trees are untouched");
        (await rig.DrainAsync()).Should().Be(0, "a later pass has nothing left to do for the tree");
        rig.Logs.Entries.Should().NotContain(entry => entry.EventId.Id == 12);
    }

    [Fact]
    public async Task AScheduleThatStartedACaseKeepsFiringAfterTheCaseIsPurgedAndNeverRecreatesIt()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));
        rig.Port("t.send");
        await rig.Engine.ActivateAsync(new ProcessActivation(Tenant, "host.trigger_schedule", "UTC"));
        rig.Time.Advance(TimeSpan.FromHours(30));
        await rig.DrainAsync();
        var started = await rig.ColumnAsync<string>("select instance_id from {schema}.process_instance order by created_at, instance_id");
        started.Should().NotBeEmpty("the schedule fired");
        foreach (var id in started)
        {
            await rig.Engine.PurgeInstanceAsync(Tenant, id, PurgeMode.Force);
        }

        (await rig.CountAsync("process_instance")).Should().Be(0);
        (await rig.CountAsync("process_schedule")).Should().BeGreaterThan(0, "the schedule rows are configuration, removed by DeactivateAsync only");
        rig.Time.Advance(TimeSpan.FromHours(30));
        var handled = await rig.DrainAsync();

        handled.Should().BeGreaterThan(0, "the schedule fires again, for its next occurrences");
        (await rig.ColumnAsync<string>("select instance_id from {schema}.process_instance")).Should().NotIntersectWith(started);
        rig.Logs.Entries.Should().NotContain(entry => entry.EventId.Id == 12);
    }

    [Fact]
    public async Task AnInboxRowWhoseInstanceDoesNotExistIsLeftAloneWithoutAnError()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.ExecuteAsync("insert into {schema}.process_inbox (tenant_id, instance_id, kind, payload, created_at, status) values (@t, 'ghost', 2, '{}', @at, 1)", ("t", Tenant), ("at", Rig.Start));

        var handled = await rig.Pump.RunOnceAsync();

        handled.Should().Be(0);
        (await rig.CountAsync("process_inbox", "status = 1")).Should().Be(1, "the pump does not delete rows it does not own; nothing in the host writes such a row");
        (await rig.CountAsync("process_instance")).Should().Be(0, "no instance is created for it");
        rig.Logs.Entries.Should().NotContain(entry => entry.EventId.Id == 12);
    }

    [Fact]
    public async Task ADeliveryThatSucceededAfterThePurgeIsDroppedWithAnOrphanedLineAndTheNewCaseOfTheSameKeyIsNotTouched()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("read-timer-action"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = string.Empty;
        var read = rig.Port("t.read");
        read.Handler = async (call, _) =>
        {
            if (call.TenantId == Tenant && call.Request.EffectId == blocked)
            {
                entered.TrySetResult();
                await release.Task;
            }

            return new PortCompleted("{\"amount\": 5}");
        };
        rig.Port("t.act");
        var victim = await rig.StartAsync("host.rta", "k-victim");
        var neighbour = await rig.StartAsync("host.rta", "k-neighbour");
        blocked = await rig.ScalarAsync<string>("select effect_id from {schema}.process_effect where instance_id = @i", ("i", victim));

        var pass = Task.Run(() => rig.Pump.RunOnceAsync());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var purged = await rig.Engine.PurgeInstanceAsync(Tenant, victim, PurgeMode.Force);
        purged.Counts.Effects.Should().Be(1, "the effect is Running, and a purge does not wait for the port");
        var restarted = await rig.StartAsync("host.rta", "k-victim");
        var restartedRevision = await rig.ScalarAsync<long>("select revision from {schema}.process_instance where instance_id = @i", ("i", restarted));
        release.SetResult();
        var handled = await pass.WaitAsync(TimeSpan.FromSeconds(30));

        handled.Should().BeGreaterThan(0);
        var orphaned = rig.Logs.Entries.Where(entry => entry.EventId.Id == 21).Should().ContainSingle().Subject;
        orphaned.Level.Should().Be(LogLevel.Warning);
        orphaned.Message.Should().Be("process.effect.orphaned: tenant " + Tenant + " instance " + victim + " effect " + blocked + " came back after its instance or its effect row was purged; the result is dropped");
        orphaned.State.Select(pair => pair.Key).Should().BeEquivalentTo("TenantId", "InstanceId", "EffectId", "{OriginalFormat}");
        rig.Logs.Entries.Should().NotContain(entry => entry.EventId.Id == 12, "the pass did not fail on it");
        (await PurgeSeed.RowsOfAsync(rig, Tenant, victim)).Should().Be(0, "the result of the port created no row");
        (await rig.Engine.GetInstanceAsync(Tenant, neighbour))!.Status.Should().Be(ProcessStatus.Waiting, "the pass went on with the effect of another instance, which finished its read");
        (await rig.ScalarAsync<long>("select revision from {schema}.process_instance where instance_id = @i", ("i", restarted))).Should().Be(restartedRevision, "the answer was for the purged instance, not for the one started under the same key");
        (await rig.CountAsync("process_effect", "instance_id = @i and status = 1", ("i", restarted))).Should().Be(1, "its own effect is still pending");
    }

    [Fact]
    public async Task APurgeOfALiveTreeWithEveryKindOfOutstandingWorkLeavesAPumpPassNothingToDo()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("parent"), Fixtures.Host("child"), Fixtures.Host("task-timeout"), Fixtures.Host("read-timer-action"));
        rig.Port("t.read");
        rig.Port("t.act");
        var parent = await rig.StartAsync("host.parent", "k-parent");
        await rig.DrainAsync();
        var child = (await rig.Engine.FindInstanceAsync(Tenant, "host.child", parent + "/spawn/1/child"))!.InstanceId;
        // Outstanding work of the tree: a waiting child with an open task, then a due timer, a pending effect and a pending inbox row.
        await rig.ExecuteAsync("insert into {schema}.process_timer (tenant_id, timer_id, instance_id, due_at) values (@t, 'due-timer', @i, @at)", ("t", Tenant), ("i", child), ("at", Rig.Start.AddHours(-1)));
        await rig.ExecuteAsync(
            "insert into {schema}.process_effect (tenant_id, effect_id, instance_id, step, idempotency_key, kind, port, input, attempt, delivery, status, due_at) values (@t, 'pending-effect', @i, 'hold', 'key', 1, 't.read', '{}', 1, 1, 1, @at)",
            ("t", Tenant),
            ("i", child),
            ("at", Rig.Start.AddHours(-1)));
        await rig.ExecuteAsync("insert into {schema}.process_inbox (tenant_id, instance_id, kind, payload, created_at, status) values (@t, @i, 2, '{}', @at, 1)", ("t", Tenant), ("i", child), ("at", Rig.Start));
        (await rig.Engine.ListOpenTasksAsync(Tenant, "child_q")).Should().ContainSingle();
        var other = await rig.StartAsync("host.task", "k-other");

        var result = await rig.Engine.PurgeInstanceAsync(Tenant, parent, PurgeMode.Force);
        var handled = await rig.Pump.RunOnceAsync();

        result.Status.Should().Be(PurgeStatus.Purged);
        result.Counts.Instances.Should().Be(2);
        result.Counts.Tasks.Should().Be(1);
        result.Counts.Timers.Should().Be(1);
        result.Counts.Effects.Should().Be(1);
        result.Counts.Inbox.Should().BeGreaterThanOrEqualTo(2);
        handled.Should().Be(0, "nothing of the tree is left for a pass to handle");
        (await rig.Engine.ListOpenTasksAsync(Tenant, "child_q")).Should().BeEmpty();
        (await PurgeSeed.RowsOfAsync(rig, Tenant, parent)).Should().Be(0);
        (await PurgeSeed.RowsOfAsync(rig, Tenant, child)).Should().Be(0);
        (await rig.Engine.GetInstanceAsync(Tenant, other))!.Status.Should().Be(ProcessStatus.Waiting);
        rig.Logs.Entries.Should().NotContain(entry => entry.EventId.Id == 12);
    }

    [Fact]
    public async Task APurgeAndPumpsOnTheSameTreeNeverDeadlockAndTheTreeIsWholeOrGoneAtEveryObservation()
    {
        await using var rig = await Rig.CreateAsync(configureHost: host =>
        {
            host.PortTimeout = TimeSpan.FromDays(1);
            host.EffectLease = TimeSpan.FromDays(7);
        });
        rig.Catalog.Add(Fixtures.Host("read-timer-action"), Fixtures.Host("parent"), Fixtures.Host("child"));
        rig.Port("t.read").Handler = (_, _) => Task.FromResult<PortResult>(new PortCompleted("{\"amount\": 5}"));
        rig.Port("t.act");
        var pumps = new[] { rig.Pump, rig.NewPump(), rig.NewPump() };
        var purgedWhileBusy = 0;

        for (var round = 0; round < Rounds; round++)
        {
            string[] ids;
            if (round % 2 == 0)
            {
                ids = [await rig.StartAsync("host.rta", "k-" + round)];
            }
            else
            {
                var parent = await rig.StartAsync("host.parent", "k-" + round);
                var child = (await rig.Engine.FindInstanceAsync(Tenant, "host.child", parent + "/spawn/1/child"))!.InstanceId;
                ids = [parent, child];
            }

            var delay = (round * 7) % 6;
            var stop = false;
            var pumping = pumps.Select(pump => Task.Run(async () =>
            {
                while (!Volatile.Read(ref stop))
                {
                    await pump.RunOnceAsync();
                    await Task.Yield();
                }
            })).ToList();
            var observing = Task.Run(async () =>
            {
                while (!Volatile.Read(ref stop))
                {
                    var (instances, others) = await ObserveAsync(rig, ids);
                    instances.Should().BeOneOf(0, ids.Length);
                    if (instances == 0)
                    {
                        others.Should().Be(0, "a tree whose instances are gone has no row left in any table");
                    }
                }
            });

            await Task.Delay(delay);
            var result = await rig.DriveAsync(rig.Engine.PurgeInstanceAsync(Tenant, ids[0], PurgeMode.Force));
            Volatile.Write(ref stop, true);
            await Task.WhenAll(pumping.Append(observing));

            result.Status.Should().Be(PurgeStatus.Purged, "round " + round);
            result.Counts.Instances.Should().Be(ids.Length, "round " + round);
            (await ObserveAsync(rig, ids)).Should().Be((0, 0L), "round " + round);
            if (result.Counts.Inbox > ids.Length || result.Counts.Effects > 0)
            {
                purgedWhileBusy++;
            }
        }

        output.WriteLine("rounds: " + Rounds + ", rounds with rows of the pump's work in the tree when it was purged: " + purgedWhileBusy + ", effect results dropped as orphaned: " + rig.Logs.Entries.Count(entry => entry.EventId.Id == 21));
        rig.Logs.Entries.Should().NotContain(entry => entry.EventId.Id == 12, "no pump item failed, which a deadlock (40P01) would have caused");
        (await rig.CountAsync("process_instance")).Should().Be(0);
        (await rig.CountAsync("process_inbox")).Should().Be(0);
        (await rig.CountAsync("process_effect")).Should().Be(0);
    }

    [Fact]
    public async Task NoLogRecordOfAPurgeCarriesABusinessKeyACorrelationAnEventIdOrAPayload()
    {
        const string Key = "zq-key-4417";
        const string Reference = "zq-ref-5530";
        const string EventId = "zq-event-8821";
        const string Payload = "zq-payload-9902";
        var sentinels = new[] { Key, Reference, EventId, Payload };
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("read-timer-action"), Fixtures.Host("wait-reply"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Port("t.read").Handler = async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return new PortCompleted("{\"amount\": 5}");
        };
        rig.Port("t.act");
        var waiting = await rig.StartAsync("host.wait", Key + "-w", "{\"case_ref\":\"" + Reference + "\"}");
        await rig.Engine.PublishEventAsync(new ProcessEvent(Tenant, EventId, "reply", Reference, "{\"text\":\"" + Payload + "\"}"));
        var reading = await rig.StartAsync("host.rta", Key, "{\"case_ref\":\"" + Reference + "\"}");
        var pass = Task.Run(() => rig.Pump.RunOnceAsync());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        (await rig.Engine.PurgeInstanceAsync(Tenant, waiting, PurgeMode.Force)).Status.Should().Be(PurgeStatus.Purged);
        (await rig.Engine.PurgeInstanceAsync(Tenant, reading, PurgeMode.Force)).Status.Should().Be(PurgeStatus.Purged);
        release.SetResult();
        await pass.WaitAsync(TimeSpan.FromSeconds(30));

        var entries = rig.Logs.Entries;
        entries.Count(entry => entry.EventId.Id == 20).Should().Be(2);
        entries.Should().Contain(entry => entry.EventId.Id == 21, "the flow logged the orphaned effect, so the check below is not vacuous");
        foreach (var entry in entries)
        {
            foreach (var sentinel in sentinels)
            {
                entry.Message.Should().NotContain(sentinel);
                (entry.ExceptionText ?? string.Empty).Should().NotContain(sentinel);
                entry.State.Select(pair => Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Should().NotContain(value => value.Contains(sentinel, StringComparison.Ordinal));
            }
        }
    }

    /// <summary>
    /// Builds the tree whose work a pump finds, returns the kind of scan that finds it and the root to purge. The clock is moved or
    /// the rows are written so that exactly that work is due.
    /// </summary>
    private static async Task<(WorkKind Scan, string Instance)> PrepareAsync(Rig rig, string kind)
    {
        switch (kind)
        {
            case "timer":
                var task = await rig.StartAsync("host.task", "k-timer");
                await rig.DrainAsync();
                rig.Time.Advance(TimeSpan.FromHours(2));
                return (WorkKind.DueTimer, task);
            case "inbox":
                var wait = await rig.StartAsync("host.wait", "k-inbox");
                await rig.DrainAsync();
                (await rig.Engine.PublishEventAsync(new ProcessEvent(Tenant, "e-1", "reply", "c-1", "{\"text\":\"x\"}"))).Status.Should().Be(PublishStatus.Queued);
                return (WorkKind.Inbox, wait);
            case "effect":
                return (WorkKind.DueEffect, await rig.StartAsync("host.rta", "k-effect"));
            case "lease":
                var leased = await rig.StartAsync("host.rta", "k-lease");
                await rig.ExecuteAsync("update {schema}.process_effect set status = 2, lease_until = @past, lease_owner = 'a-crashed-pump' where instance_id = @i", ("i", leased), ("past", Rig.Start.AddMinutes(-1)));
                return (WorkKind.ExpiredLease, leased);
            default:
                // A child finished: the notice for its parent is an inbox row of the parent, and the parent is purged with it.
                var parent = await rig.StartAsync("host.parent", "k-parent");
                await rig.DrainAsync();
                var child = (await rig.Engine.FindInstanceAsync(Tenant, "host.child", parent + "/spawn/1/child"))!.InstanceId;
                var childTask = (await rig.Engine.ListOpenTasksAsync(Tenant, "child_q")).Single();
                (await rig.Engine.CompleteTaskAsync(Tenant, childTask.TaskId, "finish", "{\"value\":\"v\"}")).Disposition.Should().Be(AdvanceDisposition.Applied);
                (await rig.CountAsync("process_inbox", "instance_id = @i and status = 1", ("i", parent))).Should().Be(1);
                child.Should().NotBeNullOrEmpty();
                return (WorkKind.Inbox, parent);
        }
    }

    /// <summary>The rows of every table that belong to an instance other than the given one.</summary>
    private static async Task<long> RowsExceptAsync(Rig rig, string kept)
    {
        var total = await rig.CountAsync("process_instance", "instance_id <> @i", ("i", kept));
        foreach (var table in PurgeSeed.RowTables)
        {
            total += await rig.CountAsync(table, "instance_id <> @i", ("i", kept));
        }

        return total;
    }

    /// <summary>One statement: the instances of the tree that exist, and the rows of every other table that belong to them.</summary>
    private static async Task<(long Instances, long Others)> ObserveAsync(Rig rig, string[] ids)
    {
        var instances = await rig.ColumnAsync<long>(
            "select (select count(*) from {schema}.process_instance where tenant_id = @t and instance_id = any(@ids)) * 1000000 "
            + "+ (select count(*) from {schema}.process_history where tenant_id = @t and instance_id = any(@ids)) "
            + "+ (select count(*) from {schema}.process_timer where tenant_id = @t and instance_id = any(@ids)) "
            + "+ (select count(*) from {schema}.process_wait where tenant_id = @t and instance_id = any(@ids)) "
            + "+ (select count(*) from {schema}.process_task where tenant_id = @t and instance_id = any(@ids)) "
            + "+ (select count(*) from {schema}.process_effect where tenant_id = @t and instance_id = any(@ids)) "
            + "+ (select count(*) from {schema}.process_inbox where tenant_id = @t and instance_id = any(@ids))",
            ("t", Tenant),
            ("ids", ids));
        return (instances[0] / 1000000, instances[0] % 1000000);
    }
}

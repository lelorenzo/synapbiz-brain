using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>The purge commands: what a purge removes and reports, what it refuses, and what it never touches.</summary>
public sealed class PurgeTests
{
    private const string A = "tenant-a";
    private const string B = "tenant-b";

    private static readonly DateTimeOffset T0 = PurgeSeed.T0;

    [Fact]
    public async Task APurgeOfAFinishedTreeRemovesEveryRowOfItAndReportsTheExactCounts()
    {
        await using var rig = await Rig.CreateAsync();
        var expected = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Completed, T0);
        var otherTree = await PurgeSeed.TreeAsync(rig, A, "r2", PurgeSeed.Completed, T0);
        var otherTenant = await PurgeSeed.TreeAsync(rig, B, "r1", PurgeSeed.Completed, T0);
        expected.Should().Be(new PurgeCounts(4, 10, 4, 6, 8, 14, 10), "the seed writes a different number of rows per instance");

        var result = await rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.FinishedOnly);

        result.Should().Be(new PurgeResult(PurgeStatus.Purged, expected));
        (await PurgeSeed.RowsOfAsync(rig, A, "r1")).Should().Be(0);
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(otherTree, "another tree of the same tenant is untouched");
        (await PurgeSeed.CountsAsync(rig, B)).Should().Be(otherTenant, "the identical tree of another tenant is untouched");
        (await rig.Engine.GetInstanceAsync(A, "r1")).Should().BeNull();
        (await rig.Engine.GetInstanceAsync(A, "r2")).Should().NotBeNull();
    }

    [Fact]
    public async Task ADescendantOfAFinishedRootGoesWithItWhateverItsStatusAndAFinishedRootIsEnoughInBothModes()
    {
        await using var rig = await Rig.CreateAsync();
        var waiting = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Cancelled, T0, PurgeSeed.Waiting);
        var suspended = await PurgeSeed.TreeAsync(rig, A, "r2", PurgeSeed.Completed, T0, PurgeSeed.Suspended);

        (await rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.FinishedOnly)).Should().Be(new PurgeResult(PurgeStatus.Purged, waiting));
        (await rig.Engine.PurgeInstanceAsync(A, "r2", PurgeMode.Force)).Should().Be(new PurgeResult(PurgeStatus.Purged, suspended));

        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(PurgeCounts.None);
    }

    [Fact]
    public async Task AnUnknownInstanceIsNotFoundInBothModesWithZeroCountsAndARetryAfterAPurgeIsNotFoundToo()
    {
        await using var rig = await Rig.CreateAsync();
        var seeded = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Completed, T0);

        var unknown = await rig.Engine.PurgeInstanceAsync(A, "nope", PurgeMode.Force);
        var otherTenant = await rig.Engine.PurgeInstanceAsync(B, "r1", PurgeMode.Force);
        var first = await rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.FinishedOnly);
        var retry = await rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.FinishedOnly);
        var retryForce = await rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.Force);

        unknown.Should().Be(new PurgeResult(PurgeStatus.NotFound, PurgeCounts.None));
        otherTenant.Should().Be(new PurgeResult(PurgeStatus.NotFound, PurgeCounts.None), "the id of another tenant behaves as an unknown id");
        first.Counts.Should().Be(seeded);
        retry.Should().Be(new PurgeResult(PurgeStatus.NotFound, PurgeCounts.None));
        retryForce.Should().Be(new PurgeResult(PurgeStatus.NotFound, PurgeCounts.None));
    }

    [Fact]
    public async Task AChildIsRefusedAsNotARootInBothModesAndNothingChanges()
    {
        await using var rig = await Rig.CreateAsync();
        var seeded = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Completed, T0, PurgeSeed.Completed);

        foreach (var child in new[] { "r1-c1", "r1-g1" })
        {
            (await rig.Engine.PurgeInstanceAsync(A, child, PurgeMode.FinishedOnly)).Should().Be(new PurgeResult(PurgeStatus.Refused, PurgeCounts.None, "process.purge.not_root"));
            (await rig.Engine.PurgeInstanceAsync(A, child, PurgeMode.Force)).Should().Be(new PurgeResult(PurgeStatus.Refused, PurgeCounts.None, "process.purge.not_root"));
        }

        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(seeded);
    }

    [Theory]
    [InlineData(PurgeSeed.Created)]
    [InlineData(PurgeSeed.Running)]
    [InlineData(PurgeSeed.Waiting)]
    [InlineData(PurgeSeed.Suspended)]
    public async Task AFinishedOnlyPurgeRefusesARootThatIsNotFinishedAndForceRemovesIt(int status)
    {
        await using var rig = await Rig.CreateAsync();
        var seeded = await PurgeSeed.TreeAsync(rig, A, "r1", status, T0);

        var refused = await rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.FinishedOnly);

        refused.Should().Be(new PurgeResult(PurgeStatus.Refused, PurgeCounts.None, "process.purge.not_finished"));
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(seeded, "a refusal changes nothing");

        var forced = await rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.Force);

        forced.Should().Be(new PurgeResult(PurgeStatus.Purged, seeded));
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(PurgeCounts.None);
    }

    [Fact]
    public async Task OnlyTheRootStatusIsLookedAtSoAFinishedChildOfARunningRootIsStillRefusedAsNotARoot()
    {
        await using var rig = await Rig.CreateAsync();
        var seeded = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Waiting, T0, PurgeSeed.Completed);

        (await rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.FinishedOnly)).Code.Should().Be("process.purge.not_finished");
        (await rig.Engine.PurgeInstanceAsync(A, "r1-c1", PurgeMode.FinishedOnly)).Code.Should().Be("process.purge.not_root");
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(seeded);
    }

    [Fact]
    public async Task ATreeDeeperThanTheCockpitWalkIsRemovedWholeAtAnyDepth()
    {
        await using var rig = await Rig.CreateAsync();
        const int Depth = 40;
        await PurgeSeed.InstanceAsync(rig, A, "chain-0", "p", "k-0", PurgeSeed.Completed, null, T0, T0);
        for (var level = 1; level <= Depth; level++)
        {
            await PurgeSeed.InstanceAsync(rig, A, "chain-" + level, "p", "k-" + level, PurgeSeed.Waiting, "chain-" + (level - 1), T0.AddSeconds(level), T0.AddSeconds(level));
            await PurgeSeed.RowsAsync(rig, A, "chain-" + level, level % 4);
        }

        await PurgeSeed.InstanceAsync(rig, A, "other-0", "p", "k-other", PurgeSeed.Completed, null, T0, T0);
        (await rig.Engine.GetCaseAsync(A, "chain-0"))!.Descendants.Should().HaveCount(16, "the cockpit walk stops at depth 16");

        var result = await rig.Engine.PurgeInstanceAsync(A, "chain-0", PurgeMode.FinishedOnly);

        result.Status.Should().Be(PurgeStatus.Purged);
        result.Counts.Instances.Should().Be(Depth + 1, "a purge never leaves a deeper descendant behind");
        (await PurgeSeed.RowsOfAsync(rig, A, "chain")).Should().Be(0);
        (await rig.CountAsync("process_instance")).Should().Be(1);
    }

    [Fact]
    public async Task AChildThatAnotherTransactionCommittedWhilePurgeWaitedForItsParentIsInTheTreeToo()
    {
        await using var rig = await Rig.CreateAsync();
        await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Waiting, T0);
        await using var other = await rig.HoldAsync("r1-g1", A);
        var inserted = await other.InsertInstanceAsync(new NewInstance(A, "r1-late", "p", 1, "h", "k-late", "r1-g1", "late", T0), CancellationToken.None);
        inserted.Inserted.Should().BeTrue();

        var purge = Task.Run(() => rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.Force));
        await WaitUntilBlockedAsync(rig);
        purge.IsCompleted.Should().BeFalse("the purge waits for the lock of the grandchild");
        await other.CommitAsync(CancellationToken.None);
        var result = await purge;

        result.Status.Should().Be(PurgeStatus.Purged);
        result.Counts.Instances.Should().Be(5, "the late child is found by the walk that follows the locks");
        (await rig.CountAsync("process_instance")).Should().Be(0);
    }

    [Fact]
    public async Task AfterAPurgeTheBusinessKeyIsFreeAndAStartCreatesANewInstance()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-reply"));
        var first = await rig.Engine.StartAsync(new StartRequest(A, "host.wait", "k-1", "{\"case_ref\":\"c-1\"}"));
        await rig.DrainAsync();
        (await rig.Engine.StartAsync(new StartRequest(A, "host.wait", "k-1", "{\"case_ref\":\"c-1\"}"))).Should().Be(new StartResult(first.InstanceId, false));

        var purged = await rig.Engine.PurgeInstanceAsync(A, first.InstanceId, PurgeMode.Force);
        var again = await rig.Engine.StartAsync(new StartRequest(A, "host.wait", "k-1", "{\"case_ref\":\"c-1\"}"));
        await rig.DrainAsync();

        purged.Status.Should().Be(PurgeStatus.Purged);
        again.Created.Should().BeTrue();
        again.InstanceId.Should().NotBe(first.InstanceId);
        (await rig.Engine.FindInstanceAsync(A, "host.wait", "k-1"))!.InstanceId.Should().Be(again.InstanceId);
        (await rig.Engine.GetInstanceAsync(A, again.InstanceId))!.Status.Should().Be(ProcessStatus.Waiting);
        (await rig.Engine.GetHistoryAsync(A, first.InstanceId)).Should().BeEmpty();
        (await rig.CountAsync("process_instance")).Should().Be(1);
    }

    [Theory]
    [InlineData("process_history")]
    [InlineData("process_timer")]
    [InlineData("process_wait")]
    [InlineData("process_task")]
    [InlineData("process_effect")]
    [InlineData("process_inbox")]
    [InlineData("process_instance")]
    public async Task AFailureInjectedMidwayThroughTheDeletesLeavesEveryRowOfTheTree(string failingTable)
    {
        await using var rig = await Rig.CreateAsync();
        var seeded = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Completed, T0);
        var other = await PurgeSeed.TreeAsync(rig, B, "r1", PurgeSeed.Completed, T0);
        await rig.ExecuteAsync(
            "create function {schema}.fail_delete() returns trigger language plpgsql as $$ begin raise exception 'injected failure' using errcode = 'XX999'; end $$; "
            + "create trigger fail_delete before delete on {schema}." + failingTable + " for each statement execute function {schema}.fail_delete()");

        var failed = await FluentActions.Awaiting(() => rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.Force)).Should().ThrowAsync<PostgresException>();

        failed.Which.SqlState.Should().Be("XX999");
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(seeded, "the deletes that ran before the failure are rolled back");
        (await PurgeSeed.CountsAsync(rig, B)).Should().Be(other);
        rig.Logs.Entries.Should().NotContain(entry => entry.Message.StartsWith("process.instance.purged", StringComparison.Ordinal), "nothing is logged for a purge that did not commit");

        await rig.ExecuteAsync("drop trigger fail_delete on {schema}." + failingTable);
        (await rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.Force)).Counts.Should().Be(seeded, "the purge works once the failure is gone");
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(PurgeCounts.None);
    }

    [Fact]
    public async Task ASuccessfulPurgeLogsOneLinePerTreeWithTheIdsAndTheCounts()
    {
        await using var rig = await Rig.CreateAsync();
        var seeded = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Completed, T0);
        await rig.Engine.PurgeInstanceAsync(A, "nope", PurgeMode.Force);
        await rig.Engine.PurgeInstanceAsync(A, "r1-c1", PurgeMode.Force);

        await rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.FinishedOnly);

        var lines = rig.Logs.Entries.Where(entry => entry.EventId.Id == 20).ToList();
        var line = lines.Should().ContainSingle("a purge logs one line per tree, and a refusal or an unknown id logs none").Subject;
        line.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Information);
        line.Message.Should().StartWith("process.instance.purged: tenant tenant-a root instance r1 ");
        line.State.Single(pair => pair.Key == "Counts").Value.Should().Be(seeded);
        line.State.Select(pair => pair.Key).Should().BeEquivalentTo("TenantId", "InstanceId", "Counts", "{OriginalFormat}");
    }

    [Fact]
    public async Task PurgeArgumentsAreCheckedBeforeTheDatabaseIsTouched()
    {
        await using var rig = await Rig.CreateAsync();
        var engine = rig.Engine;
        var tooLong = new string('x', 201);

        await FluentActions.Awaiting(() => engine.PurgeInstanceAsync(null!, "i", PurgeMode.Force)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => engine.PurgeInstanceAsync("", "i", PurgeMode.Force)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.PurgeInstanceAsync(tooLong, "i", PurgeMode.Force)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.PurgeInstanceAsync("a\u0000b", "i", PurgeMode.Force)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.PurgeInstanceAsync(A, null!, PurgeMode.Force)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => engine.PurgeInstanceAsync(A, "", PurgeMode.Force)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.PurgeInstanceAsync(A, tooLong, PurgeMode.Force)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.PurgeInstanceAsync(A, "i\n", PurgeMode.Force)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.PurgeInstanceAsync(A, "i", default)).Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("mode");
        await FluentActions.Awaiting(() => engine.PurgeInstanceAsync(A, "i", (PurgeMode)3)).Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("mode");

        await FluentActions.Awaiting(() => engine.PurgeFinishedBeforeAsync(null!, T0, 1)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => engine.PurgeFinishedBeforeAsync("", T0, 1)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.PurgeFinishedBeforeAsync(tooLong, T0, 1)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.PurgeFinishedBeforeAsync("t\u0001", T0, 1)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.PurgeFinishedBeforeAsync(A, T0, 0)).Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("limit");
        await FluentActions.Awaiting(() => engine.PurgeFinishedBeforeAsync(A, T0, 501)).Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("limit");
        await FluentActions.Awaiting(() => engine.PurgeFinishedBeforeAsync(A, T0, -1)).Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("limit");

        await FluentActions.Awaiting(() => engine.PurgeEventsBeforeAsync(null!, T0, 1)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => engine.PurgeEventsBeforeAsync("", T0, 1)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.PurgeEventsBeforeAsync(tooLong, T0, 1)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.PurgeEventsBeforeAsync("t\r", T0, 1)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.PurgeEventsBeforeAsync(A, T0, 0)).Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("limit");
        await FluentActions.Awaiting(() => engine.PurgeEventsBeforeAsync(A, T0, 5001)).Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("limit");

        (await engine.PurgeFinishedBeforeAsync(A, T0, 500)).Should().Be(new PurgeBatchResult(0, PurgeCounts.None, false));
        (await engine.PurgeFinishedBeforeAsync(A, T0, 1)).Should().Be(new PurgeBatchResult(0, PurgeCounts.None, false));
        (await engine.PurgeEventsBeforeAsync(A, T0, 5000)).Should().Be(new PurgeEventsResult(0, false));
        (await engine.PurgeEventsBeforeAsync(A, T0, 1)).Should().Be(new PurgeEventsResult(0, false));
    }

    [Fact]
    public async Task APurgeFinishedBeforeUsesAStrictBoundaryAndNeverTouchesAnUnfinishedRoot()
    {
        await using var rig = await Rig.CreateAsync();
        var early = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Completed, T0.AddDays(1));
        var atBoundary = await PurgeSeed.TreeAsync(rig, A, "r2", PurgeSeed.Cancelled, T0.AddDays(2));
        var late = await PurgeSeed.TreeAsync(rig, A, "r3", PurgeSeed.Completed, T0.AddDays(2).AddTicks(10));
        var running = await PurgeSeed.TreeAsync(rig, A, "r4", PurgeSeed.Waiting, T0);
        var suspended = await PurgeSeed.TreeAsync(rig, A, "r5", PurgeSeed.Suspended, T0);
        var created = await PurgeSeed.TreeAsync(rig, A, "r6", PurgeSeed.Created, T0);
        var runningWithFinishedChild = await PurgeSeed.TreeAsync(rig, A, "r7", PurgeSeed.Waiting, T0, PurgeSeed.Completed);

        var first = await rig.Engine.PurgeFinishedBeforeAsync(A, T0.AddDays(2), 10);

        first.Should().Be(new PurgeBatchResult(1, early, false), "the instant itself is not earlier than the instant, so the tree updated at that instant does not qualify");
        (await PurgeSeed.RowsOfAsync(rig, A, "r1")).Should().Be(0);
        (await PurgeSeed.RowsOfAsync(rig, A, "r2")).Should().BePositive();

        var second = await rig.Engine.PurgeFinishedBeforeAsync(A, T0.AddDays(2).AddTicks(10), 10);

        second.Should().Be(new PurgeBatchResult(1, atBoundary, false), "one microsecond is the precision of the database, and the tree one microsecond later still does not qualify");

        var third = await rig.Engine.PurgeFinishedBeforeAsync(A, T0.AddDays(3), 10);

        third.Should().Be(new PurgeBatchResult(1, late, false));
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(running.Plus(suspended).Plus(created).Plus(runningWithFinishedChild), "unfinished roots and their finished children stay");
    }

    [Fact]
    public async Task APurgeFinishedBeforePurgesTheOldestFirstUpToTheLimitAndSaysWhetherMoreQualify()
    {
        await using var rig = await Rig.CreateAsync();
        var trees = new List<PurgeCounts>();
        for (var index = 0; index < 7; index++)
        {
            // Written newest first, so that the order of the rows says nothing about the order of the purge.
            trees.Add(await PurgeSeed.TreeAsync(rig, A, "r" + (6 - index), PurgeSeed.Completed, T0.AddHours(6 - index)));
        }

        var batch = await rig.Engine.PurgeFinishedBeforeAsync(A, T0.AddDays(1), 3);

        batch.Trees.Should().Be(3);
        batch.More.Should().BeTrue();
        (await rig.ColumnAsync<string>("select instance_id from {schema}.process_instance where parent_instance_id is null order by instance_id")).Should().Equal("r3", "r4", "r5", "r6");

        var rest = await rig.Engine.PurgeFinishedBeforeAsync(A, T0.AddDays(1), 4);

        rest.Trees.Should().Be(4);
        rest.More.Should().BeFalse("nothing qualifies once the last tree is gone");
        batch.Counts.Plus(rest.Counts).Should().Be(trees.Aggregate(PurgeCounts.None, (sum, counts) => sum.Plus(counts)));
        (await rig.CountAsync("process_instance")).Should().Be(0);
    }

    [Fact]
    public async Task RootsThatShareAnUpdateInstantArePurgedInTheByteOrderOfTheirIds()
    {
        await using var rig = await Rig.CreateAsync();
        foreach (var id in new[] { "b", "a", "B", "_", "~"})
        {
            await PurgeSeed.InstanceAsync(rig, A, id, "p", "k-" + id, PurgeSeed.Completed, null, T0, T0);
        }

        var order = new List<string>();
        for (var round = 0; round < 5; round++)
        {
            var before = (await rig.ColumnAsync<string>("select instance_id from {schema}.process_instance")).ToHashSet(StringComparer.Ordinal);
            (await rig.Engine.PurgeFinishedBeforeAsync(A, T0.AddDays(1), 1)).Trees.Should().Be(1);
            var after = (await rig.ColumnAsync<string>("select instance_id from {schema}.process_instance")).ToHashSet(StringComparer.Ordinal);
            order.Add(before.Except(after, StringComparer.Ordinal).Single());
        }

        order.Should().Equal("B", "_", "a", "b", "~");
    }

    [Fact]
    public async Task ATreeThatAnotherTransactionHoldsIsSkippedAndFoundByTheNextCallEvenWhenItsOlderThanTheLimit()
    {
        await using var rig = await Rig.CreateAsync();
        var counts = new Dictionary<string, PurgeCounts>();
        for (var index = 0; index < 5; index++)
        {
            counts["r" + index] = await PurgeSeed.TreeAsync(rig, A, "r" + index, PurgeSeed.Completed, T0.AddHours(index));
        }

        await using (await rig.HoldAsync("r0", A))
        await using (await rig.HoldAsync("r1", A))
        {
            var batch = await rig.Engine.PurgeFinishedBeforeAsync(A, T0.AddDays(1), 2);

            batch.Trees.Should().Be(2, "the two locked trees, which are the oldest, are passed over and the next two are purged");
            batch.Counts.Should().Be(counts["r2"].Plus(counts["r3"]));
            batch.More.Should().BeTrue("the locked trees and r4 still qualify");
            (await PurgeSeed.RowsOfAsync(rig, A, "r0")).Should().BePositive();
            (await PurgeSeed.RowsOfAsync(rig, A, "r1")).Should().BePositive();
            (await PurgeSeed.RowsOfAsync(rig, A, "r2")).Should().Be(0);
            (await PurgeSeed.RowsOfAsync(rig, A, "r3")).Should().Be(0);

            var rest = await rig.Engine.PurgeFinishedBeforeAsync(A, T0.AddDays(1), 10);

            rest.Should().Be(new PurgeBatchResult(1, counts["r4"], true), "only a tree that someone still holds is left, and More says so");
        }

        var last = await rig.Engine.PurgeFinishedBeforeAsync(A, T0.AddDays(1), 10);

        last.Should().Be(new PurgeBatchResult(2, counts["r0"].Plus(counts["r1"]), false));
        (await rig.CountAsync("process_instance")).Should().Be(0);
    }

    [Fact]
    public async Task APurgeFinishedBeforeIsolatesTenantsAndTreatsAScheduleInstanceLikeAnyOther()
    {
        await using var rig = await Rig.CreateAsync();
        var scheduled = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Completed, T0);
        await rig.ExecuteAsync("update {schema}.process_instance set business_key = '@schedule/daily/20270201T0600Z' where instance_id = 'r1'");
        var otherTenant = await PurgeSeed.TreeAsync(rig, B, "r1", PurgeSeed.Completed, T0);

        var batch = await rig.Engine.PurgeFinishedBeforeAsync(A, T0.AddDays(1), 10);

        batch.Should().Be(new PurgeBatchResult(1, scheduled, false));
        (await PurgeSeed.CountsAsync(rig, B)).Should().Be(otherTenant);
        (await rig.Engine.PurgeFinishedBeforeAsync(B, T0.AddDays(1), 10)).Counts.Should().Be(otherTenant);
    }

    [Fact]
    public async Task APurgeFinishedBeforeSkipsARootThatStoppedQualifyingBetweenTheScanAndTheLock()
    {
        await using var rig = await Rig.CreateAsync();
        await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Completed, T0);
        await PurgeSeed.TreeAsync(rig, A, "r2", PurgeSeed.Completed, T0.AddHours(1));
        var acted = false;
        var hooked = new HookedStore(rig.Provider.GetRequiredService<IProcessStore>())
        {
            // A root the scan found is resumed (no longer finished) and another one is purged, before the call looks at them.
            AfterFinishedScan = async _ =>
            {
                if (!acted)
                {
                    acted = true;
                    await rig.ExecuteAsync("update {schema}.process_instance set status = 2 where instance_id = 'r1'");
                    await rig.ExecuteAsync("delete from {schema}.process_instance where instance_id = 'r2'");
                }
            },
        };
        var engine = new ProcessEngine(hooked, rig.Provider.GetRequiredService<ProcessRuntime>());

        var batch = await engine.PurgeFinishedBeforeAsync(A, T0.AddDays(1), 10);

        acted.Should().BeTrue();
        batch.Trees.Should().Be(0);
        batch.More.Should().BeFalse("the first root is no longer finished and the second is gone");
        (await rig.CountAsync("process_instance", "instance_id like 'r1%'")).Should().Be(4);
    }

    [Fact]
    public async Task PurgeEventsBeforeUsesAStrictBoundaryTheLimitTheOldestFirstAndSaysWhetherMoreQualify()
    {
        await using var rig = await Rig.CreateAsync();
        for (var index = 0; index < 6; index++)
        {
            await rig.ExecuteAsync("insert into {schema}.process_event (tenant_id, event_id, name, received_at) values (@t, @e, 'ev', @at)", ("t", A), ("e", "e-" + (5 - index)), ("at", T0.AddHours(5 - index)));
        }

        (await rig.Engine.PurgeEventsBeforeAsync(A, T0, 10)).Should().Be(new PurgeEventsResult(0, false), "the instant itself is not before the instant");
        (await rig.Engine.PurgeEventsBeforeAsync(A, T0.AddHours(3), 2)).Should().Be(new PurgeEventsResult(2, true), "three rows qualify and the limit is two");
        (await rig.ColumnAsync<string>("select event_id from {schema}.process_event order by event_id")).Should().Equal("e-2", "e-3", "e-4", "e-5");
        (await rig.Engine.PurgeEventsBeforeAsync(A, T0.AddHours(3), 2)).Should().Be(new PurgeEventsResult(1, false));
        (await rig.Engine.PurgeEventsBeforeAsync(A, T0.AddHours(3).AddTicks(10), 10)).Should().Be(new PurgeEventsResult(1, false), "one microsecond is the precision of the database");
        (await rig.Engine.PurgeEventsBeforeAsync(A, T0.AddDays(1), 1)).Should().Be(new PurgeEventsResult(1, true));
        (await rig.Engine.PurgeEventsBeforeAsync(A, T0.AddDays(1), 5000)).Should().Be(new PurgeEventsResult(1, false));
        (await rig.CountAsync("process_event")).Should().Be(0);
    }

    [Fact]
    public async Task PurgeEventsBeforeIsolatesTenantsAndAPurgedEventIdIsAcceptedAgain()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-reply"));
        var id = await rig.StartAsync("host.wait", "k-1");
        (await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-1", "reply", "c-1", "{}"))).Status.Should().Be(PublishStatus.Queued);
        await rig.Engine.PublishEventAsync(new ProcessEvent(B, "e-1", "reply", "c-1", "{}"));
        (await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-1", "reply", "c-1", "{}"))).Status.Should().Be(PublishStatus.Duplicate);
        rig.Time.Advance(TimeSpan.FromDays(2));

        var purged = await rig.Engine.PurgeEventsBeforeAsync(rig.Tenant, rig.Time.GetUtcNow(), 100);

        purged.Should().Be(new PurgeEventsResult(1, false));
        (await rig.CountAsync("process_event", "tenant_id = @t", ("t", B))).Should().Be(1, "another tenant's event ids stay");
        (await rig.Engine.PublishEventAsync(new ProcessEvent(B, "e-1", "reply", "c-1", "{}"))).Status.Should().Be(PublishStatus.Duplicate);
        (await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-1", "reply", "c-1", "{}"))).Should().Be(new PublishResult(PublishStatus.Queued, 1), "the id is unknown again, so it is a new event and not a duplicate");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id)).Should().NotBeNull();
    }

    [Fact]
    public async Task TheTreeLockRefusesToDeleteWhatItDidNotLockAndLocksTheTreeInCreationOrderRootFirst()
    {
        await using var rig = await Rig.CreateAsync();
        await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Waiting, T0);
        await PurgeSeed.InstanceAsync(rig, A, "r1-a", "p", "k-a", PurgeSeed.Waiting, "r1", T0, T0);
        var store = rig.Provider.GetRequiredService<IProcessStore>();
        var root = new InstanceKey(A, "r1");
        await using var session = await store.BeginAsync(CancellationToken.None);

        await FluentActions.Awaiting(() => session.LockTreeAsync(root, CancellationToken.None)).Should().ThrowExactlyAsync<InvalidOperationException>("the root must be locked first");
        (await session.LockInstanceAsync(root, skipLocked: false, CancellationToken.None)).Should().NotBeNull();
        await FluentActions.Awaiting(() => session.DeleteTreeRowsAsync(root, PurgeTable.History, CancellationToken.None)).Should().ThrowExactlyAsync<InvalidOperationException>("the tree is not locked yet");
        var tree = await session.LockTreeAsync(root, CancellationToken.None);

        tree.Select(key => key.InstanceId).Should().Equal("r1", "r1-a", "r1-c1", "r1-c2", "r1-g1");
        tree.Should().OnlyContain(key => key.TenantId == A);
        (await session.DeleteTreeRowsAsync(root, PurgeTable.Instances, CancellationToken.None)).Should().Be(5);
        await FluentActions.Awaiting(() => session.DeleteTreeRowsAsync(new InstanceKey(A, "r1-c1"), PurgeTable.Instances, CancellationToken.None)).Should().ThrowExactlyAsync<InvalidOperationException>("the tree is deleted for its root");
    }

    /// <summary>Waits until a statement of another connection waits for a lock.</summary>
    internal static async Task WaitUntilBlockedAsync(Rig rig)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (await rig.ScalarAsync<long>("select count(*) from pg_stat_activity where datname = current_database() and wait_event_type = 'Lock' and pid <> pg_backend_pid()") > 0)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException("No statement waited for a lock.");
    }
}

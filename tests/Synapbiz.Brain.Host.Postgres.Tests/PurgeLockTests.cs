using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// The lock behaviour of a purge: it never waits for a row lock while it holds one, so it cannot take part in a deadlock; a tree that
/// stays locked makes the call busy after <see cref="ProcessHostOptions.PurgeLockTimeout"/> and changes nothing; a tree that grows or
/// changes while it is acquired is decided on only under the locks.
/// </summary>
public sealed class PurgeLockTests
{
    private const string A = "tenant-a";
    private const string B = "tenant-b";
    private const string Event = "ev";

    private static readonly DateTimeOffset T0 = PurgeSeed.T0;

    [Fact(Timeout = 120000)]
    public async Task ATreeThatStaysLockedForTheWholeTimeoutMakesTheCallBusyChangesNothingAndANewCallPurgesOnceItIsFree()
    {
        await using var rig = await Rig.CreateAsync(configureHost: host => host.PurgeLockTimeout = TimeSpan.FromMilliseconds(500));
        var seeded = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Waiting, T0);
        var other = await PurgeSeed.TreeAsync(rig, B, "r1", PurgeSeed.Waiting, T0);
        var holder = await rig.HoldAsync("r1-g1", A);
        PurgeResult busy;
        await using (holder)
        {
            var begun = rig.Time.GetTimestamp();
            busy = await rig.DriveAsync(rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.Force));

            rig.Time.GetElapsedTime(begun).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500), "the call kept trying for the whole timeout");
        }

        busy.Should().Be(new PurgeResult(PurgeStatus.Refused, PurgeCounts.None, "process.purge.busy"));
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(seeded, "a busy purge changes nothing");
        (await PurgeSeed.CountsAsync(rig, B)).Should().Be(other);
        rig.Logs.Entries.Should().NotContain(entry => entry.EventId.Id == 20, "nothing was purged, so nothing is logged");

        var again = await rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.Force);

        again.Should().Be(new PurgeResult(PurgeStatus.Purged, seeded));
    }

    [Fact(Timeout = 120000)]
    public async Task ALockThatIsReleasedBeforeTheTimeoutIsWaitedOutByTheSameCallWhichRetriesAndPurges()
    {
        await using var rig = await Rig.CreateAsync(configureHost: host => host.PurgeLockTimeout = TimeSpan.FromMinutes(1));
        var seeded = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Waiting, T0);
        var hooked = new HookedStore(rig.Store);
        var holder = await rig.HoldAsync("r1-c2", A);
        Task<PurgeResult> purge;
        await using (holder)
        {
            purge = rig.EngineOver(hooked).PurgeInstanceAsync(A, "r1", PurgeMode.Force);
            await AdvanceUntilAsync(rig, () => hooked.Sessions >= 4);
            purge.IsCompleted.Should().BeFalse("the tree is still held, and the timeout is a minute away");
            (await PurgeSeed.CountsAsync(rig, A)).Should().Be(seeded);
        }

        var result = await rig.DriveAsync(purge);

        result.Should().Be(new PurgeResult(PurgeStatus.Purged, seeded));
        hooked.Sessions.Should().BeGreaterThanOrEqualTo(5, "every attempt is one transaction, and the held tree made the first ones give up");
    }

    [Fact(Timeout = 120000)]
    public async Task ACancelledTokenEndsTheWaitBetweenAttemptsWithTheUsualCancellationAndNothingChanges()
    {
        await using var rig = await Rig.CreateAsync();
        var seeded = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Waiting, T0);
        var hooked = new HookedStore(rig.Store);
        using var cancel = new CancellationTokenSource();
        await using var holder = await rig.HoldAsync("r1", A);
        var purge = rig.EngineOver(hooked).PurgeInstanceAsync(A, "r1", PurgeMode.Force, cancel.Token);
        await AdvanceUntilAsync(rig, () => hooked.Sessions >= 3);

        await cancel.CancelAsync();

        await FluentActions.Awaiting(() => rig.DriveAsync(purge)).Should().ThrowAsync<OperationCanceledException>();
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(seeded);
    }

    [Fact(Timeout = 120000)]
    public async Task TheBatchTriesATreeOnceAndSkipsOneThatIsPartlyLockedThenPurgesItInTheNextCall()
    {
        await using var rig = await Rig.CreateAsync();
        var seeded = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Completed, T0);
        var hooked = new HookedStore(rig.Store);
        var engine = rig.EngineOver(hooked);
        PurgeBatchResult skipped;
        await using (await rig.HoldAsync("r1-g1", A))
        {
            skipped = await engine.PurgeFinishedBeforeAsync(A, T0.AddDays(1), 10).WaitAsync(TimeSpan.FromSeconds(30));
        }

        skipped.Should().Be(new PurgeBatchResult(0, PurgeCounts.None, true), "the tree still qualifies");
        hooked.Sessions.Should().Be(1, "one attempt, no retry, no wait");
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(seeded);

        var next = await engine.PurgeFinishedBeforeAsync(A, T0.AddDays(1), 10);

        next.Should().Be(new PurgeBatchResult(1, seeded, false));
    }

    [Fact(Timeout = 120000)]
    public async Task AChildCommittedBetweenTheLocksAndTheSecondWalkAlsoRestartsTheAttempt()
    {
        await using var rig = await Rig.CreateAsync();
        await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Waiting, T0);
        var inserted = false;
        var hooked = new HookedStore(rig.Store)
        {
            AfterLock = async _ =>
            {
                if (!inserted)
                {
                    inserted = true;
                    await PurgeSeed.InstanceAsync(rig, A, "r1-late", "p", "k-late", PurgeSeed.Waiting, "r1", T0, T0);
                    await PurgeSeed.RowsAsync(rig, A, "r1-late", 2);
                }
            },
        };

        var result = await rig.DriveAsync(rig.EngineOver(hooked).PurgeInstanceAsync(A, "r1", PurgeMode.Force));

        result.Status.Should().Be(PurgeStatus.Purged);
        result.Counts.Instances.Should().Be(5);
        hooked.Sessions.Should().Be(2);
        (await rig.CountAsync("process_instance")).Should().Be(0, "no child is left behind");
        (await rig.CountAsync("process_inbox")).Should().Be(0);
    }

    [Fact(Timeout = 120000)]
    public async Task TheStatusOfTheRootIsDecidedUnderTheLocksNotBeforeThem()
    {
        await using var rig = await Rig.CreateAsync();
        var seeded = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Completed, T0);
        var resumed = false;
        var hooked = new HookedStore(rig.Store)
        {
            // The root is finished when the tree is walked; before the locks are taken it is resumed.
            AfterTreeRead = async (walk, _) =>
            {
                if (walk == 1 && !resumed)
                {
                    resumed = true;
                    await rig.ExecuteAsync("update {schema}.process_instance set status = 2 where instance_id = 'r1'");
                }
            },
        };

        var instance = await rig.DriveAsync(rig.EngineOver(hooked).PurgeInstanceAsync(A, "r1", PurgeMode.FinishedOnly));

        instance.Should().Be(new PurgeResult(PurgeStatus.Refused, PurgeCounts.None, "process.purge.not_finished"));
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(seeded);

        await rig.ExecuteAsync("update {schema}.process_instance set status = 4 where instance_id = 'r1'");
        resumed = false;
        var batch = await rig.EngineOver(hooked).PurgeFinishedBeforeAsync(A, T0.AddDays(1), 10);

        batch.Should().Be(new PurgeBatchResult(0, PurgeCounts.None, false), "the root no longer qualifies under the locks, and the final scan agrees");
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(seeded);
    }

    [Fact(Timeout = 120000)]
    public async Task TheBoundaryOfBeforeIsStrictUnderTheLocksToo()
    {
        await using var rig = await Rig.CreateAsync();
        var seeded = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Completed, T0);
        var before = T0.AddDays(1);
        var touched = false;
        var hooked = new HookedStore(rig.Store)
        {
            // The scan found the root earlier than the cut-off; it is written again, at exactly the cut-off, before the locks.
            AfterFinishedScan = async found =>
            {
                if (found.Count > 0 && !touched)
                {
                    touched = true;
                    await rig.ExecuteAsync("update {schema}.process_instance set updated_at = @at where instance_id = 'r1'", ("at", before));
                }
            },
        };

        var batch = await rig.EngineOver(hooked).PurgeFinishedBeforeAsync(A, before, 10);

        touched.Should().BeTrue();
        batch.Trees.Should().Be(0, "an update time equal to the cut-off does not qualify");
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(seeded);
    }

    [Fact(Timeout = 120000)]
    public async Task ARowOfAnotherTenantThatHasTheSameIdIsNeitherWaitedForNorLockedByAPurge()
    {
        await using var rig = await Rig.CreateAsync();
        var seeded = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Waiting, T0);
        var other = await PurgeSeed.TreeAsync(rig, B, "r1", PurgeSeed.Waiting, T0);
        var store = rig.Store;
        IReadOnlyList<LockedInstance>? tenantBRows = null;
        var hooked = new HookedStore(store)
        {
            // While the purge of tenant A holds its locks, another session takes the rows of tenant B that have the same ids.
            AfterLock = async _ =>
            {
                await using var probe = await store.BeginAsync(CancellationToken.None);
                tenantBRows = await probe.TryLockInstancesAsync(B, ["r1", "r1-c1", "r1-c2", "r1-g1"], CancellationToken.None);
            },
        };
        await using var holder = await rig.HoldAsync("r1-g1", B);

        var result = await rig.EngineOver(hooked).PurgeInstanceAsync(A, "r1", PurgeMode.Force).WaitAsync(TimeSpan.FromSeconds(30));

        result.Should().Be(new PurgeResult(PurgeStatus.Purged, seeded), "the held row belongs to another tenant, so the tree is not busy");
        hooked.Sessions.Should().Be(1, "no retry");
        tenantBRows.Should().NotBeNull();
        tenantBRows!.Select(row => row.InstanceId).Should().Equal("r1", "r1-c1", "r1-c2");
        tenantBRows.Should().HaveCount(3, "the row held by the second connection is skipped and the other three are free: the purge of tenant A locked none of them");
        (await PurgeSeed.CountsAsync(rig, B)).Should().Be(other);
    }

    [Fact(Timeout = 120000)]
    public async Task WhenAChildIsCreatedInReverseIdOrderAPublishThatHoldsOneTargetNeverDeadlocksWithAPurgeOfTheTree()
    {
        await using var rig = await Rig.CreateAsync();
        // Root r, then its children b and a, created in that order: creation order is r, b, a and id order is a, b, r. Both children wait.
        await WaitingAsync(rig, "r", null, 0, waits: false);
        await WaitingAsync(rig, "b", "r", 1);
        await WaitingAsync(rig, "a", "r", 2);

        await RunPublishAgainstPurgeAsync(rig, "r", queued: 2, instances: 3);
    }

    [Fact(Timeout = 120000)]
    public async Task WhenTheRootSortsAfterAWaitingChildAPublishThatHoldsTheChildNeverDeadlocksWithAPurgeOfTheTree()
    {
        await using var rig = await Rig.CreateAsync();
        // The root is created first and sorts last: the publish takes the child first and then the root; the root is also a target.
        await WaitingAsync(rig, "z-root", null, 0);
        await WaitingAsync(rig, "a-child", "z-root", 1);

        await RunPublishAgainstPurgeAsync(rig, "z-root", queued: 2, instances: 2);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(300000, true)]
    [InlineData(300001, false)]
    public void ThePurgeLockTimeoutIsGreaterThanZeroAndAtMostFiveMinutesAndDefaultsToTenSeconds(int milliseconds, bool valid)
    {
        using var defaults = Build(_ => { });
        defaults.GetRequiredService<IOptions<ProcessHostOptions>>().Value.PurgeLockTimeout.Should().Be(TimeSpan.FromSeconds(10));
        using var provider = Build(host => host.PurgeLockTimeout = TimeSpan.FromMilliseconds(milliseconds));

        var resolve = () => provider.GetRequiredService<IOptions<ProcessHostOptions>>().Value;

        if (valid)
        {
            resolve().PurgeLockTimeout.Should().Be(TimeSpan.FromMilliseconds(milliseconds));
        }
        else
        {
            resolve.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("PurgeLockTimeout");
        }
    }

    /// <summary>
    /// A publisher locks its first target and stops there (a hook holds its transaction open); the purge of the tree starts and is
    /// seen either to give up and retry (the right behaviour) or to wait for the row; then the publisher goes on to lock the other
    /// target and commits. The purge must end, purged, and nothing may fail with a deadlock.
    /// </summary>
    private static async Task RunPublishAgainstPurgeAsync(Rig rig, string root, int queued, int instances)
    {
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        var publisherStore = new HookedStore(rig.Store)
        {
            AfterInstanceLock = async (_, _) =>
            {
                if (first)
                {
                    first = false;
                    holding.SetResult();
                    await gate.Task;
                }
            },
        };
        var purgerStore = new HookedStore(rig.Store);
        var publish = Task.Run(() => rig.EngineOver(publisherStore).PublishEventAsync(new ProcessEvent(A, "e-1", Event, null, "{}")));
        await holding.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var purge = Task.Run(() => rig.EngineOver(purgerStore).PurgeInstanceAsync(A, root, PurgeMode.Force));
        await AdvanceUntilAsync(rig, () => purgerStore.Sessions >= 2 || BlockedOnALockAsync(rig).GetAwaiter().GetResult());
        gate.SetResult();

        var published = await publish.WaitAsync(TimeSpan.FromSeconds(60));
        var purged = await rig.DriveAsync(purge);

        published.Should().Be(new PublishResult(PublishStatus.Queued, queued));
        purged.Status.Should().Be(PurgeStatus.Purged);
        purged.Counts.Instances.Should().Be(instances);
        (await rig.CountAsync("process_instance")).Should().Be(0);
        (await rig.CountAsync("process_inbox")).Should().Be(0, "the inbox rows the publish committed went with their instances");
        rig.Logs.Entries.Should().NotContain(entry => (entry.ExceptionText ?? string.Empty).Contains("40P01", StringComparison.Ordinal));
    }

    private static async Task<bool> BlockedOnALockAsync(Rig rig) =>
        await rig.ScalarAsync<long>("select count(*) from pg_stat_activity where datname = current_database() and wait_event_type = 'Lock' and pid <> pg_backend_pid()") > 0;

    /// <summary>Moves the fake clock in small steps until a condition holds (a real half minute at most), so that a purge that waits on the clock goes on.</summary>
    internal static async Task AdvanceUntilAsync(Rig rig, Func<bool> condition)
    {
        var giveUp = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > giveUp)
            {
                throw new TimeoutException("The condition did not hold in 30 seconds.");
            }

            rig.Time.Advance(TimeSpan.FromMilliseconds(10));
            await Task.Delay(2);
        }
    }

    /// <summary>A waiting instance, created <paramref name="minute"/> minutes after <see cref="PurgeSeed.T0"/>, that waits for the event <c>ev</c> (a root that is not a target has no wait row).</summary>
    internal static async Task WaitingAsync(Rig rig, string id, string? parent, int minute, bool waits = true, string tenant = A)
    {
        await PurgeSeed.InstanceAsync(rig, tenant, id, "p", "k-" + id, PurgeSeed.Waiting, parent, T0.AddMinutes(minute), T0.AddMinutes(minute));
        if (waits)
        {
            await rig.ExecuteAsync("insert into {schema}.process_wait (tenant_id, wait_id, instance_id, event, correlation) values (@t, @i || '-w', @i, @e, null)", ("t", tenant), ("i", id), ("e", Event));
        }
    }

    private static ServiceProvider Build(Action<ProcessHostOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBrainProcessHostPostgres(store => store.ConnectionString = "Host=localhost;Database=brain_unused;Username=brain;Password=brain", configure);
        return services.BuildServiceProvider();
    }
}

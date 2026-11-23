using FluentAssertions;
using Synapbiz.Brain.Process.Hosting;
using Xunit.Abstractions;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>Publishers, pumps and purges on the same trees, many rounds: no deadlock anywhere, and a tree is whole or gone at every observation.</summary>
public sealed class PurgeDeadlockStressTests
{
    private const string Tenant = "tenant-a";
    private const int Rounds = 60;

    private readonly ITestOutputHelper output;

    public PurgeDeadlockStressTests(ITestOutputHelper output) => this.output = output;

    [Theory(Timeout = 300000)]
    [InlineData("instance")]
    [InlineData("batch")]
    public async Task ConcurrentPublishesPumpsAndPurgesNeverDeadlockAndEveryTreeIsWholeOrGoneAtEveryObservation(string kind)
    {
        await using var rig = await Rig.CreateAsync(configureHost: host => host.PurgeLockTimeout = TimeSpan.FromMinutes(5));
        var pumps = new[] { rig.Pump, rig.NewPump(), rig.NewPump() };
        var stop = false;
        var pumping = pumps.Select(pump => Task.Run(async () =>
        {
            while (!Volatile.Read(ref stop))
            {
                await pump.RunOnceAsync();
                await Task.Yield();
            }
        })).ToList();
        var observed = 0;
        try
        {
            for (var round = 0; round < Rounds; round++)
            {
                // Creation order is root, m, b, (a); id order is a, b, m, z-root: the two orders differ and the root sorts last.
                var root = "z" + round.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
                var ids = new List<string> { root };
                await Seed(rig, root, null, 0, kind == "batch");
                foreach (var (suffix, minute) in new[] { ("m", 1), ("b", 2), ("a", 3) }.Take(2 + (round % 2)))
                {
                    var id = suffix + root;
                    ids.Add(id);
                    await PurgeLockTests.WaitingAsync(rig, id, root, minute, tenant: Tenant);
                }

                var tree = ids.ToArray();
                var publishers = Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
                {
                    await Task.Delay((round + index) % 3);
                    return await rig.Engine.PublishEventAsync(new ProcessEvent(Tenant, "e-" + round + "-" + index, "ev", null, "{}"));
                })).ToList();
                var observer = Task.Run(async () =>
                {
                    while (publishers.Exists(task => !task.IsCompleted) || !Volatile.Read(ref stop))
                    {
                        var (instances, others) = await ObserveAsync(rig, tree);
                        instances.Should().BeOneOf(0, tree.Length);
                        if (instances == 0)
                        {
                            others.Should().Be(0, "a tree whose instances are gone has no row left in any table");
                            break;
                        }

                        Interlocked.Increment(ref observed);
                    }
                });

                await Task.Delay(round % 4);
                if (kind == "instance")
                {
                    var result = await rig.DriveAsync(rig.Engine.PurgeInstanceAsync(Tenant, root, PurgeMode.Force));
                    result.Status.Should().Be(PurgeStatus.Purged, "round " + round);
                    result.Counts.Instances.Should().Be(tree.Length);
                }
                else
                {
                    for (var call = 0; call < 500 && (await ObserveAsync(rig, tree)).Instances != 0; call++)
                    {
                        await rig.Engine.PurgeFinishedBeforeAsync(Tenant, PurgeSeed.T0.AddDays(1), 500);
                    }
                }

                await Task.WhenAll(publishers);
                await observer;
                (await ObserveAsync(rig, tree)).Should().Be((0, 0L), "round " + round);
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            await Task.WhenAll(pumping);
        }

        output.WriteLine(kind + ": " + Rounds + " rounds, observations of a whole tree: " + observed);
        rig.Logs.Entries.Should().NotContain(
            entry => entry.Message.Contains("40P01", StringComparison.Ordinal) || (entry.ExceptionText ?? string.Empty).Contains("40P01", StringComparison.Ordinal) || (entry.ExceptionText ?? string.Empty).Contains("deadlock", StringComparison.OrdinalIgnoreCase),
            "no statement of the pump failed with a deadlock");
        (await rig.CountAsync("process_instance")).Should().Be(0);
        (await rig.CountAsync("process_inbox")).Should().Be(0);
    }

    private static Task Seed(Rig rig, string root, string? parent, int minute, bool finished) =>
        finished
            ? SeedFinishedRootAsync(rig, root)
            : PurgeLockTests.WaitingAsync(rig, root, parent, minute, tenant: Tenant);

    /// <summary>A completed root that also waits (so that it is a target of the event) and is old enough for the batch.</summary>
    private static async Task SeedFinishedRootAsync(Rig rig, string root)
    {
        await PurgeSeed.InstanceAsync(rig, Tenant, root, "p", "k-" + root, PurgeSeed.Completed, null, PurgeSeed.T0, PurgeSeed.T0);
        await rig.ExecuteAsync("insert into {schema}.process_wait (tenant_id, wait_id, instance_id, event, correlation) values (@t, @i || '-w', @i, 'ev', null)", ("t", Tenant), ("i", root));
    }

    /// <summary>The instances of the tree that exist, and the rows of every other table that belong to them, in one statement.</summary>
    private static async Task<(long Instances, long Others)> ObserveAsync(Rig rig, string[] ids)
    {
        var total = await rig.ColumnAsync<long>(
            "select (select count(*) from {schema}.process_instance where tenant_id = @t and instance_id = any(@ids)) * 1000000 "
            + "+ (select count(*) from {schema}.process_wait where tenant_id = @t and instance_id = any(@ids)) "
            + "+ (select count(*) from {schema}.process_inbox where tenant_id = @t and instance_id = any(@ids))",
            ("t", Tenant),
            ("ids", ids));
        return (total[0] / 1000000, total[0] % 1000000);
    }
}

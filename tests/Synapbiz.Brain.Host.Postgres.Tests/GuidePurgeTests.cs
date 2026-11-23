using FluentAssertions;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>The retention example of the consumer guide, run against PostgreSQL.</summary>
public sealed class GuidePurgeTests
{
    private const string Tenant = "t-1";

    [Fact]
    public async Task TheRetentionJobPurgesWhatIsOldEnoughOnlyAndReportsNumbers()
    {
        await using var rig = await Rig.CreateAsync();
        var now = Rig.Start;
        var old = await PurgeSeed.TreeAsync(rig, Tenant, "old", PurgeSeed.Completed, now.AddDays(-36), PurgeSeed.Cancelled, "case.lifecycle");
        var recent = await PurgeSeed.TreeAsync(rig, Tenant, "recent", PurgeSeed.Completed, now.AddDays(-34), PurgeSeed.Cancelled, "case.lifecycle");
        var open = await PurgeSeed.TreeAsync(rig, Tenant, "open", PurgeSeed.Waiting, now.AddDays(-90), PurgeSeed.Waiting, "case.lifecycle");
        for (var index = 0; index < 120; index++)
        {
            await PurgeSeed.InstanceAsync(rig, Tenant, "bulk-" + index, "case.lifecycle", "k-bulk-" + index, PurgeSeed.Completed, null, now.AddDays(-60), now.AddDays(-60));
        }

        await rig.ExecuteAsync("insert into {schema}.process_event (tenant_id, event_id, name, received_at) values (@t, 'e-old', 'ev', @a), (@t, 'e-new', 'ev', @b)", ("t", Tenant), ("a", now.AddDays(-8)), ("b", now.AddDays(-6)));
        var job = new Retention(rig.Engine, rig.Time);

        var report = await job.RunAsync(Tenant, CancellationToken.None);

        report.Should().Be(new RetentionReport(121, 120 + old.Instances, 1));
        (await PurgeSeed.CountsAsync(rig, Tenant)).Should().Be(recent.Plus(open));
        (await rig.ColumnAsync<string>("select event_id from {schema}.process_event")).Should().Equal("e-new");
        (await job.RunAsync(Tenant, CancellationToken.None)).Should().Be(new RetentionReport(0, 0, 0));
    }

    [Fact]
    public async Task TheEraseExampleRemovesTheWholeCaseOfARunningRootAndAnUnknownCaseIsNotFound()
    {
        await using var rig = await Rig.CreateAsync();
        var seeded = await PurgeSeed.TreeAsync(rig, Tenant, "r1", PurgeSeed.Waiting, PurgeSeed.T0, PurgeSeed.Waiting, "case.lifecycle");
        var kept = await PurgeSeed.TreeAsync(rig, Tenant, "r2", PurgeSeed.Waiting, PurgeSeed.T0, PurgeSeed.Waiting, "case.lifecycle");
        var job = new Retention(rig.Engine, rig.Time);

        var erased = await job.EraseAsync(Tenant, "k-r1", CancellationToken.None);
        var unknown = await job.EraseAsync(Tenant, "k-nobody", CancellationToken.None);

        erased.Should().Be(new PurgeResult(PurgeStatus.Purged, seeded));
        unknown.Should().Be(new PurgeResult(PurgeStatus.NotFound, PurgeCounts.None));
        (await PurgeSeed.CountsAsync(rig, Tenant)).Should().Be(kept);
    }
}

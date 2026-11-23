using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>Tenant scope of the statements of a purge, and the narrow handling of a failure in the session.</summary>
public sealed class PurgeScopeTests
{
    private const string A = "tenant-a";
    private const string B = "tenant-b";

    private static readonly DateTimeOffset T0 = PurgeSeed.T0;

    [Fact]
    public async Task TheTreeWalkStaysInTheTenantEvenWhenAnotherTenantHasAChildOfTheSameParentIdWithTheIdOfAnUnrelatedRoot()
    {
        await using var rig = await Rig.CreateAsync();
        var aTree = await PurgeSeed.TreeAsync(rig, A, "r1", PurgeSeed.Completed, T0);
        var aOther = await PurgeSeed.TreeAsync(rig, A, "xl", PurgeSeed.Completed, T0);
        // Tenant B: an instance "xl" whose parent id is "r1", although tenant B has no "r1". In tenant A, "xl" is a root of its own.
        await PurgeSeed.InstanceAsync(rig, B, "xl", "p", "k-xl", PurgeSeed.Waiting, "r1", T0, T0);
        var bRows = await PurgeSeed.RowsAsync(rig, B, "xl", 1);
        var bBefore = await PurgeSeed.CountsAsync(rig, B);

        var result = await rig.Engine.PurgeInstanceAsync(A, "r1", PurgeMode.Force);

        result.Should().Be(new PurgeResult(PurgeStatus.Purged, aTree));
        (await PurgeSeed.CountsAsync(rig, A)).Should().Be(aOther, "the root xl of tenant A is not part of the tree of r1 and is untouched");
        (await PurgeSeed.CountsAsync(rig, B)).Should().Be(bBefore).And.Be((bRows with { Instances = 1 }));
    }

    [Fact]
    public async Task TheEventPurgeSelectsOnlyEventsOfTheTenantSoOlderEventsOfAnotherTenantDoNotUseUpTheLimit()
    {
        await using var rig = await Rig.CreateAsync();
        for (var index = 0; index < 5; index++)
        {
            await rig.ExecuteAsync("insert into {schema}.process_event (tenant_id, event_id, name, received_at) values (@b, @o, 'ev', @old), (@a, @e, 'ev', @new)", ("a", A), ("b", B), ("e", "e-" + index), ("o", "other-" + index), ("old", T0.AddHours(index)), ("new", T0.AddDays(1).AddHours(index)));
        }

        var purged = await rig.Engine.PurgeEventsBeforeAsync(A, T0.AddDays(10), 3);

        purged.Should().Be(new PurgeEventsResult(3, true), "three of the tenant's own events go although every event of the other tenant is older");
        (await rig.CountAsync("process_event", "tenant_id = @t", ("t", A))).Should().Be(2);
        (await rig.CountAsync("process_event", "tenant_id = @t", ("t", B))).Should().Be(5);
        (await rig.ColumnAsync<string>("select event_id from {schema}.process_event where tenant_id = @t order by event_id", ("t", A))).Should().Equal("e-3", "e-4");
    }

    [Fact]
    public async Task AFailedCommitSurfacesAsItsRealExceptionAndNotAsAFailureOfTheRollback()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("wait-reply"));
        await rig.ExecuteAsync(
            "create function {schema}.refuse_at_commit() returns trigger language plpgsql as $$ begin raise exception 'refused at commit' using errcode = '23514'; end $$; "
            + "create constraint trigger refuse_at_commit after insert on {schema}.process_instance deferrable initially deferred for each row execute function {schema}.refuse_at_commit()");

        var failed = await FluentActions.Awaiting(() => rig.Engine.StartAsync(new StartRequest(A, "host.wait", "k-1", "{\"case_ref\":\"c-1\"}"))).Should().ThrowAsync<PostgresException>();

        failed.Which.SqlState.Should().Be("23514");
        failed.Which.MessageText.Should().Be("refused at commit");
        (await rig.CountAsync("process_instance")).Should().Be(0, "nothing was committed");
    }

    [Fact]
    public async Task ASessionCanBeDisposedTwiceAndAfterACommitWithoutAnError()
    {
        await using var rig = await Rig.CreateAsync();
        var store = rig.Store;
        var session = await store.BeginAsync(CancellationToken.None);
        await session.CommitAsync(CancellationToken.None);

        await session.DisposeAsync();
        await FluentActions.Awaiting(async () => await session.DisposeAsync()).Should().NotThrowAsync();
        await using var open = await store.BeginAsync(CancellationToken.None);
        await open.DisposeAsync();
        await FluentActions.Awaiting(async () => await open.DisposeAsync()).Should().NotThrowAsync();
    }
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// Two tenants that activate the same process, so that their activations and schedules have the same process id and the same
/// schedule ids and differ only by the tenant: nothing one tenant's activation, schedule lock or firing reads or changes belongs
/// to the other. And a schedule row that another session holds is skipped by the pump, never waited for.
/// </summary>
public sealed class ScheduleIsolationTests
{
    private const string Process = "host.trigger_schedule";
    private const string A = "t-a";
    private const string B = "t-b";

    private static readonly string[] ScheduleIds = ["daily", "hourly_once", "hourly_skip"];

    // The first quarter of an hour after the rig clock (14:00 UTC): the hourly schedules of Test/Fixed (UTC+05:30) fall on :30 in UTC.
    private static readonly DateTimeOffset FixedHour = new(2027, 1, 4, 14, 30, 0, TimeSpan.Zero);

    private static async Task<Rig> TwoTenantsAsync(Action<ProcessHostOptions>? configureHost = null)
    {
        var rig = await Rig.CreateAsync(configureHost: configureHost);
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));
        await rig.Engine.ActivateAsync(new ProcessActivation(A, Process, "Test/Fixed", "{\"site\":\"s-a\"}"));
        await rig.Engine.ActivateAsync(new ProcessActivation(B, Process, "UTC", "{\"site\":\"s-b\"}"));
        return rig;
    }

    private static Task<List<string>> KeysAsync(Rig rig, string tenant) =>
        rig.ColumnAsync<string>("select business_key from {schema}.process_instance where tenant_id = @t order by business_key", ("t", tenant));

    private static Task<DateTimeOffset> NextAsync(Rig rig, string tenant, string schedule) =>
        rig.ScalarAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where tenant_id = @t and schedule_id = @s", ("t", tenant), ("s", schedule));

    [Fact]
    public async Task TheLockOfOneTenantsScheduleRowNeitherCoversNorSkipsTheOtherTenantsRowAndEachRecordCarriesItsOwnActivation()
    {
        await using var rig = await TwoTenantsAsync();
        var store = rig.Provider.GetRequiredService<IProcessStore>();

        foreach (var id in ScheduleIds)
        {
            await using var first = await store.BeginAsync(CancellationToken.None);
            await using var second = await store.BeginAsync(CancellationToken.None);

            var ofA = await first.LockScheduleAsync(new ScheduleKey(A, Process, id), skipLocked: true, CancellationToken.None);
            var ofB = await second.LockScheduleAsync(new ScheduleKey(B, Process, id), skipLocked: true, CancellationToken.None);
            var again = await second.LockScheduleAsync(new ScheduleKey(A, Process, id), skipLocked: true, CancellationToken.None);

            ofA.Should().NotBeNull(id);
            ofB.Should().NotBeNull(id + ": the lock on the row of the other tenant does not cover it");
            again.Should().BeNull(id + ": the row of the first tenant is held, so it is skipped");
            ofA!.Row.TenantId.Should().Be(A);
            ofB!.Row.TenantId.Should().Be(B);
            ofA.Row.Zone.Should().Be(id == "daily" ? "America/New_York" : "Test/Fixed");
            ofB.Row.Zone.Should().Be(id == "daily" ? "America/New_York" : "UTC");
            ofA.ProtectedInput.Should().Contain("s-a").And.NotContain("s-b");
            ofB.ProtectedInput.Should().Contain("s-b").And.NotContain("s-a");
        }
    }

    [Fact]
    public async Task ActivatingTheSameProcessWithTheSameSettingsForASecondTenantChangesItAndLeavesTheFirstAlone()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));
        var first = await rig.Engine.ActivateAsync(new ProcessActivation(A, Process, "Test/Fixed", "{\"site\":\"s-1\"}"));
        var before = await NextAsync(rig, A, "hourly_skip");
        rig.Time.Advance(TimeSpan.FromMinutes(45));

        var second = await rig.Engine.ActivateAsync(new ProcessActivation(B, Process, "Test/Fixed", "{\"site\":\"s-1\"}"));
        var repeat = await rig.Engine.ActivateAsync(new ProcessActivation(B, Process, "Test/Fixed", "{\"site\":\"s-1\"}"));
        var changed = await rig.Engine.ActivateAsync(new ProcessActivation(B, Process, "Test/Fixed", "{\"site\":\"s-2\"}"));
        var untouched = await rig.Engine.ActivateAsync(new ProcessActivation(A, Process, "Test/Fixed", "{\"site\":\"s-1\"}"));

        first.Changed.Should().BeTrue();
        second.Changed.Should().BeTrue("the activation of the first tenant is not the second tenant's");
        repeat.Changed.Should().BeFalse();
        changed.Changed.Should().BeTrue();
        untouched.Changed.Should().BeFalse("what the second tenant did does not change the first");
        (await rig.CountAsync("process_schedule", "tenant_id = @t", ("t", A))).Should().Be(3);
        (await rig.CountAsync("process_schedule", "tenant_id = @t", ("t", B))).Should().Be(3);
        (await rig.CountAsync("process_event_trigger", "tenant_id = @t", ("t", B))).Should().Be(0, "this process has no event trigger");
        (await NextAsync(rig, A, "hourly_skip")).Should().Be(before);
        (await NextAsync(rig, B, "hourly_skip")).Should().BeAfter(before);
        (await rig.ScalarAsync<string>("select input from {schema}.process_activation where tenant_id = @t", ("t", A))).Should().Contain("s-1");
        (await rig.ScalarAsync<string>("select input from {schema}.process_activation where tenant_id = @t", ("t", B))).Should().Contain("s-2");
    }

    [Fact]
    public async Task DeactivatingOneTenantsProcessRemovesItsRowsOnlyAndTheOtherTenantsScheduleStillFires()
    {
        await using var rig = await TwoTenantsAsync();

        (await rig.Engine.DeactivateAsync(B, Process)).Should().BeTrue();
        (await rig.Engine.DeactivateAsync(B, Process)).Should().BeFalse();
        rig.Time.SetUtcNow(FixedHour);
        await rig.DrainAsync();

        (await rig.CountAsync("process_schedule", "tenant_id = @t", ("t", B))).Should().Be(0);
        (await rig.CountAsync("process_schedule", "tenant_id = @t", ("t", A))).Should().Be(3);
        (await KeysAsync(rig, A)).Should().Equal("@schedule/hourly_once/20270104T1430Z", "@schedule/hourly_skip/20270104T1430Z");
        (await KeysAsync(rig, B)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheSchedulesOfTwoTenantsFireInTheirOwnPassesWithTheirOwnZoneAndInputAndNeverChangeEachOthersRows()
    {
        await using var rig = await TwoTenantsAsync();
        var nextOfB = await NextAsync(rig, B, "hourly_skip");
        nextOfB.Should().Be(new DateTimeOffset(2027, 1, 4, 15, 0, 0, TimeSpan.Zero), "the hourly schedules of B run in UTC: the next full hour after the activation at 14:00");

        // 14:30 UTC: only the schedules of A are due.
        rig.Time.SetUtcNow(FixedHour);
        await rig.DrainAsync();

        (await KeysAsync(rig, A)).Should().Equal("@schedule/hourly_once/20270104T1430Z", "@schedule/hourly_skip/20270104T1430Z");
        (await KeysAsync(rig, B)).Should().BeEmpty();
        (await NextAsync(rig, A, "hourly_skip")).Should().Be(FixedHour + TimeSpan.FromHours(1));
        (await NextAsync(rig, B, "hourly_skip")).Should().Be(nextOfB, "the row of B was not read or changed");
        (await NextAsync(rig, B, "hourly_once")).Should().Be(nextOfB);

        // 15:00 UTC: only the schedules of B are due.
        rig.Time.SetUtcNow(nextOfB);
        await rig.DrainAsync();

        (await KeysAsync(rig, A)).Should().HaveCount(2);
        (await KeysAsync(rig, B)).Should().Equal("@schedule/hourly_once/20270104T1500Z", "@schedule/hourly_skip/20270104T1500Z");
        (await NextAsync(rig, A, "hourly_skip")).Should().Be(FixedHour + TimeSpan.FromHours(1), "the row of A was not read or changed");
        (await NextAsync(rig, B, "hourly_skip")).Should().Be(nextOfB + TimeSpan.FromHours(1));
        var ofA = await rig.Engine.FindInstanceAsync(A, Process, "@schedule/hourly_skip/20270104T1430Z");
        var ofB = await rig.Engine.FindInstanceAsync(B, Process, "@schedule/hourly_skip/20270104T1500Z");
        ofA!.State!.Variables["site"].Should().Be("\"s-a\"");
        ofB!.State!.Variables["site"].Should().Be("\"s-b\"");
        (await rig.Engine.FindInstanceAsync(B, Process, "@schedule/hourly_skip/20270104T1430Z")).Should().BeNull();
        (await rig.Engine.FindInstanceAsync(A, Process, "@schedule/hourly_skip/20270104T1500Z")).Should().BeNull();
    }

    [Fact]
    public async Task TheSchedulesOfTwoTenantsThatAreDueAtTheSameInstantEachFireOnceWithTheirOwnInputWhateverThePumpsDo()
    {
        await using var rig = await TwoTenantsAsync();
        // At 15:30 UTC A has two occurrences behind it (14:30 and 15:30) and the last is on time, so both policies fire it; B has one
        // (15:00), half an hour late, which skip drops and fireOnce fires.
        rig.Time.SetUtcNow(new DateTimeOffset(2027, 1, 4, 15, 30, 0, TimeSpan.Zero));
        var pumps = new[] { rig.Pump, rig.NewPump(), rig.NewPump() };

        await Task.WhenAll(pumps.Select(pump => Task.Run(() => Rig.DrainAsync(pump))));
        await rig.DrainAsync();

        (await KeysAsync(rig, A)).Should().Equal("@schedule/hourly_once/20270104T1530Z", "@schedule/hourly_skip/20270104T1530Z");
        (await KeysAsync(rig, B)).Should().Equal("@schedule/hourly_once/20270104T1500Z");
        foreach (var (tenant, site) in new[] { (A, "s-a"), (B, "s-b") })
        {
            var instances = await rig.ColumnAsync<string>("select instance_id from {schema}.process_instance where tenant_id = @t", ("t", tenant));
            foreach (var id in instances)
            {
                (await rig.Engine.GetInstanceAsync(tenant, id))!.State!.Variables["site"].Should().Be("\"" + site + "\"");
            }
        }
    }

    /// <summary>Yields until PostgreSQL reports a session of this rig's schema that waits for a lock, or the token is cancelled.</summary>
    private static async Task<bool> SomeoneWaitsForALockAsync(Rig rig, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (await rig.ScalarAsync<long>(
                "select count(*) from pg_stat_activity where wait_event_type = 'Lock' and pid <> pg_backend_pid() and query like '%' || @s || '%'",
                ("s", rig.Schema)) > 0)
            {
                return true;
            }

            await Task.Yield();
        }

        return false;
    }

    [Fact]
    public async Task AScheduleRowThatAnotherSessionHoldsIsSkippedWithoutWaitingOrFiringAndFiresOnALaterPassOnceItIsReleased()
    {
        await using var rig = await TwoTenantsAsync();
        rig.Time.SetUtcNow(FixedHour);
        var store = rig.Provider.GetRequiredService<IProcessStore>();
        var held = await store.BeginAsync(CancellationToken.None);
        Task? pass = null;
        using var stop = new CancellationTokenSource();
        try
        {
            (await held.LockScheduleAsync(new ScheduleKey(A, Process, "hourly_once"), skipLocked: false, CancellationToken.None)).Should().NotBeNull();

            // If the pump waited for the held row, a session would wait for a lock: the race below then ends the other way round.
            pass = rig.Pump.RunOnceAsync();
            var waiting = SomeoneWaitsForALockAsync(rig, stop.Token);
            var finished = await Task.WhenAny(pass, waiting);
            finished.Should().BeSameAs(pass, "the pump skips a locked schedule row instead of waiting for it");
            await pass;

            (await KeysAsync(rig, A)).Should().Equal(["@schedule/hourly_skip/20270104T1430Z"], "the free row fired, the held one did not");
            (await NextAsync(rig, A, "hourly_once")).Should().Be(FixedHour, "the held row was not touched");
            rig.Logs.Entries.Count(entry => entry.EventId.Id == 14).Should().Be(1);
        }
        finally
        {
            await stop.CancelAsync();
            await held.DisposeAsync();
            if (pass is not null)
            {
                await pass;
            }
        }

        await rig.DrainAsync();

        (await KeysAsync(rig, A)).Should().Equal("@schedule/hourly_once/20270104T1430Z", "@schedule/hourly_skip/20270104T1430Z");
        (await NextAsync(rig, A, "hourly_once")).Should().Be(FixedHour + TimeSpan.FromHours(1));
        rig.Logs.Entries.Count(entry => entry.EventId.Id == 14).Should().Be(2, "each occurrence fired once");
    }

    [Fact]
    public async Task ARowHeldByAnotherSessionIsSkippedAndNeverBlocksTheRowsOfTheOtherTenant()
    {
        await using var rig = await TwoTenantsAsync();
        rig.Time.SetUtcNow(new DateTimeOffset(2027, 1, 4, 16, 0, 0, TimeSpan.Zero));
        var store = rig.Provider.GetRequiredService<IProcessStore>();
        await using var held = await store.BeginAsync(CancellationToken.None);
        (await held.LockScheduleAsync(new ScheduleKey(A, Process, "hourly_skip"), skipLocked: false, CancellationToken.None)).Should().NotBeNull();

        await rig.DrainAsync();

        (await KeysAsync(rig, B)).Should().Equal("@schedule/hourly_once/20270104T1600Z", "@schedule/hourly_skip/20270104T1600Z");
        (await KeysAsync(rig, A)).Should().Equal("@schedule/hourly_once/20270104T1530Z");
        (await NextAsync(rig, A, "hourly_skip")).Should().Be(FixedHour, "the held row is left exactly as it was");
    }
}

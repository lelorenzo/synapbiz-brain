using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// Schedules on the pump: one instance per occurrence, the misfire policies and the grace, daylight saving, the values that reach
/// the variables, deactivation, and the isolation of schedule items. The clock is a fake one and the zones are built by the tests.
/// </summary>
public sealed class ScheduleTests
{
    private const string Process = "host.trigger_schedule";
    private const string Dst = "host.trigger_dst";

    private static readonly DateTimeOffset Quarter = new(2027, 1, 4, 14, 30, 0, TimeSpan.Zero);

    private static async Task<Rig> ActivatedAsync(Action<ProcessHostOptions>? configureHost = null)
    {
        var rig = await Rig.CreateAsync(configureHost: configureHost);
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"), Fixtures.Host("trigger-dst"));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "Test/Fixed", "{\"site\":\"s-1\"}"));
        return rig;
    }

    private static Task<List<string>> KeysAsync(Rig rig, string process = Process, string like = "%") =>
        rig.ColumnAsync<string>("select business_key from {schema}.process_instance where process_id = @p and business_key like @k order by business_key", ("p", process), ("k", like));

    /// <summary>Moves the clock to the next fire time of the activation, drains the pump, and repeats until the next fire time is after <paramref name="until"/>.</summary>
    private static async Task RunThroughAsync(Rig rig, DateTimeOffset until)
    {
        while (true)
        {
            var next = await rig.ScalarAsync<DateTimeOffset>("select min(next_fire_at) from {schema}.process_schedule");
            if (next > until)
            {
                break;
            }

            rig.Time.SetUtcNow(next);
            await rig.DrainAsync();
        }

        rig.Time.SetUtcNow(until);
        await rig.DrainAsync();
    }

    [Fact]
    public async Task AScheduleFiresAtItsOccurrenceAndNotBeforeAndTheInstanceRunsToTheEnd()
    {
        await using var rig = await ActivatedAsync();

        rig.Time.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(1));
        (await rig.Pump.RunOnceAsync()).Should().Be(0, "nothing is due one second before the occurrence");
        (await KeysAsync(rig)).Should().BeEmpty();

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        (await rig.Pump.RunOnceAsync()).Should().BeGreaterThan(0);
        await rig.DrainAsync();

        (await KeysAsync(rig)).Should().Equal("hourly_once/20270104T1430Z", "hourly_skip/20270104T1430Z");
        var info = await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "hourly_skip/20270104T1430Z");
        info!.Status.Should().Be(ProcessStatus.Completed);
        info.Result.Should().Be("ran");
        info.ParentInstanceId.Should().BeNull();
        (await rig.ColumnAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id like 'hourly%'")).Should().OnlyContain(next => next == Quarter + TimeSpan.FromHours(1));
        (await rig.DrainAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TheActivationInputAndTheScheduledValuesReachTheVariablesAndTheDateIsTheLocalOne()
    {
        await using var rig = await ActivatedAsync();

        await RunThroughAsync(rig, new DateTimeOffset(2027, 1, 5, 11, 0, 0, TimeSpan.Zero));

        var daily = await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "daily/20270105T1100Z");
        daily!.State!.Variables["site"].Should().Be("\"s-1\"");
        daily.State.Variables["run_for"].Should().Be("\"2027-01-05\"");
        daily.State.Variables["fired_at"].Should().Be("\"2027-01-05T11:00:00Z\"");
        var hourly = await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "hourly_skip/20270105T1030Z");
        hourly!.State!.Variables["fired_at"].Should().Be("\"2027-01-05T10:30:00Z\"");
        hourly.State.Variables.Should().NotContainKey("run_for");
    }

    [Fact]
    public async Task ScheduledDateIsTheLocalCalendarDateOfTheOccurrenceInTheZoneOfTheSchedule()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-dst"));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Dst));

        await RunThroughAsync(rig, new DateTimeOffset(2027, 1, 5, 2, 30, 0, TimeSpan.Zero));

        var evening = await rig.Engine.FindInstanceAsync(rig.Tenant, Dst, "evening/20270105T0230Z");
        evening!.State!.Variables["run_for"].Should().Be("\"2027-01-04\"", "21:30 on 4 January in New York is 02:30 UTC on the 5th");
        evening.State.Variables["fired_at"].Should().Be("\"2027-01-05T02:30:00Z\"");
    }

    [Fact]
    public async Task AfterTheClockJumpedThreeOccurrencesSkipDropsThemAllWhenTheLastIsLateAndFireOnceFiresTheLast()
    {
        await using var rig = await ActivatedAsync();

        rig.Time.SetUtcNow(new DateTimeOffset(2027, 1, 4, 16, 45, 0, TimeSpan.Zero));
        await rig.DrainAsync();

        (await KeysAsync(rig, like: "hourly%")).Should().Equal("hourly_once/20270104T1630Z");
        (await rig.ColumnAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id like 'hourly%'"))
            .Should().OnlyContain(next => next == new DateTimeOffset(2027, 1, 4, 17, 30, 0, TimeSpan.Zero), "the first occurrence after now, whatever the policy");
        rig.Logs.Entries.Should().Contain(entry => entry.EventId.Id == 15 && entry.Level == LogLevel.Warning && entry.Message.Contains("hourly_skip", StringComparison.Ordinal) && entry.Message.Contains("fired False", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AfterTheClockJumpedThreeOccurrencesBothPoliciesFireTheLastOneWhenItIsOnTime()
    {
        await using var rig = await ActivatedAsync();

        rig.Time.SetUtcNow(new DateTimeOffset(2027, 1, 4, 16, 32, 0, TimeSpan.Zero));
        await rig.DrainAsync();

        (await KeysAsync(rig, like: "hourly%")).Should().Equal("hourly_once/20270104T1630Z", "hourly_skip/20270104T1630Z");
    }

    [Fact]
    public async Task TheGraceBoundaryIsInclusiveAnOccurrenceExactlyFiveMinutesLateIsOnTimeAndOneSecondMoreIsMissed()
    {
        await using var onTime = await ActivatedAsync();
        onTime.Time.SetUtcNow(Quarter + TimeSpan.FromMinutes(5));
        await onTime.DrainAsync();

        await using var late = await ActivatedAsync();
        late.Time.SetUtcNow(Quarter + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await late.DrainAsync();

        (await KeysAsync(onTime, like: "hourly%")).Should().Equal("hourly_once/20270104T1430Z", "hourly_skip/20270104T1430Z");
        (await KeysAsync(late, like: "hourly%")).Should().Equal(["hourly_once/20270104T1430Z"], "skip drops a single occurrence that is late, fireOnce fires it anyway");
        (await late.ColumnAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id = 'hourly_skip'")).Should().Equal(Quarter + TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task TheGraceIsAnOptionAndZeroMeansOnlyTheExactInstantIsOnTime()
    {
        await using var rig = await ActivatedAsync(host => host.ScheduleMisfireGrace = TimeSpan.Zero);

        rig.Time.SetUtcNow(Quarter + TimeSpan.FromSeconds(1));
        await rig.DrainAsync();

        (await KeysAsync(rig, like: "hourly%")).Should().Equal("hourly_once/20270104T1430Z");
    }

    [Fact]
    public async Task ADeactivatedScheduleNeverFiresAgain()
    {
        await using var rig = await ActivatedAsync();
        rig.Time.SetUtcNow(Quarter);
        await rig.DrainAsync();
        (await KeysAsync(rig, like: "hourly%")).Should().HaveCount(2);

        (await rig.Engine.DeactivateAsync(rig.Tenant, Process)).Should().BeTrue();
        rig.Time.Advance(TimeSpan.FromDays(3));
        var handled = await rig.DrainAsync();

        handled.Should().Be(0);
        (await KeysAsync(rig)).Should().HaveCount(2);
    }

    [Fact]
    public async Task AScheduleWhoseDefinitionTheCatalogCannotSupplyIsLeftUntouchedAndFiresWhenTheDefinitionIsBack()
    {
        await using var rig = await ActivatedAsync();
        var definition = Fixtures.Host("trigger-schedule");
        rig.Catalog.Remove(Process);
        rig.Time.SetUtcNow(Quarter);

        var handled = await rig.Pump.RunOnceAsync();

        handled.Should().Be(0);
        (await KeysAsync(rig)).Should().BeEmpty();
        (await rig.ColumnAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id like 'hourly%'")).Should().OnlyContain(next => next == Quarter);
        rig.Logs.Entries.Should().Contain(entry => entry.EventId.Id == 16 && entry.Level == LogLevel.Warning && entry.Message.StartsWith("process.definition.unavailable", StringComparison.Ordinal));

        rig.Catalog.Add(definition);
        await rig.DrainAsync();
        (await KeysAsync(rig, like: "hourly%")).Should().Equal("hourly_once/20270104T1430Z", "hourly_skip/20270104T1430Z");
    }

    [Fact]
    public async Task TheGapOfTheSpringTransitionFiresOnceAtTheFirstValidInstantAfterIt()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-dst"));
        rig.Time.SetUtcNow(new DateTimeOffset(2027, 3, 13, 12, 0, 0, TimeSpan.Zero));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Dst));
        (await rig.ScalarAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id = 'gap'"))
            .Should().Be(new DateTimeOffset(2027, 3, 14, 7, 0, 0, TimeSpan.Zero), "02:30 does not exist on 14 March; 03:00 EDT is 07:00 UTC");

        await RunThroughAsync(rig, new DateTimeOffset(2027, 3, 16, 0, 0, 0, TimeSpan.Zero));

        (await KeysAsync(rig, Dst, "gap/%")).Should().Equal("gap/20270314T0700Z", "gap/20270315T0630Z");
        (await KeysAsync(rig, Dst, "fold/%")).Should().Equal("fold/20270314T0630Z", "fold/20270315T0530Z");
        var gap = await rig.Engine.FindInstanceAsync(rig.Tenant, Dst, "gap/20270314T0700Z");
        gap!.State!.Variables["run_for"].Should().Be("\"2027-03-14\"");
    }

    [Fact]
    public async Task ATimeThatOccursTwiceInTheAutumnTransitionFiresAtItsFirstOccurrenceOnly()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-dst"));
        rig.Time.SetUtcNow(new DateTimeOffset(2027, 11, 6, 12, 0, 0, TimeSpan.Zero));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Dst));

        await RunThroughAsync(rig, new DateTimeOffset(2027, 11, 8, 12, 0, 0, TimeSpan.Zero));

        (await KeysAsync(rig, Dst, "fold/%")).Should().Equal("fold/20271107T0530Z", "fold/20271108T0630Z");
        (await KeysAsync(rig, Dst, "gap/%")).Should().Equal("gap/20271107T0730Z", "gap/20271108T0730Z");
    }

    [Fact]
    public async Task ThePumpReReadsTheScheduleUnderItsLockSoAnOccurrenceAnotherPumpHandledIsNotFiredAgain()
    {
        await using var rig = await ActivatedAsync();
        rig.Time.SetUtcNow(Quarter);
        var other = rig.NewPump();
        var hooked = rig.NewPump(inner => new HookedStore(inner)
        {
            AfterScan = async (kind, found) =>
            {
                if (kind == WorkKind.DueSchedule)
                {
                    // Between the scan of this pump and its lock, another pump handles everything that was due.
                    await other.RunOnceAsync();
                }

                return found;
            },
        });

        var handled = await hooked.RunOnceAsync();

        (await KeysAsync(rig, like: "hourly%")).Should().HaveCount(2);
        rig.Logs.Entries.Count(entry => entry.EventId.Id == 14).Should().Be(2, "each occurrence is fired by one pump only");
        handled.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AScheduleThatIsLockedByAnotherSessionDoesNotHideTheOnesBehindItWhateverTheBatchSize()
    {
        await using var rig = await ActivatedAsync(host => host.BatchSize = 1);
        rig.Time.SetUtcNow(Quarter);
        var store = rig.Provider.GetRequiredService<IProcessStore>();
        var held = await store.BeginAsync(CancellationToken.None);
        (await held.LockScheduleAsync(new ScheduleKey(rig.Tenant, Process, "hourly_once"), skipLocked: false, CancellationToken.None)).Should().NotBeNull();

        await rig.Pump.RunOnceAsync();

        (await KeysAsync(rig, like: "hourly%")).Should().Equal("hourly_skip/20270104T1430Z");
        (await rig.ScalarAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id = 'hourly_once'")).Should().Be(Quarter);

        await held.DisposeAsync();
        await rig.DrainAsync();
        (await KeysAsync(rig, like: "hourly%")).Should().Equal("hourly_once/20270104T1430Z", "hourly_skip/20270104T1430Z");
    }

    [Fact]
    public async Task AScheduleThatFailsIsLoggedWithoutItsMessageLeftAsItWasAndTheOthersStillFire()
    {
        await using var rig = await ActivatedAsync();
        rig.Time.SetUtcNow(new DateTimeOffset(2027, 1, 5, 11, 0, 0, TimeSpan.Zero));
        rig.Zones.Remove("America/New_York");

        await rig.DrainAsync();

        (await KeysAsync(rig, like: "daily/%")).Should().BeEmpty();
        (await rig.ScalarAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id = 'daily'")).Should().Be(new DateTimeOffset(2027, 1, 5, 11, 0, 0, TimeSpan.Zero));
        (await KeysAsync(rig, like: "hourly%")).Should().NotBeEmpty();
        var failures = rig.Logs.Entries.Where(entry => entry.EventId.Id == 12 && entry.Message.Contains("DueSchedule", StringComparison.Ordinal)).ToList();
        failures.Should().NotBeEmpty("the item is tried again on every pass");
        failures.Should().OnlyContain(failure => failure.Message.Contains("daily", StringComparison.Ordinal) && failure.Message.Contains("System.InvalidOperationException", StringComparison.Ordinal) && failure.ExceptionText == null);

        rig.Zones.Add("America/New_York", TestZoneResolver.Eastern);
        await rig.DrainAsync();
        (await KeysAsync(rig, like: "daily/%")).Should().Equal("daily/20270105T1100Z");
    }

    [Fact]
    public async Task TheSchedulesOfTwoTenantsFireTheirOwnInstancesWithTheirOwnInput()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));
        await rig.Engine.ActivateAsync(new ProcessActivation("t-1", Process, "Test/Fixed", "{\"site\":\"s-1\"}"));
        await rig.Engine.ActivateAsync(new ProcessActivation("t-2", Process, "Test/Fixed", "{\"site\":\"s-2\"}"));
        await rig.Engine.DeactivateAsync("t-2", Process);
        await rig.Engine.ActivateAsync(new ProcessActivation("t-2", Process, "Test/Fixed", "{\"site\":\"s-2\"}"));

        rig.Time.SetUtcNow(Quarter);
        await rig.DrainAsync();

        foreach (var (tenant, site) in new[] { ("t-1", "s-1"), ("t-2", "s-2") })
        {
            var info = await rig.Engine.FindInstanceAsync(tenant, Process, "hourly_skip/20270104T1430Z");
            info!.State!.Variables["site"].Should().Be("\"" + site + "\"");
        }

        (await rig.CountAsync("process_instance")).Should().Be(4);
        (await rig.Engine.FindInstanceAsync("t-3", Process, "hourly_skip/20270104T1430Z")).Should().BeNull();
    }

    [Fact]
    public async Task AnInstanceStartedByAScheduleIsPinnedToTheVersionOfTheActivationEvenWhenANewerOneIsInTheCatalog()
    {
        await using var rig = await Rig.CreateAsync();
        var first = Fixtures.Host("trigger-schedule");
        rig.Catalog.Add(first);
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "Test/Fixed", "{\"site\":\"s-1\"}"));
        // The pinned definition is still the one of the activation, so a later version that drops the variable changes nothing until the consumer activates again.
        rig.Catalog.Add(Fixtures.HostEdited("trigger-schedule", text => text.Replace("\"version\": 1", "\"version\": 2", StringComparison.Ordinal).Replace("\"site\": { \"type\": \"Code\", \"input\": true }", "\"other\": { \"type\": \"Code\", \"input\": true }", StringComparison.Ordinal)));

        rig.Time.SetUtcNow(Quarter);
        await rig.DrainAsync();

        var info = await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "hourly_skip/20270104T1430Z");
        info!.ProcessVersion.Should().Be(1);
        info.Status.Should().Be(ProcessStatus.Completed);
    }
}

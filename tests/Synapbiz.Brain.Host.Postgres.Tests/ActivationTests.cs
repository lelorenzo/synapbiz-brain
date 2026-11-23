using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>Activating and deactivating the triggers of a process: idempotency, replacement, the checks of the arguments and what is stored.</summary>
public sealed class ActivationTests
{
    private const string Process = "host.trigger_schedule";
    private const string Input = "{\"site\":\"s-1\"}";

    private static readonly DateTimeOffset FirstHourly = new(2027, 1, 4, 14, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstDaily = new(2027, 1, 5, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ActivatingWritesTheActivationItsEventTriggerRowsAndItsScheduleRowsWithTheFirstOccurrenceAfterNow()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"), Fixtures.Host("trigger-event"));

        var schedules = await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "Test/Fixed", Input));
        var events = await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, "host.trigger_event"));

        schedules.Should().Be(new ActivationResult(1, true));
        events.Should().Be(new ActivationResult(1, true));
        (await rig.CountAsync("process_activation")).Should().Be(2);
        (await rig.ScalarAsync<string>("select zone from {schema}.process_activation where process_id = @p", ("p", Process))).Should().Be("Test/Fixed");
        (await rig.ScalarAsync<string>("select process_hash from {schema}.process_activation where process_id = @p", ("p", Process))).Should().Be(Fixtures.Host("trigger-schedule").ContentHash);
        (await rig.ScalarAsync<long>("select process_version from {schema}.process_activation where process_id = @p", ("p", Process))).Should().Be(1);
        (await rig.ScalarAsync<DateTimeOffset>("select activated_at from {schema}.process_activation where process_id = @p", ("p", Process))).Should().Be(Rig.Start);
        (await rig.ColumnAsync<string>("select schedule_id || '=' || cron || '|' || zone || '|' || misfire from {schema}.process_schedule order by schedule_id"))
            .Should().Equal("daily=0 6 * * *|America/New_York|2", "hourly_once=0 * * * *|Test/Fixed|2", "hourly_skip=0 * * * *|Test/Fixed|1");
        (await rig.ScalarAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id = 'daily'")).Should().Be(FirstDaily);
        (await rig.ScalarAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id = 'hourly_skip'")).Should().Be(FirstHourly);
        (await rig.ScalarAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id = 'hourly_once'")).Should().Be(FirstHourly);
        (await rig.ColumnAsync<string>("select event || '|' || process_id || '|' || business_key_member || '|' || input_map from {schema}.process_event_trigger"))
            .Should().Equal("case.opened|host.trigger_event|case_ref|{\"case_ref\":\"case_ref\",\"visit_date\":\"visit_date\"}");
    }

    [Fact]
    public async Task ActivatingTheSameActivationAgainChangesNothingAndKeepsEveryFireTime()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));
        var activation = new ProcessActivation(rig.Tenant, Process, "Test/Fixed", Input);
        await rig.Engine.ActivateAsync(activation);
        var before = await rig.ColumnAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule order by schedule_id");
        rig.Time.Advance(TimeSpan.FromMinutes(10));

        var again = await rig.Engine.ActivateAsync(activation with { InputJson = "{ \"site\" : \"s-1\" }" });

        again.Should().Be(new ActivationResult(1, false));
        (await rig.ColumnAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule order by schedule_id")).Should().Equal(before);
        (await rig.ScalarAsync<DateTimeOffset>("select activated_at from {schema}.process_activation")).Should().Be(Rig.Start, "an unchanged activation writes nothing");
    }

    [Fact]
    public async Task ANewVersionReplacesTheRowsAndAScheduleWhoseCronAndZoneDidNotChangeKeepsItsFireTime()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));
        var activation = new ProcessActivation(rig.Tenant, Process, "Test/Fixed", Input);
        await rig.Engine.ActivateAsync(activation);
        rig.Time.Advance(TimeSpan.FromMinutes(40));
        var second = Fixtures.HostEdited("trigger-schedule", text => text.Replace("\"version\": 1", "\"version\": 2", StringComparison.Ordinal).Replace("\"cron\": \"0 * * * *\", \"misfire\": \"skip\"", "\"cron\": \"30 * * * *\", \"misfire\": \"skip\"", StringComparison.Ordinal));
        rig.Catalog.Add(second);

        var result = await rig.Engine.ActivateAsync(activation);

        result.Should().Be(new ActivationResult(2, true));
        (await rig.ScalarAsync<long>("select process_version from {schema}.process_activation")).Should().Be(2);
        (await rig.ScalarAsync<string>("select process_hash from {schema}.process_activation")).Should().Be(second.ContentHash);
        (await rig.ScalarAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id = 'daily'")).Should().Be(FirstDaily, "unchanged cron and zone keep the fire time");
        (await rig.ScalarAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id = 'hourly_once'")).Should().Be(FirstHourly);
        (await rig.ScalarAsync<string>("select cron from {schema}.process_schedule where schedule_id = 'hourly_skip'")).Should().Be("30 * * * *");
        (await rig.ScalarAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id = 'hourly_skip'"))
            .Should().Be(new DateTimeOffset(2027, 1, 4, 15, 0, 0, TimeSpan.Zero), "a changed cron is evaluated again from now, 14:40, in a +05:30 zone: 20:30 local is 15:00");
    }

    [Fact]
    public async Task ANewZoneRecomputesTheSchedulesThatTakeItAndKeepsTheOnesThatDeclareTheirOwn()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "Test/Fixed", Input));
        rig.Time.Advance(TimeSpan.FromMinutes(5));

        var result = await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "UTC", Input));

        result.Changed.Should().BeTrue();
        (await rig.ColumnAsync<string>("select zone from {schema}.process_schedule order by schedule_id")).Should().Equal("America/New_York", "UTC", "UTC");
        (await rig.ScalarAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id = 'hourly_skip'")).Should().Be(new DateTimeOffset(2027, 1, 4, 15, 0, 0, TimeSpan.Zero));
        (await rig.ScalarAsync<DateTimeOffset>("select next_fire_at from {schema}.process_schedule where schedule_id = 'daily'")).Should().Be(FirstDaily);
    }

    [Fact]
    public async Task ANewInputReplacesTheActivationAndTheNextInstancesBeginWithIt()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "Test/Fixed", Input));

        var result = await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "Test/Fixed", "{\"site\":\"s-2\"}"));
        rig.Time.Advance(TimeSpan.FromMinutes(30));
        await rig.DrainAsync();

        result.Should().Be(new ActivationResult(1, true));
        var info = await rig.Engine.FindInstanceAsync(rig.Tenant, Process, "@schedule/hourly_skip/20270104T1430Z");
        info!.State!.Variables["site"].Should().Be("\"s-2\"");
    }

    [Fact]
    public async Task DeactivatingRemovesTheActivationAndEveryRowOfItAndReportsWhetherThereWasOne()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"), Fixtures.Host("trigger-event"));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "Test/Fixed", Input));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, "host.trigger_event"));

        (await rig.Engine.DeactivateAsync(rig.Tenant, Process)).Should().BeTrue();
        (await rig.Engine.DeactivateAsync(rig.Tenant, Process)).Should().BeFalse();

        (await rig.CountAsync("process_schedule")).Should().Be(0);
        (await rig.CountAsync("process_activation")).Should().Be(1);
        (await rig.CountAsync("process_event_trigger")).Should().Be(1);
        (await rig.Engine.DeactivateAsync(rig.Tenant, "host.trigger_event")).Should().BeTrue();
        (await rig.CountAsync("process_event_trigger")).Should().Be(0);
        (await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "Test/Fixed", Input))).Changed.Should().BeTrue("an activation that was removed can be made again");
    }

    [Fact]
    public async Task DeactivatingLeavesTheRunningInstancesAlone()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-event"));
        await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, "host.trigger_event", null, "{\"site\":\"s-1\"}"));
        await rig.Engine.PublishEventAsync(new ProcessEvent(rig.Tenant, "e-1", "case.opened", null, "{\"case_ref\":\"c-1\",\"visit_date\":\"2027-02-01\"}"));
        await rig.DrainAsync();

        await rig.Engine.DeactivateAsync(rig.Tenant, "host.trigger_event");

        var info = await rig.Engine.FindInstanceAsync(rig.Tenant, "host.trigger_event", "c-1");
        info!.Status.Should().Be(ProcessStatus.Waiting);
        (await rig.CountAsync("process_wait")).Should().Be(1);
    }

    [Fact]
    public async Task ASchedulesWithNeitherAZoneOfItsOwnNorOneOfTheActivationIsRefusedAndTheMessageNamesTheScheduleId()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));

        var act = () => rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process));

        var failure = (await act.Should().ThrowAsync<ArgumentException>()).Which;
        failure.Message.Should().Contain("hourly_skip").And.NotContain("daily");
        (await rig.CountAsync("process_activation")).Should().Be(0);
    }

    [Fact]
    public async Task AZoneTheHostDoesNotKnowIsRefusedAndTheMessageNamesTheScheduleId()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));

        var unknownActivationZone = () => rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "Nowhere/Unknown"));
        (await unknownActivationZone.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("hourly_skip");

        rig.Zones.Remove("America/New_York");
        var unknownOwnZone = () => rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "UTC"));
        (await unknownOwnZone.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("daily");

        (await rig.CountAsync("process_activation")).Should().Be(0);
        (await rig.CountAsync("process_schedule")).Should().Be(0);
    }

    [Fact]
    public async Task AZoneThatNoScheduleNeedsIsNotChecked()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-event"));

        var result = await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, "host.trigger_event", "Nowhere/Unknown"));

        result.Changed.Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"nothing\":\"x\"}", "process.input.unknown")]
    [InlineData("{\"run_for\":\"not-a-date\"}", "process.variable.type_mismatch")]
    public async Task AnInputThatNamesNoInputVariableOrHasTheWrongTypeIsRefusedWithTheCode(string input, string code)
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));

        var act = () => rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "UTC", input));

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain(code);
        (await rig.CountAsync("process_activation")).Should().Be(0);
    }

    [Fact]
    public async Task AnUnknownProcessAndBadArgumentsAreRefusedBeforeAnythingIsWritten()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));
        var engine = rig.Engine;

        await engine.Invoking(e => e.ActivateAsync(new ProcessActivation(rig.Tenant, "host.nothing"))).Should().ThrowAsync<InvalidOperationException>().WithMessage("*process.definition.unavailable*");
        await engine.Invoking(e => e.ActivateAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await engine.Invoking(e => e.ActivateAsync(new ProcessActivation("", Process))).Should().ThrowAsync<ArgumentException>();
        await engine.Invoking(e => e.ActivateAsync(new ProcessActivation(rig.Tenant, ""))).Should().ThrowAsync<ArgumentException>();
        await engine.Invoking(e => e.ActivateAsync(new ProcessActivation(rig.Tenant, Process, ""))).Should().ThrowAsync<ArgumentException>();
        await engine.Invoking(e => e.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "UTC", "[1]"))).Should().ThrowAsync<ArgumentException>();
        await engine.Invoking(e => e.ActivateAsync(new ProcessActivation(rig.Tenant, Process, "UTC", null!))).Should().ThrowAsync<ArgumentNullException>();
        await engine.Invoking(e => e.DeactivateAsync("", Process)).Should().ThrowAsync<ArgumentException>();
        await engine.Invoking(e => e.DeactivateAsync(rig.Tenant, new string('x', 201))).Should().ThrowAsync<ArgumentException>();
        (await rig.CountAsync("process_activation")).Should().Be(0);
    }

    [Fact]
    public async Task ADefinitionWithoutTriggersCanBeActivatedAndDoesNothing()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("quick"));

        var result = await rig.Engine.ActivateAsync(new ProcessActivation(rig.Tenant, "host.quick"));

        result.Should().Be(new ActivationResult(1, true));
        (await rig.CountAsync("process_schedule")).Should().Be(0);
        (await rig.CountAsync("process_event_trigger")).Should().Be(0);
    }

    [Fact]
    public async Task EightActivationsAtOnceWriteOneActivationAndExactlyOneOfThemChangedIt()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"));
        var activation = new ProcessActivation(rig.Tenant, Process, "Test/Fixed", Input);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => rig.Engine.ActivateAsync(activation))));

        results.Count(result => result.Changed).Should().Be(1);
        (await rig.CountAsync("process_activation")).Should().Be(1);
        (await rig.CountAsync("process_schedule")).Should().Be(3);
    }

    [Fact]
    public async Task ActivationsSchedulesAndEventTriggersOfTwoTenantsAreSeparate()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("trigger-schedule"), Fixtures.Host("trigger-event"));
        await rig.Engine.ActivateAsync(new ProcessActivation("t-1", Process, "Test/Fixed", Input));
        await rig.Engine.ActivateAsync(new ProcessActivation("t-2", Process, "UTC", "{\"site\":\"s-2\"}"));
        await rig.Engine.ActivateAsync(new ProcessActivation("t-2", "host.trigger_event"));

        (await rig.Engine.DeactivateAsync("t-1", "host.trigger_event")).Should().BeFalse("tenant t-1 never activated that process");
        (await rig.Engine.DeactivateAsync("t-1", Process)).Should().BeTrue();

        (await rig.CountAsync("process_activation", "tenant_id = 't-1'")).Should().Be(0);
        (await rig.CountAsync("process_activation", "tenant_id = 't-2'")).Should().Be(2);
        (await rig.CountAsync("process_schedule", "tenant_id = 't-2'")).Should().Be(3);
        (await rig.CountAsync("process_event_trigger", "tenant_id = 't-2'")).Should().Be(1);
        (await rig.ScalarAsync<string>("select zone from {schema}.process_schedule where tenant_id = 't-2' and schedule_id = 'hourly_skip'")).Should().Be("UTC");
    }
}

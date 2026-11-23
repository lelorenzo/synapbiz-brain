using FluentAssertions;
using Microsoft.Extensions.Logging;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// Delivery windows on contact actions: the effect is requested only at the opening of the window, never before, and a zone the
/// host cannot resolve is an error of the step. The window is Monday to Friday, 09:00 to 18:00 in a zone of the United States
/// pattern (UTC-5, UTC-4 in daylight saving).
/// </summary>
public sealed class WindowTests
{
    private static readonly DateTimeOffset MondayEvening = new(2027, 1, 4, 23, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TuesdayOpening = new(2027, 1, 5, 14, 0, 0, TimeSpan.Zero);

    private static async Task<(Rig Rig, List<DateTimeOffset> Calls)> CreateAsync(string fixture, Func<PortCall, PortResult>? answer = null)
    {
        var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host(fixture));
        var calls = new List<DateTimeOffset>();
        rig.Port("t.send").Handler = (call, _) =>
        {
            lock (calls)
            {
                calls.Add(rig.Time.GetUtcNow());
            }

            return Task.FromResult(answer?.Invoke(call) ?? new PortCompleted("{}"));
        };
        return (rig, calls);
    }

    [Fact]
    public async Task AContactActionInsideItsWindowIsSentAtOnce()
    {
        var (rig, calls) = await CreateAsync("window-contact");
        await using var _ = rig;

        var id = await rig.StartAsync("host.window", "k-1");
        await rig.DrainAsync();

        calls.Should().Equal(Rig.Start);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("sent");
        (await rig.CountAsync("process_timer")).Should().Be(0);
    }

    [Fact]
    public async Task AContactActionOutsideItsWindowIsSentAtTheOpeningAndNeverBefore()
    {
        var (rig, calls) = await CreateAsync("window-contact");
        await using var _ = rig;
        rig.Time.SetUtcNow(MondayEvening);

        var id = await rig.StartAsync("host.window", "k-1");
        await rig.DrainAsync();

        calls.Should().BeEmpty("18:00 is the end of the window and the end is exclusive");
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.State!.Pending!.Kind.Should().Be(ProcessPendingKind.Window);
        (await rig.ColumnAsync<DateTimeOffset>("select due_at from {schema}.process_timer")).Should().Equal(TuesdayOpening);
        (await rig.ScalarAsync<string>("select timer_id from {schema}.process_timer")).Should().Be(id + "/send/1/1/window");

        rig.Time.SetUtcNow(TuesdayOpening - TimeSpan.FromSeconds(1));
        await rig.DrainAsync();
        calls.Should().BeEmpty("one second before the opening nothing is sent");

        rig.Time.SetUtcNow(TuesdayOpening);
        await rig.DrainAsync();
        calls.Should().Equal(TuesdayOpening);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("sent");
        var kinds = (await rig.Engine.GetHistoryAsync(rig.Tenant, id)).Select(entry => entry.Kind).ToList();
        kinds.Should().ContainInOrder(ProcessHistoryKind.WindowWaiting, ProcessHistoryKind.WindowOpened, ProcessHistoryKind.EffectRequested);
    }

    [Fact]
    public async Task AFridayEveningWaitsForTheMondayMorning()
    {
        var (rig, calls) = await CreateAsync("window-contact");
        await using var _ = rig;
        rig.Time.SetUtcNow(new DateTimeOffset(2027, 1, 8, 23, 30, 0, TimeSpan.Zero));

        await rig.StartAsync("host.window", "k-1");
        await rig.DrainAsync();
        calls.Should().BeEmpty();
        rig.Time.SetUtcNow(new DateTimeOffset(2027, 1, 11, 13, 59, 59, TimeSpan.Zero));
        await rig.DrainAsync();
        calls.Should().BeEmpty("Saturday and Sunday are not listed");
        rig.Time.SetUtcNow(new DateTimeOffset(2027, 1, 11, 14, 0, 0, TimeSpan.Zero));
        await rig.DrainAsync();

        calls.Should().Equal(new DateTimeOffset(2027, 1, 11, 14, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task TheOpeningFollowsTheDaylightSavingRulesOfTheInjectedZone()
    {
        var (rig, calls) = await CreateAsync("window-contact");
        await using var _ = rig;
        rig.Time.SetUtcNow(new DateTimeOffset(2027, 3, 14, 12, 0, 0, TimeSpan.Zero));

        await rig.StartAsync("host.window", "k-1");
        await rig.DrainAsync();

        (await rig.ColumnAsync<DateTimeOffset>("select due_at from {schema}.process_timer")).Should().Equal(new DateTimeOffset(2027, 3, 15, 13, 0, 0, TimeSpan.Zero));
        rig.Time.SetUtcNow(new DateTimeOffset(2027, 3, 15, 13, 0, 0, TimeSpan.Zero));
        await rig.DrainAsync();
        calls.Should().Equal(new DateTimeOffset(2027, 3, 15, 13, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ARetryPassesTheWindowAgainSoItIsNotSentOutsideTheWindowEither()
    {
        var failed = false;
        var (rig, calls) = await CreateAsync("window-contact", _ =>
        {
            if (failed)
            {
                return new PortCompleted("{}");
            }

            failed = true;
            return new PortFailed(ProcessErrorKind.Transient, "t.busy");
        });
        await using var _ = rig;
        var first = new DateTimeOffset(2027, 1, 4, 22, 58, 0, TimeSpan.Zero);
        rig.Time.SetUtcNow(first);

        var id = await rig.StartAsync("host.window", "k-1");
        await rig.DrainAsync();
        calls.Should().Equal(first);

        rig.Time.SetUtcNow(first + TimeSpan.FromMinutes(5));
        await rig.DrainAsync();
        calls.Should().HaveCount(1, "the retry delay is over at 18:03 local, but the window is closed");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.State!.Pending!.Kind.Should().Be(ProcessPendingKind.Window);
        (await rig.ColumnAsync<DateTimeOffset>("select due_at from {schema}.process_timer")).Should().Equal(TuesdayOpening);

        rig.Time.SetUtcNow(TuesdayOpening - TimeSpan.FromSeconds(1));
        await rig.DrainAsync();
        calls.Should().HaveCount(1);
        rig.Time.SetUtcNow(TuesdayOpening);
        await rig.DrainAsync();

        calls.Should().Equal(first, TuesdayOpening);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("sent");
        (await rig.CountAsync("process_timer")).Should().Be(0);
    }

    [Fact]
    public async Task CancellingWhileTheWindowIsClosedRemovesTheTimerAndNothingIsEverSent()
    {
        var (rig, calls) = await CreateAsync("window-contact");
        await using var _ = rig;
        rig.Time.SetUtcNow(MondayEvening);
        var id = await rig.StartAsync("host.window", "k-1");
        (await rig.CountAsync("process_timer")).Should().Be(1);

        var cancelled = await rig.Engine.CancelAsync(rig.Tenant, id, "test.cancelled");
        rig.Time.SetUtcNow(TuesdayOpening);
        await rig.DrainAsync();

        cancelled.Disposition.Should().Be(AdvanceDisposition.Applied);
        (await rig.CountAsync("process_timer")).Should().Be(0);
        calls.Should().BeEmpty();
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Status.Should().Be(ProcessStatus.Cancelled);
    }

    [Fact]
    public async Task AZoneTheVariableNamesIsResolvedLikeAnyOther()
    {
        var (rig, calls) = await CreateAsync("window-zone-variable");
        await using var _ = rig;
        rig.Time.SetUtcNow(MondayEvening);

        var id = await rig.StartAsync("host.window_zone", "k-1", "{\"case_ref\":\"c-1\",\"practice_zone\":\"America/New_York\"}");
        rig.Time.SetUtcNow(TuesdayOpening);
        await rig.DrainAsync();

        calls.Should().Equal(TuesdayOpening);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Result.Should().Be("sent");
    }

    [Fact]
    public async Task AZoneTheHostDoesNotKnowFollowsOnErrorAndNothingIsSent()
    {
        var (rig, calls) = await CreateAsync("window-zone-variable");
        await using var _ = rig;

        var id = await rig.StartAsync("host.window_zone", "k-1", "{\"case_ref\":\"c-1\",\"practice_zone\":\"Nowhere/Unknown\"}");
        var queued = await rig.CountAsync("process_inbox", "kind = 6 and status = 1");
        await rig.DrainAsync();

        queued.Should().Be(1, "the failure is queued as a trigger of its own, through the inbox, and applied by the pump");
        calls.Should().BeEmpty();
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Status.Should().Be(ProcessStatus.Completed);
        info.Result.Should().Be("failed");
        var history = await rig.Engine.GetHistoryAsync(rig.Tenant, id);
        history.Should().Contain(entry => entry.Kind == ProcessHistoryKind.EffectFailed && entry.Code == "process.window.zone_unknown");
        (await rig.CountAsync("process_inbox", "kind = 6 and status = 2")).Should().Be(1);
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
        var warning = rig.Logs.Entries.Single(entry => entry.EventId.Id == 17);
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().StartWith("process.window.zone_unknown").And.NotContain("Nowhere");
    }

    [Fact]
    public async Task AZoneTheHostDoesNotKnowSuspendsTheInstanceWhenTheStepHasNoOnError()
    {
        var (rig, calls) = await CreateAsync("window-zone-suspend");
        await using var _ = rig;

        var id = await rig.StartAsync("host.window_suspend", "k-1", "{\"case_ref\":\"c-1\",\"practice_zone\":\"Nowhere/Unknown\"}");
        await rig.DrainAsync();

        calls.Should().BeEmpty();
        var info = await rig.Engine.GetInstanceAsync(rig.Tenant, id);
        info!.Status.Should().Be(ProcessStatus.Suspended);
        info.Problem!.Code.Should().Be("process.window.zone_unknown");
        (await rig.OutstandingWorkAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task ASuspendedInstanceThatIsResumedAfterTheZoneIsKnownSendsAtTheOpening()
    {
        var (rig, calls) = await CreateAsync("window-zone-suspend");
        await using var _ = rig;
        var id = await rig.StartAsync("host.window_suspend", "k-1", "{\"case_ref\":\"c-1\",\"practice_zone\":\"Test/Late\"}");
        await rig.DrainAsync();
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Status.Should().Be(ProcessStatus.Suspended);

        rig.Zones.Add("Test/Late", TestZoneResolver.Fixed);
        var resumed = await rig.Engine.ResumeAsync(rig.Tenant, id);
        await rig.DrainAsync();

        resumed.Disposition.Should().Be(AdvanceDisposition.Applied);
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.State!.Pending!.Kind.Should().Be(ProcessPendingKind.Window);
        rig.Time.Advance(TimeSpan.FromDays(1));
        await rig.DrainAsync();
        calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task AWindowFailedTriggerForAnInstanceThatWasCancelledMeanwhileIsIgnoredNotRejected()
    {
        var (rig, _) = await CreateAsync("window-zone-variable");
        await using var _ = rig;
        var id = await rig.StartAsync("host.window_zone", "k-1", "{\"case_ref\":\"c-1\",\"practice_zone\":\"Nowhere/Unknown\"}");

        await rig.Engine.CancelAsync(rig.Tenant, id, "test.cancelled");
        await rig.DrainAsync();

        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Status.Should().Be(ProcessStatus.Cancelled);
        (await rig.CountAsync("process_inbox", "kind = 6 and status = 2")).Should().Be(1);
        (await rig.CountAsync("process_inbox", "kind = 6 and status = 3")).Should().Be(0);
    }
}

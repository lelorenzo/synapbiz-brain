using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Section 3 of the W1-C specification: an action that names a delivery window waits for it before every attempt.</summary>
public sealed class WindowFlowTests
{
    private const string Variables = """
        {
          "case_ref": { "type": "Code", "input": true },
          "zone": { "type": "Code", "input": true }
        }
        """;

    private const string Windows = """
        "windows": {
          "hours": { "zone": "America/New_York", "days": ["mon", "tue", "wed", "thu", "fri"], "from": "09:00", "to": "18:00" },
          "by_zone": { "zone": { "variable": "zone" }, "days": ["sat", "sun"], "from": "08:30", "to": "24:00" }
        }
        """;

    private static string Steps(string window = "hours", string extra = "\"onError\": \"fail\", ") => $$"""
        {
          "s": { "kind": "action", "port": "messaging.notice", "delivery": "atLeastOnce", "contact": true, "window": "{{window}}",
                 "input": { "case": "case_ref" }, {{extra}}"next": "ok" },
          "ok": { "kind": "end", "result": "ok" },
          "fail": { "kind": "end", "result": "fail" }
        }
        """;

    private static Driver Started(string steps, string input = """{ "case_ref": "c-1", "zone": "Europe/Paris" }""")
    {
        var driver = new Driver(Docs.Load(Variables, steps, Windows));
        driver.Start(input);
        return driver;
    }

    [Fact]
    public void TheActionAsksForTheWindowInsteadOfRequestingTheEffect()
    {
        var driver = Started(Steps());

        driver.Last.Requests.Should().ContainSingle();
        var request = driver.Single<WindowRequest>();
        request.Step.Should().Be("s");
        request.TimerId.Should().Be("i-1/s/1/1/window");
        request.Zone.Should().Be("America/New_York");
        request.Days.Should().Equal(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday);
        request.From.Should().Be(new TimeOnly(9, 0));
        request.To.Should().Be(new TimeOnly(18, 0));
        driver.State.Status.Should().Be(ProcessStatus.Waiting);
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Window, "i-1/s/1/1/window"));
        driver.Kinds.Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.WindowWaiting);
        driver.Last.History[^1].DataJson.Should().Be("""{"timerId":"i-1/s/1/1/window","zone":"America/New_York"}""");
    }

    [Fact]
    public void TheEndOfTheDayIsWrittenAsMidnight()
    {
        var driver = Started(Steps("by_zone"));

        var request = driver.Single<WindowRequest>();

        request.Zone.Should().Be("Europe/Paris");
        request.Days.Should().Equal(DayOfWeek.Saturday, DayOfWeek.Sunday);
        request.From.Should().Be(new TimeOnly(8, 30));
        request.To.Should().Be(TimeOnly.MinValue);
    }

    [Fact]
    public void WhenTheWindowOpensTheEffectIsRequestedAsAlways()
    {
        var driver = Started(Steps());
        driver.Now = Docs.T0.AddHours(3);

        driver.Send(new TimerFired("i-1/s/1/1/window"));

        var effect = driver.Single<EffectRequest>();
        effect.Should().Be(new EffectRequest("s", "i-1/s/1/1", "i-1/s/1", ProcessEffectKind.Action, "messaging.notice", """{"case":"c-1"}""", 1, ProcessDelivery.AtLeastOnce));
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Effect, "i-1/s/1/1"));
        driver.Kinds.Should().Equal(ProcessHistoryKind.WindowOpened, ProcessHistoryKind.EffectRequested);
        driver.Last.History[0].At.Should().Be(Docs.T0.AddHours(3));
        driver.Last.History[0].DataJson.Should().Be("""{"timerId":"i-1/s/1/1/window"}""");

        driver.Send(new EffectCompleted("i-1/s/1/1", "{}"));
        driver.State.Result.Should().Be("ok");
    }

    [Fact]
    public void ANonContactActionMayNameAWindowToo()
    {
        var steps = """
            {
              "s": { "kind": "action", "port": "p", "delivery": "atLeastOnce", "window": "hours", "next": "ok" },
              "ok": { "kind": "end", "result": "ok" }
            }
            """;

        var driver = Started(steps);

        driver.Single<WindowRequest>().TimerId.Should().Be("i-1/s/1/1/window");
    }

    [Fact]
    public void ARepeatedOrLateOpeningIsIgnored()
    {
        var driver = Started(Steps());
        driver.Send(new TimerFired("i-1/s/1/1/window"));
        var before = driver.State;

        var again = driver.Send(new TimerFired("i-1/s/1/1/window"));
        var other = driver.Send(new TimerFired("i-1/s/1/9/window"));

        again.Disposition.Should().Be(AdvanceDisposition.Ignored);
        other.Disposition.Should().Be(AdvanceDisposition.Ignored);
        driver.State.Should().Be(before);
    }

    [Fact]
    public void ATimerThatIsNotTheWindowDoesNotOpenIt()
    {
        var driver = Started(Steps());

        var result = driver.Send(new TimerFired("i-1/s/1/retry/1"));

        result.Disposition.Should().Be(AdvanceDisposition.Ignored);
        driver.State.Pending!.Kind.Should().Be(ProcessPendingKind.Window);
    }

    [Fact]
    public void AZoneVariableWithoutAValueSuspendsTheInstance()
    {
        var driver = Started(Steps("by_zone"), """{ "case_ref": "c-1" }""");

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("process.window.zone_missing", "s"));
        driver.State.Pending.Should().BeNull();
        driver.Last.Requests.Should().BeEmpty();
        driver.Kinds.Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Suspended);
    }

    [Fact]
    public void ResumingASuspendedWindowGateAsksAgain()
    {
        var driver = Started(Steps("by_zone"), """{ "case_ref": "c-1" }""");

        driver.Send(new ResumeRequested());

        driver.State.Problem!.Code.Should().Be("process.window.zone_missing");
        driver.Kinds.Should().Equal(ProcessHistoryKind.Resumed, ProcessHistoryKind.Suspended);
    }

    [Fact]
    public void AZoneTheHostCannotResolveRoutesToOnError()
    {
        var driver = Started(Steps());

        driver.Send(new WindowFailed("i-1/s/1/1/window", "process.window.zone_unknown"));

        driver.State.Result.Should().Be("fail");
        driver.Last.Requests.Should().BeEmpty();
        driver.Kinds.Should().Equal(ProcessHistoryKind.EffectFailed, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Completed);
        driver.Last.History[0].Code.Should().Be("process.window.zone_unknown");
    }

    [Fact]
    public void AZoneTheHostCannotResolveSuspendsWithoutOnErrorAndCancelsNothing()
    {
        var steps = """
            {
              "s": { "kind": "action", "port": "p", "delivery": "atLeastOnce", "window": "hours", "next": "ok" },
              "ok": { "kind": "end", "result": "ok" }
            }
            """;
        var driver = Started(steps);

        driver.Send(new WindowFailed("i-1/s/1/1/window", "process.window.zone_unknown"));

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("process.window.zone_unknown", "s"));
        driver.Last.Requests.Should().BeEmpty();
        driver.Kinds.Should().Equal(ProcessHistoryKind.EffectFailed, ProcessHistoryKind.Suspended);
    }

    [Fact]
    public void AFailureThatDoesNotConcernTheWindowIsIgnored()
    {
        var driver = Started(Steps());

        var result = driver.Send(new WindowFailed("i-1/s/1/2/window", "process.window.zone_unknown"));

        result.Disposition.Should().Be(AdvanceDisposition.Ignored);
        driver.State.Pending!.Id.Should().Be("i-1/s/1/1/window");
    }

    [Fact]
    public void AFailureOfTheWindowAfterItOpenedIsIgnored()
    {
        var driver = Started(Steps());
        driver.Send(new TimerFired("i-1/s/1/1/window"));

        driver.Send(new WindowFailed("i-1/s/1/1/window", "process.window.zone_unknown")).Disposition.Should().Be(AdvanceDisposition.Ignored);
    }

    [Fact]
    public void ARetryPassesTheGateAgain()
    {
        var driver = Started(Steps(extra: "\"retry\": { \"maxAttempts\": 2, \"backoff\": \"fixed\", \"initial\": \"PT1M\" }, \"onError\": \"fail\", "));
        driver.Send(new TimerFired("i-1/s/1/1/window"));

        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Transient, "busy"));

        driver.Single<TimerRequest>().TimerId.Should().Be("i-1/s/1/retry/1");
        driver.State.Pending!.Kind.Should().Be(ProcessPendingKind.RetryDelay);

        driver.Now = Docs.T0.AddMinutes(1);
        driver.Send(new TimerFired("i-1/s/1/retry/1"));

        // The retry delay ran first; now the window gate: no effect yet, a new window request for attempt 2.
        driver.Last.Requests.OfType<EffectRequest>().Should().BeEmpty();
        driver.Single<WindowRequest>().TimerId.Should().Be("i-1/s/1/2/window");
        driver.State.Attempt.Should().Be(2);
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Window, "i-1/s/1/2/window"));

        driver.Send(new TimerFired("i-1/s/1/2/window"));

        driver.Single<EffectRequest>().EffectId.Should().Be("i-1/s/1/2");
        driver.Single<EffectRequest>().Attempt.Should().Be(2);
        driver.Send(new EffectCompleted("i-1/s/1/2", "{}"));
        driver.State.Result.Should().Be("ok");
    }

    [Fact]
    public void CancellingWhileTheWindowIsClosedCancelsItsTimer()
    {
        var driver = Started(Steps());

        driver.Send(new CancelRequested("operator"));

        driver.State.Status.Should().Be(ProcessStatus.Cancelled);
        driver.Last.Requests.Should().Equal(new CancelTimerRequest("s", "i-1/s/1/1/window"));
    }

    [Fact]
    public void TheStateWhileWaitingForTheWindowRoundTrips()
    {
        var driver = Started(Steps());

        var json = driver.State.ToJson();
        var loaded = ProcessState.FromJson(json);

        json.Should().Contain("\"pending\":{\"kind\":\"Window\",\"id\":\"i-1/s/1/1/window\",\"timeoutTimerId\":null}");
        loaded.Should().Be(driver.State);
        loaded.ToJson().Should().Be(json);
    }

    [Fact]
    public void AnAtMostOnceContactActionWaitsForTheWindowToo()
    {
        var steps = """
            {
              "s": { "kind": "action", "port": "p", "delivery": "atMostOnce", "contact": true, "window": "hours", "next": "ok", "onUnknown": "fail" },
              "ok": { "kind": "end", "result": "ok" },
              "fail": { "kind": "end", "result": "fail" }
            }
            """;
        var driver = Started(steps);
        driver.Send(new TimerFired("i-1/s/1/1/window"));

        driver.Single<EffectRequest>().Delivery.Should().Be(ProcessDelivery.AtMostOnce);
    }
}

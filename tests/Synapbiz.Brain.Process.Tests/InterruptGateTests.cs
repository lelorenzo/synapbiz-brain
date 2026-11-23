using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>W1-G, G2: an <c>action</c> that names a delivery window and an <c>interruptBy</c> event.</summary>
public sealed class InterruptGateTests
{
    private const string Variables = """
        {
          "case_ref": { "type": "Code", "input": true },
          "zone": { "type": "Code", "input": true },
          "kind": { "type": "Code" }
        }
        """;

    private const string Windows = """
        "windows": {
          "hours": { "zone": "America/New_York", "days": ["mon", "tue", "wed", "thu", "fri"], "from": "09:00", "to": "18:00" },
          "by_zone": { "zone": { "variable": "zone" }, "days": ["sat", "sun"], "from": "08:30", "to": "24:00" }
        }
        """;

    private const string Interrupt = """
        "interruptBy": { "event": "case.reply_received", "correlation": "case_ref", "output": { "reply_kind": "kind" }, "to": "reply" }
        """;

    private const string WaitId = "i-1/s/1/1/wait";
    private const string WindowId = "i-1/s/1/1/window";

    private static string Steps(string window = "hours", string extra = "", string interrupt = Interrupt, bool pre = false) => $$"""
        {
          "s": { "kind": "action", "port": "messaging.notice", "delivery": "atLeastOnce", "contact": true, "window": "{{window}}",
                 "input": { "case": "case_ref" }, {{extra}}{{interrupt}}, "next": "ok", "onError": "fail" },
          {{(pre ? "\"pre\": { \"kind\": \"wait\", \"event\": \"go\", \"next\": \"s\" }," : "")}}
          "ok": { "kind": "end", "result": "sent" },
          "reply": { "kind": "end", "result": "replied" },
          "fail": { "kind": "end", "result": "fail" }
        }
        """;

    private static BrainProcess Load(string steps) => Docs.Load(Variables, steps, Windows + ", \"accepts\": [\"case.reply_received\", \"go\"]");

    private static Driver Started(string? steps = null, string input = """{ "case_ref": "c-1", "zone": "Europe/Paris" }""")
    {
        var driver = new Driver(Load(steps ?? Steps()));
        driver.Start(input, "i-1");
        return driver;
    }

    // The instance is on a step before the gate, so that an event can be buffered ahead of it.
    private static Driver AtPre(string? window = null, string input = """{ "case_ref": "c-1", "zone": "Europe/Paris" }""")
    {
        var driver = new Driver(Docs.Load(Variables, Steps(window ?? "hours", pre: true), Windows + ", \"accepts\": [\"case.reply_received\", \"go\"]", start: "pre"));
        driver.Start(input, "i-1");
        return driver;
    }

    private static EventReceived Reply(string? correlation = "c-1", string payload = """{ "reply_kind": "call" }""") =>
        new("case.reply_received", correlation, payload);

    [Fact]
    public void TheGateAsksForTheWindowAndForTheEventTogether()
    {
        var driver = Started();

        driver.Last.Requests.Should().HaveCount(2);
        driver.Last.Requests[0].Should().BeOfType<WindowRequest>().Which.TimerId.Should().Be(WindowId);
        driver.Last.Requests[1].Should().Be(new WaitRequest("s", WaitId, "case.reply_received", "c-1"));
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Window, WindowId) { InterruptWaitId = WaitId });
        driver.State.Status.Should().Be(ProcessStatus.Waiting);
        driver.Kinds.Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.WindowWaiting, ProcessHistoryKind.WaitStarted);
        driver.Last.History[^1].DataJson.Should().Be("""{"event":"case.reply_received","waitId":"i-1/s/1/1/wait"}""");
    }

    [Fact]
    public void TheWaitIdIsStateTextOnlyWhileTheGateWaitsForTheEvent()
    {
        var driver = Started();

        var json = driver.State.ToJson();

        json.Should().Contain("\"pending\":{\"kind\":\"Window\",\"id\":\"i-1/s/1/1/window\",\"timeoutTimerId\":null,\"interruptWaitId\":\"i-1/s/1/1/wait\"}");
        var loaded = ProcessState.FromJson(json);
        loaded.Should().Be(driver.State);
        loaded.ToJson().Should().Be(json);

        driver.Send(new TimerFired(WindowId));

        driver.State.ToJson().Should().NotContain("interruptWaitId");
    }

    [Fact]
    public void AGateWithoutAnInterruptWritesTheStateAsBefore()
    {
        var plain = """
            {
              "s": { "kind": "action", "port": "messaging.notice", "delivery": "atLeastOnce", "contact": true, "window": "hours", "next": "ok" },
              "ok": { "kind": "end", "result": "sent" }
            }
            """;
        var driver = Started(plain);

        driver.Last.Requests.Should().ContainSingle().Which.Should().BeOfType<WindowRequest>();
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Window, WindowId));
        driver.State.ToJson().Should().Contain("\"pending\":{\"kind\":\"Window\",\"id\":\"i-1/s/1/1/window\",\"timeoutTimerId\":null}").And.NotContain("interruptWaitId");
        driver.Kinds.Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.WindowWaiting);
    }

    [Fact]
    public void AMatchingEventWhileTheGateIsPendingCancelsTheWindowTimerAndGoesToTheTarget()
    {
        var driver = Started();

        driver.Send(Reply());

        driver.Last.Requests.Should().Equal(new CancelTimerRequest("s", WindowId));
        driver.State.Result.Should().Be("replied");
        driver.Variable("kind").Should().Be("\"call\"");
        driver.Kinds.Should().Equal(ProcessHistoryKind.EventConsumed, ProcessHistoryKind.WindowInterrupted, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Completed);
        driver.Last.History[1].DataJson.Should().Be("""{"event":"case.reply_received"}""");
        driver.Requests.OfType<EffectRequest>().Should().BeEmpty("the effect is never requested");
        driver.State.Inbox.Should().BeEmpty();
    }

    [Fact]
    public void ACorrelationThatDoesNotMatchDoesNotInterruptAndIsBuffered()
    {
        var driver = Started();

        driver.Send(Reply("c-2"));

        driver.Kinds.Should().Equal(ProcessHistoryKind.EventBuffered);
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Window, WindowId) { InterruptWaitId = WaitId });
        driver.State.Inbox.Should().ContainSingle();
    }

    [Fact]
    public void AnEventThatIsNotTheInterruptEventIsBufferedWhenAcceptedAndRejectedOtherwise()
    {
        var driver = Started();

        driver.Send(new EventReceived("go", null, "{}")).Disposition.Should().Be(AdvanceDisposition.Applied);
        driver.Send(new EventReceived("other", null, "{}")).Disposition.Should().Be(AdvanceDisposition.Rejected);

        driver.State.Pending!.Kind.Should().Be(ProcessPendingKind.Window);
        driver.State.Inbox.Should().ContainSingle().Which.Name.Should().Be("go");
    }

    [Fact]
    public void AnEventBufferedBeforeTheGateIsConsumedAtTheGateWithoutAWindowRequest()
    {
        var driver = AtPre();
        driver.Send(Reply());
        driver.State.Inbox.Should().ContainSingle();

        driver.Send(new EventReceived("go", null, "{}"));

        driver.Last.Requests.Should().BeEmpty("no window and no wait are requested when the event is already there");
        driver.State.Result.Should().Be("replied");
        driver.Variable("kind").Should().Be("\"call\"");
        driver.State.Inbox.Should().BeEmpty();
        driver.Kinds.Should().Equal(
            ProcessHistoryKind.EventConsumed,
            ProcessHistoryKind.StepEntered,
            ProcessHistoryKind.EventConsumed,
            ProcessHistoryKind.WindowInterrupted,
            ProcessHistoryKind.StepEntered,
            ProcessHistoryKind.Completed);
    }

    [Fact]
    public void ABufferedEventOfAnotherCorrelationStaysInTheInboxAndTheGateWaits()
    {
        var driver = AtPre();
        driver.Send(Reply("c-2"));

        driver.Send(new EventReceived("go", null, "{}"));

        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Window, "i-1/s/2/1/window") { InterruptWaitId = "i-1/s/2/1/wait" });
        driver.State.Inbox.Should().ContainSingle().Which.Correlation.Should().Be("c-2");
        driver.Last.Requests.Should().HaveCount(2);
    }

    [Fact]
    public void TheWindowOpeningFirstCancelsTheWaitAndRequestsTheEffect()
    {
        var driver = Started();
        driver.Now = Docs.T0.AddHours(3);

        driver.Send(new TimerFired(WindowId));

        driver.Last.Requests.Should().HaveCount(2);
        driver.Last.Requests[0].Should().Be(new CancelWaitRequest("s", WaitId));
        driver.Last.Requests[1].Should().BeOfType<EffectRequest>().Which.EffectId.Should().Be("i-1/s/1/1");
        driver.Kinds.Should().Equal(ProcessHistoryKind.WindowOpened, ProcessHistoryKind.EffectRequested);
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Effect, "i-1/s/1/1"));
    }

    [Fact]
    public void AnEventAfterTheWindowOpenedIsBufferedByTheInboxRuleAndDoesNotInterrupt()
    {
        var driver = Started();
        driver.Send(new TimerFired(WindowId));

        driver.Send(Reply());

        driver.Kinds.Should().Equal(ProcessHistoryKind.EventBuffered);
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Effect, "i-1/s/1/1"));
        driver.Send(new EffectCompleted("i-1/s/1/1", "{}"));
        driver.State.Result.Should().Be("sent");
        driver.State.Inbox.Should().ContainSingle();
    }

    [Fact]
    public void ALateWindowTimerAfterTheInterruptionIsIgnoredAndAnEventAfterTheOpeningCannotInterrupt()
    {
        var interrupted = Started();
        interrupted.Send(Reply());
        var opened = Started();
        opened.Send(new TimerFired(WindowId));

        interrupted.Send(new TimerFired(WindowId)).Disposition.Should().Be(AdvanceDisposition.Ignored);
        interrupted.State.Result.Should().Be("replied");
        opened.Send(Reply()).Disposition.Should().Be(AdvanceDisposition.Applied);
        opened.State.Pending!.Kind.Should().Be(ProcessPendingKind.Effect);
    }

    [Fact]
    public void TheGateBeforeARetryAsksAgainWithItsOwnWaitId()
    {
        var driver = Started(Steps(extra: "\"retry\": { \"maxAttempts\": 2, \"backoff\": \"fixed\", \"initial\": \"PT1M\" }, "));
        driver.Send(new TimerFired(WindowId));
        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Transient, "busy"));

        driver.Now = Docs.T0.AddMinutes(1);
        driver.Send(new TimerFired("i-1/s/1/retry/1"));

        driver.Last.Requests[0].Should().BeOfType<WindowRequest>().Which.TimerId.Should().Be("i-1/s/1/2/window");
        driver.Last.Requests[1].Should().Be(new WaitRequest("s", "i-1/s/1/2/wait", "case.reply_received", "c-1"));
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Window, "i-1/s/1/2/window") { InterruptWaitId = "i-1/s/1/2/wait" });

        driver.Send(Reply());

        driver.Last.Requests.Should().Equal(new CancelTimerRequest("s", "i-1/s/1/2/window"));
        driver.State.Result.Should().Be("replied");
    }

    [Fact]
    public void AnEventDuringTheRetryDelayIsBufferedAndTheGateAfterTheDelayTakesIt()
    {
        var driver = Started(Steps(extra: "\"retry\": { \"maxAttempts\": 2, \"backoff\": \"fixed\", \"initial\": \"PT1M\" }, "));
        driver.Send(new TimerFired(WindowId));
        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Transient, "busy"));

        driver.Send(Reply()).Disposition.Should().Be(AdvanceDisposition.Applied);
        driver.Kinds.Should().Equal(ProcessHistoryKind.EventBuffered);
        driver.State.Pending!.Kind.Should().Be(ProcessPendingKind.RetryDelay, "a retry delay is not interruptible");

        driver.Now = Docs.T0.AddMinutes(1);
        driver.Send(new TimerFired("i-1/s/1/retry/1"));

        driver.Last.Requests.Should().BeEmpty("the buffered event interrupts the gate before any request");
        driver.State.Result.Should().Be("replied");
    }

    [Fact]
    public void WindowFailedCancelsTheWaitAndFollowsOnError()
    {
        var driver = Started();

        driver.Send(new WindowFailed(WindowId, "process.window.zone_unknown"));

        driver.Last.Requests.Should().Equal(new CancelWaitRequest("s", WaitId));
        driver.State.Result.Should().Be("fail");
    }

    [Fact]
    public void WindowFailedWithoutOnErrorSuspendsAndCancelsTheWaitOnce()
    {
        var steps = """
            {
              "s": { "kind": "action", "port": "messaging.notice", "delivery": "atLeastOnce", "window": "hours", "next": "ok",
                     "interruptBy": { "event": "case.reply_received", "correlation": "case_ref", "to": "reply" } },
              "ok": { "kind": "end", "result": "sent" },
              "reply": { "kind": "end", "result": "replied" }
            }
            """;
        var driver = Started(steps);

        driver.Send(new WindowFailed(WindowId, "process.window.zone_unknown"));

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.Last.Requests.Should().Equal(new CancelWaitRequest("s", WaitId));
    }

    [Fact]
    public void CancellingWhileGatedCancelsTheWindowTimerAndTheWait()
    {
        var driver = Started();

        driver.Send(new CancelRequested("operator"));

        driver.State.Status.Should().Be(ProcessStatus.Cancelled);
        driver.Last.Requests.Should().Equal(new CancelTimerRequest("s", WindowId), new CancelWaitRequest("s", WaitId));
    }

    [Theory]
    [InlineData("""{ "reply_kind": 7 }""")]
    [InlineData("[1]")]
    public void APayloadThatDoesNotFitSuspendsLikeAnAwaitedEventAndCancelsBothRequests(string payload)
    {
        var driver = Started();

        driver.Send(Reply(payload: payload));

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("process.variable.type_mismatch", "s"));
        driver.State.Pending.Should().BeNull();
        driver.Last.Requests.Should().Equal(new CancelTimerRequest("s", WindowId), new CancelWaitRequest("s", WaitId));

        driver.Send(new ResumeRequested());

        driver.State.Status.Should().Be(ProcessStatus.Waiting, "a resume enters the gate again");
        driver.Last.Requests[0].Should().BeOfType<WindowRequest>().Which.TimerId.Should().Be("i-1/s/2/1/window");
        driver.Last.Requests[1].Should().Be(new WaitRequest("s", "i-1/s/2/1/wait", "case.reply_received", "c-1"));
    }

    [Fact]
    public void ABufferedPayloadThatDoesNotFitSuspendsAtTheGate()
    {
        var driver = AtPre();
        driver.Send(Reply(payload: """{ "reply_kind": 7 }"""));

        driver.Send(new EventReceived("go", null, "{}"));

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("process.variable.type_mismatch", "s"));
        driver.State.Inbox.Should().BeEmpty("the event was taken out of the inbox, as for a wait");
        driver.Last.Requests.Should().BeEmpty();
    }

    [Fact]
    public void AMissingCorrelationSuspendsBeforeAnyRequest()
    {
        var driver = Started(input: """{ "zone": "Europe/Paris" }""");

        driver.State.Problem.Should().Be(new ProcessProblem("process.wait.correlation_missing", "s"));
        driver.Requests.Should().BeEmpty();
        driver.Kinds.Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Suspended);
    }

    [Fact]
    public void AMissingZoneSuspendsWithoutAWaitRequestAndABufferedEventNeedsNoZone()
    {
        var steps = Steps("by_zone");
        var missing = Started(steps, """{ "case_ref": "c-1" }""");
        var buffered = AtPre("by_zone", """{ "case_ref": "c-1" }""");
        buffered.Send(Reply());
        buffered.Send(new EventReceived("go", null, "{}"));

        missing.State.Problem.Should().Be(new ProcessProblem("process.window.zone_missing", "s"));
        missing.Requests.Should().BeEmpty();
        buffered.State.Result.Should().Be("replied");
    }

    [Fact]
    public void ACorrelationThatNamesAReferenceMatchesWhateverTheCase()
    {
        var variables = """
            {
              "case_ref": { "type": "Reference", "input": true },
              "zone": { "type": "Code", "input": true },
              "kind": { "type": "Code" }
            }
            """;
        var steps = Steps().Replace("\"input\": { \"case\": \"case_ref\" }, ", "", StringComparison.Ordinal);
        var driver = new Driver(Docs.Load(variables, steps, Windows + ", \"accepts\": [\"case.reply_received\", \"go\"]"));
        driver.Start("""{ "case_ref": "AAAAAAAA-0000-0000-0000-000000000001" }""", "i-1");

        driver.Send(Reply("aaaaaaaa-0000-0000-0000-000000000001"));

        driver.State.Result.Should().Be("replied");
    }

    [Fact]
    public void AnInterruptWithoutCorrelationOrOutputMatchesByNameAlone()
    {
        var steps = Steps(interrupt: "\"interruptBy\": { \"event\": \"case.reply_received\", \"to\": \"reply\" }");
        var driver = Started(steps);

        driver.Last.Requests[1].Should().Be(new WaitRequest("s", WaitId, "case.reply_received", null));
        driver.Send(Reply("anything", "{}"));

        driver.State.Result.Should().Be("replied");
    }

    [Fact]
    public void TheEventThatInterruptsNeedNotBeAcceptedBecauseTheGateConsumesItAtOnce()
    {
        var driver = new Driver(Docs.Load(Variables, Steps(), Windows));
        driver.Start("""{ "case_ref": "c-1", "zone": "Europe/Paris" }""", "i-1");

        driver.Send(Reply());

        driver.State.Result.Should().Be("replied");
    }

    [Fact]
    public void TheRunIsDeterministic()
    {
        string Run()
        {
            var driver = Started();
            driver.Send(Reply());
            return driver.State.ToJson() + string.Join("|", driver.Requests.Select(request => request.ToString())) + string.Join("|", driver.History.Select(entry => entry.Kind + entry.DataJson));
        }

        Run().Should().Be(Run());
    }

    [Fact]
    public void AnAcceptedEventOfAnotherNameWithTheSameCorrelationIsBufferedAndDoesNotInterrupt()
    {
        var driver = Started();

        driver.Send(new EventReceived("go", "c-1", """{ "reply_kind": "call" }""")).Disposition.Should().Be(AdvanceDisposition.Applied);

        driver.Last.Requests.Should().BeEmpty("the gate keeps its window timer and its wait");
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Window, WindowId) { InterruptWaitId = WaitId });
        driver.State.Inbox.Should().ContainSingle().Which.Name.Should().Be("go");
        driver.State.Variables.ContainsKey("kind").Should().BeFalse();
        driver.Kinds.Should().NotContain(ProcessHistoryKind.WindowInterrupted);
    }

    [Fact]
    public void AGateEnteredAgainInALoopAsksForANewWaitAndANewWindowTimer()
    {
        const string steps = """
            {
              "s": { "kind": "loop", "maxIterations": 2, "body": "g", "onExhausted": "done" },
              "g": { "kind": "action", "port": "messaging.notice", "delivery": "atLeastOnce", "contact": true, "window": "hours",
                     "input": { "case": "case_ref" },
                     "interruptBy": { "event": "case.reply_received", "correlation": "case_ref", "to": "s" }, "next": "s" },
              "done": { "kind": "end", "result": "done" }
            }
            """;
        var driver = Started(steps);
        var firstWait = driver.Last.Requests.OfType<WaitRequest>().Single();
        var firstWindow = driver.Last.Requests.OfType<WindowRequest>().Single();

        driver.Send(Reply());
        var secondWait = driver.Last.Requests.OfType<WaitRequest>().Single();
        var secondWindow = driver.Last.Requests.OfType<WindowRequest>().Single();

        secondWait.WaitId.Should().NotBe(firstWait.WaitId);
        secondWindow.TimerId.Should().NotBe(firstWindow.TimerId);
        driver.Last.Requests.OfType<CancelTimerRequest>().Single().TimerId.Should().Be(firstWindow.TimerId);
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Window, secondWindow.TimerId) { InterruptWaitId = secondWait.WaitId });
        driver.State.Inbox.Should().BeEmpty();

        driver.Send(Reply());

        driver.State.Result.Should().Be("done");
        driver.History.Count(entry => entry.Kind == ProcessHistoryKind.WindowInterrupted).Should().Be(2);
    }
}

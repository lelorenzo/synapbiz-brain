using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>W1-G, G1: the <c>timeout</c> of a <c>wait</c> and of a <c>humanTask</c> given as an instant variable plus an offset.</summary>
public sealed class TimeoutAtTests
{
    // The clock of the tests is 2027-01-04T14:00:00Z; the visit is at 20:00Z, so two hours before it is 18:00Z.
    private const string Variables = """
        {
          "case_ref": { "type": "Code", "input": true },
          "visit_at": { "type": "Instant", "input": true },
          "note": { "type": "Code" }
        }
        """;

    private const string VisitInput = """{ "case_ref": "c-1", "visit_at": "2027-01-04T20:00:00Z" }""";

    private const string Wait = """
        { "kind": "wait", "event": "case.ready", "correlation": "case_ref", "output": { "n": "note" }, "next": "ok",
          "timeout": { "until": "visit_at", "offset": "-PT2H" }, "onTimeout": "late" }
        """;

    private const string Task = """
        { "kind": "humanTask", "queue": "front_desk", "outcomes": ["yes"], "on": { "yes": "ok" },
          "timeout": { "until": "visit_at", "offset": "-PT2H" }, "onTimeout": "late" }
        """;

    private static readonly DateTimeOffset Due = new(2027, 1, 4, 18, 0, 0, TimeSpan.Zero);

    private static Driver Started(string step, string input = VisitInput, string extra = "\"accepts\": [\"case.ready\"]")
    {
        var steps = "{ \"s\": " + step + ", \"ok\": { \"kind\": \"end\", \"result\": \"ok\" }, \"late\": { \"kind\": \"end\", \"result\": \"late\" } }";
        var driver = new Driver(Docs.Load(Variables, steps, extra));
        driver.Start(input);
        return driver;
    }

    [Fact]
    public void AWaitRequestsItsWaitAndATimerAtTheInstantPlusTheOffset()
    {
        var driver = Started(Wait);

        driver.Last.Requests.Should().Equal(
            new WaitRequest("s", "i-1/s/1/wait", "case.ready", "c-1"),
            new TimerRequest("s", "i-1/s/1/timeout", Due));
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Wait, "i-1/s/1/wait", "i-1/s/1/timeout"));
        driver.Kinds.Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.WaitStarted);
    }

    [Fact]
    public void ThePendingTimeoutIsTheSameShapeAsForADuration()
    {
        var driver = Started(Wait);

        var json = driver.State.ToJson();

        json.Should().Contain("\"pending\":{\"kind\":\"Wait\",\"id\":\"i-1/s/1/wait\",\"timeoutTimerId\":\"i-1/s/1/timeout\"}");
        ProcessState.FromJson(json).Should().Be(driver.State);
    }

    [Fact]
    public void ThePositiveOffsetAndTheMissingOffsetAreRead()
    {
        var later = Started(Wait.Replace("-PT2H", "PT90M", StringComparison.Ordinal));
        var exact = Started(Wait.Replace(", \"offset\": \"-PT2H\"", "", StringComparison.Ordinal));

        later.Single<TimerRequest>().DueAt.Should().Be(new DateTimeOffset(2027, 1, 4, 21, 30, 0, TimeSpan.Zero));
        exact.Single<TimerRequest>().DueAt.Should().Be(new DateTimeOffset(2027, 1, 4, 20, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void TheEventBeforeTheInstantWinsAndCancelsTheTimer()
    {
        var driver = Started(Wait);
        driver.Now = Docs.T0.AddHours(1);

        driver.Send(new EventReceived("case.ready", "c-1", """{ "n": "x" }"""));

        driver.Last.Requests.Should().Equal(new CancelTimerRequest("s", "i-1/s/1/timeout"));
        driver.State.Result.Should().Be("ok");
        driver.Variable("note").Should().Be("\"x\"");
        driver.Kinds.Should().Equal(ProcessHistoryKind.EventConsumed, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Completed);
    }

    [Fact]
    public void TheTimerAtTheInstantWinsAndCancelsTheWait()
    {
        var driver = Started(Wait);
        driver.Now = Due;

        driver.Send(new TimerFired("i-1/s/1/timeout"));

        driver.Last.Requests.Should().Equal(new CancelWaitRequest("s", "i-1/s/1/wait"));
        driver.State.Result.Should().Be("late");
        driver.Kinds.Should().Equal(ProcessHistoryKind.TimedOut, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Completed);
        driver.Last.History[0].DataJson.Should().Be("""{"timerId":"i-1/s/1/timeout"}""");
    }

    [Fact]
    public void AnEventAfterTheTimerAndATimerAfterTheEventAreIgnored()
    {
        var timerFirst = Started(Wait);
        timerFirst.Now = Due;
        timerFirst.Send(new TimerFired("i-1/s/1/timeout"));
        var eventFirst = Started(Wait);
        eventFirst.Send(new EventReceived("case.ready", "c-1", "{}"));

        timerFirst.Send(new EventReceived("case.ready", "c-1", "{}")).Disposition.Should().Be(AdvanceDisposition.Ignored);
        timerFirst.State.Result.Should().Be("late");
        eventFirst.Send(new TimerFired("i-1/s/1/timeout")).Disposition.Should().Be(AdvanceDisposition.Ignored);
        eventFirst.State.Result.Should().Be("ok");
    }

    [Fact]
    public void ABufferedEventIsConsumedFirstAndNoTimeoutIsComputed()
    {
        // The event is buffered before the wait is entered, and the variable that would give the deadline has no value.
        var steps = """
            {
              "pre": { "kind": "wait", "event": "go", "next": "s" },
              "s": { "kind": "wait", "event": "case.ready", "correlation": "case_ref", "next": "ok",
                     "timeout": { "until": "visit_at", "offset": "-PT2H" }, "onTimeout": "late" },
              "ok": { "kind": "end", "result": "ok" }, "late": { "kind": "end", "result": "late" }
            }
            """;
        var driver = new Driver(Docs.Load(Variables, steps, "\"accepts\": [\"case.ready\", \"go\"]", start: "pre"));
        driver.Start("""{ "case_ref": "c-1" }""");
        driver.Send(new EventReceived("case.ready", "c-1", "{}"));
        driver.Kinds.Should().Equal(ProcessHistoryKind.EventBuffered);

        driver.Send(new EventReceived("go", null, "{}"));

        driver.State.Status.Should().Be(ProcessStatus.Completed);
        driver.State.Result.Should().Be("ok");
        driver.Last.Requests.Should().BeEmpty();
        driver.State.Inbox.Should().BeEmpty();
    }

    [Fact]
    public void AnInstantAtOrBeforeTheClockLeavesThroughOnTimeoutAtOnceWithoutAWaitRow()
    {
        var before = Started(Wait, """{ "case_ref": "c-1", "visit_at": "2027-01-04T15:00:00Z" }""");
        var exactly = Started(Wait, """{ "case_ref": "c-1", "visit_at": "2027-01-04T16:00:00Z" }""");

        foreach (var driver in new[] { before, exactly })
        {
            driver.State.Result.Should().Be("late");
            driver.Requests.Should().BeEmpty("no wait and no timer are requested for a deadline that is already due");
            driver.Kinds.Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.TimedOut, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Completed);
        }

        before.Last.History.First(entry => entry.Kind == ProcessHistoryKind.TimedOut).DataJson.Should().Be("""{"dueAt":"2027-01-04T13:00:00.0000000+00:00"}""");
        Started(Wait, """{ "case_ref": "c-1", "visit_at": "2027-01-04T16:00:01Z" }""").Single<TimerRequest>().DueAt.Should().Be(Docs.T0.AddSeconds(1));
    }

    [Fact]
    public void AVariableWithoutAValueSuspendsTheInstanceAndResumingEntersTheStepAgain()
    {
        var driver = Started(Wait, """{ "case_ref": "c-1" }""");

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("process.timeout.instant_missing", "s", "visit_at"));
        driver.State.Pending.Should().BeNull();
        driver.Requests.Should().BeEmpty();
        driver.Kinds.Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Suspended);
        driver.Last.History[^1].Code.Should().Be("process.timeout.instant_missing");
        driver.Last.History[^1].DataJson.Should().Be("""{"detail":"visit_at"}""");

        driver.Send(new ResumeRequested());

        driver.State.Problem.Should().Be(new ProcessProblem("process.timeout.instant_missing", "s", "visit_at"));
        driver.State.StepCount.Should().Be(2, "the resume entered the step again");
        driver.Kinds.Should().Equal(ProcessHistoryKind.Resumed, ProcessHistoryKind.Suspended);
        driver.Requests.Should().BeEmpty();
    }

    [Fact]
    public void AnEarlierStepThatLeavesTheVariableEmptyLeadsToTheSameSuspension()
    {
        var steps = """
            {
              "r": { "kind": "read", "port": "p.visit", "output": { "at": "visit_at" }, "next": "s" },
              "s": { "kind": "wait", "event": "case.ready", "next": "ok", "timeout": { "until": "visit_at" }, "onTimeout": "late" },
              "ok": { "kind": "end", "result": "ok" }, "late": { "kind": "end", "result": "late" }
            }
            """;
        var driver = new Driver(Docs.Load(Variables, steps, start: "r"));
        driver.Start("{}");

        driver.Send(new EffectCompleted("i-1/r/1/1", """{ "at": null }"""));

        driver.State.Problem.Should().Be(new ProcessProblem("process.timeout.instant_missing", "s", "visit_at"));
        driver.State.CurrentStep.Should().Be("s");
    }

    [Fact]
    public void ATaskRequestsItsTaskAndATimerAtTheInstant()
    {
        var driver = Started(Task);

        driver.Last.Requests.Should().HaveCount(2);
        driver.Last.Requests[0].Should().BeOfType<TaskRequest>().Which.TaskId.Should().Be("i-1/s/1/task");
        driver.Last.Requests[1].Should().Be(new TimerRequest("s", "i-1/s/1/timeout", Due));
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Task, "i-1/s/1/task", "i-1/s/1/timeout"));
    }

    [Fact]
    public void ACompletedTaskCancelsTheTimerAndTheTimerCancelsTheTask()
    {
        var done = Started(Task);
        done.Send(new TaskCompleted("i-1/s/1/task", "yes", "{}"));
        var expired = Started(Task);
        expired.Now = Due;
        expired.Send(new TimerFired("i-1/s/1/timeout"));

        done.Last.Requests.Should().Equal(new CancelTimerRequest("s", "i-1/s/1/timeout"));
        done.State.Result.Should().Be("ok");
        expired.Last.Requests.Should().Equal(new CancelTaskRequest("s", "i-1/s/1/task"));
        expired.State.Result.Should().Be("late");
        expired.Kinds.Should().Equal(ProcessHistoryKind.TimedOut, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Completed);
    }

    [Fact]
    public void ADueInstantCreatesNoTaskAndAMissingValueSuspendsBeforeTheTask()
    {
        var due = Started(Task, """{ "case_ref": "c-1", "visit_at": "2027-01-04T15:59:59Z" }""");
        var missing = Started(Task, """{ "case_ref": "c-1" }""");

        due.State.Result.Should().Be("late");
        due.Requests.Should().BeEmpty();
        due.Kinds.Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.TimedOut, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Completed);
        missing.State.Status.Should().Be(ProcessStatus.Suspended);
        missing.State.Problem.Should().Be(new ProcessProblem("process.timeout.instant_missing", "s", "visit_at"));
        missing.Requests.Should().BeEmpty();
    }

    [Fact]
    public void CancellingCancelsTheWaitAndTheTimerLikeADurationTimeout()
    {
        var driver = Started(Wait);

        driver.Send(new CancelRequested("operator"));

        driver.Last.Requests.Should().Equal(new CancelWaitRequest("s", "i-1/s/1/wait"), new CancelTimerRequest("s", "i-1/s/1/timeout"));
    }

    [Fact]
    public void ADurationTimeoutIsUnchanged()
    {
        var driver = Started(Wait.Replace("{ \"until\": \"visit_at\", \"offset\": \"-PT2H\" }", "\"PT2H\"", StringComparison.Ordinal), "{ \"case_ref\": \"c-1\" }");

        driver.Single<TimerRequest>().DueAt.Should().Be(Docs.T0.AddHours(2));
        driver.State.Pending!.TimeoutTimerId.Should().Be("i-1/s/1/timeout");
    }

    [Fact]
    public void TheInstantIsFixedWhenTheStepIsEnteredAndTheStateKeepsOnlyTheTimerId()
    {
        var driver = Started(Wait);

        var json = driver.State.ToJson();

        json.Should().NotContain("18:00", "the due time lives in the timer the host keeps, not in the state");
        json.Should().Contain("\"visit_at\":\"2027-01-04T20:00:00Z\"");
    }
}

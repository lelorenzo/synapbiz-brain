using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Sections 6.4 to 6.6: humanTask, timer, wait and events.</summary>
public sealed class WaitingTests
{
    private const string Variables = """
        {
          "case_ref": { "type": "Code", "input": true },
          "owner": { "type": "Reference", "input": true },
          "when": { "type": "Instant", "input": true },
          "note": { "type": "Code" },
          "count": { "type": "Integer" }
        }
        """;

    private static readonly string[] EndIds = ["ok", "late", "yes", "no"];

    private static Driver Started(string steps, string input = """{ "case_ref": "c-1" }""", string extra = "")
    {
        var ends = EndIds
            .Where(id => steps.Contains($": \"{id}\"", StringComparison.Ordinal))
            .Select(id => $$"""
                "{{id}}": { "kind": "end", "result": "{{id}}" }
                """);
        var all = "{ \"s\": " + steps + ", " + string.Join(", ", ends) + " }";
        var driver = new Driver(Docs.Load(Variables, all, extra));
        driver.Start(input);
        return driver;
    }

    // humanTask

    private const string Task = """
        { "kind": "humanTask", "queue": "front_desk", "outcomes": ["yes", "no"], "show": ["case_ref", "note", "count"],
          "output": { "note": "note", "n": "count" }, "on": { "yes": "yes", "no": "no" } }
        """;

    private const string TimedTask = """
        { "kind": "humanTask", "queue": "front_desk", "outcomes": ["yes", "no"], "timeout": "PT2H",
          "on": { "yes": "yes", "no": "no" }, "onTimeout": "late" }
        """;

    [Fact]
    public void ATaskRequestListsTheOutcomesAndTheShownVariablesThatHaveAValue()
    {
        var driver = Started(Task);

        var request = driver.Single<TaskRequest>();
        request.Step.Should().Be("s");
        request.TaskId.Should().Be("i-1/s/1/task");
        request.Queue.Should().Be("front_desk");
        request.Outcomes.Should().Equal("yes", "no");
        request.ShowJson.Should().Be("""{"case_ref":"c-1"}""");
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Task, "i-1/s/1/task"));
        driver.Kinds.Should().Contain(ProcessHistoryKind.TaskRequested);
    }

    [Fact]
    public void ACompletionAssignsTheDataAndFollowsTheOutcome()
    {
        var driver = Started(Task);

        driver.Send(new TaskCompleted("i-1/s/1/task", "no", """{ "note": "call back", "n": 2 }"""));

        driver.State.Result.Should().Be("no");
        driver.Variable("note").Should().Be("\"call back\"");
        driver.Variable("count").Should().Be("2");
        driver.Kinds.Should().Contain(ProcessHistoryKind.TaskCompleted);
        driver.Last.Requests.Should().BeEmpty();
    }

    [Fact]
    public void AnUnknownOutcomeIsRejectedAndTheTaskStaysOpen()
    {
        var driver = Started(Task);
        var before = driver.State;

        var rejected = driver.Send(new TaskCompleted("i-1/s/1/task", "maybe", "{}"));

        rejected.Disposition.Should().Be(AdvanceDisposition.Rejected);
        rejected.Code.Should().Be("process.task.outcome_unknown");
        rejected.State.Should().BeSameAs(before);
        driver.Send(new TaskCompleted("i-1/s/1/task", "yes", "{}")).Disposition.Should().Be(AdvanceDisposition.Applied);
    }

    [Theory]
    [InlineData("""{ "n": "two" }""")]
    [InlineData("""[1]""")]
    [InlineData("not json")]
    public void DataOfTheWrongTypeIsRejectedAndTheTaskCanBeCompletedAgain(string data)
    {
        var driver = Started(Task);
        var before = driver.State;

        var rejected = driver.Send(new TaskCompleted("i-1/s/1/task", "yes", data));

        rejected.Disposition.Should().Be(AdvanceDisposition.Rejected);
        rejected.Code.Should().Be("process.variable.type_mismatch");
        rejected.State.Should().BeSameAs(before);
        driver.Send(new TaskCompleted("i-1/s/1/task", "yes", """{ "n": 2 }""")).State.Result.Should().Be("yes");
    }

    [Fact]
    public void ATimeoutTimerIsRequestedWithTheTaskAndCancelledOnCompletion()
    {
        var driver = Started(TimedTask);

        driver.Last.Requests.OfType<TimerRequest>().Single().Should().Be(new TimerRequest("s", "i-1/s/1/timeout", Docs.T0.AddHours(2)));
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Task, "i-1/s/1/task", "i-1/s/1/timeout"));

        driver.Send(new TaskCompleted("i-1/s/1/task", "yes", "{}"));

        driver.Last.Requests.Should().Equal(new CancelTimerRequest("s", "i-1/s/1/timeout"));
        driver.State.Result.Should().Be("yes");
    }

    [Fact]
    public void ATaskTimeoutCancelsTheTaskAndGoesToOnTimeout()
    {
        var driver = Started(TimedTask);

        driver.Now = Docs.T0.AddHours(2);
        driver.Send(new TimerFired("i-1/s/1/timeout"));

        driver.Last.Requests.Should().Equal(new CancelTaskRequest("s", "i-1/s/1/task"));
        driver.State.Result.Should().Be("late");
        driver.Kinds.Should().Contain(ProcessHistoryKind.TimedOut);
        driver.Send(new TaskCompleted("i-1/s/1/task", "yes", "{}")).Code.Should().Be("process.trigger.stale");
    }

    [Fact]
    public void ATimerThatIsNotThePendingOneIsIgnoredWhileATaskIsOpen()
    {
        var driver = Started(TimedTask);

        driver.Send(new TimerFired("i-1/s/1/other")).Code.Should().Be("process.trigger.stale");
        driver.Send(new TimerFired("i-1/s/1/timer")).Code.Should().Be("process.trigger.stale");
    }

    // timer

    [Fact]
    public void AnAfterTimerFiresAtNowPlusTheDuration()
    {
        var driver = Started("""{ "kind": "timer", "after": "P1DT2H", "next": "ok" }""");

        driver.Single<TimerRequest>().Should().Be(new TimerRequest("s", "i-1/s/1/timer", Docs.T0.AddHours(26)));
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Timer, "i-1/s/1/timer"));
        driver.Kinds.Should().Contain(ProcessHistoryKind.TimerStarted);

        driver.Send(new TimerFired("i-1/s/1/timer"));

        driver.State.Result.Should().Be("ok");
        driver.Kinds.Should().Contain(ProcessHistoryKind.TimerFired);
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData(", \"offset\": \"PT30M\"", 40)]
    [InlineData(", \"offset\": \"-PT30M\"", -20)]
    public void AnUntilTimerUsesTheInstantVariableAndTheOffset(string offset, int minutes)
    {
        var steps = "{ \"kind\": \"timer\", \"until\": \"when\"" + offset + ", \"next\": \"ok\" }";
        var driver = Started(steps, """{ "case_ref": "c-1", "when": "2027-01-04T14:10:00Z" }""");

        driver.Single<TimerRequest>().DueAt.Should().Be(Docs.T0.AddMinutes(minutes));
    }

    [Fact]
    public void ATimerInThePastIsStillRequested()
    {
        var driver = Started("""{ "kind": "timer", "until": "when", "next": "ok" }""", """{ "case_ref": "c-1", "when": "2020-01-01T00:00:00Z" }""");

        driver.Single<TimerRequest>().DueAt.Should().BeBefore(Docs.T0);
        driver.State.Status.Should().Be(ProcessStatus.Waiting);
    }

    [Fact]
    public void AnUntilTimerWithoutTheInstantSuspends()
    {
        var driver = Started("""{ "kind": "timer", "until": "when", "next": "ok" }""");

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("process.timer.until_missing", "s"));
        driver.Last.Requests.Should().BeEmpty();
    }

    // wait

    private const string Wait = """
        { "kind": "wait", "event": "form_submitted", "correlation": "case_ref", "output": { "note": "note" }, "next": "ok",
          "timeout": "PT24H", "onTimeout": "late" }
        """;

    private const string Accepts = """ "accepts": ["form_submitted", "other_event"] """;

    [Fact]
    public void AWaitRequestsTheEventAndATimeoutTimer()
    {
        var driver = Started(Wait);

        driver.Last.Requests.OfType<WaitRequest>().Single().Should().Be(new WaitRequest("s", "i-1/s/1/wait", "form_submitted", "c-1"));
        driver.Last.Requests.OfType<TimerRequest>().Single().Should().Be(new TimerRequest("s", "i-1/s/1/timeout", Docs.T0.AddHours(24)));
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Wait, "i-1/s/1/wait", "i-1/s/1/timeout"));
        driver.Kinds.Should().Contain(ProcessHistoryKind.WaitStarted);
    }

    [Fact]
    public void AMatchingEventIsConsumedAndTheTimeoutCancelled()
    {
        var driver = Started(Wait);

        driver.Send(new EventReceived("form_submitted", "c-1", """{ "note": "done" }"""));

        driver.State.Result.Should().Be("ok");
        driver.Variable("note").Should().Be("\"done\"");
        driver.Last.Requests.Should().Equal(new CancelTimerRequest("s", "i-1/s/1/timeout"));
        driver.Kinds.Should().Contain(ProcessHistoryKind.EventConsumed);
    }

    [Fact]
    public void AWaitWithoutATimeoutRequestsNoTimer()
    {
        var driver = Started("""{ "kind": "wait", "event": "form_submitted", "next": "ok" }""", extra: Accepts);

        driver.Last.Requests.Should().ContainSingle().Which.Should().Be(new WaitRequest("s", "i-1/s/1/wait", "form_submitted", null));

        driver.Send(new EventReceived("form_submitted", "anything", "{}"));

        driver.State.Result.Should().Be("ok");
        driver.Last.Requests.Should().BeEmpty();
    }

    [Fact]
    public void AnEventWithAnotherCorrelationIsNotConsumedAndIsBufferedWhenAccepted()
    {
        var driver = Started(Wait, extra: Accepts);

        var result = driver.Send(new EventReceived("form_submitted", "c-2", """{ "note": "other" }"""));

        result.Disposition.Should().Be(AdvanceDisposition.Applied);
        driver.State.Status.Should().Be(ProcessStatus.Waiting);
        driver.State.Inbox.Should().Equal(new BufferedEvent("form_submitted", "c-2", """{"note":"other"}"""));
        driver.Kinds.Should().Equal(ProcessHistoryKind.EventBuffered);
        driver.Last.Requests.Should().BeEmpty();
    }

    [Fact]
    public void AnEventThatIsNeitherAwaitedNorAcceptedIsRejected()
    {
        var driver = Started(Wait);
        var before = driver.State;

        var result = driver.Send(new EventReceived("form_submitted", "c-2", "{}"));

        result.Disposition.Should().Be(AdvanceDisposition.Rejected);
        result.Code.Should().Be("process.event.not_accepted");
        result.State.Should().BeSameAs(before);
    }

    [Fact]
    public void AnEventWhileNothingWaitsForItIsBufferedWhenAccepted()
    {
        var driver = Started("""{ "kind": "timer", "after": "PT1H", "next": "ok" }""", extra: Accepts);

        var result = driver.Send(new EventReceived("other_event", null, """{ "b": 1, "a": 2 }"""));

        result.Disposition.Should().Be(AdvanceDisposition.Applied);
        driver.State.Inbox.Should().Equal(new BufferedEvent("other_event", null, """{"a":2,"b":1}"""));
    }

    [Fact]
    public void ABufferedEventIsConsumedAtOnceOnEnteringAWait()
    {
        var steps = """
            { "kind": "timer", "after": "PT1H", "next": "w" },
            "w": { "kind": "wait", "event": "form_submitted", "correlation": "case_ref", "output": { "note": "note" }, "timeout": "PT1H", "next": "ok", "onTimeout": "late" }
            """;
        var driver = Started(steps, extra: Accepts);
        driver.Send(new EventReceived("form_submitted", "c-9", """{ "note": "wrong" }"""));
        driver.Send(new EventReceived("form_submitted", "c-1", """{ "note": "first" }"""));
        driver.Send(new EventReceived("form_submitted", "c-1", """{ "note": "second" }"""));
        driver.State.Inbox.Should().HaveCount(3);

        driver.Send(new TimerFired("i-1/s/1/timer"));

        driver.State.Result.Should().Be("ok");
        driver.Variable("note").Should().Be("\"first\"");
        driver.State.Inbox.Select(buffered => buffered.PayloadJson).Should().Equal("""{"note":"wrong"}""", """{"note":"second"}""");
        driver.Last.Requests.OfType<WaitRequest>().Should().BeEmpty();
        driver.Last.Requests.OfType<TimerRequest>().Should().BeEmpty();
        driver.Kinds.Should().Contain(ProcessHistoryKind.EventConsumed);
    }

    [Fact]
    public void ABufferedEventWithABadPayloadSuspendsOnEntering()
    {
        var steps = """
            { "kind": "timer", "after": "PT1H", "next": "w" },
            "w": { "kind": "wait", "event": "form_submitted", "output": { "n": "count" }, "next": "ok" }
            """;
        var driver = Started(steps, extra: Accepts);
        driver.Send(new EventReceived("form_submitted", null, """{ "n": "x" }"""));

        driver.Send(new TimerFired("i-1/s/1/timer"));

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("process.variable.type_mismatch", "w"));
    }

    [Fact]
    public void AnAwaitedEventWithAWrongPayloadSuspendsTheInstance()
    {
        var driver = Started("""{ "kind": "wait", "event": "form_submitted", "output": { "n": "count" }, "timeout": "PT1H", "next": "ok", "onTimeout": "late" }""");

        driver.Send(new EventReceived("form_submitted", null, """{ "n": "x" }"""));

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem!.Code.Should().Be("process.variable.type_mismatch");
        driver.Last.Requests.Should().Equal(new CancelWaitRequest("s", "i-1/s/1/wait"), new CancelTimerRequest("s", "i-1/s/1/timeout"));
    }

    [Fact]
    public void AnAwaitedEventWithAnUnparseablePayloadSuspendsTheInstance()
    {
        var driver = Started("""{ "kind": "wait", "event": "form_submitted", "next": "ok" }""");

        driver.Send(new EventReceived("form_submitted", null, "not json"));

        driver.State.Problem!.Code.Should().Be("process.variable.type_mismatch");
    }

    [Fact]
    public void AnAcceptedEventWithAnUnparseablePayloadIsRejected()
    {
        var driver = Started("""{ "kind": "timer", "after": "PT1H", "next": "ok" }""", extra: Accepts);

        var result = driver.Send(new EventReceived("other_event", null, "[1]"));

        result.Disposition.Should().Be(AdvanceDisposition.Rejected);
        result.Code.Should().Be("process.variable.type_mismatch");
        driver.State.Inbox.Should().BeEmpty();
    }

    [Fact]
    public void ABufferedEventSurvivesASuspension()
    {
        var driver = Started("""{ "kind": "read", "port": "p", "next": "ok" }""", extra: Accepts);
        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Permanent, "x"));
        driver.State.Status.Should().Be(ProcessStatus.Suspended);

        var result = driver.Send(new EventReceived("other_event", null, "{}"));

        result.Disposition.Should().Be(AdvanceDisposition.Applied);
        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Inbox.Should().HaveCount(1);
    }

    [Fact]
    public void AWaitTimeoutCancelsTheWaitAndGoesToOnTimeout()
    {
        var driver = Started(Wait);

        driver.Now = Docs.T0.AddHours(24);
        driver.Send(new TimerFired("i-1/s/1/timeout"));

        driver.Last.Requests.Should().Equal(new CancelWaitRequest("s", "i-1/s/1/wait"));
        driver.State.Result.Should().Be("late");
        driver.Kinds.Should().Contain(ProcessHistoryKind.TimedOut);
    }

    [Fact]
    public void ADeclaredCorrelationWithoutAValueSuspends()
    {
        var driver = Started(Wait, input: "{}");

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("process.wait.correlation_missing", "s"));
        driver.Last.Requests.Should().BeEmpty();
    }

    [Fact]
    public void AReferenceCorrelationIsComparedAsText()
    {
        var steps = """{ "kind": "wait", "event": "form_submitted", "correlation": "owner", "next": "ok" }""";
        var driver = Started(steps, """{ "owner": "00000000-0000-0000-0000-00000000000A" }""");

        driver.Single<WaitRequest>().Correlation.Should().Be("00000000-0000-0000-0000-00000000000a");
        driver.Send(new EventReceived("form_submitted", "00000000-0000-0000-0000-00000000000A", "{}")).State.Result.Should().Be("ok");
    }
}

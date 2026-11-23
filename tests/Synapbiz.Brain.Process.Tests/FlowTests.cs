using System.Text.Json.Nodes;
using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Sections 5 to 6.9: loops, subprocesses, limits, cancel and resume.</summary>
public sealed class FlowTests
{
    private const string Variables = """
        {
          "items": { "type": "Json", "input": true },
          "item": { "type": "Json" },
          "case_ref": { "type": "Code", "input": true },
          "count": { "type": "Integer" },
          "note": { "type": "Code" }
        }
        """;

    private static Driver Started(string steps, string input = "{}", string extra = "", string uses = "[]")
    {
        var driver = new Driver(Docs.Load(Variables, steps, extra, uses));
        driver.Start(input);
        return driver;
    }

    // loop

    private const string PlainLoop = """
        {
          "s": { "kind": "loop", "maxIterations": 2, "body": "b", "onExhausted": "late" },
          "b": { "kind": "timer", "after": "PT1M", "next": "s" },
          "late": { "kind": "end", "result": "late" }
        }
        """;

    [Fact]
    public void ALoopRunsItsBodyUpToMaxIterationsThenGoesToOnExhausted()
    {
        var driver = Started(PlainLoop);

        driver.State.CurrentStep.Should().Be("b");
        driver.State.LoopCounters["s"].Should().Be(1);
        driver.Send(new TimerFired("i-1/b/2/timer"));
        driver.State.LoopCounters["s"].Should().Be(2);
        driver.Send(new TimerFired("i-1/b/4/timer"));

        driver.State.Result.Should().Be("late");
        driver.State.LoopCounters["s"].Should().Be(3);
        driver.History.Count(entry => entry.Kind == ProcessHistoryKind.LoopIteration).Should().Be(2);
        driver.History.Single(entry => entry.Kind == ProcessHistoryKind.LoopExhausted).Step.Should().Be("s");
        driver.History.Where(entry => entry.Kind == ProcessHistoryKind.LoopIteration).Select(entry => entry.DataJson)
            .Should().Equal("""{"iteration":1}""", """{"iteration":2}""");
    }

    private const string ForEach = """
        {
          "s": { "kind": "loop", "maxIterations": 2, "forEach": "items", "item": "item", "body": "b", "onExhausted": "late", "next": "ok" },
          "b": { "kind": "timer", "after": "PT1M", "next": "s" },
          "ok": { "kind": "end", "result": "ok" },
          "late": { "kind": "end", "result": "late" }
        }
        """;

    [Fact]
    public void ForEachSetsTheItemAndCompletesAfterTheLastElement()
    {
        var driver = Started(ForEach, """{ "items": [{ "id": 1 }, "two"] }""");

        driver.Variable("item").Should().Be("""{"id":1}""");
        driver.Send(new TimerFired("i-1/b/2/timer"));
        driver.Variable("item").Should().Be("\"two\"");
        driver.Send(new TimerFired("i-1/b/4/timer"));

        driver.State.Result.Should().Be("ok");
        driver.Kinds.Should().NotContain(ProcessHistoryKind.LoopExhausted);
    }

    [Fact]
    public void TheForEachCompletionIsCheckedBeforeMaxIterations()
    {
        var shorter = Started(ForEach.Replace("\"maxIterations\": 2", "\"maxIterations\": 1", StringComparison.Ordinal), """{ "items": [1] }""");
        shorter.Send(new TimerFired("i-1/b/2/timer"));
        shorter.State.Result.Should().Be("ok");

        var longer = Started(ForEach, """{ "items": [1, 2, 3] }""");
        longer.Send(new TimerFired("i-1/b/2/timer"));
        longer.Send(new TimerFired("i-1/b/4/timer"));
        longer.State.Result.Should().Be("late");
    }

    [Fact]
    public void AnEmptyListGoesStraightToNext()
    {
        var driver = Started(ForEach, """{ "items": [] }""");

        driver.State.Result.Should().Be("ok");
        driver.State.Variables.Should().NotContainKey("item");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "items": { "a": 1 } }""")]
    [InlineData("""{ "items": "text" }""")]
    [InlineData("""{ "items": null }""")]
    public void AForEachWithoutAnArraySuspends(string input)
    {
        var driver = Started(ForEach, input);

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("process.loop.not_a_list", "s"));
    }

    [Fact]
    public void ResumingALoopCountsOneMoreArrival()
    {
        var driver = Started(ForEach, "{}");
        driver.State.LoopCounters["s"].Should().Be(1);

        driver.Send(new ResumeRequested());

        driver.State.LoopCounters["s"].Should().Be(2);
        driver.State.Status.Should().Be(ProcessStatus.Suspended);
    }

    // subprocess

    private const string Child = """
        {
          "s": { "kind": "subprocess", "process": "child.proc", "input": { "case": "case_ref" }, "output": { "n": "count" },
                 "on": { "done": "ok", "partial": "late" }, "onError": "bad" },
          "ok": { "kind": "end", "result": "ok" },
          "late": { "kind": "end", "result": "late" },
          "bad": { "kind": "end", "result": "bad" }
        }
        """;

    private const string ChildUses = """[{ "$ref": "process/child.proc" }]""";

    [Fact]
    public void ASubprocessStartsAChildAndRoutesByItsResult()
    {
        var driver = Started(Child, """{ "case_ref": "c-1" }""", uses: ChildUses);

        driver.Single<ChildStartRequest>().Should().Be(new ChildStartRequest("s", "i-1/s/1/child", "child.proc", """{"case":"c-1"}"""));
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Child, "i-1/s/1/child"));
        driver.Kinds.Should().Contain(ProcessHistoryKind.ChildStarted);

        driver.Send(new ChildCompleted("i-1/s/1/child", "partial", """{ "n": 3 }"""));

        driver.State.Result.Should().Be("late");
        driver.Variable("count").Should().Be("3");
        driver.Kinds.Should().Contain(ProcessHistoryKind.ChildCompleted);
    }

    [Fact]
    public void AnUnlistedChildResultGoesToOnErrorOrSuspends()
    {
        var withError = Started(Child, uses: ChildUses);
        withError.Send(new ChildCompleted("i-1/s/1/child", "other", "{}"));
        withError.State.Result.Should().Be("bad");

        var steps = Child.Replace(", \"onError\": \"bad\"", "", StringComparison.Ordinal).Replace("\"bad\": { \"kind\": \"end\", \"result\": \"bad\" }", "\"x\": { \"kind\": \"end\", \"result\": \"x\" }", StringComparison.Ordinal).Replace("\"partial\": \"late\"", "\"partial\": \"late\", \"x\": \"x\"", StringComparison.Ordinal);
        var without = Started(steps, uses: ChildUses);
        without.Send(new ChildCompleted("i-1/s/1/child", "other", "{}"));
        without.State.Problem.Should().Be(new ProcessProblem("process.route.unmatched", "s"));
    }

    [Fact]
    public void ASubprocessWithNextIgnoresTheResultName()
    {
        var steps = """
            {
              "s": { "kind": "subprocess", "process": "child.proc", "next": "ok" },
              "ok": { "kind": "end", "result": "ok" }
            }
            """;
        var driver = Started(steps, uses: ChildUses);

        driver.Single<ChildStartRequest>().InputJson.Should().Be("{}");
        driver.Send(new ChildCompleted("i-1/s/1/child", "anything", "{}"));

        driver.State.Result.Should().Be("ok");
    }

    [Fact]
    public void AChildFailureGoesToOnErrorOrSuspendsWithItsCode()
    {
        var withError = Started(Child, uses: ChildUses);
        withError.Send(new ChildFailed("i-1/s/1/child", "child.broken"));
        withError.State.Result.Should().Be("bad");
        withError.Last.History[0].Should().Match<ProcessHistoryEntry>(entry => entry.Kind == ProcessHistoryKind.ChildFailed && entry.Code == "child.broken");

        var steps = """
            {
              "s": { "kind": "subprocess", "process": "child.proc", "next": "ok" },
              "ok": { "kind": "end", "result": "ok" }
            }
            """;
        var without = Started(steps, uses: ChildUses);
        without.Send(new ChildFailed("i-1/s/1/child", "child.broken"));
        without.State.Problem.Should().Be(new ProcessProblem("child.broken", "s"));
    }

    [Theory]
    [InlineData("""{ "n": "x" }""")]
    [InlineData("""[1]""")]
    [InlineData("not json")]
    public void AChildOutputOfTheWrongTypeIsAChildFailure(string output)
    {
        var driver = Started(Child, uses: ChildUses);

        driver.Send(new ChildCompleted("i-1/s/1/child", "done", output));

        driver.State.Result.Should().Be("bad");
        driver.Last.History[0].Should().Match<ProcessHistoryEntry>(entry => entry.Kind == ProcessHistoryKind.ChildFailed && entry.Code == "process.variable.type_mismatch");
    }

    [Fact]
    public void AChildResultForAnotherChildIsIgnored()
    {
        var driver = Started(Child, uses: ChildUses);

        driver.Send(new ChildCompleted("i-1/s/2/child", "done", "{}")).Code.Should().Be("process.trigger.stale");
        driver.Send(new ChildFailed("i-1/s/2/child", "x")).Code.Should().Be("process.trigger.stale");
    }

    // limits

    private const string Spinner = """
        {
          "s": { "kind": "loop", "maxIterations": 50, "body": "b", "onExhausted": "late" },
          "b": { "kind": "timer", "after": "PT1M", "next": "s" },
          "late": { "kind": "end", "result": "late" }
        }
        """;

    [Fact]
    public void TheStepLimitSuspendsTheInstanceOnTheStepBefore()
    {
        var driver = Started(Spinner, extra: """ "limits": { "maxSteps": 4 } """);
        driver.Send(new TimerFired("i-1/b/2/timer"));
        driver.State.StepCount.Should().Be(4);

        driver.Send(new TimerFired("i-1/b/4/timer"));

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("process.limits.max_steps", "b"));
        driver.State.CurrentStep.Should().Be("b");
        driver.State.StepCount.Should().Be(5);
        driver.Kinds.Should().Contain(ProcessHistoryKind.Suspended);
    }

    [Fact]
    public void TheDefaultStepLimitIsTenThousand()
    {
        var driver = Started(Spinner.Replace("50", "10000", StringComparison.Ordinal));
        for (var round = 0; round < 6000 && driver.State.Status == ProcessStatus.Waiting; round++)
        {
            driver.Send(new TimerFired(driver.State.Pending!.Id));
        }

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem!.Code.Should().Be("process.limits.max_steps");
        driver.State.StepCount.Should().Be(10001);
    }

    // cancel

    [Fact]
    public void CancellingWhileAnEffectIsOutstandingEmitsNothing()
    {
        var driver = Started("""{ "s": { "kind": "read", "port": "p", "next": "e" }, "e": { "kind": "end", "result": "ok" } }""");

        var result = driver.Send(new CancelRequested("no longer needed"));

        result.Requests.Should().BeEmpty();
        AssertCancelled(driver, "no longer needed");
        driver.Send(new EffectCompleted("i-1/s/1/1", "{}")).Code.Should().Be("process.trigger.stale");
    }

    [Fact]
    public void CancellingWhileARetryDelayIsOutstandingCancelsTheTimer()
    {
        var steps = """{ "s": { "kind": "read", "port": "p", "retry": { "maxAttempts": 2, "backoff": "fixed", "initial": "PT1M" }, "next": "e" }, "e": { "kind": "end", "result": "ok" } }""";
        var driver = Started(steps);
        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Transient, "busy"));

        driver.Send(new CancelRequested("stop"));

        driver.Last.Requests.Should().Equal(new CancelTimerRequest("s", "i-1/s/1/retry/1"));
        AssertCancelled(driver, "stop");
    }

    [Fact]
    public void CancellingWhileATimerIsOutstandingCancelsTheTimer()
    {
        var driver = Started("""{ "s": { "kind": "timer", "after": "PT1H", "next": "e" }, "e": { "kind": "end", "result": "ok" } }""");

        driver.Send(new CancelRequested("stop"));

        driver.Last.Requests.Should().Equal(new CancelTimerRequest("s", "i-1/s/1/timer"));
        AssertCancelled(driver, "stop");
    }

    [Fact]
    public void CancellingWhileAWaitIsOutstandingCancelsTheWaitAndItsTimeout()
    {
        var steps = """{ "s": { "kind": "wait", "event": "x", "timeout": "PT1H", "next": "e", "onTimeout": "e" }, "e": { "kind": "end", "result": "ok" } }""";
        var driver = Started(steps);

        driver.Send(new CancelRequested("stop"));

        driver.Last.Requests.Should().Equal(new CancelWaitRequest("s", "i-1/s/1/wait"), new CancelTimerRequest("s", "i-1/s/1/timeout"));
        AssertCancelled(driver, "stop");
    }

    [Fact]
    public void CancellingWhileATaskIsOutstandingCancelsTheTaskAndItsTimeout()
    {
        var steps = """{ "s": { "kind": "humanTask", "queue": "q", "outcomes": ["a"], "timeout": "PT1H", "on": { "a": "e" }, "onTimeout": "e" }, "e": { "kind": "end", "result": "ok" } }""";
        var driver = Started(steps);

        driver.Send(new CancelRequested("stop"));

        driver.Last.Requests.Should().Equal(new CancelTaskRequest("s", "i-1/s/1/task"), new CancelTimerRequest("s", "i-1/s/1/timeout"));
        AssertCancelled(driver, "stop");
    }

    [Fact]
    public void CancellingWhileAChildIsOutstandingEmitsNothing()
    {
        var driver = Started(Child, uses: ChildUses);

        driver.Send(new CancelRequested("stop"));

        driver.Last.Requests.Should().BeEmpty();
        AssertCancelled(driver, "stop");
        driver.Send(new ChildCompleted("i-1/s/1/child", "done", "{}")).Code.Should().Be("process.trigger.stale");
    }

    [Fact]
    public void CancellingASuspendedInstanceClearsTheProblem()
    {
        var driver = Started("""{ "s": { "kind": "read", "port": "p", "next": "e" }, "e": { "kind": "end", "result": "ok" } }""");
        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Permanent, "x"));

        driver.Send(new CancelRequested("give up"));

        driver.Last.Requests.Should().BeEmpty();
        driver.State.Problem.Should().BeNull();
        AssertCancelled(driver, "give up");
    }

    private static void AssertCancelled(Driver driver, string reason)
    {
        driver.State.Status.Should().Be(ProcessStatus.Cancelled);
        driver.State.CurrentStep.Should().BeNull();
        driver.State.Pending.Should().BeNull();
        var entry = driver.Last.History.Single();
        entry.Kind.Should().Be(ProcessHistoryKind.Cancelled);
        JsonNode.Parse(entry.DataJson!)!["reason"]!.GetValue<string>().Should().Be(reason);
    }

    // resume

    [Fact]
    public void ResumeRunsTheCurrentStepAgainWithFreshIdentifiersAndNoStepEntered()
    {
        var steps = """{ "s": { "kind": "read", "port": "p", "next": "e" }, "e": { "kind": "end", "result": "ok" } }""";
        var driver = Started(steps);
        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Permanent, "x"));
        var suspended = driver.State;

        var result = driver.Send(new ResumeRequested());

        result.Disposition.Should().Be(AdvanceDisposition.Applied);
        driver.Kinds.Should().Equal(ProcessHistoryKind.Resumed, ProcessHistoryKind.EffectRequested);
        driver.State.Status.Should().Be(ProcessStatus.Waiting);
        driver.State.Problem.Should().BeNull();
        driver.State.Entry.Should().Be(suspended.StepCount + 1);
        driver.State.StepCount.Should().Be(suspended.StepCount + 1);
        driver.State.Attempt.Should().Be(1);
        driver.Single<EffectRequest>().EffectId.Should().Be("i-1/s/2/1");

        driver.Send(new EffectCompleted("i-1/s/2/1", "{}")).State.Result.Should().Be("ok");
    }

    [Fact]
    public void ResumeThatSuspendsAgainStaysSuspended()
    {
        var steps = """
            {
              "s": { "kind": "timer", "until": "when", "next": "e" },
              "e": { "kind": "end", "result": "ok" }
            }
            """;
        var driver = new Driver(Docs.Load("""{ "when": { "type": "Instant", "input": true } }""", steps));
        driver.Start();
        driver.State.Problem!.Code.Should().Be("process.timer.until_missing");

        driver.Send(new ResumeRequested());

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem!.Code.Should().Be("process.timer.until_missing");
        driver.Kinds.Should().Equal(ProcessHistoryKind.Resumed, ProcessHistoryKind.Suspended);
    }

    [Fact]
    public void ResumingAfterTheStepLimitReRunsThePreviousStepAndMayCompleteTheInstance()
    {
        var steps = """
            {
              "s": { "kind": "timer", "after": "PT1M", "next": "n" },
              "n": { "kind": "end", "result": "ok" }
            }
            """;
        var driver = Started(steps, extra: """ "limits": { "maxSteps": 1 } """);
        driver.Send(new TimerFired("i-1/s/1/timer"));
        driver.State.Problem.Should().Be(new ProcessProblem("process.limits.max_steps", "s"));

        driver.Send(new ResumeRequested());

        // The resume does not check the limit and runs the step that was current when the limit was hit: here the timer again.
        driver.State.Status.Should().Be(ProcessStatus.Waiting);
        driver.Single<TimerRequest>().TimerId.Should().Be("i-1/s/3/timer");
    }

    [Fact]
    public void AnEndStepClearsThePendingWorkAndTheCurrentStep()
    {
        var driver = Started("""{ "s": { "kind": "end", "result": "done" } }""");

        driver.State.CurrentStep.Should().BeNull();
        driver.State.Pending.Should().BeNull();
        driver.State.Result.Should().Be("done");
        driver.Last.History[^1].DataJson.Should().Be("""{"result":"done"}""");
    }
}

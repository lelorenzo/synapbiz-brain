using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Sections 6.1 and 6.2: read, action and ai steps.</summary>
public sealed class EffectTests
{
    private const string Variables = """
        {
          "case_ref": { "type": "Code", "input": true },
          "count": { "type": "Integer" },
          "note": { "type": "Code" },
          "answer": { "type": "Code" }
        }
        """;

    private static string Steps(string retry = "") => $$"""
        {
          "s": { "kind": "read", "port": "intake.status", "input": { "case": "case_ref" }, "output": { "n": "count", "t": "note" }, {{(retry.Length == 0 ? "" : "\"retry\": " + retry + ",")}} "next": "ok", "onError": "fail", "onNotAvailable": "na", "onNotEntitled": "ne" },
          "ok": { "kind": "end", "result": "ok" },
          "fail": { "kind": "end", "result": "fail" },
          "na": { "kind": "end", "result": "na" },
          "ne": { "kind": "end", "result": "ne" }
        }
        """;

    private static Driver Started(string steps)
    {
        var driver = new Driver(Docs.Load(Variables, steps));
        driver.Start("""{ "case_ref": "c-1" }""");
        return driver;
    }

    [Fact]
    public void ARequestCarriesTheInputsAndTheDeterministicIdentifiers()
    {
        var driver = Started(Steps());

        var request = driver.Single<EffectRequest>();

        request.Should().Be(new EffectRequest("s", "i-1/s/1/1", "i-1/s/1", ProcessEffectKind.Read, "intake.status", """{"case":"c-1"}""", 1, ProcessDelivery.AtLeastOnce));
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Effect, "i-1/s/1/1"));
        driver.Kinds.Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.EffectRequested);
    }

    [Fact]
    public void AVariableWithoutAValueIsLeftOutOfTheInput()
    {
        var driver = new Driver(Docs.Load(Variables, Steps()));
        driver.Start("{}");

        driver.Single<EffectRequest>().InputJson.Should().Be("{}");
    }

    [Fact]
    public void ACompletionAssignsTheOutputAndMovesOn()
    {
        var driver = Started(Steps());

        driver.Send(new EffectCompleted("i-1/s/1/1", """{ "n": 4, "t": "x", "extra": 1 }"""));

        driver.State.Status.Should().Be(ProcessStatus.Completed);
        driver.State.Result.Should().Be("ok");
        driver.Variable("count").Should().Be("4");
        driver.Variable("note").Should().Be("\"x\"");
        driver.Kinds.Should().Equal(ProcessHistoryKind.EffectCompleted, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Completed);
    }

    [Fact]
    public void AMissingOrNullMemberClearsTheVariable()
    {
        var steps = """
            {
              "s": { "kind": "read", "port": "p", "output": { "n": "count", "t": "note" }, "next": "s2" },
              "s2": { "kind": "read", "port": "q", "output": { "n": "count", "t": "note" }, "next": "ok" },
              "ok": { "kind": "end", "result": "ok" }
            }
            """;
        var driver = Started(steps);
        driver.Send(new EffectCompleted("i-1/s/1/1", """{ "n": 4, "t": "x" }"""));
        driver.Variable("note").Should().Be("\"x\"");

        driver.Send(new EffectCompleted("i-1/s2/2/1", """{ "n": null }"""));

        driver.State.Variables.Should().NotContainKey("count");
        driver.State.Variables.Should().NotContainKey("note");
        driver.State.Result.Should().Be("ok");
    }

    [Theory]
    [InlineData("""[1]""")]
    [InlineData("""not json""")]
    [InlineData("""null""")]
    [InlineData("")]
    public void AnOutputThatIsNotAnObjectIsAPermanentFailure(string output)
    {
        var driver = Started(Steps());

        driver.Send(new EffectCompleted("i-1/s/1/1", output));

        driver.State.Result.Should().Be("fail");
        driver.History.Should().Contain(entry => entry.Kind == ProcessHistoryKind.EffectFailed && entry.Code == "process.effect.output_malformed");
    }

    [Fact]
    public void ATypeMismatchIsAPermanentFailureAndAssignsNothing()
    {
        var driver = Started(Steps());

        driver.Send(new EffectCompleted("i-1/s/1/1", """{ "n": "four", "t": "x" }"""));

        driver.State.Result.Should().Be("fail");
        driver.State.Variables.Should().NotContainKey("note");
        driver.History.Should().Contain(entry => entry.Kind == ProcessHistoryKind.EffectFailed && entry.Code == "process.variable.type_mismatch");
    }

    [Fact]
    public void AFailureWithoutAnyRouteSuspendsWithItsCode()
    {
        var driver = Started("""{ "s": { "kind": "read", "port": "p", "next": "ok" }, "ok": { "kind": "end", "result": "ok" } }""");

        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Permanent, "port.broken"));

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("port.broken", "s"));
        driver.State.Pending.Should().BeNull();
        driver.State.CurrentStep.Should().Be("s");
        driver.Kinds.Should().Equal(ProcessHistoryKind.EffectFailed, ProcessHistoryKind.Suspended);
        driver.Last.History[1].Code.Should().Be("port.broken");
    }

    [Theory]
    [InlineData(ProcessErrorKind.NotAvailable, "na")]
    [InlineData(ProcessErrorKind.NotEntitled, "ne")]
    [InlineData(ProcessErrorKind.Permanent, "fail")]
    [InlineData(ProcessErrorKind.Transient, "fail")]
    [InlineData(ProcessErrorKind.RateLimited, "fail")]
    [InlineData(ProcessErrorKind.Unknown, "fail")]
    public void EachKindFollowsItsRouteOrOnError(ProcessErrorKind kind, string result)
    {
        var driver = Started(Steps());

        driver.Send(new EffectFailed("i-1/s/1/1", kind, "port.failed"));

        driver.State.Result.Should().Be(result);
        driver.Last.History[0].Kind.Should().Be(ProcessHistoryKind.EffectFailed);
        driver.Last.History[0].Code.Should().Be("port.failed");
        driver.Last.History[0].DataJson.Should().Be("{\"kind\":\"" + kind + "\"}");
    }

    [Fact]
    public void NotAvailableWithoutItsRouteFallsBackToOnError()
    {
        var driver = Started("""{ "s": { "kind": "read", "port": "p", "next": "ok", "onError": "fail" }, "ok": { "kind": "end", "result": "ok" }, "fail": { "kind": "end", "result": "fail" } }""");

        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.NotAvailable, "x"));

        driver.State.Result.Should().Be("fail");
    }

    [Fact]
    public void FixedBackoffRetriesWithTheSameDelayUntilTheAttemptsRunOut()
    {
        var driver = Started(Steps("""{ "maxAttempts": 3, "backoff": "fixed", "initial": "PT1M" }"""));

        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Transient, "busy"));
        driver.Single<TimerRequest>().Should().Be(new TimerRequest("s", "i-1/s/1/retry/1", Docs.T0.AddMinutes(1)));
        driver.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.RetryDelay, "i-1/s/1/retry/1"));
        driver.Kinds.Should().Equal(ProcessHistoryKind.EffectFailed, ProcessHistoryKind.RetryScheduled);

        driver.Now = Docs.T0.AddMinutes(1);
        driver.Send(new TimerFired("i-1/s/1/retry/1"));
        driver.Single<EffectRequest>().Should().Be(new EffectRequest("s", "i-1/s/1/2", "i-1/s/1", ProcessEffectKind.Read, "intake.status", """{"case":"c-1"}""", 2, ProcessDelivery.AtLeastOnce));
        driver.State.Attempt.Should().Be(2);

        driver.Send(new EffectFailed("i-1/s/1/2", ProcessErrorKind.RateLimited, "busy"));
        driver.Single<TimerRequest>().DueAt.Should().Be(Docs.T0.AddMinutes(2));
        driver.Now = Docs.T0.AddMinutes(2);
        driver.Send(new TimerFired("i-1/s/1/retry/2"));
        driver.State.Attempt.Should().Be(3);

        driver.Send(new EffectFailed("i-1/s/1/3", ProcessErrorKind.Transient, "busy"));
        driver.State.Result.Should().Be("fail");
    }

    [Fact]
    public void ExponentialBackoffDoublesAndIsCappedByMax()
    {
        var driver = Started(Steps("""{ "maxAttempts": 5, "backoff": "exponential", "initial": "PT1M", "max": "PT3M" }"""));
        var delays = new List<TimeSpan>();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            driver.Send(new EffectFailed($"i-1/s/1/{attempt}", ProcessErrorKind.Transient, "busy"));
            var timer = driver.Single<TimerRequest>();
            delays.Add(timer.DueAt - driver.Now);
            timer.TimerId.Should().Be($"i-1/s/1/retry/{attempt}");
            driver.Send(new TimerFired(timer.TimerId));
        }

        delays.Should().Equal(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ExponentialBackoffWithoutACapKeepsDoubling()
    {
        var driver = Started(Steps("""{ "maxAttempts": 4, "backoff": "exponential", "initial": "PT10S" }"""));
        var delays = new List<TimeSpan>();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            driver.Send(new EffectFailed($"i-1/s/1/{attempt}", ProcessErrorKind.Transient, "busy"));
            var timer = driver.Single<TimerRequest>();
            delays.Add(timer.DueAt - driver.Now);
            driver.Send(new TimerFired(timer.TimerId));
        }

        delays.Should().Equal(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40));
    }

    [Theory]
    [InlineData(ProcessErrorKind.RateLimited, 90, 90)]
    [InlineData(ProcessErrorKind.RateLimited, 30, 60)]
    [InlineData(ProcessErrorKind.Transient, 90, 60)]
    public void RetryAfterRaisesTheDelayOnlyForRateLimited(ProcessErrorKind kind, int retryAfterSeconds, int expectedSeconds)
    {
        var driver = Started(Steps("""{ "maxAttempts": 2, "backoff": "fixed", "initial": "PT1M" }"""));

        driver.Send(new EffectFailed("i-1/s/1/1", kind, "busy", TimeSpan.FromSeconds(retryAfterSeconds)));

        driver.Single<TimerRequest>().DueAt.Should().Be(Docs.T0.AddSeconds(expectedSeconds));
    }

    [Fact]
    public void WithoutARetryPolicyTheFirstFailureIsFinal()
    {
        var driver = Started(Steps());

        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Transient, "busy"));

        driver.State.Result.Should().Be("fail");
        driver.Last.Requests.Should().BeEmpty();
    }

    [Fact]
    public void ARetryTimerFiredTwiceIsIgnoredTheSecondTime()
    {
        var driver = Started(Steps("""{ "maxAttempts": 2, "backoff": "fixed", "initial": "PT1M" }"""));
        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Transient, "busy"));
        driver.Send(new TimerFired("i-1/s/1/retry/1"));

        driver.Send(new TimerFired("i-1/s/1/retry/1")).Code.Should().Be("process.trigger.stale");
    }

    [Fact]
    public void AnAtMostOnceActionIsMarkedAndNeverRetriedAfterUnknown()
    {
        var steps = """
            {
              "s": { "kind": "action", "port": "messaging.notice", "delivery": "atMostOnce", "retry": { "maxAttempts": 3, "backoff": "fixed", "initial": "PT1M" },
                     "next": "ok", "onUnknown": "unsure", "onError": "fail" },
              "ok": { "kind": "end", "result": "ok" },
              "unsure": { "kind": "end", "result": "unsure" },
              "fail": { "kind": "end", "result": "fail" }
            }
            """;
        var driver = Started(steps);

        driver.Single<EffectRequest>().Should().Match<EffectRequest>(request => request.EffectKind == ProcessEffectKind.Action && request.Delivery == ProcessDelivery.AtMostOnce);

        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Unknown, "send.lost"));

        driver.State.Result.Should().Be("unsure");
        driver.Last.Requests.Should().BeEmpty();
    }

    [Fact]
    public void AnAtLeastOnceActionRetriesTransientFailures()
    {
        var steps = """
            {
              "s": { "kind": "action", "port": "messaging.notice", "delivery": "atLeastOnce", "retry": { "maxAttempts": 2, "backoff": "fixed", "initial": "PT1M" }, "next": "ok" },
              "ok": { "kind": "end", "result": "ok" }
            }
            """;
        var driver = Started(steps);

        driver.Single<EffectRequest>().Delivery.Should().Be(ProcessDelivery.AtLeastOnce);
        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Transient, "busy"));
        driver.State.Pending!.Kind.Should().Be(ProcessPendingKind.RetryDelay);

        driver.Send(new TimerFired("i-1/s/1/retry/1"));
        driver.Send(new EffectCompleted("i-1/s/1/2", "{}"));

        driver.State.Result.Should().Be("ok");
    }

    [Fact]
    public void ARetriedEntryKeepsItsIdempotencyKeyAndAResumeGetsAFreshOne()
    {
        var steps = """{ "s": { "kind": "read", "port": "p", "next": "ok" }, "ok": { "kind": "end", "result": "ok" } }""";
        var driver = Started(steps);
        var first = driver.Single<EffectRequest>();
        driver.Send(new EffectFailed(first.EffectId, ProcessErrorKind.Permanent, "x"));

        driver.Send(new ResumeRequested());

        var second = driver.Single<EffectRequest>();
        second.IdempotencyKey.Should().Be("i-1/s/2");
        second.IdempotencyKey.Should().NotBe(first.IdempotencyKey);
        second.EffectId.Should().Be("i-1/s/2/1");
    }
}

/// <summary>Section 6.2: ai steps.</summary>
public sealed class AiTests
{
    private const string Variables = """
        {
          "case_ref": { "type": "Code", "input": true },
          "answer": { "type": "Code" },
          "score": { "type": "Integer" }
        }
        """;

    private static string Steps(string attempts = "3", string retry = "", string inputSchema = """{ "type": "object", "required": ["case"] }""") => $$"""
        {
          "s": {
            "kind": "ai", "port": "assist.classify", "model": "model-x", "prompt": { "id": "classify", "version": 2 },
            "input": { "case": "case_ref" }, "inputSchema": {{inputSchema}},
            "outputSchema": { "type": "object", "required": ["answer"], "properties": { "answer": { "type": "string" }, "score": { "type": "integer" } } },
            "output": { "answer": "answer", "score": "score" }, "maxAttempts": {{attempts}}, {{retry}}
            "next": "d", "onInvalid": "manual"
          },
          "d": { "kind": "decide", "rule": "conf.flag_rule", "facts": { "count": "score" }, "next": "ok", "onNoDecision": "manual" },
          "ok": { "kind": "end", "result": "ok" },
          "manual": { "kind": "end", "result": "manual" }
        }
        """;

    private static Driver Started(string steps, string input = """{ "case_ref": "c-1" }""")
    {
        var driver = new Driver(Docs.Load(Variables, steps, uses: """[{ "$ref": "rule/conf.flag_rule" }]""", rules: Docs.FlagRule));
        driver.Start(input);
        return driver;
    }

    [Fact]
    public void TheRequestNamesTheModelAndThePrompt()
    {
        var driver = Started(Steps());

        driver.Single<EffectRequest>().Should().Be(new EffectRequest(
            "s", "i-1/s/1/1", "i-1/s/1", ProcessEffectKind.Ai, "assist.classify", """{"case":"c-1"}""", 1, ProcessDelivery.AtLeastOnce, "model-x", "classify", 2));
    }

    [Fact]
    public void AValidOutputIsAssignedAndCarriesTheProvenance()
    {
        var driver = Started(Steps());

        driver.Send(new EffectCompleted("i-1/s/1/1", """{ "answer": "yes", "score": 12 }"""));

        driver.Variable("answer").Should().Be("\"yes\"");
        driver.State.Result.Should().Be("ok");
        var completed = driver.Last.History.Single(entry => entry.Kind == ProcessHistoryKind.EffectCompleted);
        var data = System.Text.Json.Nodes.JsonNode.Parse(completed.DataJson!)!.AsObject();
        data["model"]!.GetValue<string>().Should().Be("model-x");
        data["promptId"]!.GetValue<string>().Should().Be("classify");
        data["promptVersion"]!.GetValue<long>().Should().Be(2);
        data["inputHash"]!.GetValue<string>().Should().MatchRegex("^[0-9a-f]{64}$");
        data["output"]!.ToJsonString().Should().Be("""{"answer":"yes","score":12}""");
    }

    [Fact]
    public void TheInputHashDependsOnlyOnTheInput()
    {
        var first = Started(Steps());
        var second = Started(Steps());
        var other = Started(Steps(), """{ "case_ref": "c-2" }""");
        string Hash(Driver driver)
        {
            driver.Send(new EffectCompleted("i-1/s/1/1", """{ "answer": "yes", "score": 12 }"""));
            return System.Text.Json.Nodes.JsonNode.Parse(driver.Last.History.Single(entry => entry.Kind == ProcessHistoryKind.EffectCompleted).DataJson!)!["inputHash"]!.GetValue<string>();
        }

        Hash(first).Should().Be(Hash(second));
        Hash(other).Should().NotBe(Hash(Started(Steps())));
    }

    [Fact]
    public void AnInvalidOutputIsRetriedImmediatelyUntilTheAttemptsRunOut()
    {
        var driver = Started(Steps("2"));

        driver.Send(new EffectCompleted("i-1/s/1/1", """{ "score": 1 }"""));

        driver.Kinds.Should().Equal(ProcessHistoryKind.EffectFailed, ProcessHistoryKind.EffectRequested);
        driver.Last.History[0].Code.Should().Be("process.ai.output_invalid");
        driver.Single<EffectRequest>().Attempt.Should().Be(2);
        driver.Single<EffectRequest>().EffectId.Should().Be("i-1/s/1/2");
        driver.State.Attempt.Should().Be(2);

        driver.Send(new EffectCompleted("i-1/s/1/2", """{ "answer": 5 }"""));

        driver.State.Result.Should().Be("manual");
        driver.State.Variables.Should().NotContainKey("answer");
    }

    [Theory]
    [InlineData("""not json""")]
    [InlineData("""[1]""")]
    [InlineData("""{ "answer": "yes", "score": "high" }""")]
    public void AMalformedOrMistypedOutputIsInvalid(string output)
    {
        var driver = Started(Steps("1"));

        driver.Send(new EffectCompleted("i-1/s/1/1", output));

        driver.State.Result.Should().Be("manual");
        driver.History.Should().Contain(entry => entry.Code == "process.ai.output_invalid");
    }

    [Fact]
    public void AnOutputThatPassesTheSchemaButNotTheVariableTypeCountsAsInvalid()
    {
        var steps = Steps("1").Replace("\"score\": { \"type\": \"integer\" }", "\"score\": { \"type\": \"number\" }", StringComparison.Ordinal);
        var driver = Started(steps);

        driver.Send(new EffectCompleted("i-1/s/1/1", """{ "answer": "yes", "score": 1.5 }"""));

        driver.State.Result.Should().Be("manual");
    }

    [Fact]
    public void AnInvalidInputIsAPermanentFailureWithoutARequest()
    {
        var driver = Started(Steps(), input: "{}");

        driver.Last.Requests.Should().BeEmpty();
        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem!.Code.Should().Be("process.ai.input_invalid");
        driver.Kinds.Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.EffectFailed, ProcessHistoryKind.Suspended);
    }

    [Fact]
    public void TransportFailuresShareTheAttemptCounterWithInvalidOutputs()
    {
        var retry = "\"retry\": { \"maxAttempts\": 2, \"backoff\": \"fixed\", \"initial\": \"PT1M\" },";
        var driver = Started(Steps("2", retry));
        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Transient, "busy"));
        driver.Send(new TimerFired("i-1/s/1/retry/1"));
        driver.State.Attempt.Should().Be(2);

        driver.Send(new EffectCompleted("i-1/s/1/2", """{ "score": 1 }"""));

        driver.State.Result.Should().Be("manual");
    }

    [Fact]
    public void AValidOutputFeedsTheDecisionThatFollows()
    {
        var driver = Started(Steps());

        driver.Send(new EffectCompleted("i-1/s/1/1", """{ "answer": "yes", "score": 3 }"""));

        driver.Kinds.Should().Contain(ProcessHistoryKind.Decided);
        driver.State.Result.Should().Be("ok");
    }
}

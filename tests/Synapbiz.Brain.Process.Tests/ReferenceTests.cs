using System.Text.Json.Nodes;
using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>The reference process: scenarios, determinism and provenance.</summary>
public sealed class ReferenceTests
{
    private static readonly string[] ScenarioNames =
    [
        "nothing missing",
        "reply after the first reminder",
        "two reminders then escalate",
        "unknown delivery goes to a task",
        "transient failure is retried",
    ];

    [Fact]
    public void TheEmbeddedScenariosAllPass()
    {
        var result = TestFiles.OutreachProcess().RunScenarios();

        result.IsOk.Should().BeTrue(because: string.Join("; ", result.Results.Where(item => !item.Passed).Select(item => item.Name + ": " + item.Failure)));
        result.Results.Select(item => item.Name).Should().Equal(ScenarioNames);
        result.Results.Should().OnlyContain(item => item.Passed && item.Failure == null);
    }

    [Theory]
    [InlineData("\"result\": \"ready\",\n          \"trace\": [\"cycle\", \"check\", \"assess\", \"done\"]", "\"result\": \"closed\",\n          \"trace\": [\"cycle\", \"check\", \"assess\", \"done\"]", "result")]
    [InlineData("\"trace\": [\"cycle\", \"check\", \"assess\", \"done\"]", "\"trace\": [\"cycle\", \"check\", \"done\"]", "trace")]
    [InlineData("\"variables\": { \"missing\": 0 }\n        }\n      },\n      {\n        \"name\": \"reply after", "\"variables\": { \"missing\": 7 }\n        }\n      },\n      {\n        \"name\": \"reply after", "variable")]
    public void AnAlteredExpectationFailsTheScenarioWithAReason(string original, string altered, string reason)
    {
        var text = TestFiles.Read(TestFiles.Fixtures + "outreach.process.json");
        text.Should().Contain(original);
        var loaded = BrainProcessLoader.Load(text.Replace(original, altered, StringComparison.Ordinal), new ProcessLoadContext([TestFiles.OutreachRule()]));

        var result = loaded.Process!.RunScenarios();

        result.IsOk.Should().BeFalse();
        var failed = result.Results.Single(item => !item.Passed);
        failed.Name.Should().Be("nothing missing");
        failed.Failure.Should().Contain(reason);
        result.Results.Count(item => item.Passed).Should().Be(4);
    }

    [Fact]
    public void ARunTwiceGivesIdenticalStatesRequestsAndHistory()
    {
        var document = TestFiles.ReadJson(TestFiles.Fixtures + "outreach.process.json");

        foreach (var scenario in Player.Scenarios(document))
        {
            var first = new Player(TestFiles.OutreachProcess());
            first.Run(scenario);
            var second = new Player(TestFiles.OutreachProcess());
            second.Run(scenario);

            first.Transcript().Should().Be(second.Transcript());
            first.Results.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void TheIndependentPlayerReachesTheExpectedEndOfEveryScenario()
    {
        var document = TestFiles.ReadJson(TestFiles.Fixtures + "outreach.process.json");

        foreach (var scenario in Player.Scenarios(document))
        {
            var player = new Player(TestFiles.OutreachProcess());
            player.Run(scenario);

            player.Results.Should().OnlyContain(result => result.Disposition == AdvanceDisposition.Applied, because: scenario["name"]!.GetValue<string>());
            var last = player.Results[^1].State;
            last.Status.ToString().Should().Be(scenario["expect"]!["status"]!.GetValue<string>());
            last.Result.Should().Be(scenario["expect"]!["result"]!.GetValue<string>());
            var trace = player.Results.SelectMany(result => result.History).Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => entry.Step);
            if (scenario["expect"]!["trace"] is JsonArray expected)
            {
                trace.Should().Equal(expected.Select(step => step!.GetValue<string>()));
            }
        }
    }

    [Fact]
    public void TheSequenceOfHistoryEntriesIsContinuousAcrossAdvances()
    {
        var document = TestFiles.ReadJson(TestFiles.Fixtures + "outreach.process.json");
        var player = new Player(TestFiles.OutreachProcess());
        player.Run(Player.Scenarios(document).ElementAt(2));

        var sequences = player.Results.SelectMany(result => result.History).Select(entry => entry.Sequence).ToList();

        sequences.Should().Equal(Enumerable.Range(1, sequences.Count).Select(number => (long)number));
        player.Results[^1].State.Sequence.Should().Be(sequences.Count);
    }

    [Fact]
    public void TheDecidedEntryOfTheReferenceRunCarriesTheProvenance()
    {
        var document = TestFiles.ReadJson(TestFiles.Fixtures + "outreach.process.json");
        var player = new Player(TestFiles.OutreachProcess());
        player.Run(Player.Scenarios(document).ElementAt(1));

        var decided = player.Results.SelectMany(result => result.History).First(entry => entry.Kind == ProcessHistoryKind.Decided);
        var data = JsonNode.Parse(decided.DataJson!)!.AsObject();

        decided.Step.Should().Be("assess");
        data["rule"]!.GetValue<string>().Should().Be("previsit.missing_items");
        data["version"]!.GetValue<long>().Should().BeGreaterThan(0);
        data["decided"]!.GetValue<bool>().Should().BeTrue();
        data["matchedRowKeys"]!.AsArray().Should().NotBeEmpty();
        data["factsHash"]!.GetValue<string>().Should().MatchRegex("^[0-9a-f]{64}$");
        data["facts"]!.ToJsonString().Should().Contain("missing");
        data["outcome"]!["action"]!.GetValue<string>().Should().Be("remind");
    }

    [Fact]
    public void EveryRequestOfTheReferenceRunIsTiedToAStepAndAnInstance()
    {
        var document = TestFiles.ReadJson(TestFiles.Fixtures + "outreach.process.json");
        var player = new Player(TestFiles.OutreachProcess());
        player.Run(Player.Scenarios(document).ElementAt(2));

        var requests = player.Results.SelectMany(result => result.Requests).ToList();

        requests.Should().NotBeEmpty();
        requests.OfType<EffectRequest>().Select(request => request.EffectId).Should().OnlyHaveUniqueItems();
        requests.OfType<TimerRequest>().Select(request => request.TimerId).Should().OnlyHaveUniqueItems();
        requests.OfType<TaskRequest>().Should().ContainSingle().Which.Queue.Should().Be("front_desk");
        requests.OfType<EffectRequest>().Where(request => request.Port == "messaging.notice").Should().OnlyContain(request => request.Delivery == ProcessDelivery.AtMostOnce);
        requests.Should().OnlyContain(request => request.Step.Length > 0);
    }
}

/// <summary>The scenario runner: every kind of event and every way a scenario fails.</summary>
public sealed class ScenarioRunnerTests
{
    private const string Variables = """
        {
          "case_ref": { "type": "Code", "input": true },
          "count": { "type": "Integer" },
          "note": { "type": "Code" }
        }
        """;

    private const string Steps = """
        {
          "r": { "kind": "read", "port": "p", "output": { "n": "count" }, "retry": { "maxAttempts": 3, "backoff": "fixed", "initial": "PT1M" }, "next": "c", "onError": "bad" },
          "c": { "kind": "subprocess", "process": "child.proc", "next": "t", "onError": "bad" },
          "t": { "kind": "humanTask", "queue": "q", "outcomes": ["go", "stop"], "output": { "note": "note" }, "timeout": "PT1H", "on": { "go": "w", "stop": "ok" }, "onTimeout": "bad" },
          "w": { "kind": "wait", "event": "ping", "timeout": "PT2H", "next": "ok", "onTimeout": "bad" },
          "ok": { "kind": "end", "result": "ok" },
          "bad": { "kind": "end", "result": "bad" }
        }
        """;

    private const string Start = """ "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } } """;

    private static ScenarioRunResult Run(string events, string expect, string start = Start)
    {
        var scenarios = $$"""
            "scenarios": [ { "name": "case", {{start}}, "events": {{events}}, "expect": {{expect}} } ]
            """;
        var process = Docs.Load(Variables, Steps, scenarios, uses: """[{ "$ref": "process/child.proc" }]""", start: "r");
        return process.RunScenarios();
    }

    private static string Failure(ScenarioRunResult result)
    {
        result.IsOk.Should().BeFalse();
        return result.Results.Single().Failure!;
    }

    [Fact]
    public void AProcessWithoutScenariosIsOkWithNoResults()
    {
        var process = Docs.Load("{}", """{ "s": { "kind": "end", "result": "ok" } }""");

        var result = process.RunScenarios();

        result.IsOk.Should().BeTrue();
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public void EveryKindOfEventCanBePlayed()
    {
        var events = """
            [
              { "fail": "r", "error": "RateLimited", "code": "busy", "retryAfter": "PT5M" },
              { "advance": "PT4M" },
              { "advance": "PT2M" },
              { "complete": "r", "output": { "n": 3 } },
              { "child": "c", "result": "done", "output": {} },
              { "task": "t", "outcome": "go", "data": { "note": "fine" } },
              { "event": "ping", "payload": {} }
            ]
            """;
        var expect = """{ "status": "Completed", "result": "ok", "trace": ["r", "c", "t", "w", "ok"], "variables": { "count": 3, "note": "fine" } }""";

        var result = Run(events, expect);

        result.IsOk.Should().BeTrue(because: result.Results[0].Failure);
        result.Results.Single().Name.Should().Be("case");
    }

    [Fact]
    public void ARetryAfterBeyondTheDelayIsHonouredByTheVirtualClock()
    {
        // The retry delay is 5 minutes (retryAfter wins over 1 minute): advancing only 4 minutes must not fire it.
        var events = """
            [
              { "fail": "r", "error": "RateLimited", "code": "busy", "retryAfter": "PT5M" },
              { "advance": "PT4M" },
              { "complete": "r", "output": { "n": 1 } }
            ]
            """;

        var failure = Failure(Run(events, """{ "status": "Completed" }"""));

        failure.Should().Contain("Effect");
    }

    [Fact]
    public void ATimerThatFiresInsideTheWindowIsFiredAtItsOwnTime()
    {
        var events = """
            [
              { "complete": "r", "output": { "n": 1 } },
              { "child": "c", "result": "done" },
              { "advance": "PT1H" },
              { "advance": "PT1M" }
            ]
            """;
        var expect = """{ "status": "Completed", "result": "bad", "trace": ["r", "c", "t", "bad"] }""";

        Run(events, expect).IsOk.Should().BeTrue();
    }

    [Fact]
    public void ACancelAndAFailedChildAreEvents()
    {
        var failed = Run("""[ { "complete": "r" }, { "childFailed": "c", "code": "child.broken" } ]""", """{ "status": "Completed", "result": "bad" }""");
        var cancelled = Run("""[ { "cancel": "stop it" } ]""", """{ "status": "Cancelled", "trace": ["r"] }""");

        failed.IsOk.Should().BeTrue(because: failed.Results[0].Failure);
        cancelled.IsOk.Should().BeTrue(because: cancelled.Results[0].Failure);
    }

    [Fact]
    public void AResumeRevivesASuspendedInstance()
    {
        var steps = """
            {
              "r": { "kind": "read", "port": "p", "next": "ok" },
              "ok": { "kind": "end", "result": "ok" }
            }
            """;
        var scenarios = """
            "scenarios": [ { "name": "resume", "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04" },
              "events": [ { "fail": "r", "error": "Permanent", "code": "x" }, { "resume": true }, { "complete": "r" } ],
              "expect": { "status": "Completed", "result": "ok", "trace": ["r", "ok"] } } ]
            """;

        Docs.Load("{}", steps, scenarios, start: "r").RunScenarios().IsOk.Should().BeTrue();
    }

    [Fact]
    public void TheTodayOfTheClockAdvancesWithTheVirtualDays()
    {
        var steps = """
            {
              "t": { "kind": "timer", "after": "P2D", "next": "d" },
              "d": { "kind": "decide", "rule": "conf.flag_rule", "facts": { "count": "count" }, "next": "ok", "onNoDecision": "ok", "onError": "ok" },
              "ok": { "kind": "end", "result": "ok" }
            }
            """;
        var scenarios = """
            "scenarios": [ { "name": "days", "start": { "at": "2027-01-04T23:00:00Z", "today": "2027-01-04", "input": { "count": 3 } },
              "events": [ { "advance": "P2D" } ],
              "expect": { "status": "Completed", "result": "ok" } } ]
            """;
        var process = Docs.Load("""{ "count": { "type": "Integer", "input": true } }""", steps, scenarios, uses: """[{ "$ref": "rule/conf.flag_rule" }]""", start: "t", rules: Docs.FlagRule);

        process.RunScenarios().IsOk.Should().BeTrue();
    }

    [Fact]
    public void ARejectedStartFailsTheScenario()
    {
        var steps = """{ "r": { "kind": "read", "port": "p", "next": "ok" }, "ok": { "kind": "end", "result": "ok" } }""";
        var scenarios = """
            "scenarios": [ { "name": "bad input", "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "other": 1 } },
              "events": [], "expect": { "status": "Waiting" } } ]
            """;

        var process = Docs.Load("""{ "other": { "type": "Integer" } }""", steps, scenarios, start: "r");

        process.RunScenarios().Results.Single().Failure.Should().Contain("start was Rejected").And.Contain("process.input.unknown");
    }

    [Fact]
    public void AnEventThatAddressesAnotherStepFailsTheScenario()
    {
        var failure = Failure(Run("""[ { "task": "t", "outcome": "go" } ]""", """{ "status": "Waiting" }"""));

        failure.Should().Contain("Task").And.Contain("'t'");
    }

    [Fact]
    public void AnEventTheInstanceDoesNotAcceptFailsTheScenarioAsRejected()
    {
        var failure = Failure(Run("""[ { "complete": "r" }, { "child": "c", "result": "done" }, { "event": "ping" } ]""", """{ "status": "Waiting" }"""));

        failure.Should().Contain("Rejected").And.Contain("process.event.not_accepted");
    }

    [Fact]
    public void AnIgnoredTriggerFailsTheScenario()
    {
        var failure = Failure(Run("""[ { "cancel": "x" }, { "resume": true } ]""", """{ "status": "Cancelled" }"""));

        failure.Should().Contain("ResumeRequested").And.Contain("Ignored");
    }

    [Theory]
    [InlineData("""{ "status": "Waiting" }""", "status")]
    [InlineData("""{ "status": "Waiting", "result": "x" }""", "status")]
    [InlineData("""{ "status": "Cancelled", "result": "x" }""", "result")]
    [InlineData("""{ "status": "Completed", "trace": ["r"] }""", "status")]
    [InlineData("""{ "status": "Cancelled", "trace": ["r", "c"] }""", "trace")]
    [InlineData("""{ "status": "Cancelled", "variables": { "count": 1 } }""", "count")]
    [InlineData("""{ "status": "Cancelled", "variables": { "note": "x" } }""", "absent")]
    public void AnExpectationThatDoesNotHoldFailsTheScenarioWithTheDifference(string expect, string fragment)
    {
        var failure = Failure(Run("""[ { "cancel": "stop" } ]""", expect));

        failure.Should().Contain(fragment);
    }

    [Fact]
    public void AResultThatDiffersIsReported()
    {
        var steps = """{ "r": { "kind": "read", "port": "p", "next": "ok" }, "ok": { "kind": "end", "result": "ok" } }""";
        var scenarios = """
            "scenarios": [ { "name": "result", "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04" },
              "events": [ { "complete": "r" } ], "expect": { "status": "Completed", "result": "other" } } ]
            """;

        Docs.Load("{}", steps, scenarios, start: "r").RunScenarios().Results.Single().Failure.Should().Contain("result 'other'").And.Contain("'ok'");
    }

    [Theory]
    [InlineData("""{ "blob": { "b": [1, 2], "a": { "x": true } } }""", true)]
    [InlineData("""{ "blob": { "a": { "x": true }, "b": [1, 2] }, "n": 3.0 }""", true)]
    [InlineData("""{ "blob": { "a": { "x": false }, "b": [1, 2] } }""", false)]
    [InlineData("""{ "blob": { "a": { "x": true }, "b": [1] } }""", false)]
    [InlineData("""{ "blob": { "a": { "x": true }, "b": [1, 2], "c": 1 } }""", false)]
    [InlineData("""{ "blob": { "a": { "x": true } } }""", false)]
    [InlineData("""{ "blob": [1, 2] }""", false)]
    [InlineData("""{ "blob": "text" }""", false)]
    [InlineData("""{ "n": "3" }""", false)]
    [InlineData("""{ "n": 4 }""", false)]
    [InlineData("""{ "t": "x" }""", true)]
    [InlineData("""{ "t": null }""", false)]
    public void ExpectedVariablesAreComparedByJsonEquality(string expected, bool passes)
    {
        var variables = """{ "blob": { "type": "Json", "input": true }, "n": { "type": "Integer", "input": true }, "t": { "type": "Code", "input": true } }""";
        var steps = """{ "r": { "kind": "read", "port": "p", "next": "ok" }, "ok": { "kind": "end", "result": "ok" } }""";
        var scenarios = $$"""
            "scenarios": [ { "name": "compare", "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "blob": { "a": { "x": true }, "b": [1, 2] }, "n": 3, "t": "x" } },
              "events": [], "expect": { "status": "Waiting", "variables": {{expected}} } } ]
            """;

        Docs.Load(variables, steps, scenarios, start: "r").RunScenarios().IsOk.Should().Be(passes);
    }

    [Fact]
    public void ScenariosAreIndependentOfEachOther()
    {
        var steps = """{ "r": { "kind": "read", "port": "p", "next": "ok" }, "ok": { "kind": "end", "result": "ok" } }""";
        var scenarios = """
            "scenarios": [
              { "name": "first", "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04" }, "events": [ { "complete": "r" } ], "expect": { "status": "Completed" } },
              { "name": "second", "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04" }, "events": [], "expect": { "status": "Completed" } },
              { "name": "third", "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04" }, "events": [ { "complete": "r" } ], "expect": { "status": "Completed" } }
            ]
            """;

        var result = Docs.Load("{}", steps, scenarios, start: "r").RunScenarios();

        result.Results.Select(item => item.Passed).Should().Equal(true, false, true);
        result.IsOk.Should().BeFalse();
    }
}

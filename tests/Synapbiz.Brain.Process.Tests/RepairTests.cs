using System.Text.Json.Nodes;
using FluentAssertions;
using Nuvora.Decisions.Contracts;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Defects found by the independent verification: each test here failed before its fix.</summary>
public sealed class RepairTests
{
    private static readonly ProcessLoadContext Empty = new([]);

    // F1: the body edge of a loop is the only bounded edge.

    [Fact]
    public void ALoopWhoseExitLeadsBackToTheLoopIsAnUnboundedCycle()
    {
        var steps = """
            {
              "l": { "kind": "loop", "maxIterations": 1, "body": "t1", "onExhausted": "t2" },
              "t1": { "kind": "timer", "after": "PT1H", "next": "l" },
              "t2": { "kind": "timer", "after": "PT1H", "next": "l" }
            }
            """;

        var loaded = BrainProcessLoader.Load(Docs.Text("{}", steps, start: "l"), Empty);

        loaded.Errors.Select(error => (error.Code, error.Path)).Should().Equal(("process.cycle.unbounded", "/body/steps/l"));
    }

    [Fact]
    public void ALoopWhoseBodyReturnsToTheLoopIsValid()
    {
        var steps = """
            {
              "l": { "kind": "loop", "maxIterations": 2, "body": "a", "onExhausted": "e" },
              "a": { "kind": "read", "port": "p", "next": "b" },
              "b": { "kind": "timer", "after": "PT1H", "next": "l" },
              "e": { "kind": "end", "result": "ok" }
            }
            """;

        BrainProcessLoader.Load(Docs.Text("{}", steps, start: "l"), Empty).IsValid.Should().BeTrue();
    }

    // F2: ai schemas are proved at load.

    [Theory]
    [InlineData("inputSchema", """{ "$ref": "http://example.com/x" }""")]
    [InlineData("outputSchema", """{ "type": "object", "properties": { "a": { "$ref": "#/$defs/q" } } }""")]
    [InlineData("outputSchema", """{ "$ref": "#/$defs/q" }""")]
    [InlineData("inputSchema", """{ "$ref": "#anchor_that_is_missing" }""")]
    [InlineData("inputSchema", """{ "type": "object", "extra": 1 }""")]
    public void AnAiSchemaThatCannotBeProvedIsInvalid(string member, string schema)
    {
        var step = $$"""
            {
              "s": { "kind": "ai", "port": "p", "model": "m", "prompt": { "id": "x", "version": 1 }, "inputSchema": {{(member == "inputSchema" ? schema : "{}")}},
                     "outputSchema": {{(member == "outputSchema" ? schema : "{}")}}, "next": "d", "onInvalid": "e" },
              "d": { "kind": "decide", "rule": "conf.flag_rule", "facts": {}, "next": "e", "onNoDecision": "e" },
              "e": { "kind": "end", "result": "ok" }
            }
            """;

        var loaded = BrainProcessLoader.Load(Docs.Text("{}", step, uses: """[{ "$ref": "rule/conf.flag_rule" }]"""), new ProcessLoadContext([Docs.FlagRule]));

        loaded.Errors.Select(error => (error.Code, error.Path)).Should().Contain(("process.ai.schema_invalid", "/body/steps/s/" + member));
    }

    [Fact]
    public void ALocalReferenceInsideTheSchemaIsFine()
    {
        var steps = """
            {
              "s": { "kind": "ai", "port": "p", "model": "m", "prompt": { "id": "x", "version": 1 }, "inputSchema": { "$ref": "#/$defs/in", "$defs": { "in": { "type": "object" } } },
                     "outputSchema": { "$anchor": "root", "properties": { "a": { "$ref": "#root" } } }, "next": "d", "onInvalid": "e" },
              "d": { "kind": "decide", "rule": "conf.flag_rule", "facts": {}, "next": "e", "onNoDecision": "e" },
              "e": { "kind": "end", "result": "ok" }
            }
            """;

        var loaded = BrainProcessLoader.Load(Docs.Text("{}", steps, uses: """[{ "$ref": "rule/conf.flag_rule" }]"""), new ProcessLoadContext([Docs.FlagRule]));

        loaded.Errors.Select(error => error.Code).Should().NotContain("process.ai.schema_invalid");
    }

    // F3: a null element of a forEach array clears the item.

    [Fact]
    public void ANullElementClearsTheItemAndTheStateStillRoundTrips()
    {
        var steps = """
            {
              "s": { "kind": "loop", "maxIterations": 5, "forEach": "items", "item": "item", "body": "b", "onExhausted": "e", "next": "e" },
              "b": { "kind": "timer", "after": "PT1M", "next": "s" },
              "e": { "kind": "end", "result": "ok" }
            }
            """;
        var variables = """{ "items": { "type": "Json", "input": true }, "item": { "type": "Json" } }""";
        var driver = new Driver(Docs.Load(variables, steps));

        driver.Start("""{ "items": [null, 1] }""");

        driver.State.Variables.Should().NotContainKey("item");
        ProcessState.FromJson(driver.State.ToJson()).Should().Be(driver.State);
        driver.Send(new TimerFired("i-1/b/2/timer"));
        driver.Variable("item").Should().Be("1");
        driver.Send(new TimerFired("i-1/b/4/timer")).State.Result.Should().Be("ok");
    }

    [Fact]
    public void ANullElementAfterAValueClearsThePreviousItem()
    {
        var steps = """
            {
              "s": { "kind": "loop", "maxIterations": 5, "forEach": "items", "item": "item", "body": "b", "onExhausted": "e", "next": "e" },
              "b": { "kind": "timer", "after": "PT1M", "next": "s" },
              "e": { "kind": "end", "result": "ok" }
            }
            """;
        var driver = new Driver(Docs.Load("""{ "items": { "type": "Json", "input": true }, "item": { "type": "Json" } }""", steps));
        driver.Start("""{ "items": [1, null] }""");
        driver.Variable("item").Should().Be("1");

        driver.Send(new TimerFired("i-1/b/2/timer"));

        driver.State.Variables.Should().NotContainKey("item");
    }

    // F5: duplicate member names and malformed JSON never throw.

    [Theory]
    [InlineData("""{"a":1,"a":2}""")]
    [InlineData("""{"x":{"a":1,"a":2}}""")]
    [InlineData("""[{"a":1,"b":2,"a":3}]""")]
    [InlineData("""{"a":1,"b":{"c":[{"d":1,"d":1}]}}""")]
    public void ADocumentWithDuplicateMemberNamesIsNotValidJson(string text)
    {
        var loaded = BrainProcessLoader.Load(text, Empty);

        loaded.Errors.Select(error => error.Code).Should().Equal("spec.json.invalid");
    }

    [Fact]
    public void ADuplicateDeepInsideAValidDocumentIsNotValidJson()
    {
        var text = Docs.Text("{}", """{ "s": { "kind": "end", "result": "ok" } }""").Replace("\"meta\": {}", "\"meta\": { \"x\": { \"k\": 1, \"k\": 2 } }", StringComparison.Ordinal);

        BrainProcessLoader.Load(text, Empty).Errors.Select(error => error.Code).Should().Equal("spec.json.invalid");
    }

    [Theory]
    [InlineData("""{"instanceId":"a","instanceId":"b"}""")]
    [InlineData("""{"variables":{"a":1,"a":2}}""")]
    public void AStateWithDuplicateMemberNamesIsMalformed(string text)
    {
        var error = Record.Exception(() => ProcessState.FromJson(text));

        error.Should().BeOfType<DecisionEngineException>().Which.ErrorCode.Should().Be("process.state.malformed");
    }

    public static IEnumerable<object[]> MalformedTexts() =>
    [
        [""], ["   "], ["{"], ["}"], ["["], ["{\"a\":"], ["{\"a\":1,}"], ["{'a':1}"], ["nul"], ["undefined"], ["{} {}"], ["{}garbage"],
        ["\u0000"], ["\"\\ud800\""], ["{\"a\":1,\"a\":2}"], ["{\"a\":{\"b\":1,\"b\":2}}"], ["[{\"k\":1,\"k\":1}]"], ["{\"a\":1e999999}"],
        ["{\"a\":01}"], ["{\"a\":\"\\u00zz\"}"], ["/* c */ {}"], [new string('[', 5000)], [string.Concat(Enumerable.Repeat("{\"a\":", 200)) + "1" + new string('}', 200)],
        ["null"], ["true"], ["12"], ["\"text\""], ["[]"], ["{\"a\":[1,2,{\"b\":null}]}"],
    ];

    [Theory]
    [MemberData(nameof(MalformedTexts))]
    public void NoPublicEntryPointThrowsAnUndocumentedExceptionOnMalformedJson(string text)
    {
        var steps = """
            {
              "r": { "kind": "read", "port": "p", "output": { "n": "count" }, "next": "a", "onError": "t" },
              "a": { "kind": "ai", "port": "p", "model": "m", "prompt": { "id": "x", "version": 1 }, "inputSchema": {}, "outputSchema": { "type": "object" }, "output": { "n": "count" }, "next": "d", "onInvalid": "t" },
              "d": { "kind": "decide", "rule": "conf.flag_rule", "facts": { "count": "count" }, "next": "t", "onNoDecision": "t" },
              "t": { "kind": "humanTask", "queue": "q", "outcomes": ["go"], "output": { "n": "count" }, "on": { "go": "w" }, "timeout": "PT1H", "onTimeout": "w" },
              "w": { "kind": "wait", "event": "ping", "output": { "n": "count" }, "next": "c" },
              "c": { "kind": "subprocess", "process": "child.proc", "output": { "n": "count" }, "next": "e", "onError": "e" },
              "e": { "kind": "end", "result": "ok" }
            }
            """;
        var variables = """{ "count": { "type": "Integer", "initial": 12 } }""";
        var uses = """[{ "$ref": "rule/conf.flag_rule" }, { "$ref": "process/child.proc" }]""";
        var process = Docs.Load(variables, steps, """ "accepts": ["ping", "other"] """, uses, "r", Docs.FlagRule);
        var driver = new Driver(process);
        driver.Start();

        // Every kind of pending work receives the malformed text as its payload; the instance keeps going or stops cleanly.
        var triggers = new Func<ProcessState, ProcessTrigger>[]
        {
            state => new EffectCompleted(state.Pending?.Id ?? "x", text),
            state => new TaskCompleted(state.Pending?.Id ?? "x", "go", text),
            state => new EventReceived("ping", null, text),
            state => new EventReceived("other", "c-1", text),
            state => new ChildCompleted(state.Pending?.Id ?? "x", "done", text),
        };
        foreach (var trigger in triggers)
        {
            var act = () => driver.Send(trigger(driver.State));
            act.Should().NotThrow();
        }

        var load = () => BrainProcessLoader.Load(text, new ProcessLoadContext([Docs.FlagRule], [text]));
        var read = () => ProcessState.FromJson(text);
        load.Should().NotThrow();
        read.Should().Throw<DecisionEngineException>().Which.ErrorCode.Should().Be("process.state.malformed");
    }

    [Theory]
    [MemberData(nameof(MalformedTexts))]
    public void AMalformedPayloadForEachKindOfPendingWorkIsHandledPerSection69(string text)
    {
        var effect = new Driver(Docs.Load("{}", """{ "s": { "kind": "read", "port": "p", "next": "e", "onError": "f" }, "e": { "kind": "end", "result": "ok" }, "f": { "kind": "end", "result": "failed" } }"""));
        effect.Start();
        var act = () => effect.Send(new EffectCompleted("i-1/s/1/1", text));
        act.Should().NotThrow();
        effect.State.Status.Should().NotBe(ProcessStatus.Running);

        var task = new Driver(Docs.Load("{}", """{ "s": { "kind": "humanTask", "queue": "q", "outcomes": ["go"], "on": { "go": "e" } }, "e": { "kind": "end", "result": "ok" } }"""));
        task.Start();
        var done = () => task.Send(new TaskCompleted("i-1/s/1/task", "go", text));
        done.Should().NotThrow();

        var child = new Driver(Docs.Load("{}", """{ "s": { "kind": "subprocess", "process": "c.p", "next": "e" }, "e": { "kind": "end", "result": "ok" } }""", uses: """[{ "$ref": "process/c.p" }]"""));
        child.Start();
        var finished = () => child.Send(new ChildCompleted("i-1/s/1/child", "done", text));
        finished.Should().NotThrow();
    }

    [Fact]
    public void DuplicateNamesInAnEffectOutputAreAPermanentFailureNotAnException()
    {
        var driver = new Driver(Docs.Load("""{ "n": { "type": "Integer" } }""", """{ "s": { "kind": "read", "port": "p", "output": { "a": "n" }, "next": "e", "onError": "f" }, "e": { "kind": "end", "result": "ok" }, "f": { "kind": "end", "result": "failed" } }"""));
        driver.Start();

        driver.Send(new EffectCompleted("i-1/s/1/1", """{"a":1,"a":2}"""));

        driver.State.Result.Should().Be("failed");
        driver.History.Should().Contain(entry => entry.Code == "process.effect.output_malformed");
    }

    [Fact]
    public void DuplicateNamesInATaskDataAreRejected()
    {
        var driver = new Driver(Docs.Load("{}", """{ "s": { "kind": "humanTask", "queue": "q", "outcomes": ["go"], "on": { "go": "e" } }, "e": { "kind": "end", "result": "ok" } }"""));
        driver.Start();

        var result = driver.Send(new TaskCompleted("i-1/s/1/task", "go", """{"a":1,"a":2}"""));

        result.Disposition.Should().Be(AdvanceDisposition.Rejected);
        result.Code.Should().Be("process.variable.type_mismatch");
    }

    [Fact]
    public void DuplicateNamesInAnEventPayloadAreRejectedWhenBufferedAndSuspendWhenAwaited()
    {
        var buffered = new Driver(Docs.Load("{}", """{ "s": { "kind": "timer", "after": "PT1H", "next": "e" }, "e": { "kind": "end", "result": "ok" } }""", """ "accepts": ["ping"] """));
        buffered.Start();
        buffered.Send(new EventReceived("ping", null, """{"a":1,"a":2}""")).Code.Should().Be("process.variable.type_mismatch");

        var awaited = new Driver(Docs.Load("{}", """{ "s": { "kind": "wait", "event": "ping", "next": "e" }, "e": { "kind": "end", "result": "ok" } }"""));
        awaited.Start();
        awaited.Send(new EventReceived("ping", null, """{"a":1,"a":2}"""));
        awaited.State.Problem!.Code.Should().Be("process.variable.type_mismatch");
    }

    [Fact]
    public void DuplicateNamesInAChildOutputAreAChildFailure()
    {
        var driver = new Driver(Docs.Load("{}", """{ "s": { "kind": "subprocess", "process": "c.p", "next": "e", "onError": "f" }, "e": { "kind": "end", "result": "ok" }, "f": { "kind": "end", "result": "failed" } }""", uses: """[{ "$ref": "process/c.p" }]"""));
        driver.Start();

        driver.Send(new ChildCompleted("i-1/s/1/child", "done", """{"a":1,"a":2}"""));

        driver.State.Result.Should().Be("failed");
    }

    // F6: exact integer bounds.

    [Theory]
    [InlineData("9007199254740991", true)]
    [InlineData("-9007199254740991", true)]
    [InlineData("9007199254740990", true)]
    [InlineData("-9007199254740990", true)]
    [InlineData("9007199254740992", false)]
    [InlineData("-9007199254740992", false)]
    [InlineData("9007199254740993", false)]
    [InlineData("-9007199254740993", false)]
    public void IntegerBoundsAreExactForInput(string number, bool accepted)
    {
        var process = Docs.Load("""{ "n": { "type": "Integer", "input": true } }""", """{ "s": { "kind": "end", "result": "ok" } }""");

        var result = process.Start("i-1", Docs.Input("{ \"n\": " + number + " }"), Docs.Clock());

        result.Disposition.Should().Be(accepted ? AdvanceDisposition.Applied : AdvanceDisposition.Rejected);
        if (accepted)
        {
            result.State.Variables["n"].Should().Be(number);
        }
        else
        {
            result.Code.Should().Be("process.variable.type_mismatch");
        }
    }

    [Theory]
    [InlineData("9007199254740991", true)]
    [InlineData("-9007199254740991", true)]
    [InlineData("9007199254740990", true)]
    [InlineData("9007199254740992", false)]
    [InlineData("-9007199254740992", false)]
    public void IntegerBoundsAreExactForInitial(string number, bool accepted)
    {
        var text = Docs.Text("{ \"n\": { \"type\": \"Integer\", \"initial\": " + number + " } }", """{ "s": { "kind": "end", "result": "ok" } }""");

        var loaded = BrainProcessLoader.Load(text, Empty);

        if (accepted)
        {
            loaded.IsValid.Should().BeTrue();
            loaded.Process!.Start("i-1", null, Docs.Clock()).State.Variables["n"].Should().Be(number);
        }
        else
        {
            loaded.Errors.Select(error => (error.Code, error.Path)).Should().Equal(("process.variable.initial", "/body/variables/n/initial"));
        }
    }

    // F7: every instant is UTC.

    [Fact]
    public void EveryInstantOfRequestsAndHistoryIsUtcWhateverTheOffsetOfTheClock()
    {
        var steps = """
            {
              "r": { "kind": "read", "port": "p", "retry": { "maxAttempts": 2, "backoff": "fixed", "initial": "PT1M" }, "next": "t" },
              "t": { "kind": "timer", "until": "when", "offset": "PT30M", "next": "k" },
              "k": { "kind": "humanTask", "queue": "q", "outcomes": ["go"], "timeout": "PT1H", "on": { "go": "w" }, "onTimeout": "w" },
              "w": { "kind": "wait", "event": "ping", "timeout": "PT2H", "next": "e", "onTimeout": "e" },
              "e": { "kind": "end", "result": "ok" }
            }
            """;
        var process = Docs.Load("""{ "when": { "type": "Instant", "input": true } }""", steps, start: "r");
        var local = new DateTimeOffset(2027, 1, 4, 16, 0, 0, TimeSpan.FromHours(2));
        var driver = new Driver(process) { Now = local };
        driver.Start("""{ "when": "2027-01-05T09:00:00+05:00" }""");
        driver.Send(new EffectFailed("i-1/r/1/1", ProcessErrorKind.Transient, "busy"));
        driver.Send(new TimerFired("i-1/r/1/retry/1"));
        driver.Send(new EffectCompleted("i-1/r/1/2", "{}"));
        driver.Send(new TimerFired("i-1/t/" + driver.State.Entry + "/timer"));
        driver.Send(new TaskCompleted("i-1/k/" + driver.State.Entry + "/task", "go", "{}"));
        driver.Send(new EventReceived("ping", null, "{}"));

        var instants = driver.Requests.OfType<TimerRequest>().Select(request => request.DueAt).Concat(driver.History.Select(entry => entry.At)).ToList();

        instants.Should().NotBeEmpty().And.OnlyContain(instant => instant.Offset == TimeSpan.Zero);
        driver.Requests.OfType<TimerRequest>().Select(request => request.DueAt).Should().Contain(local.AddMinutes(1).ToUniversalTime());
        driver.Requests.OfType<TimerRequest>().Select(request => request.DueAt).Should().Contain(new DateTimeOffset(2027, 1, 5, 4, 30, 0, TimeSpan.Zero));
    }

    // Suspension cancels what is outstanding.

    [Fact]
    public void ALiveEventWithABadPayloadCancelsTheWaitAndItsTimeoutWhenItSuspends()
    {
        var driver = new Driver(Docs.Load("""{ "n": { "type": "Integer" } }""", """{ "s": { "kind": "wait", "event": "ping", "output": { "n": "n" }, "timeout": "PT1H", "next": "e", "onTimeout": "e" }, "e": { "kind": "end", "result": "ok" } }"""));
        driver.Start();

        driver.Send(new EventReceived("ping", null, """{ "n": "x" }"""));

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.Last.Requests.Should().Equal(new CancelWaitRequest("s", "i-1/s/1/wait"), new CancelTimerRequest("s", "i-1/s/1/timeout"));
    }

    [Fact]
    public void ALiveEventWithABadPayloadCancelsTheWaitWithoutATimeout()
    {
        var driver = new Driver(Docs.Load("""{ "n": { "type": "Integer" } }""", """{ "s": { "kind": "wait", "event": "ping", "output": { "n": "n" }, "next": "e" }, "e": { "kind": "end", "result": "ok" } }"""));
        driver.Start();

        driver.Send(new EventReceived("ping", null, "[1]"));

        driver.Last.Requests.Should().Equal(new CancelWaitRequest("s", "i-1/s/1/wait"));
    }

    // Reference values are stored lower-case.

    [Fact]
    public void AReferenceIsStoredLowerCaseFromEverySource()
    {
        var variables = """{ "r1": { "type": "Reference", "input": true }, "r2": { "type": "Reference", "initial": "AAAAAAAA-0000-0000-0000-00000000000B" }, "r3": { "type": "Reference" } }""";
        var steps = """
            {
              "s": { "kind": "read", "port": "p", "output": { "r": "r3" }, "next": "e" },
              "e": { "kind": "end", "result": "ok" }
            }
            """;
        var driver = new Driver(Docs.Load(variables, steps));

        driver.Start("""{ "r1": "AAAAAAAA-0000-0000-0000-00000000000C" }""");
        driver.Send(new EffectCompleted("i-1/s/1/1", """{ "r": "AAAAAAAA-0000-0000-0000-00000000000D" }"""));

        driver.Variable("r1").Should().Be("\"aaaaaaaa-0000-0000-0000-00000000000c\"");
        driver.Variable("r2").Should().Be("\"aaaaaaaa-0000-0000-0000-00000000000b\"");
        driver.Variable("r3").Should().Be("\"aaaaaaaa-0000-0000-0000-00000000000d\"");
    }

    [Fact]
    public void ACorrelationOfTypeReferenceMatchesWhateverTheCaseOfTheEvent()
    {
        var steps = """{ "kind": "wait", "event": "form_submitted", "correlation": "owner", "next": "e" }""";
        var variables = """{ "owner": { "type": "Reference", "input": true } }""";
        var process = Docs.Load(variables, "{ \"s\": " + steps + ", \"e\": { \"kind\": \"end\", \"result\": \"ok\" } }");
        var driver = new Driver(process);
        driver.Start("""{ "owner": "00000000-0000-0000-0000-0000000000AB" }""");

        driver.Send(new EventReceived("form_submitted", "00000000-0000-0000-0000-0000000000ab", "{}")).State.Result.Should().Be("ok");
    }

    [Fact]
    public void ABufferedCorrelationOfAnotherCaseIsFoundOnEnteringTheWait()
    {
        var steps = """
            {
              "t": { "kind": "timer", "after": "PT1H", "next": "w" },
              "w": { "kind": "wait", "event": "form_submitted", "correlation": "owner", "next": "e" },
              "e": { "kind": "end", "result": "ok" }
            }
            """;
        var variables = """{ "owner": { "type": "Reference", "input": true } }""";
        var driver = new Driver(Docs.Load(variables, steps, """ "accepts": ["form_submitted"] """, start: "t"));
        driver.Start("""{ "owner": "00000000-0000-0000-0000-0000000000AB" }""");
        driver.Send(new EventReceived("form_submitted", "00000000-0000-0000-0000-0000000000AB", "{}"));

        driver.Send(new TimerFired("i-1/t/1/timer"));

        driver.State.Result.Should().Be("ok");
        driver.State.Inbox.Should().BeEmpty();
    }

    [Fact]
    public void ACodeCorrelationStillComparesExactly()
    {
        var steps = """{ "s": { "kind": "wait", "event": "ping", "correlation": "c", "next": "e" }, "e": { "kind": "end", "result": "ok" } }""";
        var driver = new Driver(Docs.Load("""{ "c": { "type": "Code", "input": true } }""", steps));
        driver.Start("""{ "c": "Abc" }""");

        driver.Send(new EventReceived("ping", "abc", "{}")).Disposition.Should().Be(AdvanceDisposition.Rejected);
        driver.Send(new EventReceived("ping", "Abc", "{}")).State.Result.Should().Be("ok");
    }

    // The trigger payload read back through JsonNode is never lazily parsed.

    [Fact]
    public void ATriggerPayloadIsParsedOnceAndCanonical()
    {
        var driver = new Driver(Docs.Load("""{ "j": { "type": "Json" } }""", """{ "s": { "kind": "read", "port": "p", "output": { "x": "j" }, "next": "e" }, "e": { "kind": "end", "result": "ok" } }"""));
        driver.Start();

        driver.Send(new EffectCompleted("i-1/s/1/1", """{ "x": { "b": [3, 1], "a": null } }"""));

        JsonNode.Parse(driver.Variable("j"))!.ToJsonString().Should().Be("""{"a":null,"b":[3,1]}""");
    }
}

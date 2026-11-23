using System.Text.Json.Nodes;
using FluentAssertions;
using Nuvora.Decisions.Contracts;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>The serialized state: round trip, stability, equality and malformed text.</summary>
public sealed class StateTests
{
    private const string Waiting = """{"instanceId":"i-1","definitionId":"t.proc","definitionVersion":1,"definitionHash":"abc","status":"Waiting","currentStep":"s","stage":"one","result":null,"variables":{"a":1,"b":"x"},"loopCounters":{"l":2},"attempt":1,"entry":3,"stepCount":3,"sequence":9,"pending":{"kind":"Wait","id":"i-1/s/3/wait","timeoutTimerId":"i-1/s/3/timeout"},"inbox":[{"name":"e","correlation":null,"payload":{"x":1}},{"name":"f","correlation":"c-1","payload":{}}],"problem":null}""";

    private const string Suspended = """{"instanceId":"i-1","definitionId":"t.proc","definitionVersion":1,"definitionHash":"abc","status":"Suspended","currentStep":"s","stage":null,"result":null,"variables":{},"loopCounters":{},"attempt":1,"entry":1,"stepCount":1,"sequence":4,"pending":null,"inbox":[],"problem":{"code":"x.y","step":"s","detail":"why"}}""";

    private const string Completed = """{"instanceId":"i-1","definitionId":"t.proc","definitionVersion":1,"definitionHash":"abc","status":"Completed","currentStep":null,"stage":"two","result":"ok","variables":{},"loopCounters":{},"attempt":1,"entry":1,"stepCount":1,"sequence":4,"pending":null,"inbox":[],"problem":null}""";

    [Theory]
    [InlineData(Waiting)]
    [InlineData(Suspended)]
    [InlineData(Completed)]
    public void TheTextOfAStateReadsBackToTheSameTextAndAnEqualState(string json)
    {
        var state = ProcessState.FromJson(json);

        state.ToJson().Should().Be(json);
        ProcessState.FromJson(state.ToJson()).Should().Be(state);
    }

    [Fact]
    public void AReadStateExposesEveryMember()
    {
        var state = ProcessState.FromJson(Waiting);

        state.InstanceId.Should().Be("i-1");
        state.DefinitionId.Should().Be("t.proc");
        state.DefinitionVersion.Should().Be(1);
        state.DefinitionHash.Should().Be("abc");
        state.Status.Should().Be(ProcessStatus.Waiting);
        state.CurrentStep.Should().Be("s");
        state.Stage.Should().Be("one");
        state.Result.Should().BeNull();
        state.Variables.Should().BeEquivalentTo(new Dictionary<string, string> { ["a"] = "1", ["b"] = "\"x\"" });
        state.LoopCounters.Should().BeEquivalentTo(new Dictionary<string, int> { ["l"] = 2 });
        state.Attempt.Should().Be(1);
        state.Entry.Should().Be(3);
        state.StepCount.Should().Be(3);
        state.Sequence.Should().Be(9);
        state.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Wait, "i-1/s/3/wait", "i-1/s/3/timeout"));
        state.Inbox.Should().Equal(new BufferedEvent("e", null, """{"x":1}"""), new BufferedEvent("f", "c-1", "{}"));
        ProcessState.FromJson(Suspended).Problem.Should().Be(new ProcessProblem("x.y", "s", "why"));
    }

    [Fact]
    public void VariablesAndCountersAreWrittenInOrdinalKeyOrderWhateverTheInputOrder()
    {
        var shuffled = Waiting.Replace("\"variables\":{\"a\":1,\"b\":\"x\"}", "\"variables\":{\"b\":\"x\",\"a\":1}", StringComparison.Ordinal);

        ProcessState.FromJson(shuffled).ToJson().Should().Be(Waiting);
    }

    [Fact]
    public void EveryStateOfTheReferenceScenariosRoundTrips()
    {
        var document = TestFiles.ReadJson(TestFiles.Fixtures + "outreach.process.json");
        var count = 0;

        foreach (var scenario in Player.Scenarios(document))
        {
            var player = new Player(TestFiles.OutreachProcess());
            player.Run(scenario);
            foreach (var state in player.States)
            {
                var json = state.ToJson();
                var back = ProcessState.FromJson(json);
                back.Should().Be(state);
                back.ToJson().Should().Be(json);
                count++;
            }
        }

        count.Should().BeGreaterThan(20);
    }

    [Fact]
    public void StatesWithAnInboxASuspensionAndLoopCountersRoundTripToo()
    {
        var steps = """
            {
              "s": { "kind": "loop", "maxIterations": 3, "forEach": "list", "item": "item", "body": "t", "onExhausted": "e", "next": "e" },
              "t": { "kind": "timer", "after": "PT1M", "next": "s" },
              "e": { "kind": "end", "result": "ok" }
            }
            """;
        var variables = """{ "list": { "type": "Json", "input": true }, "item": { "type": "Json" } }""";
        var process = Docs.Load(variables, steps, """ "accepts": ["ping"] """);
        var driver = new Driver(process);
        var states = new List<ProcessState>();
        states.Add(driver.Start("""{ "list": [1, { "b": 2, "a": 1 }] }""").State);
        states.Add(driver.Send(new EventReceived("ping", "k-1", """{ "z": 1, "a": 2 }""")).State);
        states.Add(driver.Send(new TimerFired("i-1/t/2/timer")).State);
        var other = new Driver(process);
        states.Add(other.Start().State);
        states.Add(other.Send(new ResumeRequested()).State);

        states.Select(state => state.Status).Should().Contain([ProcessStatus.Waiting, ProcessStatus.Suspended]);
        states.Last().Problem.Should().NotBeNull();
        driver.State.Inbox.Should().HaveCount(1);
        foreach (var state in states)
        {
            ProcessState.FromJson(state.ToJson()).Should().Be(state);
            ProcessState.FromJson(state.ToJson()).ToJson().Should().Be(state.ToJson());
        }
    }

    [Fact]
    public void FromJsonRejectsNull()
    {
        var act = () => ProcessState.FromJson(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    public void TextThatIsNotAnObjectIsMalformed(string text) => AssertMalformed(text);

    [Theory]
    [InlineData("extra", "1")]
    [InlineData("instanceId", "\"\"")]
    [InlineData("instanceId", "5")]
    [InlineData("definitionId", "null")]
    [InlineData("definitionVersion", "0")]
    [InlineData("definitionVersion", "\"1\"")]
    [InlineData("definitionVersion", "1.5")]
    [InlineData("definitionHash", "\"\"")]
    [InlineData("status", "\"Running\"")]
    [InlineData("status", "\"waiting\"")]
    [InlineData("status", "\"Nope\"")]
    [InlineData("status", "2")]
    [InlineData("currentStep", "5")]
    [InlineData("stage", "[]")]
    [InlineData("result", "{}")]
    [InlineData("variables", "[]")]
    [InlineData("variables", "{\"a\":null}")]
    [InlineData("loopCounters", "[]")]
    [InlineData("loopCounters", "{\"l\":-1}")]
    [InlineData("loopCounters", "{\"l\":\"1\"}")]
    [InlineData("loopCounters", "{\"l\":1.5}")]
    [InlineData("attempt", "-1")]
    [InlineData("attempt", "\"1\"")]
    [InlineData("entry", "-1")]
    [InlineData("stepCount", "-1")]
    [InlineData("sequence", "-1")]
    [InlineData("pending", "[]")]
    [InlineData("pending", "{\"kind\":\"Bogus\",\"id\":\"x\",\"timeoutTimerId\":null}")]
    [InlineData("pending", "{\"kind\":\"wait\",\"id\":\"x\",\"timeoutTimerId\":null}")]
    [InlineData("pending", "{\"kind\":\"Wait\",\"timeoutTimerId\":null}")]
    [InlineData("pending", "{\"kind\":\"Wait\",\"id\":\"x\",\"timeoutTimerId\":3}")]
    [InlineData("inbox", "{}")]
    [InlineData("inbox", "[1]")]
    [InlineData("inbox", "[{\"name\":\"e\",\"correlation\":null,\"payload\":[]}]")]
    [InlineData("inbox", "[{\"correlation\":null,\"payload\":{}}]")]
    [InlineData("problem", "[]")]
    [InlineData("problem", "{\"step\":\"s\",\"detail\":null}")]
    [InlineData("problem", "{\"code\":\"x\",\"detail\":null}")]
    public void AMemberOfTheWrongFormIsMalformed(string member, string value)
    {
        var node = JsonNode.Parse(Waiting)!.AsObject();
        node[member] = JsonNode.Parse(value);

        AssertMalformed(node.ToJsonString());
    }

    [Theory]
    [InlineData("instanceId")]
    [InlineData("definitionId")]
    [InlineData("definitionVersion")]
    [InlineData("definitionHash")]
    [InlineData("status")]
    [InlineData("currentStep")]
    [InlineData("stage")]
    [InlineData("result")]
    [InlineData("variables")]
    [InlineData("loopCounters")]
    [InlineData("attempt")]
    [InlineData("entry")]
    [InlineData("stepCount")]
    [InlineData("sequence")]
    [InlineData("pending")]
    [InlineData("inbox")]
    [InlineData("problem")]
    public void ARequiredMemberThatIsMissingIsMalformed(string member)
    {
        var node = JsonNode.Parse(Waiting)!.AsObject();
        node.Remove(member);

        AssertMalformed(node.ToJsonString());
    }

    [Fact]
    public void TheMalformedErrorNamesThePath()
    {
        var node = JsonNode.Parse(Waiting)!.AsObject();
        node["attempt"] = -1;

        var error = Record.Exception(() => ProcessState.FromJson(node.ToJsonString()));

        error.Should().BeOfType<DecisionEngineException>().Which.Message.Should().Contain("$.attempt");
    }

    [Fact]
    public void EqualityLooksAtEveryMember()
    {
        var state = ProcessState.FromJson(Waiting);
        var mutations = new (string Member, string Value)[]
        {
            ("instanceId", "\"i-2\""), ("definitionId", "\"t.other\""), ("definitionVersion", "2"), ("definitionHash", "\"abd\""),
            ("status", "\"Suspended\""), ("currentStep", "\"t\""), ("stage", "\"two\""), ("result", "\"r\""),
            ("variables", "{\"a\":1}"), ("variables", "{\"a\":2,\"b\":\"x\"}"), ("variables", "{\"a\":1,\"c\":\"x\"}"),
            ("loopCounters", "{\"l\":3}"), ("loopCounters", "{}"), ("attempt", "2"), ("entry", "4"), ("stepCount", "4"), ("sequence", "10"),
            ("pending", "null"), ("pending", "{\"kind\":\"Wait\",\"id\":\"i-1/s/3/wait\",\"timeoutTimerId\":null}"),
            ("inbox", "[]"), ("inbox", "[{\"name\":\"e\",\"correlation\":null,\"payload\":{\"x\":2}},{\"name\":\"f\",\"correlation\":\"c-1\",\"payload\":{}}]"),
            ("problem", "{\"code\":\"x\",\"step\":\"s\",\"detail\":null}"),
        };

        foreach (var (member, value) in mutations)
        {
            var node = JsonNode.Parse(Waiting)!.AsObject();
            node[member] = JsonNode.Parse(value);
            var changed = ProcessState.FromJson(node.ToJsonString());
            state.Equals(changed).Should().BeFalse(because: member);
            changed.Equals(state).Should().BeFalse(because: member);
        }
    }

    [Fact]
    public void EqualStatesHaveEqualHashCodesAndTheObjectOverloadAgrees()
    {
        var first = ProcessState.FromJson(Waiting);
        var second = ProcessState.FromJson(Waiting);

        first.Equals(second).Should().BeTrue();
        first.Equals((object)second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Equals(null).Should().BeFalse();
        first.Equals((object?)"text").Should().BeFalse();
        first.Equals(ProcessState.FromJson(Suspended)).Should().BeFalse();
    }

    private static void AssertMalformed(string text)
    {
        var error = Record.Exception(() => ProcessState.FromJson(text));

        error.Should().BeOfType<DecisionEngineException>().Which.ErrorCode.Should().Be("process.state.malformed");
    }
}

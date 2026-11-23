using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Section 4: Start.</summary>
public sealed class StartTests
{
    private const string EndOnly = """{ "s": { "kind": "end", "result": "ok" } }""";

    private const string AllTypes = """
        {
          "code": { "type": "Code", "input": true },
          "mode": { "type": "Enum", "allowedValues": ["a", "b"], "input": true },
          "flag": { "type": "Boolean", "input": true },
          "count": { "type": "Integer", "input": true },
          "ratio": { "type": "Decimal", "input": true },
          "price": { "type": "Money", "input": true },
          "day": { "type": "Date", "input": true },
          "owner": { "type": "Reference", "input": true },
          "moment": { "type": "Instant", "input": true },
          "blob": { "type": "Json", "input": true },
          "fixed": { "type": "Code", "initial": "start" },
          "label": { "type": "Code", "input": true, "initial": "start" }
        }
        """;

    [Fact]
    public void AnEndOnlyProcessCompletesAtOnce()
    {
        var process = Docs.Load("{}", EndOnly);

        var result = process.Start("i-1", null, Docs.Clock());

        result.Disposition.Should().Be(AdvanceDisposition.Applied);
        result.Code.Should().BeNull();
        result.State.Status.Should().Be(ProcessStatus.Completed);
        result.State.Result.Should().Be("ok");
        result.State.CurrentStep.Should().BeNull();
        result.State.StepCount.Should().Be(1);
        result.State.Sequence.Should().Be(3);
        result.History.Select(entry => entry.Kind).Should().Equal(ProcessHistoryKind.Started, ProcessHistoryKind.StepEntered, ProcessHistoryKind.Completed);
        result.History.Select(entry => entry.Sequence).Should().Equal(1, 2, 3);
        result.History.Should().OnlyContain(entry => entry.At == Docs.T0);
        result.Requests.Should().BeEmpty();
    }

    [Fact]
    public void StartingWithoutInputIsTheSameAsAnEmptyObject()
    {
        var process = Docs.Load("{}", EndOnly);

        var none = process.Start("i-1", null, Docs.Clock());
        var empty = process.Start("i-1", Docs.Input("{}"), Docs.Clock());

        none.State.ToJson().Should().Be(empty.State.ToJson());
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("/")]
    public void AnInvalidInstanceIdThrows(string id)
    {
        var process = Docs.Load("{}", EndOnly);

        var act = () => process.Start(id, null, Docs.Clock());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AllTypesAreStoredInTheirCanonicalForm()
    {
        var process = Docs.Load(AllTypes, EndOnly);
        var input = """
            {
              "code": "c-1", "mode": "b", "flag": true, "count": 7, "ratio": "1.50",
              "price": { "currency": "USD", "amount": "2.5" }, "day": "2027-01-04",
              "owner": "00000000-0000-0000-0000-00000000000A", "moment": "2027-01-04T10:00:00.750+02:00",
              "blob": { "z": 1, "a": [1, 2] }
            }
            """;

        var state = process.Start("i-1", Docs.Input(input), Docs.Clock()).State;

        state.Variables["code"].Should().Be("\"c-1\"");
        state.Variables["mode"].Should().Be("\"b\"");
        state.Variables["flag"].Should().Be("true");
        state.Variables["count"].Should().Be("7");
        state.Variables["ratio"].Should().Be("\"1.50\"");
        state.Variables["price"].Should().Be("{\"amount\":\"2.5\",\"currency\":\"USD\"}");
        state.Variables["day"].Should().Be("\"2027-01-04\"");
        state.Variables["owner"].Should().Be("\"00000000-0000-0000-0000-00000000000a\"");
        state.Variables["moment"].Should().Be("\"2027-01-04T08:00:00Z\"");
        state.Variables["blob"].Should().Be("{\"a\":[1,2],\"z\":1}");
        state.Variables.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void InitialValuesAreSetAndInputOverridesThem()
    {
        var process = Docs.Load(AllTypes, EndOnly);

        var state = process.Start("i-1", Docs.Input("""{ "label": "given" }"""), Docs.Clock()).State;
        var untouched = process.Start("i-2", null, Docs.Clock()).State;

        state.Variables["fixed"].Should().Be("\"start\"");
        state.Variables["label"].Should().Be("\"given\"");
        untouched.Variables["label"].Should().Be("\"start\"");
        untouched.Variables.Should().NotContainKey("code");
    }

    [Fact]
    public void ANullInputClearsTheVariable()
    {
        var process = Docs.Load(AllTypes, EndOnly);

        var state = process.Start("i-1", Docs.Input("""{ "label": null }"""), Docs.Clock()).State;

        state.Variables.Should().NotContainKey("label");
    }

    [Theory]
    [InlineData("""{ "fixed": "x" }""", "process.input.unknown")]
    [InlineData("""{ "nothing": "x" }""", "process.input.unknown")]
    [InlineData("""{ "count": "7" }""", "process.variable.type_mismatch")]
    [InlineData("""{ "count": 1.5 }""", "process.variable.type_mismatch")]
    [InlineData("""{ "count": 9007199254740992 }""", "process.variable.type_mismatch")]
    [InlineData("""{ "mode": "c" }""", "process.variable.type_mismatch")]
    [InlineData("""{ "flag": "true" }""", "process.variable.type_mismatch")]
    [InlineData("""{ "ratio": "1,5" }""", "process.variable.type_mismatch")]
    [InlineData("""{ "price": { "amount": "1", "currency": "usd" } }""", "process.variable.type_mismatch")]
    [InlineData("""{ "price": { "amount": "1" } }""", "process.variable.type_mismatch")]
    [InlineData("""{ "day": "2027-02-30" }""", "process.variable.type_mismatch")]
    [InlineData("""{ "owner": "not-a-guid" }""", "process.variable.type_mismatch")]
    [InlineData("""{ "moment": "2027-01-04 10:00:00" }""", "process.variable.type_mismatch")]
    [InlineData("""{ "moment": "2027-01-04T10:00:00" }""", "process.variable.type_mismatch")]
    [InlineData("""{ "code": 5 }""", "process.variable.type_mismatch")]
    public void RejectedInputLeavesACancelledStateWithNothingElse(string input, string code)
    {
        var process = Docs.Load(AllTypes, EndOnly);

        var result = process.Start("i-1", Docs.Input(input), Docs.Clock());

        result.Disposition.Should().Be(AdvanceDisposition.Rejected);
        result.Code.Should().Be(code);
        result.State.Status.Should().Be(ProcessStatus.Cancelled);
        result.State.InstanceId.Should().Be("i-1");
        result.State.DefinitionHash.Should().Be(process.ContentHash);
        result.Requests.Should().BeEmpty();
        result.History.Should().BeEmpty();
    }

    [Fact]
    public void ARejectedStartCannotBeAdvanced()
    {
        var process = Docs.Load(AllTypes, EndOnly);
        var rejected = process.Start("i-1", Docs.Input("""{ "fixed": "x" }"""), Docs.Clock());

        var next = process.Advance(rejected.State, new ResumeRequested(), Docs.Clock());

        next.Disposition.Should().Be(AdvanceDisposition.Ignored);
        next.Code.Should().Be("process.trigger.stale");
    }

    [Fact]
    public void TheFirstStepKeepsTheStageOfTheStepAndStartsAtEntryOne()
    {
        var steps = """{ "s": { "kind": "read", "port": "p", "stage": "two", "next": "e" }, "e": { "kind": "end", "result": "ok" } }""";
        var process = Docs.Load("{}", steps);

        var result = process.Start("i-1", null, Docs.Clock());

        result.State.Stage.Should().Be("two");
        result.State.Entry.Should().Be(1);
        result.State.Attempt.Should().Be(1);
        result.State.CurrentStep.Should().Be("s");
        result.State.Status.Should().Be(ProcessStatus.Waiting);
        result.State.Pending.Should().Be(new ProcessPending(ProcessPendingKind.Effect, "i-1/s/1/1"));
        result.Requests.Should().ContainSingle().Which.Should().Be(new EffectRequest("s", "i-1/s/1/1", "i-1/s/1", ProcessEffectKind.Read, "p", "{}", 1, ProcessDelivery.AtLeastOnce));
    }

    [Fact]
    public void AStepWithoutAStageKeepsTheStageOfTheStepBefore()
    {
        var steps = """
            {
              "s": { "kind": "read", "port": "p", "stage": "one", "next": "t" },
              "t": { "kind": "read", "port": "q", "next": "e" },
              "e": { "kind": "end", "result": "ok" }
            }
            """;
        var process = Docs.Load("{}", steps);
        var driver = new Driver(process);
        driver.Start();

        driver.Send(new EffectCompleted("i-1/s/1/1", "{}"));

        driver.State.Stage.Should().Be("one");
        driver.State.CurrentStep.Should().Be("t");
    }
}

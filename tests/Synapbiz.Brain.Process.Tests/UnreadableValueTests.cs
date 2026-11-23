using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>
/// Text that no host can store or read back, and the interpreter's answer to it: nothing throws and nothing is stored that
/// a later pass could not apply (sections 10.1 and 10.4 of the W1-C specification).
/// </summary>
public sealed class UnreadableValueTests
{
    private const string Variables = """
        {
          "case_ref": { "type": "Code", "input": true },
          "note": { "type": "Code" }
        }
        """;

    private const string Wait = """
        { "s": { "kind": "wait", "event": "form_submitted", "correlation": "case_ref", "output": { "note": "note" }, "next": "ok" },
          "ok": { "kind": "end", "result": "ok" } }
        """;

    private static Driver Started(string input)
    {
        var driver = new Driver(Docs.Load(Variables, Wait, "\"accepts\": [\"other\"]"));
        driver.Start(input);
        return driver;
    }

    [Fact]
    public void ACorrelationWithTheCharacterU0000SuspendsTheStepInsteadOfWaitingForAnEventNothingCouldMatch()
    {
        var driver = Started("""{ "case_ref": "a\u0000b" }""");

        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem.Should().Be(new ProcessProblem("process.wait.correlation_invalid", "s"));
        driver.Last.Requests.Should().BeEmpty();
        driver.State.Pending.Should().BeNull();
    }

    [Fact]
    public void ACorrelationWithOtherControlCharactersOrTextThatIsUnusualButStorableStillWaits()
    {
        var driver = Started("""{ "case_ref": "tab\there é😀" }""");

        driver.State.Status.Should().Be(ProcessStatus.Waiting);
        driver.Single<WaitRequest>().Correlation.Should().Be("tab\there é😀");
    }

    [Fact]
    public void AnInputStringWithAnUnpairedSurrogateIsNoStringAndTheStartIsRejectedWithATypeMismatch()
    {
        var driver = new Driver(Docs.Load(Variables, Wait));

        var result = driver.Start("""{ "case_ref": "\ud800" }""");

        result.Disposition.Should().Be(AdvanceDisposition.Rejected);
        result.Code.Should().Be("process.variable.type_mismatch");
    }

    [Theory]
    [InlineData("""{ "note": "\ud800" }""")]
    [InlineData("""{ "note": "x", "\udc00": 1 }""")]
    [InlineData("""{ "note": "\udc00\ud800" }""")]
    public void AnEventPayloadWithAnUnpairedSurrogateIsMalformedAndSuspendsTheWaitingInstanceWithoutThrowing(string payload)
    {
        var driver = Started("""{ "case_ref": "c-1" }""");

        var result = driver.Send(new EventReceived("form_submitted", "c-1", payload));

        result.Disposition.Should().Be(AdvanceDisposition.Applied);
        driver.State.Status.Should().Be(ProcessStatus.Suspended);
        driver.State.Problem!.Code.Should().Be("process.variable.type_mismatch");
    }

    [Fact]
    public void AnEventPayloadWithAnUnpairedSurrogateThatNoStepWaitsForIsRejectedWithoutThrowing()
    {
        var driver = Started("""{ "case_ref": "c-1" }""");

        var result = driver.Send(new EventReceived("other", "c-1", """{ "note": "\ud800" }"""));

        result.Disposition.Should().Be(AdvanceDisposition.Rejected);
        result.Code.Should().Be("process.variable.type_mismatch");
        driver.State.Status.Should().Be(ProcessStatus.Waiting);
    }

    [Fact]
    public void AnEventPayloadWithU0000InAValueIsReadAsAnyOtherString()
    {
        var driver = Started("""{ "case_ref": "c-1" }""");

        driver.Send(new EventReceived("form_submitted", "c-1", """{ "note": "a\u0000b" }"""));

        driver.State.Status.Should().Be(ProcessStatus.Completed);
        driver.State.Variables["note"].Should().Be("\"a\\u0000b\"");
    }
}

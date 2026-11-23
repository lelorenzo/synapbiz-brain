using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Section 5: the checks of Advance, in order, and stale or duplicate triggers.</summary>
public sealed class GuardTests
{
    private const string Steps = """
        {
          "s": { "kind": "read", "port": "p", "output": { "n": "count" }, "next": "t" },
          "t": { "kind": "timer", "after": "PT1H", "next": "e" },
          "e": { "kind": "end", "result": "ok" }
        }
        """;

    private const string Variables = """{ "count": { "type": "Integer" } }""";

    [Fact]
    public void ANullStateOrTriggerThrows()
    {
        var process = Docs.Load(Variables, Steps);
        var state = process.Start("i-1", null, Docs.Clock()).State;

        var noState = () => process.Advance(null!, new ResumeRequested(), Docs.Clock());
        var noTrigger = () => process.Advance(state, null!, Docs.Clock());

        noState.Should().Throw<ArgumentNullException>();
        noTrigger.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AStateOfAnotherDefinitionIsRejectedAndLeftAsIs()
    {
        var process = Docs.Load(Variables, Steps);
        var other = Docs.Load(Variables, Steps.Replace("PT1H", "PT2H", StringComparison.Ordinal));
        var foreign = other.Start("i-1", null, Docs.Clock()).State;

        var result = process.Advance(foreign, new EffectCompleted("i-1/s/1/1", "{}"), Docs.Clock());

        result.Disposition.Should().Be(AdvanceDisposition.Rejected);
        result.Code.Should().Be("process.definition.mismatch");
        result.State.Should().BeSameAs(foreign);
        result.Requests.Should().BeEmpty();
        result.History.Should().BeEmpty();
    }

    [Fact]
    public void ADifferentVersionOrIdIsAlsoAMismatch()
    {
        var process = Docs.Load(Variables, Steps);
        var state = process.Start("i-1", null, Docs.Clock()).State;
        var json = state.ToJson();

        var otherVersion = ProcessState.FromJson(json.Replace("\"definitionVersion\":1", "\"definitionVersion\":2", StringComparison.Ordinal));
        var otherId = ProcessState.FromJson(json.Replace("\"definitionId\":\"t.proc\"", "\"definitionId\":\"t.other\"", StringComparison.Ordinal));

        process.Advance(otherVersion, new ResumeRequested(), Docs.Clock()).Code.Should().Be("process.definition.mismatch");
        process.Advance(otherId, new ResumeRequested(), Docs.Clock()).Code.Should().Be("process.definition.mismatch");
    }

    [Fact]
    public void ARedeliveredResultIsIgnoredAndChangesNothing()
    {
        var process = Docs.Load(Variables, Steps);
        var driver = new Driver(process);
        driver.Start();
        driver.Send(new EffectCompleted("i-1/s/1/1", """{ "n": 3 }"""));
        var before = driver.State;

        var again = driver.Send(new EffectCompleted("i-1/s/1/1", """{ "n": 9 }"""));

        again.Disposition.Should().Be(AdvanceDisposition.Ignored);
        again.Code.Should().Be("process.trigger.stale");
        again.State.Should().BeSameAs(before);
        again.Requests.Should().BeEmpty();
        again.History.Should().BeEmpty();
        driver.Variable("count").Should().Be("3");
    }

    [Theory]
    [InlineData("effect-other")]
    [InlineData("i-1/s/2/1")]
    [InlineData("i-1/s/1/2")]
    public void AnEffectResultForAnotherEffectIsIgnored(string id)
    {
        var process = Docs.Load(Variables, Steps);
        var driver = new Driver(process);
        driver.Start();

        driver.Send(new EffectCompleted(id, "{}")).Code.Should().Be("process.trigger.stale");
        driver.Send(new EffectFailed(id, ProcessErrorKind.Permanent, "x")).Code.Should().Be("process.trigger.stale");
    }

    [Fact]
    public void ATriggerOfTheWrongKindForThePendingWorkIsIgnored()
    {
        var process = Docs.Load(Variables, Steps);
        var driver = new Driver(process);
        driver.Start();

        var results = new ProcessTrigger[]
        {
            new TimerFired("i-1/s/1/timer"),
            new TaskCompleted("i-1/s/1/task", "x", "{}"),
            new ChildCompleted("i-1/s/1/child", "done", "{}"),
            new ChildFailed("i-1/s/1/child", "x"),
        };

        foreach (var trigger in results)
        {
            driver.Send(trigger).Disposition.Should().Be(AdvanceDisposition.Ignored);
        }

        driver.State.Status.Should().Be(ProcessStatus.Waiting);
    }

    [Fact]
    public void AnEffectResultWhileATimerIsPendingIsIgnored()
    {
        var process = Docs.Load(Variables, Steps);
        var driver = new Driver(process);
        driver.Start();
        driver.Send(new EffectCompleted("i-1/s/1/1", "{}"));

        driver.Send(new EffectCompleted("i-1/s/1/1", "{}")).Code.Should().Be("process.trigger.stale");
        driver.Send(new TimerFired("nope")).Code.Should().Be("process.trigger.stale");
        driver.Send(new TimerFired("i-1/t/2/timer")).Disposition.Should().Be(AdvanceDisposition.Applied);
    }

    [Fact]
    public void ACompletedInstanceIgnoresEveryTrigger()
    {
        var process = Docs.Load("{}", """{ "s": { "kind": "end", "result": "ok" } }""");
        var driver = new Driver(process);
        driver.Start();

        var triggers = new ProcessTrigger[]
        {
            new EffectCompleted("x", "{}"),
            new TimerFired("x"),
            new EventReceived("e", null, "{}"),
            new CancelRequested("late"),
            new ResumeRequested(),
        };

        foreach (var trigger in triggers)
        {
            var result = driver.Send(trigger);
            result.Disposition.Should().Be(AdvanceDisposition.Ignored);
            result.Code.Should().Be("process.trigger.stale");
            result.State.Status.Should().Be(ProcessStatus.Completed);
        }
    }

    [Fact]
    public void ASuspendedInstanceIgnoresResultsButNotCancelOrResume()
    {
        var steps = """{ "s": { "kind": "read", "port": "p", "next": "e" }, "e": { "kind": "end", "result": "ok" } }""";
        var process = Docs.Load("{}", steps);
        var driver = new Driver(process);
        driver.Start();
        driver.Send(new EffectFailed("i-1/s/1/1", ProcessErrorKind.Permanent, "port.broken"));
        driver.State.Status.Should().Be(ProcessStatus.Suspended);

        driver.Send(new EffectCompleted("i-1/s/1/1", "{}")).Code.Should().Be("process.trigger.stale");
        driver.Send(new TimerFired("x")).Code.Should().Be("process.trigger.stale");
        driver.Send(new CancelRequested("stop")).Disposition.Should().Be(AdvanceDisposition.Applied);
        driver.State.Status.Should().Be(ProcessStatus.Cancelled);
    }

    [Fact]
    public void ResumeWhileWaitingIsIgnored()
    {
        var process = Docs.Load(Variables, Steps);
        var driver = new Driver(process);
        driver.Start();

        var result = driver.Send(new ResumeRequested());

        result.Disposition.Should().Be(AdvanceDisposition.Ignored);
        result.Code.Should().Be("process.trigger.stale");
    }

    [Fact]
    public void ACancelledInstanceIgnoresASecondCancel()
    {
        var process = Docs.Load(Variables, Steps);
        var driver = new Driver(process);
        driver.Start();
        driver.Send(new CancelRequested("first"));

        var second = driver.Send(new CancelRequested("second"));

        second.Disposition.Should().Be(AdvanceDisposition.Ignored);
        second.Code.Should().Be("process.trigger.stale");
    }

    [Fact]
    public void ThePublicMembersReadBackTheDefinition()
    {
        var process = Docs.Load(Variables, Steps);

        process.Id.Should().Be("t.proc");
        process.Version.Should().Be(1);
        process.Subject.Should().Be("case");
        process.Stages.Should().Equal("one", "two");
        process.StepIds.Should().Equal("s", "t", "e");
        process.ContentHash.Should().NotBeNullOrEmpty();
        process.Meta.Should().NotBeSameAs(process.Meta);
    }
}

using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class FlowTrySourceTests
{
    private const string Scenario = "nothing is missing";

    private static IElement AskButton(FlowHarness harness, string name) =>
        harness.Buttons.First(button => FlowHarness.Normalise(button.TextContent) == "Ask " + name);

    private sealed class Named(string name, Func<FlowEffectRequest, CancellationToken, ValueTask<FlowAnswer?>> answer) : IFlowAnswerSource
    {
        public string Name => name;

        public ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken) => answer(request, cancellationToken);
    }

    private static FlowHarness Open(IFlowAnswerSource? source)
    {
        var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference(), source: source);
        harness.Start("previsit.outreach", Scenario);
        return harness;
    }

    [Fact]
    public void WithoutASourceThereIsNoAskButton()
    {
        using var harness = Open(null);
        harness.Buttons.Select(button => FlowHarness.Normalise(button.TextContent)).Should().NotContain(text => text.StartsWith("Ask ", StringComparison.Ordinal));
    }

    [Fact]
    public void ASourceAnswersTheEffectAndTheTimelineNamesIt()
    {
        var source = new Named("Practice AI", (_, _) => ValueTask.FromResult<FlowAnswer?>(FlowAnswer.Output("""{"missing_count":0}""")));
        using var harness = Open(source);
        AskButton(harness, "Practice AI").Click();
        harness.PanelText.Should().Contain("Answer at Check what is missing").And.Contain("Practice AI").And.Contain("The case has finished.");
        harness.Find(".wb-sr[role=status]").TextContent.Should().StartWith("The case finished");
    }

    [Fact]
    public void ASourceThatDoesNotAnswerLeavesTheEffectForAManualAnswerAndSaysSo()
    {
        var source = new Named("Practice AI", (_, _) => ValueTask.FromResult<FlowAnswer?>(null));
        using var harness = Open(source);
        AskButton(harness, "Practice AI").Click();
        harness.Find(".wb-sr[role=status]").TextContent.Should().Be("Practice AI gave no answer");
        harness.PanelText.Should().Contain("Waiting for an answer at");
    }

    [Fact]
    public void ASourceThatThrowsIsRefusedInWordsAndTheCaseStaysUsable()
    {
        var source = new Named("Practice AI", (_, _) => throw new InvalidOperationException("boom"));
        using var harness = Open(source);
        AskButton(harness, "Practice AI").Click();
        harness.PanelText.Should().Contain("the answer source failed").And.Contain("Refused");
        harness.Find(".wb-sr[role=status]").TextContent.Should().Contain("the answer source failed");
        harness.PanelText.Should().Contain("Waiting for an answer at");
        harness.Buttons.Where(button => FlowHarness.Normalise(button.TextContent).StartsWith("Answer like the test", StringComparison.Ordinal)).Should().NotBeEmpty();
    }

    [Fact]
    public void ASourceAnswerThatIsNotJsonIsRefusedWithItsReason()
    {
        var source = new Named("Practice AI", (_, _) => ValueTask.FromResult<FlowAnswer?>(FlowAnswer.Output("not json")));
        using var harness = Open(source);
        AskButton(harness, "Practice AI").Click();
        harness.PanelText.Should().Contain("Refused");
    }

    [Fact]
    public void WhileAskingTheOtherButtonsAreOffAndTheReaderCanCancel()
    {
        var gate = new TaskCompletionSource<FlowAnswer?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Named("Practice AI", async (_, token) =>
        {
            await using var registration = token.Register(() => gate.TrySetCanceled(token));
            return await gate.Task;
        });
        using var harness = Open(source);
        AskButton(harness, "Practice AI").Click();
        harness.Cut.WaitForAssertion(() => harness.PanelText.Should().Contain("Asking Practice AI…"));
        harness.Find(".wb-busy").GetAttribute("role").Should().Be("status");
        harness.Buttons.Where(button => FlowHarness.Normalise(button.TextContent).StartsWith("Answer like the test", StringComparison.Ordinal))
            .Should().OnlyContain(button => button.HasAttribute("disabled"));
        harness.Find("[role=tabpanel] .wb-sr[role=status]").TextContent.Should().Be("Asking Practice AI");

        harness.Press("Cancel");
        harness.Cut.WaitForAssertion(() => harness.Find("[role=tabpanel] .wb-sr[role=status]").TextContent.Should().Be("The question to Practice AI was cancelled"));
        harness.FindAll(".wb-busy").Should().BeEmpty();
        harness.PanelText.Should().Contain("Waiting for an answer at");
    }

    [Fact]
    public void LeavingTheFlowWhileAskingCancelsTheQuestionWithoutAFault()
    {
        var gate = new TaskCompletionSource<FlowAnswer?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Named("Practice AI", async (_, token) =>
        {
            await using var registration = token.Register(() => gate.TrySetCanceled(token));
            return await gate.Task;
        });
        var harness = Open(source);
        AskButton(harness, "Practice AI").Click();
        harness.Cut.WaitForAssertion(() => harness.PanelText.Should().Contain("Asking Practice AI…"));
        harness.Select("visit.note_wait");
        harness.Cut.WaitForAssertion(() => harness.FindAll(".wb-busy").Should().BeEmpty());
        harness.Dispose();
    }
}

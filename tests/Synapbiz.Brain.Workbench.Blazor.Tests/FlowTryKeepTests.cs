using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class FlowTryKeepTests
{
    private const string Done = "nothing is missing";

    private static FlowHarness Finished()
    {
        var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Start("previsit.outreach", Done);
        harness.UseAnswer("nothing is missing");
        return harness;
    }

    [Fact]
    public void ATryOutIsSavedAsAProposedTestWhichThenPassesOnTheTestsTab()
    {
        using var harness = Finished();
        harness.Type("Name of the proposed test", "my first try");
        harness.Press("Save as a proposed test");
        harness.Find("[role=tabpanel] .wb-sr[role=status]").TextContent.Should().Be("The proposed test my first try was kept");
        harness.PanelText.Should().Contain("There is 1 proposed test in this review.");
        harness.Input("Name of the proposed test").GetAttribute("value").Should().BeEmpty();

        harness.Open(FlowWorkbenchTab.Tests);
        var proposed = harness.FindAll("li.wb-scenario").First(item => item.TextContent.Contains("my first try", StringComparison.Ordinal));
        proposed.TextContent.Should().Contain("Proposed").And.Contain("Passes");
        harness.Session.UnexportedFlowCount.Should().Be(1);
    }

    [Fact]
    public void ANameIsRequiredAndMustBeNewAndTheNameFieldTakesFocusWithTheError()
    {
        using var harness = Finished();
        var focus = harness.FocusCount;
        harness.Press("Save as a proposed test");
        harness.PanelText.Should().Contain("Write a name for the test.");
        harness.Input("Name of the proposed test").GetAttribute("aria-invalid").Should().Be("true");
        harness.FocusCount.Should().BeGreaterThan(focus);

        harness.Type("Name of the proposed test", Done);
        harness.Press("Save as a proposed test");
        harness.PanelText.Should().Contain("There is already a test with that name.");

        harness.Type("Name of the proposed test", "mine");
        harness.Press("Save as a proposed test");
        harness.Type("Name of the proposed test", "mine");
        harness.Press("Save as a proposed test");
        harness.PanelText.Should().Contain("There is already a test with that name.");
    }

    [Fact]
    public void StartOverAsksFirstAndKeepingItChangesNothing()
    {
        using var harness = Finished();
        harness.Press("Start over");
        harness.Find(".wb-confirm").TextContent.Should().Contain("This case will be discarded.");
        harness.Find(".wb-confirm").GetAttribute("role").Should().Be("alert");
        harness.Press("No, keep it");
        harness.FindAll(".wb-confirm").Should().BeEmpty();
        harness.PanelText.Should().Contain("Where the case is");
    }

    [Fact]
    public void ConfirmingStartOverDiscardsTheTryOutAndBringsTheFormBack()
    {
        using var harness = Finished();
        harness.Press("Start over");
        var focus = harness.FocusCount;
        harness.Press("Yes, discard it");
        harness.PanelText.Should().Contain("Start a made-up case").And.NotContain("Where the case is");
        harness.FocusCount.Should().BeGreaterThan(focus);
    }

    [Fact]
    public void AProposedTestCanBeRemovedAndTheSessionThenHasNothingToExport()
    {
        using var harness = Finished();
        harness.Type("Name of the proposed test", "to remove");
        harness.Press("Save as a proposed test");
        harness.Open(FlowWorkbenchTab.Tests);
        harness.Press("Remove \u201Cto remove\u201D");
        harness.FindAll("li.wb-scenario").Should().NotContain(item => item.TextContent.Contains("to remove", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplayStartsATestStepByStepInTheSimulateTabAndTheReaderMayPlayToTheEndAndContinueByHand()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Select("previsit.outreach");
        harness.Open(FlowWorkbenchTab.Tests);
        harness.Press("Replay \u201Cnothing is missing\u201D");
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Try);
        harness.Find("[role=tabpanel] .wb-sr[role=status]").TextContent.Should().Be("The test \u201Cnothing is missing\u201D was replayed");
        harness.PanelText.Should().Contain("Replaying the test \u201Cnothing is missing\u201D.").And.Contain("0 of 1 actions of the test done.");
        harness.PanelText.Should().NotContain("The case has finished.");
        harness.HasButton("Next step").Should().BeTrue();
        harness.HasButton("Play to the end").Should().BeTrue();

        harness.Press("Next step");
        harness.PanelText.Should().Contain("The case has finished.");
        harness.HasButton("Next step").Should().BeFalse();
        harness.PanelText.Should().NotContain("Replaying the test");
        var process = FlowDocs.Process("previsit.outreach");
        var replay = FlowSimulation.Replay(process, "nothing is missing");
        harness.PanelText.Should().Contain(FlowWords.For(process, WorkbenchLocale.En).Result(replay.State.Result!));
    }

    [Fact]
    public void PlayToTheEndPlaysEveryCommandAtOnceAndAReplayThatStopsMidWayLetsTheReaderGoOnByHand()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Select("previsit.outreach");
        harness.Open(FlowWorkbenchTab.Tests);
        var hands = "a case that came without its zone is suspended at the reminder, never failed and never routed";
        harness.Press("Replay \u201C" + hands + "\u201D");
        harness.Press("Play to the end");
        harness.HasButton("Next step").Should().BeFalse();
        harness.PanelText.Should().Contain("The case is suspended.");
        harness.HasButton("Resume").Should().BeTrue();
    }

    [Fact]
    public void WhileAReplayHasCommandsLeftTheCardsAreReadOnly()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Select("previsit.outreach");
        harness.Open(FlowWorkbenchTab.Tests);
        harness.Press("Replay \u201Ca reply after the first reminder closes the stage\u201D");
        var cards = harness.FindAll("section.wb-wait");
        cards.Should().NotBeEmpty();
        foreach (var control in cards.SelectMany(card => card.QuerySelectorAll("button, select, textarea, input")))
        {
            control.HasAttribute("disabled").Should().BeTrue(control.OuterHtml);
        }

        harness.FindAll("section.wb-card button, .wb-card button").Where(button => button.TextContent.Contains("1 day", StringComparison.Ordinal)).Should().OnlyContain(button => button.HasAttribute("disabled"));
        while (harness.HasButton("Next step"))
        {
            harness.Press("Next step");
        }

        harness.PanelText.Should().NotContain("Replaying the test");
    }

    [Fact]
    public void TheChildOfACaseCanBeFinishedWithAResultFromTheChildsOwnList()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Start("case.lifecycle", FlowDocs.Process("case.lifecycle").RunScenarios().Results[0].Name);
        for (var turn = 0; turn < 20 && !harness.PanelText.Contains("Waiting for another flow", StringComparison.Ordinal); turn++)
        {
            Next(harness);
        }

        var card = harness.FindAll("section.wb-wait").First(item => item.TextContent.Contains("Waiting for the result of the flow", StringComparison.Ordinal));
        var select = card.QuerySelector("select")!;
        select.QuerySelectorAll("option").Should().NotBeEmpty();
        var before = harness.PanelText;
        harness.Press("Finish the child flow with this result");
        harness.PanelText.Should().NotBe(before).And.Contain("The child flow of");
    }

    [Fact]
    public void OpeningTheChildLetsTheReaderTryItAndBringItsResultBack()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Start("case.lifecycle", FlowDocs.Process("case.lifecycle").RunScenarios().Results[0].Name);
        for (var turn = 0; turn < 20 && !harness.PanelText.Contains("Waiting for another flow", StringComparison.Ordinal); turn++)
        {
            Next(harness);
        }

        harness.Press("Open the child flow");
        harness.PanelText.Should().Contain("You are simulating the child flow");
        harness.HasButton("Back to the parent flow").Should().BeTrue();
        harness.HasButton("Bring its result to the parent flow").Should().BeFalse();

        for (var turn = 0; turn < 40 && !harness.PanelText.Contains("The case has finished.", StringComparison.Ordinal); turn++)
        {
            Next(harness);
        }

        harness.PanelText.Should().Contain("The case has finished.");
        harness.Press("Bring its result to the parent flow");
        harness.PanelText.Should().NotContain("You are simulating the child flow").And.Contain("The child flow of");
    }

    [Fact]
    public void GoingBackToTheParentKeepsTheChildCaseWaitingForItsResult()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Start("case.lifecycle", FlowDocs.Process("case.lifecycle").RunScenarios().Results[0].Name);
        for (var turn = 0; turn < 20 && !harness.PanelText.Contains("Waiting for another flow", StringComparison.Ordinal); turn++)
        {
            Next(harness);
        }

        harness.Press("Open the child flow");
        harness.Press("Back to the parent flow");
        harness.PanelText.Should().NotContain("You are simulating the child flow").And.Contain("Waiting for another flow");
    }

    [Fact]
    public void ACaseThatIsSuspendedCanBeResumed()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Start("previsit.outreach", "a case that came without its zone is suspended at the reminder, never failed and never routed");
        for (var turn = 0; turn < 30 && !harness.PanelText.Contains("The case is suspended.", StringComparison.Ordinal) && !harness.PanelText.Contains("The case has finished.", StringComparison.Ordinal); turn++)
        {
            Next(harness, "a case that came without its zone is suspended at the reminder, never failed and never routed");
        }

        harness.PanelText.Should().Contain("The case is suspended.");
        harness.Press("Resume");
        harness.PanelText.Should().Contain("The case was resumed");
    }

    private static void Next(FlowHarness harness, string? scenario = null)
    {
        foreach (var card in harness.FindAll("section.wb-wait"))
        {
            var heading = FlowHarness.Normalise(card.QuerySelector("h3")!.TextContent);
            IElement? Find(string start) => card.QuerySelectorAll("button").FirstOrDefault(button => FlowHarness.Normalise(button.TextContent).StartsWith(start, StringComparison.Ordinal));
            if (heading.StartsWith("Waiting for an answer", StringComparison.Ordinal) && harness.UseAnswer(scenario))
            {
                return;
            }

            var button = heading switch
            {
                _ when heading.StartsWith("Waiting for an answer", StringComparison.Ordinal) => Find("It fails for good"),
                _ when heading.StartsWith("A person must act", StringComparison.Ordinal) => card.QuerySelectorAll(".wb-actions button").FirstOrDefault(),
                _ when heading.StartsWith("Waiting for an event", StringComparison.Ordinal) => Find("The event arrives"),
                _ when heading.StartsWith("Waiting for another flow", StringComparison.Ordinal) => Find("Finish the child flow"),
                _ => Find("Go to that moment"),
            };
            if (button is not null)
            {
                button.Click();
                return;
            }
        }

        harness.Press("1 day");
    }
}

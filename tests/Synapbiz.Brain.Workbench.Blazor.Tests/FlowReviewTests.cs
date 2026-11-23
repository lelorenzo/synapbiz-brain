using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class FlowReviewTests
{
    private static FlowHarness Open(string id = "previsit.outreach")
    {
        var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Select(id);
        return harness;
    }

    [Fact]
    public void AMarkSetInReadAppearsInTheDiagramCardAndTheReviewTabAsOneState()
    {
        using var harness = Open();
        var step = FlowDescriber.Describe(FlowDocs.Process("previsit.outreach"), WorkbenchLocale.En).Steps[0];
        harness.Find("#" + harness.Find("section.wb-step").Id + " .wb-rv button").Click();
        harness.Find("section.wb-step .wb-rv button[aria-pressed=true]").TextContent.Should().Be("Confirmed");

        harness.Open(FlowWorkbenchTab.Diagram);
        harness.Session.SelectedStepId = step.Id;
        harness.Find("aside.wb-diagram-card .wb-rv button[aria-pressed=true]").TextContent.Should().Be("Confirmed");

        harness.Open(FlowWorkbenchTab.Review);
        harness.PanelText.Should().Contain("Step " + step.Label).And.Contain("Confirmed");
        harness.PanelText.Should().Contain("1 of ");
    }

    [Fact]
    public void PressingTheSameMarkAgainTakesItOffAndAFixOrAQuestionOpensItsNote()
    {
        using var harness = Open();
        var buttons = harness.Find("section.wb-step").QuerySelectorAll(".wb-rv button");
        buttons[0].Click();
        harness.Find("section.wb-step .wb-rv button[aria-pressed=true]").TextContent.Should().Be("Confirmed");
        harness.Find("section.wb-step").QuerySelectorAll(".wb-rv button")[0].Click();
        harness.FindAll("section.wb-step .wb-rv button[aria-pressed=true]").Should().BeEmpty();

        harness.Find("section.wb-step").QuerySelectorAll(".wb-rv button")[1].Click();
        harness.Find("section.wb-step").QuerySelector("textarea")!.Should().NotBeNull();
        harness.Find("section.wb-step .wb-rv button[aria-pressed=true]").TextContent.Should().Be("To fix");
    }

    [Fact]
    public void AFixOrAQuestionNeedsItsNoteBeforeExportAndTheNoteIsWrittenFromTheReviewTab()
    {
        using var harness = Open();
        harness.Session.Reviewer = "Sam";
        harness.Find("section.wb-step").QuerySelectorAll(".wb-rv button")[2].Click();
        harness.Open(FlowWorkbenchTab.Review);
        harness.PanelText.Should().Contain("The note is missing.");
        harness.Press("Export the review");
        harness.Exports.Should().BeEmpty();
        harness.Find("[role=alert]").TextContent.Should().Contain("1 item to fix or with a question needs its note");

        harness.Press("Write the note");
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Read);
        harness.Find("section.wb-step textarea").Change("Why this wait?");
        harness.Open(FlowWorkbenchTab.Review);
        harness.PanelText.Should().Contain("Why this wait?").And.NotContain("The note is missing.");
        harness.Press("Export the review");
        harness.Exports.Should().HaveCount(1);
    }

    [Fact]
    public void ExportingWithoutANameFocusesTheNameFieldAndSendsNothing()
    {
        using var harness = Open();
        harness.Find("section.wb-step .wb-rv button").Click();
        harness.Open(FlowWorkbenchTab.Review);
        var focus = harness.FocusCount;
        harness.Press("Export the review");
        harness.Exports.Should().BeEmpty();
        harness.PanelText.Should().Contain("Write your name to export.");
        harness.Input("Your name").GetAttribute("aria-invalid").Should().Be("true");
        harness.FocusCount.Should().BeGreaterThan(focus);
        harness.Type("Your name", "Sam");
        harness.Press("Export the review");
        harness.Exports.Should().HaveCount(1);
    }

    [Fact]
    public void NothingToExportDisablesTheButtonAndSaysSo()
    {
        using var harness = Open();
        harness.Open(FlowWorkbenchTab.Review);
        harness.ButtonByText("Export the review").HasAttribute("disabled").Should().BeTrue();
        harness.PanelText.Should().Contain("There is nothing to export").And.Contain("You have marked nothing yet.");
    }

    [Fact]
    public void TheExportEqualsTheSerializedReviewOfTheSessionAndParsesBackAndChecksCleanAgainstTheFlow()
    {
        using var harness = Open();
        harness.Find("section.wb-flow-stage .wb-rv button").Click();
        harness.Find("section.wb-step").QuerySelectorAll(".wb-rv button")[0].Click();
        harness.Open(FlowWorkbenchTab.Try);
        harness.Type("Start like the test", "nothing is missing");
        harness.Press("Start");
        harness.FindAll("section.wb-wait")[0].QuerySelectorAll("button").First(button => button.TextContent.Contains("Answer like the test nothing is missing", StringComparison.Ordinal)).Click();
        harness.Type("Name of the proposed test", "kept one");
        harness.Press("Save as a proposed test");
        harness.Open(FlowWorkbenchTab.Review);
        harness.Type("Your name", "Sam");
        harness.Press("Export the review");

        var export = harness.Exports.Should().ContainSingle().Subject;
        var file = export.Files.Should().ContainSingle().Subject;
        var process = FlowDocs.Process("previsit.outreach");
        file.SuggestedName.Should().Be("review-previsit.outreach-v" + ProcessOutline.From(process).Version + ".json");
        FlowReviewJson.Serialize(file.Review).Should().Be(file.Json);
        var parsed = FlowReviewJson.Parse(file.Json);
        parsed.IsValid.Should().BeTrue();
        parsed.Review!.Reviewer.Should().Be("Sam");
        parsed.Review.CreatedAt.Should().Be(Fx.Moment);
        parsed.Review.Language.Should().Be(WorkbenchLocale.En);
        parsed.Review.Items.Should().HaveCountGreaterThan(1);
        parsed.Review.ProposedScenarios.Should().ContainSingle().Which.Name.Should().Be("kept one");
        FlowReviewCheck.Against(parsed.Review, process).IsClean.Should().BeTrue();
        FlowReviewCheck.Against(parsed.Review, process).Scenarios.Should().ContainSingle().Which.Passed.Should().BeTrue();
    }

    [Fact]
    public void AfterAnExportNothingIsUnexportedUntilTheReaderChangesSomething()
    {
        using var harness = Open();
        harness.Session.Reviewer = "Sam";
        harness.Find("section.wb-step .wb-rv button").Click();
        harness.Session.UnexportedFlowCount.Should().Be(1);
        harness.Open(FlowWorkbenchTab.Review);
        harness.Press("Export the review");
        harness.Session.UnexportedFlowCount.Should().Be(0);
        harness.PanelText.Should().Contain("Review saved to the demo folder.");
        harness.Find(".wb-saved").GetAttribute("role").Should().Be("status");

        harness.Press("Go to Read");
        harness.Find("section.wb-step").QuerySelectorAll(".wb-rv button")[0].Click();
        harness.Session.UnexportedFlowCount.Should().Be(1);
        harness.Open(FlowWorkbenchTab.Review);
        harness.FindAll(".wb-saved").Should().BeEmpty();
    }

    [Fact]
    public void ThereIsOneFileForEachFlowThatHasAMarkAndNoneForTheOthers()
    {
        using var harness = Open();
        harness.Session.Reviewer = "Sam";
        harness.Find("section.wb-step .wb-rv button").Click();
        harness.Select("visit.note_wait");
        harness.Find("section.wb-step .wb-rv button").Click();
        harness.Open(FlowWorkbenchTab.Review);
        harness.PanelText.Should().Contain("2 reviews are exported");
        harness.Press("Export the review");
        harness.Exports.Single().Files.Select(file => file.SuggestedName).Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void ANoteIsKeptToItsLimitAndShowsACounterNearIt()
    {
        using var harness = Open();
        harness.Find("section.wb-step").QuerySelectorAll(".wb-rv button")[1].Click();
        harness.Find("section.wb-step textarea").Change(new string('x', 950));
        harness.Find("section.wb-step .wb-hint").TextContent.Should().Contain("950 of 1000 characters");
        harness.Find("section.wb-step textarea").Change(new string('y', 1200));
        harness.Find("section.wb-step textarea").GetAttribute("value").Should().HaveLength(1000);
    }

    [Fact]
    public void TheReviewTabCarriesThePrivacyNoticeAndTheProgressAsAStatusLine()
    {
        using var harness = Open();
        harness.Open(FlowWorkbenchTab.Review);
        harness.Find(".wb-notice").GetAttribute("role").Should().Be("note");
        harness.Find("p[role=status]").TextContent.Should().MatchRegex(@"^0 of \d+ items marked\.$");
    }

    [Fact]
    public void TheReviewerNameIsSharedByEveryFlowOfTheSession()
    {
        using var harness = Open();
        harness.Find("section.wb-step .wb-rv button").Click();
        harness.Open(FlowWorkbenchTab.Review);
        harness.Type("Your name", "Sam");
        harness.Select("visit.note_wait");
        harness.Input("Your name").GetAttribute("value").Should().Be("Sam");
    }
}

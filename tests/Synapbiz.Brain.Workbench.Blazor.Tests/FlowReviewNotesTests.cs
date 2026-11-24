using Bunit;
using FluentAssertions;
using Microsoft.JSInterop;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The notes of the Review tab (every item has its own editor and focus is only given to what is on screen) and what the tab says about notes, exports and the flows of the list.</summary>
public sealed class FlowReviewNotesTests
{
    private static FlowHarness Open(string id = "previsit.outreach", IReadOnlyList<WorkbenchFlow>? flows = null)
    {
        var harness = new FlowHarness();
        harness.Render(flows ?? FlowHarness.Reference());
        harness.Select(id);
        return harness;
    }

    private static void Mark(FlowHarness harness, string flowKey, string target, ReviewState state, string? note = null)
    {
        var entry = harness.Session.Work(flowKey).ReviewForEdit(target);
        entry.State = state;
        if (note is not null)
        {
            entry.Note = note;
        }

        harness.Session.Touch();
    }

    public static TheoryData<string, string> Kinds => new()
    {
        { "previsit.outreach", "process" },
        { "previsit.outreach", "stage:" },
        { "previsit.outreach", "step:" },
        { "previsit.outreach", "route:" },
        { "previsit.outreach", "scenario:" },
        { "case.lifecycle", "trigger:" },
    };

    private static string FirstOf(string id, string kind) =>
        FlowReviewTargets.Of(FlowDocs.Process(id)).First(target => target == kind || (kind.EndsWith(':') && target.StartsWith(kind, StringComparison.Ordinal)));

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AnItemOfEveryKindThatNeedsANoteGetsItsEditorFromTheReviewTabAndFocusGoesToIt(string id, string kind)
    {
        using var harness = Open(id);
        var target = FirstOf(id, kind);
        Mark(harness, id, target, ReviewState.NeedsFix);
        harness.Open(FlowWorkbenchTab.Review);
        harness.PanelText.Should().Contain("The note is missing.");
        harness.FocusIds.Clear();

        harness.Press("Write the note");
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Review);
        harness.FocusIds.Should().Equal(harness.RefOf("note:" + target).Id);
        harness.Find("table textarea").Change("Please look at this.");
        harness.PanelText.Should().NotContain("The note is missing.");
        harness.Find("table textarea").GetAttribute("value").Should().Be("Please look at this.");
        harness.Session.Work(id).Review(target).Note.Should().Be("Please look at this.");
    }

    [Fact]
    public void ANoteEditorIsLabelledWithTheNameOfTheItemItIsAbout()
    {
        using var harness = Open();
        var step = FlowDescriber.Describe(FlowDocs.Process("previsit.outreach"), WorkbenchLocale.En).Steps[0];
        Mark(harness, "previsit.outreach", "step:" + step.Id, ReviewState.Question);
        harness.Open(FlowWorkbenchTab.Review);
        var area = harness.Find("table textarea");
        harness.Find("label[for=" + area.Id + "]").TextContent.Should().Contain("Note about").And.Contain(step.Label);
    }

    [Theory]
    [InlineData(typeof(JSException))]
    [InlineData(typeof(InvalidOperationException))]
    public void AFocusThatTheBrowserRefusesDoesNotEndTheSession(Type failure)
    {
        using var harness = Open();
        Mark(harness, "previsit.outreach", "process", ReviewState.NeedsFix);
        harness.Open(FlowWorkbenchTab.Review);
        harness.FocusThrows = (Exception)Activator.CreateInstance(failure, "the element is gone")!;
        harness.Press("Write the note");
        harness.FocusIds.Should().NotBeEmpty();
        harness.Find("table textarea").Change("Still here.");
        harness.PanelText.Should().NotContain("The note is missing.");
        harness.Press("Export the review");
        harness.PanelText.Should().Contain("Write your name to export.");
    }

    [Fact]
    public void AFocusAskedForAnElementThatIsNotOnScreenAsksTheBrowserForNothing()
    {
        using var harness = Open();
        var step = FlowDescriber.Describe(FlowDocs.Process("previsit.outreach"), WorkbenchLocale.En).Steps[0].Id;
        var key = "note:step:" + step;
        Mark(harness, "previsit.outreach", "step:" + step, ReviewState.NeedsFix);
        harness.Open(FlowWorkbenchTab.Review);
        harness.RefOf(key).Id.Should().NotBeNull();

        // The mark is taken off: the row and its editor are gone, but the panel still holds the old reference.
        Mark(harness, "previsit.outreach", "step:" + step, ReviewState.Unreviewed);
        harness.FindAll("table textarea").Should().BeEmpty();
        harness.FocusIds.Clear();
        harness.AskFocus(key);
        harness.Session.Touch();
        harness.FocusIds.Should().BeEmpty();
        harness.FocusCount.Should().Be(0);
    }

    [Fact]
    public void ANoteWrittenWhenNothingElseChangedMakesAnExportedFlowUnexportedAgain()
    {
        using var harness = Open();
        harness.Session.Reviewer = "Sam";
        Mark(harness, "previsit.outreach", "process", ReviewState.Confirmed);
        harness.Open(FlowWorkbenchTab.Review);
        harness.Press("Export the review");
        harness.Exports.Should().HaveCount(1);
        harness.Session.UnexportedFlowCount.Should().Be(0);
        harness.PanelText.Should().Contain("Review saved to");

        harness.Find("table textarea").Change("A late thought.");
        harness.Session.UnexportedFlowCount.Should().Be(1);
        harness.PanelText.Should().NotContain("Review saved to");
    }

    [Fact]
    public void ChangingTheSelectedFlowClearsTheSelectedStep()
    {
        using var harness = Open();
        harness.Session.SelectedStepId = "assess";
        harness.Session.SelectedStepId.Should().Be("assess");
        harness.Session.SelectedFlowKey = "visit.note_wait";
        harness.Session.SelectedStepId.Should().BeNull();
    }

    // ---------------------------------------------------------------- the flows of the list

    [Fact]
    public void AMissingNoteInAnotherFlowBlocksTheExportAndTheMessageNamesTheFlows()
    {
        using var harness = Open();
        harness.Session.Reviewer = "Sam";
        Mark(harness, "previsit.outreach", "process", ReviewState.Confirmed);
        Mark(harness, "visit.note_wait", "process", ReviewState.NeedsFix);
        Mark(harness, "collection.followup", "process", ReviewState.Question);
        harness.Open(FlowWorkbenchTab.Review);
        harness.Press("Export the review");
        harness.Exports.Should().BeEmpty();
        var alert = harness.Find("[role=alert]").TextContent;
        alert.Should().Contain("2 items to fix or with a question need their notes");
        alert.Should().Contain(FlowDescriber.Describe(FlowDocs.Process("visit.note_wait"), WorkbenchLocale.En).Title);
        alert.Should().Contain(FlowDescriber.Describe(FlowDocs.Process("collection.followup"), WorkbenchLocale.En).Title);
        alert.Should().NotContain(FlowDescriber.Describe(FlowDocs.Process("previsit.outreach"), WorkbenchLocale.En).Title);

        harness.ListText.Should().Contain("needs a note");
        harness.FindAll(".wb-list-panel .wb-pill").Count(pill => pill.TextContent.Contains("needs a note", StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public void TheExportMarksOnlyTheFlowsItWroteAndTheReviewerIsToldAboutTheOnesThatLeftTheList()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Session.Reviewer = "Sam";
        harness.Select("previsit.outreach");
        Mark(harness, "previsit.outreach", "process", ReviewState.Confirmed);
        Mark(harness, "visit.note_wait", "process", ReviewState.Confirmed);
        harness.Session.UnexportedFlowCount.Should().Be(2);

        // The reader's host takes one flow out of the list: its work is no longer listed and cannot be written.
        var listed = FlowHarness.Reference().Where(flow => flow.Key != "visit.note_wait").ToList();
        harness.Cut.SetParametersAndRender(parameters => parameters.Add(component => component.Flows, listed));
        harness.Open(FlowWorkbenchTab.Review);
        harness.PanelText.Should().Contain("1 process with unexported work is no longer in the list.");
        harness.Press("Export the review");
        harness.Exports.Should().HaveCount(1);
        harness.Exports[0].Files.Should().ContainSingle().Which.SuggestedName.Should().Contain("previsit.outreach");
        harness.Session.UnexportedFlowCount.Should().Be(1);
        harness.Session.Work("previsit.outreach").HasUnexportedChanges.Should().BeFalse();
        harness.Session.Work("visit.note_wait").HasUnexportedChanges.Should().BeTrue();
        harness.PanelText.Should().Contain("1 process with unexported work is no longer in the list.");
    }

    [Fact]
    public void AHostThatFailsToSaveLeavesTheWorkUnexportedAndTheExceptionGoesToTheHost()
    {
        using var harness = new FlowHarness();
        harness.Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        harness.Session.Reviewer = "Sam";
        harness.Session.View = FlowWorkbenchView.List;
        var cut = harness.Ctx.RenderComponent<FlowWorkbench>(parameters => parameters
            .Add(component => component.Flows, FlowHarness.Reference())
            .Add(component => component.Session, harness.Session)
            .Add(component => component.Now, harness.Now)
            .Add(component => component.OnExport, _ => throw new IOException("the disk is full")));
        harness.Session.SelectedFlowKey = "previsit.outreach";
        Mark(harness, "previsit.outreach", "process", ReviewState.Confirmed);
        cut.FindAll("[role=tab]")[(int)FlowWorkbenchTab.Review].Click();
        var press = () => cut.FindAll("[role=tabpanel] button").First(button => FlowHarness.Normalise(button.TextContent) == "Export the review").Click();
        press.Should().Throw<IOException>().WithMessage("the disk is full");
        harness.Session.UnexportedFlowCount.Should().Be(1);
        harness.Session.Work("previsit.outreach").HasUnexportedChanges.Should().BeTrue();
    }

    [Fact]
    public void TwoFlowsWithTheSameTitleAreToldApartByTheirVersion()
    {
        var first = FlowHarness.One("previsit.outreach");
        var second = new WorkbenchFlow
        {
            Key = "previsit.outreach.v2",
            Group = first.Group,
            Process = FlowDocs.Edited("previsit.outreach", document => document["version"] = 2),
        };
        using var harness = Open("previsit.outreach", [first, second]);
        var items = harness.FindAll(".wb-list-panel [role=option], .wb-list-panel li").Select(item => FlowHarness.Normalise(item.TextContent)).ToList();
        items.Should().Contain(text => text.Contains("version 1", StringComparison.Ordinal));
        items.Should().Contain(text => text.Contains("version 2", StringComparison.Ordinal));

        using var alone = Open("visit.note_wait");
        alone.ListText.Should().NotContain("version 1");
    }

    [Fact]
    public void TheListLineSaysHowManyOfTheTestsFail()
    {
        using var harness = Open();
        harness.ListText.Should().NotContain("tests fails").And.NotContain("test fails");
        var broken = FlowDocs.Edited("previsit.outreach", document =>
        {
            var scenarios = document["body"]!["scenarios"]!.AsArray();
            scenarios[0]!["expect"]!["result"] = "no_such_result";
        });
        using var other = new FlowHarness();
        other.Render([new WorkbenchFlow { Key = "x", Group = "g", Process = broken }]);
        other.ListText.Should().MatchRegex(@"1 of \d+ tests fails");
    }
}

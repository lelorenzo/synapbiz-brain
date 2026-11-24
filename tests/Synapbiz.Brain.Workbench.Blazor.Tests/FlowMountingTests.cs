using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class FlowMountingTests
{
    [Fact]
    public void NullFlowsShowTheLoadingLineAndNoTabs()
    {
        using var harness = new FlowHarness();
        harness.Render(null);
        harness.DetailText.Should().Contain("Loading processes");
        harness.FindAll("[role=tab]").Should().BeEmpty();
        harness.FindAll(".wb-skeleton").Should().HaveCount(3);
    }

    [Fact]
    public void AnEmptyListShowsTheEmptyText()
    {
        using var harness = new FlowHarness();
        harness.Render([]);
        harness.DetailText.Should().Contain("No processes are loaded on this computer.");
        harness.FindAll("[role=tab]").Should().BeEmpty();
    }

    [Fact]
    public void AnUnreadableFlowIsListedAsSuchWithItsFallbackTitle()
    {
        using var harness = new FlowHarness();
        harness.Render([new WorkbenchFlow { Key = "broken", FallbackTitle = "Broken one" }]);
        harness.ListText.Should().Contain("Broken one").And.Contain("Could not be read");
        harness.DetailText.Should().Contain("This process could not be read on this computer.");
        harness.FindAll("[role=tab]").Should().BeEmpty();
    }

    [Fact]
    public void AFlowTheHostCouldNotLoadIsListedAsUnreadableWithItsFallbackTitle()
    {
        using var harness = new FlowHarness();
        harness.Render([new WorkbenchFlow { Key = "a", Process = null, FallbackTitle = "Not loaded" }]);
        harness.ListText.Should().Contain("Not loaded").And.Contain("Could not be read");
        harness.DetailText.Should().Contain("This process could not be read on this computer.");
    }

    [Fact]
    public void OneFlowShowsItsTitleAndFiveTabsWithTheReadTabOpen()
    {
        using var harness = new FlowHarness();
        harness.Render([FlowHarness.One("previsit.outreach")]);
        harness.Find("h1").TextContent.Should().NotBeNullOrWhiteSpace();
        harness.FindAll("[role=tab]").Select(tab => FlowHarness.Normalise(tab.TextContent)).Should().SatisfyRespectively(
            tab => tab.Should().Be("Read"),
            tab => tab.Should().Be("Diagram"),
            tab => tab.Should().Be("Simulate"),
            tab => tab.Should().StartWith("Tests"),
            tab => tab.Should().Be("Review"));
        harness.Tab(FlowWorkbenchTab.Read).GetAttribute("aria-selected").Should().Be("true");
        harness.Find("[role=tabpanel]").GetAttribute("aria-labelledby").Should().Be(harness.Tab(FlowWorkbenchTab.Read).Id);
    }

    [Fact]
    public void FlowsAreListedUnderTheirGroupHeadings()
    {
        using var harness = new FlowHarness();
        var flows = FlowHarness.Reference();
        harness.Render([.. flows.Take(2), new WorkbenchFlow { Key = "x", Group = "Other group", Process = flows[2].Process }]);
        harness.FindAll("h3.wb-group").Select(heading => heading.TextContent).Should().Equal("Reference flows", "Other group");
        harness.FindAll("button.wb-rule").Should().HaveCount(3);
        harness.Find("button.wb-rule[aria-current=true]").Should().NotBeNull();
    }

    [Fact]
    public void TheNarrowSelectListsEveryFlowAndSelectsOne()
    {
        using var harness = new FlowHarness();
        var flows = FlowHarness.Reference();
        harness.Render(flows);
        var select = harness.Find("select.wb-list-select");
        select.GetAttribute("aria-label").Should().Be("Process list");
        select.QuerySelectorAll("option").Should().HaveCount(6);
        select.Change("2");
        harness.Session.SelectedFlowKey.Should().Be(flows[2].Key);
    }

    [Fact]
    public void ClickingAFlowSelectsItAndTheArrowKeysMoveTheSelection()
    {
        using var harness = new FlowHarness();
        var flows = FlowHarness.Reference();
        harness.Render(flows);
        harness.FindAll("button.wb-rule")[1].Click();
        harness.Session.SelectedFlowKey.Should().Be(flows[1].Key);
        var focus = harness.FocusCount;
        harness.FindAll("button.wb-rule")[1].KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowDown" });
        harness.Session.SelectedFlowKey.Should().Be(flows[2].Key);
        harness.FocusCount.Should().BeGreaterThan(focus);
    }

    [Fact]
    public void ChangingTheLocaleReRendersInPlaceAndKeepsTheSession()
    {
        using var harness = new FlowHarness();
        var flows = FlowHarness.Reference();
        harness.Render(flows);
        harness.Select(flows[1].Key);
        harness.Open(FlowWorkbenchTab.Review);
        var english = harness.Find("h1").TextContent;
        harness.Cut.SetParametersAndRender(parameters => parameters.Add(component => component.Locale, WorkbenchLocale.Es));
        harness.Find("h1").TextContent.Should().NotBe(english);
        harness.Find("[role=tabpanel]").Should().NotBeNull();
        harness.Session.SelectedFlowKey.Should().Be(flows[1].Key);
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Review);
        harness.Find(".wb-root").GetAttribute("lang").Should().Be("es");
        harness.Tab(FlowWorkbenchTab.Read).TextContent.Trim().Should().Be("Leer");
    }

    [Fact]
    public void TextOverridesReplaceFixedStringsByKey()
    {
        using var harness = new FlowHarness();
        harness.Render([FlowHarness.One("previsit.outreach")], overrides: new Dictionary<string, string> { ["flow.tabs.read"] = "Look" });
        harness.Tab(FlowWorkbenchTab.Read).TextContent.Trim().Should().Be("Look");
    }

    [Fact]
    public void TheMandatoryMarkingsCanBeRewordedButNeverEmptied()
    {
        using var harness = new FlowHarness();
        var flow = FlowHarness.One("previsit.outreach");
        harness.Render([flow], overrides: new Dictionary<string, string> { ["flow.band.try"] = "Pretend only.", ["flow.band.tests"] = " ", ["pill.simulation"] = string.Empty });
        harness.Open(FlowWorkbenchTab.Try);
        harness.Find(".wb-band").TextContent.Should().Contain("Pretend only.").And.Contain("Simulation");
        harness.Open(FlowWorkbenchTab.Tests);
        harness.Find(".wb-band").TextContent.Should().Contain("made-up examples");
    }

    [Fact]
    public void TheSimulationBandAndPillAreOnTheTryAndTestsTabsAndThePrivacyNoticeOnReview()
    {
        using var harness = new FlowHarness();
        harness.Render([FlowHarness.One("previsit.outreach")]);
        foreach (var tab in new[] { FlowWorkbenchTab.Try, FlowWorkbenchTab.Tests })
        {
            harness.Open(tab);
            harness.Find(".wb-band .wb-pill-sim").TextContent.Should().Be("Simulation");
        }

        harness.Open(FlowWorkbenchTab.Review);
        harness.Find(".wb-notice").TextContent.Should().Contain("Do not identify real people");
    }

    [Fact]
    public void AReaderWithoutOnExportSeesNoExportButton()
    {
        using var harness = new FlowHarness();
        harness.Render([FlowHarness.One("previsit.outreach")], withExport: false);
        harness.Open(FlowWorkbenchTab.Review);
        harness.HasButton("Export the review").Should().BeFalse();
    }

    [Fact]
    public void TheSessionRaisesChangedAndClearForgetsEverything()
    {
        var session = new FlowWorkbenchSession();
        var raised = 0;
        session.Changed += (_, _) => raised++;
        session.SelectedTab = FlowWorkbenchTab.Diagram;
        session.Reviewer = "Sam";
        session.SelectedFlowKey = "a";
        raised.Should().Be(3);
        session.Clear();
        session.SelectedTab.Should().Be(FlowWorkbenchTab.Read);
        session.Reviewer.Should().BeEmpty();
        session.SelectedFlowKey.Should().BeNull();
        session.UnexportedFlowCount.Should().Be(0);
    }
}

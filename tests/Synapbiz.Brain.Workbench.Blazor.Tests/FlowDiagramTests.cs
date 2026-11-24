using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class FlowDiagramTests
{
    public static TheoryData<string> EveryDocument()
    {
        var data = new TheoryData<string>();
        foreach (var (name, _) in FlowDocs.All)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryDocument))]
    public void ThereIsOneNodePerStepAndOneLinePerOrderedPairOfStepsAndTheTitlesNameEveryRoute(string name)
    {
        using var harness = new FlowHarness();
        var process = FlowDocs.Process(name);
        harness.Render([new WorkbenchFlow { Key = name, Process = process }]);
        harness.Open(FlowWorkbenchTab.Diagram);
        var graph = FlowGraph.From(process, WorkbenchLocale.En);
        var pairs = graph.Edges.Select(edge => (edge.From, edge.To)).Distinct().Count();
        harness.FindAll("g.wb-node").Should().HaveCount(ProcessOutline.From(process).Steps.Count);
        harness.FindAll("g.wb-edge-g").Should().HaveCount(pairs);
        harness.FindAll("svg.wb-diagram-svg path.wb-edge").Should().HaveCount(pairs);
        graph.Edges.Count.Should().Be(ProcessOutline.From(process).Steps.Sum(step => step.Routes.Count));
        harness.FindAll("svg.wb-diagram-svg path.wb-edge > title").Sum(title => title.TextContent.Split('\n').Length).Should().Be(graph.Edges.Count);
    }

    [Fact]
    public void TheMainPathIsSolidTheOtherRoutesAndTheReturnsAreDashedAndTheUntriedOnesAreMarked()
    {
        using var harness = new FlowHarness();
        harness.Render([FlowHarness.One("previsit.outreach")]);
        harness.Open(FlowWorkbenchTab.Diagram);
        var (layout, _) = FlowDiagramGeometryTests.Of("previsit.outreach", WorkbenchLocale.En);
        harness.FindAll("svg.wb-diagram-svg path.wb-edge:not(.wb-edge-side)").Should().HaveCount(layout.Lines.Count(line => line.IsHappyPath && !line.IsReturn));
        harness.FindAll("svg.wb-diagram-svg path.wb-edge-side").Should().HaveCount(layout.Lines.Count(line => !line.IsHappyPath || line.IsReturn));
        harness.FindAll("svg.wb-diagram-svg path.wb-edge-return").Should().HaveCount(layout.Lines.Count(line => line.IsReturn));
        harness.FindAll("svg.wb-diagram-svg circle.wb-untried").Should().BeEmpty("the scenarios of this flow take every route");
        harness.FindAll("svg.wb-diagram-svg circle.wb-count").Should().HaveCount(layout.Lines.Count(line => line.Routes.Count > 1));
        harness.FindAll("svg.wb-diagram-svg circle.wb-count + text.wb-count-t").Select(text => text.TextContent).Should().Equal(layout.Lines.Where(line => line.Routes.Count > 1).Select(line => line.Routes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ARouteNoTestTakesGetsTheQuestionMarkAndTheLegendCountsThem()
    {
        using var harness = new FlowHarness();
        var process = FlowDocs.Process("flow.clock");
        var untried = FlowCoverage.Of(process).NeverTaken.Count;
        untried.Should().BeGreaterThan(0);
        harness.Render([new WorkbenchFlow { Key = "clock", Process = process }]);
        harness.Open(FlowWorkbenchTab.Diagram);
        harness.FindAll("svg.wb-diagram-svg circle.wb-untried").Should().NotBeEmpty();
        harness.Find("section.wb-legend").TextContent.Should().Contain(untried == 1 ? "marks 1 route that no test takes" : "routes that no test takes");
    }

    [Fact]
    public void TheDiagramHasAGroupRoleALabelAScrollRegionAndAReadAsListControl()
    {
        using var harness = new FlowHarness();
        harness.Render([FlowHarness.One("previsit.outreach")]);
        harness.Open(FlowWorkbenchTab.Diagram);
        var svg = harness.Find("svg.wb-diagram-svg");
        svg.GetAttribute("role").Should().Be("group");
        svg.GetAttribute("aria-label").Should().StartWith("Diagram of the process");
        harness.Find(".wb-diagram-scroll").GetAttribute("tabindex").Should().Be("0");
        harness.Find(".wb-diagram-scroll").GetAttribute("role").Should().Be("region");
        harness.Press("Read it as a list");
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Read);
    }

    [Fact]
    public void EveryNodeIsFocusableNamedAndOpensItsCardByClickEnterAndSpace()
    {
        using var harness = new FlowHarness();
        harness.Render([FlowHarness.One("previsit.outreach")]);
        harness.Open(FlowWorkbenchTab.Diagram);
        var nodes = harness.FindAll("g.wb-node");
        nodes.Should().OnlyContain(node => node.GetAttribute("tabindex") == "0" && node.GetAttribute("role") == "button" && node.GetAttribute("aria-pressed") == "false" && !string.IsNullOrWhiteSpace(node.GetAttribute("aria-label")));
        harness.FindAll("aside.wb-diagram-card").Should().BeEmpty();

        nodes[0].Click();
        var step = harness.Session.SelectedStepId;
        step.Should().NotBeNull();
        harness.Find("aside.wb-diagram-card").TextContent.Should().Contain("Chosen step");
        harness.FindAll("g.wb-node[aria-pressed=true]").Should().HaveCount(1);

        harness.FindAll("g.wb-node")[1].KeyDown(new KeyboardEventArgs { Key = "Enter" });
        harness.Session.SelectedStepId.Should().NotBe(step);
        var second = harness.Session.SelectedStepId;
        harness.FindAll("g.wb-node")[1].KeyDown(new KeyboardEventArgs { Key = " " });
        harness.Session.SelectedStepId.Should().BeNull();
        harness.FindAll("g.wb-node")[1].KeyDown(new KeyboardEventArgs { Key = "a" });
        harness.Session.SelectedStepId.Should().BeNull();
        second.Should().NotBeNull();
    }

    [Fact]
    public void TheCardHasTheContentOfTheStepInReadAndItsRouteLinksOpenTheNextCard()
    {
        using var harness = new FlowHarness();
        var flows = FlowHarness.Reference();
        harness.Render(flows);
        harness.Select("previsit.outreach");
        harness.Open(FlowWorkbenchTab.Diagram);
        var view = FlowDescriber.Describe(flows[0].Process!, WorkbenchLocale.En, flows.Select(flow => flow.Process!));
        var step = view.Steps.First(item => item.Routes.Count > 0);
        harness.Session.SelectedStepId = step.Id;
        var card = FlowHarness.Normalise(harness.Find("aside.wb-diagram-card").TextContent);
        card.Should().Contain(step.Sentence.Replace("`", string.Empty, StringComparison.Ordinal)).And.Contain(step.Label);
        harness.Find("aside.wb-diagram-card .wb-route-go").Click();
        harness.Session.SelectedStepId.Should().Be(step.Routes[0].Target);
        harness.Press("Close");
        harness.Session.SelectedStepId.Should().BeNull();
    }

    [Fact]
    public void TheLegendNamesEveryShapeAndLineStyleAndCountsTheUntriedRoutes()
    {
        using var harness = new FlowHarness();
        harness.Render([FlowHarness.One("previsit.outreach")]);
        harness.Open(FlowWorkbenchTab.Diagram);
        var legend = FlowHarness.Normalise(harness.Find("section.wb-legend").TextContent);
        legend.Should().Contain("Action").And.Contain("Decision").And.Contain("Task for a person").And.Contain("Wait").And.Contain("Another process").And.Contain("End")
            .And.Contain("Main path").And.Contain("Other route").And.Contain("Goes back to an earlier step").And.Contain("Marks the first step");
        legend.Should().Contain("The step the simulation is at").And.Contain("The number tells how many routes share this line").And.Contain("Every route is taken by some test");
        harness.FindAll("section.wb-legend svg").Should().OnlyContain(svg => svg.GetAttribute("aria-hidden") == "true");
    }

    [Fact]
    public void AStepWithNoStageGetsALastBandAndTheLegendSaysSo()
    {
        using var harness = new FlowHarness();
        harness.Render([new WorkbenchFlow { Key = "bare", Process = FlowDocs.Process("flow.bare") }]);
        harness.Open(FlowWorkbenchTab.Diagram);
        harness.FindAll("g.wb-band-g").Should().HaveCount(1);
        harness.Find("section.wb-legend").TextContent.Should().Contain("Steps with no stage are in the last row.");
    }

    [Fact]
    public void TheTryOutShowsWhereTheCaseIsOnTheDiagramWithAWordAndASimulationPill()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Start("previsit.outreach");
        harness.Open(FlowWorkbenchTab.Diagram);
        harness.FindAll("g.wb-node-now").Should().HaveCount(1);
        harness.Find("g.wb-node-now").GetAttribute("aria-label").Should().Contain("the simulation is here");
        harness.Find(".wb-diagram-top").TextContent.Should().Contain("Simulation").And.Contain("The simulation is at:");
    }
}

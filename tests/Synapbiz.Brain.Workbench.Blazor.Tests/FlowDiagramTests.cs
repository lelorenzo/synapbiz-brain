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

    private static DiagramLayout LayoutOf(string name)
    {
        var process = FlowDocs.Process(name);
        return DiagramLayout.Of(FlowGraph.From(process, WorkbenchLocale.En), ProcessOutline.From(process).Stages.Count);
    }

    [Theory]
    [MemberData(nameof(EveryDocument))]
    public void NoTwoNodesOverlapAndEveryNodeIsInsideItsBand(string name)
    {
        var layout = LayoutOf(name);
        foreach (var box in layout.Nodes)
        {
            var band = layout.Bands[box.Band];
            box.X.Should().BeGreaterThanOrEqualTo(band.X);
            (box.X + box.Width).Should().BeLessThanOrEqualTo(band.X + band.Width);
            box.Y.Should().BeGreaterThanOrEqualTo(DiagramLayout.HeaderHeight);
            (box.Y + box.Height).Should().BeLessThanOrEqualTo(layout.Height);
        }

        for (var left = 0; left < layout.Nodes.Count; left++)
        {
            for (var right = left + 1; right < layout.Nodes.Count; right++)
            {
                var a = layout.Nodes[left];
                var b = layout.Nodes[right];
                var apart = a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                apart.Should().BeTrue(a.Id + " and " + b.Id);
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryDocument))]
    public void TheLayoutIsDeterministic(string name)
    {
        LayoutOf(name).Should().BeEquivalentTo(LayoutOf(name));
    }

    [Fact]
    public void ThePrevisitOutreachCoordinatesArePinned()
    {
        var layout = LayoutOf("previsit.outreach");
        layout.Width.Should().Be(816);
        layout.Height.Should().Be(474);
        layout.Nodes.Select(box => $"{box.Id}:{box.X},{box.Y}").Should().Equal(
        "cycle:18,52",
        "check:18,140",
        "assess:18,228",
        "send:18,316",
        "reply:18,404",
        "escalate:206,228",
        "escalate_final:206,140",
        "done:394,228",
        "closed:582,228");
        layout.Edges.Select(edge => $"{edge.Edge.From}>{edge.Edge.To}:{edge.Path}").Should().Equal(
        "cycle>check:M 102 104 L 102 140",
        "cycle>escalate_final:M 102 104 L 290 140",
        "check>assess:M 102 192 L 102 228",
        "check>escalate:M 102 192 L 290 228",
        "check>escalate_final:M 186 166 L 206 166",
        "assess>done:M 186 254 L 394 254",
        "assess>send:M 102 280 L 102 316",
        "assess>escalate:M 186 254 L 206 254",
        "assess>escalate:M 186 254 L 206 254",
        "send>reply:M 102 368 L 102 404",
        "send>escalate:M 186 342 C 414 342 414 254 374 254",
        "send>escalate_final:M 102 316 L 290 192",
        "send>escalate_final:M 102 316 L 290 192",
        "send>escalate:M 186 342 C 414 342 414 254 374 254",
        "reply>cycle:M 186 430 C 226 430 226 78 186 78",
        "reply>cycle:M 186 430 C 226 430 226 78 186 78",
        "escalate>cycle:M 374 254 C 414 254 414 78 186 78",
        "escalate>closed:M 374 254 L 582 254",
        "escalate>closed:M 374 254 L 582 254",
        "escalate_final>done:M 290 192 L 478 228",
        "escalate_final>closed:M 290 192 L 666 228",
        "escalate_final>closed:M 290 192 L 666 228");
    }

    [Theory]
    [MemberData(nameof(EveryDocument))]
    public void ThereIsOneNodePerStepAndOneLinePerEdgeOfTheGraph(string name)
    {
        using var harness = new FlowHarness();
        var process = FlowDocs.Process(name);
        harness.Render([new WorkbenchFlow { Key = name, Process = process, ProcessText = "{}" }]);
        harness.Open(FlowWorkbenchTab.Diagram);
        var graph = FlowGraph.From(process, WorkbenchLocale.En);
        harness.FindAll("g.wb-node").Should().HaveCount(ProcessOutline.From(process).Steps.Count);
        harness.FindAll("g.wb-edge-g").Should().HaveCount(graph.Edges.Count);
        harness.FindAll("svg.wb-diagram-svg path.wb-edge").Should().HaveCount(graph.Edges.Count);
        graph.Edges.Count.Should().Be(ProcessOutline.From(process).Steps.Sum(step => step.Routes.Count));
    }

    [Fact]
    public void TheHappyPathIsSolidAndTheOtherRoutesAreDashedAndTheUntriedOnesAreMarked()
    {
        using var harness = new FlowHarness();
        var process = FlowDocs.Process("previsit.outreach");
        harness.Render([FlowHarness.One("previsit.outreach")]);
        harness.Open(FlowWorkbenchTab.Diagram);
        var graph = FlowGraph.From(process, WorkbenchLocale.En);
        harness.FindAll("svg.wb-diagram-svg path.wb-edge:not(.wb-edge-side)").Should().HaveCount(graph.Edges.Count(edge => edge.IsHappyPath));
        harness.FindAll("svg.wb-diagram-svg path.wb-edge-side").Should().HaveCount(graph.Edges.Count(edge => !edge.IsHappyPath));
        harness.FindAll("svg.wb-diagram-svg circle.wb-untried").Should().HaveCount(FlowCoverage.Of(process).NeverTaken.Count);
    }

    [Fact]
    public void TheDiagramHasAGroupRoleALabelAScrollRegionAndAReadAsListControl()
    {
        using var harness = new FlowHarness();
        harness.Render([FlowHarness.One("previsit.outreach")]);
        harness.Open(FlowWorkbenchTab.Diagram);
        var svg = harness.Find("svg.wb-diagram-svg");
        svg.GetAttribute("role").Should().Be("group");
        svg.GetAttribute("aria-label").Should().StartWith("Diagram of the flow");
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
        legend.Should().Contain("Action").And.Contain("Decision").And.Contain("Task for a person").And.Contain("Wait").And.Contain("Another flow").And.Contain("End")
            .And.Contain("Main path").And.Contain("Other route").And.Contain("Goes back to an earlier step").And.Contain("Marks the first step");
        legend.Should().Contain(FlowCoverage.Of(FlowDocs.Process("previsit.outreach")).NeverTaken.Count == 1 ? "marks 1 route" : "routes that no test takes");
        harness.FindAll("section.wb-legend svg").Should().OnlyContain(svg => svg.GetAttribute("aria-hidden") == "true");
    }

    [Fact]
    public void AStepWithNoStageGetsALastBandAndTheLegendSaysSo()
    {
        using var harness = new FlowHarness();
        harness.Render([new WorkbenchFlow { Key = "bare", Process = FlowDocs.Process("flow.bare"), ProcessText = "{}" }]);
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
        harness.Find("g.wb-node-now").GetAttribute("aria-label").Should().Contain("the try-out is here");
        harness.Find(".wb-diagram-top").TextContent.Should().Contain("Simulation").And.Contain("The try-out is at:");
    }
}

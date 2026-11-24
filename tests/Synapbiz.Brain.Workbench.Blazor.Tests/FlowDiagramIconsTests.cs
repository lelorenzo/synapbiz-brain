using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The symbols of the steps of the diagram (the same as the map's), the numbered marks of the labels that do not fit and the list under the diagram.</summary>
public sealed class FlowDiagramIconsTests
{
    private static string SymbolOf(FlowFamily family) => family switch
    {
        FlowFamily.Action => "con",
        FlowFamily.Decision => "rule",
        FlowFamily.Person => "per",
        FlowFamily.Ai => "ai",
        FlowFamily.Wait => "sch",
        FlowFamily.Flow => "sub",
        _ => string.Empty,
    };

    private static string Symbol(AngleSharp.Dom.IElement use) => use.GetAttribute("href")![(use.GetAttribute("href")!.LastIndexOf("-i-", StringComparison.Ordinal) + 3)..];

    [Theory]
    [MemberData(nameof(FlowDiagramTests.EveryDocument), MemberType = typeof(FlowDiagramTests))]
    public void EveryStepCarriesTheSymbolOfItsFamilyAndAnEndStepCarriesNone(string name)
    {
        using var harness = new FlowHarness();
        var process = FlowDocs.Process(name);
        harness.Render([new WorkbenchFlow { Key = name, Process = process }]);
        harness.Open(FlowWorkbenchTab.Diagram);
        var graph = FlowGraph.From(process, WorkbenchLocale.En);
        var nodes = harness.FindAll("svg.wb-diagram-svg g.wb-node");
        nodes.Should().HaveCount(graph.Nodes.Count);
        for (var position = 0; position < nodes.Count; position++)
        {
            var expected = SymbolOf(graph.Nodes[position].Family);
            var uses = nodes[position].QuerySelectorAll("use.wb-node-i");
            if (expected.Length == 0)
            {
                uses.Should().BeEmpty(graph.Nodes[position].Id);
            }
            else
            {
                uses.Should().HaveCount(1, graph.Nodes[position].Id);
                Symbol(uses[0]).Should().Be(expected, graph.Nodes[position].Id);
            }
        }
    }

    [Fact]
    public void EveryUseOfASymbolResolvesToASymbolOfTheSpriteAndTheLegendDrawsTheSymbolsOfTheMap()
    {
        using var harness = new FlowHarness();
        harness.Render([FlowHarness.One("case.lifecycle")]);
        harness.Open(FlowWorkbenchTab.Diagram);
        var ids = harness.FindAll("svg.wb-sprite symbol").Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        var uses = harness.FindAll("use").Select(use => use.GetAttribute("href")!.TrimStart('#')).ToList();
        uses.Should().NotBeEmpty();
        uses.Where(id => !ids.Contains(id)).Should().BeEmpty();
        var onMap = harness.FindAll("svg.wb-diagram-svg use.wb-node-i").Select(Symbol).ToHashSet(StringComparer.Ordinal);
        var inLegend = harness.FindAll("section.wb-legend use.wb-node-i").Select(Symbol).ToHashSet(StringComparer.Ordinal);
        inLegend.Order(StringComparer.Ordinal).Should().Equal("ai", "con", "per", "rule", "sch", "sub");
        onMap.IsSubsetOf(inLegend).Should().BeTrue();
    }

    [Fact]
    public void ALabelWithNoPlaceBecomesANumberedMarkAndItsTextIsListedUnderTheDiagram()
    {
        var found = false;
        foreach (var flow in FlowHarness.Reference())
        {
            var (layout, _) = FlowDiagramGeometryTests.Of(flow.Key, WorkbenchLocale.En);
            if (!layout.NumberedLines.Any())
            {
                continue;
            }

            found = true;
            using var harness = new FlowHarness();
            harness.Render([flow]);
            harness.Open(FlowWorkbenchTab.Diagram);
            harness.FindAll("svg.wb-diagram-svg circle.wb-numbered + text.wb-numbered-t").Select(text => text.TextContent)
                .Should().Equal(layout.NumberedLines.Select(line => line.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            harness.FindAll("ul.wb-marks-list li").Select(item => FlowHarness.Normalise(item.TextContent))
                .Should().Equal(layout.NumberedLines.Select(line => line.Number + ": " + line.FullLabel));
            FlowHarness.Normalise(harness.Find("section.wb-legend").TextContent).Should().Contain("A number in a circle stands for a text that does not fit");
        }

        found.Should().BeTrue("some reference flow has a label with no place beside its line");
    }

    [Fact]
    public void WithNoNumberedMarkThereIsNoListAndNoLegendEntryForIt()
    {
        using var harness = new FlowHarness();
        harness.Render([FlowHarness.One("previsit.outreach")]);
        harness.Open(FlowWorkbenchTab.Diagram);
        harness.FindAll("ul.wb-marks-list").Should().BeEmpty();
        harness.FindAll("circle.wb-numbered").Should().BeEmpty();
        FlowHarness.Normalise(harness.Find("section.wb-legend").TextContent).Should().NotContain("A number in a circle");
    }

    [Fact]
    public void TheTextOfAStepWithASymbolIsWrittenToTheRightOfIt()
    {
        using var harness = new FlowHarness();
        harness.Render([FlowHarness.One("previsit.outreach")]);
        harness.Open(FlowWorkbenchTab.Diagram);
        var (layout, graph) = FlowDiagramGeometryTests.Of("previsit.outreach", WorkbenchLocale.En);
        var nodes = harness.FindAll("svg.wb-diagram-svg g.wb-node");
        for (var position = 0; position < nodes.Count; position++)
        {
            var box = layout.Nodes.Single(item => item.Id == graph.Nodes[position].Id);
            var middle = box.X + (box.Width / 2);
            var texts = nodes[position].QuerySelectorAll("text.wb-node-t").Select(text => int.Parse(text.GetAttribute("x")!, System.Globalization.CultureInfo.InvariantCulture));
            texts.Should().OnlyContain(x => x == middle + (SymbolOf(graph.Nodes[position].Family).Length == 0 ? 0 : 10), graph.Nodes[position].Id);
        }
    }
}

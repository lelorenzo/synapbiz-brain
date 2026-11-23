using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class FlowGraphTests
{
    [Fact]
    public void The_graph_has_a_node_per_step_and_an_edge_per_route()
    {
        foreach (var (name, process) in FlowDocs.All)
        {
            var outline = ProcessOutline.From(process);
            var graph = FlowGraph.From(process, WorkbenchLocale.En);
            graph.Nodes.Select(node => node.Id).Should().Equal(outline.Steps.Select(step => step.Id), name);
            graph.Edges.Select(edge => (edge.From, edge.Label, edge.To)).Should()
                .BeEquivalentTo(outline.Steps.SelectMany(step => step.Routes.Select(route => (step.Id, route.Label, route.Target))), name);
        }
    }

    [Fact]
    public void Depth_counts_the_shortest_way_from_the_start()
    {
        foreach (var (name, process) in FlowDocs.All)
        {
            var graph = FlowGraph.From(process, WorkbenchLocale.En);
            var start = graph.Nodes.Single(node => node.IsStart);
            start.Id.Should().Be(ProcessOutline.From(process).Start, name);
            start.Depth.Should().Be(0, name);
            var depths = graph.Nodes.ToDictionary(node => node.Id, node => node.Depth);
            foreach (var edge in graph.Edges.Where(edge => !edge.IsBackEdge))
            {
                depths[edge.To].Should().BeLessThanOrEqualTo(depths[edge.From] + 1, name + " " + edge.From + "->" + edge.To);
            }

            foreach (var edge in graph.Edges.Where(edge => edge.IsBackEdge))
            {
                depths[edge.To].Should().BeLessThanOrEqualTo(depths[edge.From], name + " " + edge.From + "->" + edge.To);
            }
        }
    }

    [Fact]
    public void The_loops_of_the_outreach_flow_come_back_as_back_edges()
    {
        var graph = FlowGraph.From(FlowDocs.Process("previsit.outreach"), WorkbenchLocale.En);
        graph.Edges.Where(edge => edge.IsBackEdge).Select(edge => (edge.From, edge.To)).Should().Contain([("reply", "cycle"), ("escalate", "cycle")]);
        graph.Edges.Single(edge => edge.From == "cycle" && edge.Label == "body").IsBackEdge.Should().BeFalse();
        graph.Edges.Single(edge => edge.From == "cycle" && edge.Label == "body").IsHappyPath.Should().BeTrue();
        graph.Edges.Single(edge => edge.From == "check" && edge.Label == "onError").IsHappyPath.Should().BeFalse();
        graph.Edges.Single(edge => edge.From == "assess" && edge.Label == "on:some").IsHappyPath.Should().BeTrue();
        graph.Nodes.Where(node => node.IsEnd).Select(node => node.Id).Should().BeEquivalentTo("done", "closed");
    }

    [Fact]
    public void Nodes_carry_the_stage_and_the_label_of_the_language()
    {
        var en = FlowGraph.From(FlowDocs.Process("case.lifecycle"), WorkbenchLocale.En);
        var es = FlowGraph.From(FlowDocs.Process("case.lifecycle"), WorkbenchLocale.Es);
        en.Nodes.Select(node => node.Id).Should().Equal(es.Nodes.Select(node => node.Id));
        en.Nodes.Select(node => node.Label).Should().NotEqual(es.Nodes.Select(node => node.Label));
        en.Nodes.Where(node => node.Stage is not null).Should().OnlyContain(node => node.StageIndex >= 0);
        en.Edges.Should().OnlyContain(edge => edge.Phrase.Length > 0);
    }

    [Fact]
    public void A_null_process_is_rejected()
    {
        FluentActions.Invoking(() => FlowGraph.From(null!, WorkbenchLocale.En)).Should().Throw<ArgumentNullException>();
    }
}

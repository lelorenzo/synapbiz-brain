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

            foreach (var edge in graph.Edges.Where(edge => !edge.IsBackEdge && edge.From == edge.To))
            {
                edge.Should().BeNull(name + ": a route to the step itself closes a cycle");
            }
        }
    }

    [Fact]
    public void The_loops_of_the_outreach_flow_come_back_as_back_edges()
    {
        var graph = FlowGraph.From(FlowDocs.Process("previsit.outreach"), WorkbenchLocale.En);
        graph.Edges.Where(edge => edge.IsBackEdge).Select(edge => (edge.From, edge.To)).Should().Contain([("reply", "cycle"), ("escalate", "cycle")]);
        graph.Edges.Single(edge => edge.From == "cycle" && edge.Label == "body").IsBackEdge.Should().BeTrue("the body leads back to the loop");
        var checkError = graph.Edges.Single(edge => edge.From == "check" && edge.Label == "onError");
        checkError.IsBackEdge.Should().Be(graph.Edges.Any(edge => edge.From == checkError.To) && graph.Edges.Where(edge => edge.IsBackEdge).Any(edge => edge.To == checkError.To), "an exit is a back edge only when its target can reach it again");
        graph.Edges.Single(edge => edge.From == "cycle" && edge.Label == "body").IsHappyPath.Should().BeTrue();
        graph.Edges.Single(edge => edge.From == "check" && edge.Label == "onError").IsHappyPath.Should().BeFalse();
        graph.Edges.Single(edge => edge.From == "assess" && edge.Label == "on:some").IsHappyPath.Should().BeTrue();
        graph.Nodes.Where(node => node.IsEnd).Select(node => node.Id).Should().BeEquivalentTo("done", "closed");
    }

    [Fact]
    public void A_back_edge_closes_a_cycle_and_an_exit_to_an_end_step_does_not()
    {
        var graph = FlowGraph.From(FlowDocs.Process("flow.batch"), WorkbenchLocale.En);
        graph.Edges.Where(edge => edge.IsBackEdge).Select(edge => (edge.From, edge.Label, edge.To)).Should().BeEquivalentTo([("each", "body", "child"), ("child", "next", "each")]);
        var exit = graph.Edges.Single(edge => edge.From == "child" && edge.Label == "onError");
        exit.To.Should().Be("broken");
        exit.IsBackEdge.Should().BeFalse();
    }

    [Fact]
    public void An_edge_between_nodes_of_equal_depth_is_not_a_back_edge_unless_it_closes_a_cycle()
    {
        var process = FlowDocs.FromJson(
            """
            {"$spec":"brain.process@1","id":"flow.diamond","version":1,"uses":[],"requires":[],"meta":{},
             "body":{"subject":"case","stages":[],"variables":{},"start":"a",
              "steps":{"a":{"kind":"wait","event":"e.one","timeout":"PT1H","next":"b","onTimeout":"c"},
                       "b":{"kind":"timer","after":"PT1M","next":"c"},
                       "c":{"kind":"end","result":"done"}},
              "scenarios":[]}}
            """);
        var graph = FlowGraph.From(process, WorkbenchLocale.En);
        var depths = graph.Nodes.ToDictionary(node => node.Id, node => node.Depth);
        depths["b"].Should().Be(depths["c"]);
        graph.Edges.Single(edge => edge.From == "b" && edge.To == "c").IsBackEdge.Should().BeFalse();
        graph.Edges.Should().OnlyContain(edge => !edge.IsBackEdge);
    }

    [Fact]
    public void A_route_to_the_step_itself_closes_a_cycle()
    {
        var process = FlowDocs.FromJson(
            """
            {"$spec":"brain.process@1","id":"flow.self","version":1,"uses":[],"requires":[],"meta":{},
             "body":{"subject":"case","stages":[],"variables":{},"start":"a",
              "steps":{"a":{"kind":"loop","maxIterations":2,"body":"a","onExhausted":"b"},
                       "b":{"kind":"end","result":"done"}},
              "scenarios":[]}}
            """);
        FlowGraph.From(process, WorkbenchLocale.En).Edges.Where(edge => edge.IsBackEdge).Select(edge => (edge.From, edge.To)).Should().Equal(("a", "a"));
    }

    [Fact]
    public void The_stage_index_is_the_position_in_the_list_of_stages()
    {
        var graph = FlowGraph.From(FlowDocs.Process("case.lifecycle"), WorkbenchLocale.En);
        var stages = ProcessOutline.From(FlowDocs.Process("case.lifecycle")).Stages;
        stages.Count.Should().BeGreaterThan(1);
        foreach (var node in graph.Nodes)
        {
            node.StageIndex.Should().Be(node.Stage is null ? -1 : stages.ToList().IndexOf(node.Stage));
        }

        graph.Nodes.Select(node => node.StageIndex).Distinct().Should().Contain(stages.Select((_, index) => index));
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

using System.Collections.ObjectModel;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>A step as a node of a diagram.</summary>
/// <param name="Id">The step id.</param>
/// <param name="Label">The label of the step.</param>
/// <param name="Kind">The kind of step.</param>
/// <param name="Stage">The stage id, or null.</param>
/// <param name="StageIndex">The position of the stage in the document's list of stages, or -1 when the step has none.</param>
/// <param name="Depth">The number of routes on the shortest path from the start step (0 for the start step).</param>
/// <param name="IsStart">True for the start step.</param>
/// <param name="IsEnd">True for an <c>end</c> step.</param>
public sealed record FlowNode(string Id, string Label, OutlineStepKind Kind, string? Stage, int StageIndex, int Depth, bool IsStart, bool IsEnd);

/// <summary>A route as an edge of a diagram.</summary>
/// <param name="From">The id of the step the route leaves.</param>
/// <param name="To">The id of the step it leads to.</param>
/// <param name="Label">The label key of the route (<c>next</c>, <c>onError</c>, <c>on:approved</c>, <c>interrupt</c> and so on).</param>
/// <param name="Phrase">The route in words.</param>
/// <param name="IsHappyPath">True for <c>next</c>, <c>body</c> and the <c>on:</c> entries; false for the <c>on...</c> members and <c>interrupt</c>.</param>
/// <param name="IsBackEdge">True when the edge closes a cycle: its target can reach its source by following routes (a route from a step to itself included). An edge that only leads to a node of the same or a lower depth, such as an error exit to an end node, is not one.</param>
public sealed record FlowEdge(string From, string To, string Label, string Phrase, bool IsHappyPath, bool IsBackEdge);

/// <summary>
/// The nodes and edges of a flow with a layering, so that a screen can draw it without a layout engine: the stage index of each node
/// and its depth from the start by shortest path. Edges that close a cycle (back edges) are flagged.
/// </summary>
/// <param name="Nodes">The nodes, in document order.</param>
/// <param name="Edges">The edges, in document order of their steps.</param>
public sealed record FlowGraph(IReadOnlyList<FlowNode> Nodes, IReadOnlyList<FlowEdge> Edges)
{
    /// <summary>Builds the graph of a flow.</summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="locale">The language of the labels and phrases.</param>
    /// <param name="knownProcesses">Other documents the caller knows, to word the result of a child process by the child's own labels; null for none.</param>
    public static FlowGraph From(BrainProcess process, WorkbenchLocale locale, IEnumerable<BrainProcess>? knownProcesses = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        var outline = ProcessOutline.From(process);
        var labels = new FlowLabels(process, locale, knownProcesses);
        var depth = Depths(outline);
        var reach = Reach(outline);
        var nodes = outline.Steps
            .Select(step => new FlowNode(
                step.Id,
                labels.Step(step.Id),
                step.Kind,
                step.Stage,
                step.Stage is null ? -1 : outline.Stages.ToList().IndexOf(step.Stage),
                depth[step.Id],
                string.Equals(step.Id, outline.Start, StringComparison.Ordinal),
                step.Kind == OutlineStepKind.End))
            .ToList();
        var edges = outline.Steps
            .SelectMany(step => step.Routes.Select(route => new FlowEdge(
                step.Id,
                route.Target,
                route.Label,
                FlowDescriber.RouteView(step, route, labels, locale).Phrase,
                route.Label is "next" or "body" || route.Label.StartsWith("on:", StringComparison.Ordinal),
                reach[route.Target].Contains(step.Id))))
            .ToList();
        return new FlowGraph(new ReadOnlyCollection<FlowNode>(nodes), new ReadOnlyCollection<FlowEdge>(edges));
    }

    // For each step, the steps it can reach by following routes, itself included (an edge closes a cycle when its target reaches its source).
    private static Dictionary<string, HashSet<string>> Reach(ProcessOutline outline)
    {
        var reach = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var origin in outline.Steps.Select(step => step.Id))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { origin };
            var queue = new Queue<string>([origin]);
            while (queue.TryDequeue(out var id))
            {
                foreach (var target in outline.Step(id)!.Routes.Select(route => route.Target).Where(seen.Add))
                {
                    queue.Enqueue(target);
                }
            }

            reach[origin] = seen;
        }

        return reach;
    }

    // Breadth-first from the start: every step of a loaded document is reachable, so every step has a depth.
    private static Dictionary<string, int> Depths(ProcessOutline outline)
    {
        var depth = new Dictionary<string, int>(StringComparer.Ordinal) { [outline.Start] = 0 };
        var queue = new Queue<string>([outline.Start]);
        while (queue.TryDequeue(out var id))
        {
            foreach (var target in outline.Step(id)!.Routes.Select(route => route.Target).Where(target => !depth.ContainsKey(target)))
            {
                depth[target] = depth[id] + 1;
                queue.Enqueue(target);
            }
        }

        return depth;
    }
}

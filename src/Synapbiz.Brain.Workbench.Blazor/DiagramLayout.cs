using System.Collections.ObjectModel;
using System.Globalization;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>The box of a step in the diagram, in SVG units.</summary>
internal sealed record NodeBox(string Id, int X, int Y, int Width, int Height, int Band, int Column);

/// <summary>The column band of a stage (<see cref="Index"/> is the position of the stage), or, when <see cref="IsLoose"/>, of the steps that have no stage.</summary>
internal sealed record BandBox(int Index, bool IsLoose, int X, int Width);

/// <summary>The line of a route: its SVG path, where its small label or mark sits, and whether it is a curve at the side.</summary>
internal sealed record EdgePath(FlowEdge Edge, string Path, int MidX, int MidY, bool IsCurve);

/// <summary>
/// Where everything of a flow's diagram goes, from the stage and the depth of each step and nothing else. The rule: one band for each
/// stage in document order, then one for the steps with no stage (only when there are some). In a band, a step is in the row of its
/// depth (<see cref="RowHeight"/> per level, below the heading), and the steps of one band and one depth are side by side in document
/// order. A band is as wide as its widest row plus room at the side for the curves of routes that go back. A route to a deeper
/// step is a straight line from the bottom of its step to the top of the other; a route that closes a cycle and leads to a
/// shallower step is a curve out of the right side of its step and back into the right side of the other (a small loop for a route to
/// the step itself); any other route is a straight line between the nearest sides, upward when the target is shallower.
/// </summary>
internal sealed record DiagramLayout(
    IReadOnlyList<BandBox> Bands,
    IReadOnlyList<NodeBox> Nodes,
    IReadOnlyList<EdgePath> Edges,
    int Width,
    int Height)
{
    internal const int NodeWidth = 168;
    internal const int NodeHeight = 52;
    internal const int ColumnGap = 20;
    internal const int RowHeight = 88;
    internal const int HeaderHeight = 34;
    internal const int Padding = 18;
    internal const int SideRoom = 48;
    private const int Bulge = 40;

    internal static DiagramLayout Of(FlowGraph graph, int stageCount)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var hasLoose = graph.Nodes.Any(node => node.StageIndex < 0);
        var bandCount = stageCount + (hasLoose ? 1 : 0);
        var bandOf = (FlowNode node) => node.StageIndex < 0 ? stageCount : node.StageIndex;

        var columns = new int[bandCount];
        var column = new Dictionary<string, int>(StringComparer.Ordinal);
        var counts = new Dictionary<(int Band, int Depth), int>();
        foreach (var node in graph.Nodes)
        {
            var key = (bandOf(node), node.Depth);
            var next = counts.GetValueOrDefault(key);
            counts[key] = next + 1;
            column[node.Id] = next;
            columns[key.Item1] = Math.Max(columns[key.Item1], next + 1);
        }

        var bands = new List<BandBox>();
        var x = 0;
        for (var index = 0; index < bandCount; index++)
        {
            var width = (2 * Padding) + (Math.Max(columns[index], 1) * NodeWidth) + (Math.Max(columns[index] - 1, 0) * ColumnGap) + SideRoom;
            bands.Add(new BandBox(index, index >= stageCount, x, width));
            x += width;
        }

        var nodes = graph.Nodes.Select(node =>
        {
            var band = bandOf(node);
            return new NodeBox(
                node.Id,
                bands[band].X + Padding + (column[node.Id] * (NodeWidth + ColumnGap)),
                HeaderHeight + Padding + (node.Depth * RowHeight),
                NodeWidth,
                NodeHeight,
                band,
                column[node.Id]);
        }).ToList();
        var boxes = nodes.ToDictionary(box => box.Id, StringComparer.Ordinal);
        var depths = graph.Nodes.ToDictionary(node => node.Id, node => node.Depth, StringComparer.Ordinal);
        var edges = graph.Edges.Select(edge => Edge(edge, boxes[edge.From], boxes[edge.To], depths[edge.From], depths[edge.To])).ToList();
        var maxDepth = graph.Nodes.Count == 0 ? 0 : graph.Nodes.Max(node => node.Depth);
        return new DiagramLayout(
            new ReadOnlyCollection<BandBox>(bands),
            new ReadOnlyCollection<NodeBox>(nodes),
            new ReadOnlyCollection<EdgePath>(edges),
            Math.Max(x, 1),
            HeaderHeight + (2 * Padding) + (maxDepth * RowHeight) + NodeHeight);
    }

    private static EdgePath Edge(FlowEdge edge, NodeBox from, NodeBox to, int fromDepth, int toDepth)
    {
        static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
        var fromRight = from.X + from.Width;
        var fromMid = from.Y + (from.Height / 2);
        if (edge.IsBackEdge && (toDepth < fromDepth || from.Id == to.Id))
        {
            if (from.Id == to.Id)
            {
                var path = $"M {N(fromRight)} {N(fromMid - 10)} C {N(fromRight + Bulge)} {N(fromMid - 10)} {N(fromRight + Bulge)} {N(fromMid + 10)} {N(fromRight)} {N(fromMid + 10)}";
                return new EdgePath(edge, path, fromRight + (Bulge * 3 / 4), fromMid, true);
            }

            var toRight = to.X + to.Width;
            var toMid = to.Y + (to.Height / 2);
            var far = Math.Max(fromRight, toRight) + Bulge;
            var curve = $"M {N(fromRight)} {N(fromMid)} C {N(far)} {N(fromMid)} {N(far)} {N(toMid)} {N(toRight)} {N(toMid)}";
            return new EdgePath(edge, curve, (fromRight + (6 * far) + toRight) / 8, (fromMid + toMid) / 2, true);
        }

        if (toDepth > fromDepth)
        {
            var x1 = from.X + (from.Width / 2);
            var y1 = from.Y + from.Height;
            var x2 = to.X + (to.Width / 2);
            return new EdgePath(edge, $"M {N(x1)} {N(y1)} L {N(x2)} {N(to.Y)}", (x1 + x2) / 2, (y1 + to.Y) / 2, false);
        }

        if (toDepth == fromDepth)
        {
            var toRight = to.X >= from.X;
            var x1 = toRight ? fromRight : from.X;
            var x2 = toRight ? to.X : to.X + to.Width;
            var y2 = to.Y + (to.Height / 2);
            return new EdgePath(edge, $"M {N(x1)} {N(fromMid)} L {N(x2)} {N(y2)}", (x1 + x2) / 2, (fromMid + y2) / 2, false);
        }

        var upX1 = from.X + (from.Width / 2);
        var upX2 = to.X + (to.Width / 2);
        var upY2 = to.Y + to.Height;
        return new EdgePath(edge, $"M {N(upX1)} {N(from.Y)} L {N(upX2)} {N(upY2)}", (upX1 + upX2) / 2, (from.Y + upY2) / 2, false);
    }
}

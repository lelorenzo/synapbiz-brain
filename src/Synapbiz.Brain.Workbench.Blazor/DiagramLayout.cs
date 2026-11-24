using System.Collections.ObjectModel;
using System.Globalization;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>A point of the diagram, in SVG units.</summary>
internal readonly record struct Spot(int X, int Y);

/// <summary>
/// The box of a step in the diagram, in SVG units, with the lines of its label (at most two, at most
/// <see cref="DiagramLayout.LineChars"/> characters each) and whether the label had to be cut with an ellipsis.
/// </summary>
internal sealed record NodeBox(string Id, int X, int Y, int Width, int Height, int Band, int Row, int Column, IReadOnlyList<string> Lines, bool IsCut);

/// <summary>The horizontal strip of a stage (<see cref="Index"/> is the position of the stage), or, when <see cref="IsLoose"/>, of the steps that have no stage. It spans the whole width of the diagram.</summary>
internal sealed record BandBox(int Index, bool IsLoose, int Y, int Height, int LabelX, int LabelY, int LabelWidth);

/// <summary>A mark drawn on the first segment of a line: the "not tried" sign or the count of the routes the line stands for.</summary>
internal sealed record LineMark(bool IsCount, int X, int Y);

/// <summary>Where the short label of a line is written.</summary>
internal enum LabelPlace
{
    /// <summary>The line has no label.</summary>
    None = 0,

    /// <summary>Beside the port the line leaves its step by, on its first segment.</summary>
    BesidePort = 1,

    /// <summary>On the first horizontal segment of the line that has room for it.</summary>
    OnSegment = 2,

    /// <summary>Nowhere on the line: a numbered mark stands for it and the text is listed under the diagram.</summary>
    Numbered = 3,
}

/// <summary>
/// One drawn line: every route between the same two steps (<see cref="Routes"/>, in document order) as one orthogonal polyline
/// (<see cref="Points"/>, its SVG form in <see cref="Path"/>), with the marks that sit on its first segment and where the short label
/// of a decision route is written: <see cref="ShortLabel"/> at <see cref="LabelAt"/> (its place is <see cref="Place"/>), or, when it
/// has no place on the line, <see cref="Number"/> at <see cref="NumberAt"/> with the text in <see cref="FullLabel"/>.
/// </summary>
internal sealed record DrawnLine(
    string From,
    string To,
    IReadOnlyList<FlowEdge> Routes,
    IReadOnlyList<Spot> Points,
    string Path,
    bool IsHappyPath,
    bool IsReturn,
    IReadOnlyList<LineMark> Marks,
    string? ShortLabel,
    Spot? LabelAt,
    bool LabelEndsAtPoint = false,
    string? FullLabel = null,
    LabelPlace Place = LabelPlace.None,
    int Number = 0,
    Spot? NumberAt = null)
{
    /// <summary>The segments of the line, each between two consecutive points.</summary>
    internal IEnumerable<(Spot A, Spot B)> Segments => Points.Zip(Points.Skip(1), (a, b) => (a, b));

    /// <summary>The width, in SVG units, of the text of the label as it is drawn; 0 when none is.</summary>
    internal int LabelWidth => (ShortLabel?.Length ?? 0) * DiagramLayout.CharWidth;

    /// <summary>The space the drawn label takes, as left, top, right and bottom; null when none is drawn.</summary>
    internal (int Left, int Top, int Right, int Bottom)? LabelBox
    {
        get
        {
            if (LabelAt is not { } at)
            {
                return null;
            }

            var left = LabelEndsAtPoint ? at.X - LabelWidth : at.X;
            return (left, at.Y - DiagramLayout.TextHalf, left + LabelWidth, at.Y + DiagramLayout.TextHalf);
        }
    }
}

/// <summary>
/// Where everything of a flow's diagram goes, by a rule with no search and no randomness (the reference page states it):
/// <list type="number">
/// <item>The stage bands are stacked vertically in stage order, a last band for the steps with no stage; each is a full-width strip
/// with its label at the top left. A stage with no step has no band.</item>
/// <item>The rank of a step is the longest path to it from the first step over the routes that do not return. A route returns when,
/// walking the routes depth-first from the first step in document order, its target is a step still being walked.</item>
/// <item>In a band the rows are the ranks of its steps squeezed in order (equal ranks share a row); an end step goes in the last row
/// of its band, one row lower when a route of the band leads to it from that row.</item>
/// <item>Column 0 of a band is its spine: from its first step by rank, the first happy-path route that stays in the band, again and
/// again. The other steps take columns 1, 2, ... in document order, row by row, so that no two steps share a row and a column. The
/// columns are the same in every band.</item>
/// <item>One line is drawn for each ordered pair of steps that has a route; its count and its phrases are on the line. A line runs
/// between boxes only: it leaves the bottom edge of its step at a port of its own, runs in the gutters between rows and columns, one
/// lane for each line that uses a stretch of a gutter, and enters the top edge of the other step. Two steps in the same column and
/// adjacent rows are joined by a straight vertical line. A route that returns, or leads to an earlier band, runs to a lane at the
/// right of the bands (one lane for each route) and enters the other step from its right side.</item>
/// <item>The "not tried" sign and the count of a line sit 14 units below its port, on its first segment. The short label of a decision
/// route is written, in this order of preference and always in the first place where no line, step, mark or other label touches its
/// text: beside the port (the leftmost labelled line of a step that has several lines tries the left of its port first, the others the
/// right of theirs); on the first horizontal segment of its own line that has room for it; beside the port again, cut with an ellipsis,
/// never below <see cref="MinLabelChars"/> characters (a label that has fewer is never cut). A label that has no such place becomes a
/// numbered mark on the first segment, and its text is listed under the diagram. A label longer than <see cref="MaxLabelChars"/> is cut
/// wherever it is written; the list under the diagram has all of it.</item>
/// </list>
/// </summary>
internal sealed record DiagramLayout(
    IReadOnlyList<BandBox> Bands,
    IReadOnlyList<NodeBox> Nodes,
    IReadOnlyList<DrawnLine> Lines,
    int Width,
    int Height)
{
    /// <summary>The lines whose label became a numbered mark, in the order of their numbers.</summary>
    internal IEnumerable<DrawnLine> NumberedLines => Lines.Where(line => line.Place == LabelPlace.Numbered).OrderBy(line => line.Number);

    internal const int NodeWidth = 184;
    internal const int NodeHeight = 50;
    internal const int LineChars = 22;
    internal const int MarkOffset = 14;
    internal const int MarkRadius = 8;
    internal const int LabelChars = 14;

    /// <summary>A label is never cut below this many characters (a shorter one is not cut at all).</summary>
    internal const int MinLabelChars = 8;

    /// <summary>A label longer than this is cut with an ellipsis wherever it is written; the numbered list under the diagram has all of it.</summary>
    internal const int MaxLabelChars = 24;

    /// <summary>Half the height of a line of text in a diagram, in SVG units: text is centred on its y.</summary>
    internal const int TextHalf = 7;
    internal const int Padding = 8;
    internal const int CharWidth = 7;
    private const int Lane = 8;
    private const int MinColumnGutter = 44;
    private const int FirstLane = 26;
    private const int LastLane = 30;
    private const int HeaderRoom = 40;
    private const int SideStart = 14;
    private const int SideStep = 10;
    private const int MarkStep = 18;

    /// <summary>The coordinates of the first row of a band are those of its header: the label sits at this height from the top of the band.</summary>
    internal const int LabelBaseline = 20;

    internal static DiagramLayout Of(FlowGraph graph, int stageCount, IReadOnlySet<string>? untried = null, IReadOnlyList<string>? stageLabels = null, string? looseLabel = null) =>
        new Builder(graph, stageCount, untried ?? new HashSet<string>(), stageLabels, looseLabel).Build();

    /// <summary>
    /// The lines of a label: up to two, at most <see cref="LineChars"/> characters each, broken at the space nearest the middle (a word
    /// longer than a line is broken inside). A label that does not fit two lines ends with an ellipsis.
    /// </summary>
    /// <param name="label">The full label.</param>
    internal static (IReadOnlyList<string> Lines, bool IsCut) Wrap(string label)
    {
        var text = string.Join(' ', label.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (text.Length <= LineChars)
        {
            return ([text], false);
        }

        var middle = text.Length / 2.0;
        var spaces = Enumerable.Range(0, text.Length).Where(index => text[index] == ' ').OrderBy(index => Math.Abs(index - middle)).ThenBy(index => index).ToList();
        foreach (var at in spaces)
        {
            if (at <= LineChars && text.Length - at - 1 <= LineChars)
            {
                return ([text[..at], text[(at + 1)..]], false);
            }
        }

        if (text.Length <= 2 * LineChars)
        {
            var cut = text[..LineChars];
            return ([cut, text[LineChars..]], false);
        }

        // Too long for two lines: the first line takes whole words while they fit, the second ends with an ellipsis.
        var firstEnd = text.LastIndexOf(' ', Math.Min(LineChars, text.Length - 1));
        if (firstEnd <= 0)
        {
            firstEnd = LineChars;
        }

        var first = text[..firstEnd];
        var rest = text[firstEnd..].TrimStart();
        var second = rest.Length <= LineChars ? rest : rest[..(LineChars - 1)] + "…";
        return ([first, second], true);
    }

    internal static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private enum Plan
    {
        Straight,
        Elbow,
        Via,
        Side,
    }

    private sealed class Pair
    {
        internal required string From { get; init; }

        internal required string To { get; init; }

        internal List<FlowEdge> Routes { get; } = [];

        internal bool Returns { get; set; }

        internal Plan Plan { get; set; }

        internal int SourceBand { get; set; }

        internal int SourceRow { get; set; }

        internal int SourceColumn { get; set; }

        internal int TargetBand { get; set; }

        internal int TargetRow { get; set; }

        internal int TargetColumn { get; set; }

        internal int Gutter { get; set; }

        internal int VerticalLane { get; set; }

        internal int SourceLane { get; set; }

        internal int TargetLane { get; set; }

        internal int SideLane { get; set; }

        internal int EntryLane { get; set; }

        internal int EntryGutter { get; set; }

        internal int Order { get; set; }

        internal int SourceX { get; set; }

        internal int NextPortX { get; set; } = int.MaxValue;

        internal int SourceLeft { get; set; }

        internal bool LabelLeft { get; set; }

        internal int TargetX { get; set; }

        internal int TargetY { get; set; }

        internal List<LineMark> Marks { get; } = [];
    }

    private sealed class Builder
    {
        private readonly FlowGraph graph;
        private readonly int stageCount;
        private readonly IReadOnlySet<string> untried;
        private readonly IReadOnlyList<string>? stageLabels;
        private readonly string? looseLabel;

        private readonly Dictionary<string, int> index = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> band = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> row = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> column = new(StringComparer.Ordinal);
        private List<int> bandStages = [];
        private int[] rowCount = [];
        private int columnCount;
        private readonly List<string> order = [];
        private readonly HashSet<(string From, string To)> numbered = [];
        private List<Pair> pairs = [];

        internal Builder(FlowGraph graph, int stageCount, IReadOnlySet<string> untried, IReadOnlyList<string>? stageLabels, string? looseLabel)
        {
            ArgumentNullException.ThrowIfNull(graph);
            this.graph = graph;
            this.stageCount = stageCount;
            this.untried = untried;
            this.stageLabels = stageLabels;
            this.looseLabel = looseLabel;
        }

        internal DiagramLayout Build()
        {
            for (var position = 0; position < graph.Nodes.Count; position++)
            {
                index[graph.Nodes[position].Id] = position;
            }

            var start = graph.Nodes.First(node => node.IsStart).Id;
            var returning = Returning(start);
            var rank = Ranks(start, returning);
            Place(rank, returning);
            pairs = Pairs(returning);
            Plans();
            var gutters = AllocateLanes();

            // A label with no place on its line becomes a numbered mark, and the mark needs a slot on the first segment of its line, so the
            // gutter below its row is taller. The geometry is worked out again with the slot until no other label needs one.
            while (true)
            {
                var (layout, fresh) = Geometry(gutters);
                if (fresh.Count == 0)
                {
                    return layout;
                }

                numbered.UnionWith(fresh);
            }
        }

        // ----- ranks and returning routes -----------------------------------------------------------------------------------

        private HashSet<(string, string)> Returning(string start)
        {
            var routes = graph.Edges.GroupBy(edge => edge.From, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Select(edge => edge.To).ToList(), StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal) { start };
            var onStack = new HashSet<string>(StringComparer.Ordinal) { start };
            var found = new HashSet<(string, string)>();
            order.Clear();
            var stack = new Stack<(string Id, int Next)>();
            stack.Push((start, 0));
            while (stack.Count > 0)
            {
                var (id, next) = stack.Pop();
                var outgoing = routes.GetValueOrDefault(id) ?? [];
                if (next >= outgoing.Count)
                {
                    onStack.Remove(id);
                    order.Add(id);
                    continue;
                }

                stack.Push((id, next + 1));
                var target = outgoing[next];
                if (onStack.Contains(target))
                {
                    found.Add((id, target));
                }
                else if (visited.Add(target))
                {
                    onStack.Add(target);
                    stack.Push((target, 0));
                }
            }

            return found;
        }

        private Dictionary<string, int> Ranks(string start, HashSet<(string, string)> returning)
        {
            var rank = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
            var outgoing = graph.Edges.GroupBy(edge => edge.From, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Select(edge => edge.To).ToList(), StringComparer.Ordinal);
            foreach (var id in Enumerable.Reverse(order))
            {
                foreach (var target in outgoing.GetValueOrDefault(id) ?? [])
                {
                    if (!returning.Contains((id, target)))
                    {
                        rank[target] = Math.Max(rank[target], rank[id] + 1);
                    }
                }
            }

            rank[start] = 0;
            return rank;
        }

        // ----- bands, rows and columns --------------------------------------------------------------------------------------

        private int BandOf(FlowNode node) => node.StageIndex < 0 ? stageCount : node.StageIndex;

        private void Place(Dictionary<string, int> rank, HashSet<(string, string)> returning)
        {
            var wanted = graph.Nodes.Select(BandOf).Distinct().Order().ToList();
            bandStages = wanted;
            rowCount = new int[wanted.Count];
            var maxColumns = 1;
            for (var position = 0; position < wanted.Count; position++)
            {
                var members = graph.Nodes.Where(node => BandOf(node) == wanted[position]).ToList();
                foreach (var node in members)
                {
                    band[node.Id] = position;
                }

                var rows = RowsOf(members, rank, returning);
                foreach (var (id, value) in rows)
                {
                    row[id] = value;
                }

                rowCount[position] = rows.Values.Max() + 1;
                var columns = ColumnsOf(members, rows, rank, returning, position);
                foreach (var (id, value) in columns)
                {
                    column[id] = value;
                }

                maxColumns = Math.Max(maxColumns, columns.Values.Max() + 1);
            }

            columnCount = maxColumns;
        }

        private Dictionary<string, int> RowsOf(List<FlowNode> members, Dictionary<string, int> rank, HashSet<(string, string)> returning)
        {
            var ids = members.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
            var rows = members.Where(node => !node.IsEnd).Select(node => rank[node.Id]).Distinct().Order().Select((value, position) => (value, position)).ToDictionary(pair => pair.value, pair => pair.position);
            var result = members.Where(node => !node.IsEnd).ToDictionary(node => node.Id, node => rows[rank[node.Id]], StringComparer.Ordinal);
            var ends = members.Where(node => node.IsEnd).ToList();
            if (ends.Count == 0)
            {
                return result;
            }

            var lastRow = result.Count == 0 ? 0 : result.Values.Max();
            if (result.Count > 0 && graph.Edges.Any(edge => ids.Contains(edge.From) && !returning.Contains((edge.From, edge.To)) && ends.Any(end => end.Id == edge.To) && result.TryGetValue(edge.From, out var from) && from >= lastRow))
            {
                lastRow++;
            }

            foreach (var end in ends)
            {
                result[end.Id] = lastRow;
            }

            return result;
        }

        private Dictionary<string, int> ColumnsOf(List<FlowNode> members, Dictionary<string, int> rows, Dictionary<string, int> rank, HashSet<(string, string)> returning, int bandIndex)
        {
            var ids = members.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);
            var taken = new HashSet<(int Row, int Column)>();
            var first = members.OrderBy(node => rank[node.Id]).ThenBy(node => index[node.Id]).First();
            var spine = first;
            while (true)
            {
                if (!taken.Add((rows[spine.Id], 0)))
                {
                    break;
                }

                columns[spine.Id] = 0;
                var next = graph.Edges
                    .Where(edge => edge.From == spine.Id && edge.IsHappyPath && ids.Contains(edge.To) && !returning.Contains((edge.From, edge.To)) && !columns.ContainsKey(edge.To))
                    .Select(edge => edge.To)
                    .FirstOrDefault();
                if (next is null)
                {
                    break;
                }

                spine = members.First(node => node.Id == next);
            }

            foreach (var id in members.Where(node => !columns.ContainsKey(node.Id)).Select(node => node.Id))
            {
                var at = 1;
                while (!taken.Add((rows[id], at)))
                {
                    at++;
                }

                columns[id] = at;
            }

            _ = bandIndex;
            return columns;
        }

        // ----- lines and their plans ----------------------------------------------------------------------------------------

        private List<Pair> Pairs(HashSet<(string, string)> returning)
        {
            var result = new List<Pair>();
            var byKey = new Dictionary<(string, string), Pair>();
            foreach (var edge in graph.Edges)
            {
                if (!byKey.TryGetValue((edge.From, edge.To), out var pair))
                {
                    pair = new Pair { From = edge.From, To = edge.To, Returns = returning.Contains((edge.From, edge.To)) };
                    byKey[(edge.From, edge.To)] = pair;
                    result.Add(pair);
                }

                pair.Routes.Add(edge);
            }

            foreach (var pair in result)
            {
                pair.SourceBand = band[pair.From];
                pair.SourceRow = row[pair.From];
                pair.SourceColumn = column[pair.From];
                pair.TargetBand = band[pair.To];
                pair.TargetRow = row[pair.To];
                pair.TargetColumn = column[pair.To];
            }

            return [.. result.OrderBy(pair => pair.SourceBand).ThenBy(pair => pair.SourceRow).ThenBy(pair => pair.SourceColumn).ThenBy(pair => index[pair.To])
                .Select((pair, position) =>
                {
                    pair.Order = position;
                    return pair;
                })];
        }

        private void Plans()
        {
            foreach (var pair in pairs)
            {
                var side = pair.Returns || pair.TargetBand < pair.SourceBand || (pair.TargetBand == pair.SourceBand && pair.TargetRow <= pair.SourceRow);
                if (side)
                {
                    pair.Plan = Plan.Side;
                    pair.Returns = true;
                }
                else if (pair.TargetBand == pair.SourceBand && pair.TargetRow == pair.SourceRow + 1)
                {
                    pair.Plan = pair.TargetColumn == pair.SourceColumn ? Plan.Straight : Plan.Elbow;
                }
                else
                {
                    pair.Plan = Plan.Via;
                    pair.Gutter = ViaGutter(pair);
                }
            }
        }

        private int ViaGutter(Pair pair)
        {
            var from = (2 * pair.SourceColumn) + 1;
            var to = (2 * pair.TargetColumn) + 1;
            return new[] { pair.SourceColumn, pair.SourceColumn + 1, pair.TargetColumn, pair.TargetColumn + 1 }
                .Where(gutter => gutter >= 1 && gutter <= columnCount)
                .OrderBy(gutter => Math.Abs((2 * gutter) - from) + Math.Abs((2 * gutter) - to))
                .ThenBy(gutter => gutter)
                .First();
        }

        // ----- lanes --------------------------------------------------------------------------------------------------------

        private int[] bandBase = [];

        private int GutterSlot(int bandIndex, int gutterRow) => bandBase[bandIndex] + (2 * gutterRow);

        private int RowSlot(int bandIndex, int rowIndex) => bandBase[bandIndex] + (2 * rowIndex) + 1;

        // The count of lanes of each gutter: the vertical ones by column gutter, the horizontal ones by (band, gutter row).
        private sealed record Gutters(int[] Vertical, Dictionary<(int Band, int Row), int> Horizontal, int Side);

        private Gutters AllocateLanes()
        {
            bandBase = new int[bandStages.Count + 1];
            for (var position = 0; position < bandStages.Count; position++)
            {
                bandBase[position + 1] = bandBase[position] + (2 * rowCount[position]) + 1;
            }

            var sidePosition = (2 * columnCount) + 2;
            var vertical = new Dictionary<int, List<(Pair Pair, int Start, int End, bool Entry)>>();
            var horizontal = new Dictionary<(int, int), List<(Pair Pair, int Start, int End, bool Second)>>();
            var sideIndex = 0;
            foreach (var pair in pairs)
            {
                var sourceGutter = (pair.SourceBand, pair.SourceRow + 1);
                var targetGutter = (pair.TargetBand, pair.TargetRow);
                var fromSlot = (2 * pair.SourceColumn) + 1;
                var toSlot = (2 * pair.TargetColumn) + 1;
                switch (pair.Plan)
                {
                    case Plan.Elbow:
                        Add(horizontal, sourceGutter, (pair, Math.Min(fromSlot, toSlot), Math.Max(fromSlot, toSlot), false));
                        break;
                    case Plan.Via:
                        Add(vertical, pair.Gutter, (pair, GutterSlot(sourceGutter.Item1, sourceGutter.Item2), GutterSlot(targetGutter.Item1, targetGutter.Item2), false));
                        Add(horizontal, sourceGutter, (pair, Math.Min(fromSlot, 2 * pair.Gutter), Math.Max(fromSlot, 2 * pair.Gutter), false));
                        Add(horizontal, targetGutter, (pair, Math.Min(toSlot, 2 * pair.Gutter), Math.Max(toSlot, 2 * pair.Gutter), true));
                        break;
                    case Plan.Side:
                        pair.SideLane = sideIndex++;
                        pair.EntryGutter = pair.TargetColumn + 1;
                        Add(horizontal, sourceGutter, (pair, fromSlot, sidePosition, false));
                        Add(horizontal, targetGutter, (pair, 2 * pair.EntryGutter, sidePosition, true));
                        Add(vertical, pair.EntryGutter, (pair, GutterSlot(targetGutter.Item1, targetGutter.Item2), RowSlot(pair.TargetBand, pair.TargetRow), true));
                        break;
                    default:
                        break;
                }
            }

            var verticalCounts = new int[columnCount + 1];
            foreach (var (gutter, requests) in vertical)
            {
                verticalCounts[gutter] = Colour(requests.Select(request => (request.Start, request.End)).ToList(), requests, (request, lane) =>
                {
                    if (request.Entry)
                    {
                        request.Pair.EntryLane = lane;
                    }
                    else
                    {
                        request.Pair.VerticalLane = lane;
                    }
                });
            }

            var horizontalCounts = new Dictionary<(int Band, int Row), int>();
            foreach (var (key, requests) in horizontal)
            {
                horizontalCounts[(key.Item1, key.Item2)] = Colour(requests.Select(request => (request.Start, request.End)).ToList(), requests, (request, lane) =>
                {
                    if (request.Second)
                    {
                        request.Pair.TargetLane = lane;
                    }
                    else
                    {
                        request.Pair.SourceLane = lane;
                    }
                });
            }

            return new Gutters(verticalCounts, horizontalCounts, sideIndex);
        }

        private static void Add<TKey, TValue>(Dictionary<TKey, List<TValue>> table, TKey key, TValue value)
            where TKey : notnull
        {
            if (!table.TryGetValue(key, out var list))
            {
                table[key] = list = [];
            }

            list.Add(value);
        }

        // Gives each request the first lane whose last request ends before it starts (the intervals are closed, so touching ones get two
        // lanes); returns the number of lanes used.
        private static int Colour<T>(List<(int Start, int End)> intervals, List<T> requests, Action<T, int> assign)
        {
            var ends = new List<int>();
            foreach (var position in Enumerable.Range(0, requests.Count).OrderBy(position => intervals[position].Start).ThenBy(position => intervals[position].End).ThenBy(position => position))
            {
                var lane = ends.FindIndex(end => end < intervals[position].Start);
                if (lane < 0)
                {
                    ends.Add(intervals[position].End);
                    lane = ends.Count - 1;
                }
                else
                {
                    ends[lane] = intervals[position].End;
                }

                assign(requests[position], lane);
            }

            return ends.Count;
        }

        // ----- geometry -----------------------------------------------------------------------------------------------------

        private (DiagramLayout Layout, List<(string From, string To)> Fresh) Geometry(Gutters gutters)
        {
            // How many marks the lines that leave a row have: the first lane sits lower when a line carries two.
            var marksBelow = new Dictionary<(int Band, int Row), int>();
            foreach (var pair in pairs)
            {
                var items = MarkItems(pair);
                var key = (pair.SourceBand, pair.SourceRow + 1);
                marksBelow[key] = Math.Max(marksBelow.GetValueOrDefault(key), items);
            }

            // Columns.
            var columnGutter = new int[columnCount + 1];
            for (var gutter = 0; gutter <= columnCount; gutter++)
            {
                var lanes = gutter < gutters.Vertical.Length ? gutters.Vertical[gutter] : 0;
                columnGutter[gutter] = Math.Max(MinColumnGutter, (2 * SideStart) + (Lane * Math.Max(lanes - 1, 0)));
            }

            var columnX = new int[columnCount];
            var gutterX = new int[columnCount + 1];
            var cursor = 0;
            for (var position = 0; position < columnCount; position++)
            {
                gutterX[position] = cursor;
                cursor += columnGutter[position];
                columnX[position] = cursor;
                cursor += NodeWidth;
            }

            gutterX[columnCount] = cursor;
            cursor += columnGutter[columnCount];
            var sideX = cursor;

            // Rows.
            var bandTop = new int[bandStages.Count];
            var bandHeight = new int[bandStages.Count];
            var gutterTop = new Dictionary<(int Band, int Row), int>();
            var rowTop = new Dictionary<(int Band, int Row), int>();
            var y = 0;
            for (var position = 0; position < bandStages.Count; position++)
            {
                bandTop[position] = y;
                for (var gutterRow = 0; gutterRow <= rowCount[position]; gutterRow++)
                {
                    var lanes = gutters.Horizontal.GetValueOrDefault((position, gutterRow));
                    var first = FirstLaneOf(gutterRow, marksBelow.GetValueOrDefault((position, gutterRow)));
                    var height = first + (Lane * Math.Max(lanes - 1, 0)) + LastLane;
                    gutterTop[(position, gutterRow)] = y;
                    y += height;
                    if (gutterRow < rowCount[position])
                    {
                        rowTop[(position, gutterRow)] = y;
                        y += NodeHeight;
                    }
                }

                bandHeight[position] = y - bandTop[position];
            }

            var nodes = graph.Nodes.Select(node =>
            {
                var (lines, cut) = Wrap(node.Label);
                var at = band[node.Id];
                return new NodeBox(node.Id, columnX[column[node.Id]], rowTop[(at, row[node.Id])], NodeWidth, NodeHeight, at, row[node.Id], column[node.Id], lines, cut);
            }).ToList();
            var boxes = nodes.ToDictionary(box => box.Id, StringComparer.Ordinal);

            int LaneY(int bandIndex, int gutterRow, int lane) =>
                gutterTop[(bandIndex, gutterRow)] + FirstLaneOf(gutterRow, marksBelow.GetValueOrDefault((bandIndex, gutterRow))) + (Lane * lane);

            int VerticalX(int gutter, int lane) => gutterX[gutter] + SideStart + (Lane * lane);

            // Ports.
            AssignPorts(boxes);

            var drawn = new List<DrawnLine>();
            foreach (var pair in pairs)
            {
                var source = boxes[pair.From];
                var target = boxes[pair.To];
                var bottom = source.Y + source.Height;
                var points = new List<Spot> { new(pair.SourceX, bottom) };
                var sourceLaneY = LaneY(pair.SourceBand, pair.SourceRow + 1, pair.SourceLane);
                var targetLaneY = LaneY(pair.TargetBand, pair.TargetRow, pair.TargetLane);
                switch (pair.Plan)
                {
                    case Plan.Straight:
                        points.Add(new Spot(pair.SourceX, target.Y));
                        break;
                    case Plan.Elbow:
                        points.Add(new Spot(pair.SourceX, sourceLaneY));
                        points.Add(new Spot(pair.TargetX, sourceLaneY));
                        points.Add(new Spot(pair.TargetX, target.Y));
                        break;
                    case Plan.Via:
                        var lane = VerticalX(pair.Gutter, pair.VerticalLane);
                        points.Add(new Spot(pair.SourceX, sourceLaneY));
                        points.Add(new Spot(lane, sourceLaneY));
                        points.Add(new Spot(lane, targetLaneY));
                        points.Add(new Spot(pair.TargetX, targetLaneY));
                        points.Add(new Spot(pair.TargetX, target.Y));
                        break;
                    default:
                        var side = sideX + SideStart + (SideStep * pair.SideLane);
                        var entry = VerticalX(pair.EntryGutter, pair.EntryLane);
                        points.Add(new Spot(pair.SourceX, sourceLaneY));
                        points.Add(new Spot(side, sourceLaneY));
                        points.Add(new Spot(side, targetLaneY));
                        points.Add(new Spot(entry, targetLaneY));
                        points.Add(new Spot(entry, pair.TargetY));
                        points.Add(new Spot(target.X + target.Width, pair.TargetY));
                        break;
                }

                drawn.Add(Draw(pair, Simplify(points), bottom));
            }

            var bands = new List<BandBox>();
            for (var position = 0; position < bandStages.Count; position++)
            {
                var isLoose = bandStages[position] >= stageCount;
                var label = looseLabel ?? string.Empty;
                if (!isLoose)
                {
                    label = stageLabels is not null && bandStages[position] < stageLabels.Count ? stageLabels[bandStages[position]] : string.Empty;
                }

                var labelWidth = label.Length * CharWidth;
                bands.Add(new BandBox(bandStages[position], isLoose, bandTop[position], bandHeight[position], Padding, bandTop[position] + LabelBaseline, labelWidth));
            }

            var (placed, fresh) = PlaceLabels(drawn, nodes, bands);
            var textRight = placed.Select(line => Math.Max(line.LabelBox?.Right ?? 0, line.NumberAt is { } at ? at.X + MarkRadius : 0)).DefaultIfEmpty(0).Max();
            var contentRight = Math.Max(nodes.Select(box => box.X + box.Width).DefaultIfEmpty(0).Max(), drawn.SelectMany(line => line.Points).Select(spot => spot.X).DefaultIfEmpty(0).Max());
            var labelRight = bands.Select(box => box.LabelX + box.LabelWidth).DefaultIfEmpty(0).Max();
            var width = Math.Max(Math.Max(contentRight, labelRight + Padding), textRight) + Padding;
            return (
                new DiagramLayout(
                    new ReadOnlyCollection<BandBox>(bands),
                    new ReadOnlyCollection<NodeBox>(nodes),
                    new ReadOnlyCollection<DrawnLine>(placed),
                    width,
                    y),
                fresh);
        }

        private static int FirstLaneOf(int gutterRow, int marks) =>
            (gutterRow == 0 ? HeaderRoom : FirstLane) + (MarkStep * Math.Max(marks - 1, 0));

        private int MarkItems(Pair pair) =>
            (pair.Routes.Any(route => untried.Contains(route.From + "/" + route.Label)) ? 1 : 0)
            + (pair.Routes.Count > 1 ? 1 : 0)
            + (numbered.Contains((pair.From, pair.To)) ? 1 : 0);

        // Ports are spread evenly along the edge of a step: the bottom edge for the lines that leave it, the top edge for the lines that
        // enter it from above and the right edge for the lines that enter it from the side. A straight line takes the middle of both.
        private void AssignPorts(Dictionary<string, NodeBox> boxes)
        {
            foreach (var box in boxes.Values)
            {
                var leaving = pairs.Where(pair => pair.From == box.Id).OrderBy(pair => pair.Plan == Plan.Side ? 1 : 0).ThenBy(pair => pair.TargetBand).ThenBy(pair => pair.TargetColumn).ThenBy(pair => pair.TargetRow).ThenBy(pair => pair.Order).ToList();
                var xs = Spread(box.X, leaving.Count, leaving.FindIndex(pair => pair.Plan == Plan.Straight));
                for (var position = 0; position < leaving.Count; position++)
                {
                    leaving[position].SourceX = xs[position];
                }

                var firstLabelled = leaving.Count > 1 ? leaving.Where(pair => pair.Routes.Any(route => route.ShortLabel is not null)).OrderBy(pair => pair.SourceX).FirstOrDefault() : null;
                foreach (var pair in leaving)
                {
                    pair.SourceLeft = box.X;
                    pair.LabelLeft = ReferenceEquals(pair, firstLabelled);
                    pair.NextPortX = leaving.Where(other => other.SourceX > pair.SourceX).Select(other => other.SourceX).DefaultIfEmpty(int.MaxValue).Min();
                }
            }

            foreach (var box in boxes.Values)
            {
                var entering = pairs.Where(pair => pair.To == box.Id && pair.Plan != Plan.Side).OrderBy(pair => pair.SourceBand).ThenBy(pair => pair.SourceColumn).ThenBy(pair => pair.SourceRow).ThenBy(pair => pair.Order).ToList();
                var tops = Spread(box.X, entering.Count, entering.FindIndex(pair => pair.Plan == Plan.Straight));
                var taken = pairs.Where(pair => pair.SourceBand == box.Band && pair.SourceRow == box.Row - 1 && pair.SourceColumn == box.Column).Select(pair => pair.SourceX).ToHashSet();
                for (var position = 0; position < entering.Count; position++)
                {
                    if (entering[position].Plan != Plan.Straight)
                    {
                        while (taken.Contains(tops[position]))
                        {
                            tops[position] += 3;
                        }
                    }

                    entering[position].TargetX = tops[position];
                }

                var sides = pairs.Where(pair => pair.To == box.Id && pair.Plan == Plan.Side).OrderBy(pair => pair.Order).ToList();
                for (var position = 0; position < sides.Count; position++)
                {
                    sides[position].TargetY = box.Y + (int)Math.Round(box.Height * (position + 1.0) / (sides.Count + 1), MidpointRounding.AwayFromZero);
                }
            }

            // A straight line is vertical: the port it leaves by is the port it enters at.
            foreach (var pair in pairs.Where(pair => pair.Plan == Plan.Straight))
            {
                pair.TargetX = pair.SourceX;
            }
        }

        private static int[] Spread(int left, int count, int middleIndex)
        {
            if (count == 0)
            {
                return [];
            }

            var slots = middleIndex >= 0 && count % 2 == 0 ? count + 1 : count;
            var positions = Enumerable.Range(0, slots).Select(slot => left + (int)Math.Round(NodeWidth * (slot + 1.0) / (slots + 1), MidpointRounding.AwayFromZero)).ToList();
            var result = new int[count];
            if (middleIndex < 0)
            {
                for (var position = 0; position < count; position++)
                {
                    result[position] = positions[position];
                }

                return result;
            }

            var centre = (slots - 1) / 2;
            var free = positions.Where((_, slot) => slot != centre).ToList();
            var taken = 0;
            for (var position = 0; position < count; position++)
            {
                result[position] = position == middleIndex ? positions[centre] : free[taken++];
            }

            return result;
        }

        private static List<Spot> Simplify(List<Spot> points)
        {
            var result = new List<Spot>();
            foreach (var point in points)
            {
                if (result.Count == 0 || result[^1] != point)
                {
                    result.Add(point);
                }
            }

            for (var position = result.Count - 2; position >= 1; position--)
            {
                var a = result[position - 1];
                var b = result[position];
                var c = result[position + 1];
                if ((a.X == b.X && b.X == c.X) || (a.Y == b.Y && b.Y == c.Y))
                {
                    result.RemoveAt(position);
                }
            }

            return result;
        }

        private DrawnLine Draw(Pair pair, List<Spot> points, int bottom)
        {
            var path = string.Join(' ', points.Select((point, position) => (position == 0 ? "M " : "L ") + N(point.X) + " " + N(point.Y)));
            var marks = new List<LineMark>();
            var at = bottom + MarkOffset;
            if (pair.Routes.Any(route => untried.Contains(route.From + "/" + route.Label)))
            {
                marks.Add(new LineMark(false, pair.SourceX, at));
            }

            if (pair.Routes.Count > 1)
            {
                marks.Add(new LineMark(true, pair.SourceX, at + (MarkStep * marks.Count)));
            }

            var shorts = pair.Routes.Select(route => route.ShortLabel).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            return new DrawnLine(
                pair.From,
                pair.To,
                pair.Routes,
                points,
                path,
                pair.Routes.Any(route => route.IsHappyPath),
                pair.Returns,
                marks,
                null,
                null,
                false,
                shorts.Count == 0 ? null : string.Join(", ", shorts));
        }

        // ----- labels -------------------------------------------------------------------------------------------------------

        private readonly record struct Rect(int Left, int Top, int Right, int Bottom)
        {
            internal bool Touches(Rect other) => Left <= other.Right && Right >= other.Left && Top <= other.Bottom && Bottom >= other.Top;
        }

        // Where every label is written (see the rule in the summary of the type). A label is only put where its text touches no step, no
        // band label, no line (its own other segments included), no mark and no label written before it.
        private (List<DrawnLine> Lines, List<(string From, string To)> Fresh) PlaceLabels(List<DrawnLine> drawn, List<NodeBox> nodes, List<BandBox> bands)
        {
            var obstacles = new List<Rect>();
            obstacles.AddRange(nodes.Select(box => new Rect(box.X, box.Y, box.X + box.Width, box.Y + box.Height)));
            obstacles.AddRange(bands.Select(box => new Rect(box.LabelX, box.LabelY - TextHalf, box.LabelX + box.LabelWidth, box.LabelY + TextHalf)));
            foreach (var line in drawn)
            {
                obstacles.AddRange(line.Segments.Select(segment => new Rect(Math.Min(segment.A.X, segment.B.X), Math.Min(segment.A.Y, segment.B.Y), Math.Max(segment.A.X, segment.B.X), Math.Max(segment.A.Y, segment.B.Y))));
                obstacles.AddRange(line.Marks.Select(mark => new Rect(mark.X - MarkRadius, mark.Y - MarkRadius, mark.X + MarkRadius, mark.Y + MarkRadius)));
            }

            // The marks of the lines already known to need one are laid first, so that no label is written over them.
            var reserved = new Dictionary<int, Spot>();
            for (var position = 0; position < drawn.Count; position++)
            {
                if (drawn[position].FullLabel is not null && numbered.Contains((pairs[position].From, pairs[position].To)))
                {
                    var at = MarkSpot(drawn[position], pairs[position], obstacles);
                    reserved[position] = at;
                    obstacles.Add(new Rect(at.X - MarkRadius, at.Y - MarkRadius, at.X + MarkRadius, at.Y + MarkRadius));
                }
            }

            var result = new List<DrawnLine>(drawn.Count);
            var fresh = new List<(string From, string To)>();
            var number = 0;
            for (var position = 0; position < drawn.Count; position++)
            {
                var line = drawn[position];
                var pair = pairs[position];
                if (line.FullLabel is not { } full)
                {
                    result.Add(line);
                    continue;
                }

                var key = (pair.From, pair.To);
                if (reserved.TryGetValue(position, out var reservedAt))
                {
                    number++;
                    result.Add(line with { Place = LabelPlace.Numbered, Number = number, NumberAt = reservedAt });
                    continue;
                }

                var chosen = Place(line, pair, full, obstacles);
                if (chosen is { } place)
                {
                    obstacles.Add(place.Box);
                    result.Add(line with { ShortLabel = place.Text, LabelAt = place.At, LabelEndsAtPoint = place.EndsAtPoint, Place = place.Kind });
                    continue;
                }

                number++;
                var markAt = MarkSpot(line, pair, obstacles);
                obstacles.Add(new Rect(markAt.X - MarkRadius, markAt.Y - MarkRadius, markAt.X + MarkRadius, markAt.Y + MarkRadius));
                fresh.Add(key);

                result.Add(line with { Place = LabelPlace.Numbered, Number = number, NumberAt = markAt });
            }

            return (result, fresh);
        }

        /// <summary>The place of a numbered mark: on the first segment of its line, as far up as it can go without touching a step, a mark or a label; when nothing there is free it keeps the place under the marks of its line.</summary>
        private static Spot MarkSpot(DrawnLine line, Pair pair, List<Rect> obstacles)
        {
            var first = line.Points[0];
            var last = line.Points[1];
            var own = line.Segments.Select(segment => new Rect(Math.Min(segment.A.X, segment.B.X), Math.Min(segment.A.Y, segment.B.Y), Math.Max(segment.A.X, segment.B.X), Math.Max(segment.A.Y, segment.B.Y))).ToList();
            var lowest = last.Y - MarkRadius;
            for (var y = first.Y + MarkOffset; y <= lowest; y += 2)
            {
                var box = new Rect(pair.SourceX - MarkRadius, y - MarkRadius, pair.SourceX + MarkRadius, y + MarkRadius);
                if (!obstacles.Exists(other => box.Touches(other) && !own.Contains(other)))
                {
                    return new Spot(pair.SourceX, y);
                }
            }

            return new Spot(pair.SourceX, first.Y + MarkOffset + (MarkStep * line.Marks.Count));
        }

        private readonly record struct Placement(string Text, Spot At, bool EndsAtPoint, LabelPlace Kind, Rect Box);

        private static bool Free(Rect box, List<Rect> obstacles) => box.Left >= Padding && box.Top >= 0 && !obstacles.Exists(box.Touches);

        private static Placement? Place(DrawnLine line, Pair pair, string full, List<Rect> obstacles)
        {
            var wanted = Cut(full, MaxLabelChars);
            var y = line.Points[0].Y + MarkOffset;
            var sides = pair.LabelLeft ? new[] { true, false } : [false, true];

            Placement? Beside(string text, bool left)
            {
                var width = text.Length * CharWidth;
                var x = left ? pair.SourceX - MarkRadius - 4 : pair.SourceX + MarkRadius + 4;
                var box = left ? new Rect(x - width, y - TextHalf, x, y + TextHalf) : new Rect(x, y - TextHalf, x + width, y + TextHalf);
                return Free(box, obstacles) ? new Placement(text, new Spot(x, y), left, LabelPlace.BesidePort, box) : null;
            }

            foreach (var left in sides)
            {
                if (Beside(wanted, left) is { } beside)
                {
                    return beside;
                }
            }

            var width = wanted.Length * CharWidth;
            foreach (var (a, b) in line.Segments.Where(segment => segment.A.Y == segment.B.Y))
            {
                var from = Math.Min(a.X, b.X);
                var to = Math.Max(a.X, b.X);
                for (var x = from + 6; x + width <= to - 6; x += 8)
                {
                    var box = new Rect(x, a.Y - 16, x + width, a.Y - 2);
                    if (Free(box, obstacles))
                    {
                        return new Placement(wanted, new Spot(x, a.Y - 9), false, LabelPlace.OnSegment, box);
                    }
                }
            }

            for (var chars = wanted.Length - 1; chars >= MinLabelChars && full.Length > MinLabelChars; chars--)
            {
                foreach (var left in sides)
                {
                    if (Beside(Cut(full, chars), left) is { } cut)
                    {
                        return cut;
                    }
                }
            }

            return null;
        }

        private static string Cut(string text, int chars) => text.Length <= chars ? text : text[..(chars - 1)] + "…";
    }
}

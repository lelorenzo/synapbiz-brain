using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>
/// The words written in a diagram: a step's text breaks only at spaces and clears its symbol, its folded corner and its markers; a
/// route label is never cut below eight characters, with the number written out here so that a change to the constant fails a test.
/// </summary>
public sealed class FlowDiagramTextTests
{
    private const int Symbol = 9 + 15;
    private const int Fold = 14;

    private static FlowNode Node(string id, string label, OutlineStepKind kind, bool start = false) => new(id, label, kind, null, -1, start ? 0 : 1, start, kind == OutlineStepKind.End);

    private static FlowGraph Fan(params string[] labels)
    {
        var nodes = new List<FlowNode> { Node("d", "Decide", OutlineStepKind.Decide, true) };
        var edges = new List<FlowEdge>();
        for (var index = 0; index < labels.Length; index++)
        {
            nodes.Add(Node("t" + index, "Target " + index, OutlineStepKind.End));
            edges.Add(new FlowEdge("d", "t" + index, "on:" + labels[index], "p", true, false) { ShortLabel = labels[index] });
        }

        return new FlowGraph(nodes, edges);
    }

    /// <summary>A graph with one step of every family and long labels: words that need two lines, three lines, more than three, and one word longer than a line.</summary>
    private static FlowGraph LongLabels()
    {
        string[] labels =
        [
            "Operaciones: problema previo a la visita",
            "Ask the patient to confirm the visit and the time",
            "Check the coverage again before the visit and tell the front desk what is still missing",
            "Supercalifragilisticexpialidocious",
            "Short",
            "Revisión de cobertura antes de la cita con el paciente",
        ];
        OutlineStepKind[] kinds = [OutlineStepKind.Action, OutlineStepKind.HumanTask, OutlineStepKind.Decide, OutlineStepKind.Subprocess, OutlineStepKind.Ai, OutlineStepKind.Wait];
        var nodes = labels.Select((label, index) => Node("s" + index, label, kinds[index], index == 0)).Append(Node("end", "Done and closed for this visit", OutlineStepKind.End)).ToList();
        var edges = Enumerable.Range(0, labels.Length).Select(index => new FlowEdge("s" + index, index + 1 < labels.Length ? "s" + (index + 1) : "end", "next", "p", true, false)).ToList();
        return new FlowGraph(nodes, edges);
    }

    public static TheoryData<string, WorkbenchLocale> EveryDocumentAndFixtureInBothLanguages()
    {
        var data = new TheoryData<string, WorkbenchLocale>();
        foreach (var locale in new[] { WorkbenchLocale.En, WorkbenchLocale.Es })
        {
            foreach (var (name, _) in FlowDocs.All)
            {
                data.Add(name, locale);
            }

            foreach (var process in MapDocs.All)
            {
                data.Add("map:" + process.Id, locale);
            }

            data.Add("fixture:long-labels", locale);
        }

        return data;
    }

    private static (DiagramLayout Layout, FlowGraph Graph) Of(string name, WorkbenchLocale locale)
    {
        if (name == "fixture:long-labels")
        {
            var graph = LongLabels();
            return (DiagramLayout.Of(graph, 0), graph);
        }

        if (name.StartsWith("map:", StringComparison.Ordinal))
        {
            var process = MapDocs.Process(name[4..]);
            var fromMap = FlowGraph.From(process, locale);
            return (DiagramLayout.Of(fromMap, ProcessOutline.From(process).Stages.Count), fromMap);
        }

        return FlowDiagramGeometryTests.Of(name, locale);
    }

    private static (int Left, int Top, int Right, int Bottom) TextBox(NodeBox box, FlowFamily family, int position)
    {
        var width = box.Lines[position].Length * DiagramLayout.CharWidth;
        var middle = box.X + (box.Width / 2) + DiagramLayout.TextShift(family);
        var y = box.Y + (box.Height / 2) + DiagramLayout.LineOffset(box.Lines.Count, position);
        return (middle - (width / 2), y - DiagramLayout.TextHalf, middle + (width / 2), y + DiagramLayout.TextHalf);
    }

    private static bool Touch((int Left, int Top, int Right, int Bottom) a, (int Left, int Top, int Right, int Bottom) b) =>
        a.Left <= b.Right && a.Right >= b.Left && a.Top <= b.Bottom && a.Bottom >= b.Top;

    [Theory]
    [MemberData(nameof(EveryDocumentAndFixtureInBothLanguages))]
    public void NoWordOfAStepIsSplitAcrossLinesAndOnlyAWordOrTheEndOfTheLabelIsCut(string name, WorkbenchLocale locale)
    {
        var (layout, graph) = Of(name, locale);
        var labels = graph.Nodes.ToDictionary(node => node.Id, node => node.Label, StringComparer.Ordinal);
        foreach (var box in layout.Nodes)
        {
            var who = name + "/" + box.Id;
            var written = box.Lines.SelectMany(line => line.Split(' ')).ToList();
            box.Lines.Should().OnlyContain(line => line.Length > 0 && line == line.Trim() && !line.Contains(" …", StringComparison.Ordinal), who);
            var original = labels[box.Id].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < written.Count; index++)
            {
                if (written[index] == original[index])
                {
                    continue;
                }

                written[index].Should().EndWith("…", who + " a word that is not whole ends with an ellipsis");
                original[index].Should().StartWith(written[index][..^1], who);
                (index == written.Count - 1 || original[index].Length > DiagramLayout.SymbolChars).Should().BeTrue(who + " only the last word, or a word longer than a line, is cut");
            }

            if (written.Count < original.Length)
            {
                box.IsCut.Should().BeTrue(who);
                written[^1].Should().EndWith("…", who);
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryDocumentAndFixtureInBothLanguages))]
    public void TheTextOfAStepClearsItsSymbolItsFoldedCornerAndTheMarkersOfTheStepAndStaysInsideTheBox(string name, WorkbenchLocale locale)
    {
        var (layout, graph) = Of(name, locale);
        var families = graph.Nodes.ToDictionary(node => node.Id, node => node.Family, StringComparer.Ordinal);
        foreach (var box in layout.Nodes)
        {
            var family = families[box.Id];
            var who = name + "/" + box.Id;
            var here = (box.X + box.Width - DiagramLayout.HereInset - DiagramLayout.HereRadius, box.Y + DiagramLayout.HereInset - DiagramLayout.HereRadius, box.X + box.Width - DiagramLayout.HereInset + DiagramLayout.HereRadius, box.Y + DiagramLayout.HereInset + DiagramLayout.HereRadius);
            var seen = (box.X + 12 - 9, box.Y + box.Height - 12 - 9, box.X + 12 + 9, box.Y + box.Height - 12 + 9);
            var start = (box.X + 12 - 5, box.Y + 12 - 5, box.X + 12 + 5, box.Y + 12 + 5);
            for (var position = 0; position < box.Lines.Count; position++)
            {
                var text = TextBox(box, family, position);
                Touch(text, here).Should().BeFalse(who + " text touches the marker of the current step");
                Touch(text, seen).Should().BeFalse(who + " text touches the number of a visited step");
                text.Top.Should().BeGreaterThan(box.Y, who);
                text.Bottom.Should().BeLessThan(box.Y + box.Height, who);
                text.Right.Should().BeLessThanOrEqualTo(box.X + box.Width - (family == FlowFamily.Person ? Fold + 2 : 4), who);
                if (family != FlowFamily.End)
                {
                    text.Left.Should().BeGreaterThanOrEqualTo(box.X + Symbol + 4, who + " text touches the symbol");
                    Touch(text, start).Should().BeFalse(who + " text touches the start dot");
                }
            }
        }
    }

    [Fact]
    public void ALongLabelTakesThreeLinesInATallerStepAndTheRowIsAsTallAsItsTallestStep()
    {
        var layout = DiagramLayout.Of(LongLabels(), 0);
        var byId = layout.Nodes.ToDictionary(box => box.Id, StringComparer.Ordinal);
        byId["s0"].Lines.Should().Equal("Operaciones:", "problema previo", "a la visita");
        byId["s0"].Height.Should().Be(DiagramLayout.TallNodeHeight);
        byId["s4"].Lines.Should().Equal("Short");
        byId["s4"].Height.Should().Be(DiagramLayout.NodeHeight);
        byId["s3"].Lines.Should().ContainSingle().Which.Should().Be("Supercalifragilisti…");
        layout.Nodes.Select(box => box.Y + box.Height).Max().Should().BeLessThanOrEqualTo(layout.Height);
    }

    [Fact]
    public void ALabelIsCutAtAWordBoundaryWhenThatKeepsEightCharactersAndNeverAfterASpace()
    {
        DiagramLayout.Cut("Payment arrangement agreed", 24).Should().Be("Payment arrangement…");
        DiagramLayout.Cut("Payment arrangement agreed", 24).Should().NotContain(" …");
        DiagramLayout.Cut("Declined entirely by the patient", 24).Should().Be("Declined entirely by…");
        DiagramLayout.Cut("Abcdefghijklmnopqrstuvwxyz", 10).Should().Be("Abcdefghi…");
        DiagramLayout.Cut("Ab cdefghijklmnopqrstuvwxyz", 10).Should().Be("Ab cdefgh…", "a cut at the space would keep three characters, fewer than eight");
        DiagramLayout.Cut("Abcdefg hijklmnopqrstuvwxyz", 9).Should().Be("Abcdefg…", "a space just where the ellipsis would go is a word boundary");
        DiagramLayout.Cut("Short", 24).Should().Be("Short");
        DiagramLayout.Cut("Abcdefg hijklmnopqrstuvwxyz", 9).Should().NotContain(" …");
    }

    [Fact]
    public void TheLeastLabelLengthIsEightCharactersWrittenOutHere()
    {
        DiagramLayout.MinLabelChars.Should().Be(8);
        DiagramLayout.MaxLabelChars.Should().Be(24);
    }

    [Fact]
    public void ALabelThatDoesNotFitItsLineIsCutToAtLeastEightCharactersAndTheFullTextIsKept()
    {
        var layout = DiagramLayout.Of(Fan("Needs a follow up call this week", "Needs a second opinion before", "Payment arrangement agreed", "Wait", "Other"), 0);
        var cut = layout.Lines.Where(line => line.ShortLabel is not null && line.ShortLabel != line.FullLabel).ToList();
        cut.Should().NotBeEmpty("a label longer than 24 characters is cut wherever it is written");
        foreach (var line in cut)
        {
            line.ShortLabel!.Length.Should().BeGreaterThanOrEqualTo(8);
            line.ShortLabel.Should().EndWith("…").And.NotContain(" …");
            line.FullLabel!.Should().StartWith(line.ShortLabel[..^1].TrimEnd());
        }

        layout.Lines.Single(line => line.FullLabel == "Payment arrangement agreed").ShortLabel.Should().Be("Payment arrangement…");
        layout.Lines.Single(line => line.FullLabel == "Wait").ShortLabel.Should().Be("Wait", "a label that has fewer than eight characters is never cut");
    }

    [Fact]
    public void ALabelThatHasNoPlaceBecomesANumberedMarkWhoseTextIsListedUnderTheDiagram()
    {
        var layout = DiagramLayout.Of(Fan("Needs a follow up call this week", "Declined", "Wait"), 0);
        var numbered = layout.NumberedLines.ToList();
        numbered.Should().NotBeEmpty();
        numbered.Select(line => line.Number).Should().Equal(Enumerable.Range(1, numbered.Count));
        numbered.Should().OnlyContain(line => line.ShortLabel == null && line.LabelAt == null && line.NumberAt != null && line.FullLabel!.Length > 8);
        numbered.Select(line => line.FullLabel).Should().Contain("Needs a follow up call this week");
    }
}

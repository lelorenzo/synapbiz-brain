using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>
/// The geometric rules of the diagram, checked on every document of the tests in both languages. Only one flow's coordinates are
/// pinned (see <see cref="FlowDiagramTests"/>): for the others the rule is what is tested.
/// </summary>
public sealed class FlowDiagramGeometryTests
{
    public static TheoryData<string, WorkbenchLocale> EveryDocumentInBothLanguages()
    {
        var data = new TheoryData<string, WorkbenchLocale>();
        foreach (var (name, _) in FlowDocs.All)
        {
            data.Add(name, WorkbenchLocale.En);
            data.Add(name, WorkbenchLocale.Es);
        }

        return data;
    }

    public static TheoryData<string> TheReferenceDocuments()
    {
        var data = new TheoryData<string>();
        foreach (var name in FlowDocs.ReferenceIds)
        {
            data.Add(name);
        }

        return data;
    }

    internal static (DiagramLayout Layout, FlowGraph Graph) Of(string name, WorkbenchLocale locale, bool everyRouteUntried = false)
    {
        var process = FlowDocs.Process(name);
        var graph = FlowGraph.From(process, locale);
        IReadOnlySet<string>? untried = everyRouteUntried ? graph.Edges.Select(edge => edge.From + "/" + edge.Label).ToHashSet(StringComparer.Ordinal) : null;
        return (DiagramLayout.Of(graph, ProcessOutline.From(process).Stages.Count, untried), graph);
    }

    private static Dictionary<string, NodeBox> Boxes(DiagramLayout layout) => layout.Nodes.ToDictionary(box => box.Id, StringComparer.Ordinal);

    /// <summary>True when the segment goes through the inside of the box (touching its edge is not going through it).</summary>
    private static bool Crosses((Spot A, Spot B) segment, NodeBox box)
    {
        var left = Math.Min(segment.A.X, segment.B.X);
        var right = Math.Max(segment.A.X, segment.B.X);
        var top = Math.Min(segment.A.Y, segment.B.Y);
        var bottom = Math.Max(segment.A.Y, segment.B.Y);
        return left < box.X + box.Width && right > box.X && top < box.Y + box.Height && bottom > box.Y
            && !(left == right && (left <= box.X || left >= box.X + box.Width))
            && !(top == bottom && (top <= box.Y || top >= box.Y + box.Height));
    }

    [Theory]
    [MemberData(nameof(EveryDocumentInBothLanguages))]
    public void NoTwoStepsOverlapAndEveryStepIsInsideItsBandAndTheDiagram(string name, WorkbenchLocale locale)
    {
        var (layout, graph) = Of(name, locale);
        layout.Nodes.Should().HaveCount(graph.Nodes.Count);
        foreach (var box in layout.Nodes)
        {
            var band = layout.Bands[box.Band];
            box.Y.Should().BeGreaterThanOrEqualTo(band.Y);
            (box.Y + box.Height).Should().BeLessThanOrEqualTo(band.Y + band.Height);
            box.X.Should().BeGreaterThanOrEqualTo(DiagramLayout.Padding);
            (box.X + box.Width).Should().BeLessThanOrEqualTo(layout.Width - DiagramLayout.Padding);
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
    [MemberData(nameof(EveryDocumentInBothLanguages))]
    public void TheBandsAreStackedInOrderAndFillTheHeightAndTheLooseOneIsLast(string name, WorkbenchLocale locale)
    {
        var (layout, _) = Of(name, locale);
        layout.Bands.Should().NotBeEmpty();
        for (var position = 1; position < layout.Bands.Count; position++)
        {
            layout.Bands[position].Y.Should().BeGreaterThanOrEqualTo(layout.Bands[position - 1].Y + layout.Bands[position - 1].Height);
        }

        layout.Bands.Take(layout.Bands.Count - 1).Any(band => band.IsLoose).Should().BeFalse();
        (layout.Bands[^1].Y + layout.Bands[^1].Height).Should().Be(layout.Height);
        layout.Bands.Should().OnlyContain(band => band.LabelY > band.Y && band.LabelY < band.Y + band.Height);
    }

    [Theory]
    [MemberData(nameof(EveryDocumentInBothLanguages))]
    public void EveryLineIsOrthogonalLeavesTheBottomOfItsStepAndEntersTheTopOrTheSideOfTheOther(string name, WorkbenchLocale locale)
    {
        var (layout, _) = Of(name, locale);
        var boxes = Boxes(layout);
        foreach (var line in layout.Lines)
        {
            var label = line.From + ">" + line.To;
            line.Points.Count.Should().BeGreaterThanOrEqualTo(2, label);
            line.Segments.Should().OnlyContain(segment => segment.A.X == segment.B.X || segment.A.Y == segment.B.Y, label);
            line.Segments.Should().OnlyContain(segment => segment.A != segment.B, label);

            var from = boxes[line.From];
            var to = boxes[line.To];
            var start = line.Points[0];
            start.Y.Should().Be(from.Y + from.Height, label);
            start.X.Should().BeInRange(from.X + 1, from.X + from.Width - 1, label);

            var end = line.Points[^1];
            var onTop = end.Y == to.Y && end.X > to.X && end.X < to.X + to.Width;
            var onSide = end.X == to.X + to.Width && end.Y > to.Y && end.Y < to.Y + to.Height;
            (onTop || onSide).Should().BeTrue(label + " ends at " + end);
            if (!line.IsReturn)
            {
                onTop.Should().BeTrue(label);
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryDocumentInBothLanguages))]
    public void NoLineGoesThroughTheInsideOfAStepAndNoLineLeavesTheDiagram(string name, WorkbenchLocale locale)
    {
        var (layout, _) = Of(name, locale);
        foreach (var line in layout.Lines)
        {
            foreach (var segment in line.Segments)
            {
                foreach (var box in layout.Nodes)
                {
                    Crosses(segment, box).Should().BeFalse(line.From + ">" + line.To + " crosses " + box.Id);
                }
            }

            line.Points.Should().OnlyContain(spot => spot.X >= 0 && spot.X <= layout.Width && spot.Y >= 0 && spot.Y <= layout.Height);
        }
    }

    [Theory]
    [MemberData(nameof(EveryDocumentInBothLanguages))]
    public void TwoLinesNeverRunAlongTheSameStretch(string name, WorkbenchLocale locale)
    {
        var (layout, _) = Of(name, locale);
        for (var first = 0; first < layout.Lines.Count; first++)
        {
            for (var second = first + 1; second < layout.Lines.Count; second++)
            {
                foreach (var a in layout.Lines[first].Segments)
                {
                    foreach (var b in layout.Lines[second].Segments)
                    {
                        Overlap(a, b).Should().BeFalse(layout.Lines[first].From + ">" + layout.Lines[first].To + " and " + layout.Lines[second].From + ">" + layout.Lines[second].To);
                    }
                }
            }
        }
    }

    private static bool Overlap((Spot A, Spot B) a, (Spot A, Spot B) b)
    {
        var verticalA = a.A.X == a.B.X;
        var verticalB = b.A.X == b.B.X;
        if (verticalA != verticalB)
        {
            return false;
        }

        if (verticalA)
        {
            return a.A.X == b.A.X && Math.Min(Math.Max(a.A.Y, a.B.Y), Math.Max(b.A.Y, b.B.Y)) > Math.Max(Math.Min(a.A.Y, a.B.Y), Math.Min(b.A.Y, b.B.Y));
        }

        return a.A.Y == b.A.Y && Math.Min(Math.Max(a.A.X, a.B.X), Math.Max(b.A.X, b.B.X)) > Math.Max(Math.Min(a.A.X, a.B.X), Math.Min(b.A.X, b.B.X));
    }

    [Theory]
    [MemberData(nameof(EveryDocumentInBothLanguages))]
    public void ThereIsOneLineForEachOrderedPairOfStepsThatHasARouteAndTheCountsAddUp(string name, WorkbenchLocale locale)
    {
        var (layout, graph) = Of(name, locale);
        var pairs = graph.Edges.Select(edge => (edge.From, edge.To)).Distinct().ToList();
        layout.Lines.Select(line => (line.From, line.To)).Should().BeEquivalentTo(pairs);
        layout.Lines.Sum(line => line.Routes.Count).Should().Be(graph.Edges.Count);
        foreach (var line in layout.Lines)
        {
            line.Routes.Should().OnlyContain(edge => edge.From == line.From && edge.To == line.To);
            layout.Lines.Count(other => other.From == line.From && other.To == line.To).Should().Be(1);
        }
    }

    [Theory]
    [MemberData(nameof(EveryDocumentInBothLanguages))]
    public void TheMarksSitOnTheFirstSegmentTheFirstOneFourteenBelowThePortAndAMultipleLineHasACountMark(string name, WorkbenchLocale locale)
    {
        var (layout, _) = Of(name, locale, everyRouteUntried: true);
        foreach (var line in layout.Lines)
        {
            var label = line.From + ">" + line.To;
            line.Marks.Count(mark => mark.IsCount).Should().Be(line.Routes.Count > 1 ? 1 : 0, label);
            line.Marks.Count(mark => !mark.IsCount).Should().Be(1, label);
            var first = line.Points[0];
            var next = line.Points[1];
            next.X.Should().Be(first.X, label + " leaves its step straight down");
            line.Marks.Should().OnlyContain(mark => mark.X == first.X, label);
            line.Marks.Min(mark => mark.Y).Should().Be(first.Y + DiagramLayout.MarkOffset, label);
            line.Marks.Select(mark => mark.Y).Distinct().Should().HaveCount(line.Marks.Count, label);
            line.Marks.Max(mark => mark.Y + DiagramLayout.MarkRadius).Should().BeLessThanOrEqualTo(next.Y, label + " keeps its marks on the first segment");
        }
    }

    [Fact]
    public void ThePrevisitOutreachCoordinatesArePinnedForThisOneFlowAndTheRuleIsTestedForAllOfThem()
    {
        var (layout, _) = Of("previsit.outreach", WorkbenchLocale.En);
        layout.Width.Should().Be(1004);
        layout.Height.Should().Be(810);
        layout.Nodes.Select(box => $"{box.Id}:{box.X},{box.Y}").Should().Equal(
        "cycle:44,78",
        "check:44,184",
        "assess:44,298",
        "send:288,420",
        "reply:288,566",
        "escalate:516,566",
        "escalate_final:744,566",
        "done:44,704",
        "closed:288,704");
        layout.Lines.Select(line => $"{line.From}>{line.To}:{line.Path}").Should().Equal(
        "cycle>check:M 136 128 L 136 184",
        "cycle>escalate_final:M 90 128 L 90 154 L 242 154 L 242 512 L 790 512 L 790 566",
        "check>assess:M 136 234 L 136 298",
        "check>escalate:M 90 234 L 90 260 L 250 260 L 250 496 L 562 496 L 562 566",
        "check>escalate_final:M 182 234 L 182 268 L 258 268 L 258 520 L 836 520 L 836 566",
        "assess>send:M 136 348 L 136 390 L 380 390 L 380 420",
        "assess>escalate:M 182 348 L 182 374 L 266 374 L 266 504 L 608 504 L 608 566",
        "assess>done:M 90 348 L 90 382 L 274 382 L 274 642 L 105 642 L 105 704",
        "send>reply:M 380 470 L 380 566",
        "send>escalate:M 334 470 L 334 528 L 654 528 L 654 566",
        "send>escalate_final:M 426 470 L 426 536 L 882 536 L 882 566",
        "reply>cycle:M 380 616 L 380 666 L 986 666 L 986 40 L 242 40 L 242 95 L 228 95",
        "escalate>cycle:M 639 616 L 639 674 L 996 674 L 996 48 L 250 48 L 250 111 L 228 111",
        "escalate>closed:M 577 616 L 577 642 L 349 642 L 349 704",
        "escalate_final>done:M 805 616 L 805 650 L 167 650 L 167 704",
        "escalate_final>closed:M 867 616 L 867 658 L 411 658 L 411 704");
        layout.Lines.Where(line => line.LabelAt is not null).Select(line => $"{line.From}>{line.To}:{line.ShortLabel}@{line.LabelAt!.Value.X},{line.LabelAt.Value.Y}").Should().Equal(
        "assess>send:some@148,362",
        "assess>done:none@102,362",
        "escalate>cycle:Resolved@651,630",
        "escalate>closed:Unreachable@589,630",
        "escalate_final>done:Resolved@817,630",
        "escalate_final>closed:Unreachable@879,630");
        layout.Lines.Where(line => line.Marks.Count > 0).Select(line => $"{line.From}>{line.To}:{string.Join(";", line.Marks.Select(mark => $"{(mark.IsCount ? "count" : "untried")}@{mark.X},{mark.Y}"))}").Should().Equal(
        "assess>escalate:count@182,362",
        "send>escalate:count@334,484",
        "send>escalate_final:count@426,484",
        "reply>cycle:count@380,630",
        "escalate>closed:count@577,630",
        "escalate_final>closed:count@867,630");
    }

    [Fact]
    public void TheWidestReferenceFlowFitsInElevenHundredUnitsAndTheViewBoxIsSizedToTheContent()
    {
        foreach (var name in FlowDocs.ReferenceIds)
        {
            var (layout, _) = Of(name, WorkbenchLocale.En);
            layout.Width.Should().BeLessThanOrEqualTo(1100, name);
            var text = layout.Lines.Where(line => line.LabelAt is not null).Select(line => line.LabelAt!.Value.X + (line.ShortLabel!.Length * DiagramLayout.CharWidth)).DefaultIfEmpty(0).Max();
            var right = new[] { layout.Nodes.Max(box => box.X + box.Width), layout.Lines.SelectMany(line => line.Points).Max(spot => spot.X), text }.Max();
            layout.Width.Should().Be(right + DiagramLayout.Padding, name);
        }

        Of("case.lifecycle", WorkbenchLocale.En).Layout.Width.Should().BeLessThanOrEqualTo(1100);
    }

    [Theory]
    [MemberData(nameof(EveryDocumentInBothLanguages))]
    public void TheLayoutIsTheSameEveryTimeAndTheLabelsOfTheStepsFitTheirBoxes(string name, WorkbenchLocale locale)
    {
        var (first, _) = Of(name, locale);
        var (second, _) = Of(name, locale);
        second.Nodes.Select(box => $"{box.Id}:{box.X},{box.Y},{string.Join("|", box.Lines)}").Should().Equal(first.Nodes.Select(box => $"{box.Id}:{box.X},{box.Y},{string.Join("|", box.Lines)}"));
        second.Lines.Select(line => line.Path).Should().Equal(first.Lines.Select(line => line.Path));
        first.Nodes.Should().OnlyContain(box => box.Lines.Count >= 1 && box.Lines.Count <= 2 && box.Lines.All(text => text.Length <= DiagramLayout.LineChars));
    }

    [Fact]
    public void ALabelIsBrokenAtTheSpaceNearestTheMiddleAndOnlyTheLongOnesAreCut()
    {
        DiagramLayout.Wrap("Short").Lines.Should().Equal("Short");
        DiagramLayout.Wrap("Short").IsCut.Should().BeFalse();
        DiagramLayout.Wrap("Check what is missing").Lines.Should().Equal("Check what is missing");
        var two = DiagramLayout.Wrap("Decide whether to ask the assistant");
        two.Lines.Should().Equal("Decide whether to", "ask the assistant");
        two.IsCut.Should().BeFalse();
        var cut = DiagramLayout.Wrap("A very long label that cannot possibly fit in two lines of the box");
        cut.IsCut.Should().BeTrue();
        cut.Lines.Should().HaveCount(2).And.OnlyContain(text => text.Length <= DiagramLayout.LineChars);
        cut.Lines[1].Should().EndWith("…");
        var word = DiagramLayout.Wrap("Supercalifragilisticexpialidocious");
        word.Lines.Should().HaveCount(2).And.OnlyContain(text => text.Length <= DiagramLayout.LineChars);
    }
}

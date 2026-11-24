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
        layout.Lines.Where(line => line.LabelAt is not null).Select(line => $"{line.From}>{line.To}:{line.ShortLabel}@{line.LabelAt!.Value.X},{line.LabelAt.Value.Y}{(line.LabelEndsAtPoint ? " end" : string.Empty)} {line.Place}").Should().Equal(
        "assess>send:some@124,362 end BesidePort",
        "assess>done:none@78,362 end BesidePort",
        "escalate>cycle:Resolved@651,630 BesidePort",
        "escalate>closed:Unreachable@565,630 end BesidePort",
        "escalate_final>done:Resolved@793,630 end BesidePort",
        "escalate_final>closed:Unreachable@879,630 BesidePort");
        layout.Lines.Count(line => line.Place == LabelPlace.Numbered).Should().Be(0);
        layout.Lines.Where(line => line.Marks.Count > 0).Select(line => $"{line.From}>{line.To}:{string.Join(";", line.Marks.Select(mark => $"{(mark.IsCount ? "count" : "untried")}@{mark.X},{mark.Y}"))}").Should().Equal(
        "assess>escalate:count@182,362",
        "send>escalate:count@334,484",
        "send>escalate_final:count@426,484",
        "reply>cycle:count@380,630",
        "escalate>closed:count@577,630",
        "escalate_final>closed:count@867,630");
    }

    private readonly record struct Box(int Left, int Top, int Right, int Bottom)
    {
        internal bool Touches(Box other) => Left <= other.Right && Right >= other.Left && Top <= other.Bottom && Bottom >= other.Top;
    }

    private static Box Around(int x, int y, int radius) => new(x - radius, y - radius, x + radius, y + radius);

    private static Box Of((Spot A, Spot B) segment) => new(Math.Min(segment.A.X, segment.B.X), Math.Min(segment.A.Y, segment.B.Y), Math.Max(segment.A.X, segment.B.X), Math.Max(segment.A.Y, segment.B.Y));

    /// <summary>Everything a label or a numbered mark must not touch, except the segments of the line it belongs to.</summary>
    private static List<(string What, Box Box)> Obstacles(DiagramLayout layout, DrawnLine? except)
    {
        var all = new List<(string, Box)>();
        all.AddRange(layout.Nodes.Select(box => ("step " + box.Id, new Box(box.X, box.Y, box.X + box.Width, box.Y + box.Height))));
        all.AddRange(layout.Bands.Select(band => ("band label " + band.Index, new Box(band.LabelX, band.LabelY - DiagramLayout.TextHalf, band.LabelX + band.LabelWidth, band.LabelY + DiagramLayout.TextHalf))));
        foreach (var line in layout.Lines)
        {
            var name = line.From + ">" + line.To;
            if (!ReferenceEquals(line, except))
            {
                all.AddRange(line.Segments.Select(segment => ("line " + name, Of(segment))));
            }

            all.AddRange(line.Marks.Select(mark => ("mark of " + name, Around(mark.X, mark.Y, DiagramLayout.MarkRadius))));
            if (!ReferenceEquals(line, except))
            {
                if (line.LabelBox is { } text)
                {
                    all.Add(("label of " + name, new Box(text.Left, text.Top, text.Right, text.Bottom)));
                }

                if (line.NumberAt is { } number)
                {
                    all.Add(("number of " + name, Around(number.X, number.Y, DiagramLayout.MarkRadius)));
                }
            }
        }

        return all;
    }

    [Theory]
    [MemberData(nameof(EveryDocumentInBothLanguages))]
    public void NoLineStepMarkOrOtherLabelTouchesTheTextOfALabelOrANumberedMark(string name, WorkbenchLocale locale)
    {
        var (layout, _) = Of(name, locale);
        foreach (var line in layout.Lines)
        {
            var who = name + " " + line.From + ">" + line.To;
            if (line.LabelBox is { } text)
            {
                var box = new Box(text.Left, text.Top, text.Right, text.Bottom);
                box.Left.Should().BeGreaterThanOrEqualTo(DiagramLayout.Padding, who);
                box.Right.Should().BeLessThanOrEqualTo(layout.Width, who);
                box.Top.Should().BeGreaterThanOrEqualTo(0, who);
                box.Bottom.Should().BeLessThanOrEqualTo(layout.Height, who);
                Obstacles(layout, line).Where(item => item.Box.Touches(box)).Select(item => item.What).Should().BeEmpty(who + " label " + line.ShortLabel);
                line.Segments.Where(segment => Of(segment).Touches(box) && !(line.Place == LabelPlace.OnSegment && Of(segment).Top > box.Bottom)).Should().BeEmpty(who + " own line");
            }

            if (line.NumberAt is { } number)
            {
                var box = Around(number.X, number.Y, DiagramLayout.MarkRadius);
                box.Right.Should().BeLessThanOrEqualTo(layout.Width, who);
                Obstacles(layout, line).Where(item => item.Box.Touches(box)).Select(item => item.What).Should().BeEmpty(who + " numbered");
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryDocumentInBothLanguages))]
    public void EveryLabelledRouteHasAPlaceAndALabelIsNeverCutBelowEightCharacters(string name, WorkbenchLocale locale)
    {
        var (layout, _) = Of(name, locale);
        foreach (var line in layout.Lines)
        {
            var who = name + " " + line.From + ">" + line.To;
            if (line.FullLabel is null)
            {
                line.Place.Should().Be(LabelPlace.None, who);
                line.ShortLabel.Should().BeNull(who);
                continue;
            }

            line.Place.Should().NotBe(LabelPlace.None, who);
            if (line.Place == LabelPlace.Numbered)
            {
                line.ShortLabel.Should().BeNull(who);
                line.LabelAt.Should().BeNull(who);
                line.NumberAt.Should().NotBeNull(who);
                line.Number.Should().BeGreaterThan(0, who);
                continue;
            }

            line.Number.Should().Be(0, who);
            line.NumberAt.Should().BeNull(who);
            line.ShortLabel.Should().NotBeNull(who);
            line.LabelAt.Should().NotBeNull(who);
            line.ShortLabel!.Length.Should().BeGreaterThanOrEqualTo(Math.Min(DiagramLayout.MinLabelChars, line.FullLabel.Length), who);
            if (line.ShortLabel != line.FullLabel)
            {
                line.ShortLabel.Should().EndWith("…", who);
                line.ShortLabel.Length.Should().BeGreaterThanOrEqualTo(DiagramLayout.MinLabelChars, who);
                line.FullLabel.Length.Should().BeGreaterThan(line.ShortLabel.Length - 1, who);
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryDocumentInBothLanguages))]
    public void TheNumberedMarksAreNumberedFromOneInOrderAndTheListUnderTheDiagramHasTheirText(string name, WorkbenchLocale locale)
    {
        var (layout, _) = Of(name, locale);
        var numbered = layout.NumberedLines.ToList();
        numbered.Select(line => line.Number).Should().Equal(Enumerable.Range(1, numbered.Count));
        numbered.Where(line => string.IsNullOrWhiteSpace(line.FullLabel)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(EveryDocumentInBothLanguages))]
    public void TheDiagramIsWideEnoughForEveryLabelAndMark(string name, WorkbenchLocale locale)
    {
        var (layout, _) = Of(name, locale);
        var right = layout.Lines.Select(line => line.LabelBox?.Right ?? 0)
            .Concat(layout.Lines.Select(line => line.NumberAt is { } at ? at.X + DiagramLayout.MarkRadius : 0))
            .Concat(layout.Nodes.Select(box => box.X + box.Width))
            .Concat(layout.Lines.SelectMany(line => line.Points).Select(spot => spot.X)).Max();
        layout.Width.Should().Be(right + DiagramLayout.Padding, name);
    }

    [Fact]
    public void TheWidestReferenceFlowFitsInElevenHundredUnitsAndTheViewBoxIsSizedToTheContent()
    {
        foreach (var name in FlowDocs.ReferenceIds)
        {
            var (layout, _) = Of(name, WorkbenchLocale.En);
            layout.Width.Should().BeLessThanOrEqualTo(1100, name);
            var text = layout.Lines.Select(line => line.LabelBox?.Right ?? 0).Max();
            var marks = layout.Lines.Select(line => line.NumberAt is { } at ? at.X + DiagramLayout.MarkRadius : 0).Max();
            var right = new[] { layout.Nodes.Max(box => box.X + box.Width), layout.Lines.SelectMany(line => line.Points).Max(spot => spot.X), text, marks }.Max();
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
        first.Nodes.Should().OnlyContain(box => box.Lines.Count >= 1 && box.Lines.Count <= 3 && box.Lines.All(text => text.Length <= DiagramLayout.LineChars));
        first.Nodes.Should().OnlyContain(box => box.Height >= DiagramLayout.HeightFor(box.Lines.Count));
    }

    [Theory]
    [MemberData(nameof(EveryDocumentInBothLanguages))]
    public void TheTextOfAStepNeverTouchesItsSymbolOrTheFoldedCornerOfAPersonStep(string name, WorkbenchLocale locale)
    {
        const int symbolRight = 9 + 15;
        const int foldWidth = 14;
        var (layout, graph) = Of(name, locale);
        var families = graph.Nodes.ToDictionary(node => node.Id, node => node.Family, StringComparer.Ordinal);
        foreach (var box in layout.Nodes)
        {
            var family = families[box.Id];
            var textShift = DiagramLayout.TextShift(family);
            var width = box.Lines.Max(line => line.Length) * DiagramLayout.CharWidth;
            var middle = box.X + (box.Width / 2) + textShift;
            var left = middle - (width / 2);
            var right = middle + (width / 2);
            if (family != FlowFamily.End)
            {
                left.Should().BeGreaterThanOrEqualTo(box.X + symbolRight + 4, name + "/" + box.Id + " text touches the symbol");
            }

            right.Should().BeLessThanOrEqualTo(box.X + box.Width - (family == FlowFamily.Person ? foldWidth + 2 : 4), name + "/" + box.Id + " text touches the edge or the folded corner");
        }
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
        var three = DiagramLayout.Wrap("A very long label that cannot possibly fit in two lines of the box");
        three.Lines.Should().HaveCount(3).And.OnlyContain(text => text.Length <= DiagramLayout.LineChars);
        var cut = DiagramLayout.Wrap("A very long label that cannot possibly fit in two lines of the box and not in three either, it goes on and on");
        cut.IsCut.Should().BeTrue();
        cut.Lines.Should().HaveCount(3).And.OnlyContain(text => text.Length <= DiagramLayout.LineChars);
        cut.Lines[2].Should().EndWith("…").And.NotContain(" …");
        var word = DiagramLayout.Wrap("Supercalifragilisticexpialidocious");
        word.Lines.Should().ContainSingle().Which.Should().Be("Supercalifragilistice…");
        word.IsCut.Should().BeTrue();
    }
}

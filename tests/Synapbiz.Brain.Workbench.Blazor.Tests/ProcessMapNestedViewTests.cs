using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The map draws every process: a subprocess a subprocess starts and a subprocess a scheduled task starts are cards one level down, with the same names; the map column answers to its own width.</summary>
public sealed class ProcessMapNestedViewTests
{
    private static IReadOnlyList<WorkbenchFlow> Flows(Func<BrainProcess, bool>? keep = null) =>
    [.. MapDocs.All.Where(process => keep?.Invoke(process) ?? true).Select(process => new WorkbenchFlow { Key = process.Id, Group = "Synthetic", Process = process })];

    private static FlowHarness Open(WorkbenchLocale locale, IReadOnlyList<WorkbenchFlow> flows, FlowWorkbenchView view = FlowWorkbenchView.Map, Action<ComponentParameterCollectionBuilder<FlowWorkbench>>? more = null)
    {
        var harness = new FlowHarness();
        harness.Session.View = view;
        harness.Render(flows, locale, view: null, more: more);
        return harness;
    }

    private static string Text(AngleSharp.Dom.IElement element) => FlowHarness.Normalise(element.TextContent);

    private static IEnumerable<string> Titles(IEnumerable<MapChild> children) =>
        children.SelectMany(child => new[] { child.Process.Title }.Concat(Titles(child.Nested)));

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void A_grandchild_and_the_child_of_a_scheduled_task_are_cards_inside_the_card_that_starts_them(string language)
    {
        var locale = language == "es" ? WorkbenchLocale.Es : WorkbenchLocale.En;
        using var harness = Open(locale, Flows());
        var model = ProcessMap.From(MapDocs.All, locale);

        var wrap = harness.FindAll("div.wb-card").Single(card => Text(card.QuerySelector(".wb-card-t")!) == model.Process("map.wrap")!.Title);
        var inner = wrap.QuerySelectorAll("ul.wb-nested div.wb-card").Should().ContainSingle().Subject;
        Text(inner.QuerySelector(".wb-card-t")!).Should().Be(model.Process("map.inner")!.Title);

        var nightly = harness.FindAll(".wb-schc").Single(card => Text(card.QuerySelector(".wb-card-t")!) == model.Process("map.nightly")!.Title);
        var child = nightly.QuerySelectorAll("ul.wb-nested div.wb-card").Should().ContainSingle().Subject;
        Text(child.QuerySelector(".wb-card-t")!).Should().Be(model.Process("map.nightly_child")!.Title);

        // The same card component and the same accessible name as any subprocess card.
        var name = inner.QuerySelector("button.wb-card-btn")!.GetAttribute("aria-label")!;
        name.Should().StartWith((locale == WorkbenchLocale.Es ? "Subproceso" : "Subprocess") + ": " + model.Process("map.inner")!.Title + ", ");
        child.QuerySelector("button.wb-card-btn")!.GetAttribute("aria-label").Should().StartWith(locale == WorkbenchLocale.Es ? "Subproceso: " : "Subprocess: ");
        inner.ParentElement!.QuerySelector(".wb-stem")!.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void Every_process_of_the_library_has_a_card_on_the_map_in_the_order_of_the_model(string language)
    {
        var locale = language == "es" ? WorkbenchLocale.Es : WorkbenchLocale.En;
        foreach (var flows in new[] { Flows(), Flows(process => process.Id != "map.front"), Flows(process => process.Id is not ("map.front" or "map.cron")) })
        {
            using var harness = Open(locale, flows);
            var model = ProcessMap.From(flows.Select(flow => flow.Process!), locale);
            var drawn = harness.FindAll(".wb-card-t, h4.wb-frame-t").Select(Text).ToList();
            foreach (var process in model.Processes)
            {
                drawn.Should().Contain(process.Title, process.Id);
            }

            var expected = model.Mains.SelectMany(main => main.Phases).SelectMany(phase => Titles(phase.Subprocesses)).ToList();
            drawn.Where(title => expected.Contains(title)).Take(expected.Count).Should().Equal(expected);
        }
    }

    [Fact]
    public void Without_the_front_process_the_notification_is_under_the_scheduled_sweep_that_starts_it()
    {
        using var harness = Open(WorkbenchLocale.En, Flows(process => process.Id != "map.front"));

        var sweep = harness.FindAll(".wb-schc").Single(card => Text(card.QuerySelector(".wb-card-t")!) == "Morning sweep");
        sweep.QuerySelectorAll("ul.wb-nested .wb-card-t").Select(Text).Should().Equal("Notify");
        var detached = harness.FindAll("section.wb-lane").Single(lane => Text(lane.QuerySelector("h3")!) == "Subprocesses nothing reaches");
        detached.QuerySelectorAll(".wb-card-t").Select(Text).Should().Equal("Loop A", "Loop B");
    }

    [Fact]
    public void In_the_live_view_the_nested_cards_show_their_own_counts_like_any_card()
    {
        var source = ScriptedLiveSource.Reference();
        source.Cases.Add(new FlowLiveCase("case-inner", "map.inner", 1, ProcessStatus.Running, "done", null, null, [], FlowLiveWait.Nothing));
        source.Cases.Add(new FlowLiveCase("case-inner-2", "map.inner", 1, ProcessStatus.Running, "done", null, null, [], FlowLiveWait.Nothing));
        source.Cases.Add(new FlowLiveCase("case-child", "map.nightly_child", 1, ProcessStatus.Running, "done", null, null, [], FlowLiveWait.Nothing));
        using var harness = Open(WorkbenchLocale.En, Flows(), FlowWorkbenchView.Live, parameters => parameters.Add(component => component.LiveSource, source));

        var inner = harness.FindAll("ul.wb-nested div.wb-card").Single(card => Text(card.QuerySelector(".wb-card-t")!) == "Inner step");
        Text(inner.QuerySelector(".wb-live-btn")!).Should().Be("2 cases");
        var child = harness.FindAll("ul.wb-nested div.wb-card").Single(card => Text(card.QuerySelector(".wb-card-t")!) == "Night archive");
        Text(child.QuerySelector(".wb-live-btn")!).Should().Be("1 case");
        child.QuerySelector(".wb-live-btn")!.GetAttribute("aria-label").Should().Be("Show the cases of Night archive: 1 case");
        harness.FindAll("ul.wb-nested div.wb-card .wb-live-btn, ul.wb-nested div.wb-card .wb-live-none").Should().NotBeEmpty();
    }

    [Fact]
    public void The_map_column_stacks_the_phases_by_its_own_width_and_nothing_can_overflow_the_frame()
    {
        var css = Fx.Read("src/Synapbiz.Brain.Workbench.Blazor/wwwroot/workbench.css");

        css.Should().Contain("container-type: inline-size").And.Contain("container-name: wbmap");
        css.Should().Contain("@container wbmap (max-width: 900px)").And.Contain("@container wbmap (max-width: 1240px)");
        var phases = Block(css, ".wb-phases {");
        phases.Should().Contain("grid-auto-columns: minmax(0, 1fr)").And.Contain("min-width: 0");
        css.Should().NotContain("minmax(200px, 1fr)");
        Block(css, ".wb-mapgrid {").Should().Contain("min-width: 0");
        Block(css, ".wb-mapview {").Should().Contain("min-width: 0");
        Block(css, ".wb-phase {").Should().Contain("min-width: 0");
        var container = css[css.IndexOf("@container wbmap (max-width: 900px)", StringComparison.Ordinal)..];
        container[..container.IndexOf("}\n}", StringComparison.Ordinal)].Should().Contain("grid-auto-flow: row").And.Contain("grid-row: auto");
    }

    private static string Block(string css, string opening)
    {
        var start = css.IndexOf(opening, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, opening);
        return css[start..css.IndexOf('}', start)];
    }

    [Fact]
    public void A_main_process_with_five_phases_or_more_gets_the_wider_threshold_and_one_with_four_does_not()
    {
        var five = MapDocs.With("map.front", document => document["body"]!["stages"] = System.Text.Json.Nodes.JsonNode.Parse("""["intake","service","three","four","five"]"""));
        var flows = (IReadOnlyList<WorkbenchFlow>)[.. five.Select(process => new WorkbenchFlow { Key = process.Id, Group = "Synthetic", Process = process })];
        using var many = Open(WorkbenchLocale.En, flows);
        many.FindAll("section.wb-frame").Single(frame => Text(frame.QuerySelector("h4")!) == "Front process").QuerySelectorAll("li.wb-phase").Length.Should().BeGreaterThanOrEqualTo(5);
        many.FindAll("ol.wb-phases.wb-many").Should().ContainSingle();

        using var plain = Open(WorkbenchLocale.En, FlowHarness.Reference());
        plain.FindAll("ol.wb-phases").Should().OnlyContain(list => !list.ClassList.Contains("wb-many"));
    }
}

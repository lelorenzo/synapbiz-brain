using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The live view of the process workbench: no source means no view, the counts over the map, the list of cases, one case over its diagram, refreshing and failing.</summary>
public sealed class FlowLiveViewTests
{
    private static IReadOnlyList<BrainProcess> Documents => [.. FlowDocs.ReferenceIds.Select(FlowDocs.Process)];

    private static FlowHarness Open(ScriptedLiveSource? source, WorkbenchLocale locale = WorkbenchLocale.En, FlowWorkbenchView view = FlowWorkbenchView.Live, Action<ComponentParameterCollectionBuilder<FlowWorkbench>>? more = null, List<string>? opened = null)
    {
        var harness = new FlowHarness();
        harness.Session.View = view;
        harness.Render(
            FlowHarness.Reference(),
            locale,
            view: null,
            more: parameters =>
            {
                if (source is not null)
                {
                    parameters.Add(component => component.LiveSource, source);
                }

                if (opened is not null)
                {
                    parameters.Add(component => component.OnOpenCase, caseRef => opened.Add(caseRef));
                }

                more?.Invoke(parameters);
            });
        return harness;
    }

    private static string Text(AngleSharp.Dom.IElement element) => FlowHarness.Normalise(element.TextContent);

    private static string Zero(WorkbenchLocale locale) => locale == WorkbenchLocale.Es ? "sin casos" : "no cases";

    private static string CountLine(LiveCount count, WorkbenchLocale locale)
    {
        if (count.IsZero)
        {
            return Zero(locale);
        }

        var es = locale == WorkbenchLocale.Es;
        var noun = es ? "casos" : "cases";
        if (count.Open == 1)
        {
            noun = es ? "caso" : "case";
        }

        var line = count.Open + " " + noun;
        if (count.Waiting == 0)
        {
            return line;
        }

        var waiting = es ? " esperando a una persona" : " waiting for a person";
        return line + ", " + count.Waiting + waiting;
    }

    private static ProcessMapLive Overlay(ScriptedLiveSource source) => ProcessMapLive.From(Documents, source.CurrentCounts());

    private static string MainWithCases(ScriptedLiveSource source) =>
        ProcessMap.From(Documents, WorkbenchLocale.En).Mains.Select(main => main.Process.Id).First(id => Overlay(source).Process(id).Open > 0);

    [Fact]
    public void Without_a_source_there_is_no_live_button_and_a_live_session_shows_the_map()
    {
        using var harness = Open(null, view: FlowWorkbenchView.Live);

        harness.FindAll(".wb-viewbar button").Select(Text).Should().Equal("Map", "List");
        harness.FindAll("section.wb-mapview").Should().ContainSingle();
        harness.FindAll(".wb-live-btn, .wb-live-bar").Should().BeEmpty();
        harness.Find(".wb-viewbar button[aria-pressed=true]").TextContent.Trim().Should().Be("Map");
    }

    [Fact]
    public void With_a_source_the_view_bar_offers_live_and_the_map_view_stays_as_it_was()
    {
        var source = ScriptedLiveSource.Reference();
        using var harness = Open(source, view: FlowWorkbenchView.Map);

        harness.FindAll(".wb-viewbar button").Select(Text).Should().Equal("Map", "List", "Live");
        harness.FindAll(".wb-live-btn, .wb-live-bar").Should().BeEmpty();
        source.CountCalls.Should().Be(0, "nothing is read until the reader asks for the live view");

        harness.FindAll(".wb-viewbar button")[2].Click();

        harness.Session.View.Should().Be(FlowWorkbenchView.Live);
        source.CountCalls.Should().Be(1);
        harness.Find(".wb-viewbar button[aria-pressed=true]").TextContent.Trim().Should().Be("Live");
        harness.FindAll(".wb-live-btn").Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void The_counts_on_every_process_and_phase_are_the_headless_overlay_in_both_languages(string language)
    {
        var locale = language == "es" ? WorkbenchLocale.Es : WorkbenchLocale.En;
        var source = ScriptedLiveSource.Reference();
        using var harness = Open(source, locale);
        var overlay = Overlay(source);
        var model = ProcessMap.From(Documents, locale);

        foreach (var main in model.Mains)
        {
            var frame = harness.FindAll("section.wb-frame").Single(item => Text(item.QuerySelector("h4")!) == main.Process.Title);
            var count = overlay.Process(main.Process.Id);
            var processButton = frame.QuerySelector(".wb-frame-h .wb-live-btn, .wb-frame-h .wb-live-none")!;
            Text(processButton).Should().Be(CountLine(count, locale));
            var phases = frame.QuerySelectorAll("li.wb-phase");
            for (var index = 0; index < main.Phases.Count; index++)
            {
                var phase = phases[index].QuerySelector(".wb-live-btn, .wb-live-none")!;
                var phaseCount = overlay.Phase(main.Process.Id, main.Phases[index].StageId);
                Text(phase).Should().Be(CountLine(phaseCount, locale));
            }
        }

        foreach (var child in model.Mains.SelectMany(main => main.Phases).SelectMany(phase => phase.Subprocesses))
        {
            var card = harness.FindAll("li.wb-card-li .wb-card").First(item => Text(item.QuerySelector(".wb-card-t")!) == child.Process.Title);
            var count = overlay.Process(child.Process.Id);
            Text(card.QuerySelector(".wb-live-btn, .wb-live-none")!).Should().Be(CountLine(count, locale));
        }

        Text(harness.Find(".wb-live-status")).Should().Contain(overlay.Total.Open.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Text(harness.Find(".wb-viewbar button[aria-pressed=true]")).Should().Be(locale == WorkbenchLocale.Es ? "En vivo" : "Live");
        Text(harness.Find(".wb-live-bar button")).Should().Be(locale == WorkbenchLocale.Es ? "Actualizar" : "Refresh");
        harness.Cut.Markup.Should().NotContainEquivalentOf("flujo", "the wording is process in both languages");
    }

    [Fact]
    public void A_count_button_has_a_full_name_that_says_what_it_opens()
    {
        var source = ScriptedLiveSource.Reference();
        using var harness = Open(source);
        var id = MainWithCases(source);
        var title = ProcessMap.From(Documents, WorkbenchLocale.En).Mains.Single(main => main.Process.Id == id).Process.Title;

        var frame = harness.FindAll("section.wb-frame").Single(item => Text(item.QuerySelector("h4")!) == title);
        var name = frame.QuerySelector(".wb-frame-h .wb-live-btn")!.GetAttribute("aria-label");

        name.Should().StartWith("Show the cases of " + title + ": ");
        name.Should().Contain("case");
    }

    [Fact]
    public void Choosing_a_count_lists_the_opaque_references_of_those_cases_and_a_case_raises_the_host_callback()
    {
        var source = ScriptedLiveSource.Reference();
        var opened = new List<string>();
        using var harness = Open(source, opened: opened);
        var id = MainWithCases(source);
        var expected = source.Cases.Where(item => item.ProcessId == id).Select(item => item.CaseRef).ToList();
        var title = ProcessMap.From(Documents, WorkbenchLocale.En).Mains.Single(main => main.Process.Id == id).Process.Title;

        harness.FindAll("section.wb-frame").Single(item => Text(item.QuerySelector("h4")!) == title).QuerySelector(".wb-frame-h .wb-live-btn")!.Click();

        harness.Find("section.wb-side h2").TextContent.Trim().Should().Be("Open cases of " + title);
        var lines = harness.FindAll("ul.wb-caselist li button");
        lines.Select(line => Text(line.QuerySelector(".wb-caseref")!)).Should().Equal(expected.Select(item => "Case " + item));
        lines[0].GetAttribute("aria-label").Should().StartWith("Open the case " + expected[0] + ": ");
        harness.Cut.Markup.Should().NotContain("business", "a case line carries no business key");

        lines[0].Click();

        opened.Should().Equal(expected[0]);
        harness.Session.LiveCaseRef.Should().Be(expected[0]);
        harness.Find("section[aria-labelledby$=-case] h2").TextContent.Trim().Should().Be("Case " + expected[0]);
    }

    [Fact]
    public void A_phase_count_lists_only_the_cases_on_the_steps_of_that_phase()
    {
        var source = ScriptedLiveSource.Reference();
        using var harness = Open(source);
        var overlay = Overlay(source);
        var model = ProcessMap.From(Documents, WorkbenchLocale.En);
        var (main, phase) = model.Mains.SelectMany(item => item.Phases.Select(each => (item, each))).First(pair => overlay.Phase(pair.item.Process.Id, pair.each.StageId).Open > 0 && overlay.Phase(pair.item.Process.Id, pair.each.StageId).Open < overlay.Process(pair.item.Process.Id).Open);
        var outline = ProcessOutline.From(Documents.Single(process => process.Id == main.Process.Id));
        var steps = outline.Steps.Where(step => step.Stage == phase.StageId).Select(step => step.Id).ToHashSet();
        var expected = source.Cases.Where(item => item.ProcessId == main.Process.Id && item.CurrentStep is not null && steps.Contains(item.CurrentStep)).Select(item => item.CaseRef).ToList();

        var frame = harness.FindAll("section.wb-frame").Single(item => Text(item.QuerySelector("h4")!) == main.Process.Title);
        frame.QuerySelectorAll("li.wb-phase")[main.Phases.ToList().IndexOf(phase)].QuerySelector(".wb-live-btn")!.Click();

        harness.FindAll("ul.wb-caselist li button").Select(line => Text(line.QuerySelector(".wb-caseref")!)).Should().Equal(expected.Select(item => "Case " + item));
        harness.Find("section.wb-side h2").TextContent.Should().Contain("phase");
    }

    [Fact]
    public void The_list_pages_with_show_more_and_never_reads_more_than_it_shows_on_the_first_click()
    {
        var source = ScriptedLiveSource.Reference(copies: 12);
        source.PageSize = 10;
        using var harness = Open(source);
        var id = MainWithCases(source);
        var total = source.Cases.Count(item => item.ProcessId == id);
        total.Should().BeGreaterThan(25);
        var title = ProcessMap.From(Documents, WorkbenchLocale.En).Mains.Single(main => main.Process.Id == id).Process.Title;

        harness.FindAll("section.wb-frame").Single(item => Text(item.QuerySelector("h4")!) == title).QuerySelector(".wb-frame-h .wb-live-btn")!.Click();

        var first = harness.FindAll("ul.wb-caselist li").Count;
        first.Should().BeGreaterThanOrEqualTo(25).And.BeLessThan(total);
        harness.Find("section.wb-side button.wb-btn-sm:not(:first-of-type), section.wb-side .wb-body > button").TextContent.Trim().Should().Be("Show more cases");

        harness.Find("section.wb-side .wb-body > button").Click();

        harness.FindAll("ul.wb-caselist li").Count.Should().BeGreaterThan(first);
        harness.FindAll("ul.wb-caselist li").Select(item => item.QuerySelector(".wb-caseref")!.TextContent).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void The_path_of_a_case_over_its_diagram_is_the_headless_path_in_both_languages(string language)
    {
        var locale = language == "es" ? WorkbenchLocale.Es : WorkbenchLocale.En;
        var source = ScriptedLiveSource.Reference();
        using var harness = Open(source, locale);
        var live = source.Cases.First(item => LiveCasePath.From(Documents.Single(process => process.Id == item.ProcessId), item, locale, Documents).Routes.Count > 0);
        var process = Documents.Single(item => item.Id == live.ProcessId);
        var path = LiveCasePath.From(process, live, locale, Documents);
        harness.Session.LiveScopeProcessId = live.ProcessId;
        harness.Session.LiveCaseRef = live.CaseRef;
        harness.Cut.Render();
        harness.Cut.WaitForAssertion(() => harness.FindAll(".wb-node-seen").Should().NotBeEmpty());

        // The diagram marks exactly the steps the headless model says, with their order, and the one the case is on.
        harness.FindAll("g.wb-node-seen").Count.Should().Be(path.Steps.Count);
        harness.FindAll("svg.wb-diagram-svg .wb-seen-badge-t").Select(item => item.TextContent).Should().BeEquivalentTo(Enumerable.Range(1, path.Steps.Count).Select(number => number.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        harness.FindAll("g.wb-node-now").Count.Should().Be(path.CurrentStep is null ? 0 : 1);
        harness.FindAll("svg.wb-diagram-svg .wb-node-here").Count.Should().Be(path.CurrentStep is null ? 0 : 1);
        harness.FindAll("path.wb-edge-taken").Count.Should().Be(path.TakenPairs.Count);
        harness.FindAll("svg.wb-diagram-svg [role=button]").Should().BeEmpty("nothing on a live diagram acts on the case");

        // The same path as an ordered list in text.
        harness.FindAll("ol.wb-casepath-list li strong").Select(item => item.TextContent).Should().Equal(path.Steps.Select(step => step.Label));
        harness.FindAll("ol.wb-casepath-list li").Count.Should().Be(path.Steps.Count);

        // The side column.
        var side = Text(harness.Find("aside.wb-case-side"));
        side.Should().Contain(path.Side!.CurrentStep!.Replace("`", string.Empty, StringComparison.Ordinal));
        side.Should().Contain(live.ProcessVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        harness.FindAll("aside.wb-case-side .wb-next li").Count.Should().Be(path.Side.Next.Count);

        // Visited nodes say so in words for a screen reader, with their order.
        harness.FindAll("g.wb-node-seen").Select(item => item.GetAttribute("aria-label")).Should().OnlyContain(label => label!.Contains(locale == WorkbenchLocale.Es ? "visitado, número" : "visited, number", StringComparison.Ordinal));
    }

    [Fact]
    public void A_case_of_another_version_than_any_loaded_says_in_words_that_the_path_cannot_be_shown()
    {
        var source = ScriptedLiveSource.Reference();
        var live = source.Cases[0];
        source.Cases[0] = live with { ProcessVersion = 99 };
        using var harness = Open(source);
        harness.Session.LiveCaseRef = live.CaseRef;
        harness.Cut.Render();

        Text(harness.Find("p.wb-note")).Should().StartWith("The path cannot be shown over the diagram.");
        harness.FindAll("g.wb-node-seen, aside.wb-case-side, .wb-casepath-list").Should().BeEmpty();
    }

    [Fact]
    public void A_case_the_source_no_longer_has_and_a_process_that_is_not_loaded_each_say_so()
    {
        var source = ScriptedLiveSource.Reference();
        using var harness = Open(source);
        harness.Session.LiveCaseRef = "case-missing";
        harness.Cut.Render();
        Text(harness.Find(".wb-casepanel, section[aria-labelledby$=-case] .wb-body")).Should().Contain("no longer has this case");

        source.Cases.Add(source.Cases[0] with { CaseRef = "case-stranger", ProcessId = "not.loaded" });
        harness.Session.LiveCaseRef = "case-stranger";
        harness.Cut.Render();
        Text(harness.Find("section[aria-labelledby$=-case] .wb-body")).Should().Contain("not.loaded").And.Contain("not loaded in the workbench");
    }

    [Fact]
    public async Task Refresh_and_a_new_token_read_the_source_again_and_nothing_reads_it_by_itself()
    {
        var source = ScriptedLiveSource.Reference();
        using var harness = Open(source, more: parameters => parameters.Add(component => component.LiveRefreshToken, 1));
        var id = MainWithCases(source);
        var before = Overlay(source).Process(id).Open;
        source.CountCalls.Should().Be(1);

        harness.Cut.Render();
        source.CountCalls.Should().Be(1, "a render alone reads nothing");

        source.Cases.Add(source.Cases.First(item => item.ProcessId == id) with { CaseRef = "case-new" });
        await harness.Cut.InvokeAsync(() => harness.Cut.Instance.Refresh());

        source.CountCalls.Should().Be(2);
        Overlay(source).Process(id).Open.Should().Be(before + 1);
        harness.FindAll("section.wb-frame .wb-frame-h .wb-live-btn").Select(Text).Should().Contain(text => text.StartsWith((before + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) || text.Contains(" " + (before + 1), StringComparison.Ordinal));

        harness.Cut.SetParametersAndRender(parameters => parameters.Add(component => component.LiveRefreshToken, 2));
        source.CountCalls.Should().Be(3, "a new token makes the drawn live view read again");

        harness.Cut.SetParametersAndRender(parameters => parameters.Add(component => component.LiveRefreshToken, 2));
        source.CountCalls.Should().Be(3, "the same token reads nothing");
    }

    [Fact]
    public void Pressing_refresh_in_the_panel_reads_the_counts_again()
    {
        var source = ScriptedLiveSource.Reference();
        using var harness = Open(source);

        harness.Find(".wb-live-bar button").Click();

        source.CountCalls.Should().Be(2);
    }

    [Fact]
    public void While_the_counts_load_the_screen_says_so_and_then_draws_them()
    {
        var gate = new TaskCompletionSource();
        var source = ScriptedLiveSource.Reference();
        source.CountsGate = gate.Task;
        using var harness = Open(source);

        Text(harness.Find(".wb-live-status")).Should().Be("Loading the open cases…");
        harness.FindAll(".wb-live-btn").Should().BeEmpty();
        harness.Find(".wb-live-bar button").HasAttribute("disabled").Should().BeTrue();

        gate.SetResult();
        harness.Cut.WaitForAssertion(() => harness.FindAll(".wb-live-btn").Should().NotBeEmpty());
        harness.Find(".wb-live-status").TextContent.Should().Contain("open case");
    }

    [Fact]
    public void A_source_with_no_open_cases_says_there_are_none()
    {
        var source = new ScriptedLiveSource();
        using var harness = Open(source);

        Text(harness.Find(".wb-live-status")).Should().Be("No open cases.");
        harness.FindAll(".wb-live-btn").Should().BeEmpty();
        harness.FindAll(".wb-live-none").Should().NotBeEmpty("every card says it has no cases");
    }

    [Fact]
    public void A_source_that_fails_says_so_keeps_the_numbers_it_had_and_offers_to_try_again()
    {
        var source = ScriptedLiveSource.Reference();
        using var harness = Open(source);
        var buttons = harness.FindAll(".wb-live-btn").Select(Text).ToList();
        buttons.Should().NotBeEmpty();

        source.FailCounts = true;
        harness.Find(".wb-live-bar button").Click();

        Text(harness.Find("p.wb-live-fail[role=alert]")).Should().Be("The source of cases did not answer. What was drawn before is still shown.");
        harness.FindAll(".wb-live-btn").Select(Text).Should().Equal(buttons);

        source.FailCounts = false;
        harness.Find(".wb-live-bar button").Click();

        harness.FindAll("p.wb-live-fail").Should().BeEmpty();
    }

    [Fact]
    public void A_source_that_fails_before_any_numbers_arrive_draws_none_and_says_so()
    {
        var source = ScriptedLiveSource.Reference();
        source.FailCounts = true;
        using var harness = Open(source);

        Text(harness.Find("p.wb-live-fail[role=alert]")).Should().Be("The source of cases did not answer and there are no numbers to show.");
        harness.FindAll(".wb-live-btn, .wb-live-none").Should().BeEmpty();
        harness.FindAll("section.wb-frame").Should().NotBeEmpty("the map itself is still drawn");
    }

    [Fact]
    public void A_list_and_a_case_that_fail_each_say_so_and_the_list_can_be_tried_again()
    {
        var source = ScriptedLiveSource.Reference();
        using var harness = Open(source);
        var id = MainWithCases(source);
        var title = ProcessMap.From(Documents, WorkbenchLocale.En).Mains.Single(main => main.Process.Id == id).Process.Title;
        source.FailList = true;

        harness.FindAll("section.wb-frame").Single(item => Text(item.QuerySelector("h4")!) == title).QuerySelector(".wb-frame-h .wb-live-btn")!.Click();

        Text(harness.Find("section.wb-side p.wb-live-fail")).Should().StartWith("The list of cases could not be loaded.");
        source.FailList = false;
        harness.Find("section.wb-side p.wb-live-fail button").Click();
        harness.FindAll("ul.wb-caselist li").Should().NotBeEmpty();

        source.FailCase = true;
        harness.Find("ul.wb-caselist li button").Click();
        Text(harness.Find("p.wb-live-fail")).Should().StartWith("The case could not be loaded.");
    }

    [Fact]
    public async Task A_stale_answer_never_replaces_a_newer_one()
    {
        var gate = new TaskCompletionSource();
        var source = ScriptedLiveSource.Reference();
        using var harness = Open(source);
        source.CountsGate = gate.Task;
        var first = harness.Cut.InvokeAsync(() => harness.Cut.Instance.Refresh());
        source.CountsGate = null;
        source.Cases.Clear();
        await harness.Cut.InvokeAsync(() => harness.Cut.Instance.Refresh());

        gate.SetResult();
        await first;

        Text(harness.Find(".wb-live-status")).Should().Be("No open cases.");
    }

    [Fact]
    public void Nothing_in_the_live_view_acts_on_a_case()
    {
        var source = ScriptedLiveSource.Reference();
        using var harness = Open(source);
        harness.Session.LiveScopeProcessId = source.Cases[0].ProcessId;
        harness.Session.LiveCaseRef = source.Cases[0].CaseRef;
        harness.Cut.Render();

        var names = harness.FindAll("section button").Select(Text).Distinct().ToList();

        names.Should().OnlyContain(name => name == "Back to the cases" || name == "Back to the live map" || name == "Try again" || name == "Refresh");
        harness.FindAll("input, select, textarea").Should().BeEmpty();
    }

    [Fact]
    public void Every_live_string_exists_in_both_languages_and_says_process_and_never_flow()
    {
        var keys = WorkbenchStrings.Keys.Where(key => key.StartsWith("flow.cases.", StringComparison.Ordinal) || key == "flow.view.live").ToList();

        keys.Should().HaveCountGreaterThan(50);
        foreach (var key in keys)
        {
            WorkbenchStrings.Default(key, WorkbenchLocale.En).Should().NotBeNullOrWhiteSpace(key);
            WorkbenchStrings.Default(key, WorkbenchLocale.Es).Should().NotBeNullOrWhiteSpace(key);
            System.Text.RegularExpressions.Regex.IsMatch(WorkbenchStrings.Default(key, WorkbenchLocale.En), "\\bflows?\\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeFalse(key);
            WorkbenchStrings.Default(key, WorkbenchLocale.Es).Should().NotContainEquivalentOf("flujo", key);
        }
    }

    [Fact]
    public void The_live_rules_of_the_stylesheet_exist_and_use_tokens_only()
    {
        var css = Fx.Read("src/Synapbiz.Brain.Workbench.Blazor/wwwroot/workbench.css");
        var live = css[css.IndexOf("/* ----- live:", StringComparison.Ordinal)..];

        foreach (var selector in new[] { ".wb-live-btn", ".wb-live-fail", ".wb-caseline", ".wb-case-layout", ".wb-case-side", ".wb-node-seen .wb-node-shape", ".wb-seen-badge", ".wb-seen-badge-t", ".wb-edge-taken" })
        {
            live.Should().Contain(selector);
        }

        System.Text.RegularExpressions.Regex.IsMatch(live, "#[0-9a-fA-F]{3,8}\\b|rgb\\(|hsl\\(").Should().BeFalse("the stylesheet ships no colour of its own");
    }
}

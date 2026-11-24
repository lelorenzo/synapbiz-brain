using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The map view of the process workbench: its structure is the headless model's, selecting opens a process beside it, and it reads as a list structure.</summary>
public sealed class ProcessMapViewTests
{
    private static FlowHarness Open(WorkbenchLocale locale = WorkbenchLocale.En, Action<Bunit.ComponentParameterCollectionBuilder<FlowWorkbench>>? more = null, IReadOnlyList<WorkbenchFlow>? flows = null)
    {
        var harness = new FlowHarness();
        harness.Render(flows ?? FlowHarness.Reference(), locale, view: null, more: more);
        return harness;
    }

    private static ProcessMap ModelOf(WorkbenchLocale locale, IReadOnlyDictionary<string, string>? labels = null) =>
        ProcessMap.From(FlowDocs.ReferenceIds.Select(FlowDocs.Process), locale, labels);

    [Fact]
    public void The_component_opens_on_the_map_not_the_list()
    {
        using var harness = Open();

        harness.Session.View.Should().Be(FlowWorkbenchView.Map);
        harness.FindAll("section.wb-mapview").Should().ContainSingle();
        harness.FindAll("nav.wb-list").Should().BeEmpty();
        harness.FindAll(".wb-detail").Should().BeEmpty("no process is open until the reader chooses one");
        harness.Find(".wb-viewbar").GetAttribute("role").Should().Be("group");
        harness.FindAll(".wb-viewbar button").Select(button => button.GetAttribute("aria-pressed")).Should().Equal("true", "false");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void The_structure_of_the_map_is_the_structure_of_the_headless_model(string language)
    {
        var locale = language == "es" ? WorkbenchLocale.Es : WorkbenchLocale.En;
        using var harness = Open(locale);
        var model = ModelOf(locale);

        harness.FindAll("section.wb-frame h4").Select(heading => heading.TextContent.Trim()).Should().Equal(model.Mains.Select(main => main.Process.Title));
        harness.FindAll("li.wb-phase .wb-phase-t").Count.Should().Be(model.Mains.Sum(main => main.Phases.Count));
        var loose = locale == WorkbenchLocale.Es ? "Sin fase" : "No phase";
        harness.FindAll("li.wb-phase .wb-phase-t").Select(phase => phase.TextContent.Trim()).Should()
            .Equal(model.Mains.SelectMany(main => main.Phases.Select((phase, index) => phase.IsLoose ? loose : (index + 1) + " · " + phase.Label)));
        harness.FindAll("li.wb-card-li .wb-card-t").Select(card => card.TextContent.Trim()).Should()
            .Equal(model.Mains.SelectMany(main => main.Phases).SelectMany(phase => phase.Subprocesses).Select(child => child.Process.Title));
        harness.FindAll(".wb-end").Select(end => end.TextContent.Trim()).Should().Equal(model.Mains.SelectMany(main => main.Ends));
        harness.FindAll(".wb-schc .wb-card-t").Select(task => task.TextContent.Trim()).Should().Equal(model.Scheduled.Select(task => task.Process.Title));
        harness.FindAll(".wb-phase-e").Select(item => item.TextContent.Trim()).Should()
            .Equal(model.Mains.SelectMany(main => main.Phases).Select(phase => phase.EndsWhen).Where(text => text is not null).Select(text => text!.Replace("`", string.Empty, StringComparison.Ordinal)));
        harness.FindAll(".wb-shared-i").Count.Should().Be(model.Shared.Count);
    }

    [Fact]
    public void The_map_lists_what_each_process_uses_with_the_documents_labels_and_in_the_models_order()
    {
        using var harness = Open();
        var model = ModelOf(WorkbenchLocale.En);
        var previsit = model.Process("previsit.outreach")!;

        var card = harness.FindAll("li.wb-card-li").Single(item => item.TextContent.Contains(previsit.Title, StringComparison.Ordinal));
        card.QuerySelectorAll(".wb-chips li").Select(chip => chip.TextContent.Replace(" :", ":", StringComparison.Ordinal).Trim()).Should().HaveCount(previsit.Uses.Count);
        card.QuerySelectorAll(".wb-chips li .wb-sr").Select(word => word.TextContent.Trim()).Should()
            .Equal(previsit.Uses.Select(use => use.Kind switch { MapPieceKind.Rule => "Rule:", MapPieceKind.Connector => "Connector to another system:", MapPieceKind.PersonTask => "Task for a person:", _ => "AI step:" }));
    }

    [Fact]
    public void Every_card_is_a_button_with_its_full_name()
    {
        using var harness = Open();

        var names = harness.FindAll("li.wb-card-li button.wb-card-btn").Select(button => button.GetAttribute("aria-label")).ToList();
        names.Should().Contain("Subprocess: Pre-visit outreach, 9 steps, 16 tests pass");
        names.Should().OnlyContain(name => name!.StartsWith("Subprocess: ", StringComparison.Ordinal));
        harness.FindAll("section.wb-frame h4 button").Single().GetAttribute("aria-label").Should().StartWith("Main process: ");
        harness.FindAll(".wb-schc button.wb-card-btn").Single().GetAttribute("aria-label").Should().StartWith("Scheduled task: ");
    }

    [Fact]
    public void The_map_is_a_list_structure_with_headings_and_chips_as_list_items()
    {
        using var harness = Open();

        harness.FindAll("section.wb-mapview > .wb-ph h2").Should().ContainSingle();
        harness.FindAll("section.wb-mapview h3").Count.Should().BeGreaterThan(2);
        harness.FindAll("section.wb-frame ol.wb-phases > li.wb-phase").Should().NotBeEmpty();
        harness.FindAll("li.wb-phase ul.wb-cards > li").Should().NotBeEmpty();
        harness.FindAll(".wb-chips").Should().OnlyContain(list => list.TagName == "UL");
        harness.FindAll(".wb-chips > *").Should().OnlyContain(item => item.TagName == "LI");
        harness.FindAll("section[aria-labelledby]").Select(section => section.GetAttribute("aria-labelledby")!)
            .Should().OnlyContain(id => harness.FindAll("[id='" + id + "']").Count == 1);
    }

    [Fact]
    public void The_legend_is_a_description_list_with_an_icon_and_a_word_for_each_kind_of_piece()
    {
        using var harness = Open();

        var terms = harness.FindAll("dl.wb-mapkey-l dt").Select(term => term.TextContent.Trim()).ToList();
        terms.Should().Equal("Main process", "Subprocess", "Scheduled task", "Event", "Rule", "Connector to another system", "Task for a person", "AI step");
        harness.FindAll("dl.wb-mapkey-l dt svg").Should().HaveCount(8).And.OnlyContain(icon => icon.GetAttribute("aria-hidden") == "true");
        harness.FindAll("dl.wb-mapkey-l dd").Should().HaveCount(8).And.OnlyContain(description => description.TextContent.Trim().Length > 10);
    }

    [Fact]
    public void Every_icon_points_at_a_symbol_the_component_draws()
    {
        using var harness = Open();

        var symbols = harness.FindAll("svg.wb-sprite symbol").Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        symbols.Count.Should().BeGreaterThanOrEqualTo(10);
        var targets = harness.FindAll("svg.wb-i use").Select(use => use.GetAttribute("href")!.TrimStart('#')).Distinct().ToList();
        targets.Should().NotBeEmpty().And.OnlyContain(target => symbols.Contains(target));
        harness.FindAll("svg.wb-i").Should().OnlyContain(icon => icon.GetAttribute("aria-hidden") == "true");
        harness.FindAll("[style]").Should().BeEmpty();
        harness.FindAll("svg.wb-sprite").Should().ContainSingle().Which.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void Choosing_a_card_opens_the_process_beside_the_map_with_a_breadcrumb()
    {
        using var harness = Open();

        harness.FindAll("li.wb-card-li button.wb-card-btn").Single(button => button.GetAttribute("aria-label")!.Contains("Pre-visit outreach", StringComparison.Ordinal)).Click();

        harness.Session.ProcessOpen.Should().BeTrue();
        harness.Session.SelectedFlowKey.Should().Be("previsit.outreach");
        harness.FindAll("section.wb-mapview").Should().ContainSingle("the map stays beside the process");
        harness.Find(".wb-mapgrid").ClassList.Should().Contain("wb-open");
        harness.FindAll(".wb-detail h1").Single().TextContent.Should().Be("Pre-visit outreach");
        harness.FindAll("[role=tab]").Should().HaveCount(5);
        var crumb = harness.Find("nav.wb-crumb");
        crumb.GetAttribute("aria-label").Should().Be("You are here");
        crumb.QuerySelectorAll("ol > li").Select(item => item.TextContent.Trim()).Should().Equal("Map", "Case lifecycle", "Pre-visit outreach");
        crumb.QuerySelector("li[aria-current=page]")!.TextContent.Trim().Should().Be("Pre-visit outreach");
        harness.FindAll("li.wb-card-li button[aria-current=true]").Should().ContainSingle();
    }

    [Fact]
    public void The_breadcrumb_goes_back_to_the_parent_and_to_the_map()
    {
        using var harness = Open();
        harness.FindAll("li.wb-card-li button.wb-card-btn")[0].Click();

        harness.FindAll("nav.wb-crumb .wb-crumb-btn")[1].Click();
        harness.Session.SelectedFlowKey.Should().Be("case.lifecycle");
        harness.FindAll("nav.wb-crumb ol > li").Select(item => item.TextContent.Trim()).Should().Equal("Map", "Case lifecycle");

        harness.FindAll("nav.wb-crumb .wb-crumb-btn")[0].Click();
        harness.Session.ProcessOpen.Should().BeFalse();
        harness.FindAll(".wb-detail").Should().BeEmpty();
    }

    [Fact]
    public void A_main_process_opens_from_its_title_and_its_diagram_from_see_the_diagram()
    {
        using var harness = Open();

        harness.Find("section.wb-frame h4 button").Click();
        harness.Session.SelectedFlowKey.Should().Be("case.lifecycle");
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Read);

        harness.Find("section.wb-frame .wb-link").Click();
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Diagram);
        harness.FindAll("svg.wb-diagram-svg").Should().ContainSingle();
    }

    [Fact]
    public void The_full_diagram_gets_the_whole_width_and_the_map_comes_back_on_request()
    {
        using var harness = Open();
        harness.FindAll("li.wb-card-li button.wb-card-btn")[0].Click();

        harness.FindAll("nav.wb-crumb .wb-btn").Single(button => button.TextContent.Contains("Open the full diagram", StringComparison.Ordinal)).Click();
        harness.Session.FullDiagram.Should().BeTrue();
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Diagram);
        harness.FindAll("section.wb-mapview").Should().BeEmpty();
        harness.Find(".wb-mapgrid").ClassList.Should().Contain("wb-full");
        harness.FindAll("svg.wb-diagram-svg").Should().ContainSingle();

        harness.FindAll("nav.wb-crumb .wb-btn").Single(button => button.TextContent.Contains("Show the map again", StringComparison.Ordinal)).Click();
        harness.Session.FullDiagram.Should().BeFalse();
        harness.FindAll("section.wb-mapview").Should().ContainSingle();
        harness.FindAll(".wb-detail").Should().ContainSingle();
    }

    [Fact]
    public void Closing_the_process_forgets_the_full_diagram_and_where_it_was_opened_from()
    {
        using var harness = Open();
        harness.FindAll("li.wb-card-li button.wb-card-btn")[0].Click();
        harness.Session.FullDiagram = true;

        harness.Session.ProcessOpen = false;

        harness.Session.FullDiagram.Should().BeFalse();
        harness.Session.Clear();
        harness.Session.View.Should().Be(FlowWorkbenchView.Map);
    }

    [Fact]
    public void Below_eleven_hundred_pixels_the_side_panel_replaces_the_map_and_a_back_control_returns()
    {
        using var harness = Open();
        harness.FindAll(".wb-back").Should().BeEmpty("nothing is open yet");
        harness.FindAll("li.wb-card-li button.wb-card-btn")[0].Click();

        harness.FindAll(".wb-side button.wb-back").Should().ContainSingle().Which.TextContent.Trim().Should().Be("Back to the map");
        var sheet = Fx.Read("src/Synapbiz.Brain.Workbench.Blazor/wwwroot/workbench.css");
        var narrow = sheet[sheet.IndexOf("@media (max-width: 1100px)", StringComparison.Ordinal)..sheet.IndexOf("@media (max-width: 760px)", StringComparison.Ordinal)];
        narrow.Should().Contain(".wb-mapgrid.wb-open .wb-mapview").And.Contain("display: none").And.Contain("grid-auto-flow: row").And.Contain(".wb-back");
        harness.FindAll(".wb-side button.wb-back")[0].Click();
        harness.Session.ProcessOpen.Should().BeFalse();
    }

    [Fact]
    public void A_rule_chip_is_a_button_only_when_the_host_handles_opening_a_rule()
    {
        using var without = Open();
        without.FindAll("button.wb-chip-btn").Should().BeEmpty();

        var opened = new List<string>();
        using var with = Open(more: parameters => parameters.Add(component => component.OnOpenRule, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<string>(this, id => opened.Add(id))));
        var rules = ModelOf(WorkbenchLocale.En).Processes.SelectMany(process => process.Uses).Where(use => use.Kind == MapPieceKind.Rule).ToList();
        var chips = with.FindAll("button.wb-chip-btn");
        chips.Should().NotBeEmpty();
        chips.Should().OnlyContain(chip => chip.GetAttribute("aria-label")!.StartsWith("Open the rule ", StringComparison.Ordinal));
        chips[0].Click();
        opened.Should().Equal(rules[0].Name);
        with.FindAll("li .wb-chip.wb-k-con button.wb-chip-btn").Should().BeEmpty("only rules can be opened");
    }

    [Fact]
    public void The_authority_of_a_rule_is_a_badge_on_its_chip_only_when_the_host_has_a_label_for_it()
    {
        var rule = ModelOf(WorkbenchLocale.En).Processes.SelectMany(process => process.Uses).First(use => use.Kind == MapPieceKind.Rule).Name;
        var authorities = new Dictionary<string, string> { [rule] = "recommendation" };

        using var labelled = Open(more: parameters => parameters.Add(component => component.RuleAuthorities, authorities).Add(component => component.AuthorityLabels, new Dictionary<string, string> { ["recommendation"] = "Recommendation" }));
        labelled.FindAll(".wb-chip .wb-badge").Should().NotBeEmpty().And.OnlyContain(badge => badge.TextContent == "Recommendation" && badge.GetAttribute("title") == "Authority: Recommendation");

        using var unlabelled = Open(more: parameters => parameters.Add(component => component.RuleAuthorities, authorities).Add(component => component.AuthorityLabels, new Dictionary<string, string> { ["other"] = "Other" }));
        unlabelled.FindAll(".wb-badge").Should().BeEmpty();

        using var none = Open(more: parameters => parameters.Add(component => component.AuthorityLabels, new Dictionary<string, string> { ["recommendation"] = "Recommendation" }));
        none.FindAll(".wb-badge").Should().BeEmpty();
    }

    [Fact]
    public void A_name_with_no_label_is_shown_as_code_and_a_host_label_is_used_when_the_document_has_none()
    {
        using var plain = Open();
        var model = ModelOf(WorkbenchLocale.En);
        var raw = model.Processes.SelectMany(process => process.Uses).FirstOrDefault(use => use.LabelIsName);
        if (raw is not null)
        {
            plain.FindAll(".wb-chip code.wb-code").Select(code => code.TextContent).Should().Contain(raw.Label);

            var labels = new Dictionary<string, string> { [raw.LabelKey] = "A label of the host" };
            using var labelled = Open(more: parameters => parameters.Add(component => component.MapLabels, labels));
            labelled.FindAll(".wb-chip").Select(chip => chip.TextContent).Should().Contain(text => text.Contains("A label of the host", StringComparison.Ordinal));
        }
        else
        {
            model.Processes.SelectMany(process => process.Uses).Should().OnlyContain(use => !use.LabelIsName, "the reference documents label everything they use");
        }
    }

    [Fact]
    public void Switching_to_the_list_groups_it_by_kind_and_back_again_keeps_the_selection()
    {
        using var harness = Open();
        harness.FindAll("li.wb-card-li button.wb-card-btn")[0].Click();

        harness.FindAll(".wb-viewbar button")[1].Click();
        harness.Session.View.Should().Be(FlowWorkbenchView.List);
        harness.FindAll("section.wb-mapview").Should().BeEmpty();
        harness.FindAll("h3.wb-kind").Select(heading => heading.TextContent).Should().Equal("Main processes", "Subprocesses", "Scheduled tasks");
        harness.FindAll(".wb-rule .wb-kindmark").Select(mark => mark.TextContent.Trim()).Should()
            .Equal("Main process", "Subprocess", "Subprocess", "Subprocess", "Subprocess", "Scheduled task");
        harness.FindAll("button.wb-rule[aria-current=true] .wb-rule-name").Single().TextContent.Should().Be("Pre-visit outreach");

        harness.FindAll(".wb-viewbar button")[0].Click();
        harness.FindAll("section.wb-mapview").Should().ContainSingle();
        harness.Session.SelectedFlowKey.Should().Be("previsit.outreach");
    }

    [Fact]
    public void The_words_of_the_map_and_the_kinds_are_spanish_in_spanish()
    {
        using var harness = Open(WorkbenchLocale.Es);

        harness.FindAll("dl.wb-mapkey-l dt").Select(term => term.TextContent.Trim()).Should()
            .Equal("Proceso principal", "Subproceso", "Tarea programada", "Evento", "Regla", "Conector a otro sistema", "Tarea de una persona", "Paso con IA");
        harness.Find("section.wb-mapview h2").TextContent.Should().Be("Mapa de procesos");
        harness.FindAll(".wb-viewbar button").Select(button => button.TextContent.Trim()).Should().Equal("Mapa", "Lista");
        harness.FindAll("li.wb-card-li button.wb-card-btn")[0].GetAttribute("aria-label").Should().StartWith("Subproceso: ");
    }

    [Fact]
    public void Processes_that_could_not_be_read_leave_the_list_to_say_so_and_the_map_is_not_drawn()
    {
        using var harness = new FlowHarness();
        harness.Render([new WorkbenchFlow { Key = "broken", Process = null, FallbackTitle = "Broken one" }], view: null);

        harness.FindAll("section.wb-mapview").Should().BeEmpty();
        harness.FindAll(".wb-viewbar").Should().BeEmpty();
        harness.DetailText.Should().Contain("could not be read");
        harness.FindAll("h3.wb-kind").Select(heading => heading.TextContent).Should().Equal("Could not be read");
    }

    [Fact]
    public void A_process_nothing_starts_says_so_in_words_in_the_map_and_in_the_list()
    {
        var flows = new[] { new WorkbenchFlow { Key = "map.nostart", Process = MapDocs.Process("map.nostart") } };
        using var harness = new FlowHarness();
        harness.Render(flows, view: null);

        harness.FindAll("section.wb-frame .wb-pill").Select(pill => pill.TextContent.Trim()).Should().Contain("No start defined: nothing starts this process on its own.");
        harness.FindAll(".wb-viewbar button")[1].Click();
        harness.Find(".wb-rule .wb-flow-l").TextContent.Should().Be("No start defined: nothing starts this process on its own.");
    }
}

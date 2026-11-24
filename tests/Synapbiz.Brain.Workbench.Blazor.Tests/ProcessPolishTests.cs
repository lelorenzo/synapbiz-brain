using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>Small refinements of the map and the list: the host's group is written once when it is the same for every process, the legend folds away while a process is open, and the cards of a phase line up.</summary>
public sealed class ProcessPolishTests
{
    private static List<WorkbenchFlow> WithGroups(Func<int, string?> group) =>
        [.. FlowHarness.Reference().Select((flow, index) => flow with { Group = group(index) })];

    [Fact]
    public void When_every_process_has_the_same_group_it_is_written_once_above_the_kinds()
    {
        using var harness = new FlowHarness();
        harness.Render(WithGroups(_ => "Reference processes"), view: FlowWorkbenchView.List);

        harness.FindAll("p.wb-group-once").Select(item => item.TextContent.Trim()).Should().Equal("Reference processes");
        harness.FindAll("h4.wb-group").Should().BeEmpty();
        harness.FindAll("h3.wb-kind").Should().NotBeEmpty();
    }

    [Fact]
    public void When_the_groups_differ_each_kind_keeps_its_group_headings()
    {
        using var harness = new FlowHarness();
        harness.Render(WithGroups(index => index % 2 == 0 ? "Group A" : "Group B"), view: FlowWorkbenchView.List);

        harness.FindAll("p.wb-group-once").Should().BeEmpty();
        harness.FindAll("h4.wb-group").Should().NotBeEmpty();
        harness.FindAll("h4.wb-group").Select(item => item.TextContent.Trim()).Distinct().Should().BeEquivalentTo("Group A", "Group B");
    }

    [Fact]
    public void A_process_without_a_group_among_others_that_share_one_keeps_the_headings()
    {
        using var harness = new FlowHarness();
        harness.Render(WithGroups(index => index == 0 ? null : "Group A"), view: FlowWorkbenchView.List);

        harness.FindAll("p.wb-group-once").Should().BeEmpty();
    }

    [Fact]
    public void The_legend_is_open_on_the_map_and_closed_while_a_process_is_open_and_stays_reachable()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference(), view: null);

        harness.Find("details.wb-mapkey").HasAttribute("open").Should().BeTrue();

        harness.Find("button.wb-card-btn").Click();

        harness.Find("details.wb-mapkey").HasAttribute("open").Should().BeFalse();
        harness.Find("details.wb-mapkey > summary").TextContent.Trim().Should().NotBeEmpty();
        harness.FindAll("details.wb-mapkey dl.wb-mapkey-l dt").Should().HaveCount(8);
    }

    [Fact]
    public void The_stylesheet_lines_up_the_cards_of_the_phases_and_gives_the_narrow_layout_its_own_rows()
    {
        var css = Fx.Read("src/Synapbiz.Brain.Workbench.Blazor/wwwroot/workbench.css");

        css.Should().Contain("grid-template-rows: subgrid");
        css.Should().Contain("@supports (grid-template-rows: subgrid)");
        var narrow = css[css.IndexOf("@media (max-width: 1100px)", StringComparison.Ordinal)..];
        narrow.Should().Contain("grid-row: auto");
    }
}

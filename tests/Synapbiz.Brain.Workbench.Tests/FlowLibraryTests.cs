using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class FlowLibraryTests
{
    private static IReadOnlyList<FlowEntry> Entries(WorkbenchLocale locale = WorkbenchLocale.En) => FlowLibrary.From(FlowDocs.All.Select(item => item.Process), locale);

    [Fact]
    public void Entries_are_ordered_by_id_then_version()
    {
        var entries = Entries();
        entries.Select(entry => entry.Id).Should().BeInAscendingOrder(StringComparer.Ordinal);
        entries.Should().HaveCount(FlowDocs.All.Count);
        FlowLibrary.From(FlowDocs.All.Select(item => item.Process).Reverse(), WorkbenchLocale.En).Select(entry => entry.ContentHash)
            .Should().Equal(entries.Select(entry => entry.ContentHash));
    }

    [Fact]
    public void An_entry_summarises_the_document()
    {
        var entry = Entries().Single(item => item.Id == "previsit.outreach" && item.Title == "Pre-visit outreach");
        entry.Version.Should().Be(1);
        entry.Title.Should().Be("Pre-visit outreach");
        entry.Subject.Should().Be("case");
        entry.StageLabels.Should().Equal("Before the visit");
        entry.StepCounts[OutlineStepKind.Read].Should().Be(1);
        entry.StepCounts.Values.Sum().Should().Be(ProcessOutline.From(FlowDocs.Process("previsit.outreach")).Steps.Count);
        entry.ScenarioCount.Should().BeGreaterThan(0);
        entry.ScenariosPass.Should().BeTrue();
        entry.Rules.Should().Contain("rule/previsit.missing_items");
        entry.ContentHash.Should().Be(ProcessOutline.From(FlowDocs.Process("previsit.outreach")).ContentHash);
    }

    [Fact]
    public void Coverage_in_the_entry_is_the_coverage_of_the_flow()
    {
        foreach (var entry in Entries())
        {
            var coverage = FlowCoverage.Of(FlowDocs.Process(FlowDocs.All.Single(item => ProcessOutline.From(item.Process).ContentHash == entry.ContentHash).Name));
            entry.TotalRoutes.Should().Be(coverage.TotalRoutes, entry.Id);
            entry.CoveredRoutes.Should().Be(coverage.CoveredRoutes, entry.Id);
        }
    }

    [Fact]
    public void Children_are_resolved_inside_the_set_and_a_missing_one_is_flagged()
    {
        var lifecycle = Entries().Single(item => item.Id == "case.lifecycle");
        lifecycle.Children.Should().OnlyContain(child => !child.IsMissing);
        lifecycle.Children.Select(child => child.ProcessId).Should().Contain("previsit.outreach");
        lifecycle.Children.Single(child => child.ProcessId == "previsit.outreach").Title.Should().NotBeNullOrEmpty();

        var alone = FlowLibrary.From([FlowDocs.Process("flow.batch")], WorkbenchLocale.En).Single();
        var missing = alone.Children.Should().ContainSingle().Subject;
        (missing.ProcessId, missing.IsMissing, missing.Title).Should().Be(("flow.child", true, null));

        var together = FlowLibrary.From([FlowDocs.Process("flow.batch"), FlowDocs.Process("flow.child")], WorkbenchLocale.En).Single(item => item.Id == "flow.batch");
        together.Children.Should().ContainSingle().Which.Should().Be(new FlowChildLink("flow.child", "Batch child", false));
    }

    [Fact]
    public void What_starts_a_flow_is_read_from_its_triggers()
    {
        var entries = Entries();
        entries.Single(item => item.Id == "practice.daily_refresh").Triggers.Should().ContainSingle().Which.Should().Contain("06:00");
        entries.Single(item => item.Id == "case.lifecycle").Triggers.Should().ContainSingle().Which.Should().Contain("case.opened");
        entries.Single(item => item.Id == "flow.batch").Triggers.Should().HaveCount(4);
        entries.Single(item => item.Id == "previsit.outreach" && item.Title == "Pre-visit outreach").Triggers.Should().BeEmpty();
        entries.Single(item => item.Id == "flow.child").StartedBy.Should().Equal("flow.batch");
        entries.Single(item => item.Id == "case.lifecycle").StartedBy.Should().BeEmpty();
    }

    [Fact]
    public void Titles_and_stage_labels_follow_the_language()
    {
        var hash = ProcessOutline.From(FlowDocs.Process("previsit.outreach")).ContentHash;
        var es = Entries(WorkbenchLocale.Es).Single(item => item.ContentHash == hash);
        es.Title.Should().NotBe("Pre-visit outreach");
        es.StageLabels.Should().NotEqual(["Before the visit"]);
    }

    [Fact]
    public void A_null_set_is_rejected_and_an_empty_one_is_empty()
    {
        FluentActions.Invoking(() => FlowLibrary.From(null!, WorkbenchLocale.En)).Should().Throw<ArgumentNullException>();
        FlowLibrary.From([], WorkbenchLocale.En).Should().BeEmpty();
    }
}

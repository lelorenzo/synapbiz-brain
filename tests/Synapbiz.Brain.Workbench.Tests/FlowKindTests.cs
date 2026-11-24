using System.Text.Json.Nodes;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The kind of every process, pinned on every reference document and fixture.</summary>
public sealed class FlowKindTests
{
    [Theory]
    [InlineData("case.lifecycle", FlowKind.Main)]
    [InlineData("previsit.outreach", FlowKind.Subprocess)]
    [InlineData("visit.note_wait", FlowKind.Subprocess)]
    [InlineData("postvisit.coding_review", FlowKind.Subprocess)]
    [InlineData("collection.followup", FlowKind.Subprocess)]
    [InlineData("practice.daily_refresh", FlowKind.Scheduled)]
    public void TheSixReferenceDocumentsHaveTheKindTheRuleGives(string id, FlowKind kind)
    {
        var entries = FlowLibrary.From(FlowDocs.Reference.Select(item => item.Process), WorkbenchLocale.En);
        entries.Single(entry => entry.Id == id).Kind.Should().Be(kind);
        entries.Single(entry => entry.Id == id).NoStartDefined.Should().BeFalse();
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void EveryDocumentHasTheKindOfTheGoldenFilesAloneAndInItsSet(WorkbenchLocale locale)
    {
        var language = locale == WorkbenchLocale.Es ? "es" : "en";
        var alone = MapDocs.RenderKinds(FlowDocs.All, locale);
        MapDocs.Golden("kinds.alone." + language + ".txt", alone).Should().Be(alone);
        var reference = string.Join(string.Empty, FlowLibrary.From(FlowDocs.Reference.Select(item => item.Process), locale)
            .Select(entry => entry.Id + ": " + entry.Kind + (entry.NoStartDefined ? " (no start defined)" : string.Empty) + " <- " + string.Join(",", entry.StartedBy) + "\n"));
        MapDocs.Golden("kinds.reference." + language + ".txt", reference).Should().Be(reference);
        var map = string.Join(string.Empty, FlowLibrary.From(MapDocs.All, locale)
            .Select(entry => entry.Id + ": " + entry.Kind + (entry.NoStartDefined ? " (no start defined)" : string.Empty) + " <- " + string.Join(",", entry.StartedBy) + "\n"));
        MapDocs.Golden("kinds.map." + language + ".txt", map).Should().Be(map);
    }

    [Fact]
    public void AScheduleTriggerWinsOverAnEventTrigger()
    {
        var clock = FlowDocs.Process("flow.clock");
        ProcessOutline.From(clock).Triggers.Should().Contain(trigger => trigger is EventTriggerDeclaration).And.Contain(trigger => trigger is ScheduleTriggerDeclaration);
        FlowLibrary.From([clock], WorkbenchLocale.En).Single().Kind.Should().Be(FlowKind.Scheduled);
    }

    [Fact]
    public void AProcessNobodyStartsWithNoTriggerIsMainWithTheMark()
    {
        var entries = FlowLibrary.From(MapDocs.All, WorkbenchLocale.En);
        var orphan = entries.Single(entry => entry.Id == "map.nostart");
        orphan.Kind.Should().Be(FlowKind.Main);
        orphan.NoStartDefined.Should().BeTrue();
        entries.Single(entry => entry.Id == "map.front").NoStartDefined.Should().BeFalse();

        var alone = FlowLibrary.From([MapDocs.Process("map.serve")], WorkbenchLocale.En).Single();
        alone.Kind.Should().Be(FlowKind.Main);
        alone.NoStartDefined.Should().BeTrue();
    }

    [Fact]
    public void AProcessWithItsOwnEventTriggerThatAnotherProcessStartsIsMainAndStillListsItsParent()
    {
        var documents = MapDocs.With("map.serve", document =>
            document["body"]!["triggers"] = JsonNode.Parse("""[{"kind":"event","event":"case.served","businessKey":"ref","input":{"ref":"ref"}}]"""));
        var serve = FlowLibrary.From(documents, WorkbenchLocale.En).Single(entry => entry.Id == "map.serve");
        serve.Kind.Should().Be(FlowKind.Main);
        serve.StartedBy.Should().Equal("map.front", "map.second");
        serve.NoStartDefined.Should().BeFalse();
    }

    [Fact]
    public void ACycleBetweenProcessesIsMadeOfSubprocessesAndASelfStartCountsAsNoParent()
    {
        var entries = FlowLibrary.From(MapDocs.All, WorkbenchLocale.En);
        entries.Single(entry => entry.Id == "map.loop_a").Kind.Should().Be(FlowKind.Subprocess);
        entries.Single(entry => entry.Id == "map.loop_b").Kind.Should().Be(FlowKind.Subprocess);

        var selfish = MapDocs.With("map.loop_a", ProcessMapTests.SelfStart);
        var alone = FlowLibrary.From([selfish.Single(process => process.Id == "map.loop_a")], WorkbenchLocale.En).Single();
        alone.StartedBy.Should().Equal("map.loop_a");
        alone.Kind.Should().Be(FlowKind.Main);
        alone.NoStartDefined.Should().BeTrue();
    }

    [Fact]
    public void TheWordsOfTheKindsAreTheOnesOfTheSpecInBothLanguages()
    {
        FlowKinds.Word(FlowKind.Main, WorkbenchLocale.En).Should().Be("Main process");
        FlowKinds.Word(FlowKind.Subprocess, WorkbenchLocale.En).Should().Be("Subprocess");
        FlowKinds.Word(FlowKind.Scheduled, WorkbenchLocale.En).Should().Be("Scheduled task");
        FlowKinds.Word(FlowKind.Main, WorkbenchLocale.Es).Should().Be("Proceso principal");
        FlowKinds.Word(FlowKind.Subprocess, WorkbenchLocale.Es).Should().Be("Subproceso");
        FlowKinds.Word(FlowKind.Scheduled, WorkbenchLocale.Es).Should().Be("Tarea programada");
        FlowKinds.Plural(FlowKind.Scheduled, WorkbenchLocale.En).Should().Be("Scheduled tasks");
        FluentActions.Invoking(() => FlowKinds.Word((FlowKind)9, WorkbenchLocale.En)).Should().Throw<ArgumentOutOfRangeException>();
    }
}

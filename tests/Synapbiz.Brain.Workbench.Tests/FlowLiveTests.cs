using System.Text;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>
/// The headless Live model: the path of a case over the document it runs on, replayed from the scripted histories of the six
/// reference documents (every embedded scenario, stopped after every command), a history that cannot be explained, the counts laid
/// over the map, and what a live case is allowed to carry.
/// </summary>
public sealed class FlowLiveTests
{
    private static IReadOnlyList<BrainProcess> Children => [.. FlowDocs.Reference.Select(item => item.Process)];

    /// <summary>Every (document, scenario, number of commands played) of the reference documents: the case after each command.</summary>
    public static TheoryData<string, string> Scenarios()
    {
        var data = new TheoryData<string, string>();
        foreach (var id in FlowDocs.ReferenceIds)
        {
            foreach (var scenario in ProcessOutline.From(FlowDocs.Process(id)).Scenarios)
            {
                data.Add(id, scenario);
            }
        }

        return data;
    }

    private static FlowLiveCase CaseAfter(BrainProcess process, string scenario, int commands, string caseRef = "c-1")
    {
        var replay = FlowReplay.Start(process, scenario, null, Children);
        for (var played = 0; played < commands && !replay.IsFinished; played++)
        {
            _ = replay.Next();
        }

        return FlowLiveScripts.FromSimulation(caseRef, replay.Session);
    }

    private static int Total(BrainProcess process, string scenario) => FlowReplay.Start(process, scenario, null, Children).Total;

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Every_prefix_of_every_scripted_history_is_explained_and_agrees_with_the_run(string id, string scenario)
    {
        var process = FlowDocs.Process(id);
        var outline = ProcessOutline.From(process);
        foreach (var locale in new[] { WorkbenchLocale.En, WorkbenchLocale.Es })
        {
            for (var commands = 0; commands <= Total(process, scenario); commands++)
            {
                var live = CaseAfter(process, scenario, commands);
                var path = LiveCasePath.From(process, live, locale, Children);

                path.IsExplained.Should().BeTrue(id + "/" + scenario + "/" + commands + ": " + path.Reason);
                path.Reason.Should().BeNull();
                var entered = live.History.Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => entry.Step!).ToList();
                path.Steps.Select(step => step.Id).Should().Equal(entered.Distinct(StringComparer.Ordinal));
                path.Steps.Sum(step => step.Times).Should().Be(entered.Count);
                path.Routes.All(route => outline.Step(route.From)!.Routes.Any(declared => declared.Label == route.Label && declared.Target == route.To)).Should().BeTrue();
                path.Routes.Count.Should().BeLessThanOrEqualTo(Math.Max(0, entered.Count - 1));
                var open = live.Status is ProcessStatus.Running or ProcessStatus.Waiting or ProcessStatus.Suspended;
                path.CurrentStep.Should().Be(open ? live.CurrentStep : null);
                path.Steps.Where(step => step.IsCurrent).Select(step => step.Id).Should().Equal(path.CurrentStep is null ? Array.Empty<string>() : new[] { path.CurrentStep });
                path.Waiting.Should().NotBeNullOrWhiteSpace();
                path.Side!.Version.Should().Be(outline.Version);
                path.Side.CurrentStep.Should().Be(path.CurrentStep is null ? null : FlowWords.For(process, locale, Children).Step(path.CurrentStep));
                if (path.CurrentStep is { } current)
                {
                    path.Side.Why.Should().NotBeNullOrWhiteSpace();
                    path.Side.Next.Should().HaveCount(outline.Step(current)!.Routes.Count);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_routes_of_a_finished_case_are_the_routes_coverage_reads_from_the_same_run(string id, string scenario)
    {
        var process = FlowDocs.Process(id);
        var replay = FlowReplay.Start(process, scenario, null, Children);
        _ = replay.PlayToEnd();

        var path = LiveCasePath.From(process, FlowLiveScripts.FromSimulation("c-1", replay.Session), WorkbenchLocale.En, Children);
        var coverage = FlowCoverage.Of(process, extraRuns: null);
        var taken = FlowCoverage.Compute(ProcessOutline.From(process), [replay.Session], WorkbenchLocale.En).Routes.Where(route => route.Times > 0).Select(route => (route.Step, route.Label)).ToHashSet();

        path.IsExplained.Should().BeTrue();
        path.Routes.Select(route => (route.From, route.Label)).Distinct().Should().BeEquivalentTo(taken);
        coverage.Problems.Should().BeEmpty();
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "en")]
    [InlineData(WorkbenchLocale.Es, "es")]
    public void The_text_of_every_reference_case_in_the_middle_and_at_the_end_is_the_golden_text(WorkbenchLocale locale, string language)
    {
        var text = new StringBuilder();
        foreach (var id in FlowDocs.ReferenceIds)
        {
            var process = FlowDocs.Process(id);
            foreach (var scenario in ProcessOutline.From(process).Scenarios)
            {
                var total = Total(process, scenario);
                foreach (var commands in new[] { total / 2, total }.Distinct())
                {
                    var live = CaseAfter(process, scenario, commands);
                    var path = LiveCasePath.From(process, live, locale, Children);
                    text.Append(id).Append(" / ").Append(scenario).Append(" / after ").Append(commands).Append(" of ").Append(total).Append(" commands: ").Append(live.Status).Append('\n');
                    text.Append("  steps: ").Append(string.Join(" > ", path.Steps.Select(step => step.Label + (step.Times > 1 ? " x" + step.Times : string.Empty) + (step.IsCurrent ? " [here]" : string.Empty)))).Append('\n');
                    text.Append("  waiting: ").Append(path.Waiting).Append('\n');
                    text.Append("  why: ").Append(path.Side!.Why).Append('\n');
                    text.Append("  next: ").Append(string.Join(" | ", path.Side.Next)).Append('\n');
                }
            }
        }

        var actual = text.ToString();
        Golden("live.reference." + language + ".txt", actual).Should().Be(actual);
    }

    private static string Golden(string name, string actual)
    {
        var path = Path.Combine(Path.GetDirectoryName(TestFiles.Locate("tests/fixtures/workbench/live/README.md"))!, name);
        if (Environment.GetEnvironmentVariable("BRAIN_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual);
        }

        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static (BrainProcess Process, FlowLiveCase Live) Midway()
    {
        var process = FlowDocs.Process("previsit.outreach");
        var scenario = ProcessOutline.From(process).Scenarios[0];
        return (process, CaseAfter(process, scenario, Total(process, scenario)));
    }

    [Fact]
    public void A_case_of_another_process_or_another_version_is_not_explained_and_says_why()
    {
        var (process, live) = Midway();
        var other = process.Id == "visit.note_wait" ? FlowDocs.Process("previsit.outreach") : FlowDocs.Process("visit.note_wait");

        var otherProcess = LiveCasePath.From(other, live, WorkbenchLocale.En);
        var otherVersion = LiveCasePath.From(process, live with { ProcessVersion = live.ProcessVersion + 1 }, WorkbenchLocale.Es);

        otherProcess.Unexplained.Should().Be(LiveUnexplained.OtherProcess);
        otherProcess.Reason.Should().Contain(live.ProcessId).And.Contain(other.Id);
        otherProcess.Steps.Should().BeEmpty();
        otherProcess.Side.Should().BeNull();
        otherVersion.Unexplained.Should().Be(LiveUnexplained.OtherVersion);
        otherVersion.Reason.Should().Contain((live.ProcessVersion + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)).And.Contain("versión");
    }

    [Fact]
    public void A_history_that_names_an_unknown_step_or_takes_an_impossible_route_or_disagrees_with_the_current_step_is_not_explained()
    {
        var (process, live) = Midway();
        var outline = ProcessOutline.From(process);
        var firstEntry = live.History.First(entry => entry.Kind == ProcessHistoryKind.StepEntered);
        var unknown = live with { History = [.. live.History.Select(entry => entry == firstEntry ? entry with { Step = "no.such.step" } : entry)] };
        var lastEntered = live.History.Last(entry => entry.Kind == ProcessHistoryKind.StepEntered);
        var start = outline.Start;
        var stranger = outline.Steps.First(step => step.Id != start && !outline.Step(start)!.Routes.Any(route => route.Target == step.Id)).Id;
        var impossible = new FlowLiveCase(
            "c-2", live.ProcessId, live.ProcessVersion, ProcessStatus.Waiting, stranger, null, null,
            [
                new FlowLiveEntry(1, firstEntry.At, start, ProcessHistoryKind.StepEntered, null, null),
                new FlowLiveEntry(2, firstEntry.At, stranger, ProcessHistoryKind.StepEntered, null, null),
            ],
            FlowLiveWait.Nothing);
        var mismatch = live with { Status = ProcessStatus.Waiting, CurrentStep = outline.Steps.First(step => step.Id != lastEntered.Step).Id, Result = null };
        var unknownCurrent = live with { Status = ProcessStatus.Waiting, CurrentStep = "no.such.step", Result = null };

        LiveCasePath.From(process, unknown, WorkbenchLocale.En).Unexplained.Should().Be(LiveUnexplained.UnknownStep);
        LiveCasePath.From(process, unknown, WorkbenchLocale.En).Reason.Should().Contain("no.such.step");
        LiveCasePath.From(process, unknownCurrent, WorkbenchLocale.En).Unexplained.Should().Be(LiveUnexplained.UnknownStep);
        var route = LiveCasePath.From(process, impossible, WorkbenchLocale.En);
        route.Unexplained.Should().Be(LiveUnexplained.ImpossibleRoute);
        route.Reason.Should().Contain(start).And.Contain(stranger);
        LiveCasePath.From(process, mismatch, WorkbenchLocale.Es).Unexplained.Should().Be(LiveUnexplained.CurrentStepMismatch);
        foreach (var path in new[] { LiveCasePath.From(process, unknown, WorkbenchLocale.En), route })
        {
            path.IsExplained.Should().BeFalse();
            path.Steps.Should().BeEmpty();
            path.Routes.Should().BeEmpty();
            path.CurrentStep.Should().BeNull();
            path.Waiting.Should().BeNull();
        }
    }

    [Fact]
    public void A_case_that_has_not_entered_a_step_has_an_empty_explained_path()
    {
        var process = FlowDocs.Process("previsit.outreach");
        var live = new FlowLiveCase("c-3", process.Id, ProcessOutline.From(process).Version, ProcessStatus.Running, null, null, null, [], FlowLiveWait.Nothing);

        var path = LiveCasePath.From(process, live, WorkbenchLocale.En);

        path.IsExplained.Should().BeTrue();
        path.Steps.Should().BeEmpty();
        path.CurrentStep.Should().BeNull();
        path.Waiting.Should().Be("The case has not entered a step yet.");
        path.Side!.Why.Should().BeNull();
    }

    [Fact]
    public void A_live_entry_keeps_the_members_an_explanation_reads_and_never_the_facts_or_what_a_person_typed()
    {
        var entry = new ProcessHistoryEntry(
            7,
            DateTimeOffset.Parse("2027-01-04T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            "decide",
            3,
            1,
            ProcessHistoryKind.Decided,
            null,
            "{\"rule\":\"r.one\",\"version\":2,\"decided\":true,\"matchedRowKeys\":[\"a\"],\"outcome\":{\"route\":\"x\"},\"factsHash\":\"h\",\"facts\":{\"patient\":\"secret\"},\"taskId\":\"t\",\"data\":{\"typed\":\"secret\"}}");

        var live = FlowLiveEntry.Of(entry);

        live.Sequence.Should().Be(7);
        live.Kind.Should().Be(ProcessHistoryKind.Decided);
        live.DataJson.Should().Contain("r.one").And.Contain("matchedRowKeys").And.Contain("outcome");
        live.DataJson.Should().NotContain("secret").And.NotContain("facts").And.NotContain("taskId").And.NotContain("typed");
        FlowLiveEntry.Of(entry with { DataJson = "{\"facts\":{}}" }).DataJson.Should().BeNull();
        FlowLiveEntry.Of(entry with { DataJson = null }).DataJson.Should().BeNull();
        FlowLiveEntry.Of(entry with { DataJson = "not json" }).DataJson.Should().BeNull();
    }

    [Fact]
    public void Counts_are_laid_over_the_map_per_process_step_and_phase_and_what_the_document_does_not_place_is_counted_apart()
    {
        var documents = FlowDocs.Reference.Select(item => item.Process).ToList();
        var lifecycle = FlowDocs.Process("case.lifecycle");
        var outline = ProcessOutline.From(lifecycle);
        var staged = outline.Steps.Where(step => step.Stage is not null).ToList();
        var first = staged[0];
        var second = staged.First(step => step.Stage != first.Stage);
        var counts = new FlowLiveCounts(
        [
            new FlowLiveCount("case.lifecycle", first.Id, 3, 1),
            new FlowLiveCount("case.lifecycle", second.Id, 2, 2),
            new FlowLiveCount("case.lifecycle", "gone.step", 4, 0),
            new FlowLiveCount("case.lifecycle", null, 1, 0),
            new FlowLiveCount("previsit.outreach", ProcessOutline.From(FlowDocs.Process("previsit.outreach")).Start, 5, 9),
            new FlowLiveCount("unknown.process", "any", 6, 0),
            new FlowLiveCount("case.lifecycle", first.Id, 0, 0),
        ],
        IsPartial: true);

        var live = ProcessMapLive.From(documents, counts);

        live.IsPartial.Should().BeTrue();
        live.Total.Should().Be(new LiveCount(21, 8));
        live.Process("case.lifecycle").Should().Be(new LiveCount(10, 3));
        live.Process("previsit.outreach").Should().Be(new LiveCount(5, 5), "waiting can never exceed open");
        live.Process("visit.note_wait").Should().Be(LiveCount.Zero);
        live.Step("case.lifecycle", first.Id).Should().Be(new LiveCount(3, 1));
        live.Phase("case.lifecycle", first.Stage).Should().Be(new LiveCount(3, 1));
        live.Phase("case.lifecycle", second.Stage).Should().Be(new LiveCount(2, 2));
        live.Unplaced.Should().Be(new LiveCount(11, 0), "a step the document lacks, a case with no step, a process nobody loaded");
        live.Phase("case.lifecycle", "no.such.stage").Should().Be(LiveCount.Zero);
        ProcessMapLive.From(documents, FlowLiveCounts.None).Total.IsZero.Should().BeTrue();
    }

    [Fact]
    public void The_cases_of_the_steps_of_one_stage_add_up_to_the_phase_and_the_phases_to_the_process_when_every_step_is_placed()
    {
        var lifecycle = FlowDocs.Process("case.lifecycle");
        var outline = ProcessOutline.From(lifecycle);
        var rows = outline.Steps.Select((step, index) => new FlowLiveCount(outline.Id, step.Id, index + 1, index % 2)).ToList();

        var live = ProcessMapLive.From([lifecycle], new FlowLiveCounts(rows));

        var phases = outline.Stages.Select(stage => live.Phase(outline.Id, stage)).Append(live.Phase(outline.Id, null)).Aggregate(LiveCount.Zero, (sum, item) => sum.Plus(item));
        phases.Should().Be(live.Process(outline.Id));
        live.Unplaced.IsZero.Should().BeTrue();
        live.Total.Open.Should().Be(rows.Sum(row => row.Open));
    }

    [Fact]
    public void The_live_contract_and_its_text_are_in_both_languages()
    {
        foreach (var key in FlowTemplates.EnglishKeys.Where(key => key.StartsWith("live.", StringComparison.Ordinal)))
        {
            FlowTemplates.Has(WorkbenchLocale.Es, key).Should().BeTrue(key);
        }

        FlowTemplates.EnglishKeys.Count(key => key.StartsWith("live.", StringComparison.Ordinal)).Should().BeGreaterThan(20);
    }

    [Fact]
    public void Null_arguments_are_refused()
    {
        var (process, live) = Midway();

        FluentActions.Invoking(() => LiveCasePath.From(null!, live, WorkbenchLocale.En)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => LiveCasePath.From(process, null!, WorkbenchLocale.En)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ProcessMapLive.From(null!, FlowLiveCounts.None)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ProcessMapLive.From([], null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowLiveEntry.Of(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowLiveScripts.FromSimulation(null!, FlowReplay.Start(process, ProcessOutline.From(process).Scenarios[0]).Session)).Should().Throw<ArgumentNullException>();
    }
}

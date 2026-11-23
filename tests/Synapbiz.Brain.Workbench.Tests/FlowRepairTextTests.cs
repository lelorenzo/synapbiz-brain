using System.Text.Json.Nodes;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The repair pass of the words of the flow surface (describer, trigger sentences, library) and of the review check.</summary>
public sealed class FlowRepairTextTests
{
    private static readonly Dictionary<string, string> NoInput = [];

    private static readonly string[] Crons = ["0 6 * * *", "0 6 * * 1-5", "0 6 * * 2,3", "*/5 9 1 * *"];

    private static ScheduleTriggerDeclaration Schedule(string cron, string? zone = null) => new("t", cron, zone, ProcessMisfire.Skip, NoInput);

    private static IEnumerable<BrainProcess> Everything() => FlowDocs.All.Select(item => item.Process);

    private static FlowStepView StepOf(string document, string id, WorkbenchLocale locale, IEnumerable<BrainProcess>? known = null) =>
        FlowDescriber.Describe(FlowDocs.Process(document), locale, known).Steps.Single(step => step.Id == id);

    // ---------------------------------------------------------------- values are written as code

    [Fact]
    public void The_result_of_a_child_is_written_as_code_unless_the_child_is_known()
    {
        var outline = ProcessOutline.From(FlowDocs.Process("case.lifecycle"));
        var subprocess = outline.Steps.First(step => step.Kind == OutlineStepKind.Subprocess && step.Routes.Any(route => route.Label.StartsWith("on:", StringComparison.Ordinal)));
        var value = subprocess.Routes.First(route => route.Label.StartsWith("on:", StringComparison.Ordinal)).Label[3..];

        var en = StepOf("case.lifecycle", subprocess.Id, WorkbenchLocale.En).Routes.Single(route => route.Label == "on:" + value).Phrase;
        var es = StepOf("case.lifecycle", subprocess.Id, WorkbenchLocale.Es).Routes.Single(route => route.Label == "on:" + value).Phrase;
        en.Should().StartWith("If the process ends with `" + value + "`: go to ");
        es.Should().StartWith("Si el proceso termina con `" + value + "`: ir a ");

        var known = StepOf("case.lifecycle", subprocess.Id, WorkbenchLocale.En, Everything()).Routes.Single(route => route.Label == "on:" + value).Phrase;
        var child = FlowDocs.All.Select(item => item.Process).First(process => ProcessOutline.From(process).Id == subprocess.Process);
        var label = ProcessDisplay.From(child).Result("en", value);
        label.Should().NotBeNull("the reference children label their results");
        known.Should().StartWith("If the process ends with " + label + ": go to ");
        FlowGraph.From(FlowDocs.Process("case.lifecycle"), WorkbenchLocale.En, Everything()).Edges
            .Single(edge => edge.From == subprocess.Id && edge.Label == "on:" + value).Phrase.Should().Be(known);
    }

    [Fact]
    public void The_values_a_decide_routes_on_are_written_as_code_in_both_languages()
    {
        StepOf("flow.batch", "judge", WorkbenchLocale.En).Routes.Single(route => route.Label == "on:high").Phrase.Should().Be("If `level` is `high`: go to Finished");
        StepOf("flow.batch", "judge", WorkbenchLocale.Es).Routes.Single(route => route.Label == "on:high").Phrase.Should().Be("Si `level` es `high`: ir a Terminado");
    }

    [Fact]
    public void A_deadline_at_an_instant_reads_by_it_and_a_timer_until_one_reads_until_it()
    {
        StepOf("fixture.reminder", "call_patient", WorkbenchLocale.En).Routes.Single(route => route.Label == "onTimeout").Phrase.Should().Contain("If no answer arrives by `visit_at`: go to ");
        StepOf("fixture.reminder", "call_patient", WorkbenchLocale.Es).Routes.Single(route => route.Label == "onTimeout").Phrase.Should().Contain("Si no llega respuesta antes de `visit_at`: ir a ");
        StepOf("flow.clock", "settle", WorkbenchLocale.En).Sentence.Should().Be("Waits until `due`.");
        StepOf("flow.clock", "settle", WorkbenchLocale.Es).Sentence.Should().Be("Espera hasta `due`.");
        StepOf("flow.clock", "hold", WorkbenchLocale.Es).Routes.Single(route => route.Label == "onTimeout").Phrase.Should().StartWith("Si no llega respuesta antes de `due`: ir a ");
        StepOf("flow.batch", "nap", WorkbenchLocale.En).Sentence.Should().Be("Waits until 1 hour after `visit_at`.");
        StepOf("flow.batch", "nap", WorkbenchLocale.Es).Sentence.Should().Be("Espera hasta 1 hora después de `visit_at`.");
    }

    // ---------------------------------------------------------------- trigger sentences

    [Fact]
    public void The_trigger_sentences_are_pinned_in_both_languages()
    {
        var triggers = ProcessOutline.From(FlowDocs.Process("flow.clock")).Triggers;
        triggers.Select(trigger => FlowDescriber.DescribeTrigger(trigger, WorkbenchLocale.En)).Should().Equal(
            "Starts when `clock.started` arrives, one case for each `ref`.",
            "Runs every day at 06:00 America/New_York.",
            "Runs every weekday at 18:45 America/Chicago.",
            "Runs every Sunday at 01:30 in the zone of the activation.");
        triggers.Select(trigger => FlowDescriber.DescribeTrigger(trigger, WorkbenchLocale.Es)).Should().Equal(
            "Empieza cuando llega `clock.started`, un caso por cada `ref`.",
            "Se ejecuta todos los días a las 06:00 America/New_York.",
            "Se ejecuta todos los días laborables a las 18:45 America/Chicago.",
            "Se ejecuta cada domingo a la 01:30 en la zona de la activación.");
    }

    [Fact]
    public void The_hour_of_a_schedule_says_a_la_only_for_one_in_the_morning()
    {
        string Es(string cron) => FlowDescriber.DescribeTrigger(Schedule(cron), WorkbenchLocale.Es);
        Es("0 1 * * *").Should().Contain(" a la 01:00 ");
        Es("5 13 * * *").Should().Contain(" a las 13:05 ");
        Es("30 0 * * *").Should().Contain(" a las 00:30 ");
        Es("30 11 * * *").Should().Contain(" a las 11:30 ");
        FlowDescriber.DescribeTrigger(Schedule("0 1 * * *"), WorkbenchLocale.En).Should().Be("Runs every day at 01:00 in the zone of the activation.");
    }

    [Fact]
    public void Day_seven_of_a_cron_is_sunday_and_a_list_of_days_has_no_repeats()
    {
        string En(string cron) => FlowDescriber.DescribeTrigger(Schedule(cron, "America/New_York"), WorkbenchLocale.En);
        En("0 8 * * 7").Should().Be("Runs every Sunday at 08:00 America/New_York.");
        En("0 8 * * 0,7").Should().Be("Runs every Sunday at 08:00 America/New_York.");
        En("0 8 * * 5-7").Should().Be("Runs every Sunday, Friday and Saturday at 08:00 America/New_York.");
        En("0 8 * * 0,1,6").Should().Be("Runs every Sunday, Monday and Saturday at 08:00 America/New_York.");
        En("*/5 9 1 * *").Should().Be("Runs on the schedule `*/5 9 1 * *` America/New_York.");
        En("0 8 * * 8").Should().StartWith("Runs on the schedule `0 8 * * 8`");
        FlowDescriber.DescribeTrigger(Schedule("0 8 * * 7", "America/New_York"), WorkbenchLocale.Es).Should().Be("Se ejecuta cada domingo a las 08:00 America/New_York.");
    }

    [Fact]
    public void Schedule_sentences_end_with_a_period_like_the_event_sentence()
    {
        foreach (var locale in new[] { WorkbenchLocale.En, WorkbenchLocale.Es })
        {
            foreach (var cron in Crons)
            {
                FlowDescriber.DescribeTrigger(Schedule(cron), locale).Should().EndWith(".");
            }

            ProcessOutline.From(FlowDocs.Process("case.lifecycle")).Triggers.Select(trigger => FlowDescriber.DescribeTrigger(trigger, locale)).Should().OnlyContain(text => text.EndsWith('.'));
        }
    }

    // ---------------------------------------------------------------- the describer

    [Fact]
    public void An_ai_step_with_its_own_target_for_a_plan_without_it_names_that_target_in_the_fallback_note()
    {
        var process = FlowDocs.Edited("flow.batch", document => document["body"]!["steps"]!["ask"]!["onNotEntitled"] = "ok");
        var ai = FlowDescriber.Describe(process, WorkbenchLocale.En).Steps.Single(step => step.Id == "ask").Ai!;
        ai.OnNotEntitled!.Target.Should().Be("ok");
        ai.Notes.Should().Contain(note => note.Contains("it goes on at Broken and Finished", StringComparison.Ordinal));
        FlowDescriber.Describe(FlowDocs.Process("flow.batch"), WorkbenchLocale.En).Steps.Single(step => step.Id == "ask").Ai!.Notes
            .Should().Contain(note => note.Contains("it goes on at Broken.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_poll_with_a_cap_says_so_only_when_the_backoff_is_exponential()
    {
        StepOf("flow.batch", "check", WorkbenchLocale.En).Sentence.Should().NotContain("never grows");
        var capped = ProcessOutline.From(FlowDocs.Process("visit.note_wait")).Steps.Single(step => step.Kind == OutlineStepKind.Poll);
        capped.Poll!.Backoff.Should().Be("exponential");
        StepOf("visit.note_wait", capped.Id, WorkbenchLocale.En).Sentence.Should().Contain(capped.Poll.Max is null ? "doubles the wait" : "The wait never grows beyond");
        var fixedWithCap = TryFixedPollWithCap();
        if (fixedWithCap is not null)
        {
            FlowDescriber.Describe(fixedWithCap, WorkbenchLocale.En).Steps.Single(step => step.Id == "check").Sentence.Should().NotContain("never grows");
        }
    }

    private static BrainProcess? TryFixedPollWithCap()
    {
        try
        {
            return FlowDocs.Edited("flow.batch", document => document["body"]!["steps"]!["check"]!["max"] = "PT5M");
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    [Fact]
    public void The_explicit_zone_of_a_schedule_is_shown()
    {
        FlowLibrary.From([FlowDocs.Process("flow.clock")], WorkbenchLocale.En).Single().Triggers.Should().Contain(text => text.Contains("06:00 America/New_York.", StringComparison.Ordinal));
        FlowLibrary.From([FlowDocs.Process("flow.clock")], WorkbenchLocale.En).Single().Triggers.Should().Contain(text => text.Contains("01:30 in the zone of the activation.", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- the library

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void The_library_matches_its_golden_text(string language)
    {
        var locale = language == "es" ? WorkbenchLocale.Es : WorkbenchLocale.En;
        var actual = FlowDocs.RenderLibrary(FlowLibrary.From(Everything(), locale));
        FlowDocs.Golden("library." + language + ".txt", actual).Should().Be(actual);
    }

    [Fact]
    public void Two_versions_of_one_flow_are_listed_in_ascending_order_whatever_the_order_given()
    {
        var second = FlowDocs.Edited("flow.bare", document => document["version"] = 2);
        var third = FlowDocs.Edited("flow.bare", document => document["version"] = 10);
        var entries = FlowLibrary.From([third, FlowDocs.Process("flow.bare"), second], WorkbenchLocale.En);
        entries.Select(entry => (entry.Id, entry.Version)).Should().Equal(("flow.bare", 1L), ("flow.bare", 2L), ("flow.bare", 10L));
    }

    [Fact]
    public void A_child_resolves_to_the_highest_version_in_the_set()
    {
        var newer = FlowDocs.Edited("flow.child", document =>
        {
            document["version"] = 2;
            document["meta"]!["display"]!["en"]!["title"] = "Batch child, second version";
        });
        foreach (var set in new[] { new[] { FlowDocs.Process("flow.batch"), FlowDocs.Process("flow.child"), newer }, new[] { newer, FlowDocs.Process("flow.child"), FlowDocs.Process("flow.batch") } })
        {
            FlowLibrary.From(set, WorkbenchLocale.En).Single(entry => entry.Id == "flow.batch").Children.Should().ContainSingle().Which.Title.Should().Be("Batch child, second version");
        }
    }

    [Fact]
    public void A_flow_with_a_scenario_that_fails_does_not_pass_and_one_with_none_is_told_apart()
    {
        var failing = FlowDocs.WithScenario(
            "flow.bare",
            """{"name":"it never completes","start":{"at":"2027-01-04T14:00:00Z","today":"2027-01-04","input":{"ref":"r"}},"events":[],"expect":{"status":"Completed"}}""");
        failing.RunScenarios().IsOk.Should().BeFalse();
        var entry = FlowLibrary.From([failing], WorkbenchLocale.En).Single();
        entry.ScenarioCount.Should().Be(2);
        entry.ScenariosPass.Should().BeFalse();

        var none = FlowLibrary.From([FlowDocs.Process("flow.clock")], WorkbenchLocale.En).Single();
        none.ScenarioCount.Should().Be(0);
        none.ScenariosPass.Should().BeTrue();
        none.CoveredRoutes.Should().Be(0);
        none.TotalRoutes.Should().BeGreaterThan(0);
        FlowLibrary.From([FlowDocs.Process("flow.bare")], WorkbenchLocale.En).Single().ScenariosPass.Should().BeTrue();
    }

    [Fact]
    public void A_spanish_entry_has_spanish_trigger_sentences()
    {
        var es = FlowLibrary.From([FlowDocs.Process("flow.clock")], WorkbenchLocale.Es).Single();
        es.Triggers.Should().Equal(
            "Empieza cuando llega `clock.started`, un caso por cada `ref`.",
            "Se ejecuta todos los días a las 06:00 America/New_York.",
            "Se ejecuta todos los días laborables a las 18:45 America/Chicago.",
            "Se ejecuta cada domingo a la 01:30 en la zona de la activación.");
        es.Title.Should().Be("Vigilancia del reloj");
    }

    [Fact]
    public void What_the_library_hands_out_cannot_be_changed_through_it()
    {
        var entries = FlowLibrary.From(Everything(), WorkbenchLocale.En);
        entries.GetType().Should().NotBe<List<FlowEntry>>();
        ((ICollection<FlowEntry>)entries).IsReadOnly.Should().BeTrue();
        entries[0].StepCounts.GetType().Should().NotBe<Dictionary<OutlineStepKind, int>>();
        var coverage = FlowCoverage.Of(FlowDocs.Process("flow.batch"));
        ((ICollection<FlowRouteCoverage>)coverage.Routes).IsReadOnly.Should().BeTrue();
        coverage.Routes.GetType().Should().NotBe<List<FlowRouteCoverage>>();
        var graph = FlowGraph.From(FlowDocs.Process("flow.batch"), WorkbenchLocale.En);
        graph.Nodes.GetType().Should().NotBe<List<FlowNode>>();
        graph.Edges.GetType().Should().NotBe<List<FlowEdge>>();
    }

    // ---------------------------------------------------------------- the review check

    private static readonly string[] UnknownTargets = ["Process", "process", "route:/next", "route:ask", "route:ask/", "route:ask/next", "step:Ask", "Step:ask", "stage:Run", "trigger:Weekdays", "scenario:"];

    [Fact]
    public void A_target_that_differs_only_in_case_or_has_no_step_is_unknown()
    {
        var process = FlowDocs.Process("flow.batch");
        var outline = ProcessOutline.From(process);
        var review = new FlowReview
        {
            ProcessId = outline.Id,
            ProcessVersion = outline.Version,
            ProcessContentHash = outline.ContentHash,
            Reviewer = "r",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Language = WorkbenchLocale.En,
            Items = [.. UnknownTargets.Select(target => new ReviewItem(target, ReviewState.Question, "?"))],
            ProposedScenarios = [],
        };
        var unknown = FlowReviewCheck.Against(review, process).Findings.Where(finding => finding.Code == FlowReviewCheck.ErrorTargetUnknown).Select(finding => finding.Path).ToList();
        unknown.Should().Equal("/items/0/target", "/items/10/target", "/items/2/target", "/items/3/target", "/items/4/target", "/items/6/target", "/items/7/target", "/items/8/target", "/items/9/target");
    }

    [Fact]
    public void The_format_name_is_compared_exactly()
    {
        var document = """{"$spec":"Brain.Process-Review@1","process":{},"reviewer":"r","createdAt":"2026-11-22T21:00:00+00:00","language":"en","items":[],"proposedScenarios":[]}""";
        var parsed = FlowReviewJson.Parse(document);
        parsed.Errors.Should().ContainSingle().Which.Code.Should().Be(FlowReviewJson.ErrorUnknownFormat);
        FlowReviewJson.Parse(document.Replace("Brain.Process-Review@1", "brain.process-review@1", StringComparison.Ordinal)).Errors.Should().NotContain(error => error.Code == FlowReviewJson.ErrorUnknownFormat);
    }

    [Fact]
    public void A_scenario_that_is_not_an_object_has_its_text_in_both_languages_from_the_template_table()
    {
        FlowTemplates.Template(WorkbenchLocale.En, "review.scenario_not_object").Should().Be("The proposed scenario is not a JSON object.");
        FlowTemplates.Template(WorkbenchLocale.Es, "review.scenario_not_object").Should().Be("El escenario propuesto no es un objeto JSON.");
        FlowReviewCheck.ErrorScenarioNotObject.Should().Be("workbench.flow_review.scenario_not_object");
    }

    [Fact]
    public void Edited_documents_load_like_the_original()
    {
        JsonNode.Parse(FlowDocs.SourceOf("flow.bare").Text).Should().NotBeNull();
        ProcessOutline.From(FlowDocs.Edited("flow.bare", _ => { })).ContentHash.Should().NotBeNullOrEmpty();
    }
}

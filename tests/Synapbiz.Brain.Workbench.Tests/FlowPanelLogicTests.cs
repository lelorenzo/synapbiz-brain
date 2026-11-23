using System.Globalization;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The logic the flow panels lean on: families, short labels, route stems, the next due moment, the answers a scenario gives, replay, the report, review targets and the sentences for codes.</summary>
public sealed class FlowPanelLogicTests
{
    private const string BatchInput = """{"batch_ref":"b-1","items":[{"id":"a"},{"id":"b"}],"visit_at":"2027-01-10T15:00:00Z"}""";
    private const string Transient = "a transient failure of the reminder is retried inside the window";
    private const string SmallPick = "a small pick ends the batch as broken";

    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2027-01-04T14:00:00Z", CultureInfo.InvariantCulture);

    private static FlowSimulationSetup Setup(string? input = null, IFlowAnswerSource? source = null, IReadOnlyCollection<BrainProcess>? children = null, DateTimeOffset? at = null, Dictionary<string, TimeSpan>? zones = null)
    {
        var start = at ?? At;
        return new FlowSimulationSetup
        {
            At = start,
            Today = FlowSimulationSetup.TodayOf(start),
            InputJson = input,
            AnswerSource = source,
            ChildProcesses = children,
            Zones = zones ?? new Dictionary<string, TimeSpan>(),
        };
    }

    // ---------------------------------------------------------------- family, short label and stem

    [Theory]
    [InlineData("previsit.outreach", "check", FlowFamily.Action)]
    [InlineData("previsit.outreach", "assess", FlowFamily.Decision)]
    [InlineData("previsit.outreach", "cycle", FlowFamily.Decision)]
    [InlineData("previsit.outreach", "escalate", FlowFamily.Person)]
    [InlineData("previsit.outreach", "reply", FlowFamily.Wait)]
    [InlineData("previsit.outreach", "done", FlowFamily.End)]
    [InlineData("flow.batch", "child", FlowFamily.Flow)]
    [InlineData("flow.batch", "ask", FlowFamily.Ai)]
    [InlineData("flow.batch", "nap", FlowFamily.Wait)]
    public void A_step_has_the_family_of_its_kind(string document, string step, FlowFamily family)
    {
        FlowGraph.From(FlowDocs.Process(document), WorkbenchLocale.En).Nodes.Single(node => node.Id == step).Family.Should().Be(family);
    }

    [Fact]
    public void Every_kind_of_step_has_a_family_and_the_families_are_all_used_by_the_reference_documents()
    {
        var families = FlowDocs.All.SelectMany(item => FlowGraph.From(item.Process, WorkbenchLocale.En).Nodes).Select(node => node.Family).Distinct().ToList();
        families.Should().BeEquivalentTo(Enum.GetValues<FlowFamily>());
    }

    [Fact]
    public void A_route_that_names_a_value_has_a_short_label_and_one_that_does_not_has_none()
    {
        var graph = FlowGraph.From(FlowDocs.Process("previsit.outreach"), WorkbenchLocale.En);
        graph.Edges.Single(edge => edge.From == "assess" && edge.To == "send").ShortLabel.Should().Be("some");
        graph.Edges.Single(edge => edge.From == "assess" && edge.To == "done").ShortLabel.Should().Be("none");
        graph.Edges.Single(edge => edge.From == "check" && edge.To == "assess").ShortLabel.Should().BeNull();
        var person = graph.Edges.Single(edge => edge.From == "escalate" && edge.Label == "on:unreachable");
        person.ShortLabel.Should().NotBeNullOrWhiteSpace().And.NotStartWith("on:");
    }

    [Fact]
    public void The_short_label_of_a_person_task_is_the_outcome_in_the_asked_language()
    {
        var en = FlowGraph.From(FlowDocs.Process("previsit.outreach"), WorkbenchLocale.En).Edges.Single(edge => edge.From == "escalate" && edge.Label == "on:unreachable").ShortLabel;
        var es = FlowGraph.From(FlowDocs.Process("previsit.outreach"), WorkbenchLocale.Es).Edges.Single(edge => edge.From == "escalate" && edge.Label == "on:unreachable").ShortLabel;
        en.Should().Be(FlowWords.For(FlowDocs.Process("previsit.outreach"), WorkbenchLocale.En).Outcome("unreachable"));
        es.Should().Be(FlowWords.For(FlowDocs.Process("previsit.outreach"), WorkbenchLocale.Es).Outcome("unreachable"));
    }

    [Fact]
    public void The_stem_of_a_route_plus_its_target_label_is_the_phrase_in_every_document_and_language()
    {
        foreach (var locale in new[] { WorkbenchLocale.En, WorkbenchLocale.Es })
        {
            foreach (var (name, process) in FlowDocs.All)
            {
                var description = FlowDescriber.Describe(process, locale);
                foreach (var route in description.Steps.SelectMany(step => step.Routes))
                {
                    (route.Stem + route.TargetLabel).Should().Be(route.Phrase, $"{name} {locale} {route.Label}");
                }
            }
        }
    }

    // ---------------------------------------------------------------- the clock and the answers a session has had

    [Fact]
    public void The_next_due_moment_is_the_timer_that_is_waiting_and_is_null_when_nothing_is_timed()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("flow.batch"), Setup(BatchInput, null, [FlowDocs.Process("flow.child")]));
        session.NextDue.Should().BeNull();
        while (session.Outstanding.OfType<OutstandingChild>().FirstOrDefault() is { } child)
        {
            session.CompleteChild(child.Id, "done");
        }

        var timer = session.Outstanding.OfType<OutstandingTimer>().Single();
        session.NextDue.Should().Be(timer.Due).And.BeAfter(At);
    }

    [Fact]
    public void The_next_due_moment_is_also_the_opening_of_a_delivery_window()
    {
        var zones = new Dictionary<string, TimeSpan> { ["America/New_York"] = TimeSpan.FromHours(-5) };
        var session = FlowSimulation.Start(
            FlowDocs.Process("previsit.outreach"),
            Setup("""{"case_ref":"c-1001","practice_zone":"America/New_York"}""", null, null, DateTimeOffset.Parse("2027-01-08T23:00:00Z", CultureInfo.InvariantCulture), zones));
        session.CompleteEffect(session.Outstanding.OfType<OutstandingEffect>().Single().Id, """{"missing_count":2}""");
        var window = session.Outstanding.OfType<OutstandingWindow>().Single();
        session.NextDue.Should().Be(window.OpensAt);
        session.AdvanceBy(window.OpensAt - session.Now);
        session.CompleteEffect(session.Outstanding.OfType<OutstandingEffect>().Single().Id, "{}");
        var timeout = session.Outstanding.OfType<OutstandingTimer>().Single();
        session.NextDue.Should().Be(timeout.Due);
    }

    [Fact]
    public void The_second_visit_of_an_answered_step_is_offered_the_second_answer_of_the_scenarios()
    {
        var process = FlowDocs.Process("previsit.outreach");
        FlowScenarioAnswers.For(process, "send", 0).Single(answer => answer.Scenario == Transient).Answer.IsFailure.Should().BeTrue();
        FlowScenarioAnswers.For(process, "send", 1).Single(answer => answer.Scenario == Transient).Answer.IsFailure.Should().BeFalse();

        var replay = FlowReplay.Start(process, Transient);
        replay.Next();
        FlowScenarioAnswers.For(replay.Session, "send").Single(answer => answer.Scenario == Transient).Answer.IsFailure.Should().BeTrue();
        replay.Session.AnsweredCount("send").Should().Be(0);
        replay.Next();
        replay.Session.AnsweredCount("send").Should().Be(1);
        FlowScenarioAnswers.For(replay.Session, "send").Single(answer => answer.Scenario == Transient).Answer.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void An_answer_that_was_refused_is_not_counted()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("flow.batch"), Setup(BatchInput, null, [FlowDocs.Process("flow.child")]));
        session.CompleteEffect("no-such-effect", "{}");
        session.AnsweredCount("ask").Should().Be(0);
    }

    [Fact]
    public void A_log_entry_lists_the_steps_it_entered_says_whether_it_was_automatic_and_whether_it_ended_the_case()
    {
        var replay = FlowReplay.Start(FlowDocs.Process("previsit.outreach"), "nothing is missing");
        var entries = replay.PlayToEnd();
        entries.SelectMany(entry => entry.EnteredSteps).Should().Equal(replay.Session.EnteredSteps.Skip(replay.Session.EnteredSteps.Count - entries.SelectMany(entry => entry.EnteredSteps).Count()));
        entries.Where(entry => entry.EndedCase).Should().ContainSingle();
        entries.Should().OnlyContain(entry => !entry.IsAutomatic);

        var timed = FlowReplay.Start(FlowDocs.Process("previsit.outreach"), "three reminders without a reply end at the front desk");
        timed.PlayToEnd().Where(entry => entry.IsAutomatic).Should().NotBeEmpty();
    }

    [Fact]
    public void Today_is_the_utc_date_of_an_instant()
    {
        FlowSimulationSetup.TodayOf(DateTimeOffset.Parse("2027-01-04T23:30:00-05:00", CultureInfo.InvariantCulture)).Should().Be(new DateOnly(2027, 1, 5));
        FlowSimulationSetup.TodayOf(At).Should().Be(new DateOnly(2027, 1, 4));
    }

    // ---------------------------------------------------------------- asking one effect

    [Fact]
    public async Task Asking_for_one_effect_applies_the_answer_of_the_source_for_that_effect()
    {
        var seen = new List<string>();
        var source = new PickSource(seen, FlowAnswer.Output("""{"pick":"p","size":3}"""));
        var session = FlowSimulation.Start(FlowDocs.Process("flow.batch"), Setup(BatchInput, source, [FlowDocs.Process("flow.child")]));
        while (session.Outstanding.OfType<OutstandingChild>().FirstOrDefault() is { } child)
        {
            session.CompleteChild(child.Id, "done");
        }

        session.AdvanceBy(TimeSpan.FromDays(7));
        var effect = session.Outstanding.OfType<OutstandingEffect>().Single();
        var entries = await session.AskAsync(effect.Id);
        seen.Should().Equal(effect.Id);
        entries.Should().ContainSingle().Which.Kind.Should().Be(FlowSimulationEntryKind.EffectAnswered);
        session.AnsweredCount(effect.Step).Should().Be(1);
    }

    [Fact]
    public async Task Asking_for_an_effect_that_is_not_outstanding_is_a_refusal_and_the_source_is_not_asked()
    {
        var seen = new List<string>();
        var session = FlowSimulation.Start(FlowDocs.Process("flow.batch"), Setup(BatchInput, new PickSource(seen, FlowAnswer.Output("{}")), [FlowDocs.Process("flow.child")]));
        var entries = await session.AskAsync("no-such-effect");
        entries.Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorEffectUnknown);
        seen.Should().BeEmpty();
    }

    [Fact]
    public async Task Asking_a_source_that_throws_is_a_refusal_with_the_code_of_a_failed_source()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("flow.batch"), Setup(BatchInput, new ThrowingSource(), [FlowDocs.Process("flow.child")]));
        while (session.Outstanding.OfType<OutstandingChild>().FirstOrDefault() is { } child)
        {
            session.CompleteChild(child.Id, "done");
        }

        session.AdvanceBy(TimeSpan.FromDays(7));
        var effect = session.Outstanding.OfType<OutstandingEffect>().Single();
        var entries = await session.AskAsync(effect.Id);
        entries.Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorAnswerSourceFailed);
        session.Outstanding.OfType<OutstandingEffect>().Should().ContainSingle();
    }

    [Fact]
    public async Task Asking_without_a_source_is_an_error_of_the_caller()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("flow.batch"), Setup(BatchInput, null, [FlowDocs.Process("flow.child")]));
        await FluentActions.Awaiting(() => session.AskAsync("x")).Should().ThrowAsync<InvalidOperationException>();
    }

    // ---------------------------------------------------------------- replay

    [Fact]
    public void A_replay_applies_one_command_at_a_time_and_ends_with_the_result_of_the_scenario()
    {
        var process = FlowDocs.Process("flow.batch");
        var replay = FlowReplay.Start(process, SmallPick, null, [FlowDocs.Process("flow.child")]);
        replay.Name.Should().Be(SmallPick);
        replay.Played.Should().Be(0);
        replay.Total.Should().BeGreaterThan(1);
        replay.IsFinished.Should().BeFalse();
        replay.Failure.Should().BeNull();
        replay.Next().Should().NotBeEmpty();
        replay.Played.Should().Be(1);
        replay.IsFinished.Should().BeFalse();
        replay.PlayToEnd().Should().NotBeEmpty();
        replay.IsFinished.Should().BeTrue();
        replay.Played.Should().Be(replay.Total);
        replay.Failure.Should().BeNull();
        replay.Next().Should().BeEmpty();
        replay.Session.Status.Should().Be(ProcessStatus.Completed);
    }

    [Fact]
    public void Playing_a_replay_to_the_end_gives_the_session_of_the_scenario_player()
    {
        var process = FlowDocs.Process("flow.batch");
        var children = new[] { FlowDocs.Process("flow.child") };
        var replay = FlowReplay.Start(process, SmallPick, null, children);
        replay.PlayToEnd();
        var played = FlowSimulation.Replay(process, SmallPick, null, children);
        replay.Session.Log.Select(entry => (entry.Kind, entry.Step, entry.Disposition)).Should().Equal(played.Log.Select(entry => (entry.Kind, entry.Step, entry.Disposition)));
        replay.Session.History.Count.Should().Be(played.History.Count);
    }

    [Fact]
    public void A_replay_carries_the_answer_source_for_the_reader_to_go_on_with()
    {
        var source = new PickSource([], FlowAnswer.Output("{}"));
        FlowReplay.Start(FlowDocs.Process("flow.batch"), SmallPick, source, [FlowDocs.Process("flow.child")]).Session.AnswerSource.Should().BeSameAs(source);
        FlowSimulation.Replay(FlowDocs.Process("flow.batch"), SmallPick, source, [FlowDocs.Process("flow.child")]).AnswerSource.Should().BeSameAs(source);
    }

    [Fact]
    public void A_replay_of_a_scenario_that_expects_the_wrong_result_ends_with_a_failure()
    {
        var process = FlowDocs.Process("flow.batch");
        var json = ProcessOutline.From(process).ScenarioJson(SmallPick)!.Replace("\"broken\"", "\"ok\"", StringComparison.Ordinal);
        var replay = FlowReplay.StartJson(process, json, null, [FlowDocs.Process("flow.child")]);
        replay.PlayToEnd();
        replay.IsFinished.Should().BeTrue();
        replay.Failure.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_replay_refuses_a_name_or_a_text_that_is_not_a_scenario_of_the_document()
    {
        var process = FlowDocs.Process("flow.batch");
        FluentActions.Invoking(() => FlowReplay.Start(process, "no such scenario")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => FlowReplay.StartJson(process, "{}")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => FlowReplay.StartJson(process, "not json")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => FlowReplay.Start(null!, "x")).Should().Throw<ArgumentNullException>();
    }

    // ---------------------------------------------------------------- the report

    [Fact]
    public void The_report_plays_each_scenario_once_and_has_the_results_the_traces_and_the_coverage()
    {
        var process = FlowDocs.Process("flow.batch");
        var report = FlowScenarioReport.Of(process, WorkbenchLocale.En);
        report.ScenariosPlayed.Should().Be(ProcessOutline.From(process).Scenarios.Count);
        report.Scenarios.Select(result => result.Name).Should().Equal(ProcessOutline.From(process).Scenarios);
        report.AllPass.Should().Be(report.Scenarios.All(result => result.Passed));
        report.Scenarios.Should().OnlyContain(result => result.Trace.Count > 0);
        report.Coverage.TotalRoutes.Should().BeGreaterThan(0);
        report.Process.Should().BeSameAs(process);
    }

    [Fact]
    public void A_proposed_scenario_is_played_alone_and_one_that_is_not_valid_has_no_result()
    {
        var process = FlowDocs.Process("flow.batch");
        var json = ProcessOutline.From(process).ScenarioJson(SmallPick)!;
        var result = FlowScenarioReport.ResultOf(process, "mine", json)!;
        result.Name.Should().Be("mine");
        result.Passed.Should().BeTrue();
        result.Trace.Should().Equal(FlowScenarioReport.Of(process, WorkbenchLocale.En).Scenarios.Single(item => item.Name == SmallPick).Trace);
        FlowScenarioReport.ResultOf(process, "mine", json.Replace("\"broken\"", "\"ok\"", StringComparison.Ordinal))!.Passed.Should().BeFalse();
        FlowScenarioReport.ResultOf(process, "mine", "{}").Should().BeNull();
        FlowScenarioReport.ResultOf(process, "mine", "nope").Should().BeNull();
    }

    [Fact]
    public void The_library_takes_what_it_needs_from_reports_the_caller_kept()
    {
        var set = FlowDocs.Reference.Select(item => item.Process).ToList();
        var asked = new List<string>();
        var entries = FlowLibrary.From(set, WorkbenchLocale.En, process =>
        {
            asked.Add(process.Id);
            return FlowScenarioReport.Of(process, WorkbenchLocale.En);
        });
        asked.Should().BeEquivalentTo(set.Select(process => process.Id));
        entries.Select(entry => (entry.Id, entry.ScenarioCount, entry.CoveredRoutes)).Should().Equal(FlowLibrary.From(set, WorkbenchLocale.En).Select(entry => (entry.Id, entry.ScenarioCount, entry.CoveredRoutes)));
    }

    [Fact]
    public void The_results_a_flow_can_give_are_those_of_its_end_steps_each_once()
    {
        FlowLibrary.ResultsOf(FlowDocs.Process("flow.child")).Should().NotBeEmpty().And.OnlyHaveUniqueItems();
        FlowLibrary.ResultsOf(FlowDocs.Process("previsit.outreach")).Should().Contain("previsit_closed");
        FluentActions.Invoking(() => FlowLibrary.ResultsOf(null!)).Should().Throw<ArgumentNullException>();
    }

    // ---------------------------------------------------------------- review targets

    [Fact]
    public void The_review_targets_of_a_flow_are_the_flow_its_triggers_stages_steps_routes_and_scenarios_in_that_order()
    {
        var process = FlowDocs.Process("flow.batch");
        var targets = FlowReviewTargets.Of(process);
        targets[0].Should().Be("process");
        targets.Should().OnlyHaveUniqueItems();
        targets.Should().Contain(["trigger:weekdays", "trigger:mondays", "trigger:odd", "trigger:midweek", "stage:run", "step:ask", "route:ask/onInvalid", "route:judge/on:high", "scenario:" + SmallPick]);
        FlowReviewTargets.TriggerIds(process).Should().Equal("weekdays", "mondays", "odd", "midweek");
        var kinds = targets.Select(target => target.Split(':')[0]).ToList();
        kinds.IndexOf("trigger").Should().BeLessThan(kinds.IndexOf("stage"));
        kinds.IndexOf("stage").Should().BeLessThan(kinds.IndexOf("step"));
        kinds.IndexOf("step").Should().BeLessThan(kinds.IndexOf("route"));
        kinds.IndexOf("route").Should().BeLessThan(kinds.IndexOf("scenario"));
    }

    [Fact]
    public void An_event_trigger_is_a_target_named_by_its_event()
    {
        var process = FlowDocs.Process("case.lifecycle");
        var ids = FlowReviewTargets.TriggerIds(process);
        ids.Should().NotBeEmpty();
        FlowReviewTargets.Of(process).Should().Contain(ids.Select(FlowReviewTargets.Trigger));
        FlowReviewTargets.Trigger("case.opened").Should().Be("trigger:case.opened");
        FlowReviewTargets.Route("ask", "next").Should().Be("route:ask/next");
        FlowReviewTargets.Flow.Should().Be("process");
    }

    [Fact]
    public void Every_review_target_of_every_document_is_valid_for_the_review()
    {
        foreach (var (name, process) in FlowDocs.All)
        {
            var outline = ProcessOutline.From(process);
            var review = new FlowReview
            {
                ProcessId = outline.Id,
                ProcessVersion = outline.Version,
                ProcessContentHash = outline.ContentHash,
                Reviewer = "Dr. Example",
                CreatedAt = At,
                Language = WorkbenchLocale.En,
                Items = [.. FlowReviewTargets.Of(process).Select(target => new ReviewItem(target, ReviewState.Confirmed, string.Empty))],
                ProposedScenarios = [],
            };
            FlowReviewCheck.Against(review, process).Findings.Should().BeEmpty(name);
        }
    }

    // ---------------------------------------------------------------- the sentences for codes

    [Fact]
    public void A_code_the_interpreter_gives_is_a_sentence_in_both_languages()
    {
        string[] codes =
        [
            "process.input.unknown", "process.variable.type_mismatch", FlowSimulation.ErrorJsonInvalid, FlowSimulation.ErrorDurationInvalid,
            FlowSimulation.ErrorNotStarted, FlowSimulation.ErrorAnswerSourceFailed, FlowSimulation.ErrorEffectUnknown, "process.trigger.stale",
            "process.task.outcome_unknown", "process.event.not_accepted", "process.window.zone_unknown", "process.effect.output_malformed",
            "process.ai.output_invalid", FlowCodes.ZoneInvalid, FlowCodes.ChildUnknown,
        ];
        foreach (var code in codes)
        {
            var en = FlowCodes.Describe(code, WorkbenchLocale.En);
            var es = FlowCodes.Describe(code, WorkbenchLocale.Es);
            en.Should().NotBeNullOrWhiteSpace().And.NotContain(code, code);
            es.Should().NotBeNullOrWhiteSpace().And.NotContain(code, code).And.NotBe(en, code);
        }
    }

    [Fact]
    public void The_sentence_for_an_unknown_zone_says_that_the_zone_is_not_known()
    {
        FlowCodes.Describe("process.window.zone_unknown", WorkbenchLocale.En).Should().ContainEquivalentOf("zone");
        FlowCodes.Describe("process.window.zone_unknown", WorkbenchLocale.Es).Should().ContainEquivalentOf("zona");
        FlowCodes.Describe("process.window.zone_unknown", WorkbenchLocale.En).Should().NotBe(FlowCodes.Describe("process.trigger.stale", WorkbenchLocale.En));
    }

    [Fact]
    public void A_code_the_table_does_not_know_is_written_between_backticks_and_no_code_is_an_unknown_reason()
    {
        FlowCodes.Describe("vendor.odd_code", WorkbenchLocale.En).Should().Contain("`vendor.odd_code`");
        FlowCodes.Describe("vendor.odd_code", WorkbenchLocale.Es).Should().Contain("`vendor.odd_code`");
        FlowCodes.Describe(null, WorkbenchLocale.En).Should().NotBeNullOrWhiteSpace().And.NotContain("`");
        FlowCodes.Describe(string.Empty, WorkbenchLocale.Es).Should().Be(FlowCodes.Describe(null, WorkbenchLocale.Es));
    }

    // ---------------------------------------------------------------- a start the interpreter rejects

    [Fact]
    public void A_start_whose_input_names_a_variable_the_flow_does_not_have_is_rejected_and_the_session_has_not_started()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("previsit.outreach"), Setup("""{"no_such_variable":1}"""));
        session.Log.Should().ContainSingle(entry => entry.Kind == FlowSimulationEntryKind.Started && entry.Disposition != AdvanceDisposition.Applied);
        session.Log.Single(entry => entry.Kind == FlowSimulationEntryKind.Started).Code.Should().Be("process.input.unknown");
        session.Outstanding.Should().BeEmpty();
    }

    private sealed class PickSource(List<string> seen, FlowAnswer answer) : IFlowAnswerSource
    {
        public string Name => "Pick";

        public ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken)
        {
            seen.Add(request.EffectId);
            return ValueTask.FromResult<FlowAnswer?>(answer);
        }
    }

    private sealed class ThrowingSource : IFlowAnswerSource
    {
        public string Name => "Throwing";

        public ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the account is down");
    }
}

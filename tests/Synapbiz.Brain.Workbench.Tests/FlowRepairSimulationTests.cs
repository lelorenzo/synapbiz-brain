using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The repair pass of the flow simulator: inputs it must refuse, the clock, the session after a rejected start, and what the answer loop does.</summary>
public sealed class FlowRepairSimulationTests
{
    private const string Duplicate = """{"a":1,"a":2}""";
    private const string BatchInput = """{"batch_ref":"b-1","items":[{"id":"a"},{"id":"b"}],"visit_at":"2027-01-10T15:00:00Z"}""";
    private const string NoStepProcess = """
        {"$spec":"brain.process@1","id":"flow.susp","version":1,"uses":[],"requires":[],"meta":{},
         "body":{"subject":"case","stages":[],"variables":{},"start":"r",
          "steps":{"r":{"kind":"read","port":"p.read","next":"e"},"e":{"kind":"end","result":"done"}},"scenarios":[]}}
        """;

    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2027-01-04T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly DateOnly Today = new(2027, 1, 4);
    private static readonly Dictionary<string, TimeSpan> NewYork = new() { ["America/New_York"] = TimeSpan.FromHours(-5) };

    private static FlowSimulationSetup Setup(string? input = null, IFlowAnswerSource? source = null, IReadOnlyCollection<BrainProcess>? children = null, bool zones = false, DateTimeOffset? at = null) =>
        new() { At = at ?? At, Today = Today, InputJson = input, AnswerSource = source, ChildProcesses = children, Zones = zones ? NewYork : new Dictionary<string, TimeSpan>() };

    private static FlowSimulation Batch(IFlowAnswerSource? source = null) =>
        FlowSimulation.Start(FlowDocs.Process("flow.batch"), Setup(BatchInput, source, [FlowDocs.Process("flow.child")]));

    private static FlowSimulation AtAsk(IFlowAnswerSource? source = null)
    {
        var session = Batch(source);
        while (session.Outstanding.OfType<OutstandingChild>().FirstOrDefault() is { } child)
        {
            session.CompleteChild(child.Id, "done");
        }

        session.AdvanceBy(TimeSpan.FromDays(7));
        return session;
    }

    private static FlowSimulation Outreach(IFlowAnswerSource? source = null) =>
        FlowSimulation.Start(FlowDocs.Process("previsit.outreach"), Setup("""{"case_ref":"c-1","practice_zone":"America/New_York"}""", source, zones: true));

    // ---------------------------------------------------------------- A1: a text that repeats a member name

    [Fact]
    public void An_effect_answer_that_repeats_a_member_name_is_refused_and_nothing_changes()
    {
        var session = AtAsk();
        var effect = session.Outstanding.OfType<OutstandingEffect>().Single();
        var history = session.History.Count;
        session.CompleteEffect(effect.Id, Duplicate).Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorJsonInvalid);
        session.History.Count.Should().Be(history);
        session.Outstanding.OfType<OutstandingEffect>().Should().ContainSingle().Which.Id.Should().Be(effect.Id);
    }

    [Fact]
    public void An_event_payload_that_repeats_a_member_name_is_refused()
    {
        var session = Outreach();
        session.SendEvent("case.reply_received", "c-1", Duplicate).Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorJsonInvalid);
    }

    [Fact]
    public void Task_data_that_repeats_a_member_name_is_refused()
    {
        var session = Batch();
        session.CompleteTask("t", "o", Duplicate).Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorJsonInvalid);
    }

    [Fact]
    public void A_child_output_that_repeats_a_member_name_is_refused_and_the_child_stays_outstanding()
    {
        var session = Batch();
        var key = session.Outstanding.OfType<OutstandingChild>().Single().Id;
        session.CompleteChild(key, "done", Duplicate).Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorJsonInvalid);
        session.Outstanding.OfType<OutstandingChild>().Should().ContainSingle().Which.Id.Should().Be(key);
    }

    [Fact]
    public void Input_that_repeats_a_member_name_cannot_start_a_case()
    {
        FluentActions.Invoking(() => FlowSimulation.Start(FlowDocs.Process("flow.bare"), Setup("""{"ref":"a","ref":"b"}"""))).Should().Throw<ArgumentException>().WithMessage("*JSON object*");
    }

    [Fact]
    public void A_proposed_scenario_that_repeats_a_member_name_cannot_be_written_and_is_not_an_object_when_checked()
    {
        var outline = ProcessOutline.From(FlowDocs.Process("flow.batch"));
        var review = new FlowReview
        {
            ProcessId = outline.Id,
            ProcessVersion = outline.Version,
            ProcessContentHash = outline.ContentHash,
            Reviewer = "r",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Language = WorkbenchLocale.Es,
            Items = [],
            ProposedScenarios = [new FlowProposedScenario("dup", Duplicate)],
        };
        FluentActions.Invoking(() => FlowReviewJson.Serialize(review)).Should().Throw<JsonException>().WithMessage("*repeats no member name*");
        var report = FlowReviewCheck.Against(review, FlowDocs.Process("flow.batch"));
        report.Scenarios.Single().IsValid.Should().BeFalse();
        report.Scenarios.Single().Errors.Single().Code.Should().Be(FlowReviewCheck.ErrorScenarioNotObject);
        report.Scenarios.Single().Errors.Single().MessageEs.Should().Be("El escenario propuesto no es un objeto JSON.");
        report.Scenarios.Single().Errors.Single().MessageEn.Should().Be("The proposed scenario is not a JSON object.");
        report.Findings.Should().ContainSingle().Which.Message.Should().Contain(FlowReviewCheck.ErrorScenarioNotObject).And.Contain("El escenario propuesto");
    }

    [Fact]
    public void A_review_document_that_repeats_a_member_name_is_json_invalid()
    {
        var parsed = FlowReviewJson.Parse("""{"$spec":"brain.process-review@1","reviewer":"a","reviewer":"b"}""");
        parsed.Errors.Should().ContainSingle().Which.Code.Should().Be(FlowReviewJson.ErrorJsonInvalid);
    }

    [Fact]
    public async Task An_answer_source_output_that_repeats_a_member_name_is_logged_and_the_effect_stays_outstanding()
    {
        var source = new ScriptedAnswerSource().ForPort("schedule.cases_for_day", FlowAnswer.Output(Duplicate));
        var session = FlowSimulation.Start(FlowDocs.Process("practice.daily_refresh"), Setup("""{"run_for":"2027-01-05"}""", source));
        var entries = await session.RunAnswersAsync();
        var entry = entries.Should().ContainSingle().Subject;
        entry.Kind.Should().Be(FlowSimulationEntryKind.AnswerSourceFailed);
        entry.Code.Should().Be(FlowSimulation.ErrorJsonInvalid);
        entry.Source.Should().Be(FlowSimulation.ScriptedSource);
        session.Outstanding.OfType<OutstandingEffect>().Should().ContainSingle();
    }

    // ---------------------------------------------------------------- A2: whole seconds

    [Fact]
    public void A_start_with_a_fraction_of_a_second_is_cut_to_whole_seconds_and_advancing_to_an_instant_works()
    {
        var process = FlowDocs.Process("fixture.reminder");
        var session = FlowSimulation.Start(process, new FlowSimulationSetup
        {
            At = DateTimeOffset.Parse("2027-01-04T23:00:00.5Z", System.Globalization.CultureInfo.InvariantCulture),
            Today = Today,
            InputJson = """{"case_ref":"c-1","visit_at":"2027-01-05T20:00:00Z"}""",
            Zones = NewYork,
        });
        session.Now.Should().Be(DateTimeOffset.Parse("2027-01-04T23:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var opens = session.Outstanding.OfType<OutstandingWindow>().Single().OpensAt;
        session.AdvanceTo(opens).Should().Contain(entry => entry.Kind == FlowSimulationEntryKind.WindowOpened);
        session.Now.Should().Be(opens);
        session.Log[0].At.Should().Be(DateTimeOffset.Parse("2027-01-04T23:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Advancing_to_a_fractional_instant_rounds_up_and_an_instant_that_is_not_later_is_refused()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("flow.bare"), Setup("""{"ref":"r"}"""));
        session.AdvanceTo(At.AddMilliseconds(1500)).Should().Contain(entry => entry.Kind == FlowSimulationEntryKind.ClockAdvanced);
        session.Now.Should().Be(At.AddSeconds(2));
        session.AdvanceTo(session.Now).Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorDurationInvalid);
        session.AdvanceTo(session.Now.AddSeconds(-5)).Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorDurationInvalid);
        session.Now.Should().Be(At.AddSeconds(2));
    }

    [Fact]
    public void A_replayed_session_with_a_fractional_start_can_still_advance_to_an_instant()
    {
        var process = FlowDocs.Edited("flow.bare", document =>
        {
            document["body"]!["scenarios"]![0]!["start"]!["at"] = "2027-01-04T14:00:00.25Z";
            document["body"]!["scenarios"]![0]!["events"] = new JsonArray();
        });
        var session = FlowSimulation.Replay(process, "the bare flow ends fine");
        var start = DateTimeOffset.Parse("2027-01-04T14:00:00.25Z", System.Globalization.CultureInfo.InvariantCulture);
        session.Now.Should().Be(start);
        session.AdvanceTo(start.AddMilliseconds(1100)).Should().Contain(entry => entry.Kind == FlowSimulationEntryKind.TimerFired);
        session.Now.Should().Be(start.AddSeconds(2));
    }

    // ---------------------------------------------------------------- A4: the clock cannot overflow

    [Fact]
    public void The_clock_cannot_be_moved_past_the_largest_instant()
    {
        var late = DateTimeOffset.Parse("9999-12-30T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var session = FlowSimulation.Start(FlowDocs.Process("flow.bare"), new FlowSimulationSetup { At = late, Today = new DateOnly(9999, 12, 30), InputJson = """{"ref":"r"}""" });
        session.AdvanceBy(TimeSpan.FromDays(2)).Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorDurationInvalid);
        session.AdvanceTo(DateTimeOffset.MaxValue).Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorDurationInvalid);
        session.Now.Should().Be(late);
        session.AdvanceBy(TimeSpan.FromDays(1)).Should().Contain(entry => entry.Kind == FlowSimulationEntryKind.ClockAdvanced);
        session.Now.Should().Be(late.AddDays(1));
    }

    // ---------------------------------------------------------------- A5: a start the interpreter rejected

    [Fact]
    public void A_start_the_interpreter_rejects_leaves_a_session_that_refuses_every_command()
    {
        var process = FlowDocs.Process("flow.bare");
        var session = FlowSimulation.Start(process, Setup("""{"nope":1}"""));
        session.Started.Should().BeFalse();
        session.StartCode.Should().Be("process.input.unknown");
        session.Log.Should().ContainSingle().Which.Disposition.Should().Be(AdvanceDisposition.Rejected);
        session.Outstanding.Should().BeEmpty();
        var refused = new List<IReadOnlyList<FlowSimulationEntry>>
        {
            session.AdvanceBy(TimeSpan.FromSeconds(5)),
            session.AdvanceTo(At.AddSeconds(5)),
            session.CompleteEffect("e", "{}"),
            session.FailEffect("e", "x", true),
            session.CompleteTask("t", "o"),
            session.SendEvent("e.x"),
            session.CompleteChild("c", "done"),
            session.FailChild("c", "x"),
            session.Cancel(),
            session.Resume(),
        };
        refused.Should().OnlyContain(entries => entries.Count == 1 && entries[0].Code == FlowSimulation.ErrorNotStarted && entries[0].Disposition == AdvanceDisposition.Rejected);
        session.Log.Should().HaveCount(11);
        FluentActions.Invoking(() => session.ToScenarioJson("x")).Should().Throw<InvalidOperationException>();
        session.Now.Should().Be(At);
    }

    [Fact]
    public void A_case_that_started_says_so()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("flow.bare"), Setup("""{"ref":"r"}"""));
        session.Started.Should().BeTrue();
        session.StartCode.Should().BeNull();
    }

    // ---------------------------------------------------------------- A6: a scenario that can always be written

    [Fact]
    public void A_zone_an_embedded_scenario_would_not_accept_cannot_start_a_session()
    {
        foreach (var bad in new[] { "not a zone", "Nowhere", "/x", "Area/", "A B/C" })
        {
            FluentActions.Invoking(() => FlowSimulation.Start(FlowDocs.Process("flow.bare"), new FlowSimulationSetup
            {
                At = At,
                Today = Today,
                InputJson = """{"ref":"r"}""",
                Zones = new Dictionary<string, TimeSpan> { [bad] = TimeSpan.Zero },
            })).Should().Throw<ArgumentException>(because: bad);
        }

        FlowSimulation.Start(FlowDocs.Process("flow.bare"), Setup("""{"ref":"r"}""", zones: true)).Started.Should().BeTrue();
    }

    [Fact]
    public void A_name_the_scenario_format_would_not_accept_cannot_be_written()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("flow.bare"), Setup("""{"ref":"r"}"""));
        FluentActions.Invoking(() => session.ToScenarioJson(string.Empty)).Should().Throw<ArgumentException>();
        JsonNode.Parse(session.ToScenarioJson("a name"))!["name"]!.GetValue<string>().Should().Be("a name");
    }

    // ---------------------------------------------------------------- A10: what the session hands out

    [Fact]
    public void The_history_the_log_and_what_is_outstanding_cannot_be_changed_through_the_property()
    {
        var session = Batch();
        session.CompleteChild(session.Outstanding.OfType<OutstandingChild>().Single().Id, "done");
        session.History.GetType().Should().NotBe<List<ProcessHistoryEntry>>();
        session.Log.GetType().Should().NotBe<List<FlowSimulationEntry>>();
        session.Outstanding.GetType().Should().NotBe<List<FlowOutstanding>>();
        ((System.Collections.Generic.ICollection<ProcessHistoryEntry>)session.History).IsReadOnly.Should().BeTrue();
        ((System.Collections.Generic.ICollection<FlowSimulationEntry>)session.Log).IsReadOnly.Should().BeTrue();
        ((System.Collections.Generic.ICollection<FlowOutstanding>)session.Outstanding).IsReadOnly.Should().BeTrue();
        FluentActions.Invoking(() => ((IList<FlowSimulationEntry>)session.Log).Clear()).Should().Throw<NotSupportedException>();
        var count = session.Log.Count;
        session.Cancel();
        session.Log.Count.Should().BeGreaterThan(count, "the view follows the session");
    }

    [Fact]
    public void A_failure_with_a_retry_hint_that_cannot_be_used_is_refused_and_the_effect_stays_outstanding()
    {
        var session = AtAsk();
        var effect = session.Outstanding.OfType<OutstandingEffect>().Single();
        foreach (var bad in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(-3), TimeSpan.FromDays(3661) })
        {
            session.FailEffect(effect.Id, ProcessErrorKind.RateLimited, "x.busy", bad).Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorDurationInvalid);
        }

        session.Outstanding.OfType<OutstandingEffect>().Should().ContainSingle();
        session.FailEffect(effect.Id, ProcessErrorKind.RateLimited, "x.busy", TimeSpan.FromSeconds(30)).Should().NotBeEmpty();
        session.ToScenarioJson("with a hint").Should().Contain("\"retryAfter\": \"PT30S\"");
    }

    // ---------------------------------------------------------------- C: the simulator

    [Fact]
    public void The_clock_may_move_exactly_3660_days_and_not_a_second_more()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("flow.bare"), Setup("""{"ref":"r"}"""));
        session.AdvanceBy(TimeSpan.FromDays(3660).Add(TimeSpan.FromSeconds(1))).Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorDurationInvalid);
        session.Now.Should().Be(At);
        session.AdvanceBy(TimeSpan.FromDays(3660)).Should().Contain(entry => entry.Kind == FlowSimulationEntryKind.ClockAdvanced);
        session.Now.Should().Be(At.AddDays(3660));
    }

    [Fact]
    public void Today_moves_with_the_clock_by_whole_days()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("flow.bare"), Setup("""{"ref":"r"}"""));
        session.Clock().Today.Should().Be(Today);
        session.AdvanceBy(TimeSpan.FromHours(9));
        session.Clock().Today.Should().Be(Today, "the clock has not reached the next day");
        session.AdvanceBy(TimeSpan.FromHours(40));
        session.Clock().Today.Should().Be(Today.AddDays(2));
        session.Clock().Now.Should().Be(session.Now);
    }

    [Fact]
    public async Task The_answer_limit_counts_answers_when_two_effects_can_be_answered_in_a_row()
    {
        static ScriptedAnswerSource Script() => new ScriptedAnswerSource()
            .ForStep("check", FlowAnswer.Output("""{"missing_count":2}"""))
            .ForStep("send", FlowAnswer.Output("{}"));

        var one = Outreach(Script());
        var entries = await one.RunAnswersAsync(maxAnswers: 1);
        entries.Count(entry => entry.Kind == FlowSimulationEntryKind.EffectAnswered).Should().Be(1);
        one.Outstanding.OfType<OutstandingEffect>().Should().ContainSingle().Which.Step.Should().Be("send");

        var two = Outreach(Script());
        (await two.RunAnswersAsync(maxAnswers: 2)).Count(entry => entry.Kind == FlowSimulationEntryKind.EffectAnswered).Should().Be(2);
        two.Outstanding.OfType<OutstandingEffect>().Should().BeEmpty();
        two.Outstanding.OfType<OutstandingWait>().Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_source_that_throws_a_cancellation_nobody_asked_for_is_logged_as_a_failed_source()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("practice.daily_refresh"), Setup("""{"run_for":"2027-01-05"}""", new RogueCancellationSource()));
        var entries = await session.RunAnswersAsync();
        var entry = entries.Should().ContainSingle().Subject;
        entry.Kind.Should().Be(FlowSimulationEntryKind.AnswerSourceFailed);
        entry.Code.Should().Be(FlowSimulation.ErrorAnswerSourceFailed);
        entry.Source.Should().Be("Rogue");
        session.Outstanding.OfType<OutstandingEffect>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_failed_answer_names_the_source_that_gave_it()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("practice.daily_refresh"), Setup("""{"run_for":"2027-01-05"}""", new NamedSource(FlowAnswer.Failure("schedule.gone", isPermanent: true))));
        await session.RunAnswersAsync();
        session.Log.Single(entry => entry.Kind == FlowSimulationEntryKind.EffectFailed).Source.Should().Be("Named");
    }

    [Fact]
    public void A_scenario_command_that_addresses_the_wrong_step_is_reported_with_where_the_case_is()
    {
        var scenario = JsonNode.Parse(
            """
            {"name":"wrong step","start":{"at":"2027-01-04T14:00:00Z","today":"2027-01-04","input":{"batch_ref":"b-1","items":[],"visit_at":"2027-01-10T15:00:00Z"}},
             "events":[{"complete":"ask","output":{"pick":"p","size":3}}],
             "expect":{"status":"Completed"}}
            """)!.AsObject();
        var run = FlowSimulation.Play(FlowDocs.Process("flow.batch"), scenario);
        run.Failure.Should().StartWith("the event addresses Effect of step 'ask' but the instance is Waiting on '");
    }

    [Fact]
    public void A_child_is_opened_from_the_highest_version_given()
    {
        var newer = FlowDocs.Edited("flow.child", document =>
        {
            document["version"] = 2;
            document["body"]!["steps"]!["finish"]!["result"] = "done_again";
        });
        foreach (var children in new[] { new[] { FlowDocs.Process("flow.child"), newer }, new[] { newer, FlowDocs.Process("flow.child") } })
        {
            var session = FlowSimulation.Start(FlowDocs.Process("flow.batch"), Setup(BatchInput, null, children));
            var child = session.OpenChild(session.Outstanding.OfType<OutstandingChild>().Single().Id)!;
            child.Process.Should().BeSameAs(newer);
            child.State.Result.Should().Be("done_again");
        }
    }

    [Fact]
    public void Nothing_is_outstanding_after_a_cancel_or_while_the_case_is_suspended()
    {
        var cancelled = Batch();
        cancelled.Outstanding.Should().NotBeEmpty();
        cancelled.Cancel();
        cancelled.Outstanding.Should().BeEmpty();

        var broken = FlowSimulation.Start(FlowDocs.FromJson(NoStepProcess), Setup());
        broken.FailEffect(broken.Outstanding.OfType<OutstandingEffect>().Single().Id, "x.failed", isPermanent: true);
        broken.Status.Should().Be(ProcessStatus.Suspended);
        broken.Outstanding.Should().BeEmpty();
    }

    [Fact]
    public void Outside_the_window_the_case_waits_for_the_window_and_for_the_interrupt()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("fixture.reminder"), new FlowSimulationSetup
        {
            At = DateTimeOffset.Parse("2027-01-04T23:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Today = Today,
            InputJson = """{"case_ref":"c-1","visit_at":"2027-01-05T20:00:00Z"}""",
            Zones = NewYork,
        });
        session.Outstanding.OfType<OutstandingWindow>().Should().ContainSingle();
        var wait = session.Outstanding.OfType<OutstandingWait>().Should().ContainSingle().Subject;
        wait.Event.Should().Be("case.reply_received");
        wait.Correlation.Should().Be("c-1");
    }

    public static TheoryData<string, string> Scenarios()
    {
        var data = new TheoryData<string, string>();
        foreach (var (name, process) in FlowDocs.All)
        {
            foreach (var scenario in ProcessOutline.From(process).Scenarios)
            {
                data.Add(name, scenario);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void A_replay_meets_the_expect_of_its_own_scenario(string document, string scenario)
    {
        var process = FlowDocs.Process(document);
        var run = FlowSimulation.Play(process, JsonNode.Parse(ProcessOutline.From(process).ScenarioJson(scenario)!)!.AsObject());
        run.Failure.Should().BeNull();
        run.Session.Name.Should().Be(scenario);
    }

    [Fact]
    public void A_proposed_scenario_that_does_not_meet_its_own_expect_is_reported_as_failing()
    {
        var process = FlowDocs.Process("flow.batch");
        var outline = ProcessOutline.From(process);
        string Scenario(string expect) =>
            """
            {"name":"NAME","start":{"at":"2027-01-04T14:00:00Z","today":"2027-01-04","input":{"batch_ref":"b-1","items":[],"visit_at":"2027-01-10T15:00:00Z"}},
             "events":[{"advance":"P7D"},{"complete":"ask","output":{"pick":"p","size":3}}],
             "expect":EXPECT}
            """.Replace("NAME", "n", StringComparison.Ordinal).Replace("EXPECT", expect, StringComparison.Ordinal);
        var good = Scenario("""{"status":"Completed","result":"broken","trace":["each","nap","ask","verify","broken"],"variables":{"count":3}}""");
        var wrongVariable = Scenario("""{"status":"Completed","result":"broken","variables":{"count":99}}""");
        var shortTrace = Scenario("""{"status":"Completed","result":"broken","trace":["each","nap"]}""");
        var wrongResult = Scenario("""{"status":"Completed","result":"ok"}""");
        var wrongStatus = Scenario("""{"status":"Waiting"}""");
        var review = new FlowReview
        {
            ProcessId = outline.Id,
            ProcessVersion = outline.Version,
            ProcessContentHash = outline.ContentHash,
            Reviewer = "r",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Language = WorkbenchLocale.En,
            Items = [],
            ProposedScenarios = [.. new[] { good, wrongVariable, shortTrace, wrongResult, wrongStatus }.Select(text => new FlowProposedScenario("n", text))],
        };
        var results = FlowReviewCheck.Against(review, process).Scenarios;
        results.Select(result => result.Passed).Should().Equal(true, false, false, false, false);
        results[1].Failure.Should().Be("expected variable 'count' to be 99 but it was 3");
        results[2].Failure.Should().StartWith("expected trace [each, nap] but it was [each, nap, ask, verify, broken]");
        results[3].Failure.Should().Be("expected result 'ok' but it was 'broken'");
        results[4].Failure.Should().Be("expected status Waiting but it was Completed");
    }

    [Fact]
    public async Task A_malformed_ai_answer_gets_two_attempts_and_then_leaves_through_on_invalid()
    {
        var attempts = new List<int>();
        var source = new ScriptedAnswerSource().ForPort("assist.suggest", FlowAnswer.Output("""{"code":"bad"}"""), FlowAnswer.Output("""{"code":"still bad"}"""));
        var spy = new AttemptSpy(source, attempts);
        var session = FlowSimulation.Start(FlowDocs.Process("postvisit.coding_review"), Setup("""{"case_ref":"c-1","assist_enabled":true}""", spy));
        session.CompleteEffect(session.Outstanding.OfType<OutstandingEffect>().Single().Id, """{"items":4,"flagged":1}""");
        var entries = await session.RunAnswersAsync();
        attempts.Should().Equal(1, 2);
        entries.Count(entry => entry.Kind == FlowSimulationEntryKind.EffectAnswered && entry.Disposition == AdvanceDisposition.Applied).Should().Be(2);
        session.CurrentStep.Should().Be("physician_review");
        session.History.Count(entry => entry.Kind == ProcessHistoryKind.EffectFailed && entry.Code == "process.ai.output_invalid").Should().Be(2);
    }

    [Fact]
    public async Task Cancelling_from_inside_the_source_on_its_second_call_keeps_the_first_answer()
    {
        using var cancel = new CancellationTokenSource();
        var session = Outreach(new CancelOnSecondCall(cancel));
        await FluentActions.Awaiting(() => session.RunAnswersAsync(50, cancel.Token)).Should().ThrowAsync<OperationCanceledException>();
        session.Log.Count(entry => entry.Kind == FlowSimulationEntryKind.EffectAnswered).Should().Be(1);
        session.Variables.Should().ContainKey("missing");
        session.Outstanding.OfType<OutstandingEffect>().Should().ContainSingle().Which.Step.Should().Be("send");
    }

    private sealed class RogueCancellationSource : IFlowAnswerSource
    {
        public string Name => "Rogue";

        public ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken) => throw new OperationCanceledException("not the caller's token");
    }

    private sealed class NamedSource(FlowAnswer answer) : IFlowAnswerSource
    {
        public string Name => "Named";

        public ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken) => ValueTask.FromResult<FlowAnswer?>(answer);
    }

    private sealed class AttemptSpy(ScriptedAnswerSource inner, List<int> attempts) : IFlowAnswerSource
    {
        public string Name => "Spy";

        public ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken)
        {
            attempts.Add(request.Attempt);
            return inner.AnswerAsync(request, cancellationToken);
        }
    }

    private sealed class CancelOnSecondCall(CancellationTokenSource cancel) : IFlowAnswerSource
    {
        private int calls;

        public string Name => "Cancelling";

        public async ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken)
        {
            calls++;
            if (calls == 2)
            {
                await cancel.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return FlowAnswer.Output("""{"missing_count":2}""");
        }
    }
}

using System.Text.Json.Nodes;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;
using ProcessTests = Synapbiz.Brain.Process.Tests;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class FlowSimulationTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2027-01-04T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly DateOnly Today = new(2027, 1, 4);
    private const string BatchInput = """{"batch_ref":"b-1","items":[{"id":"a"},{"id":"b"}],"visit_at":"2027-01-10T15:00:00Z"}""";

    private static readonly Dictionary<string, TimeSpan> NewYork = new() { ["America/New_York"] = TimeSpan.FromHours(-5) };

    private static FlowSimulationSetup Setup(string? input = null, IFlowAnswerSource? source = null, IReadOnlyCollection<BrainProcess>? children = null, bool zones = false) =>
        new() { At = At, Today = Today, InputJson = input, AnswerSource = source, ChildProcesses = children, Zones = zones ? NewYork : new Dictionary<string, TimeSpan>() };

    private static FlowSimulation Batch(IFlowAnswerSource? source = null) =>
        FlowSimulation.Start(FlowDocs.Process("flow.batch"), Setup(BatchInput, source, [FlowDocs.Process("flow.child")]));

    private static void FinishChildren(FlowSimulation session)
    {
        while (session.Outstanding.OfType<OutstandingChild>().FirstOrDefault() is { } child)
        {
            session.CompleteChild(child.Id, "done");
        }
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

    [Fact]
    public void Every_document_passes_its_own_scenarios()
    {
        foreach (var (name, process) in FlowDocs.All)
        {
            var result = process.RunScenarios();
            result.IsOk.Should().BeTrue(name + ": " + string.Join("; ", result.Results.Where(item => !item.Passed).Select(item => item.Name + " " + item.Failure)));
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Replay_ends_where_the_independent_player_ends(string document, string scenario)
    {
        var process = FlowDocs.Process(document);
        var session = FlowSimulation.Replay(process, scenario);
        var independent = ProcessTests.ScenarioTracer.Play(process, JsonNode.Parse(ProcessOutline.From(process).ScenarioJson(scenario)!)!.AsObject());
        session.History.Should().Equal(independent.History);
        session.Status.Should().Be(independent.Final.Status);
        session.Variables.OrderBy(pair => pair.Key, StringComparer.Ordinal).Should().Equal(independent.Final.Variables.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        session.Name.Should().Be(scenario);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void A_replay_written_back_is_a_scenario_that_passes(string document, string scenario)
    {
        var session = FlowSimulation.Replay(FlowDocs.Process(document), scenario);
        var json = session.ToScenarioJson("round trip");
        var extended = FlowDocs.WithScenario(document, json);
        var result = extended.RunScenarios();
        result.IsOk.Should().BeTrue(string.Join("; ", result.Results.Where(item => !item.Passed).Select(item => item.Name + " " + item.Failure)));
        ProcessOutline.From(extended).Scenarios.Should().Contain("round trip");
        JsonNode.Parse(json)!["name"]!.GetValue<string>().Should().Be("round trip");
    }

    [Fact]
    public void Replay_of_an_unknown_scenario_is_rejected()
    {
        FluentActions.Invoking(() => FlowSimulation.Replay(FlowDocs.Process("flow.batch"), "no such scenario")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_session_is_deterministic()
    {
        string Run()
        {
            var session = Batch();
            FinishChildren(session);
            session.AdvanceBy(TimeSpan.FromDays(7));
            session.CompleteEffect(session.Outstanding.OfType<OutstandingEffect>().Single().Id, """{"pick":"p","size":12}""");
            return session.ToScenarioJson("x") + string.Join("|", session.Log.Select(entry => entry.Kind + ":" + entry.Step + ":" + entry.At.ToString("O")));
        }

        Run().Should().Be(Run());
    }

    [Fact]
    public void Starting_shows_where_the_case_waits()
    {
        var session = Batch();
        session.Status.Should().Be(ProcessStatus.Waiting);
        session.CurrentStep.Should().Be("child");
        session.Now.Should().Be(At);
        session.Process.Should().BeSameAs(FlowDocs.Process("flow.batch"));
        session.Log[0].Kind.Should().Be(FlowSimulationEntryKind.Started);
        session.Log.Select(entry => entry.Sequence).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        var child = session.Outstanding.Should().ContainSingle().Which.Should().BeOfType<OutstandingChild>().Subject;
        child.ProcessId.Should().Be("flow.child");
        child.Step.Should().Be("child");
    }

    [Fact]
    public void A_child_can_be_opened_and_its_result_sent_back()
    {
        var session = Batch();
        var key = session.Outstanding.OfType<OutstandingChild>().Single().Id;
        var child = session.OpenChild(key)!;
        child.Status.Should().Be(ProcessStatus.Completed);
        child.Name.Should().Be("child:flow.child");
        session.CompleteChild(key, child.State.Result!).Should().NotBeEmpty();
        session.OpenChild("nope").Should().BeNull();
        FlowSimulation.Start(FlowDocs.Process("flow.batch"), Setup(BatchInput)).OpenChild(key).Should().BeNull();
    }

    [Fact]
    public void Manual_commands_move_the_case_and_log_who_did_it()
    {
        var session = Batch();
        FinishChildren(session);
        session.CurrentStep.Should().Be("nap");
        var timer = session.Outstanding.Should().ContainSingle().Which.Should().BeOfType<OutstandingTimer>().Subject;
        timer.Due.Should().Be(DateTimeOffset.Parse("2027-01-10T16:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var entries = session.AdvanceTo(timer.Due);
        entries.Select(entry => entry.Kind).Should().Contain([FlowSimulationEntryKind.TimerFired, FlowSimulationEntryKind.ClockAdvanced]);
        session.Now.Should().Be(timer.Due);
        session.CurrentStep.Should().Be("ask");
        var effect = session.Outstanding.Should().ContainSingle().Which.Should().BeOfType<OutstandingEffect>().Subject;
        effect.Port.Should().Be("assist.pick");
        effect.StepKind.Should().Be(OutlineStepKind.Ai);
        effect.Attempt.Should().Be(1);
        session.Log.Where(entry => entry.Kind != FlowSimulationEntryKind.Started).Select(entry => entry.Source).Distinct().Should().Equal(FlowSimulation.ManualSource);
        session.CompleteEffect(effect.Id, """{"pick":"p","size":12}""");
        session.Variables["count"].Should().Be("12");
        session.CurrentStep.Should().Be("check");
    }

    [Fact]
    public void Commands_the_case_cannot_take_are_logged_and_change_nothing()
    {
        var session = Batch();
        var before = session.History.Count;
        var entries = session.CompleteEffect("e-nope", "{}");
        entries.Should().ContainSingle().Which.Disposition.Should().NotBe(AdvanceDisposition.Applied);
        session.History.Count.Should().Be(before);
        session.CompleteTask("t-nope", "x").Should().ContainSingle();
        session.SendEvent("no.such.event").Should().ContainSingle();
        session.FailEffect("e-nope", "x.code", isPermanent: true).Should().ContainSingle();
        session.CompleteChild("nope", "done").Should().ContainSingle();
        session.FailChild("nope", "x.code").Should().ContainSingle();
        session.Resume().Should().ContainSingle();
        session.History.Count.Should().Be(before);
    }

    [Fact]
    public void Time_commands_reject_what_the_scenario_format_cannot_hold()
    {
        var session = Batch();
        foreach (var bad in new[] { TimeSpan.Zero, TimeSpan.FromHours(-1), TimeSpan.FromMilliseconds(500), TimeSpan.FromDays(3661) })
        {
            session.AdvanceBy(bad).Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorDurationInvalid);
        }

        session.Now.Should().Be(At);
        session.AdvanceTo(At).Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorDurationInvalid);
        session.SendEvent("case.reply_received", null, "[1]").Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorJsonInvalid);
        session.CompleteTask("t", "o", "not json").Should().ContainSingle().Which.Code.Should().Be(FlowSimulation.ErrorJsonInvalid);
        session.ToScenarioJson("fine").Should().Contain("fine");
    }

    [Fact]
    public void A_scenario_cannot_hold_a_payload_that_is_not_an_object()
    {
        var session = Batch();
        FinishChildren(session);
        session.AdvanceBy(TimeSpan.FromDays(7));
        session.CompleteEffect(session.Outstanding.OfType<OutstandingEffect>().Single().Id, "[1]");
        FluentActions.Invoking(() => session.ToScenarioJson("x")).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Time_moves_timers_in_due_order()
    {
        var process = FlowDocs.Process("flow.bare");
        var session = FlowSimulation.Start(process, Setup("""{"ref":"r-1"}"""));
        session.Outstanding.Should().ContainSingle().Which.Should().BeOfType<OutstandingTimer>();
        var entries = session.AdvanceBy(TimeSpan.FromSeconds(31));
        entries.Where(entry => entry.Kind == FlowSimulationEntryKind.TimerFired).Should().HaveCount(2);
        session.Status.Should().Be(ProcessStatus.Completed);
        session.State.Result.Should().Be("fine");
    }

    [Fact]
    public void Cancel_and_resume_follow_the_interpreter()
    {
        var session = Batch();
        session.Cancel().Should().NotBeEmpty();
        session.Status.Should().Be(ProcessStatus.Cancelled);
        session.Log[^1].Kind.Should().Be(FlowSimulationEntryKind.Cancelled);
        session.Resume().Should().ContainSingle().Which.Disposition.Should().NotBe(AdvanceDisposition.Applied);
    }

    [Fact]
    public void Human_tasks_and_events_are_outstanding_and_answered()
    {
        var process = FlowDocs.Process("previsit.outreach");
        var session = FlowSimulation.Start(process, Setup("""{"case_ref":"c-1","practice_zone":"America/New_York"}""", zones: true));
        var effect = session.Outstanding.OfType<OutstandingEffect>().Single();
        effect.Step.Should().Be("check");
        session.CompleteEffect(effect.Id, """{"missing_count":2}""");
        var send = session.Outstanding.OfType<OutstandingEffect>().Single();
        send.Step.Should().Be("send");
        session.Outstanding.OfType<OutstandingWindow>().Should().BeEmpty();
        session.CompleteEffect(send.Id, "{}");
        var wait = session.Outstanding.OfType<OutstandingWait>().Single();
        wait.Event.Should().Be("case.reply_received");
        wait.Correlation.Should().Be("c-1");
        session.SendEvent(wait.Event, "c-1", "{}").Should().NotBeEmpty();
        session.FailEffect(session.Outstanding.OfType<OutstandingEffect>().Single().Id, "x.permanent", isPermanent: true);
        var task = session.Outstanding.OfType<OutstandingTask>().Single();
        task.Queue.Should().Be("front_desk");
        task.Outcomes.Should().Equal("resolved", "unreachable");
        JsonNode.Parse(task.ShowJson).Should().NotBeNull();
        session.CompleteTask(task.Id, "unreachable");
        session.Status.Should().Be(ProcessStatus.Completed);
        session.Log.Select(entry => entry.Kind).Should().Contain([FlowSimulationEntryKind.EffectAnswered, FlowSimulationEntryKind.EffectFailed, FlowSimulationEntryKind.EventSent, FlowSimulationEntryKind.TaskCompleted]);
    }

    [Fact]
    public void A_contact_inside_a_window_is_answered_with_the_next_opening()
    {
        var process = FlowDocs.Process("fixture.reminder");
        var session = FlowSimulation.Start(process, new FlowSimulationSetup
        {
            At = DateTimeOffset.Parse("2027-01-04T23:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Today = Today,
            InputJson = """{"case_ref":"c-1","visit_at":"2027-01-05T20:00:00Z"}""",
            Zones = new Dictionary<string, TimeSpan> { ["America/New_York"] = TimeSpan.FromHours(-5) },
        });
        session.Outstanding.OfType<OutstandingWindow>().Should().ContainSingle();
        var opens = session.Outstanding.OfType<OutstandingWindow>().Single().OpensAt;
        opens.Should().Be(DateTimeOffset.Parse("2027-01-05T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        session.AdvanceTo(opens);
        session.Log.Select(entry => entry.Kind).Should().Contain(FlowSimulationEntryKind.WindowOpened);
        session.Outstanding.OfType<OutstandingEffect>().Should().ContainSingle();
    }

    [Fact]
    public void A_window_in_an_unknown_zone_fails_the_window()
    {
        var process = FlowDocs.Process("fixture.reminder");
        var session = FlowSimulation.Start(process, Setup("""{"case_ref":"c-1","visit_at":"2027-01-09T15:00:00Z"}"""));
        session.Log.Select(entry => entry.Kind).Should().Contain(FlowSimulationEntryKind.WindowFailed);
    }

    [Fact]
    public void A_start_with_input_that_is_not_an_object_or_a_bad_zone_is_rejected()
    {
        var process = FlowDocs.Process("flow.bare");
        FluentActions.Invoking(() => FlowSimulation.Start(process, Setup("[1]"))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => FlowSimulation.Start(process, Setup("not json"))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => FlowSimulation.Start(process, new FlowSimulationSetup { At = At, Today = Today, InputJson = """{"ref":"r"}""", Zones = new Dictionary<string, TimeSpan> { ["z"] = TimeSpan.FromHours(30) } }))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task A_scripted_source_drives_a_flow_end_to_end()
    {
        var source = new ScriptedAnswerSource().ForPort("schedule.cases_for_day", FlowAnswer.Output("""{"cases":[{"case_ref":"c-1"}]}"""));
        var session = FlowSimulation.Start(FlowDocs.Process("practice.daily_refresh"), Setup("""{"run_for":"2027-01-05"}""", source));
        var entries = await session.RunAnswersAsync();
        entries.Should().NotBeEmpty();
        session.Status.Should().Be(ProcessStatus.Completed);
        session.State.Result.Should().Be("refreshed");
        session.Log.Where(entry => entry.Kind == FlowSimulationEntryKind.EffectAnswered).Should().OnlyContain(entry => entry.Source == FlowSimulation.ScriptedSource);
        var json = session.ToScenarioJson("scripted run");
        FlowDocs.WithScenario("practice.daily_refresh", json).RunScenarios().IsOk.Should().BeTrue();
    }

    [Fact]
    public async Task A_failing_answer_takes_the_error_route()
    {
        var source = new ScriptedAnswerSource().ForPort("schedule.cases_for_day", FlowAnswer.Failure("schedule.gone", isPermanent: true));
        var session = FlowSimulation.Start(FlowDocs.Process("practice.daily_refresh"), Setup("""{"run_for":"2027-01-05"}""", source));
        await session.RunAnswersAsync();
        session.CurrentStep.Should().Be("refresh_attention");
        session.Log.Should().Contain(entry => entry.Kind == FlowSimulationEntryKind.EffectFailed && entry.Disposition == AdvanceDisposition.Applied);
    }

    [Fact]
    public async Task A_transient_failure_waits_for_the_clock_and_a_manual_advance_retries()
    {
        var source = new ScriptedAnswerSource().ForPort(
            "schedule.cases_for_day",
            FlowAnswer.Failure("schedule.busy", isPermanent: false),
            FlowAnswer.Output("""{"cases":[]}"""));
        var session = FlowSimulation.Start(FlowDocs.Process("practice.daily_refresh"), Setup("""{"run_for":"2027-01-05"}""", source));
        await session.RunAnswersAsync();
        session.Status.Should().Be(ProcessStatus.Waiting);
        session.AdvanceBy(TimeSpan.FromMinutes(1));
        await session.RunAnswersAsync();
        session.State.Result.Should().Be("refreshed");
        await session.RunAnswersAsync();
    }

    [Fact]
    public async Task A_malformed_ai_answer_is_tried_again_and_then_leaves_through_on_invalid()
    {
        var source = new ScriptedAnswerSource().ForPort("assist.suggest", FlowAnswer.Output("""{"code":"bad"}"""), FlowAnswer.Output("""{"code":"still bad"}"""));
        var process = FlowDocs.Process("postvisit.coding_review");
        var session = FlowSimulation.Start(process, Setup("""{"case_ref":"c-1","assist_enabled":true}""", source));
        session.CompleteEffect(session.Outstanding.OfType<OutstandingEffect>().Single().Id, """{"items":4,"flagged":1}""");
        var request = session.Outstanding.OfType<OutstandingEffect>().Single();
        request.Step.Should().Be("suggest");
        request.Attempt.Should().Be(1);
        await session.RunAnswersAsync();
        session.CurrentStep.Should().Be("physician_review");
        session.History.Count(entry => entry.Kind == ProcessHistoryKind.StepEntered && entry.Step == "suggest").Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task One_attempt_means_one_answer_before_on_invalid()
    {
        var source = new ScriptedAnswerSource().ForPort("assist.pick", FlowAnswer.Output("""{"pick":5}"""));
        var session = Batch(source);
        FinishChildren(session);
        session.AdvanceBy(TimeSpan.FromDays(7));
        await session.RunAnswersAsync();
        session.State.Result.Should().Be("broken");
    }

    [Fact]
    public async Task The_request_an_answer_source_sees_names_the_model_and_the_schema()
    {
        var seen = new List<FlowEffectRequest>();
        var source = new RecordingSource(seen);
        var session = Batch(source);
        FinishChildren(session);
        session.AdvanceBy(TimeSpan.FromDays(7));
        await session.RunAnswersAsync();
        var request = seen.Should().ContainSingle().Subject;
        request.ProcessId.Should().Be("flow.batch");
        request.StepKind.Should().Be(OutlineStepKind.Ai);
        request.Model.Should().Be("model-a");
        request.PromptId.Should().Be("pick");
        request.PromptVersion.Should().Be(2);
        request.OutputSchemaJson.Should().Contain("\"pick\"");
        request.InputJson.Should().Contain("b-1");
        session.Outstanding.OfType<OutstandingEffect>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_source_that_throws_is_logged_and_the_session_stays_usable()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("practice.daily_refresh"), Setup("""{"run_for":"2027-01-05"}""", new ThrowingSource()));
        var entries = await session.RunAnswersAsync();
        entries.Should().ContainSingle().Which.Kind.Should().Be(FlowSimulationEntryKind.AnswerSourceFailed);
        entries[0].Code.Should().Be(FlowSimulation.ErrorAnswerSourceFailed);
        session.Outstanding.OfType<OutstandingEffect>().Should().ContainSingle();
        session.CompleteEffect(session.Outstanding.OfType<OutstandingEffect>().Single().Id, """{"cases":[]}""");
        session.Status.Should().Be(ProcessStatus.Completed);
    }

    [Fact]
    public async Task The_answer_limit_stops_the_run()
    {
        var source = new ScriptedAnswerSource().ForPort("schedule.cases_for_day", FlowAnswer.Failure("schedule.busy", false), FlowAnswer.Failure("schedule.busy", false));
        var session = FlowSimulation.Start(FlowDocs.Process("practice.daily_refresh"), Setup("""{"run_for":"2027-01-05"}""", source));
        var entries = await session.RunAnswersAsync(maxAnswers: 1);
        entries.Count(entry => entry.Kind == FlowSimulationEntryKind.EffectFailed).Should().Be(1);
        await FluentActions.Awaiting(() => session.RunAnswersAsync(maxAnswers: 0)).Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Cancelling_the_run_throws_and_keeps_the_answers_applied()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var source = new ScriptedAnswerSource().ForPort("schedule.cases_for_day", FlowAnswer.Output("""{"cases":[]}"""));
        var session = FlowSimulation.Start(FlowDocs.Process("practice.daily_refresh"), Setup("""{"run_for":"2027-01-05"}""", source));
        await FluentActions.Awaiting(() => session.RunAnswersAsync(50, cancel.Token)).Should().ThrowAsync<OperationCanceledException>();
        session.Status.Should().Be(ProcessStatus.Waiting);
    }

    [Fact]
    public async Task Running_answers_without_a_source_is_a_usage_error()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("practice.daily_refresh"), Setup("""{"run_for":"2027-01-05"}"""));
        await FluentActions.Awaiting(() => session.RunAnswersAsync()).Should().ThrowAsync<InvalidOperationException>();
        session.AnswerSource.Should().BeNull();
    }

    [Fact]
    public async Task A_source_that_does_not_answer_leaves_the_effect_for_a_manual_answer()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("practice.daily_refresh"), Setup("""{"run_for":"2027-01-05"}""", new ScriptedAnswerSource()));
        var entries = await session.RunAnswersAsync();
        entries.Should().BeEmpty();
        session.Outstanding.OfType<OutstandingEffect>().Should().ContainSingle();
    }

    [Fact]
    public void Answers_for_a_step_win_over_answers_for_its_port()
    {
        var source = new ScriptedAnswerSource().ForPort("p", FlowAnswer.Output("{}")).ForStep("s", FlowAnswer.Failure("x", true));
        source.Next("s", "p")!.IsFailure.Should().BeTrue();
        source.Next("s", "p")!.IsFailure.Should().BeFalse();
        source.Next("s", "p").Should().BeNull();
        source.Name.Should().Be(FlowSimulation.ScriptedSource);
    }

    [Fact]
    public void Answers_describe_themselves()
    {
        FlowAnswer.Output("{}").IsFailure.Should().BeFalse();
        FlowAnswer.Failure("c", true).IsPermanent.Should().BeTrue();
        FlowAnswer.Failure("c", false).Kind.Should().Be(ProcessErrorKind.Transient);
        var limited = FlowAnswer.Failure(ProcessErrorKind.RateLimited, "c", TimeSpan.FromMinutes(2));
        limited.RetryAfter.Should().Be(TimeSpan.FromMinutes(2));
        limited.IsPermanent.Should().BeFalse();
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var session = Batch();
        FluentActions.Invoking(() => FlowSimulation.Start(null!, Setup())).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowSimulation.Start(FlowDocs.Process("flow.bare"), null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowSimulation.Replay(null!, "x")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowSimulation.Replay(FlowDocs.Process("flow.bare"), null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => session.CompleteEffect(null!, "{}")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => session.CompleteEffect("x", null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => session.FailEffect(null!, "c", true)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => session.CompleteTask(null!, "o")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => session.SendEvent(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => session.CompleteChild(null!, "r")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => session.FailChild(null!, "c")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => session.OpenChild(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => session.ToScenarioJson(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ScriptedAnswerSource().ForPort(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ScriptedAnswerSource().ForStep("s", null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowAnswer.Output(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowAnswer.Failure(null!, true)).Should().Throw<ArgumentNullException>();
    }

    private sealed class RecordingSource(List<FlowEffectRequest> seen) : IFlowAnswerSource
    {
        public string Name => "Recording";

        public ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken)
        {
            seen.Add(request);
            return ValueTask.FromResult<FlowAnswer?>(null);
        }
    }

    private sealed class ThrowingSource : IFlowAnswerSource
    {
        public string Name => "Throwing";

        public ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the account is down");
    }
}

using System.Globalization;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The small additions the flow panels draw on: the words of a flow, the fields of its forms, the answers of its scenarios and the members of an output schema.</summary>
public sealed class FlowPanelSupportTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2027-01-04T14:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void The_words_of_a_flow_are_the_labels_of_the_document_in_the_asked_language_with_the_usual_fallbacks()
    {
        var process = FlowDocs.Process("previsit.outreach");
        var en = FlowWords.For(process, WorkbenchLocale.En);
        var es = FlowWords.For(process, WorkbenchLocale.Es);
        en.Title().Should().Be(FlowDescriber.Describe(process, WorkbenchLocale.En).Title);
        es.Title().Should().Be(FlowDescriber.Describe(process, WorkbenchLocale.Es).Title);
        en.Step("escalate").Should().Be(FlowDescriber.Describe(process, WorkbenchLocale.En).Steps.Single(step => step.Id == "escalate").Label);
        es.Stage("previsit").Should().Be(FlowDescriber.Describe(process, WorkbenchLocale.Es).Stages.Single(stage => stage.Id == "previsit").Label);
        en.Outcome("no_such_outcome").Should().Be(WorkbenchText.Humanize("no_such_outcome"));
        en.Queue("no_such_queue").Should().Be(WorkbenchText.Humanize("no_such_queue"));
        en.Event("no.such.event").Should().Be(WorkbenchText.Humanize("no.such.event"));
        en.Window("no_such_window").Should().Be(WorkbenchText.Humanize("no_such_window"));
        en.Result("no_such_result").Should().Be(WorkbenchText.Humanize("no_such_result"));
    }

    [Fact]
    public void The_words_of_every_document_name_every_outcome_queue_result_and_event_it_declares()
    {
        foreach (var (name, process) in FlowDocs.All)
        {
            var outline = ProcessOutline.From(process);
            var words = FlowWords.For(process, WorkbenchLocale.Es);
            foreach (var step in outline.Steps)
            {
                words.Step(step.Id).Should().NotBeNullOrWhiteSpace(name);
                foreach (var outcome in step.Outcomes ?? [])
                {
                    words.Outcome(outcome).Should().NotBeNullOrWhiteSpace(name);
                }

                if (step.Queue is not null)
                {
                    words.Queue(step.Queue).Should().NotBeNullOrWhiteSpace(name);
                }

                if (step.Event is not null)
                {
                    words.Event(step.Event).Should().NotBeNullOrWhiteSpace(name);
                }

                if (step.Result is not null)
                {
                    words.Result(step.Result).Should().NotBeNullOrWhiteSpace(name);
                }
            }
        }
    }

    [Fact]
    public void The_result_of_a_child_is_worded_by_the_child_and_null_when_unknown()
    {
        var parent = FlowDocs.Process("case.lifecycle");
        var known = FlowDocs.Reference.Select(item => item.Process).ToList();
        var words = FlowWords.For(parent, WorkbenchLocale.En, known);
        words.ChildResult("previsit.outreach", "previsit_closed").Should().NotBeNull();
        words.ChildResult("no.such.child", "x").Should().BeNull();
        FlowWords.For(parent, WorkbenchLocale.En).ChildResult("previsit.outreach", "previsit_closed").Should().BeNull();
    }

    [Fact]
    public void The_words_refuse_a_null()
    {
        FluentActions.Invoking(() => FlowWords.For(null!, WorkbenchLocale.En)).Should().Throw<ArgumentNullException>();
        var words = FlowWords.For(FlowDocs.Process("previsit.outreach"), WorkbenchLocale.En);
        FluentActions.Invoking(() => words.Step(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void The_members_of_an_output_schema_are_listed_in_ordinal_order_with_their_types_and_required_marks()
    {
        var fields = FlowSchemaFields.From("""{"type":"object","required":["pick"],"properties":{"size":{"type":"integer"},"pick":{"type":"string"},"tags":{"type":["array","null"]},"free":{}}}""");
        fields.Should().Equal(
            new FlowSchemaField("free", "any", false),
            new FlowSchemaField("pick", "string", true),
            new FlowSchemaField("size", "integer", false),
            new FlowSchemaField("tags", "array", false));
        FlowSchemaFields.From(null).Should().BeEmpty();
        FlowSchemaFields.From("not json").Should().BeEmpty();
        FlowSchemaFields.From("""{"type":"string"}""").Should().BeEmpty();
        FlowSchemaFields.From("""{"properties":{"a":{"type":[1]},"b":{"type":["null"]}}}""").Select(field => field.Type).Should().Equal("any", "any");
    }

    [Fact]
    public void The_fields_of_an_ai_step_in_a_reference_document_are_the_ones_its_view_shows()
    {
        var process = FlowDocs.Process("postvisit.coding_review");
        var ai = FlowDescriber.Describe(process, WorkbenchLocale.En).Steps.Single(step => step.Ai is not null).Ai!;
        FlowSchemaFields.From(ai.OutputSchemaJson).Should().NotBeEmpty();
    }

    [Fact]
    public void A_scenario_answers_a_step_by_occurrence_exactly_as_it_is_written()
    {
        var process = FlowDocs.Process("previsit.outreach");
        var first = FlowScenarioAnswers.For(process, "check", 0);
        first.Should().NotBeEmpty();
        first.Select(item => item.Scenario).Should().OnlyContain(name => ProcessOutline.From(process).Scenarios.Contains(name));
        first.Should().Contain(item => !item.Answer.IsFailure);
        first.Should().Contain(item => item.Answer.OutputJson!.Contains("missing_count", StringComparison.Ordinal));
        FlowScenarioAnswers.For(process, "check", 1).Should().NotBeEmpty();
        FlowScenarioAnswers.For(process, "check", 50).Should().BeEmpty();
        FlowScenarioAnswers.For(process, "no_such_step", 0).Should().BeEmpty();
        FlowScenarioAnswers.For(process, "escalate", 0).Should().BeEmpty("a task for a person is not an effect");
    }

    [Fact]
    public void A_scenario_that_fails_a_step_gives_a_failure_with_its_kind_code_and_retry_hint()
    {
        var scenario = """{"name":"the pick call fails","start":{"at":"2027-01-04T14:00:00Z","today":"2027-01-04","input":{"batch_ref":"b-1","items":[],"visit_at":"2027-01-10T15:00:00Z"}},"events":[{"advance":"P7D"},{"fail":"ask","error":"RateLimited","code":"assist.busy","retryAfter":"PT30S"},{"fail":"ask","error":"Permanent","code":"assist.gone"}],"expect":{"status":"Completed","result":"broken"}}""";
        var edited = FlowDocs.WithScenario("flow.batch", scenario);
        var first = FlowScenarioAnswers.For(edited, "ask", 0).Single(item => item.Scenario == "the pick call fails").Answer;
        first.IsFailure.Should().BeTrue();
        first.Kind.Should().Be(ProcessErrorKind.RateLimited);
        first.Code.Should().Be("assist.busy");
        first.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
        var second = FlowScenarioAnswers.For(edited, "ask", 1).Single(item => item.Scenario == "the pick call fails").Answer;
        second.IsPermanent.Should().BeTrue();
        second.Code.Should().Be("assist.gone");
    }

    [Fact]
    public void The_answers_of_a_scenario_applied_one_by_one_take_the_case_where_the_scenario_took_it()
    {
        var process = FlowDocs.Process("previsit.outreach");
        var name = ProcessOutline.From(process).Scenarios[0];
        var values = FlowForms.FromScenario(process, name)!;
        var json = FlowForms.ToJson(FlowForms.StartFields(process), values.Values);
        json.Invalid.Should().BeEmpty();
        var session = FlowSimulation.Start(process, new FlowSimulationSetup
        {
            At = values.At,
            Today = DateOnly.FromDateTime(values.At.DateTime),
            InputJson = json.Json,
            Zones = new Dictionary<string, TimeSpan> { ["America/New_York"] = TimeSpan.FromHours(-5) },
        });
        session.Started.Should().BeTrue();
        var effect = session.Outstanding.OfType<OutstandingEffect>().Single();
        var answer = FlowScenarioAnswers.For(process, effect.Step, 0).First(item => item.Scenario == name).Answer;
        var entries = session.ApplyAnswer(effect.Id, answer);
        entries.Should().ContainSingle().Which.Source.Should().Be(FlowSimulation.ScriptedSource);
        session.Log[^1].Kind.Should().Be(FlowSimulationEntryKind.EffectAnswered);
    }

    [Fact]
    public void Applying_an_answer_records_the_source_and_a_failure_goes_through_the_failure_command()
    {
        var scenario = """{"name":"the pick call fails","start":{"at":"2027-01-04T14:00:00Z","today":"2027-01-04","input":{"batch_ref":"b-1","items":[],"visit_at":"2027-01-10T15:00:00Z"}},"events":[{"advance":"P7D"},{"fail":"ask","error":"Permanent","code":"assist.gone"}],"expect":{"status":"Completed","result":"broken"}}""";
        var edited = FlowDocs.WithScenario("flow.batch", scenario);
        var session = FlowSimulation.Start(edited, new FlowSimulationSetup { At = Start, Today = new DateOnly(2027, 1, 4), InputJson = """{"batch_ref":"b-1","items":[],"visit_at":"2027-01-10T15:00:00Z"}""" });
        session.AdvanceBy(TimeSpan.FromDays(7));
        var effect = session.Outstanding.OfType<OutstandingEffect>().Single();
        var answer = FlowScenarioAnswers.For(edited, "ask", 0).Single(item => item.Scenario == "the pick call fails").Answer;
        session.ApplyAnswer(effect.Id, answer, "A test").Should().ContainSingle().Which.Should().Match<FlowSimulationEntry>(entry => entry.Kind == FlowSimulationEntryKind.EffectFailed && entry.Source == "A test");
        FluentActions.Invoking(() => session.ApplyAnswer(null!, answer)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => session.ApplyAnswer("x", null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => session.ApplyAnswer("x", answer, null!)).Should().Throw<ArgumentNullException>();
        session.ApplyAnswer("no-such-effect", FlowAnswer.Output("{}")).Should().ContainSingle().Which.Disposition.Should().NotBe(AdvanceDisposition.Applied);
    }

    [Fact]
    public void The_start_form_has_a_field_for_each_input_variable_with_the_control_of_its_type()
    {
        FlowForms.StartFields(FlowDocs.Process("flow.batch")).Should().Equal(
            new FlowFormField("batch_ref", "Code", FlowFieldKind.Text),
            new FlowFormField("items", "Json", FlowFieldKind.Lines),
            new FlowFormField("visit_at", "Instant", FlowFieldKind.DateTime));
        FlowForms.StartFields(FlowDocs.Process("case.lifecycle")).Select(field => field.Kind).Should().Equal(FlowFieldKind.Text, FlowFieldKind.Text, FlowFieldKind.YesNo);
        FlowForms.StartFields(FlowDocs.Process("practice.daily_refresh")).Should().ContainSingle().Which.Kind.Should().Be(FlowFieldKind.Date);
        FluentActions.Invoking(() => FlowForms.StartFields(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Every_kind_of_field_is_written_as_its_type_and_an_unusable_text_is_named_and_left_out()
    {
        FlowFormField[] fields =
        [
            new("t", "Code", FlowFieldKind.Text),
            new("n", "Integer", FlowFieldKind.Number),
            new("d", "Decimal", FlowFieldKind.Number),
            new("day", "Date", FlowFieldKind.Date),
            new("at", "Instant", FlowFieldKind.DateTime),
            new("yes", "Boolean", FlowFieldKind.YesNo),
            new("list", "Json", FlowFieldKind.Lines),
            new("obj", "Json", FlowFieldKind.Lines),
            new("blank", "Code", FlowFieldKind.Text),
        ];
        var result = FlowForms.ToJson(fields, new Dictionary<string, string>
        {
            ["t"] = "  c-1 ",
            ["n"] = "-12",
            ["d"] = "2.50",
            ["day"] = "2027-01-05",
            ["at"] = "2027-01-10T15:00",
            ["yes"] = "true",
            ["list"] = "one\n\n two \n",
            ["obj"] = """{"b":1,"a":[2]}""",
            ["blank"] = "   ",
        });
        result.Invalid.Should().BeEmpty();
        result.Json.Should().Be("""{"at":"2027-01-10T15:00:00Z","d":2.50,"day":"2027-01-05","list":["one","two"],"n":-12,"obj":{"a":[2],"b":1},"t":"c-1","yes":true}""");

        var bad = FlowForms.ToJson(fields, new Dictionary<string, string> { ["n"] = "1.5", ["d"] = "x", ["day"] = "05/01/2027", ["at"] = "soon", ["yes"] = "maybe", ["t"] = "ok" });
        bad.Invalid.Should().Equal("n", "d", "day", "at", "yes");
        bad.Json.Should().Be("""{"t":"ok"}""");
        FlowForms.ToJson([], new Dictionary<string, string>()).Json.Should().Be("{}");
        FluentActions.Invoking(() => FlowForms.ToJson(null!, new Dictionary<string, string>())).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowForms.ToJson([], null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_start_form_filled_from_a_scenario_starts_the_case_the_scenario_starts_for_every_document()
    {
        foreach (var (name, process) in FlowDocs.All)
        {
            var outline = ProcessOutline.From(process);
            foreach (var scenario in outline.Scenarios)
            {
                var values = FlowForms.FromScenario(process, scenario)!;
                var form = FlowForms.ToJson(FlowForms.StartFields(process), values.Values);
                form.Invalid.Should().BeEmpty(name + " / " + scenario);
                // A control that is empty means "not given", so an input the scenario writes as an empty text is the one thing a form cannot repeat.
                var input = System.Text.Json.Nodes.JsonNode.Parse(outline.ScenarioJson(scenario)!)!["start"]!["input"]?.AsObject() ?? [];
                var expected = new System.Text.Json.Nodes.JsonObject(input.Where(pair => pair.Value?.ToJsonString() != "\"\"").Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())));
                FlowJson.Equal(System.Text.Json.Nodes.JsonNode.Parse(form.Json), expected).Should().BeTrue(name + " / " + scenario);
            }
        }
    }

    [Fact]
    public void A_scenario_that_does_not_exist_has_no_start_values_and_the_instant_text_keeps_the_seconds_only_when_there_are_some()
    {
        FlowForms.FromScenario(FlowDocs.Process("previsit.outreach"), "no such scenario").Should().BeNull();
        FluentActions.Invoking(() => FlowForms.FromScenario(null!, "x")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowForms.FromScenario(FlowDocs.Process("previsit.outreach"), null!)).Should().Throw<ArgumentNullException>();
        FlowForms.DateTimeText(Start).Should().Be("2027-01-04T14:00");
        FlowForms.DateTimeText(Start.AddSeconds(5).ToOffset(TimeSpan.FromHours(2))).Should().Be("2027-01-04T14:00:05");
    }

    [Fact]
    public void The_payload_form_of_a_wait_has_a_field_for_each_member_it_keeps_typed_by_its_variable()
    {
        var process = FlowDocs.Edited("flow.clock", document =>
        {
            var hold = document["body"]!["steps"]!["hold"]!.AsObject();
            hold["output"] = new System.Text.Json.Nodes.JsonObject { ["note"] = "ref" };
        });
        FlowForms.PayloadFields(process, "hold").Should().Equal(new FlowFormField("note", "Code", FlowFieldKind.Text));
        FlowForms.PayloadFields(process, "settle").Should().BeEmpty("a timer is not a wait");
        FlowForms.PayloadFields(process, "no_such_step").Should().BeEmpty();
        FlowForms.PayloadFields(FlowDocs.Process("flow.clock"), "hold").Should().BeEmpty();
        FluentActions.Invoking(() => FlowForms.PayloadFields(null!, "x")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowForms.PayloadFields(process, null!)).Should().Throw<ArgumentNullException>();
    }
}

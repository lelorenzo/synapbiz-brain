using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class FlowReviewTests
{
    private static readonly string[] RealTargets = ["process", "stage:run", "step:ask", "route:ask/onInvalid", "route:judge/on:high", "trigger:weekdays", "scenario:a small pick ends the batch as broken"];

    private static readonly string[] MissingPaths = ["/$spec", "/createdAt", "/items", "/language", "/process", "/proposedScenarios", "/reviewer"];

    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-11-22T21:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture);

    private static FlowReview Review(string document = "flow.batch", WorkbenchLocale language = WorkbenchLocale.En, IReadOnlyList<ReviewItem>? items = null, IReadOnlyList<FlowProposedScenario>? scenarios = null)
    {
        var outline = ProcessOutline.From(FlowDocs.Process(document));
        return new FlowReview
        {
            ProcessId = outline.Id,
            ProcessVersion = outline.Version,
            ProcessContentHash = outline.ContentHash,
            Reviewer = "Dr. Example",
            CreatedAt = Created,
            Language = language,
            Items = items ?? [new ReviewItem("process", ReviewState.Confirmed, "Reads well.")],
            ProposedScenarios = scenarios ?? [],
        };
    }

    private static string ValidScenario(string name = "a small pick") =>
        """
        {"name":"NAME","start":{"at":"2027-01-04T14:00:00Z","today":"2027-01-04","input":{"batch_ref":"b-1","items":[],"visit_at":"2027-01-10T15:00:00Z"}},
         "events":[{"advance":"P7D"},{"complete":"ask","output":{"pick":"p","size":3}}],
         "expect":{"status":"Completed","result":"broken","trace":["each","nap","ask","verify","broken"]}}
        """.Replace("NAME", name, StringComparison.Ordinal);

    [Fact]
    public void A_review_survives_writing_and_reading()
    {
        var review = Review(items: [
            new ReviewItem("process", ReviewState.Confirmed, "Reads well."),
            new ReviewItem("step:ask", ReviewState.NeedsFix, "Why one attempt?"),
            new ReviewItem("route:check/onExpired", ReviewState.Question, "Is 45 minutes enough?"),
            new ReviewItem("stage:run", ReviewState.Unreviewed, string.Empty),
        ], scenarios: [new FlowProposedScenario("a small pick", ValidScenario())]);
        var text = FlowReviewJson.Serialize(review);
        var read = FlowReviewJson.Parse(text);
        read.IsValid.Should().BeTrue();
        read.Review!.ProcessId.Should().Be("flow.batch");
        read.Review.Items.Should().Equal(review.Items);
        read.Review.Language.Should().Be(WorkbenchLocale.En);
        read.Review.CreatedAt.Should().Be(Created);
        read.Review.ProposedScenarios.Single().Name.Should().Be("a small pick");
        FlowReviewJson.Serialize(read.Review).Should().Be(text);
    }

    [Fact]
    public void The_written_document_has_a_fixed_shape()
    {
        var text = FlowReviewJson.Serialize(Review(language: WorkbenchLocale.Es));
        text.Should().StartWith("{\n  \"$spec\": \"brain.process-review@1\",\n  \"process\": {");
        text.Should().Contain("\"createdAt\": \"2026-11-22T21:00:00+00:00\"").And.Contain("\"language\": \"es\"").And.Contain("\"state\": \"confirmed\"");
        text.Should().NotContain("\r");
        FlowReviewJson.Format.Should().Be("brain.process-review@1");
        FlowReviewJson.Serialize(Review(scenarios: [new FlowProposedScenario("a", """{"b":1,"a":2,"name":"a"}""")])).Should().Contain("\"a\": 2,\n      \"b\": 1");
    }

    [Fact]
    public void A_scenario_that_is_not_an_object_cannot_be_written()
    {
        FluentActions.Invoking(() => FlowReviewJson.Serialize(Review(scenarios: [new FlowProposedScenario("x", "[1]")]))).Should().Throw<JsonException>();
    }

    [Fact]
    public void A_text_that_is_not_json_is_json_invalid()
    {
        var result = FlowReviewJson.Parse("{ not json");
        result.Review.Should().BeNull();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(FlowReviewJson.ErrorJsonInvalid);
        FlowReviewJson.Parse("""{"a":1,"a":2}""").Errors.Should().ContainSingle().Which.Code.Should().Be(FlowReviewJson.ErrorJsonInvalid);
    }

    [Fact]
    public void Another_format_is_unknown_format()
    {
        var result = FlowReviewJson.Parse("""{"$spec":"brain.process-review@2"}""");
        result.Errors.Should().ContainSingle().Which.Should().Be(new WorkbenchError(FlowReviewJson.ErrorUnknownFormat, "/$spec", "The format 'brain.process-review@2' is not 'brain.process-review@1'."));
    }

    [Fact]
    public void Every_defect_of_a_document_is_reported_with_its_path()
    {
        FlowReviewJson.Parse("[]").Errors.Should().ContainSingle().Which.Code.Should().Be(FlowReviewJson.ErrorMalformed);
        var empty = FlowReviewJson.Parse("{}");
        empty.Errors.Select(error => error.Path).Should().Equal(MissingPaths);
        empty.Errors.Should().OnlyContain(error => error.Code == FlowReviewJson.ErrorMalformed);

        var broken = FlowReviewJson.Parse(
            """
            {"$spec":"brain.process-review@1","process":{"id":1,"version":"x","contentHash":null},"reviewer":3,"createdAt":"yesterday","language":"fr",
             "items":[1,{"target":"process","state":"maybe","note":"n"},{"target":"process","state":"confirmed"}],"proposedScenarios":["x"]}
            """);
        broken.Review.Should().BeNull();
        broken.Errors.Select(error => error.Path).Should().BeInAscendingOrder(StringComparer.Ordinal)
            .And.Contain(["/process/id", "/process/version", "/process/contentHash", "/reviewer", "/createdAt", "/language", "/items/0", "/items/1/state", "/items/2/note", "/proposedScenarios/0"]);
    }

    [Fact]
    public void A_date_written_with_a_Z_is_read()
    {
        var text = FlowReviewJson.Serialize(Review()).Replace("2026-11-22T21:00:00+00:00", "2026-11-22T21:00:00Z", StringComparison.Ordinal);
        FlowReviewJson.Parse(text).Review!.CreatedAt.Should().Be(Created);
    }

    [Fact]
    public void A_review_of_this_document_with_real_targets_is_clean()
    {
        var items = RealTargets
            .Select(target => new ReviewItem(target, ReviewState.Confirmed, "ok")).ToList();
        var report = FlowReviewCheck.Against(Review(items: items), FlowDocs.Process("flow.batch"));
        report.IsClean.Should().BeTrue(string.Join("; ", report.Findings.Select(finding => finding.Message)));
        report.Scenarios.Should().BeEmpty();
    }

    [Fact]
    public void An_event_trigger_is_named_by_its_event()
    {
        var review = Review("case.lifecycle", items: [new ReviewItem("trigger:case.opened", ReviewState.Confirmed, "ok")]);
        FlowReviewCheck.Against(review, FlowDocs.Process("case.lifecycle")).IsClean.Should().BeTrue();
    }

    [Theory]
    [InlineData("stage:nothing")]
    [InlineData("step:nothing")]
    [InlineData("route:ask/nothing")]
    [InlineData("route:nothing/next")]
    [InlineData("route:ask")]
    [InlineData("trigger:nothing")]
    [InlineData("scenario:nothing")]
    [InlineData("flow")]
    [InlineData("colour:red")]
    [InlineData("")]
    public void A_target_the_flow_does_not_have_is_reported(string target)
    {
        var report = FlowReviewCheck.Against(Review(items: [new ReviewItem("process", ReviewState.Confirmed, "ok"), new ReviewItem(target, ReviewState.Question, "?")]), FlowDocs.Process("flow.batch"));
        var finding = report.Findings.Should().ContainSingle().Subject;
        finding.Code.Should().Be(FlowReviewCheck.ErrorTargetUnknown);
        finding.Path.Should().Be("/items/1/target");
        finding.Message.Should().Contain("`" + target + "`");
        report.IsClean.Should().BeFalse();
    }

    [Fact]
    public void A_review_of_another_version_is_stale_in_the_language_of_the_review()
    {
        var other = Review() with { ProcessContentHash = "0000" };
        var en = FlowReviewCheck.Against(other, FlowDocs.Process("flow.batch")).Findings.Should().ContainSingle().Subject;
        en.Code.Should().Be(FlowReviewCheck.ErrorStale);
        en.Path.Should().Be("/process/contentHash");
        var es = FlowReviewCheck.Against(other with { Language = WorkbenchLocale.Es }, FlowDocs.Process("flow.batch")).Findings.Single();
        es.Message.Should().NotBe(en.Message).And.Contain("revisión");
        FlowReviewCheck.Against(Review() with { ProcessVersion = 2 }, FlowDocs.Process("flow.batch")).Findings.Should().ContainSingle().Which.Code.Should().Be(FlowReviewCheck.ErrorStale);
        FlowReviewCheck.Against(Review() with { ProcessId = "other.flow" }, FlowDocs.Process("flow.batch")).Findings.Should().ContainSingle().Which.Code.Should().Be(FlowReviewCheck.ErrorStale);
    }

    [Fact]
    public void A_proposed_scenario_that_is_valid_and_passes_is_reported_as_passing()
    {
        var report = FlowReviewCheck.Against(Review(scenarios: [new FlowProposedScenario("a small pick", ValidScenario())]), FlowDocs.Process("flow.batch"));
        report.IsClean.Should().BeTrue(string.Join("; ", report.Findings.Select(finding => finding.Message)));
        report.Scenarios.Should().ContainSingle().Which.Should().Be(new FlowProposedScenarioResult(0, "a small pick", true, [], true, null));
    }

    [Fact]
    public void A_proposed_scenario_that_is_valid_and_fails_says_why_without_a_finding()
    {
        var wrong = ValidScenario().Replace("\"result\":\"broken\"", "\"result\":\"ok\"", StringComparison.Ordinal);
        var report = FlowReviewCheck.Against(Review(scenarios: [new FlowProposedScenario("wrong", wrong)]), FlowDocs.Process("flow.batch"));
        report.IsClean.Should().BeTrue();
        var result = report.Scenarios.Should().ContainSingle().Subject;
        result.IsValid.Should().BeTrue();
        result.Passed.Should().BeFalse();
        result.Failure.Should().Contain("ok");
    }

    [Fact]
    public void A_proposed_scenario_that_is_not_valid_carries_the_validators_code_and_path()
    {
        var invalid = ValidScenario().Replace("\"complete\":\"ask\"", "\"complete\":\"nothing\"", StringComparison.Ordinal);
        foreach (var language in new[] { WorkbenchLocale.En, WorkbenchLocale.Es })
        {
            var report = FlowReviewCheck.Against(Review(language: language, scenarios: [new FlowProposedScenario("bad", invalid)]), FlowDocs.Process("flow.batch"));
            var finding = report.Findings.Should().ContainSingle().Subject;
            finding.Code.Should().Be(FlowReviewCheck.ErrorScenarioInvalid);
            finding.Path.Should().StartWith("/proposedScenarios/0/events/1");
            var result = report.Scenarios.Should().ContainSingle().Subject;
            result.IsValid.Should().BeFalse();
            result.Passed.Should().BeNull();
            result.Errors.Should().NotBeEmpty();
            finding.Message.Should().Contain(result.Errors[0].Code);
        }
    }

    [Fact]
    public void A_proposed_scenario_without_an_expectation_is_invalid_at_its_root()
    {
        var noExpect = JsonNode.Parse(ValidScenario())!.AsObject();
        noExpect.Remove("expect");
        var report = FlowReviewCheck.Against(Review(scenarios: [new FlowProposedScenario("bad", noExpect.ToJsonString())]), FlowDocs.Process("flow.batch"));
        report.Findings.Should().ContainSingle().Which.Path.Should().Be("/proposedScenarios/0");
    }

    [Fact]
    public void A_proposed_scenario_that_is_not_an_object_is_invalid()
    {
        var report = FlowReviewCheck.Against(Review(scenarios: [new FlowProposedScenario("x", "[1]")]), FlowDocs.Process("flow.batch"));
        report.Findings.Should().ContainSingle().Which.Code.Should().Be(FlowReviewCheck.ErrorScenarioInvalid);
        report.Scenarios.Single().IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_scenario_a_reader_recorded_goes_through_the_review_and_passes()
    {
        var session = FlowSimulation.Start(FlowDocs.Process("practice.daily_refresh"), new FlowSimulationSetup { At = DateTimeOffset.Parse("2027-01-05T11:00:00Z", System.Globalization.CultureInfo.InvariantCulture), Today = new DateOnly(2027, 1, 5), InputJson = """{"run_for":"2027-01-05"}""" });
        session.CompleteEffect(session.Outstanding.OfType<OutstandingEffect>().Single().Id, """{"cases":[]}""");
        var review = Review("practice.daily_refresh", scenarios: [new FlowProposedScenario("recorded", session.ToScenarioJson("recorded"))]);
        var text = FlowReviewJson.Serialize(review);
        var read = FlowReviewJson.Parse(text).Review!;
        var report = FlowReviewCheck.Against(read, FlowDocs.Process("practice.daily_refresh"));
        report.IsClean.Should().BeTrue();
        report.Scenarios.Should().ContainSingle().Which.Passed.Should().BeTrue();
    }

    [Fact]
    public void Findings_are_ordered_by_path()
    {
        var review = Review(items: [new ReviewItem("step:zzz", ReviewState.Question, "?"), new ReviewItem("step:aaa", ReviewState.Question, "?")]) with { ProcessContentHash = "0" };
        FlowReviewCheck.Against(review, FlowDocs.Process("flow.batch")).Findings.Select(finding => finding.Path).Should().Equal("/items/0/target", "/items/1/target", "/process/contentHash");
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        FluentActions.Invoking(() => FlowReviewJson.Serialize(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowReviewJson.Parse(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowReviewCheck.Against(null!, FlowDocs.Process("flow.batch"))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => FlowReviewCheck.Against(Review(), null!)).Should().Throw<ArgumentNullException>();
    }
}

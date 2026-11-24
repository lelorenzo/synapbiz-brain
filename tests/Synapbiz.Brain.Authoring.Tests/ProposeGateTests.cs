using System.Text.Json.Nodes;
using FluentAssertions;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>flow_propose and rule_propose are the gate: nothing reaches a proposal file that the readiness checks refuse, and a rule proposal always needs review unless it stays inside what the rule already says.</summary>
public sealed class ProposeGateTests
{
    private const string NW = "visit.note_wait";
    private const string PV = "previsit.outreach";
    private const string PC = "postvisit.coding_review";

    public sealed record Defect(string Name, string Id, string Patch, string Check, bool NoTests = false)
    {
        public override string ToString() => Name;
    }

    private static readonly Defect[] Cases =
    [
        new("a label missing in one language", NW, """[{"op":"remove","path":"/meta/display/es/steps/confirm_note"}]""", ReadinessChecks.LabelMissing),
        new("a contact step that can send twice", PV, """[{"op":"replace","path":"/body/steps/send/delivery","value":"atLeastOnce"},{"op":"remove","path":"/body/steps/send/onUnknown"}]""", ReadinessChecks.ContactUnsafe),
        new("an AI step with no way without AI", PC, """[{"op":"remove","path":"/body/steps/suggest/onError"}]""", ReadinessChecks.AiWithoutFallback),
        new("a wait without a time limit", PV, """[{"op":"remove","path":"/body/steps/reply/timeout"},{"op":"remove","path":"/body/steps/reply/onTimeout"}]""", ReadinessChecks.WaitWithoutTimeout),
        new("a port outside the catalog", NW, """[{"op":"replace","path":"/body/steps/await_note/port","value":"records.nothing_here"}]""", ReadinessChecks.OutsideCatalog),
        new("a test that fails", NW, """[{"op":"replace","path":"/body/steps/chase_note/on/signed","value":"cancelled"}]""", ReadinessChecks.ScenarioFailing),
        new("a route no test takes", NW, """[{"op":"add","path":"/body/steps/chase_note/timeout","value":"PT24H"},{"op":"add","path":"/body/steps/chase_note/onTimeout","value":"cancelled"}]""", ReadinessChecks.RouteUntested),
        new("no tests at all", NW, "[]", ReadinessChecks.NoScenarios, NoTests: true),
    ];

    public static IEnumerable<object[]> Names => Cases.Select(item => new object[] { item.Name });

    private static JsonObject Draft(string id, string patch, bool noTests)
    {
        var document = JsonNode.Parse(ReferenceDocs.Changed(id, patch, noTests))!.AsObject();
        document["version"] = document["version"]!.GetValue<int>() + 1;
        return document;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void A_draft_that_fails_a_readiness_check_cannot_be_proposed(string name)
    {
        var item = Cases.Single(candidate => candidate.Name == name);
        foreach (var language in new[] { "en", "es" })
        {
            var (tools, store) = ReferenceWorkspace.Rig();
            var saved = tools.Invoke("flow_draft_save", new JsonObject { ["id"] = item.Id, ["document"] = Draft(item.Id, item.Patch, item.NoTests), ["language"] = language });
            IsOk(saved).Should().BeTrue(saved.ToJsonString());
            var failed = saved["readiness"]!["checks"]!.AsArray().Where(check => !check!["passed"]!.GetValue<bool>()).Select(check => check!["code"]!.GetValue<string>()).ToList();
            failed.Should().Contain(item.Check, name + ": " + saved["say"]);

            var proposed = tools.Invoke("flow_propose", new JsonObject { ["id"] = item.Id, ["summary"] = "A reason that is long enough", ["requestedBy"] = "Nurse Ann", ["language"] = language });
            IsOk(proposed).Should().BeFalse(name + " must be refused: " + proposed.ToJsonString());
            ErrorCode(proposed).Should().Be("authoring.proposal.not_ready");
            proposed["reasons"]!.AsArray().Should().NotBeEmpty();
            store.ProposalNames.Should().BeEmpty(name + " wrote a proposal file");
        }
    }

    [Fact]
    public void The_reference_processes_without_a_defect_are_proposed_so_the_refusals_above_are_the_defects()
    {
        foreach (var id in new[] { NW, PV, PC })
        {
            var (tools, store) = ReferenceWorkspace.Rig();
            var document = Draft(id, "[]", false);
            IsOk(tools.Invoke("flow_draft_save", new JsonObject { ["id"] = id, ["document"] = document })).Should().BeTrue();
            var proposed = tools.Invoke("flow_propose", new JsonObject { ["id"] = id, ["summary"] = "A reason that is long enough", ["requestedBy"] = "Nurse Ann" });
            IsOk(proposed).Should().BeTrue(id + ": " + proposed.ToJsonString());
            store.ProposalNames.Should().HaveCount(1);
        }
    }

    [Fact]
    public void A_rule_proposal_that_gives_a_new_outcome_value_is_written_with_needs_review_true_in_the_file_and_the_answer()
    {
        var (tools, store, _) = Rig();
        var edited = tools.Invoke("rule_draft_edit", Obj("""{"id":"intake-routing","edits":[{"op":"addRow","row":"never-seen","conditions":[{"fact":"topic","operator":"equals","value":"zzz"}],"outcome":{"route":"Manual","queue":"a-queue-nobody-used"}}]}"""));
        IsOk(edited).Should().BeTrue(edited.ToJsonString());
        var proposed = tools.Invoke("rule_propose", Obj("""{"id":"intake-routing","summary":"A new queue for odd topics","requestedBy":"Dr. Reviewer"}"""));
        IsOk(proposed).Should().BeTrue(proposed.ToJsonString());
        proposed["proposal"]!["applied"]!.GetValue<bool>().Should().BeFalse();
        proposed["proposal"]!["needsReview"]!.GetValue<bool>().Should().BeTrue("the rule now gives an outcome value it did not give");
        var stored = JsonNode.Parse(store.ReadProposal(proposed["proposal"]!["name"]!.GetValue<string>())!)!.AsObject();
        stored["needsReview"]!.GetValue<bool>().Should().BeTrue();
    }
}

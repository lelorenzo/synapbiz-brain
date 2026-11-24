using System.Text.Json.Nodes;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Kinds;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>The proposal documents: the process proposal's round trip and checks, the rule proposal file that stays readable by the older parser, and the review reasons.</summary>
public sealed class ProposalTests
{
    private static (FlowProposal Proposal, string Text, string ApprovedText) ProposedFlow()
    {
        var (tools, store, _) = Rig();
        var saved = tools.Invoke("flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(document => document["version"] = 2) });
        IsOk(saved).Should().BeTrue(saved.ToJsonString());
        var proposed = tools.Invoke("flow_propose", Obj("""{"id":"visit.note_wait","summary":"A second version, same behaviour.","requestedBy":"Front desk lead","language":"es"}"""));
        IsOk(proposed).Should().BeTrue(proposed.ToJsonString());
        var text = store.ReadProposal(store.ProposalNames.Single())!;
        var parsed = FlowProposalJson.Parse(text);
        parsed.IsValid.Should().BeTrue(string.Join("; ", parsed.Errors.Select(error => error.Code + " " + error.Path)));
        return (parsed.Proposal!, text, NoteWait);
    }

    private static ProcessLoadContext Context() => new(ReferenceDocs.Rules("visit.note_wait"), ["visit.note_wait"]);

    [Fact]
    public void A_process_proposal_survives_a_round_trip_and_says_what_a_person_needs()
    {
        var (proposal, text, _) = ProposedFlow();
        proposal.ProcessId.Should().Be("visit.note_wait");
        proposal.Version.Should().Be(2);
        proposal.BaseVersion.Should().Be(1);
        proposal.BaseContentHash.Should().NotBeNullOrWhiteSpace();
        proposal.Author.Should().Be("Front desk lead");
        proposal.CreatedAt.Should().Be(Start);
        proposal.Language.Should().Be(WorkbenchLocale.Es);
        proposal.NeedsReview.Should().BeTrue();
        proposal.Tests.Should().NotBeEmpty().And.OnlyContain(test => test.Passed);
        proposal.DocumentHash.Should().Be(FlowProposalJson.HashOf(proposal.DocumentJson));
        proposal.ChangesEn.Summary.Should().NotBeNullOrWhiteSpace();
        proposal.ChangesEs.Summary.Should().NotBeNullOrWhiteSpace().And.NotBe(proposal.ChangesEn.Summary);

        FlowProposalJson.Serialize(proposal).Should().Be(text, "serialising what was parsed gives the same text");
        FlowProposalJson.Parse(text).Proposal.Should().BeEquivalentTo(proposal);
        JsonNode.Parse(text)!["$spec"]!.GetValue<string>().Should().Be("brain.process-proposal@1");
        FlowProposalCheck.Against(proposal, Context(), NoteWait).Should().BeEmpty();
    }

    [Fact]
    public void The_text_a_person_reads_is_in_the_asked_language_and_says_nothing_is_applied()
    {
        var (proposal, _, _) = ProposedFlow();
        var en = ProposalText.Flow(proposal, WorkbenchLocale.En);
        var es = ProposalText.Flow(proposal, WorkbenchLocale.Es);
        en.Should().Contain("Nothing changes until a person applies");
        es.Should().NotBe(en).And.Contain("proceso");
        en.Should().Contain("Front desk lead");
    }

    [Theory]
    [InlineData("not json", FlowProposalJson.ErrorJsonInvalid)]
    [InlineData("[]", FlowProposalJson.ErrorJsonInvalid)]
    [InlineData("""{"$spec":"brain.process-proposal@2"}""", FlowProposalJson.ErrorUnknownFormat)]
    [InlineData("""{"$spec":"brain.process-proposal@1"}""", FlowProposalJson.ErrorMalformed)]
    [InlineData("""{"$spec":"brain.process-proposal@1","processId":5}""", FlowProposalJson.ErrorMalformed)]
    public void A_proposal_that_cannot_be_read_is_refused_with_a_code(string text, string code)
    {
        var parsed = FlowProposalJson.Parse(text);
        parsed.IsValid.Should().BeFalse();
        parsed.Proposal.Should().BeNull();
        parsed.Errors.Should().Contain(error => error.Code == code);
    }

    [Fact]
    public void A_proposal_with_a_member_missing_names_the_member()
    {
        var (_, text, _) = ProposedFlow();
        foreach (var member in new[] { "process", "author", "createdAt", "summary", "document", "documentHash", "tests" })
        {
            var node = (JsonObject)JsonNode.Parse(text)!;
            node.Remove(member).Should().BeTrue(member);
            var parsed = FlowProposalJson.Parse(node.ToJsonString());
            parsed.IsValid.Should().BeFalse(member);
            parsed.Errors.Should().Contain(error => error.Code == FlowProposalJson.ErrorMalformed, member);
        }
    }

    [Fact]
    public void Each_check_of_a_process_proposal_has_a_fixture_that_fails_it()
    {
        var (proposal, _, approved) = ProposedFlow();
        var context = Context();

        Codes(proposal with { NeedsReview = false }).Should().Equal(FlowProposalCheck.ErrorNeedsReview);
        Codes(proposal with { DocumentHash = "0000" }).Should().Equal(FlowProposalCheck.ErrorHash);
        Codes(proposal with { ProcessId = "other.process" }).Should().Equal(FlowProposalCheck.ErrorIdentity);
        Codes(proposal with { BaseVersion = 2 }).Should().Equal(FlowProposalCheck.ErrorVersion);
        Codes(proposal, "{ \"somebody\": \"changed it\" }").Should().Equal(FlowProposalCheck.ErrorBaseStale);

        var broken = Obj(proposal.DocumentJson);
        broken["body"]!["start"] = "nowhere";
        var brokenText = AuthoringJson.Format(broken);
        Codes(proposal with { DocumentJson = brokenText, DocumentHash = FlowProposalJson.HashOf(brokenText) }).Should().Contain(FlowProposalCheck.ErrorDocument);

        var failing = Obj(proposal.DocumentJson);
        failing["body"]!["steps"]!["chase_note"]!["on"]!["signed"] = "cancelled";
        var failingText = AuthoringJson.Format(failing);
        Codes(proposal with { DocumentJson = failingText, DocumentHash = FlowProposalJson.HashOf(failingText) }).Should().Equal(FlowProposalCheck.ErrorTests);

        FlowProposalCheck.Codes.Should().BeEquivalentTo(
            FlowProposalCheck.ErrorNeedsReview, FlowProposalCheck.ErrorHash, FlowProposalCheck.ErrorDocument, FlowProposalCheck.ErrorIdentity,
            FlowProposalCheck.ErrorTests, FlowProposalCheck.ErrorBaseStale, FlowProposalCheck.ErrorVersion);
        _ = approved;

        IReadOnlyList<string> Codes(FlowProposal candidate, string? approvedText = null) =>
            [.. FlowProposalCheck.Against(candidate, context, approvedText ?? approved).Select(error => error.Code)];
    }

    [Fact]
    public void A_new_process_has_no_base_and_is_proposed_as_new()
    {
        var (tools, store, _) = Rig();
        var document = NoteWaitChanged(item =>
        {
            item["id"] = "visit.note_wait_copy";
            item["version"] = 1;
        });
        var saved = tools.Invoke("flow_draft_save", new JsonObject { ["id"] = "visit.note_wait_copy", ["document"] = document });
        IsOk(saved).Should().BeTrue(saved.ToJsonString());
        var proposed = tools.Invoke("flow_propose", Obj("""{"id":"visit.note_wait_copy","summary":"A copy for another kind of visit.","requestedBy":"Front desk lead"}"""));
        IsOk(proposed).Should().BeTrue(proposed.ToJsonString());
        var proposal = FlowProposalJson.Parse(store.ReadProposal(store.ProposalNames.Single())!).Proposal!;
        proposal.BaseVersion.Should().BeNull();
        proposal.BaseContentHash.Should().BeNull();
        proposal.NeedsReview.Should().BeTrue();
    }

    [Fact]
    public void The_rule_proposal_file_carries_needs_review_and_the_older_parser_still_reads_it()
    {
        var inner = new RuleProposal("visit.note_ready", 1, "sha256:abc", "Dr. Reviewer", Start, "Name the first row.", NoteReady);
        var file = new RuleProposalFile(inner, true, [RuleReviewNeed.NewFactRead], "The first row now also reads the amount.", "La primera fila ahora también lee el importe.");
        var text = RuleProposalFiles.Serialize(file);

        var old = RuleProposalJson.Parse(text);
        old.IsValid.Should().BeTrue(string.Join("; ", old.Errors.Select(error => error.Code + " " + error.Path)));
        old.Proposal.Should().BeEquivalentTo(inner with { DraftJson = old.Proposal!.DraftJson });
        JsonNode.DeepEquals(JsonNode.Parse(old.Proposal!.DraftJson), JsonNode.Parse(NoteReady)).Should().BeTrue("the draft document is the same, only formatted");
        JsonNode.Parse(text)!["$spec"]!.GetValue<string>().Should().Be("brain.rule-proposal@1");

        var round = RuleProposalFiles.Parse(text);
        round.IsValid.Should().BeTrue();
        round.File.Should().BeEquivalentTo(file, options => options.Excluding(item => item.Proposal.DraftJson));
        RuleProposalFiles.Serialize(round.File!).Should().Be(text);
        round.File!.Summary(WorkbenchLocale.Es).Should().Be(file.SummaryEs);
    }

    [Fact]
    public void A_rule_proposal_without_the_review_members_counts_as_needing_review()
    {
        var inner = new RuleProposal("visit.note_ready", 1, "sha256:abc", "Dr. Reviewer", Start, "Name the first row.", NoteReady);
        var plain = RuleProposalJson.Serialize(inner);
        var parsed = RuleProposalFiles.Parse(plain);
        parsed.IsValid.Should().BeTrue();
        parsed.File!.NeedsReview.Should().BeTrue("an older file cannot vouch that it stays within the rule");
        parsed.File.Reasons.Should().BeEmpty();
        RuleProposalFiles.Parse("not json").IsValid.Should().BeFalse();
    }

    [Fact]
    public void The_rule_proposal_the_tool_writes_is_readable_by_the_older_parser()
    {
        var (tools, store, _) = Rig();
        _ = tools.Invoke("rule_draft_edit", Obj("""{"id":"visit.note_ready","edits":[{"op":"setRowLabel","row":"signed","locale":"en","text":"The note is signed"}]}"""));
        var proposed = tools.Invoke("rule_propose", Obj("""{"id":"visit.note_ready","summary":"Name the first row.","requestedBy":"Dr. Reviewer"}"""));
        IsOk(proposed).Should().BeTrue(proposed.ToJsonString());
        var text = store.ReadProposal(store.ProposalNames.Single())!;
        RuleProposalJson.Parse(text).IsValid.Should().BeTrue();
        var file = RuleProposalFiles.Parse(text).File!;
        file.NeedsReview.Should().BeFalse();
        ProposalText.Rule(file, WorkbenchLocale.En).Should().Contain("Nothing changes until");
        ProposalText.Rule(file, WorkbenchLocale.Es).Should().NotBe(ProposalText.Rule(file, WorkbenchLocale.En));
    }

    // ---- The reasons a rule proposal needs review.

    private static BrainRule Rule(string text, JsonObject? contract = null)
    {
        var loaded = BrainRuleLoader.Load(text, [BrainContract.Parse((contract ?? IntakeContract).ToJsonString())]);
        return loaded.Rule ?? throw new InvalidOperationException("The rule does not load: " + string.Join("; ", loaded.Errors.Select(error => error.Code + " at " + error.Path + ": " + error.MessageEn)));
    }

    private static string IntakeWithout(string? rowId = null, string? outcomeField = null, Action<JsonObject>? change = null)
    {
        var document = Obj(IntakeRule);
        var body = (JsonObject)document["body"]!;
        var rows = (JsonArray)body["rows"]!;
        if (rowId is not null)
        {
            rows.Remove(rows.Single(row => row!["id"]!.GetValue<string>() == rowId));
        }

        if (outcomeField is not null)
        {
            foreach (var row in rows.Concat([body["defaultRow"]]).OfType<JsonObject>())
            {
                ((JsonObject)row["outcome"]!).Remove(outcomeField);
            }
        }

        body["goldens"] = new JsonArray();
        change?.Invoke(body);
        return document.ToJsonString();
    }

    [Fact]
    public void A_rule_that_changes_nothing_that_matters_needs_no_review()
    {
        var full = Rule(IntakeWithout());
        RuleReviewNeed.Reasons(full, full).Should().BeEmpty();
        RuleReviewNeed.Reasons(Rule(IntakeWithout()), Rule(IntakeWithout(rowId: "mid-amount"))).Should().BeEmpty("reading fewer facts stays within the rule");
    }

    [Fact]
    public void A_new_fact_read_needs_review()
    {
        // The row `mid-amount` is the only one that reads `amount`: a draft without it is the approved rule, and the full rule is the draft that reads a new fact.
        var reasons = RuleReviewNeed.Reasons(Rule(IntakeWithout(rowId: "mid-amount")), Rule(IntakeWithout()));
        reasons.Should().Contain(RuleReviewNeed.NewFactRead);
        reasons.Should().NotContain(RuleReviewNeed.KindChanged).And.NotContain(RuleReviewNeed.HitPolicyChanged);
    }

    [Fact]
    public void A_new_outcome_field_needs_review()
    {
        var reasons = RuleReviewNeed.Reasons(Rule(IntakeWithout(outcomeField: "priority")), Rule(IntakeWithout()));
        reasons.Should().Contain(RuleReviewNeed.NewOutcomeField);
    }

    [Fact]
    public void A_new_value_of_a_code_or_enum_outcome_needs_review()
    {
        var reasons = RuleReviewNeed.Reasons(Rule(IntakeWithout(rowId: "old-request")), Rule(IntakeWithout()));
        reasons.Should().Equal(RuleReviewNeed.NewOutcomeValue);
    }

    [Fact]
    public void A_changed_hit_policy_needs_review()
    {
        var first = Rule(IntakeWithout());
        var unique = Rule(Read("tests/fixtures/workbench/intake.unique.rule.json").Replace("intake-unique", "intake-routing", StringComparison.Ordinal));
        RuleReviewNeed.Reasons(first, unique).Should().Contain(RuleReviewNeed.HitPolicyChanged);
    }

    [Fact]
    public void A_changed_decision_kind_needs_review()
    {
        var contract = IntakeContract;
        var kinds = (JsonArray)contract["kinds"]!;
        var other = (JsonObject)kinds[0]!.DeepClone();
        other["id"] = "sample.intake_b";
        kinds.Add(other);
        var facts = (JsonArray)contract["facts"]!;
        foreach (var fact in facts.OfType<JsonObject>().Where(fact => fact["kind"]!.GetValue<string>() == "sample.intake").ToList())
        {
            var copy = (JsonObject)fact.DeepClone();
            copy["kind"] = "sample.intake_b";
            facts.Add(copy);
        }

        var moved = IntakeWithout(change: body => body["kind"] = "sample.intake_b");
        RuleReviewNeed.Reasons(Rule(IntakeWithout(), contract), Rule(moved, contract)).Should().Contain(RuleReviewNeed.KindChanged);
    }

    [Fact]
    public void Every_reason_has_a_sentence_in_both_languages()
    {
        RuleReviewNeed.Codes.Should().Equal(RuleReviewNeed.KindChanged, RuleReviewNeed.HitPolicyChanged, RuleReviewNeed.NewFactRead, RuleReviewNeed.NewOutcomeField, RuleReviewNeed.NewOutcomeValue);
        foreach (var code in RuleReviewNeed.Codes)
        {
            foreach (var locale in new[] { WorkbenchLocale.En, WorkbenchLocale.Es })
            {
                AuthoringText.Text(locale, "rule.review." + code).Should().NotBeNullOrWhiteSpace(code);
            }
        }
    }
}

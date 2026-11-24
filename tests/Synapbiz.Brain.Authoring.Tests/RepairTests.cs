using System.Text.Json.Nodes;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>One test for each finding of the verification of the first release: each fails on the code before the repair.</summary>
public sealed class RepairTests
{
    private const string NW = "visit.note_wait";
    private const string Reason = "Checked by hand with the front desk.";

    private static JsonObject Version2(Action<JsonObject>? change = null)
    {
        var document = NoteWaitChanged(item => item["version"] = 2);
        change?.Invoke(document);
        return document;
    }

    private static JsonObject SaveV2(AuthoringTools tools, Action<JsonObject>? change = null, JsonObject? extra = null, string language = "en")
    {
        var input = new JsonObject { ["id"] = NW, ["document"] = Version2(change), ["language"] = language };
        foreach (var (name, value) in extra ?? [])
        {
            input[name] = value?.DeepClone();
        }

        return tools.Invoke("flow_draft_save", input);
    }

    private static IEnumerable<string> FailedChecks(JsonObject answer) =>
        answer["readiness"]!["checks"]!.AsArray().Where(check => !check!["passed"]!.GetValue<bool>()).Select(check => check!["code"]!.GetValue<string>());

    private static string Findings(JsonObject answer) =>
        string.Join(" | ", answer["readiness"]!["checks"]!.AsArray().SelectMany(check => check!["findings"]!.AsArray().Select(finding => finding!["say"]!.GetValue<string>())));

    // ---- B2: the id of a draft is the id of its file, whichever tool writes it.

    [Fact]
    public void A_patch_that_replaces_the_id_is_refused_like_a_save_with_another_id()
    {
        var (tools, store, _) = Rig();
        IsOk(SaveV2(tools)).Should().BeTrue();
        var patched = tools.Invoke("flow_draft_patch", Obj("""{"id":"visit.note_wait","operations":[{"op":"replace","path":"/id","value":"another.process"}]}"""));
        IsOk(patched).Should().BeFalse(patched.ToJsonString());
        ErrorCode(patched).Should().Be("authoring.draft.id_mismatch");
        JsonNode.Parse(store.ReadDraft(DocumentKind.Flow, NW)!.Text)!["id"]!.GetValue<string>().Should().Be(NW, "the draft was not changed");
    }

    [Fact]
    public void A_proposal_for_another_process_than_its_base_is_inconsistent()
    {
        var (tools, store, _) = Rig();
        IsOk(SaveV2(tools)).Should().BeTrue();
        IsOk(tools.Invoke("flow_propose", Obj("""{"id":"visit.note_wait","summary":"A second version","requestedBy":"Nurse Ann"}"""))).Should().BeTrue();
        var proposal = FlowProposalJson.Parse(store.ReadProposal(store.ProposalNames.Single())!).Proposal!;
        var context = new ProcessLoadContext(ReferenceDocs.Rules(NW), [NW]);
        FlowProposalCheck.Against(proposal, context, NoteWait, NW).Should().BeEmpty();
        FlowProposalCheck.Against(proposal, context, NoteWait, "another.process").Should().Contain(error => error.Code == FlowProposalCheck.ErrorIdentity && error.Path == "/process/id");
    }

    // ---- SF1: no catalog, no safety.

    [Fact]
    public void A_process_that_reads_and_acts_is_not_ready_when_no_catalog_was_given()
    {
        var contractsOnly = new AuthoringCatalog { ContractDocuments = [VisitContract] };
        var store = new InMemoryAuthoringStore(new Dictionary<string, string> { [NW] = NoteWait }, new Dictionary<string, string> { ["visit.note_ready"] = NoteReady }, CatalogJson.Serialize(contractsOnly));
        var tools = new AuthoringTools(store, new AuthoringOptions());
        var saved = SaveV2(tools);
        IsOk(saved).Should().BeTrue(saved.ToJsonString());
        saved["ready"]!.GetValue<bool>().Should().BeFalse();
        FailedChecks(saved).Should().Contain(ReadinessChecks.CatalogMissing);
        Findings(saved).Should().Contain("No catalog was given, so connectors and contact safety cannot be checked");
        Findings(SaveV2(tools, language: "es")).Should().Contain("No se dio ningún catálogo");
    }

    // ---- SF2: zero tests is never ready; what is accepted knowingly is named and kept.

    [Fact]
    public void A_process_with_no_tests_is_not_ready_even_when_everything_else_is_fine()
    {
        var (tools, _, _) = Rig();
        var saved = SaveV2(tools, document => document["body"]!.AsObject().Remove("scenarios"));
        IsOk(saved).Should().BeTrue(saved.ToJsonString());
        saved["ready"]!.GetValue<bool>().Should().BeFalse();
        FailedChecks(saved).Should().Contain(ReadinessChecks.NoScenarios);
    }

    private static void AddUntestedRoute(JsonObject document)
    {
        var step = document["body"]!["steps"]!["chase_note"]!.AsObject();
        step["timeout"] = "PT24H";
        step["onTimeout"] = "cancelled";
    }

    private static JsonObject UntestedGap(string reason) => new()
    {
        ["knownGaps"] = new JsonArray(new JsonObject { ["check"] = KnownGap.RouteUntested, ["step"] = "chase_note", ["route"] = "onTimeout", ["reason"] = reason }),
    };

    [Fact]
    public void A_reason_shorter_than_ten_characters_is_refused_and_nothing_is_saved()
    {
        var (tools, store, _) = Rig();
        var saved = SaveV2(tools, AddUntestedRoute, UntestedGap("later"));
        IsOk(saved).Should().BeFalse(saved.ToJsonString());
        ErrorCode(saved).Should().Be("authoring.gaps.reason_short");
        store.ReadDraft(DocumentKind.Flow, NW).Should().BeNull();
    }

    [Fact]
    public void Everything_accepted_knowingly_is_named_in_the_ready_answer_and_written_in_the_proposal_with_its_reason()
    {
        var (tools, store, _) = Rig();
        var refused = SaveV2(tools, AddUntestedRoute);
        refused["ready"]!.GetValue<bool>().Should().BeFalse();

        var saved = SaveV2(tools, AddUntestedRoute, UntestedGap(Reason));
        saved["ready"]!.GetValue<bool>().Should().BeTrue(saved.ToJsonString());
        saved["say"]!.GetValue<string>().Should().Contain("Chase the note").And.Contain("accepted knowingly");

        IsOk(tools.Invoke("flow_propose", Obj("""{"id":"visit.note_wait","summary":"A second version","requestedBy":"Nurse Ann"}"""))).Should().BeTrue();
        var proposal = FlowProposalJson.Parse(store.ReadProposal(store.ProposalNames.Single())!).Proposal!;
        proposal.KnownGaps.Should().ContainSingle().Which.Reason.Should().Be(Reason);
        ProposalText.Flow(proposal, WorkbenchLocale.En).Should().Contain(Reason);
    }

    // ---- SF3: the version is newer and the base is not stale.

    [Fact]
    public void A_draft_whose_version_is_not_greater_than_its_base_is_not_ready_in_both_languages()
    {
        var (tools, _, _) = Rig();
        foreach (var language in new[] { "en", "es" })
        {
            var input = new JsonObject { ["id"] = NW, ["document"] = NoteWaitChanged(_ => { }), ["basedOn"] = NW, ["language"] = language };
            var saved = tools.Invoke("flow_draft_save", input);
            IsOk(saved).Should().BeTrue(saved.ToJsonString());
            FailedChecks(saved).Should().Contain(ReadinessChecks.VersionNotNewer);
            Findings(saved).Should().Contain(language == "en" ? "greater than the current one" : "mayor que el actual");
        }

        FixHints.For("authoring.proposal.version_not_newer").Should().NotBeNullOrWhiteSpace();
        FixHints.For("authoring.proposal.base_stale").Should().Contain("basedOn");
    }

    [Fact]
    public void A_draft_whose_base_moved_is_not_ready_and_says_how_to_start_again()
    {
        var inner = NoteStore();
        var moving = new MovingFlowStore(inner);
        var tools = new AuthoringTools(moving, new AuthoringOptions());
        var saved = SaveV2(tools, extra: new JsonObject { ["basedOn"] = NW });
        IsOk(saved).Should().BeTrue(saved.ToJsonString());
        saved["ready"]!.GetValue<bool>().Should().BeTrue(saved["say"]?.ToString());
        moving.Moved = true;
        var again = tools.Invoke("flow_draft_patch", Obj("""{"id":"visit.note_wait","operations":[{"op":"replace","path":"/version","value":3}]}"""));
        IsOk(again).Should().BeTrue();
        again["ready"]!.GetValue<bool>().Should().BeFalse();
        FailedChecks(again).Should().Contain(ReadinessChecks.BaseStale);
        var proposed = tools.Invoke("flow_propose", Obj("""{"id":"visit.note_wait","summary":"A second version","requestedBy":"Nurse Ann"}"""));
        IsOk(proposed).Should().BeFalse();
        ErrorCode(proposed).Should().Be("authoring.proposal.not_ready");
        FixHints.For(ReadinessChecks.BaseStale).Should().Contain("basedOn");
    }

    // ---- SF4: tests are named once.

    [Fact]
    public void Two_tests_with_the_same_name_make_the_draft_not_ready()
    {
        var (tools, _, _) = Rig();
        var saved = SaveV2(tools, document =>
        {
            var scenarios = document["body"]!["scenarios"]!.AsArray();
            scenarios.Add(scenarios[0]!.DeepClone());
        });
        IsOk(saved).Should().BeTrue(saved.ToJsonString());
        FailedChecks(saved).Should().Contain(ReadinessChecks.ScenarioNamesDuplicated);
    }

    // ---- SF5: calls are serialized.

    [Fact]
    public void Many_calls_at_once_give_only_successes_or_coded_refusals_and_the_draft_is_one_of_the_written_values()
    {
        var (tools, store, _) = Rig();
        IsOk(SaveV2(tools)).Should().BeTrue();
        var versions = Enumerable.Range(3, 40).ToList();
        var answers = new System.Collections.Concurrent.ConcurrentBag<JsonObject>();
        Parallel.ForEach(versions, new ParallelOptions { MaxDegreeOfParallelism = 8 }, version =>
        {
            answers.Add(version % 2 == 0
                ? tools.Invoke("flow_draft_save", new JsonObject { ["id"] = NW, ["document"] = NoteWaitChanged(document => document["version"] = version) })
                : tools.Invoke("flow_draft_patch", Obj("{\"id\":\"visit.note_wait\",\"operations\":[{\"op\":\"replace\",\"path\":\"/version\",\"value\":" + version + "}]}")));
        });

        answers.Should().HaveCount(versions.Count);
        answers.Should().OnlyContain(answer => IsOk(answer) || answer["error"]!["code"]!.GetValue<string>().StartsWith("authoring.", StringComparison.Ordinal));
        var final = JsonNode.Parse(store.ReadDraft(DocumentKind.Flow, NW)!.Text)!["version"]!.GetValue<int>();
        versions.Should().Contain(final);
    }

    // ---- SF6: the token of a simulation comes from the source the host chose.

    [Fact]
    public void A_simulation_token_comes_from_the_injected_source_and_is_never_a_counter_when_one_is_given()
    {
        var made = 0;
        var (tools, _, _) = Rig(options: new AuthoringOptions { SessionIds = () => "sim-" + Guid.NewGuid().ToString("N") + (++made).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        IsOk(SaveV2(tools)).Should().BeTrue();
        var started = tools.Invoke("flow_draft_simulate", Obj("""{"id":"visit.note_wait","start":{"at":"2027-01-04T14:00:00Z","input":{"case_ref":"c-1"}}}"""));
        IsOk(started).Should().BeTrue(started.ToJsonString());
        var token = started["session"]!.GetValue<string>();
        token.Should().NotBe("sim-1").And.HaveLength(4 + 32 + 1);
        made.Should().Be(1);
    }

    // ---- SF7 and SF8.

    [Fact]
    public void A_start_with_an_input_the_process_does_not_take_names_it_in_words()
    {
        var (tools, _, _) = Rig();
        IsOk(SaveV2(tools)).Should().BeTrue();
        var started = tools.Invoke("flow_draft_simulate", Obj("""{"id":"visit.note_wait","start":{"input":{"colour":"red","case_ref":"c-1"}}}"""));
        var say = started["say"]!.GetValue<string>();
        say.Should().Contain("“colour”").And.Contain("“case ref”");
        say.Should().NotContain("workbench").And.NotContain("`");
        var es = tools.Invoke("flow_draft_simulate", Obj("""{"id":"visit.note_wait","language":"es","start":{"input":{"colour":"red","case_ref":"c-1"}}}"""))["say"]!.GetValue<string>();
        es.Should().Contain("“colour”").And.Contain("no recibe");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_proposal_without_a_summary_is_refused_for_a_process_and_for_a_rule(string summary)
    {
        var (tools, store, _) = Rig();
        IsOk(SaveV2(tools)).Should().BeTrue();
        IsOk(tools.Invoke("rule_draft_edit", Obj("""{"id":"visit.note_ready","edits":[{"op":"setRowLabel","row":"signed","locale":"en","text":"The note is signed"}]}"""))).Should().BeTrue();
        foreach (var tool in new[] { "flow_propose", "rule_propose" })
        {
            var id = tool == "flow_propose" ? NW : "visit.note_ready";
            var refused = tools.Invoke(tool, new JsonObject { ["id"] = id, ["summary"] = summary, ["requestedBy"] = "Nurse Ann" });
            IsOk(refused).Should().BeFalse(tool);
            ErrorCode(refused).Should().Be("authoring.summary.empty");
        }

        store.ProposalNames.Should().BeEmpty();
    }

    // ---- SF9: a list and a read say what could not be read.

    [Fact]
    public void A_list_and_a_read_say_how_many_approved_documents_cannot_be_read_and_why_in_words()
    {
        var broken = Obj(NoteWait);
        broken["body"]!["start"] = "nowhere";
        var store = new InMemoryAuthoringStore(
            new Dictionary<string, string> { [NW] = NoteWait, ["broken.process"] = broken.ToJsonString().Replace("visit.note_wait", "broken.process", StringComparison.Ordinal) },
            new Dictionary<string, string> { ["visit.note_ready"] = NoteReady },
            CatalogJson.Serialize(NoteCatalog()));
        var tools = new AuthoringTools(store, new AuthoringOptions());
        var listed = tools.Invoke("flow_list", new JsonObject());
        listed["say"]!.GetValue<string>().Should().Contain("could not read").And.Contain("1");
        var read = tools.Invoke("flow_get", Obj("""{"id":"broken.process"}"""));
        read["say"]!.GetValue<string>().Should().Contain("first problem").And.NotContain("`").And.NotContain("/body");
    }

    // ---- SF10: wording.

    [Fact]
    public void A_moment_is_written_as_a_person_says_it_with_its_zone()
    {
        AuthoringText.Moment(WorkbenchLocale.En, new DateTimeOffset(2027, 1, 4, 14, 0, 0, TimeSpan.Zero)).Should().Be("January 4, 2027 at 14:00 (UTC)");
        AuthoringText.Moment(WorkbenchLocale.Es, new DateTimeOffset(2027, 1, 4, 14, 0, 0, TimeSpan.FromHours(-5))).Should().Be("4 de enero de 2027 a las 14:00 (UTC-5)");
    }

    [Fact]
    public void The_proposal_text_says_the_tests_take_the_routes_and_asks_without_ugly_plurals()
    {
        var (tools, store, _) = Rig();
        IsOk(SaveV2(tools)).Should().BeTrue();
        IsOk(tools.Invoke("flow_propose", Obj("""{"id":"visit.note_wait","summary":"A second version","requestedBy":"Nurse Ann"}"""))).Should().BeTrue();
        var proposal = FlowProposalJson.Parse(store.ReadProposal(store.ProposalNames.Single())!).Proposal!;
        ProposalText.Flow(proposal, WorkbenchLocale.En).Should().MatchRegex(@"The tests take \d+ of \d+ routes\.").And.Contain("Asked by Nurse Ann on January 4, 2027 at 14:00 (UTC)");
        ProposalText.Flow(proposal, WorkbenchLocale.Es).Should().MatchRegex(@"Las pruebas recorren \d+ de \d+ rutas\.");
    }

    private sealed class MovingFlowStore(IAuthoringStore inner) : IAuthoringStore
    {
        public bool Moved { get; set; }

        public IReadOnlyList<string> ListApproved(DocumentKind kind) => inner.ListApproved(kind);

        public string? ReadApproved(DocumentKind kind, string id)
        {
            var text = inner.ReadApproved(kind, id);
            if (!Moved || kind != DocumentKind.Flow || text is null)
            {
                return text;
            }

            var document = Obj(text);
            document["meta"]!["description"] = "Changed by someone else meanwhile.";
            return document.ToJsonString();
        }

        public string? ReadCatalog() => inner.ReadCatalog();

        public IReadOnlyList<string> ListDrafts(DocumentKind kind) => inner.ListDrafts(kind);

        public StoredDraft? ReadDraft(DocumentKind kind, string id) => inner.ReadDraft(kind, id);

        public void WriteDraft(DocumentKind kind, string id, StoredDraft draft) => inner.WriteDraft(kind, id, draft);

        public IReadOnlyList<string> ListProposals() => inner.ListProposals();

        public string? ReadProposal(string name) => inner.ReadProposal(name);

        public string WriteProposal(string name, string json) => inner.WriteProposal(name, json);
    }
}

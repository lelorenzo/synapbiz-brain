using System.Text.Json.Nodes;
using FluentAssertions;
using Json.Schema;
using Synapbiz.Brain.Workbench;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>Every tool: its schema, a success, and every refusal it can give.</summary>
public sealed class ToolTests
{
    private static readonly string[] ToolNames =
    [
        "brain_guide", "brain_catalog", "flow_list", "flow_get", "rule_list", "rule_get", "flow_draft_save", "flow_draft_patch", "flow_draft_simulate",
        "flow_draft_add_scenario", "rule_draft_edit", "rule_simulate", "rule_test", "flow_propose", "rule_propose", "proposal_list", "proposal_get",
    ];

    public sealed record Refusal(string Name, string Code, Func<AuthoringTools, JsonObject?> Act, AuthoringOptions? Options = null)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<object[]> Refusals => Cases.Select(item => new object[] { item.Name });

    private static readonly Refusal[] Cases =
    [
        new("unknown tool", "authoring.tool.unknown", tools => I(tools, "flow_delete", new JsonObject())),
        new("flow_get without id", "authoring.input.invalid", tools => I(tools, "flow_get", new JsonObject())),
        new("flow_get with an extra member", "authoring.input.invalid", tools => I(tools, "flow_get", Obj("""{"id":"visit.note_wait","force":true}"""))),
        new("flow_get with a bad language", "authoring.input.invalid", tools => I(tools, "flow_get", Obj("""{"id":"visit.note_wait","language":"fr"}"""))),
        new("flow_get with a path as id", "authoring.input.invalid", tools => I(tools, "flow_get", Obj("""{"id":"../visit.note_wait"}"""))),
        new("flow_get with an id of a file name", "authoring.workspace.id_invalid", tools => I(tools, "flow_get", Obj("""{"id":"visit.draft"}"""))),
        new("flow_get unknown", "authoring.flow.not_found", tools => I(tools, "flow_get", Obj("""{"id":"nothing.here"}"""))),
        new("flow_get of a missing draft", "authoring.draft.not_found", tools => I(tools, "flow_get", Obj("""{"id":"visit.note_wait","draft":true}"""))),
        new("rule_get unknown", "authoring.rule.not_found", tools => I(tools, "rule_get", Obj("""{"id":"nothing.here"}"""))),
        new("rule_get of a missing draft", "authoring.draft.not_found", tools => I(tools, "rule_get", Obj("""{"id":"visit.note_ready","draft":true}"""))),
        new("brain_guide unknown section", "authoring.input.invalid", tools => I(tools, "brain_guide", Obj("""{"section":"cooking"}"""))),
        new("catalog that is not valid", "authoring.catalog.invalid", tools => I(tools, "flow_list", new JsonObject())),
        new("save with a document of another id", "authoring.draft.id_mismatch", tools => I(tools, "flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(document => document["id"] = "other.thing") })),
        new("save too large", "authoring.draft.too_large", tools => I(tools, "flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(_ => { }) }), Options: new AuthoringOptions { MaxDocumentLength = 100 }),
        new("save with a base that does not exist", "authoring.draft.base_not_found", tools => I(tools, "flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(_ => { }), ["basedOn"] = "no.such" })),
        new("patch without a draft", "authoring.draft.not_found", tools => I(tools, "flow_draft_patch", Obj("""{"id":"visit.note_wait","operations":[]}"""))),
        new("patch with an operation that fails", "authoring.patch.failed", tools =>
        {
            Save(tools);
            return I(tools, "flow_draft_patch", Obj("""{"id":"visit.note_wait","operations":[{"op":"remove","path":"/body/steps/nowhere"}]}"""));
        }),
        new("patch of a draft that is not JSON", "authoring.draft.not_json", tools =>
        {
            _ = I(tools, "flow_draft_save", Obj("""{"id":"visit.note_wait","document":"this is not json"}"""));
            return I(tools, "flow_draft_patch", Obj("""{"id":"visit.note_wait","operations":[{"op":"replace","path":"/version","value":2}]}"""));
        }),
        new("patch too large", "authoring.draft.too_large", tools =>
        {
            Save(tools);
            return I(tools, "flow_draft_patch", new JsonObject { ["id"] = "visit.note_wait", ["operations"] = new JsonArray(new JsonObject { ["op"] = "replace", ["path"] = "/version", ["value"] = new string('x', 300) }) });
        }, Options: new AuthoringOptions { MaxDocumentLength = NoteWaitChanged(_ => { }).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }).Length + 100 }),
        new("scenario without a source", "authoring.scenario.source", tools =>
        {
            Save(tools);
            return I(tools, "flow_draft_add_scenario", Obj("""{"id":"visit.note_wait"}"""));
        }),
        new("scenario on a missing draft", "authoring.draft.not_found", tools => I(tools, "flow_draft_add_scenario", Obj("""{"id":"visit.note_wait","scenario":{"name":"x"}}"""))),
        new("scenario on a draft that does not load", "authoring.scenario.draft_not_loaded", tools =>
        {
            _ = I(tools, "flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(document => document["body"]!["start"] = "nowhere") });
            return I(tools, "flow_draft_add_scenario", Obj("""{"id":"visit.note_wait","scenario":{"name":"x"}}"""));
        }),
        new("scenario with a name in use", "authoring.scenario.name_taken", tools =>
        {
            Save(tools);
            var existing = Obj(NoteWait)["body"]!["scenarios"]![0]!.DeepClone();
            return I(tools, "flow_draft_add_scenario", new JsonObject { ["id"] = "visit.note_wait", ["scenario"] = existing });
        }),
        new("scenario that does not fit the document", "authoring.scenario.invalid", tools =>
        {
            Save(tools);
            return I(tools, "flow_draft_add_scenario", Obj("""{"id":"visit.note_wait","scenario":{"name":"a case that goes nowhere","start":{"at":"2027-01-04T14:00:00Z","today":"2027-01-04","input":{"case_ref":"c-1"}},"events":[{"complete":"no_such_step","output":{}}],"expect":{"status":"Completed"}}}"""));
        }),
        new("scenario from a session that is gone", "authoring.simulation.not_found", tools =>
        {
            Save(tools);
            return I(tools, "flow_draft_add_scenario", Obj("""{"id":"visit.note_wait","fromSimulation":{"session":"sim-99","name":"gone"}}"""));
        }),
        new("scenario from a session of a draft that changed", "authoring.simulation.draft_changed", tools =>
        {
            Save(tools);
            var session = StartCase(tools);
            _ = I(tools, "flow_draft_patch", Obj("""{"id":"visit.note_wait","operations":[{"op":"replace","path":"/version","value":5}]}"""));
            return I(tools, "flow_draft_add_scenario", new JsonObject { ["id"] = "visit.note_wait", ["fromSimulation"] = new JsonObject { ["session"] = session, ["name"] = "after a change" } });
        }),
        new("simulate with neither start nor session", "authoring.simulation.target", tools => I(tools, "flow_draft_simulate", new JsonObject())),
        new("simulate with both start and session", "authoring.simulation.target", tools => I(tools, "flow_draft_simulate", Obj("""{"id":"visit.note_wait","start":{},"session":"sim-1"}"""))),
        new("simulate start without an id", "authoring.simulation.target", tools => I(tools, "flow_draft_simulate", Obj("""{"start":{}}"""))),
        new("simulate close without a session", "authoring.simulation.target", tools => I(tools, "flow_draft_simulate", Obj("""{"close":true}"""))),
        new("simulate on a missing draft", "authoring.draft.not_found", tools => I(tools, "flow_draft_simulate", Obj("""{"id":"visit.note_wait","start":{}}"""))),
        new("simulate on a draft that does not load", "authoring.simulation.draft_not_loaded", tools =>
        {
            _ = I(tools, "flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(document => document["body"]!["start"] = "nowhere") });
            return I(tools, "flow_draft_simulate", Obj("""{"id":"visit.note_wait","start":{}}"""));
        }),
        new("simulate with a start that cannot be read", "authoring.simulation.start_invalid", tools =>
        {
            Save(tools);
            return I(tools, "flow_draft_simulate", Obj("""{"id":"visit.note_wait","start":{"at":"yesterday-ish"}}"""));
        }),
        new("simulate beyond the limit", "authoring.simulation.limit", tools =>
        {
            Save(tools);
            _ = StartCase(tools);
            return I(tools, "flow_draft_simulate", Obj("""{"id":"visit.note_wait","start":{"at":"2027-01-04T14:00:00Z","input":{"case_ref":"c-2"}}}"""));
        }, Options: new AuthoringOptions { MaxSimulations = 1 }),
        new("simulate a session that is unknown", "authoring.simulation.not_found", tools => I(tools, "flow_draft_simulate", Obj("""{"session":"sim-404","command":{"type":"advance","duration":"PT1H"}}"""))),
        new("simulate a command that fails", "authoring.simulation.command_failed", tools =>
        {
            Save(tools);
            var session = StartCase(tools);
            return I(tools, "flow_draft_simulate", new JsonObject { ["session"] = session, ["command"] = Obj("""{"type":"complete","effect":"no_such_effect","output":{}}""") });
        }),
        new("rule_draft_edit without edits that read", "authoring.rule.edit_invalid", tools => I(tools, "rule_draft_edit", Obj("""{"id":"visit.note_ready","edits":[{"op":"clearCondition","row":"signed"}]}"""))),
        new("rule_draft_edit with an operation that is unknown", "authoring.input.invalid", tools => I(tools, "rule_draft_edit", Obj("""{"id":"visit.note_ready","edits":[{"op":"explode"}]}"""))),
        new("rule_draft_edit on a rule that is unknown", "authoring.rule.not_found", tools => I(tools, "rule_draft_edit", Obj("""{"id":"nothing.here","edits":[]}"""))),
        new("rule_draft_edit that cannot be applied", "authoring.rule.edit_failed", tools => I(tools, "rule_draft_edit", Obj("""{"id":"visit.note_ready","edits":[{"op":"removeRow","row":"no-such-row"}]}"""))),
        new("rule_simulate on a rule that is unknown", "authoring.rule.not_found", tools => I(tools, "rule_simulate", Obj("""{"id":"nothing.here","facts":{}}"""))),
        new("rule_simulate with a fact that is unknown", "authoring.rule.facts_invalid", tools => I(tools, "rule_simulate", Obj("""{"id":"visit.note_ready","facts":{"colour":"red"}}"""))),
        new("rule_simulate with a date that is not a date", "authoring.rule.facts_invalid", tools => I(tools, "rule_simulate", Obj("""{"id":"visit.note_ready","facts":{"status":"signed"},"asOf":"tomorrow"}"""))),
        new("rule_simulate with a draft that is missing", "authoring.draft.not_found", tools => I(tools, "rule_simulate", Obj("""{"id":"visit.note_ready","facts":{"status":"signed"},"draft":true}"""))),
        new("rule_test on a rule that is unknown", "authoring.rule.not_found", tools => I(tools, "rule_test", Obj("""{"id":"nothing.here"}"""))),
        new("rule_test of a draft that is missing", "authoring.draft.not_found", tools => I(tools, "rule_test", Obj("""{"id":"visit.note_ready","draft":true}"""))),
        new("flow_propose without a draft", "authoring.draft.not_found", tools => I(tools, "flow_propose", Obj("""{"id":"visit.note_wait","summary":"s","requestedBy":"Front desk lead"}"""))),
        new("flow_propose of a draft that is not ready", "authoring.proposal.not_ready", tools =>
        {
            _ = I(tools, "flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(document => document["body"]!["start"] = "nowhere") });
            return I(tools, "flow_propose", Obj("""{"id":"visit.note_wait","summary":"s","requestedBy":"Front desk lead"}"""));
        }),
        new("rule_propose without a draft", "authoring.draft.not_found", tools => I(tools, "rule_propose", Obj("""{"id":"visit.note_ready","summary":"s","requestedBy":"Dr. Reviewer"}"""))),
        new("rule_propose of a draft whose tests fail", "authoring.proposal.not_ready", tools =>
        {
            _ = I(tools, "rule_draft_edit", Obj("""{"id":"visit.note_ready","edits":[{"op":"removeRow","row":"signed"}]}"""));
            return I(tools, "rule_propose", Obj("""{"id":"visit.note_ready","summary":"s","requestedBy":"Dr. Reviewer"}"""));
        }),
        new("rule_propose of a draft whose base moved", "authoring.proposal.base_stale", tools =>
        {
            _ = I(tools, "rule_draft_edit", Obj("""{"id":"visit.note_ready","edits":[{"op":"setRowLabel","row":"signed","locale":"en","text":"The note is signed"}]}"""));
            currentMoving!.Moved = true;
            return I(tools, "rule_propose", Obj("""{"id":"visit.note_ready","summary":"s","requestedBy":"Dr. Reviewer"}"""));
        }),
        new("proposal_get of a name that is not there", "authoring.proposal.not_found", tools => I(tools, "proposal_get", Obj("""{"name":"flow.visit.note_wait.v9"}"""))),
        new("proposal_get of a file that is not a proposal", "authoring.proposal.unreadable", tools => I(tools, "proposal_get", Obj("""{"name":"stray"}"""))),
    ];

    [Fact]
    public void There_are_seventeen_tools_in_the_order_of_the_specification()
    {
        var (tools, _, _) = Rig();
        tools.All.Select(tool => tool.Name).Should().Equal(ToolNames);
        tools.Find("flow_get")!.Name.Should().Be("flow_get");
        tools.Find("nope").Should().BeNull();
    }

    [Fact]
    public void Every_input_schema_is_a_valid_schema_and_every_description_states_what_the_tool_never_does()
    {
        var (tools, _, _) = Rig();
        foreach (var tool in tools.All)
        {
            tool.InputSchema["$schema"]!.GetValue<string>().Should().Be("https://json-schema.org/draft/2020-12/schema");
            tool.InputSchema["type"]!.GetValue<string>().Should().Be("object");
            tool.InputSchema["additionalProperties"]!.GetValue<bool>().Should().BeFalse(tool.Name);
            tool.InputSchema["properties"]!["language"].Should().NotBeNull(tool.Name);
            var meta = MetaSchemas.Draft202012.Evaluate(System.Text.Json.JsonSerializer.SerializeToElement(tool.InputSchema), new EvaluationOptions { OutputFormat = OutputFormat.List });
            meta.IsValid.Should().BeTrue(tool.Name + " is not a valid JSON Schema");
            tool.Description.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Tools_that_take_free_text_carry_the_warning_against_patient_text()
    {
        var (tools, _, _) = Rig();
        foreach (var name in new[] { "flow_propose", "rule_propose", "flow_draft_simulate", "flow_draft_add_scenario" })
        {
            tools.Find(name)!.InputSchema.ToJsonString().Should().Contain("never type text that identifies a patient", name);
        }

        foreach (var tool in tools.All)
        {
            tool.Description.Should().NotBeNullOrWhiteSpace();
        }

        tools.Find("flow_draft_simulate")!.Description.Should().Contain("made-up");
        tools.Find("flow_draft_simulate")!.Description.Should().Contain("Real cases, real patients and live ports are never reached");
        tools.Find("brain_catalog")!.Description.Should().ContainEquivalentOf("definitions");
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Every_refusal_is_a_coded_answer_with_a_hint_and_a_sentence_in_each_language(string name)
    {
        var item = Cases.Single(candidate => candidate.Name == name);
        foreach (var language in new[] { "en", "es" })
        {
            var (tools, _, _) = RigFor(item);
            currentLanguage = language;
            JsonObject answer;
            try
            {
                answer = item.Act(tools) ?? new JsonObject();
            }
            finally
            {
                currentLanguage = null;
            }

            IsOk(answer).Should().BeFalse(name + ": " + answer.ToJsonString());
            ErrorCode(answer).Should().Be(item.Code, name + ": " + answer.ToJsonString());
            answer["error"]!["hint"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
            var say = answer["say"]!.GetValue<string>();
            say.Should().NotBeNullOrWhiteSpace();
            say.Should().NotContain("{", "no placeholder is left in a sentence");

        }
    }

    [Fact]
    public void The_sentence_of_a_refusal_follows_the_asked_language()
    {
        var (tools, _, _) = Rig();
        var en = tools.Invoke("flow_get", Obj("""{"id":"nothing.here","language":"en"}"""))["say"]!.GetValue<string>();
        var es = tools.Invoke("flow_get", Obj("""{"id":"nothing.here","language":"es"}"""))["say"]!.GetValue<string>();
        en.Should().NotBe(es);
        es.Should().Contain("proceso");
    }

    [Fact]
    public void A_tool_never_throws_whatever_the_input_is()
    {
        var (tools, _, _) = Rig();
        var inputs = new JsonNode?[] { null, new JsonObject(), JsonValue.Create(5), new JsonArray(), Obj("""{"id":5}"""), Obj("""{"id":null}"""), Obj("""{"operations":"x"}"""), Obj("""{"command":[]}""") };
        foreach (var tool in tools.All)
        {
            foreach (var input in inputs)
            {
                var act = () => tool.Invoke(input?.DeepClone());
                act.Should().NotThrow(tool.Name);
            }
        }
    }

    [Fact]
    public void Reading_tools_succeed_and_say_what_they_found()
    {
        var (tools, _, _) = Rig();
        foreach (var name in new[] { "brain_catalog", "flow_list", "rule_list", "proposal_list" })
        {
            var answer = tools.Invoke(name, new JsonObject());
            IsOk(answer).Should().BeTrue(name + ": " + answer.ToJsonString());
            answer["say"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        }

        var guide = tools.Invoke("brain_guide", new JsonObject());
        IsOk(guide).Should().BeTrue();
        var section = tools.Invoke("brain_guide", Obj("""{"section":"routes"}"""));
        IsOk(section).Should().BeTrue();
        section.ToJsonString().Should().Contain("route");

        var rules = tools.Invoke("rule_list", new JsonObject());
        rules["rules"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).Should().Contain(["visit.note_ready", "intake-routing"]);
        var rule = tools.Invoke("rule_get", Obj("""{"id":"visit.note_ready","language":"es"}"""));
        IsOk(rule).Should().BeTrue(rule.ToJsonString());
        rule["document"].Should().NotBeNull();
        var flow = tools.Invoke("flow_get", Obj("""{"id":"visit.note_wait"}"""));
        IsOk(flow).Should().BeTrue();
        flow["document"].Should().NotBeNull();
        flow["description"].Should().NotBeNull();
        flow["diagram"].Should().NotBeNull();
    }

    [Fact]
    public void A_rule_is_edited_simulated_tested_and_proposed_and_a_wider_change_needs_review()
    {
        var (tools, store, _) = Rig();
        var edited = tools.Invoke("rule_draft_edit", Obj("""{"id":"visit.note_ready","edits":[{"op":"setRowLabel","row":"signed","locale":"en","text":"The note is signed"}]}"""));
        IsOk(edited).Should().BeTrue(edited.ToJsonString());
        var draft = tools.Invoke("rule_get", Obj("""{"id":"visit.note_ready","draft":true}"""));
        IsOk(draft).Should().BeTrue();

        var simulated = tools.Invoke("rule_simulate", Obj("""{"id":"visit.note_ready","facts":{"status":"signed"},"draft":true}"""));
        IsOk(simulated).Should().BeTrue(simulated.ToJsonString());
        var tested = tools.Invoke("rule_test", Obj("""{"id":"visit.note_ready","draft":true}"""));
        IsOk(tested).Should().BeTrue(tested.ToJsonString());

        var proposed = tools.Invoke("rule_propose", Obj("""{"id":"visit.note_ready","summary":"Name the first row.","requestedBy":"Dr. Reviewer"}"""));
        IsOk(proposed).Should().BeTrue(proposed.ToJsonString());
        proposed["proposal"]!["needsReview"]!.GetValue<bool>().Should().BeFalse("a change of a label stays within the facts and outcomes of the rule");
        store.ProposalNames.Should().ContainSingle();

        var listed = tools.Invoke("proposal_list", new JsonObject());
        listed["proposals"]!.AsArray().Should().ContainSingle();
        var read = tools.Invoke("proposal_get", new JsonObject { ["name"] = store.ProposalNames.Single() });
        IsOk(read).Should().BeTrue(read.ToJsonString());
    }

    [Fact]
    public void A_simulation_can_be_closed_and_a_closed_session_is_gone()
    {
        var (tools, _, _) = Rig();
        Save(tools);
        var session = StartCase(tools);
        var closed = tools.Invoke("flow_draft_simulate", new JsonObject { ["session"] = session, ["close"] = true });
        IsOk(closed).Should().BeTrue();
        closed["closed"]!.GetValue<bool>().Should().BeTrue();
        ErrorCode(tools.Invoke("flow_draft_simulate", new JsonObject { ["session"] = session })).Should().Be("authoring.simulation.not_found");
    }

    [Fact]
    public void A_simulation_expires_after_the_lifetime_by_the_injected_clock_and_not_before()
    {
        var (tools, _, clock) = Rig(options: new AuthoringOptions { SimulationLifetime = TimeSpan.FromMinutes(10) });
        Save(tools);
        var session = StartCase(tools);

        clock.Advance(TimeSpan.FromMinutes(9));
        IsOk(tools.Invoke("flow_draft_simulate", new JsonObject { ["session"] = session })).Should().BeTrue("using a session renews it");
        clock.Advance(TimeSpan.FromMinutes(9));
        IsOk(tools.Invoke("flow_draft_simulate", new JsonObject { ["session"] = session })).Should().BeTrue();
        clock.Advance(TimeSpan.FromMinutes(11));
        ErrorCode(tools.Invoke("flow_draft_simulate", new JsonObject { ["session"] = session })).Should().Be("authoring.simulation.not_found");
    }

    [Fact]
    public void An_expired_session_frees_its_place()
    {
        var (tools, _, clock) = Rig(options: new AuthoringOptions { MaxSimulations = 1, SimulationLifetime = TimeSpan.FromMinutes(5) });
        Save(tools);
        _ = StartCase(tools);
        clock.Advance(TimeSpan.FromMinutes(6));
        IsOk(tools.Invoke("flow_draft_simulate", Obj("""{"id":"visit.note_wait","start":{"input":{"case_ref":"c-2"}}}"""))).Should().BeTrue();
    }

    [Fact]
    public void A_proposal_carries_the_time_of_the_injected_clock()
    {
        var (tools, store, clock) = Rig();
        Save(tools);
        clock.Advance(TimeSpan.FromHours(3));
        var proposed = tools.Invoke("flow_propose", Obj("""{"id":"visit.note_wait","summary":"Nothing changes.","requestedBy":"Front desk lead"}"""));
        if (!IsOk(proposed))
        {
            return;
        }

        store.ReadProposal(store.ProposalNames.Single())!.Should().Contain("2027-01-04T17:00:00");
    }

    [Fact]
    public void No_tool_changes_an_approved_document()
    {
        var (tools, store, _) = Rig();
        var before = (store.ReadApproved(DocumentKind.Flow, "visit.note_wait"), store.ReadApproved(DocumentKind.Rule, "visit.note_ready"), store.ReadCatalog());
        Save(tools);
        _ = tools.Invoke("flow_draft_patch", Obj("""{"id":"visit.note_wait","operations":[{"op":"replace","path":"/version","value":3}]}"""));
        _ = tools.Invoke("rule_draft_edit", Obj("""{"id":"visit.note_ready","edits":[{"op":"removeRow","row":"signed"}]}"""));
        (store.ReadApproved(DocumentKind.Flow, "visit.note_wait"), store.ReadApproved(DocumentKind.Rule, "visit.note_ready"), store.ReadCatalog()).Should().Be(before);
    }

    [Fact]
    public void An_id_that_walks_out_of_the_workspace_never_reaches_a_store()
    {
        var (tools, _, _) = Rig();
        foreach (var id in new[] { "../outside", "..", "a/b", "a\\b", "/etc/passwd", "C:\\x", "x\u0000y", "visit.note_wait/../../x" })
        {
            foreach (var name in new[] { "flow_get", "rule_get", "flow_draft_save", "flow_draft_patch", "flow_propose", "rule_draft_edit", "proposal_get" })
            {
                var answer = tools.Invoke(name, new JsonObject { ["id"] = id, ["name"] = id, ["document"] = new JsonObject(), ["operations"] = new JsonArray(), ["edits"] = new JsonArray(), ["summary"] = "s", ["requestedBy"] = "n" });
                IsOk(answer).Should().BeFalse(name + " " + id);
            }
        }
    }

    [Fact]
    public void A_store_that_fails_is_a_coded_refusal()
    {
        var failing = new FailingStore(NoteStore());
        var tools = new AuthoringTools(failing, new AuthoringOptions { Clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Start) });
        var answer = tools.Invoke("flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(_ => { }) });
        ErrorCode(answer).Should().Be("authoring.workspace.store_failed");
        answer["say"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_rule_that_does_not_load_against_the_catalog_cannot_be_edited_tested_or_simulated()
    {
        var broken = Obj(NoteReady);
        broken["body"]!["kind"] = "visit.unknown_kind";
        var store = new InMemoryAuthoringStore(
            new Dictionary<string, string> { ["visit.note_wait"] = NoteWait },
            new Dictionary<string, string> { ["visit.note_ready"] = broken.ToJsonString() },
            CatalogJson.Serialize(NoteCatalog()));
        var tools = new AuthoringTools(store, new AuthoringOptions { Clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Start) });
        foreach (var (name, input) in new[]
        {
            ("rule_draft_edit", """{"id":"visit.note_ready","edits":[{"op":"removeRow","row":"signed"}]}"""),
            ("rule_test", """{"id":"visit.note_ready"}"""),
            ("rule_simulate", """{"id":"visit.note_ready","facts":{"status":"signed"}}"""),
        })
        {
            var answer = tools.Invoke(name, Obj(input));
            ErrorCode(answer).Should().Be("authoring.rule.not_loading", name + ": " + answer.ToJsonString());
        }
    }

    [Fact]
    public void A_catalog_that_names_nothing_the_draft_uses_refuses_the_draft_as_not_available_here()
    {
        var empty = new AuthoringCatalog { Ports = [], Events = [], Queues = [], ContractDocuments = [VisitContract] };
        var (tools, _, _) = Rig(empty);
        var saved = tools.Invoke("flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(_ => { }) });
        IsOk(saved).Should().BeTrue();
        saved["ready"]!.GetValue<bool>().Should().BeFalse();
        saved["catalogProblems"]!.AsArray().Should().NotBeEmpty();
        saved["say"]!.GetValue<string>().Should().Contain("not available here");
    }

    private static (AuthoringTools Tools, InMemoryAuthoringStore Store, Microsoft.Extensions.Time.Testing.FakeTimeProvider Clock) RigFor(Refusal item)
    {
        if (item.Name == "catalog that is not valid")
        {
            var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Start);
            var store = new InMemoryAuthoringStore(
                new Dictionary<string, string> { ["visit.note_wait"] = NoteWait },
                new Dictionary<string, string> { ["visit.note_ready"] = NoteReady },
                "{ this is not a catalog");
            return (new AuthoringTools(store, new AuthoringOptions { Clock = clock }), store, clock);
        }

        if (item.Name == "rule_propose of a draft whose base moved")
        {
            var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Start);
            var moving = new MovingStore(NoteStore());
            currentMoving = moving;
            return (new AuthoringTools(moving, new AuthoringOptions { Clock = clock }), null!, clock);
        }

        if (item.Name == "proposal_get of a file that is not a proposal")
        {
            var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Start);
            var store = NoteStore();
            _ = store.WriteProposal("stray", "{\"hello\":1}");
            return (new AuthoringTools(store, new AuthoringOptions { Clock = clock }), store, clock);
        }

        return Rig(options: item.Options);
    }

    [ThreadStatic]
    private static string? currentLanguage;

    [ThreadStatic]
    private static MovingStore? currentMoving;

    private static JsonObject I(AuthoringTools tools, string name, JsonObject input)
    {
        if (currentLanguage is not null && input["language"] is null)
        {
            input["language"] = currentLanguage;
        }

        return tools.Invoke(name, input);
    }

    private static void Save(AuthoringTools tools)
    {
        var saved = tools.Invoke("flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(_ => { }) });
        IsOk(saved).Should().BeTrue(saved.ToJsonString());
    }

    private static string StartCase(AuthoringTools tools)
    {
        var started = tools.Invoke("flow_draft_simulate", Obj("""{"id":"visit.note_wait","start":{"at":"2027-01-04T14:00:00Z","input":{"case_ref":"c-1"}}}"""));
        IsOk(started).Should().BeTrue(started.ToJsonString());
        return started["session"]!.GetValue<string>();
    }

    private sealed class MovingStore(IAuthoringStore inner) : IAuthoringStore
    {
        public bool Moved { get; set; }

        public IReadOnlyList<string> ListApproved(DocumentKind kind) => inner.ListApproved(kind);

        public string? ReadApproved(DocumentKind kind, string id)
        {
            var text = inner.ReadApproved(kind, id);
            if (!Moved || kind != DocumentKind.Rule || text is null)
            {
                return text;
            }

            var document = Obj(text);
            document["meta"]!["title"] = "Another title, approved meanwhile";
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

    private sealed class FailingStore(IAuthoringStore inner) : IAuthoringStore
    {
        public IReadOnlyList<string> ListApproved(DocumentKind kind) => inner.ListApproved(kind);

        public string? ReadApproved(DocumentKind kind, string id) => inner.ReadApproved(kind, id);

        public string? ReadCatalog() => inner.ReadCatalog();

        public IReadOnlyList<string> ListDrafts(DocumentKind kind) => inner.ListDrafts(kind);

        public StoredDraft? ReadDraft(DocumentKind kind, string id) => inner.ReadDraft(kind, id);

        public void WriteDraft(DocumentKind kind, string id, StoredDraft draft) => throw new AuthoringStoreException("The disk is full.");

        public IReadOnlyList<string> ListProposals() => inner.ListProposals();

        public string? ReadProposal(string name) => inner.ReadProposal(name);

        public string WriteProposal(string name, string json) => throw new AuthoringStoreException("The disk is full.");
    }
}

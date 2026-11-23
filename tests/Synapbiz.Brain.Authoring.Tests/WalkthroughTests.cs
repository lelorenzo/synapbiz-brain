using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit.Abstractions;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>The scripted conversation of the specification: list, get, save a broken draft, read the hints, fix, simulate, add a test, propose.</summary>
public sealed class WalkthroughTests(ITestOutputHelper output)
{
    [Fact]
    public void An_assistant_changes_a_process_from_request_to_proposal()
    {
        var (tools, store, _) = Rig();

        // Read before writing.
        var list = Call(tools, "flow_list");
        list["flows"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).Should().Equal("visit.note_wait");
        var get = Call(tools, "flow_get", Obj("""{"id":"visit.note_wait"}"""));
        IsOk(get).Should().BeTrue();
        var document = (JsonObject)get["document"]!;

        // A change with a mistake: a route to a step that does not exist, and a new version.
        document["version"] = 2;
        document["body"]!["steps"]!["await_note"]!["onExpired"] = "front_desk_call";
        var broken = Call(tools, "flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = document.DeepClone() });
        output.WriteLine(broken.ToJsonString(new() { WriteIndented = true }));
        IsOk(broken).Should().BeTrue();
        broken["loaded"]!.GetValue<bool>().Should().BeFalse();
        broken["ready"]!.GetValue<bool>().Should().BeFalse();
        var error = broken["errors"]!.AsArray()[0]!;
        error["hint"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        broken["say"]!.GetValue<string>().Should().Contain("does not accept");

        // Fix it with a patch: the route goes back to the step the person meant, and the confirmation task also shows the note status.
        var fixedUp = Call(tools, "flow_draft_patch", Obj("""
            {
              "id": "visit.note_wait",
              "operations": [
                { "op": "replace", "path": "/body/steps/await_note/onExpired", "value": "chase_note" },
                { "op": "add", "path": "/body/steps/confirm_note/show/-", "value": "note_status" }
              ]
            }
            """));
        output.WriteLine(fixedUp.ToJsonString(new() { WriteIndented = true }));
        fixedUp["loaded"]!.GetValue<bool>().Should().BeTrue();
        fixedUp["changes"]!["items"]!.AsArray().Should().NotBeEmpty();

        // Simulate the case the person described.
        var started = Call(tools, "flow_draft_simulate", Obj("""{"id":"visit.note_wait","start":{"input":{"case_ref":"c-1001"}}}"""));
        IsOk(started).Should().BeTrue(started.ToJsonString());
        var session = started["session"]!.GetValue<string>();
        var effect = started["outstanding"]!.AsArray()[0]!["id"]!.GetValue<string>();
        var answered = Call(tools, "flow_draft_simulate", new JsonObject { ["session"] = session, ["command"] = Obj("{\"type\":\"complete\",\"effect\":\"" + effect + "\",\"output\":{\"status\":\"signed\"}}") });
        output.WriteLine(answered.ToJsonString(new() { WriteIndented = true }));
        answered["status"]!.GetValue<string>().Should().Be("Completed");

        // Add the case as a test, then check readiness and propose.
        var added = Call(tools, "flow_draft_add_scenario", Obj("{\"id\":\"visit.note_wait\",\"fromSimulation\":{\"session\":\"" + session + "\",\"name\":\"a made-up visit whose note is signed at once\"}}"));
        output.WriteLine(added.ToJsonString(new() { WriteIndented = true }));
        IsOk(added).Should().BeTrue(added.ToJsonString());
        added["scenarioAdded"]!["passed"]!.GetValue<bool>().Should().BeTrue();
        output.WriteLine(added["readiness"]!.ToJsonString());
        added["ready"]!.GetValue<bool>().Should().BeTrue(added["say"]!.GetValue<string>());

        var proposed = Call(tools, "flow_propose", Obj("""{"id":"visit.note_wait","summary":"The front desk calls when the note is not signed in time.","requestedBy":"Front desk lead"}"""));
        output.WriteLine(proposed.ToJsonString(new() { WriteIndented = true }));
        IsOk(proposed).Should().BeTrue(proposed.ToJsonString());
        store.ProposalNames.Should().ContainSingle();
        proposed["proposal"]!["needsReview"]!.GetValue<bool>().Should().BeTrue();
        var read = Call(tools, "proposal_get", Obj("{\"name\":\"" + store.ProposalNames.Single() + "\"}"));
        IsOk(read).Should().BeTrue();
        read["text"]!.GetValue<string>().Should().Contain("Nothing changes until a person applies");
    }
}

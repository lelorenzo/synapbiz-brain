using System.Text.Json.Nodes;
using FluentAssertions;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>The two refusals the table of <see cref="ToolTests"/> does not reach, triggered through the tools.</summary>
public sealed class RefusalGapTests
{
    [Fact]
    public void A_simulation_that_cannot_be_written_as_a_test_is_refused_as_not_writable()
    {
        var (tools, _, _) = Rig();
        _ = Call(tools, "flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(_ => { }) });
        var started = Call(tools, "flow_draft_simulate", Obj("""{"id":"visit.note_wait","start":{"input":{"case_ref":"c-1001"}}}"""));
        IsOk(started).Should().BeTrue(started.ToJsonString());
        var session = started["session"]!.GetValue<string>();

        var answer = Call(tools, "flow_draft_add_scenario", new JsonObject { ["id"] = "visit.note_wait", ["fromSimulation"] = new JsonObject { ["session"] = session, ["name"] = string.Empty } });

        IsOk(answer).Should().BeFalse();
        ErrorCode(answer).Should().Be("authoring.scenario.not_writable");
        answer["error"]!["hint"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        answer["say"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        Call(tools, "proposal_list")["proposals"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public void A_draft_that_does_not_raise_the_version_is_not_ready_and_is_refused_as_not_ready()
    {
        var (tools, store, _) = Rig();
        var saved = Call(tools, "flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(_ => { }) });
        IsOk(saved).Should().BeTrue();
        saved["ready"]!.GetValue<bool>().Should().BeFalse("ready means proposable, and an unchanged version cannot be proposed");
        var answer = Call(tools, "flow_propose", Obj("""{"id":"visit.note_wait","summary":"No change of version.","requestedBy":"Front desk lead"}"""));
        IsOk(answer).Should().BeFalse();
        ErrorCode(answer).Should().Be("authoring.proposal.not_ready");
        answer["reasons"]!.AsArray().Select(reason => reason!.GetValue<string>()).Should().Contain(reason => reason.Contains("greater than the current one (1)", StringComparison.Ordinal));
        store.ProposalNames.Should().BeEmpty("a refused proposal writes nothing");
    }
}

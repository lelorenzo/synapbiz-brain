using System.Text.Json.Nodes;
using FluentAssertions;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Everything_answers()
    {
        var (tools, _, _) = Rig();
        foreach (var name in new[] { "brain_guide", "brain_catalog", "flow_list", "rule_list", "proposal_list" })
        {
            var answer = Call(tools, name);
            IsOk(answer).Should().BeTrue(answer.ToJsonString());
        }

        var flow = Call(tools, "flow_get", Obj("""{"id":"visit.note_wait","language":"es"}"""));
        IsOk(flow).Should().BeTrue(flow.ToJsonString());
        var rule = Call(tools, "rule_get", Obj("""{"id":"visit.note_ready"}"""));
        IsOk(rule).Should().BeTrue(rule.ToJsonString());
        var saved = Call(tools, "flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = Obj(NoteWait) });
        IsOk(saved).Should().BeTrue(saved.ToJsonString());
        saved["ready"]!.GetValue<bool>().Should().BeTrue(saved.ToJsonString());
    }
}

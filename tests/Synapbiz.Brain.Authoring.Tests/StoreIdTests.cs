using FluentAssertions;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>An id is one file on every file system: no name Windows reserves, and no two ids that differ only in capital letters.</summary>
public sealed class StoreIdTests
{
    [Theory]
    [InlineData("con")]
    [InlineData("CON")]
    [InlineData("nul.json")]
    [InlineData("Aux.v1")]
    [InlineData("com1")]
    [InlineData("LPT9.extra")]
    public void A_name_windows_reserves_is_not_an_id(string id)
    {
        StoreIds.IsValid(id).Should().BeFalse();
        var (tools, _, _) = Rig();
        var refused = tools.Invoke("flow_get", new System.Text.Json.Nodes.JsonObject { ["id"] = id });
        ErrorCode(refused).Should().Be("authoring.workspace.id_invalid");
    }

    [Theory]
    [InlineData("console")]
    [InlineData("com10")]
    [InlineData("visit.note_wait")]
    public void A_name_that_only_starts_like_a_reserved_one_is_fine(string id) => StoreIds.IsValid(id).Should().BeTrue();

    [Fact]
    public void A_draft_whose_id_differs_only_by_case_from_an_approved_process_is_refused_in_memory_and_on_disk()
    {
        var (tools, store, _) = Rig();
        var refused = tools.Invoke("flow_draft_save", new System.Text.Json.Nodes.JsonObject { ["id"] = "Visit.Note_Wait", ["document"] = NoteWaitChanged(document => document["id"] = "Visit.Note_Wait") });
        ErrorCode(refused).Should().Be("authoring.workspace.id_invalid");
        store.ListDrafts(DocumentKind.Flow).Should().BeEmpty();

        using var workspace = new Workspace();
        var disk = workspace.Store();
        var act = () => disk.WriteDraft(DocumentKind.Flow, "Visit.Note_Wait", new StoredDraft("{}", null, []));
        act.Should().Throw<ArgumentException>();
        disk.ListDrafts(DocumentKind.Flow).Should().BeEmpty();
        disk.ReadApproved(DocumentKind.Flow, "VISIT.NOTE_WAIT").Should().BeNull("an id is read only in the case it was written");
        disk.ReadApproved(DocumentKind.Flow, "visit.note_wait").Should().NotBeNull();
    }

    [Fact]
    public void A_proposal_name_that_differs_only_by_case_from_another_is_refused()
    {
        var store = new InMemoryAuthoringStore();
        _ = store.WriteProposal("process.a.v2", "{}");
        var act = () => store.WriteProposal("Process.A.v2", "{}");
        act.Should().Throw<ArgumentException>();
        using var workspace = new Workspace();
        var disk = workspace.Store();
        _ = disk.WriteProposal("process.a.v2", "{}");
        var again = () => disk.WriteProposal("PROCESS.a.v2", "{}");
        again.Should().Throw<ArgumentException>();
    }
}

using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>A workspace folder in the temporary area, removed afterwards.</summary>
public sealed class Workspace : IDisposable
{
    public Workspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "brain-authoring-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Path.Combine(Root, "flows"));
        _ = Directory.CreateDirectory(Path.Combine(Root, "rules"));
        File.WriteAllText(Path.Combine(Root, "flows", "visit.note_wait.json"), NoteWait);
        File.WriteAllText(Path.Combine(Root, "rules", "visit.note_ready.json"), NoteReady);
        File.WriteAllText(Path.Combine(Root, "rules", "intake-routing.json"), IntakeRule);
        File.WriteAllText(Path.Combine(Root, "catalog.json"), CatalogJson.Serialize(NoteCatalog()));
        Outside = Path.Combine(Path.GetTempPath(), "brain-authoring-outside-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Outside);
    }

    public string Root { get; }

    public string Outside { get; }

    public FileAuthoringStore Store() => new(Root);

    public static IReadOnlyDictionary<string, string> Snapshot(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(folder, path), File.ReadAllText);

    public void Dispose()
    {
        foreach (var folder in new[] { Root, Outside })
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}

public sealed class StoreTests : IDisposable
{
    private readonly Workspace workspace = new();

    public void Dispose() => workspace.Dispose();

    public static TheoryData<string> BadIds() => new() { "../x", "..", "a/b", "a\\b", "/etc/passwd", "C:\\x", "", " ", "x.", "x..y", ".hidden", "-lead", "name.draft", "name.tmp", new string('a', 121), "tab\tx", "nul\0" };

    [Theory]
    [MemberData(nameof(BadIds))]
    public void Refuses_every_unsafe_id_in_both_stores(string id)
    {
        StoreIds.IsValid(id).Should().BeFalse();
        var stores = new IAuthoringStore[] { workspace.Store(), new InMemoryAuthoringStore() };
        foreach (var store in stores)
        {
            var draft = new StoredDraft("{}", null, []);
            ((Action)(() => store.WriteDraft(DocumentKind.Flow, id, draft))).Should().Throw<ArgumentException>();
            ((Action)(() => store.ReadDraft(DocumentKind.Rule, id))).Should().Throw<ArgumentException>();
            ((Action)(() => store.ReadApproved(DocumentKind.Flow, id))).Should().Throw<ArgumentException>();
            ((Action)(() => store.WriteProposal(id, "{}"))).Should().Throw<ArgumentException>();
            ((Action)(() => store.ReadProposal(id))).Should().Throw<ArgumentException>();
        }

        Directory.GetFiles(workspace.Outside).Should().BeEmpty();
    }

    [Fact]
    public void Reads_the_approved_documents_and_the_catalog()
    {
        var store = workspace.Store();
        store.ListApproved(DocumentKind.Flow).Should().Equal("visit.note_wait");
        store.ListApproved(DocumentKind.Rule).Should().Equal("intake-routing", "visit.note_ready");
        store.ReadApproved(DocumentKind.Flow, "visit.note_wait").Should().Be(NoteWait);
        store.ReadApproved(DocumentKind.Flow, "missing").Should().BeNull();
        store.ReadCatalog().Should().NotBeNull();
    }

    [Fact]
    public void A_draft_round_trips_with_its_base_known_gaps_and_edits_in_both_stores()
    {
        var draft = new StoredDraft("{\n  \"id\": \"x\"\n}", new DraftBase("x", 3, "abc"), [new KnownGap(KnownGap.RouteUntested, "step_a", "onError", "never happens in the clinic")], "[{\"op\":\"removeRow\",\"row\":\"r\"}]");
        foreach (var store in new IAuthoringStore[] { workspace.Store(), new InMemoryAuthoringStore() })
        {
            store.ReadDraft(DocumentKind.Flow, "x").Should().BeNull();
            store.WriteDraft(DocumentKind.Flow, "x", draft);
            store.ReadDraft(DocumentKind.Flow, "x").Should().BeEquivalentTo(draft);
            store.ReadDraft(DocumentKind.Rule, "x").Should().BeNull();
            store.ListDrafts(DocumentKind.Flow).Should().Equal("x");
            store.WriteDraft(DocumentKind.Flow, "x", draft with { Text = "{}" });
            store.ReadDraft(DocumentKind.Flow, "x")!.Text.Should().Be("{}");
            store.ListDrafts(DocumentKind.Flow).Should().Equal("x");
        }
    }

    [Fact]
    public void A_proposal_is_written_once_and_the_location_is_a_relative_path()
    {
        var store = workspace.Store();
        store.WriteProposal("flow.x.v2", "{\"a\":1}").Should().Be("proposals/flow.x.v2.json");
        store.ReadProposal("flow.x.v2").Should().Be("{\"a\":1}");
        store.ListProposals().Should().Equal("flow.x.v2");
        ((Action)(() => store.WriteProposal("flow.x.v2", "{}"))).Should().Throw<AuthoringStoreException>();
        var memory = new InMemoryAuthoringStore();
        memory.WriteProposal("p", "{}").Should().Be("proposals/p.json");
        ((Action)(() => memory.WriteProposal("p", "{}"))).Should().Throw<AuthoringStoreException>();
    }

    [Fact]
    public void Writes_only_inside_drafts_and_proposals()
    {
        var store = workspace.Store();
        store.WriteDraft(DocumentKind.Flow, "x", new StoredDraft("{}", null, []));
        store.WriteDraft(DocumentKind.Rule, "y", new StoredDraft("{}", null, []));
        store.WriteProposal("p", "{}");
        var written = Workspace.Snapshot(workspace.Root).Keys.Select(key => key.Replace('\\', '/')).Where(key => key.StartsWith("drafts/", StringComparison.Ordinal) || key.StartsWith("proposals/", StringComparison.Ordinal));
        written.Should().HaveCount(5);
        Workspace.Snapshot(workspace.Root).Keys.Select(key => key.Replace('\\', '/')).Where(key => !key.StartsWith("drafts/", StringComparison.Ordinal) && !key.StartsWith("proposals/", StringComparison.Ordinal))
            .Should().BeEquivalentTo("catalog.json", "flows/visit.note_wait.json", "rules/visit.note_ready.json", "rules/intake-routing.json");
    }

    [Fact]
    public void A_drafts_folder_that_is_a_link_to_the_outside_is_refused()
    {
        Directory.CreateSymbolicLink(Path.Combine(workspace.Root, "drafts"), workspace.Outside);
        var store = workspace.Store();
        ((Action)(() => store.WriteDraft(DocumentKind.Flow, "x", new StoredDraft("{}", null, [])))).Should().Throw<AuthoringStoreException>();
        Directory.GetFileSystemEntries(workspace.Outside).Should().BeEmpty();
    }

    [Fact]
    public void A_proposals_folder_that_is_a_link_to_the_outside_is_refused()
    {
        Directory.CreateSymbolicLink(Path.Combine(workspace.Root, "proposals"), workspace.Outside);
        var store = workspace.Store();
        ((Action)(() => store.WriteProposal("p", "{}"))).Should().Throw<AuthoringStoreException>();
        Directory.GetFileSystemEntries(workspace.Outside).Should().BeEmpty();
    }

    [Fact]
    public void A_subfolder_of_drafts_that_is_a_link_to_the_outside_is_refused()
    {
        _ = Directory.CreateDirectory(Path.Combine(workspace.Root, "drafts"));
        Directory.CreateSymbolicLink(Path.Combine(workspace.Root, "drafts", "flows"), workspace.Outside);
        var store = workspace.Store();
        ((Action)(() => store.WriteDraft(DocumentKind.Flow, "x", new StoredDraft("{}", null, [])))).Should().Throw<AuthoringStoreException>();
        Directory.GetFileSystemEntries(workspace.Outside).Should().BeEmpty();
    }

    private static readonly string[] ApprovedFolders = ["flows", "rules"];

    private Dictionary<string, string> ApprovedSnapshot() =>
        ApprovedFolders.SelectMany(folder => Directory.GetFiles(Path.Combine(workspace.Root, folder)))
            .Append(Path.Combine(workspace.Root, "catalog.json"))
            .ToDictionary(path => Path.GetRelativePath(workspace.Root, path), File.ReadAllText);

    private void ApprovedFoldersStayUntouched(Action act)
    {
        var before = ApprovedSnapshot();
        act.Should().Throw<AuthoringStoreException>();
        ApprovedSnapshot().Should().BeEquivalentTo(before);
        Directory.GetFileSystemEntries(workspace.Outside).Should().BeEmpty();
    }

    [Fact]
    public void A_drafts_folder_that_is_a_link_to_the_workspace_root_is_refused()
    {
        Directory.CreateSymbolicLink(Path.Combine(workspace.Root, "drafts"), workspace.Root);
        var store = workspace.Store();
        ApprovedFoldersStayUntouched(() => store.WriteDraft(DocumentKind.Flow, "visit.note_wait", new StoredDraft("changed", null, [])));
        ApprovedFoldersStayUntouched(() => store.WriteDraft(DocumentKind.Rule, "x", new StoredDraft("changed", null, [])));
        File.Exists(Path.Combine(workspace.Root, "flows", "visit.note_wait.draft.json")).Should().BeFalse();
    }

    [Fact]
    public void A_drafts_folder_that_is_a_link_to_the_rules_folder_is_refused()
    {
        Directory.CreateSymbolicLink(Path.Combine(workspace.Root, "drafts"), Path.Combine(workspace.Root, "rules"));
        var store = workspace.Store();
        ApprovedFoldersStayUntouched(() => store.WriteDraft(DocumentKind.Rule, "x", new StoredDraft("changed", null, [])));
        Directory.GetFiles(Path.Combine(workspace.Root, "rules")).Should().HaveCount(2);
    }

    [Fact]
    public void A_proposals_folder_that_is_a_link_to_the_flows_folder_is_refused()
    {
        Directory.CreateSymbolicLink(Path.Combine(workspace.Root, "proposals"), Path.Combine(workspace.Root, "flows"));
        var store = workspace.Store();
        ApprovedFoldersStayUntouched(() => store.WriteProposal("p", "{}"));
        Directory.GetFiles(Path.Combine(workspace.Root, "flows")).Should().ContainSingle();
    }

    [Fact]
    public void A_drafts_subfolder_that_is_a_link_to_the_approved_folder_of_its_kind_is_refused()
    {
        _ = Directory.CreateDirectory(Path.Combine(workspace.Root, "drafts"));
        Directory.CreateSymbolicLink(Path.Combine(workspace.Root, "drafts", "flows"), Path.Combine(workspace.Root, "flows"));
        var store = workspace.Store();
        ApprovedFoldersStayUntouched(() => store.WriteDraft(DocumentKind.Flow, "x", new StoredDraft("changed", null, [])));
    }

    [Fact]
    public void A_draft_file_that_is_a_link_to_an_approved_file_is_refused()
    {
        _ = Directory.CreateDirectory(Path.Combine(workspace.Root, "drafts", "flows"));
        File.CreateSymbolicLink(Path.Combine(workspace.Root, "drafts", "flows", "x.json"), Path.Combine(workspace.Root, "flows", "visit.note_wait.json"));
        var store = workspace.Store();
        ApprovedFoldersStayUntouched(() => store.WriteDraft(DocumentKind.Flow, "x", new StoredDraft("changed", null, [])));
    }

    [Fact]
    public void A_dangling_link_at_the_target_is_refused()
    {
        _ = Directory.CreateDirectory(Path.Combine(workspace.Root, "proposals"));
        File.CreateSymbolicLink(Path.Combine(workspace.Root, "proposals", "p.json"), Path.Combine(workspace.Outside, "not-yet.json"));
        var store = workspace.Store();
        ApprovedFoldersStayUntouched(() => store.WriteProposal("p", "{}"));
    }

    [Fact]
    public void A_planted_temporary_file_link_is_not_written_through()
    {
        var target = Path.Combine(workspace.Outside, "victim.json");
        File.WriteAllText(target, "original");
        _ = Directory.CreateDirectory(Path.Combine(workspace.Root, "drafts", "flows"));
        _ = Directory.CreateDirectory(Path.Combine(workspace.Root, "proposals"));
        foreach (var planted in new[] { "drafts/flows/x.json.tmp", "drafts/flows/x.draft.json.tmp", "proposals/p.json.tmp" })
        {
            File.CreateSymbolicLink(Path.Combine(workspace.Root, planted), target);
        }

        var store = workspace.Store();
        store.WriteDraft(DocumentKind.Flow, "x", new StoredDraft("{}", null, []));
        _ = store.WriteProposal("p", "{}");
        File.ReadAllText(target).Should().Be("original");
        File.ReadAllText(Path.Combine(workspace.Root, "drafts", "flows", "x.json")).Should().Be("{}");
        new FileInfo(Path.Combine(workspace.Root, "drafts", "flows", "x.json")).LinkTarget.Should().BeNull();
        Directory.GetFiles(Path.Combine(workspace.Root, "drafts", "flows"), "*", SearchOption.TopDirectoryOnly).Where(path => !path.EndsWith(".tmp", StringComparison.Ordinal)).Should().HaveCount(2);
    }

    [Fact]
    public void The_temporary_files_are_removed_after_a_write()
    {
        var store = workspace.Store();
        store.WriteDraft(DocumentKind.Flow, "x", new StoredDraft("{}", null, []));
        _ = store.WriteProposal("p", "{}");
        Directory.GetFiles(workspace.Root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void Reading_through_a_drafts_folder_that_is_a_link_is_refused()
    {
        Directory.CreateSymbolicLink(Path.Combine(workspace.Root, "drafts"), Path.Combine(workspace.Root, "rules"));
        var store = workspace.Store();
        ((Action)(() => store.ListDrafts(DocumentKind.Rule))).Should().Throw<AuthoringStoreException>();
        ((Action)(() => store.ReadDraft(DocumentKind.Rule, "visit.note_ready"))).Should().Throw<AuthoringStoreException>();
    }

    [Fact]
    public void A_draft_file_that_is_a_link_to_a_file_outside_is_not_written_through()
    {
        var target = Path.Combine(workspace.Outside, "secret.json");
        File.WriteAllText(target, "original");
        _ = Directory.CreateDirectory(Path.Combine(workspace.Root, "drafts", "flows"));
        File.CreateSymbolicLink(Path.Combine(workspace.Root, "drafts", "flows", "x.json"), target);
        var store = workspace.Store();
        ((Action)(() => store.WriteDraft(DocumentKind.Flow, "x", new StoredDraft("changed", null, [])))).Should().Throw<AuthoringStoreException>();
        File.ReadAllText(target).Should().Be("original");
    }

    [Fact]
    public void An_approved_file_that_is_a_link_to_the_outside_is_not_read()
    {
        var target = Path.Combine(workspace.Outside, "secret.json");
        File.WriteAllText(target, "secret");
        File.CreateSymbolicLink(Path.Combine(workspace.Root, "flows", "leak.json"), target);
        var store = workspace.Store();
        ((Action)(() => store.ReadApproved(DocumentKind.Flow, "leak"))).Should().Throw<AuthoringStoreException>();
    }

    [Fact]
    public void The_workspace_folder_may_itself_be_reached_through_a_link()
    {
        var alias = Path.Combine(workspace.Outside, "alias");
        Directory.CreateSymbolicLink(alias, workspace.Root);
        var store = new FileAuthoringStore(alias);
        store.ReadApproved(DocumentKind.Flow, "visit.note_wait").Should().Be(NoteWait);
        store.WriteDraft(DocumentKind.Flow, "x", new StoredDraft("{}", null, []));
        File.Exists(Path.Combine(workspace.Root, "drafts", "flows", "x.json")).Should().BeTrue();
    }

    [Fact]
    public void The_folder_must_exist()
    {
        ((Action)(() => _ = new FileAuthoringStore(Path.Combine(workspace.Outside, "nothing")))).Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Approved_documents_are_byte_identical_after_any_sequence_of_tool_calls()
    {
        var random = new Sequence(20261123u);
        var tools = new AuthoringTools(workspace.Store(), new AuthoringOptions { Clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Start) });
        var before = Workspace.Snapshot(workspace.Root).Where(pair => !pair.Key.StartsWith("drafts", StringComparison.Ordinal) && !pair.Key.StartsWith("proposals", StringComparison.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value);
        var calls = new Func<JsonObject>[]
        {
            () => Obj("""{"id":"visit.note_wait"}"""),
            () => new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(document => document["version"] = random.Next(2, 9)) },
            () => new JsonObject { ["id"] = "visit.note_wait", ["document"] = "not json at all" },
            () => Obj("""{"id":"visit.note_wait","operations":[{"op":"replace","path":"/body/start","value":"nowhere"}]}"""),
            () => Obj("""{"id":"visit.note_ready","edits":[{"op":"removeRow","row":"signed"}]}"""),
            () => Obj("""{"id":"intake-routing","edits":[{"op":"setOutcome","row":"old-request","field":"priority","value":3}]}"""),
            () => Obj("""{"id":"visit.note_wait","start":{"input":{"case_ref":"c-1"}}}"""),
            () => Obj("""{"id":"visit.note_wait","summary":"s","requestedBy":"n"}"""),
            () => Obj("""{"id":"intake-routing","summary":"s","requestedBy":"n"}"""),
            () => Obj("""{"id":"visit.note_ready","facts":{"status":"signed"}}"""),
            () => Obj("""{"id":"visit.note_ready"}"""),
        };
        var names = new[] { "flow_get", "flow_draft_save", "flow_draft_save", "flow_draft_patch", "rule_draft_edit", "rule_draft_edit", "flow_draft_simulate", "flow_propose", "rule_propose", "rule_simulate", "rule_test" };
        for (var round = 0; round < 120; round++)
        {
            var pick = random.Next(names.Length);
            tools.Invoke(names[pick], calls[pick]());
        }

        var after = Workspace.Snapshot(workspace.Root).Where(pair => !pair.Key.StartsWith("drafts", StringComparison.Ordinal) && !pair.Key.StartsWith("proposals", StringComparison.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value);
        after.Should().BeEquivalentTo(before);
        foreach (var (key, value) in before)
        {
            Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(workspace.Root, key))).Should().Equal(Encoding.UTF8.GetBytes(value));
        }

        Directory.GetFileSystemEntries(workspace.Outside).Should().BeEmpty();
        File.Exists(Path.Combine(workspace.Root, "drafts", "flows", "visit.note_wait.json")).Should().BeTrue("the sequence did write drafts");
    }
}

/// <summary>A tiny fixed-seed sequence, so the randomized test replays the same calls on every run.</summary>
internal sealed class Sequence(uint seed)
{
    private uint state = seed;

    public int Next(int maxExclusive) => Next(0, maxExclusive);

    public int Next(int minInclusive, int maxExclusive)
    {
        state = unchecked((state * 1664525u) + 1013904223u);
        return minInclusive + (int)((state >> 8) % (uint)(maxExclusive - minInclusive));
    }
}

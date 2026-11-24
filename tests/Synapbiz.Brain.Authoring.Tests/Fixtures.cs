using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Synapbiz.Brain.Authoring;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>The workspaces and calls the tests share. Every document is a synthetic reference document of the repository.</summary>
internal static class Fixtures
{
    internal static readonly DateTimeOffset Start = new(2027, 1, 4, 14, 0, 0, TimeSpan.Zero);

    internal static string Locate(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("Could not locate the file above the test assembly.", relativePath);
    }

    internal static string Read(string relativePath) => File.ReadAllText(Locate(relativePath));

    internal static string NoteWait => Read("samples/process-reference/visit.note_wait/process.json");

    internal static string NoteReady => Read("samples/process-reference/visit.note_wait/note_ready.rule.json");

    internal static JsonObject VisitContract => (JsonObject)JsonNode.Parse(Read("samples/process-reference/visit.note_wait/contract.json"))!;

    internal static JsonObject IntakeContract => (JsonObject)JsonNode.Parse(Read("tests/fixtures/workbench/intake.contract.json"))!;

    internal static string IntakeRule => Read("tests/fixtures/workbench/intake.first.rule.json");

    /// <summary>The catalog of the note-wait workspace: two ports, two queues, no events, and the contract of the visit module.</summary>
    internal static AuthoringCatalog NoteCatalog() => new()
    {
        Ports =
        [
            new CatalogPort("records.note_status", "read", "atLeastOnce", false, "Reads the status of the note of a visit.", null, null),
            new CatalogPort("messages.send_reminder", "action", "either", true, "Sends a reminder to a person.", null, null),
        ],
        Events = [],
        Queues = [new CatalogName("note_followup", null), new CatalogName("note_confirmation", null)],
        ContractDocuments = [VisitContract, IntakeContract],
    };

    internal static InMemoryAuthoringStore NoteStore(AuthoringCatalog? catalog = null) => new(
        new Dictionary<string, string> { ["visit.note_wait"] = NoteWait },
        new Dictionary<string, string> { ["visit.note_ready"] = NoteReady, ["intake-routing"] = IntakeRule },
        CatalogJson.Serialize(catalog ?? NoteCatalog()));

    internal static (AuthoringTools Tools, InMemoryAuthoringStore Store, FakeTimeProvider Clock) Rig(AuthoringCatalog? catalog = null, AuthoringOptions? options = null)
    {
        var clock = new FakeTimeProvider(Start);
        var store = NoteStore(catalog);
        var tools = new AuthoringTools(store, (options ?? new AuthoringOptions()) with { Clock = clock });
        return (tools, store, clock);
    }

    internal static JsonObject Call(AuthoringTools tools, string name, JsonObject? input = null) => tools.Invoke(name, input ?? new JsonObject());

    internal static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    internal static bool IsOk(JsonObject answer) => answer["ok"]!.GetValue<bool>();

    internal static string ErrorCode(JsonObject answer) => answer["error"]!["code"]!.GetValue<string>();

    /// <summary>The approved note-wait document changed by a function on its parsed form.</summary>
    internal static JsonObject NoteWaitChanged(Action<JsonObject> change)
    {
        var document = Obj(NoteWait);
        change(document);
        return document;
    }
}

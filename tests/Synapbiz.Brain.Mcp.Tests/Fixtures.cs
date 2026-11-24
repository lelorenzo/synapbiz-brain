using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using Synapbiz.Brain.Authoring;

namespace Synapbiz.Brain.Mcp.Tests;

/// <summary>The workspaces and connections the tests share. Every document is a synthetic reference document of the repository.</summary>
internal static class Fixtures
{
    internal static readonly DateTimeOffset Start = new(2027, 1, 4, 14, 0, 0, TimeSpan.Zero);

    internal static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Synapbiz.Brain.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find Synapbiz.Brain.sln above the test assembly.");
    }

    internal static string Read(string relativePath) => File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));

    internal static string NoteWait => Read("samples/process-reference/visit.note_wait/process.json");

    internal static string NoteReady => Read("samples/process-reference/visit.note_wait/note_ready.rule.json");

    /// <summary>The catalog of the note-wait workspace: two ports, two queues, no events, and the contract of the visit module.</summary>
    internal static string CatalogText() => CatalogJson.Serialize(new AuthoringCatalog
    {
        Ports =
        [
            new CatalogPort("records.note_status", "read", "atLeastOnce", false, "Reads the status of the note of a visit.", null, null),
            new CatalogPort("messages.send_reminder", "action", "either", true, "Sends a reminder to a person.", null, null),
        ],
        Events = [],
        Queues = [new CatalogName("note_followup", null), new CatalogName("note_confirmation", null)],
        ContractDocuments = [(JsonObject)JsonNode.Parse(Read("samples/process-reference/visit.note_wait/contract.json"))!],
    });

    internal static InMemoryAuthoringStore NoteStore() => new(
        new Dictionary<string, string> { ["visit.note_wait"] = NoteWait },
        new Dictionary<string, string> { ["visit.note_ready"] = NoteReady },
        CatalogText());

    /// <summary>A folder with the approved documents and the catalog, made from the reference documents.</summary>
    internal static string MakeWorkspace(bool withCatalog = true)
    {
        var folder = Path.Combine(Path.GetTempPath(), "brain-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "flows"));
        Directory.CreateDirectory(Path.Combine(folder, "rules"));
        File.WriteAllText(Path.Combine(folder, "flows", "visit.note_wait.json"), NoteWait);
        File.WriteAllText(Path.Combine(folder, "rules", "visit.note_ready.json"), NoteReady);
        if (withCatalog)
        {
            File.WriteAllText(Path.Combine(folder, "catalog.json"), CatalogText());
        }

        return folder;
    }

    /// <summary>A server over the options and an SDK client connected to it in memory.</summary>
    internal static async Task<Connection> Connect(BrainMcpOptions options)
    {
        var brain = BrainMcp.Create(options);
        var (serverEnd, clientEnd) = MemoryTransport.Pair();
        var server = McpServer.Create(serverEnd, brain.ToServerOptions("test", "0.0.0"));
        var run = server.RunAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = await McpClient.CreateAsync(new MemoryClientTransport(clientEnd), cancellationToken: timeout.Token);
        return new Connection(client, server, run);
    }

    internal static BrainMcpOptions Options(FakeTimeProvider? clock = null, string language = "en", Func<BrainMcpRequest, CancellationToken, ValueTask<bool>>? authorize = null) => new()
    {
        Store = NoteStore(),
        Authoring = new AuthoringOptions { Clock = clock ?? new FakeTimeProvider(Start) },
        DefaultLanguage = language,
        Authorize = authorize,
    };

    internal sealed class Connection(McpClient client, McpServer server, Task run) : IAsyncDisposable
    {
        internal McpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await server.DisposeAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
    }
}

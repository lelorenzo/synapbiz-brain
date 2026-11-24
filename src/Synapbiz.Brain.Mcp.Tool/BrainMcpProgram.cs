using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Synapbiz.Brain.Authoring;

namespace Synapbiz.Brain.Mcp.Tool;

/// <summary>
/// The <c>brain-mcp</c> program: the Brain authoring tools over the Model Context Protocol on standard input and output, for a folder of
/// documents on an author's machine. Standard output carries the protocol and nothing else; every message for the operator goes to the
/// error stream, one line each.
/// </summary>
internal static class BrainMcpProgram
{
    /// <summary>The server ended normally: the client closed the connection.</summary>
    internal const int Success = 0;

    /// <summary>An unexpected failure while serving.</summary>
    internal const int Failure = 1;

    /// <summary>The arguments are wrong.</summary>
    internal const int Usage = 2;

    /// <summary>The workspace folder is missing or cannot be read.</summary>
    internal const int WorkspaceUnreadable = 3;

    /// <summary>The catalog file is missing, unreadable or not a valid <c>brain.catalog@1</c>.</summary>
    internal const int CatalogUnreadable = 4;

    private const string UsageText =
        "usage: brain-mcp --workspace <folder> [--catalog <file>] [--language en|es]\n"
        + "  --workspace <folder>  the folder of approved documents (flows/, rules/); drafts and proposals are written under it (required)\n"
        + "  --catalog <file>      a brain.catalog@1 file of what the host offers; without it the workspace's catalog.json is used if there is one\n"
        + "  --language en|es      the language of the answers when a call does not say (default en)\n"
        + "  --help, --version";

    /// <summary>Runs the program.</summary>
    /// <param name="args">The command line arguments.</param>
    /// <param name="input">The protocol input (standard input).</param>
    /// <param name="output">The protocol output (standard output).</param>
    /// <param name="error">Where the one-line messages for the operator go (the error stream).</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <returns>The exit code: 0, 1 (unexpected failure), 2 (usage), 3 (workspace) or 4 (catalog).</returns>
    internal static async Task<int> RunAsync(string[] args, Stream input, Stream output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var parsed = Parse(args);
        if (parsed.Problem is not null)
        {
            await error.WriteLineAsync("brain-mcp: " + parsed.Problem + " (try --help)").ConfigureAwait(false);
            return Usage;
        }

        if (parsed.Help)
        {
            await error.WriteLineAsync(UsageText).ConfigureAwait(false);
            return Success;
        }

        if (parsed.Version)
        {
            await error.WriteLineAsync("brain-mcp " + (typeof(BrainMcpProgram).Assembly.GetName().Version?.ToString(3) ?? "0.0.0")).ConfigureAwait(false);
            return Success;
        }

        IAuthoringStore store;
        try
        {
            store = Open(parsed.Workspace!, parsed.Catalog, out var catalogProblem);
            if (catalogProblem is not null)
            {
                await error.WriteLineAsync("brain-mcp: " + catalogProblem).ConfigureAwait(false);
                return CatalogUnreadable;
            }
        }
        catch (Exception problem) when (problem is DirectoryNotFoundException or IOException or UnauthorizedAccessException or AuthoringStoreException or ArgumentException)
        {
            await error.WriteLineAsync("brain-mcp: the workspace folder '" + parsed.Workspace + "' is missing or cannot be read.").ConfigureAwait(false);
            return WorkspaceUnreadable;
        }

        var brain = BrainMcp.Create(new BrainMcpOptions
        {
            Store = store,
            Authoring = new AuthoringOptions { Clock = TimeProvider.System },
            DefaultLanguage = parsed.Language,
        });
        try
        {
            await error.WriteLineAsync("brain-mcp: serving " + parsed.Workspace + " over standard input and output (language " + parsed.Language + ").").ConfigureAwait(false);
            var transport = new StreamServerTransport(input, output, "brain-mcp");
            await using (transport.ConfigureAwait(false))
            {
                var server = McpServer.Create(transport, brain.ToServerOptions("brain-mcp", typeof(BrainMcpProgram).Assembly.GetName().Version?.ToString(3)));
                await using (server.ConfigureAwait(false))
                {
                    await server.RunAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            return Success;
        }
        catch (OperationCanceledException)
        {
            return Success;
        }
        catch (Exception problem) when (problem is IOException or InvalidOperationException or McpException)
        {
            await error.WriteLineAsync("brain-mcp: the connection failed: " + problem.Message.ReplaceLineEndings(" ")).ConfigureAwait(false);
            return Failure;
        }
    }

    private static IAuthoringStore Open(string workspace, string? catalog, out string? catalogProblem)
    {
        catalogProblem = null;
        var files = new FileAuthoringStore(workspace);
        if (catalog is null)
        {
            return files;
        }

        string text;
        try
        {
            text = File.ReadAllText(catalog);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or ArgumentException)
        {
            catalogProblem = "the catalog file '" + catalog + "' is missing or cannot be read.";
            return files;
        }

        var result = CatalogJson.Parse(text);
        if (result.Catalog is null)
        {
            catalogProblem = "the catalog file '" + catalog + "' is not a valid brain.catalog@1: " + result.Errors[0].Message.ReplaceLineEndings(" ");
            return files;
        }

        return new CatalogOverride(files, text);
    }

    private static Arguments Parse(string[] args)
    {
        string? workspace = null;
        string? catalog = null;
        var language = "en";
        var pending = new Queue<string>(args);
        while (pending.Count > 0)
        {
            var name = pending.Dequeue();
            if (name is "--help" or "-h")
            {
                return Arguments.AskHelp;
            }

            if (name == "--version")
            {
                return Arguments.AskVersion;
            }

            if (name is not ("--workspace" or "--catalog" or "--language"))
            {
                return Arguments.Wrong("unknown argument '" + name + "'");
            }

            if (pending.Count == 0 || pending.Peek().StartsWith("--", StringComparison.Ordinal))
            {
                return Arguments.Wrong(name + " needs a value");
            }

            var value = pending.Dequeue();
            switch (name)
            {
                case "--workspace":
                    workspace = value;
                    break;
                case "--catalog":
                    catalog = value;
                    break;
                default:
                    if (value is not ("en" or "es"))
                    {
                        return Arguments.Wrong("--language must be en or es");
                    }

                    language = value;
                    break;
            }
        }

        return workspace is null ? Arguments.Wrong("--workspace <folder> is required") : new Arguments(workspace, catalog, language, null, false, false);
    }

    private sealed record Arguments(string? Workspace, string? Catalog, string Language, string? Problem, bool Help, bool Version)
    {
        internal static Arguments AskHelp { get; } = new(null, null, "en", null, true, false);

        internal static Arguments AskVersion { get; } = new(null, null, "en", null, false, true);

        internal static Arguments Wrong(string problem) => new(null, null, "en", problem, false, false);
    }

    /// <summary>The workspace with the catalog the operator named in place of the workspace's own; everything else is the folder store.</summary>
    private sealed class CatalogOverride(IAuthoringStore inner, string catalog) : IAuthoringStore
    {
        public IReadOnlyList<string> ListApproved(DocumentKind kind) => inner.ListApproved(kind);

        public string? ReadApproved(DocumentKind kind, string id) => inner.ReadApproved(kind, id);

        public string? ReadCatalog() => catalog;

        public IReadOnlyList<string> ListDrafts(DocumentKind kind) => inner.ListDrafts(kind);

        public StoredDraft? ReadDraft(DocumentKind kind, string id) => inner.ReadDraft(kind, id);

        public void WriteDraft(DocumentKind kind, string id, StoredDraft draft) => inner.WriteDraft(kind, id, draft);

        public IReadOnlyList<string> ListProposals() => inner.ListProposals();

        public string? ReadProposal(string name) => inner.ReadProposal(name);

        public string WriteProposal(string name, string json) => inner.WriteProposal(name, json);
    }
}

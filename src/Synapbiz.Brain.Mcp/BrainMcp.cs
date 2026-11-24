using ModelContextProtocol.Server;
using Synapbiz.Brain.Authoring;

namespace Synapbiz.Brain.Mcp;

/// <summary>
/// The Brain authoring tools, the guide resources and the prompt as MCP primitives, built once over one workspace. A host that does not use
/// <c>WithBrainAuthoring</c> takes the lists and registers them itself (for example with
/// <c>McpServerOptions.ToolCollection</c>). Simulation sessions live in the tools, so one set serves every connection of the host.
/// </summary>
public sealed class BrainMcp
{
    private BrainMcp(IReadOnlyList<McpServerTool> tools, IReadOnlyList<McpServerResource> resources, IReadOnlyList<McpServerPrompt> prompts)
    {
        Tools = tools;
        Resources = resources;
        Prompts = prompts;
    }

    /// <summary>One MCP tool for each authoring tool, in the order of the specification.</summary>
    public IReadOnlyList<McpServerTool> Tools { get; }

    /// <summary>One resource for each section of the guide, <c>brain://guide/&lt;section&gt;</c>.</summary>
    public IReadOnlyList<McpServerResource> Resources { get; }

    /// <summary>The prompt <c>model_a_process_with_me</c>.</summary>
    public IReadOnlyList<McpServerPrompt> Prompts { get; }

    /// <summary>Builds the set over the host's workspace and settings.</summary>
    /// <param name="options">What the host supplies; its store is required.</param>
    public static BrainMcp Create(BrainMcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Store);
        if (options.DefaultLanguage is not ("en" or "es"))
        {
            throw new ArgumentException("DefaultLanguage must be en or es.", nameof(options));
        }

        var authoring = new AuthoringTools(options.Store, options.Authoring);
        return new BrainMcp(
            [.. authoring.All.Select(tool => (McpServerTool)new BrainMcpTool(tool, options))],
            [.. BrainGuide.Sections.Select(section => (McpServerResource)new BrainMcpResource(section, options))],
            [new BrainMcpPrompt(options)]);
    }

    /// <summary>
    /// The server options the stdio program and a test use: the three collections and the instructions, without a builder or a host.
    /// </summary>
    /// <param name="serverName">The name the server reports.</param>
    /// <param name="serverVersion">The version the server reports.</param>
    public McpServerOptions ToServerOptions(string serverName = "synapbiz-brain", string? serverVersion = null) => new()
    {
        ServerInfo = new ModelContextProtocol.Protocol.Implementation { Name = serverName, Version = serverVersion ?? typeof(BrainMcp).Assembly.GetName().Version?.ToString() ?? "0.0.0" },
        ServerInstructions = Instructions,
        ToolCollection = [.. Tools],
        ResourceCollection = [.. Resources],
        PromptCollection = [.. Prompts],
    };

    /// <summary>What the server tells a client when it connects.</summary>
    internal const string Instructions =
        "Brain authoring: read the guide first (brain_guide, or the resources brain://guide/<section>), then model and refine processes and rules as drafts. "
        + "Brain judges every draft; you propose and never activate. Use the prompt model_a_process_with_me to set the working method.";
}

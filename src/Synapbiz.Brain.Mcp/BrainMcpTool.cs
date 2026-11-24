using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Synapbiz.Brain.Authoring;

namespace Synapbiz.Brain.Mcp;

/// <summary>One authoring tool as one MCP tool: same name, the English description, the JSON Schema of the tool as the input schema.</summary>
internal sealed class BrainMcpTool : McpServerTool
{
    /// <summary>The tools that only read: they never write a draft or a proposal.</summary>
    private static readonly HashSet<string> ReadOnly = new(StringComparer.Ordinal)
    {
        "brain_guide", "brain_catalog", "flow_list", "flow_get", "rule_list", "rule_get", "rule_simulate", "rule_test", "proposal_list", "proposal_get",
    };

    private readonly AuthoringTool tool;
    private readonly BrainMcpOptions options;

    internal BrainMcpTool(AuthoringTool tool, BrainMcpOptions options)
    {
        this.tool = tool;
        this.options = options;
        ProtocolTool = new Tool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = JsonSerializer.SerializeToElement(tool.InputSchema),
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = ReadOnly.Contains(tool.Name),
                DestructiveHint = false,
                IdempotentHint = false,
                OpenWorldHint = false,
            },
        };
    }

    /// <inheritdoc />
    public override Tool ProtocolTool { get; }

    /// <inheritdoc />
    public override IReadOnlyList<object> Metadata { get; } = [];

    /// <inheritdoc />
    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var arguments = new JsonObject();
        foreach (var (name, value) in request.Params?.Arguments ?? new Dictionary<string, JsonElement>())
        {
            arguments[name] = JsonSerializer.SerializeToNode(value);
        }

        if (!arguments.ContainsKey("language"))
        {
            arguments["language"] = options.DefaultLanguage;
        }

        var allowed = options.Authorize is null || await options.Authorize(new BrainMcpRequest("tool", tool.Name, request.User), cancellationToken).ConfigureAwait(false);
        var answer = allowed ? tool.Invoke(arguments) : BrainMcpAnswers.NotAuthorized(Language(arguments), "tool", tool.Name);
        return BrainMcpAnswers.ToResult(answer);
    }

    private static string Language(JsonObject arguments) => (string?)arguments["language"] == "es" ? "es" : "en";
}

/// <summary>How an answer of a tool, or of the adapter itself, becomes the result of an MCP tool call.</summary>
internal static class BrainMcpAnswers
{
    /// <summary>
    /// The result: the <c>say</c> text first, then the whole answer as JSON text, and the same answer as structured content.
    /// A refusal (<c>ok</c> false) is a normal result with <c>isError</c> true, never a protocol error.
    /// </summary>
    internal static CallToolResult ToResult(JsonObject answer)
    {
        var say = (string?)answer["say"] ?? string.Empty;
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = say }, new TextContentBlock { Text = answer.ToJsonString() }],
            StructuredContent = JsonSerializer.SerializeToElement(answer),
            IsError = (bool?)answer["ok"] != true,
        };
    }

    /// <summary>The answer when the host's authorization says no: the same shape as a refusal of a tool.</summary>
    internal static JsonObject NotAuthorized(string language, string kind, string name) => new()
    {
        ["ok"] = false,
        ["error"] = new JsonObject
        {
            ["code"] = "mcp.not_authorized",
            ["hint"] = "The host did not allow this " + kind + " (" + name + ") for this connection. Do not retry it; tell the person it is not available to them here.",
        },
        ["say"] = language == "es" ? "No tienes permiso para hacer esto aquí." : "You are not allowed to do this here.",
    };
}

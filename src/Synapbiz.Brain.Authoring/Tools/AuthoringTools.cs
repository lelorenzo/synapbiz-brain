using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Authoring;

/// <summary>
/// The whole set of tools, over one workspace. Each tool is a plain function from JSON to JSON with a JSON Schema for its input; a transport
/// (the MCP adapter, a test) lists <see cref="All"/> and calls <see cref="Invoke(string, JsonNode?)"/>. The set holds the simulation sessions in memory;
/// it reads and writes the workspace only through the <see cref="IAuthoringStore"/> it is given, which can never write outside <c>drafts</c> and <c>proposals</c>.
/// No tool changes an approved document, activates a process, starts a real case or calls a port.
/// </summary>
public sealed class AuthoringTools
{
    private static readonly string[] Order =
    [
        "brain_guide", "brain_catalog", "flow_list", "flow_get", "rule_list", "rule_get", "flow_draft_save", "flow_draft_patch", "flow_draft_simulate",
        "flow_draft_add_scenario", "rule_draft_edit", "rule_simulate", "rule_test", "flow_propose", "rule_propose", "proposal_list", "proposal_get",
    ];

    private readonly Dictionary<string, AuthoringTool> byName;

    /// <summary>Creates the tools over a workspace.</summary>
    /// <param name="store">The workspace.</param>
    /// <param name="options">Settings; the defaults work.</param>
    public AuthoringTools(IAuthoringStore store, AuthoringOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var context = new ToolContext(store, options ?? new AuthoringOptions());
        var made = GuideTools.Create(context)
            .Concat(FlowTools.Create(context))
            .Concat(RuleTools.Create(context))
            .Concat(SimulationTools.Create(context))
            .Concat(ProposalTools.Create(context))
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        All = [.. Order.Select(name => made[name])];
        byName = All.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
    }

    /// <summary>Every tool, in the order of the specification.</summary>
    public IReadOnlyList<AuthoringTool> All { get; }

    /// <summary>The tool with the name; null when there is none.</summary>
    /// <param name="name">The tool name.</param>
    public AuthoringTool? Find(string name) => byName.GetValueOrDefault(name);

    /// <summary>Runs the tool with the name. An unknown name is a coded refusal, never an exception.</summary>
    /// <param name="name">The tool name.</param>
    /// <param name="input">The input object.</param>
    public JsonObject Invoke(string name, JsonNode? input) =>
        Find(name)?.Invoke(input) ?? Results.Refuse(AuthoringJson.Text((input as JsonObject)?["language"]) == "es" ? Synapbiz.Brain.Workbench.WorkbenchLocale.Es : Synapbiz.Brain.Workbench.WorkbenchLocale.En, "tool.unknown", [("tool", name)], "There is no tool with this name. The tools are: " + string.Join(", ", All.Select(tool => tool.Name)) + ".");
}

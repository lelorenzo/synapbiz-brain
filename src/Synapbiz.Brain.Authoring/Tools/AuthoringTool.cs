using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>
/// One tool: a name, a description for the assistant, the JSON Schema of its input and a function from JSON to JSON. A call never throws:
/// an input the schema refuses, an unknown id or a draft that is not ready comes back as <c>{ "ok": false, "error": { "code": ... }, "say": ... }</c>.
/// </summary>
public sealed class AuthoringTool
{
    private readonly Func<ToolContext, JsonObject, WorkbenchLocale, JsonObject> run;
    private readonly ToolContext context;
    private readonly JsonSchema schema;

    internal AuthoringTool(string name, string description, JsonObject inputSchema, ToolContext context, Func<ToolContext, JsonObject, WorkbenchLocale, JsonObject> run)
    {
        Name = name;
        Description = description;
        InputSchema = inputSchema;
        this.context = context;
        this.run = run;
        schema = JsonSchema.FromText(inputSchema.ToJsonString());
    }

    /// <summary>The tool name, for example <c>flow_draft_save</c>.</summary>
    public string Name { get; }

    /// <summary>What the tool does and what it never does, written for the assistant.</summary>
    public string Description { get; }

    /// <summary>The JSON Schema (draft 2020-12) of the input object.</summary>
    public JsonObject InputSchema { get; }

    /// <summary>Runs the tool. Never throws for any input; every refusal is a coded answer.</summary>
    /// <param name="input">The input object; null counts as an empty object.</param>
    public JsonObject Invoke(JsonNode? input)
    {
        var arguments = input ?? new JsonObject();
        var language = AuthoringJson.Locale((arguments as JsonObject)?["language"]) ?? WorkbenchLocale.En;
        var problems = Validate(arguments);
        if (problems.Count > 0)
        {
            return Results.Refuse(language, "input.invalid", [("tool", Name), ("detail", problems[0])], "Fix the input so it matches the input schema of the tool: " + string.Join(" ", problems.Take(5)));
        }

        try
        {
            return run(context, (JsonObject)arguments, language);
        }
        catch (AuthoringStoreException error)
        {
            return Results.Refuse(language, "workspace.store_failed", [("detail", error.Message)], "The workspace refused the request: " + error.Message);
        }
        catch (ArgumentException error)
        {
            return Results.Refuse(language, "workspace.id_invalid", [("detail", error.Message)], "An id is not valid: " + error.Message);
        }
    }

    private List<string> Validate(JsonNode arguments)
    {
        var result = schema.Evaluate(JsonSerializer.SerializeToElement(arguments), new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid)
        {
            return [];
        }

        var found = new List<string>();
        foreach (var detail in (result.Details ?? []).Where(item => !item.IsValid && item.Errors is not null))
        {
            var where = detail.InstanceLocation.ToString();
            foreach (var message in detail.Errors!.Values)
            {
                found.Add((where.Length == 0 ? "The input" : where) + ": " + message);
            }
        }

        return found.Count == 0 ? ["The input does not match the schema."] : found;
    }
}

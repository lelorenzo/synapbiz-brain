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

    /// <summary>Runs the tool. Never throws for any input; every refusal is a coded answer. Calls are served one at a time, so a call always sees the whole result of the one before it.</summary>
    /// <param name="input">The input object; null counts as an empty object.</param>
    public JsonObject Invoke(JsonNode? input)
    {
        var arguments = input ?? new JsonObject();
        var language = AuthoringJson.Locale((arguments as JsonObject)?["language"]) ?? WorkbenchLocale.En;
        lock (context.Gate)
        {
            try
            {
                var problems = Validate(arguments, language);
                if (problems.Count > 0)
                {
                    return Results.Refuse(
                        language,
                        "input.invalid",
                        [("say", problems[0].Sentence)],
                        "Fix the input so it matches the input schema of the tool: " + string.Join(" ", problems.Take(5).Select(problem => problem.Pointer + " " + problem.Message)),
                        new JsonObject { ["tool"] = Name, ["problems"] = new JsonArray([.. problems.Take(10).Select(problem => (JsonNode?)new JsonObject { ["pointer"] = problem.Pointer, ["rule"] = problem.Rule, ["detail"] = problem.Message })]) });
                }

                return run(context, (JsonObject)arguments, language);
            }
            catch (AuthoringStoreException error)
            {
                return Results.Refuse(language, "workspace.store_failed", [], "The workspace refused the request: " + error.Message, new JsonObject { ["detail"] = error.Message });
            }
            catch (ArgumentException error)
            {
                return Results.Refuse(language, "workspace.id_invalid", [], "An id is not valid: " + error.Message, new JsonObject { ["detail"] = error.Message });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return Results.Refuse(language, "workspace.store_failed", [], "The workspace could not be read or written: " + error.GetType().Name + ".", new JsonObject { ["detail"] = error.GetType().Name });
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException && error.Message.Contains("depth", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Refuse(language, "input.too_deep", [], "A document is nested more deeply than the protocol allows. Flatten it.", new JsonObject { ["detail"] = error.GetType().Name });
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException and not InsufficientExecutionStackException)
            {
                return Results.Refuse(language, "tool.failed", [], "The tool failed unexpectedly (" + error.GetType().Name + "). Try again; if it persists, report it to the host.", new JsonObject { ["detail"] = error.GetType().Name });
            }
        }
    }

    private List<InputWords.InputProblem> Validate(JsonNode arguments, WorkbenchLocale language)
    {
        JsonElement element;
        try
        {
            element = JsonSerializer.SerializeToElement(arguments);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return [new InputWords.InputProblem(AuthoringText.Text(language, "input.too_deep"), string.Empty, "depth", error.GetType().Name)];
        }

        var result = schema.Evaluate(element, new EvaluationOptions { OutputFormat = OutputFormat.List });
        return result.IsValid ? [] : InputWords.Read(result, InputSchema, arguments, language);
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>
/// Says in words what is wrong with the input of a tool call: the wrong or missing member, never the text of the schema validator. The
/// validator only decides whether the input is valid and where it is not; the sentence comes from the string table and the names of the members.
/// The validator's own messages go to the assistant in the data of the refusal.
/// </summary>
internal static class InputWords
{
    /// <summary>One thing wrong with the input.</summary>
    /// <param name="Sentence">The sentence for the person.</param>
    /// <param name="Pointer">The JSON Pointer of the member in the input, for the assistant.</param>
    /// <param name="Rule">The rule of the schema that failed, for the assistant.</param>
    /// <param name="Message">The validator's own message, for the assistant.</param>
    internal sealed record InputProblem(string Sentence, string Pointer, string Rule, string Message);

    // For each type of simulation command, the members that make sense with it.
    private static readonly Dictionary<string, string[]> CommandMembers = new(StringComparer.Ordinal)
    {
        ["complete"] = ["effect", "output"],
        ["fail"] = ["effect", "code", "permanent"],
        ["task"] = ["task", "outcome", "data"],
        ["event"] = ["name", "correlation", "payload"],
        ["advance"] = ["duration"],
        ["child"] = ["child", "result"],
        ["childFail"] = ["child", "code"],
        ["cancel"] = [],
        ["resume"] = [],
    };

    internal static List<InputProblem> Read(EvaluationResults result, JsonObject schema, JsonNode arguments, WorkbenchLocale locale)
    {
        var found = new List<InputProblem>();
        foreach (var detail in (result.Details ?? []).Where(item => !item.IsValid && item.Errors is not null))
        {
            var pointer = detail.InstanceLocation.ToString();
            var path = detail.EvaluationPath.ToString();
            foreach (var (keyword, message) in detail.Errors!)
            {
                var name = path.EndsWith("/additionalProperties", StringComparison.Ordinal) ? "additionalProperties" : keyword;
                found.Add(new InputProblem(Describe(name, pointer, path, schema, arguments, locale), pointer, name, message));
            }
        }

        return found.Count == 0 ? [new InputProblem(AuthoringText.Text(locale, "input.generic"), string.Empty, "schema", "The input does not match the schema.")] : found;
    }

    private static string Describe(string keyword, string pointer, string evaluationPath, JsonObject schema, JsonNode arguments, WorkbenchLocale locale)
    {
        string T(string key, params (string, string)[] values) => AuthoringText.Text(locale, key, values);
        var parts = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Unescape).ToList();
        var member = parts.Count == 0 ? string.Empty : parts[^1];
        var container = parts.Count > 1 ? parts[^2] : string.Empty;
        var of = container.Length == 0 ? string.Empty : T("input.of", ("container", Quote(container)));
        var node = Schema(schema, evaluationPath);
        switch (keyword)
        {
            case "additionalProperties":
                var allowed = Allowed(schema, evaluationPath, pointer, arguments);
                return allowed.Count == 0
                    ? T("input.unknown.none", ("member", Quote(member)), ("of", of))
                    : T("input.unknown", ("member", Quote(member)), ("of", of), ("allowed", AuthoringText.Join(locale, [.. allowed.Select(Quote)])));
            case "required":
                var missing = Missing(node, arguments, pointer);
                var target = parts.Count == 0 ? string.Empty : T("input.of", ("container", Quote(member)));
                return T("input.missing", ("member", AuthoringText.Join(locale, [.. missing.Select(Quote)])), ("of", target));
            case "type":
                if (parts.Count == 0)
                {
                    return T("input.notobject");
                }

                return T("input.type", ("member", Quote(member)), ("kind", KindWords(node, locale)));
            case "enum":
                var values = (node?["enum"] as JsonArray)?.Select(item => AuthoringJson.Text(item)).OfType<string>().ToList() ?? [];
                return T("input.value", ("member", Quote(member)), ("allowed", AuthoringText.Join(locale, [.. values.Select(Quote)], "join.or")));
            case "pattern":
                return T("input.pattern", ("member", Quote(member)));
            case "maxLength":
                return T("input.long", ("member", Quote(member)), ("max", AuthoringText.Number(Whole(node, "maxLength"))));
            case "minLength":
                return T("input.empty", ("member", Quote(member)));
            case "maxItems":
                return T("input.many", ("member", Quote(member)), ("max", AuthoringText.Number(Whole(node, "maxItems"))));
            case "minimum" or "maximum":
                return T("input.range", ("member", Quote(member)), ("min", AuthoringText.Number(Whole(node, "minimum"))), ("max", AuthoringText.Number(Whole(node, "maximum"))));
            default:
                return member.Length == 0 ? T("input.generic") : T("input.member", ("member", Quote(member)));
        }
    }

    private static string Quote(string name) => FlowChangeDescriber.Quote(name);

    private static string Unescape(string token) => token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

    private static long Whole(JsonObject? node, string name) => AuthoringJson.Whole(node?[name]) ?? 0;

    // The schema at an evaluation path such as /properties/command/properties/type.
    private static JsonObject? Schema(JsonObject root, string evaluationPath)
    {
        JsonNode? current = root;
        var tokens = evaluationPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Unescape).ToList();
        for (var index = 0; index < tokens.Count && current is not null; index++)
        {
            current = current is JsonObject owner ? owner[tokens[index]] : null;
        }

        return current as JsonObject;
    }

    private static List<string> Allowed(JsonObject schema, string evaluationPath, string pointer, JsonNode arguments)
    {
        var tokens = evaluationPath.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count > 0)
        {
            tokens.RemoveAt(tokens.Count - 1);
        }

        var container = Schema(schema, string.Concat(tokens.Select(token => "/" + token)));
        var names = (container?["properties"] as JsonObject)?.Select(pair => pair.Key).Where(name => name != "language").ToList() ?? [];
        var parts = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Unescape).ToList();
        if (parts is ["command", _] && Walk(arguments, ["command", "type"]) is { } type && CommandMembers.TryGetValue(type, out var relevant))
        {
            return [.. relevant];
        }

        return names;
    }

    private static string? Walk(JsonNode? node, string[] path)
    {
        foreach (var name in path)
        {
            node = (node as JsonObject)?[name];
        }

        return AuthoringJson.Text(node);
    }

    private static List<string> Missing(JsonObject? node, JsonNode arguments, string pointer)
    {
        var required = (node?["required"] as JsonArray)?.Select(item => AuthoringJson.Text(item)).OfType<string>().ToList() ?? [];
        JsonNode? at = arguments;
        foreach (var token in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Unescape))
        {
            at = (at as JsonObject)?[token];
        }

        return [.. required.Where(name => (at as JsonObject)?.ContainsKey(name) != true)];
    }

    private static string KindWords(JsonObject? node, WorkbenchLocale locale)
    {
        var type = node?["type"] is JsonArray list ? AuthoringJson.Text(list.FirstOrDefault()) : AuthoringJson.Text(node?["type"]);
        return AuthoringText.Text(locale, type switch
        {
            "string" => "input.kind.text",
            "integer" => "input.kind.whole",
            "number" => "input.kind.number",
            "boolean" => "input.kind.flag",
            "array" => "input.kind.list",
            "object" => "input.kind.set",
            _ => "input.kind.value",
        });
    }
}

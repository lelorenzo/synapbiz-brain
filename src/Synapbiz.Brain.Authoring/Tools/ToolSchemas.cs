using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Authoring;

/// <summary>Builders for the JSON Schemas of the tool inputs, so every schema is written the same way.</summary>
internal static class ToolSchemas
{
    /// <summary>The warning every free-text field carries (rule 3 of the specification).</summary>
    internal const string NoPatientText = "Write definitions and made-up examples only; never type text that identifies a patient (names, birth dates, phone numbers, record numbers, anything copied from a chart).";

    internal static JsonObject Object(IEnumerable<(string Name, JsonObject Schema)> properties, params string[] required)
    {
        var members = new JsonObject();
        foreach (var (name, schema) in properties)
        {
            members[name] = schema;
        }

        members["language"] = Language();
        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["properties"] = members,
            ["additionalProperties"] = false,
        };
        if (required.Length > 0)
        {
            root["required"] = AuthoringJson.Array(required);
        }

        return root;
    }

    internal static JsonObject Language() => new()
    {
        ["type"] = "string",
        ["enum"] = AuthoringJson.Array(["en", "es"]),
        ["description"] = "The language of the `say` texts: en (default) or es.",
    };

    internal static JsonObject Text(string description, int maxLength = 2000) => new()
    {
        ["type"] = "string",
        ["description"] = description,
        ["maxLength"] = maxLength,
    };

    internal static JsonObject FreeText(string description, int maxLength = 2000) => Text(description + " " + NoPatientText, maxLength);

    internal static JsonObject Id(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description,
        ["pattern"] = "^[A-Za-z0-9_][A-Za-z0-9_.-]*$",
        ["maxLength"] = 120,
    };

    internal static JsonObject Flag(string description) => new() { ["type"] = "boolean", ["description"] = description };

    internal static JsonObject Whole(string description, int minimum, int maximum) => new()
    {
        ["type"] = "integer",
        ["description"] = description,
        ["minimum"] = minimum,
        ["maximum"] = maximum,
    };

    internal static JsonObject Enum(string description, params string[] values) => new()
    {
        ["type"] = "string",
        ["description"] = description,
        ["enum"] = AuthoringJson.Array(values),
    };

    internal static JsonObject AnyObject(string description) => new() { ["type"] = "object", ["description"] = description };

    internal static JsonObject Items(string description, JsonObject item, int maxItems = 200) => new()
    {
        ["type"] = "array",
        ["description"] = description,
        ["items"] = item,
        ["maxItems"] = maxItems,
    };

    internal static JsonObject DocumentArgument(string description) => new()
    {
        ["description"] = description,
        ["type"] = AuthoringJson.Array(["object", "string"]),
    };

    internal static JsonObject KnownGapsArgument() => Items(
        "What the author accepts knowingly, kept beside the draft and never inside the document: a route no test takes, or a wait without a timeout, each with the reason. Replaces the list kept so far; leave it out to keep that list.",
        Object(
            [
                ("check", Enum("What is excused.", KnownGap.RouteUntested, KnownGap.WaitWithoutTimeout)),
                ("step", Text("The step id.", 200)),
                ("route", Text("For route_untested, the route label (for example onError); leave out for wait_without_timeout.", 200)),
                ("reason", FreeText("Why it is accepted, one sentence.", 500)),
            ],
            "check",
            "step",
            "reason").WithoutLanguage());

    private static JsonObject WithoutLanguage(this JsonObject schema)
    {
        schema["properties"]!.AsObject().Remove("language");
        schema.Remove("$schema");
        return schema;
    }
}

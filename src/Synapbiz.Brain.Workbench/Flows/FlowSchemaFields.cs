using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>One member of the object an <c>ai</c> step must answer with.</summary>
/// <param name="Name">The member name.</param>
/// <param name="Type">The JSON type the schema asks for (<c>string</c>, <c>integer</c>, <c>number</c>, <c>boolean</c>, <c>array</c>, <c>object</c>), several joined with <c>/</c> when the schema allows more than one, or <c>any</c> when it says none.</param>
/// <param name="IsRequired">True when the schema lists the member as required.</param>
public sealed record FlowSchemaField(string Name, string Type, bool IsRequired);

/// <summary>Reads the members an output schema asks for, so that a screen can say what a step must answer in a list and not in schema text.</summary>
public static class FlowSchemaFields
{
    /// <summary>The members of the top level of a schema, in ordinal order of their names; empty for a schema that is null, not JSON or has no <c>properties</c>.</summary>
    /// <param name="schemaJson">The schema, for example <see cref="FlowAiView.OutputSchemaJson"/>.</param>
    public static IReadOnlyList<FlowSchemaField> From(string? schemaJson)
    {
        if (FlowJson.TryObject(schemaJson) is not { } schema || schema["properties"] is not JsonObject properties)
        {
            return new ReadOnlyCollection<FlowSchemaField>([]);
        }

        var required = (schema["required"] as JsonArray ?? []).Select(item => item is JsonValue value && value.TryGetValue<string>(out var name) ? name : null).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var fields = properties.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new FlowSchemaField(pair.Key, TypeOf(pair.Value), required.Contains(pair.Key)))
            .ToList();
        return new ReadOnlyCollection<FlowSchemaField>(fields);
    }

    private static string TypeOf(JsonNode? property)
    {
        var type = (property as JsonObject)?["type"];
        if (type is JsonValue value && value.TryGetValue<string>(out var single))
        {
            return single;
        }

        if (type is JsonArray many)
        {
            var names = many.Select(item => item is JsonValue entry && entry.TryGetValue<string>(out var name) ? name : null).OfType<string>().Where(name => name != "null").ToList();
            if (names.Count > 0)
            {
                return string.Join('/', names);
            }
        }

        return "any";
    }
}

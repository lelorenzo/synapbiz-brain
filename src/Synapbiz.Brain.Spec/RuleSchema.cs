using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Synapbiz.Brain.Spec;

/// <summary>The embedded <c>brain.rule@1</c> JSON Schema, built once on first use.</summary>
internal static class RuleSchema
{
    internal const string Format = "brain.rule@1";

    private const string ResourceName = "Synapbiz.Brain.Spec.Schemas.brain.rule.v1.json";
    private static readonly Lazy<JsonSchema> Schema = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The distinct JSON Pointers of the values that violate the schema, in ordinal order.</summary>
    internal static IReadOnlyList<string> Violations(JsonNode? document)
    {
        var result = Schema.Value.Evaluate(
            JsonSerializer.SerializeToElement(document),
            new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid)
        {
            return [];
        }

        return [.. (result.Details ?? []).Append(result)
            .Where(detail => !detail.IsValid && detail.Errors?.Count > 0)
            .Select(detail => detail.InstanceLocation.ToString())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static JsonSchema Build()
    {
        using var stream = typeof(RuleSchema).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded schema {ResourceName}.");
        using var document = JsonDocument.Parse(stream);
        return JsonSchema.FromText(
            document.RootElement.GetRawText(),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Synapbiz.Brain.Process;

/// <summary>The embedded <c>brain.process@1</c> JSON Schema, built once on first use.</summary>
internal static class ProcessSchema
{
    internal const string Format = "brain.process@1";

    private const string ResourceName = "Synapbiz.Brain.Process.Schemas.brain.process.v1.json";
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
            .Where(detail => !detail.IsValid && detail.Errors?.Count > 0 && !IsCondition(detail))
            .Select(detail => detail.InstanceLocation.ToString())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// True for a result produced while evaluating the <c>if</c> of a conditional: its failures only select a branch and are
    /// not violations of the document.
    /// </summary>
    private static bool IsCondition(EvaluationResults detail) =>
        detail.EvaluationPath.ToString().Contains("/if/", StringComparison.Ordinal);

    private static JsonSchema Build()
    {
        using var stream = typeof(ProcessSchema).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded schema {ResourceName}.");
        using var document = JsonDocument.Parse(stream);
        return JsonSchema.FromText(
            document.RootElement.GetRawText(),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
    }
}

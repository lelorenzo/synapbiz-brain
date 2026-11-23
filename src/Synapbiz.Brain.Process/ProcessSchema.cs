using System.Globalization;
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
        EvaluationResults result;
        try
        {
            result = Evaluate(document);
        }
        catch (FormatException)
        {
            // The evaluation compares numbers as decimals, for example when it checks that the items of an array are unique, and
            // throws for a literal that does not fit one (1e400, thirty digits). Such a number is evaluated as the largest
            // magnitude a decimal holds, with its sign, so that it is judged by the same keywords as any other large number and
            // reported at its own path.
            result = Evaluate(WithRepresentableNumbers(document));
        }

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

    private static EvaluationResults Evaluate(JsonNode? document) =>
        Schema.Value.Evaluate(
            JsonSerializer.SerializeToElement(document),
            new EvaluationOptions { OutputFormat = OutputFormat.List });

    // A copy of the document in which every number that is not a decimal is replaced by the largest magnitude of its sign (or by zero
    // when it is too close to zero to be one), so that the evaluation can compare it. The structure, and so every path, is unchanged.
    private static JsonNode? WithRepresentableNumbers(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject members:
                var copy = new JsonObject();
                foreach (var (name, value) in members)
                {
                    copy[name] = WithRepresentableNumbers(value);
                }

                return copy;
            case JsonArray items:
                return new JsonArray([.. items.Select(WithRepresentableNumbers)]);
            case JsonValue value when value.GetValueKind() == JsonValueKind.Number && !JsonSerializer.SerializeToElement(value).TryGetDecimal(out _):
                var magnitude = double.Parse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                if (Math.Abs(magnitude) < 1)
                {
                    return JsonValue.Create(0m);
                }

                return JsonValue.Create(magnitude < 0 ? decimal.MinValue : decimal.MaxValue);
            default:
                return node?.DeepClone();
        }
    }

    /// <summary>
    /// True for a result produced while evaluating the <c>if</c> of a conditional: its failures only select a branch and are
    /// not violations of the document.
    /// </summary>
    private static bool IsCondition(EvaluationResults detail)
    {
        var path = detail.EvaluationPath.ToString();
        return path.Contains("/if/", StringComparison.Ordinal) || path.EndsWith("/if", StringComparison.Ordinal);
    }

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

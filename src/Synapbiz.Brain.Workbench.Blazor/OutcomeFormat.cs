using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>One field of an outcome: the label of the field and its value in words.</summary>
internal sealed record OutcomeField(string Label, string Text);

/// <summary>Writes the outcome JSON of a row, a result or an expectation in words, the way the rule view writes it.</summary>
internal static class OutcomeFormat
{
    private static readonly JsonSerializerOptions RawJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>One group of fields for an object and one per element for an array (a <c>collect</c> outcome).</summary>
    internal static List<List<OutcomeField>> Groups(RuleView view, RuleLabels labels, WorkbenchLocale locale, string outcomeJson)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(outcomeJson);
        }
        catch (JsonException)
        {
            return [];
        }

        return node switch
        {
            JsonObject single => [Fields(view, labels, locale, single)],
            JsonArray many => [.. many.OfType<JsonObject>().Select(item => Fields(view, labels, locale, item))],
            _ => [],
        };
    }

    /// <summary>The values of every group in words: the fields of a group joined by commas, the groups by semicolons.</summary>
    internal static string Summary(IReadOnlyList<List<OutcomeField>> groups) =>
        string.Join("; ", groups.Select(group => string.Join(", ", group.Select(field => field.Text))));

    private static List<OutcomeField> Fields(RuleView view, RuleLabels labels, WorkbenchLocale locale, JsonObject outcome)
    {
        var declared = view.OutcomeFields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        var names = view.OutcomeFields.Select(field => field.Name).Where(outcome.ContainsKey)
            .Concat(outcome.Select(member => member.Key).Where(key => !declared.ContainsKey(key)));

        var fields = new List<OutcomeField>();
        foreach (var name in names)
        {
            if (outcome[name] is { } value)
            {
                var found = declared.TryGetValue(name, out var field);
                fields.Add(new OutcomeField(
                    found ? field!.Label : WorkbenchText.Humanize(name),
                    ValueText(name, found ? field!.Type : null, value, labels, locale)));
            }
        }

        return fields;
    }

    private static string ValueText(string field, FactType? type, JsonNode node, RuleLabels labels, WorkbenchLocale locale)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                if (labels.Values.TryGetValue(field, out var byValue) && byValue.TryGetValue(text, out var label))
                {
                    return label;
                }

                return type == FactType.Enum ? WorkbenchText.Humanize(text) : text;
            case JsonValue value when value.GetValueKind() is JsonValueKind.True or JsonValueKind.False:
                return WorkbenchText.FormatValue(DecisionValue.Boolean(value.GetValueKind() == JsonValueKind.True), field, locale);
            case JsonObject money when money["amount"] is { } amount && money["currency"] is { } currency:
                return Raw(amount) + " " + Raw(currency);
            default:
                return node.ToJsonString(RawJson);
        }
    }

    private static string Raw(JsonNode node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToJsonString(RawJson);

    /// <summary>The text of a fact set in words: the label of each fact and its value, in the order the kind declares them.</summary>
    internal static string FactsText(
        Synapbiz.Brain.Spec.BrainRule rule, RuleView view, RuleLabels labels, WorkbenchLocale locale, IReadOnlyDictionary<string, DecisionValue> facts)
    {
        var parts = new List<string>();
        for (var index = 0; index < rule.Kind.Facts.Count; index++)
        {
            var name = rule.Kind.Facts[index].Name;
            if (rule.Kind.Facts[index].Type != FactType.Reference && facts.TryGetValue(name, out var value))
            {
                parts.Add(view.Facts[index].Label + " " + WorkbenchText.FormatValue(value, name, locale, labels));
            }
        }

        return string.Join(", ", parts);
    }

    /// <summary>The number as an invariant text; used for counts that go into a template.</summary>
    internal static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}

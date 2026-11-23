using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Workbench;

/// <summary>Turns a loaded rule into a view a person can read: facts, rows, conditions and one sentence per row.</summary>
public static class RuleDescriber
{
    private static readonly JsonSerializerOptions RawJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Describes a rule in the given locale.</summary>
    /// <param name="rule">The loaded rule.</param>
    /// <param name="locale">The language of the sentences.</param>
    /// <param name="labels">Display names to use; humanised names when null or when a label is missing.</param>
    public static RuleView Describe(BrainRule rule, WorkbenchLocale locale, RuleLabels? labels = null)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var names = labels ?? RuleLabels.Empty;
        var facts = rule.Kind.Facts.Select(fact => new FactView(
            fact.Name,
            FactLabel(fact.Name, names),
            fact.Type,
            fact.Nullable,
            [.. (fact.AllowedValues ?? []).Select(value => new ValueOption(value, names.ValueLabel(fact.Name, value) ?? WorkbenchText.Humanize(value)))])).ToList();
        var outcomeFields = rule.Kind.OutcomeFields
            .Select(field => new OutcomeFieldView(field.Name, FieldLabel(field.Name, names), field.Type, field.Nullable)).ToList();

        var rows = rule.Table.Rows.Select((row, index) => DescribeRow(rule, row, index, false, locale, names)).ToList();
        var defaultRow = rule.Table.DefaultRow is { } fallback ? DescribeRow(rule, fallback, rows.Count, true, locale, names) : null;

        return new RuleView(
            rule.Id,
            rule.Version,
            names.Title ?? rule.Id,
            rule.Kind.Key.Value,
            rule.Table.HitPolicy,
            facts,
            outcomeFields,
            rows,
            defaultRow);
    }

    private static string FactLabel(string name, RuleLabels labels) =>
        labels.Facts.TryGetValue(name, out var label) ? label : WorkbenchText.Humanize(name);

    private static string FieldLabel(string name, RuleLabels labels) =>
        labels.OutcomeFields.TryGetValue(name, out var label) ? label : WorkbenchText.Humanize(name);

    private static RowView DescribeRow(BrainRule rule, Row row, int index, bool isDefault, WorkbenchLocale locale, RuleLabels labels)
    {
        var rowId = rule.RowIdsByKey.TryGetValue(row.Key, out var id) ? id : row.Key;
        var parts = row.Conditions.Select((condition, position) => DescribeCondition(condition, position, locale, labels)).ToList();
        var outcome = DescribeOutcome(rule, row, locale, labels);

        var outcomeText = outcome.Count == 0
            ? SentenceTemplates.Text(locale, "outcome.none")
            : string.Join(
                SentenceTemplates.Text(locale, "sentence.list"),
                outcome.Select(value => SentenceTemplates.Text(locale, "outcome.field", ("field", value.FieldLabel), ("value", value.Text))));

        var sentence = isDefault
            ? SentenceTemplates.Text(locale, "sentence.default", ("outcome", outcomeText))
            : Sentence(parts.Where(part => !part.Vacuous).Select(part => part.View.Text).ToList(), outcomeText, locale);

        return new RowView(
            row.Key,
            rowId,
            index,
            isDefault,
            labels.Rows.TryGetValue(rowId, out var title) ? title : null,
            [.. parts.Select(part => part.View)],
            outcome,
            sentence);
    }

    private static string Sentence(List<string> conditions, string outcome, WorkbenchLocale locale) =>
        conditions.Count == 0
            ? SentenceTemplates.Text(locale, "sentence.always", ("outcome", outcome))
            : SentenceTemplates.Text(locale, "sentence.if", ("conditions", string.Join(SentenceTemplates.Text(locale, "sentence.and"), conditions)), ("outcome", outcome));

    /// <summary>A condition and whether it says nothing (any value, or a range without a bound) and so is left out of the sentence.</summary>
    private static (ConditionView View, bool Vacuous) DescribeCondition(Condition condition, int index, WorkbenchLocale locale, RuleLabels labels)
    {
        var fact = FactLabel(condition.FactName, labels);
        string One(DecisionValue? value) => WorkbenchText.FormatValue(value, condition.FactName, locale, labels);
        var any = SentenceTemplates.Text(locale, "operator.any", ("fact", fact));

        var (text, vacuous) = condition.Operator switch
        {
            Operator.Any => (any, true),
            Operator.Equals => (SentenceTemplates.Text(locale, "operator.equals", ("fact", fact), ("value", One(condition.Value))), false),
            Operator.NotEquals => (SentenceTemplates.Text(locale, "operator.notEquals", ("fact", fact), ("value", One(condition.Value))), false),
            Operator.StartsWith => (SentenceTemplates.Text(locale, "operator.startsWith", ("fact", fact), ("value", One(condition.Value))), false),
            Operator.In => (SentenceTemplates.Text(locale, "operator.in", ("fact", fact), ("values", List(condition.Values, One, locale))), false),
            Operator.NotIn => (SentenceTemplates.Text(locale, "operator.notIn", ("fact", fact), ("values", List(condition.Values, One, locale))), false),
            Operator.IsNull => (SentenceTemplates.Text(locale, "operator.isNull", ("fact", fact)), false),
            Operator.IsNotNull => (SentenceTemplates.Text(locale, "operator.isNotNull", ("fact", fact)), false),
            Operator.Range => RangeText(condition, fact, any, One, locale),
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition.Operator, "Unknown operator."),
        };

        return (new ConditionView(index, condition.FactName, fact, condition.Operator, text), vacuous);
    }

    private static string List(IReadOnlyList<DecisionValue> values, Func<DecisionValue?, string> one, WorkbenchLocale locale) =>
        string.Join(SentenceTemplates.Text(locale, "sentence.list"), values.Select(one));

    private static (string Text, bool Vacuous) RangeText(
        Condition condition, string fact, string any, Func<DecisionValue?, string> one, WorkbenchLocale locale)
    {
        var lower = condition.Value;
        var upper = condition.UpperValue;
        if (lower is not null && upper is not null && condition.LowerInclusive && condition.UpperInclusive)
        {
            return (SentenceTemplates.Text(locale, "operator.range", ("fact", fact), ("lower", one(lower)), ("upper", one(upper))), false);
        }

        var bounds = new List<string>();
        if (lower is not null)
        {
            bounds.Add(SentenceTemplates.Text(locale, condition.LowerInclusive ? "range.atLeast" : "range.greaterThan", ("value", one(lower))));
        }

        if (upper is not null)
        {
            bounds.Add(SentenceTemplates.Text(locale, condition.UpperInclusive ? "range.atMost" : "range.lessThan", ("value", one(upper))));
        }

        return bounds.Count == 0
            ? (any, true)
            : (SentenceTemplates.Text(locale, "operator.rangeBounds", ("fact", fact), ("bounds", string.Join(SentenceTemplates.Text(locale, "sentence.and"), bounds))), false);
    }

    private static List<OutcomeValueView> DescribeOutcome(BrainRule rule, Row row, WorkbenchLocale locale, RuleLabels labels)
    {
        var views = new List<OutcomeValueView>();
        if (JsonNode.Parse(row.Outcome) is not JsonObject outcome)
        {
            return views;
        }

        var declared = rule.Kind.OutcomeFields.ToDictionary(field => field.Name, field => field.Type, StringComparer.Ordinal);
        var names = rule.Kind.OutcomeFields.Select(field => field.Name).Where(outcome.ContainsKey)
            .Concat(outcome.Select(member => member.Key).Where(key => !declared.ContainsKey(key)));

        foreach (var name in names)
        {
            if (outcome[name] is { } node)
            {
                var type = declared.TryGetValue(name, out var found) ? found : (FactType?)null;
                views.Add(new OutcomeValueView(name, FieldLabel(name, labels), RawText(node), OutcomeText(name, type, node, locale, labels)));
            }
        }

        return views;
    }

    private static string RawText(JsonNode node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToJsonString(RawJson);

    private static string OutcomeText(string field, FactType? type, JsonNode node, WorkbenchLocale locale, RuleLabels labels)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                return labels.ValueLabel(field, text) ?? (type == FactType.Enum ? WorkbenchText.Humanize(text) : text);
            case JsonValue value when value.GetValueKind() is JsonValueKind.True or JsonValueKind.False:
                return SentenceTemplates.Text(locale, value.GetValueKind() == JsonValueKind.True ? "value.true" : "value.false");
            case JsonObject money when money["amount"] is { } amount && money["currency"] is { } currency:
                return SentenceTemplates.Text(locale, "value.money", ("amount", RawText(amount)), ("currency", RawText(currency)));
            default:
                return node.ToJsonString(RawJson);
        }
    }
}

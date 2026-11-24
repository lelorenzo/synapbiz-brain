using System.Globalization;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>The result of reading the edits of a rule tool call: the edits, or what is wrong with one of them.</summary>
/// <param name="Edits">The edits; empty when <paramref name="Problem"/> is set.</param>
/// <param name="Index">The position of the edit that cannot be read, from 0.</param>
/// <param name="Problem">What is wrong with it, in English for the assistant; null when all edits were read.</param>
/// <param name="Why">The structured form of the problem, for the sentence; null when all edits were read.</param>
internal sealed record RuleEditsRead(IReadOnlyList<RuleEdit> Edits, int Index, string? Problem, AuthoringProblemException? Why = null);

/// <summary>
/// Reads the JSON edits of <c>rule_draft_edit</c> into the <see cref="RuleEdit"/>s of the rule workbench. Values are written as plain JSON (text,
/// number, boolean) and typed by the fact or outcome field they are for, with the same reading a person's typed text gets in the workbench.
/// </summary>
internal static class RuleEditReader
{
    internal static readonly string[] Operators = ["any", "equals", "notEquals", "in", "notIn", "isNull", "isNotNull", "range", "startsWith"];

    internal static readonly string[] Operations =
        ["setCondition", "clearCondition", "setOutcome", "addRow", "removeRow", "moveRow", "setDefaultOutcome", "setCaseExpectation", "addCase", "removeCase", "setRowLabel"];

    internal static RuleEditsRead Read(JsonArray edits, BrainRule rule)
    {
        var read = new List<RuleEdit>();
        for (var index = 0; index < edits.Count; index++)
        {
            try
            {
                read.Add(One((JsonObject)edits[index]!, rule));
            }
            catch (AuthoringProblemException error)
            {
                return new RuleEditsRead([], index, error.Message, error);
            }
            catch (FormatException error)
            {
                return new RuleEditsRead([], index, error.Message, new AuthoringProblemException("other", null, error.Message));
            }
        }

        return new RuleEditsRead(read, -1, null);
    }

    private static RuleEdit One(JsonObject edit, BrainRule rule)
    {
        var op = AuthoringJson.Text(edit["op"])!;
        return op switch
        {
            "setCondition" => new SetCondition(Row(edit), Condition(edit, rule)),
            "clearCondition" => new ClearCondition(Row(edit), Required(edit, "fact")),
            "setOutcome" => new SetOutcome(Row(edit), Required(edit, "field"), Outcome(rule, Required(edit, "field"), edit["value"])),
            "addRow" => new AddRow(Row(edit), AuthoringJson.Text(edit["after"]), [.. (edit["conditions"] as JsonArray ?? []).OfType<JsonObject>().Select(item => Condition(item, rule))], Outcomes(rule, edit["outcome"] as JsonObject)),
            "removeRow" => new RemoveRow(Row(edit)),
            "moveRow" => new MoveRow(Row(edit), AuthoringJson.Text(edit["after"])),
            "setDefaultOutcome" => new SetDefaultOutcome(Required(edit, "field"), Outcome(rule, Required(edit, "field"), edit["value"])),
            "setCaseExpectation" => CaseExpectation(edit, rule),
            "addCase" => AddCase(edit, rule),
            "removeCase" => new RemoveCase(Required(edit, "case")),
            "setRowLabel" => new SetRowLabel(Row(edit), AuthoringJson.Locale(edit["locale"]) ?? throw new AuthoringProblemException("locale", null, "`locale` must be en or es."), AuthoringJson.Text(edit["text"])),
            _ => throw new AuthoringProblemException("operation", null, "The operation `" + op + "` is not one of " + string.Join(", ", Operations) + "."),
        };
    }

    private static SetCaseExpectation CaseExpectation(JsonObject edit, BrainRule rule)
    {
        var name = Required(edit, "case");
        if (edit["noDecision"]?.GetValue<bool>() == true)
        {
            return SetCaseExpectation.NoDecision(name);
        }

        return new SetCaseExpectation(name, AuthoringJson.Text(edit["row"]), Outcomes(rule, edit["outcome"] as JsonObject));
    }

    private static AddCase AddCase(JsonObject edit, BrainRule rule)
    {
        var facts = new Dictionary<string, DecisionValue>(StringComparer.Ordinal);
        foreach (var (name, value) in edit["facts"] as JsonObject ?? [])
        {
            var fact = rule.Kind.Facts.FirstOrDefault(item => item.Name == name) ?? throw new AuthoringProblemException("unknownfact", name, "The rule has no fact `" + name + "`.");
            if (Typed(fact, value) is { } typed)
            {
                facts[name] = typed;
            }
        }

        var noDecision = edit["noDecision"]?.GetValue<bool>() == true;
        var rowKey = AuthoringJson.Text(edit["row"]);
        var rowId = rowKey is null ? null : rule.RowIdsByKey.GetValueOrDefault(rowKey, rowKey);
        return new AddCase(new ProposedCase(Required(edit, "case"), facts, noDecision, null, rowId));
    }

    private static string Row(JsonObject edit) => Required(edit, "row");

    private static string Required(JsonObject edit, string name) =>
        AuthoringJson.Text(edit[name]) is { Length: > 0 } text ? text : throw new AuthoringProblemException("needtext", name, "The edit needs `" + name + "` as text.");

    private static Condition Condition(JsonObject node, BrainRule rule)
    {
        var name = Required(node, "fact");
        var fact = rule.Kind.Facts.FirstOrDefault(item => item.Name == name) ?? throw new AuthoringProblemException("unknownfact", name, "The rule has no fact `" + name + "`. Its facts are " + string.Join(", ", rule.Kind.Facts.Select(item => item.Name)) + ".");
        var op = AuthoringJson.Text(node["operator"]) ?? throw new AuthoringProblemException("needoperator", null, "A condition needs `operator`: one of " + string.Join(", ", Operators) + ".");
        return op switch
        {
            "any" => Nuvora.Decisions.Contracts.Tables.Condition.Any(name),
            "isNull" => Nuvora.Decisions.Contracts.Tables.Condition.IsNull(name),
            "isNotNull" => Nuvora.Decisions.Contracts.Tables.Condition.IsNotNull(name),
            "equals" => Nuvora.Decisions.Contracts.Tables.Condition.Equals(name, Single(fact, node["value"], "value")),
            "notEquals" => Nuvora.Decisions.Contracts.Tables.Condition.NotEquals(name, Single(fact, node["value"], "value")),
            "in" => Nuvora.Decisions.Contracts.Tables.Condition.In(name, Many(fact, node["values"])),
            "notIn" => Nuvora.Decisions.Contracts.Tables.Condition.NotIn(name, Many(fact, node["values"])),
            "startsWith" => Nuvora.Decisions.Contracts.Tables.Condition.StartsWith(name, Text(node["value"]) ?? throw new AuthoringProblemException("needtextvalue", name, "startsWith needs a text `value`.")),
            "range" => Range(fact, node),
            _ => throw new AuthoringProblemException("unknownoperator", name, "The operator `" + op + "` is not one of " + string.Join(", ", Operators) + "."),
        };
    }

    private static Condition Range(FactDescriptor fact, JsonObject node)
    {
        var lower = node["value"] is null ? null : Typed(fact, node["value"]);
        var upper = node["upper"] is null ? null : Typed(fact, node["upper"]);
        if (lower is null && upper is null)
        {
            throw new AuthoringProblemException("needrange", fact.Name, "A range needs `value` (the lower end), `upper`, or both.");
        }

        return Nuvora.Decisions.Contracts.Tables.Condition.Range(fact.Name, lower, upper, node["lowerInclusive"]?.GetValue<bool>() ?? true, node["upperInclusive"]?.GetValue<bool>() ?? true);
    }

    private static DecisionValue Single(FactDescriptor fact, JsonNode? value, string name) =>
        (value is null ? null : Typed(fact, value)) ?? throw new AuthoringProblemException("needvalue", fact.Name, "The condition needs `" + name + "`.");

    private static List<DecisionValue> Many(FactDescriptor fact, JsonNode? values) =>
        values is JsonArray list && list.Count > 0
            ? [.. list.Select(item => Single(fact, item, "values"))]
            : throw new AuthoringProblemException("needvalues", null, "The condition needs `values` as a non-empty array.");

    private static DecisionValue? Outcome(BrainRule rule, string field, JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }

        var descriptor = rule.Kind.OutcomeFields.FirstOrDefault(item => item.Name == field)
            ?? throw new AuthoringProblemException("unknownfield", field, "The rule has no outcome field `" + field + "`. Its outcome fields are " + string.Join(", ", rule.Kind.OutcomeFields.Select(item => item.Name)) + ".");
        return Typed(new FactDescriptor(descriptor.Name, descriptor.Type, descriptor.Nullable), value);
    }

    private static Dictionary<string, DecisionValue> Outcomes(BrainRule rule, JsonObject? outcome)
    {
        var values = new Dictionary<string, DecisionValue>(StringComparer.Ordinal);
        foreach (var (field, value) in outcome ?? [])
        {
            if (Outcome(rule, field, value) is { } typed)
            {
                values[field] = typed;
            }
        }

        return values;
    }

    private static DecisionValue? Typed(FactDescriptor fact, JsonNode? value)
    {
        var parsed = FactInput.Parse(fact, Text(value));
        if (parsed.IsValid)
        {
            return parsed.Value;
        }

        var shown = Text(value) is { } text ? "`" + text + "` " : string.Empty;
        var allowed = fact.AllowedValues is { Count: > 0 } values ? "; allowed values are " + string.Join(", ", values) : string.Empty;
        throw new AuthoringProblemException("value:" + parsed.ErrorCode, fact.Name, "The value " + shown + "does not fit the " + fact.Type + " `" + fact.Name + "` (" + parsed.ErrorCode + ")" + allowed + ".");
    }

    internal static string? Text(JsonNode? value) => value switch
    {
        null => null,
        JsonValue scalar when scalar.TryGetValue<string>(out var text) => text,
        JsonValue scalar when scalar.TryGetValue<bool>(out var flag) => flag ? "true" : "false",
        JsonValue scalar => scalar.ToJsonString().Trim('"').ToString(CultureInfo.InvariantCulture),
        _ => throw new AuthoringProblemException("needscalar", null, "A value must be text, a number or true or false."),
    };
}

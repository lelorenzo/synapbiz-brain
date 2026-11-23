using System.Globalization;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Workbench;

/// <summary>
/// Writes typed values and conditions in the JSON shape <c>Synapbiz.Brain.Spec</c> reads. The writer of that library is
/// internal, so this is the workbench's own, pinned by the round-trip tests of the drafter: every shape written here is loaded
/// back with <c>BrainRuleLoader</c>. An Enum value carries no <c>allowedValues</c> and a Reference no <c>labelSource</c>: the
/// loader takes both from the module contract.
/// </summary>
internal static class TypedValueJson
{
    /// <summary>A value as a fact or condition operand: <c>{ "type": ..., "value": ... }</c>, Integer and Decimal as strings.</summary>
    internal static JsonObject Tagged(DecisionValue value) => value switch
    {
        CodeValue code => Typed("Code", code.Value),
        EnumValue member => Typed("Enum", member.Value),
        BooleanValue flag => Typed("Boolean", flag.Value),
        IntegerValue integer => Typed("Integer", integer.Value.ToString(CultureInfo.InvariantCulture)),
        DecimalValue number => Typed("Decimal", number.Value.ToString(CultureInfo.InvariantCulture)),
        MoneyValue money => new JsonObject
        {
            ["type"] = "Money",
            ["value"] = new JsonObject
            {
                ["amount"] = money.Amount.ToString(CultureInfo.InvariantCulture),
                ["currency"] = money.Currency,
            },
        },
        DateValue date => Typed("Date", date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        ReferenceValue reference => new JsonObject
        {
            ["type"] = "Reference",
            ["value"] = new JsonObject { ["guid"] = reference.Value.ToString("D", CultureInfo.InvariantCulture) },
        },
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown value type."),
    };

    /// <summary>A value as an outcome field: the plain JSON the engine stores (a string, a boolean, a number or a money object).</summary>
    internal static JsonNode Plain(DecisionValue value) => value switch
    {
        CodeValue code => JsonValue.Create(code.Value),
        EnumValue member => JsonValue.Create(member.Value),
        BooleanValue flag => JsonValue.Create(flag.Value),
        IntegerValue integer => JsonValue.Create(integer.Value),
        DecimalValue number => JsonValue.Create(number.Value),
        MoneyValue money => new JsonObject { ["amount"] = JsonValue.Create(money.Amount), ["currency"] = money.Currency },
        DateValue date => JsonValue.Create(date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        ReferenceValue reference => JsonValue.Create(reference.Value.ToString("D", CultureInfo.InvariantCulture)),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown value type."),
    };

    /// <summary>An outcome as a JSON object, the fields in the order of the dictionary.</summary>
    internal static JsonObject Outcome(IReadOnlyDictionary<string, DecisionValue> fields)
    {
        var outcome = new JsonObject();
        foreach (var (name, value) in fields)
        {
            outcome[name] = Plain(value);
        }

        return outcome;
    }

    /// <summary>A condition as the document writes it; only the members the operator reads are written.</summary>
    internal static JsonObject Condition(Condition condition)
    {
        var node = new JsonObject { ["fact"] = condition.FactName, ["op"] = OperatorName(condition.Operator) };
        switch (condition.Operator)
        {
            case Operator.Equals or Operator.NotEquals or Operator.StartsWith:
                SetOperand(node, "value", condition.Value);
                break;
            case Operator.In or Operator.NotIn:
                node["values"] = new JsonArray([.. condition.Values.Select(value => (JsonNode)Tagged(value))]);
                break;
            case Operator.Range:
                SetOperand(node, "value", condition.Value);
                SetOperand(node, "upper", condition.UpperValue);
                node["lowerInclusive"] = condition.LowerInclusive;
                node["upperInclusive"] = condition.UpperInclusive;
                break;
            default:
                break;
        }

        return node;
    }

    private static string OperatorName(Operator op) => op switch
    {
        Operator.Any => "any",
        Operator.Equals => "equals",
        Operator.NotEquals => "notEquals",
        Operator.In => "in",
        Operator.NotIn => "notIn",
        Operator.IsNull => "isNull",
        Operator.IsNotNull => "isNotNull",
        Operator.Range => "range",
        Operator.StartsWith => "startsWith",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown operator."),
    };

    private static void SetOperand(JsonObject node, string name, DecisionValue? value)
    {
        if (value is not null)
        {
            node[name] = Tagged(value);
        }
    }

    private static JsonObject Typed(string type, string value) => new() { ["type"] = type, ["value"] = value };

    private static JsonObject Typed(string type, bool value) => new() { ["type"] = type, ["value"] = value };
}

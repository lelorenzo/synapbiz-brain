using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Synapbiz.Brain.Spec.Json;

namespace Synapbiz.Brain.Spec;

/// <summary>
/// Step 3 of loading, the part that depends on the module contracts: the owning contract is required and available,
/// the decision kind is declared, and every condition, outcome and golden uses the facts, operators and types the
/// contract declares.
/// </summary>
internal static class RuleSemantics
{
    private const string BodyPath = "/body";

    internal static IEnumerable<BrainSpecError> Validate(JsonObject root, IReadOnlyDictionary<string, BrainContract> contracts)
    {
        var body = root["body"]!.AsObject();
        var module = Text(body["kind"])!.Split('.', 2)[0];
        BrainContract? owningContract = null;
        var declaresOwner = false;
        foreach (var (requirement, path) in Items(root["requires"], "/requires"))
        {
            var address = Text(requirement)!;
            var ownsSection = address.StartsWith(module + "@", StringComparison.Ordinal);
            declaresOwner |= ownsSection;
            if (!contracts.TryGetValue(address, out var contract))
            {
                yield return Diagnostics.ContractNotAvailable(path);
            }
            else if (ownsSection)
            {
                owningContract ??= contract;
            }
        }

        if (!declaresOwner)
        {
            yield return Diagnostics.OwnerRequired();
        }

        if (owningContract is null)
        {
            yield break;
        }

        foreach (var error in ValidateRule(body, owningContract))
        {
            yield return error;
        }
    }

    private static IEnumerable<BrainSpecError> ValidateRule(JsonObject body, BrainContract contract)
    {
        var kindId = Text(body["kind"])!;
        if (!contract.TryGetSource(kindId, out var kind))
        {
            yield return Diagnostics.KindUnknown(BodyPath + "/kind");
            yield break;
        }

        var facts = kind.Facts;
        var outcomes = kind.Outcomes;
        var rowIds = new HashSet<string>(StringComparer.Ordinal);
        var legacyKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (row, path) in Rows(body))
        {
            rowIds.Add(Text(row["id"])!);
            if (!legacyKeys.Add(Text(row["legacyKey"])!))
            {
                yield return Diagnostics.RowKeyDuplicated(path + "/legacyKey");
            }

            foreach (var (condition, conditionPath) in Items(row["conditions"], path + "/conditions"))
            {
                foreach (var error in ValidateCondition(condition!.AsObject(), conditionPath, facts))
                {
                    yield return error;
                }
            }

            foreach (var error in ValidateOutcome(row["outcome"]!.AsObject(), path + "/outcome", outcomes))
            {
                yield return error;
            }
        }

        var collect = Text(body["hitPolicy"]) == "collect";
        foreach (var (golden, path) in Items(body["goldens"], BodyPath + "/goldens"))
        {
            foreach (var error in ValidateGolden(golden!.AsObject(), path, facts, outcomes, rowIds, collect))
            {
                yield return error;
            }
        }
    }

    private static IEnumerable<(JsonObject Value, string Path)> Rows(JsonObject body)
    {
        foreach (var (row, path) in Items(body["rows"], BodyPath + "/rows"))
        {
            yield return (row!.AsObject(), path);
        }

        if (body["defaultRow"] is JsonObject defaultRow)
        {
            yield return (defaultRow, BodyPath + "/defaultRow");
        }
    }

    private static IEnumerable<BrainSpecError> ValidateCondition(
        JsonObject condition, string path, IReadOnlyDictionary<string, JsonObject> facts)
    {
        if (!facts.TryGetValue(Text(condition["fact"])!, out var fact))
        {
            yield return Diagnostics.FactUnknown(path + "/fact");
            yield break;
        }

        var type = Text(fact["type"]);
        var op = Text(condition["op"]);
        if (!IsOperatorAllowed(op, type))
        {
            yield return Diagnostics.OperatorUnsupported(path + "/op");
            yield break;
        }

        var valid = true;
        foreach (var (operand, operandPath) in ConditionOperands(condition, path, op))
        {
            var problem = ValidateTaggedValue(
                operand, fact, operandPath,
                op == "range" && operand is not null ? Diagnostics.RangeBoundMismatch(operandPath) : Diagnostics.OperandMismatch(operandPath));
            if (problem is not null)
            {
                yield return problem;
                valid = false;
            }
        }

        if (valid && type == "Money" && HasMixedCurrencies(condition))
        {
            yield return Diagnostics.CurrenciesMixed(path);
        }
    }

    private static bool IsOperatorAllowed(string? op, string? factType) => op switch
    {
        "in" or "notIn" => factType != "Boolean",
        "range" => factType is "Integer" or "Decimal" or "Money" or "Date",
        "startsWith" => factType == "Code",
        "any" or "equals" or "notEquals" or "isNull" or "isNotNull" => true,
        _ => false,
    };

    private static IEnumerable<(JsonNode? Value, string Path)> ConditionOperands(JsonObject condition, string path, string? op)
    {
        if (op is "in" or "notIn")
        {
            return Items(condition["values"], path + "/values");
        }

        if (op is "equals" or "notEquals" or "startsWith")
        {
            return [(condition["value"], path + "/value")];
        }

        if (op == "range")
        {
            var lower = condition["value"];
            var upper = condition["upper"];
            if (upper is null)
            {
                return [(lower, path + "/value")];
            }

            return lower is null ? [(upper, path + "/upper")] : [(lower, path + "/value"), (upper, path + "/upper")];
        }

        return [];
    }

    private static bool HasMixedCurrencies(JsonObject condition) =>
        Items(condition["values"], "").Select(item => item.Value)
            .Append(condition["value"]).Append(condition["upper"])
            .OfType<JsonObject>().Where(operand => Text(operand["type"]) == "Money")
            .Select(operand => Text((operand["value"] as JsonObject)?["currency"]))
            .OfType<string>().Distinct(StringComparer.Ordinal).Skip(1).Any();

    /// <summary>
    /// Checks a typed value against the contract's declaration of the fact: the declared type, the allowed values of an
    /// Enum and the label source of a Reference, which the document may leave out and the contract then supplies.
    /// </summary>
    private static BrainSpecError? ValidateTaggedValue(JsonNode? input, JsonObject declaration, string path, BrainSpecError mismatch)
    {
        var type = Text(declaration["type"]);
        if (input is not JsonObject tagged || Text(tagged["type"]) != type)
        {
            return mismatch;
        }

        var value = tagged["value"];
        var normalized = tagged;
        if (type == "Enum" && declaration["allowedValues"] is JsonArray allowed)
        {
            if (!Contains(allowed, Text(value)))
            {
                return Diagnostics.EnumValueNotAllowed(path);
            }

            if (!tagged.ContainsKey("allowedValues"))
            {
                normalized = (JsonObject)tagged.DeepClone();
                normalized["allowedValues"] = allowed.DeepClone();
            }
        }
        else if (type == "Reference" && value is JsonObject reference && declaration["labelSource"] is JsonObject source)
        {
            if (reference["labelSource"] is null)
            {
                normalized = (JsonObject)tagged.DeepClone();
                normalized["value"]!["labelSource"] = source.DeepClone();
            }
            else if (!JsonNode.DeepEquals(reference["labelSource"], source))
            {
                return Diagnostics.LabelSourceDiffers(path);
            }
        }

        return TaggedValues.IsReadable(normalized) ? null : mismatch;
    }

    private static IEnumerable<BrainSpecError> ValidateGolden(
        JsonObject golden,
        string path,
        IReadOnlyDictionary<string, JsonObject> facts,
        IReadOnlyDictionary<string, JsonObject> outcomes,
        HashSet<string> rowIds,
        bool collect)
    {
        foreach (var (name, value) in golden["facts"]!.AsObject())
        {
            var factPath = Pointer(path + "/facts", name);
            if (!facts.TryGetValue(name, out var fact))
            {
                yield return Diagnostics.GoldenFactUndeclared(factPath);
            }
            else
            {
                var error = ValidateTaggedValue(value, fact, factPath, Diagnostics.GoldenFactMismatch(factPath));
                if (error is not null)
                {
                    yield return error;
                }
            }
        }

        var expect = golden["expect"]!.AsObject();
        if (expect.ContainsKey("noDecision"))
        {
            yield break;
        }

        if (Text(expect["row"]) is { } row && !rowIds.Contains(row))
        {
            yield return Diagnostics.GoldenRowUndeclared(path + "/expect/row");
        }

        var outcome = expect["outcome"];
        var outcomePath = path + "/expect/outcome";
        if (collect ? outcome is not JsonArray : outcome is not JsonObject)
        {
            yield return Diagnostics.GoldenOutcomeShape(outcomePath);
            yield break;
        }

        foreach (var (value, valuePath) in collect ? Items(outcome, outcomePath) : [(outcome, outcomePath)])
        {
            foreach (var error in ValidateOutcome(value!.AsObject(), valuePath, outcomes))
            {
                yield return error;
            }
        }
    }

    private static IEnumerable<BrainSpecError> ValidateOutcome(
        JsonObject outcome, string path, IReadOnlyDictionary<string, JsonObject> fields)
    {
        foreach (var (name, value) in outcome)
        {
            var fieldPath = Pointer(path, name);
            if (!fields.TryGetValue(name, out var field))
            {
                yield return Diagnostics.OutcomeFieldUndeclared(fieldPath);
            }
            else if (!OutcomeHasType(value, field))
            {
                yield return Diagnostics.OutcomeValueMismatch(fieldPath);
            }
            else if (Text(field["type"]) == "Enum" && value is not null && field["allowedValues"] is JsonArray allowed
                && !Contains(allowed, Text(value)))
            {
                yield return Diagnostics.EnumValueNotAllowed(fieldPath);
            }
        }

        foreach (var (name, field) in fields)
        {
            if (!Flag(field["nullable"]) && !outcome.ContainsKey(name))
            {
                yield return Diagnostics.OutcomeFieldMissing(Pointer(path, name));
            }
        }
    }

    private static bool OutcomeHasType(JsonNode? value, JsonObject field)
    {
        if (value is null)
        {
            return Flag(field["nullable"]);
        }

        return Text(field["type"]) switch
        {
            "Code" or "Enum" => Text(value) is not null,
            "Boolean" => value.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
            "Integer" => IsSafeInteger(Number(value)),
            "Decimal" => IsDecimalRange(value),
            "Money" => value is JsonObject money && money.Count == 2 && IsDecimalRange(money["amount"])
                && Text(money["currency"]) is { Length: 3 } currency && currency.All(character => character is >= 'A' and <= 'Z'),
            "Date" => Text(value) is { Length: 10 } date && TaggedValues.TryParseDate(date, out _),
            "Reference" => Text(value) is { Length: 36 } guid && Guid.TryParseExact(guid, "D", out _),
            _ => false,
        };
    }

    private static bool IsDecimalRange(JsonNode? node) => Math.Abs(Number(node)) <= (double)decimal.MaxValue;
}

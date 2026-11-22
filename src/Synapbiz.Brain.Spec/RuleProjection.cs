using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Tables;
using static Synapbiz.Brain.Spec.Json;

namespace Synapbiz.Brain.Spec;

/// <summary>A document that passed validation, as the engine reads it.</summary>
internal sealed record ProjectedRule(
    DecisionTable Table,
    IReadOnlyList<GoldenCase> Goldens,
    IReadOnlyDictionary<string, string> RowIdsByKey);

/// <summary>A value of the document that cannot be written as an engine value.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors",
    Justification = "Internal control-flow exception: every throw site names the JSON path and a message.")]
[SuppressMessage("Design", "CA1064:Exceptions should be public",
    Justification = "Internal control-flow exception, caught by the loader; the public surface is fixed by the brief.")]
[SuppressMessage("Major Code Smell", "S3871:Exception types should be \"public\"",
    Justification = "Internal control-flow exception, caught by the loader; the public surface is fixed by the brief.")]
internal sealed class ProjectionException(string path, string message) : Exception(message)
{
    internal string Path { get; } = path;
}

/// <summary>
/// Step 4 of loading: the validated <c>body</c> of a rule as a <see cref="DecisionTable"/> and a list of
/// <see cref="GoldenCase"/>. Rows keep their document order and are keyed by their <c>legacyKey</c>; typed values are
/// read from their lexemes with the invariant culture; outcomes are stored as canonical JSON (members in ordinal order,
/// numbers as written).
/// </summary>
internal static class RuleProjection
{
    private static readonly JsonWriterOptions CompactRelaxed = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static ProjectedRule Project(JsonObject body)
    {
        var policy = ReadHitPolicy(Text(body["hitPolicy"]));
        var rowIdsByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var keysById = new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = new List<Row>();
        foreach (var (node, path) in Items(body["rows"], "/body/rows"))
        {
            rows.Add(ProjectRow(AsObject(node, path), path, rowIdsByKey, keysById));
        }

        Row? defaultRow = null;
        if (body["defaultRow"] is JsonObject defaultNode)
        {
            defaultRow = ProjectRow(defaultNode, "/body/defaultRow", rowIdsByKey, keysById);
        }

        var goldens = new List<GoldenCase>();
        foreach (var (node, path) in Items(body["goldens"], "/body/goldens"))
        {
            goldens.Add(ProjectGolden(AsObject(node, path), path, policy, keysById));
        }

        return new ProjectedRule(new DecisionTable(policy, rows, defaultRow), goldens, rowIdsByKey);
    }

    /// <summary>The outcome as the engine stores it: members in ordinal order at every depth, scalars as written.</summary>
    internal static string CanonicalOutcome(JsonNode outcome)
    {
        var first = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(first, CompactRelaxed))
        {
            outcome.WriteTo(writer);
        }

        using var document = JsonDocument.Parse(first.WrittenMemory);
        var second = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(second, CompactRelaxed))
        {
            WriteCanonical(writer, document.RootElement);
        }

        return Encoding.UTF8.GetString(second.WrittenSpan);
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static HitPolicy ReadHitPolicy(string? name) => name switch
    {
        "unique" => HitPolicy.Unique,
        "first" => HitPolicy.First,
        "collect" => HitPolicy.Collect,
        _ => throw new ProjectionException("/body/hitPolicy", $"Unknown value '{name}'."),
    };

    private static Row ProjectRow(
        JsonObject row, string path, Dictionary<string, string> rowIdsByKey, Dictionary<string, string> keysById)
    {
        var id = RequiredText(row["id"], path + "/id");
        var key = RequiredText(row["legacyKey"], path + "/legacyKey");
        rowIdsByKey[key] = id;
        keysById[id] = key;

        var conditions = new List<Condition>();
        foreach (var (node, conditionPath) in Items(row["conditions"], path + "/conditions"))
        {
            conditions.Add(ProjectCondition(AsObject(node, conditionPath), conditionPath));
        }

        var outcome = AsObject(row["outcome"], path + "/outcome");
        return new Row(key, conditions, CanonicalOutcome(outcome));
    }

    private static Condition ProjectCondition(JsonObject condition, string path)
    {
        var values = new List<DecisionValue>();
        foreach (var (node, valuePath) in Items(condition["values"], path + "/values"))
        {
            values.Add(ProjectValue(AsObject(node, valuePath), valuePath));
        }

        return new Condition
        {
            FactName = RequiredText(condition["fact"], path + "/fact"),
            Operator = ReadOperator(Text(condition["op"]), path + "/op"),
            Value = condition["value"] is { } value ? ProjectValue(AsObject(value, path + "/value"), path + "/value") : null,
            UpperValue = condition["upper"] is { } upper ? ProjectValue(AsObject(upper, path + "/upper"), path + "/upper") : null,
            LowerInclusive = OptionalFlag(condition["lowerInclusive"]),
            UpperInclusive = OptionalFlag(condition["upperInclusive"]),
            Values = values,
        };
    }

    private static bool OptionalFlag(JsonNode? node) => node is not JsonValue value || !value.TryGetValue<bool>(out var flag) || flag;

    private static Operator ReadOperator(string? name, string path) => name switch
    {
        "any" => Operator.Any,
        "equals" => Operator.Equals,
        "notEquals" => Operator.NotEquals,
        "in" => Operator.In,
        "notIn" => Operator.NotIn,
        "isNull" => Operator.IsNull,
        "isNotNull" => Operator.IsNotNull,
        "range" => Operator.Range,
        "startsWith" => Operator.StartsWith,
        _ => throw new ProjectionException(path, $"Unknown value '{name}'."),
    };

    private static DecisionValue ProjectValue(JsonObject source, string path)
    {
        var type = Text(source["type"]);
        var value = source["value"] ?? throw new ProjectionException(path + "/value", "A tagged value needs a payload.");
        var valuePath = path + "/value";
        switch (type)
        {
            case "Code":
                return DecisionValue.Code(RequiredText(value, valuePath));
            case "Enum":
                return DecisionValue.Enum(RequiredText(value, valuePath));
            case "Boolean":
                return value is JsonValue flag && flag.TryGetValue<bool>(out var boolean)
                    ? DecisionValue.Boolean(boolean)
                    : throw new ProjectionException(valuePath, "Expected true or false.");
            case "Integer":
                return long.TryParse(RequiredText(value, valuePath), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
                    ? DecisionValue.Integer(integer)
                    : throw new ProjectionException(valuePath, "The numeric value is out of range.");
            case "Decimal":
                return DecisionValue.Decimal(ReadDecimal(value, valuePath));
            case "Money":
                var money = AsObject(value, valuePath);
                return DecisionValue.Money(
                    ReadDecimal(money["amount"], valuePath + "/amount"),
                    RequiredText(money["currency"], valuePath + "/currency"));
            case "Date":
                return TaggedValues.TryParseDate(RequiredText(value, valuePath), out var date)
                    ? DecisionValue.Date(date)
                    : throw new ProjectionException(valuePath, "Expected a date as yyyy-MM-dd.");
            case "Reference":
                var reference = AsObject(value, valuePath);
                return Guid.TryParse(RequiredText(reference["guid"], valuePath + "/guid"), out var guid)
                    ? DecisionValue.Reference(guid)
                    : throw new ProjectionException(valuePath + "/guid", "A Reference needs a GUID.");
            default:
                throw new ProjectionException(path + "/type", $"Unknown value '{type}'.");
        }
    }

    private static decimal ReadDecimal(JsonNode? node, string path) =>
        TaggedValues.TryReadStored(RequiredText(node, path), out var number)
            ? number
            : throw new ProjectionException(path, "The numeric value is out of range.");

    private static GoldenCase ProjectGolden(
        JsonObject golden, string path, HitPolicy policy, Dictionary<string, string> keysById)
    {
        var facts = new Dictionary<string, DecisionValue>(StringComparer.Ordinal);
        foreach (var (name, fact) in AsObject(golden["facts"], path + "/facts").OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (fact is not null)
            {
                var factPath = Pointer(path + "/facts", name);
                facts[name] = ProjectValue(AsObject(fact, factPath), factPath);
            }
        }

        var expect = AsObject(golden["expect"], path + "/expect");
        GoldenExpectation expectation;
        if (expect["noDecision"] is JsonValue noDecision && noDecision.TryGetValue<bool>(out var flag) && flag)
        {
            expectation = new ExpectedNoDecision();
        }
        else
        {
            var outcomePath = path + "/expect/outcome";
            var outcome = expect["outcome"] ?? throw new ProjectionException(outcomePath, "A decided golden requires an expected outcome.");
            var shapeIsValid = policy == HitPolicy.Collect ? outcome is JsonArray list && list.All(item => item is JsonObject) : outcome is JsonObject;
            if (!shapeIsValid)
            {
                throw new ProjectionException(outcomePath, "Expected an outcome matching the hit policy.");
            }

            string? rowKey = null;
            if (Text(expect["row"]) is { } rowId)
            {
                rowKey = keysById.TryGetValue(rowId, out var key)
                    ? key
                    : throw new ProjectionException(path + "/expect/row", "The golden references an unknown row ID.");
            }

            expectation = new ExpectedDecided(CanonicalOutcome(outcome), rowKey);
        }

        return new GoldenCase(RequiredText(golden["name"], path + "/name"), facts, expectation);
    }

    private static JsonObject AsObject(JsonNode? node, string path) =>
        node as JsonObject ?? throw new ProjectionException(path, "Expected an object.");

    private static string RequiredText(JsonNode? node, string path) =>
        Text(node) ?? throw new ProjectionException(path, "Expected a string.");
}

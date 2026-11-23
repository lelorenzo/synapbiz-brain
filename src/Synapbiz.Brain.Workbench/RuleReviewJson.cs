using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Workbench;

/// <summary>Writes and reads the <c>brain.rule-review@1</c> document.</summary>
public static class RuleReviewJson
{
    /// <summary>The value of the <c>$spec</c> member.</summary>
    public const string Format = "brain.rule-review@1";

    /// <summary>The text is not JSON.</summary>
    public const string ErrorJsonInvalid = "workbench.review.json_invalid";

    /// <summary>The <c>$spec</c> member names another format.</summary>
    public const string ErrorUnknownFormat = "workbench.review.unknown_format";

    /// <summary>A member is missing, of the wrong JSON kind or not readable.</summary>
    public const string ErrorMalformed = "workbench.review.malformed";

    private const string CreatedAtFormat = "yyyy-MM-dd'T'HH:mm:sszzz";

    private static readonly string[] CreatedAtFormats = [CreatedAtFormat, "yyyy-MM-dd'T'HH:mm:ss'Z'"];

    private static readonly JsonSerializerOptions CompactOutcome = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary><c>sha256:</c> and the lower-case hexadecimal SHA-256 of the UTF-8 bytes of the text exactly as given.</summary>
    /// <param name="ruleJson">The text of the rule document.</param>
    public static string ContentHash(string ruleJson)
    {
        ArgumentNullException.ThrowIfNull(ruleJson);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ruleJson)));
    }

    /// <summary>
    /// Writes the review as indented JSON with the members in a fixed order and the facts of each proposed case sorted by name.
    /// <c>createdAt</c> is written to whole seconds. A proposed case that expects a decision writes its
    /// <see cref="ProposedCase.ExpectedOutcomeJson"/> as JSON; that text must be valid JSON or <see cref="JsonException"/> is thrown.
    /// </summary>
    /// <param name="review">The review.</param>
    public static string Serialize(RuleReview review)
    {
        ArgumentNullException.ThrowIfNull(review);

        var buffer = new ArrayBufferWriter<byte>();
        var options = new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            writer.WriteStartObject();
            writer.WriteString("$spec", Format);
            writer.WriteStartObject("rule");
            writer.WriteString("id", review.RuleId);
            writer.WriteNumber("version", review.RuleVersion);
            writer.WriteString("contentHash", review.RuleContentHash);
            writer.WriteEndObject();
            writer.WriteString("reviewer", review.Reviewer);
            writer.WriteString("createdAt", review.CreatedAt.ToString(CreatedAtFormat, CultureInfo.InvariantCulture));
            writer.WriteStartArray("items");
            foreach (var item in review.Items)
            {
                writer.WriteStartObject();
                writer.WriteString("target", item.Target);
                writer.WriteString("state", StateName(item.State));
                writer.WriteString("note", item.Note);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("proposedCases");
            foreach (var proposed in review.ProposedCases)
            {
                WriteCase(writer, proposed);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Reads a review document. Every defect is collected and ordered by path; with any error the review is null. Unknown
    /// members are ignored. <see cref="ProposedCase.ExpectedOutcomeJson"/> is the outcome re-serialised compactly, so a review
    /// that is written and read again has the same members, with <see cref="RuleReview.CreatedAt"/> to whole seconds and the
    /// outcome in its compact form.
    /// </summary>
    /// <param name="json">The document text.</param>
    public static RuleReviewParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return Failed(ErrorJsonInvalid, string.Empty, "The text is not valid JSON.");
        }

        if (root is not JsonObject document)
        {
            return Failed(ErrorMalformed, string.Empty, "The document must be a JSON object.");
        }

        try
        {
            return Read(document);
        }
        catch (ArgumentException)
        {
            // System.Text.Json refuses to look a member up in an object that repeats a member name.
            return Failed(ErrorJsonInvalid, string.Empty, "The document repeats a member name.");
        }
    }

    private static RuleReviewParseResult Read(JsonObject document)
    {
        var spec = document["$spec"];
        if (spec is JsonValue value && value.TryGetValue<string>(out var format) && !string.Equals(format, Format, StringComparison.Ordinal))
        {
            return Failed(ErrorUnknownFormat, "/$spec", $"The format '{format}' is not '{Format}'.");
        }

        var errors = new List<WorkbenchError>();
        if (spec is null || !IsText(spec))
        {
            errors.Add(Malformed("/$spec", "Expected the string '" + Format + "'."));
        }

        var rule = ObjectOf(document, "rule", string.Empty, errors);
        var ruleId = rule is null ? null : TextOf(rule, "id", "/rule", errors);
        var ruleVersion = rule is null ? null : IntegerOf(rule, "version", "/rule", errors);
        var contentHash = rule is null ? null : TextOf(rule, "contentHash", "/rule", errors);
        var reviewer = TextOf(document, "reviewer", string.Empty, errors);
        var createdAt = DateOf(document, "createdAt", errors);
        var items = ReadItems(ArrayOf(document, "items", errors), errors);
        var cases = ReadCases(ArrayOf(document, "proposedCases", errors), errors);

        if (errors.Count > 0)
        {
            return new RuleReviewParseResult(null, [.. errors.OrderBy(error => error.Path, StringComparer.Ordinal)]);
        }

        return new RuleReviewParseResult(
            new RuleReview(ruleId!, ruleVersion!.Value, contentHash!, reviewer!, createdAt!.Value, items, cases),
            []);
    }

    private static RuleReviewParseResult Failed(string code, string path, string message) =>
        new(null, [new WorkbenchError(code, path, message)]);

    private static WorkbenchError Malformed(string path, string message) => new(ErrorMalformed, path, message);

    private static string StateName(ReviewState state) => state switch
    {
        ReviewState.Unreviewed => "unreviewed",
        ReviewState.Confirmed => "confirmed",
        ReviewState.NeedsFix => "needsFix",
        ReviewState.Question => "question",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown review state."),
    };

    private static ReviewState? StateOf(string name) => name switch
    {
        "unreviewed" => ReviewState.Unreviewed,
        "confirmed" => ReviewState.Confirmed,
        "needsFix" => ReviewState.NeedsFix,
        "question" => ReviewState.Question,
        _ => null,
    };

    // ---- writing -----------------------------------------------------------------------------

    private static void WriteCase(Utf8JsonWriter writer, ProposedCase proposed)
    {
        writer.WriteStartObject();
        writer.WriteString("name", proposed.Name);
        writer.WriteStartObject("facts");
        foreach (var fact in proposed.Facts.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(fact.Key);
            WriteValue(writer, fact.Value);
        }

        writer.WriteEndObject();
        writer.WriteStartObject("expect");
        if (proposed.ExpectNoDecision)
        {
            writer.WriteBoolean("noDecision", true);
        }
        else
        {
            if (proposed.ExpectedRowId is not null)
            {
                writer.WriteString("row", proposed.ExpectedRowId);
            }

            if (proposed.ExpectedOutcomeJson is not null)
            {
                using var outcome = JsonDocument.Parse(proposed.ExpectedOutcomeJson);
                writer.WritePropertyName("outcome");
                outcome.RootElement.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, DecisionValue value)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case CodeValue code:
                writer.WriteString("type", "Code");
                writer.WriteString("value", code.Value);
                break;
            case EnumValue member:
                writer.WriteString("type", "Enum");
                writer.WriteString("value", member.Value);
                break;
            case BooleanValue flag:
                writer.WriteString("type", "Boolean");
                writer.WriteBoolean("value", flag.Value);
                break;
            case IntegerValue integer:
                writer.WriteString("type", "Integer");
                writer.WriteString("value", integer.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case DecimalValue number:
                writer.WriteString("type", "Decimal");
                writer.WriteString("value", number.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case MoneyValue money:
                writer.WriteString("type", "Money");
                writer.WriteStartObject("value");
                writer.WriteString("amount", money.Amount.ToString(CultureInfo.InvariantCulture));
                writer.WriteString("currency", money.Currency);
                writer.WriteEndObject();
                break;
            case DateValue date:
                writer.WriteString("type", "Date");
                writer.WriteString("value", date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                break;
            case ReferenceValue reference:
                writer.WriteString("type", "Reference");
                writer.WriteStartObject("value");
                writer.WriteString("guid", reference.Value.ToString("D", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown value type.");
        }

        writer.WriteEndObject();
    }

    // ---- reading -----------------------------------------------------------------------------

    private static string Pointer(string parent, string key) =>
        parent + "/" + key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static bool IsText(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out _);

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string? TextOf(JsonObject owner, string name, string path, List<WorkbenchError> errors)
    {
        var text = Text(owner[name]);
        if (text is null)
        {
            errors.Add(Malformed(Pointer(path, name), "Expected a string."));
        }

        return text;
    }

    private static JsonObject? ObjectOf(JsonObject owner, string name, string path, List<WorkbenchError> errors)
    {
        if (owner[name] is JsonObject child)
        {
            return child;
        }

        errors.Add(Malformed(Pointer(path, name), "Expected an object."));
        return null;
    }

    private static JsonArray? ArrayOf(JsonObject owner, string name, List<WorkbenchError> errors)
    {
        if (owner[name] is JsonArray child)
        {
            return child;
        }

        errors.Add(Malformed(Pointer(string.Empty, name), "Expected an array."));
        return null;
    }

    private static long? IntegerOf(JsonObject owner, string name, string path, List<WorkbenchError> errors)
    {
        if (owner[name] is JsonValue { } node && node.GetValueKind() == JsonValueKind.Number && node.TryGetValue<long>(out var number))
        {
            return number;
        }

        errors.Add(Malformed(Pointer(path, name), "Expected a whole number."));
        return null;
    }

    private static DateTimeOffset? DateOf(JsonObject owner, string name, List<WorkbenchError> errors)
    {
        var text = Text(owner[name]);
        if (text is not null
            && DateTimeOffset.TryParseExact(text, CreatedAtFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment))
        {
            return moment;
        }

        errors.Add(Malformed(Pointer(string.Empty, name), "Expected a date and time such as 2026-11-22T21:00:00+00:00."));
        return null;
    }

    private static List<ReviewItem> ReadItems(JsonArray? array, List<WorkbenchError> errors)
    {
        var items = new List<ReviewItem>();
        for (var index = 0; array is not null && index < array.Count; index++)
        {
            var path = "/items/" + index.ToString(CultureInfo.InvariantCulture);
            if (array[index] is not JsonObject item)
            {
                errors.Add(Malformed(path, "Expected an object."));
                continue;
            }

            var target = TextOf(item, "target", path, errors);
            var note = TextOf(item, "note", path, errors);
            var stateName = TextOf(item, "state", path, errors);
            var state = stateName is null ? null : StateOf(stateName);
            if (stateName is not null && state is null)
            {
                errors.Add(Malformed(Pointer(path, "state"), "Expected unreviewed, confirmed, needsFix or question."));
            }

            if (target is not null && note is not null && state is not null)
            {
                items.Add(new ReviewItem(target, state.Value, note));
            }
        }

        return items;
    }

    private static List<ProposedCase> ReadCases(JsonArray? array, List<WorkbenchError> errors)
    {
        var cases = new List<ProposedCase>();
        for (var index = 0; array is not null && index < array.Count; index++)
        {
            var path = "/proposedCases/" + index.ToString(CultureInfo.InvariantCulture);
            if (array[index] is not JsonObject node)
            {
                errors.Add(Malformed(path, "Expected an object."));
                continue;
            }

            var name = TextOf(node, "name", path, errors);
            var facts = ReadFacts(ObjectOf(node, "facts", path, errors), Pointer(path, "facts"), errors);
            var expect = ObjectOf(node, "expect", path, errors) is { } expectation ? ReadExpectation(expectation, Pointer(path, "expect"), errors) : null;
            if (name is not null && facts is not null && expect is not null)
            {
                cases.Add(new ProposedCase(name, facts, expect.Value.NoDecision, expect.Value.OutcomeJson, expect.Value.RowId));
            }
        }

        return cases;
    }

    private static Dictionary<string, DecisionValue>? ReadFacts(JsonObject? node, string path, List<WorkbenchError> errors)
    {
        if (node is null)
        {
            return null;
        }

        var facts = new Dictionary<string, DecisionValue>(StringComparer.Ordinal);
        var valid = true;
        foreach (var (name, entry) in node)
        {
            var value = ReadValue(entry, Pointer(path, name), errors);
            if (value is null)
            {
                valid = false;
            }
            else
            {
                facts[name] = value;
            }
        }

        return valid ? facts : null;
    }

    private static (bool NoDecision, string? OutcomeJson, string? RowId)? ReadExpectation(JsonObject expect, string path, List<WorkbenchError> errors)
    {
        if (expect.ContainsKey("noDecision"))
        {
            if (expect["noDecision"] is JsonValue flag && flag.GetValueKind() == JsonValueKind.True)
            {
                return (true, null, null);
            }

            errors.Add(Malformed(Pointer(path, "noDecision"), "Expected true."));
            return null;
        }

        var rowId = expect["row"] is null ? null : Text(expect["row"]);
        var valid = true;
        if (expect["row"] is not null && rowId is null)
        {
            errors.Add(Malformed(Pointer(path, "row"), "Expected a string."));
            valid = false;
        }

        string? outcome = null;
        if (expect["outcome"] is JsonObject or JsonArray)
        {
            outcome = expect["outcome"]!.ToJsonString(CompactOutcome);
        }
        else
        {
            errors.Add(Malformed(Pointer(path, "outcome"), "Expected an object, or an array of objects."));
            valid = false;
        }

        return valid ? (false, outcome, rowId) : null;
    }

    private static DecisionValue? ReadValue(JsonNode? node, string path, List<WorkbenchError> errors)
    {
        if (node is not JsonObject typed)
        {
            errors.Add(Malformed(path, "Expected a typed value object."));
            return null;
        }

        var type = Text(typed["type"]);
        var value = typed["value"];
        var valuePath = Pointer(path, "value");
        DecisionValue? result = type switch
        {
            "Code" => Text(value) is { } code ? DecisionValue.Code(code) : null,
            "Enum" => Text(value) is { } member ? DecisionValue.Enum(member) : null,
            "Boolean" => value is JsonValue flag && flag.GetValueKind() is JsonValueKind.True or JsonValueKind.False
                ? DecisionValue.Boolean(flag.GetValueKind() == JsonValueKind.True)
                : null,
            "Integer" => long.TryParse(Text(value), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
                ? DecisionValue.Integer(integer)
                : null,
            "Decimal" => TryDecimal(Text(value), out var number) ? DecisionValue.Decimal(number) : null,
            "Money" => ReadMoney(value, valuePath, errors),
            "Date" => DateOnly.TryParseExact(Text(value), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? DecisionValue.Date(date)
                : null,
            "Reference" => value is JsonObject reference && Guid.TryParseExact(Text(reference["guid"]), "D", out var guid)
                ? DecisionValue.Reference(guid)
                : null,
            _ => null,
        };

        if (result is null)
        {
            ReportValue(type, valuePath, path, errors);
        }

        return result;
    }

    private static void ReportValue(string? type, string valuePath, string path, List<WorkbenchError> errors)
    {
        if (type is "Code" or "Enum" or "Boolean" or "Integer" or "Decimal" or "Date" or "Reference")
        {
            errors.Add(Malformed(type == "Reference" ? Pointer(valuePath, "guid") : valuePath, $"The value is not a readable {type}."));
        }
        else if (type != "Money")
        {
            errors.Add(Malformed(Pointer(path, "type"), "Expected Code, Enum, Boolean, Integer, Decimal, Money, Date or Reference."));
        }
    }

    private static DecisionValue? ReadMoney(JsonNode? value, string valuePath, List<WorkbenchError> errors)
    {
        if (value is not JsonObject money)
        {
            errors.Add(Malformed(valuePath, "The value is not a readable Money."));
            return null;
        }

        var valid = true;
        if (!TryDecimal(Text(money["amount"]), out var amount))
        {
            errors.Add(Malformed(Pointer(valuePath, "amount"), "Expected a decimal number as a string."));
            valid = false;
        }

        var currency = Text(money["currency"]);
        if (currency is not { Length: 3 } || !currency.All(letter => letter is >= 'A' and <= 'Z'))
        {
            errors.Add(Malformed(Pointer(valuePath, "currency"), "Expected three capital letters."));
            valid = false;
        }

        return valid ? DecisionValue.Money(amount, currency!) : null;
    }

    private static bool TryDecimal(string? text, out decimal number)
    {
        number = 0;
        return text is not null
            && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);
    }
}

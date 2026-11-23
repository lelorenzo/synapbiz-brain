using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Process;

/// <summary>The type of a process variable: the eight rule value types plus <c>Instant</c> and <c>Json</c>.</summary>
internal enum VariableType
{
    Code,
    Enum,
    Boolean,
    Integer,
    Decimal,
    Money,
    Date,
    Reference,
    Instant,
    Json,
}

/// <summary>A declared process variable.</summary>
/// <param name="Name">The variable name.</param>
/// <param name="Type">The declared type.</param>
/// <param name="AllowedValues">The members of an <c>Enum</c> variable; null for the other types.</param>
/// <param name="Input">True when <c>Start</c> may receive the variable.</param>
/// <param name="Initial">The declared initial value, when the document has one.</param>
internal sealed record VariableDef(
    string Name, VariableType Type, IReadOnlyList<string>? AllowedValues, bool Input, JsonNode? Initial);

/// <summary>
/// The value rules of the variable types (section 2.2 of the specification): validation and normalization of a JSON
/// value, and the conversions to and from the decision engine's values. A stored value is canonical JSON text.
/// </summary>
internal static partial class VariableValues
{
    private const double MaximumSafeInteger = 9_007_199_254_740_991;

    /// <summary>The variable type of a type name of the document, or null.</summary>
    internal static VariableType? ParseType(string? name) => name switch
    {
        "Code" => VariableType.Code,
        "Enum" => VariableType.Enum,
        "Boolean" => VariableType.Boolean,
        "Integer" => VariableType.Integer,
        "Decimal" => VariableType.Decimal,
        "Money" => VariableType.Money,
        "Date" => VariableType.Date,
        "Reference" => VariableType.Reference,
        "Instant" => VariableType.Instant,
        "Json" => VariableType.Json,
        _ => null,
    };

    /// <summary>The fact type a variable type corresponds to, or null for <c>Instant</c> and <c>Json</c>.</summary>
    internal static FactType? FactTypeOf(VariableType type) => type switch
    {
        VariableType.Code => FactType.Code,
        VariableType.Enum => FactType.Enum,
        VariableType.Boolean => FactType.Boolean,
        VariableType.Integer => FactType.Integer,
        VariableType.Decimal => FactType.Decimal,
        VariableType.Money => FactType.Money,
        VariableType.Date => FactType.Date,
        VariableType.Reference => FactType.Reference,
        _ => null,
    };

    /// <summary>
    /// Checks a JSON value against a variable declaration and writes it in canonical form. A JSON null (or an absent
    /// value) is valid for every type and means "no value": <paramref name="canonical"/> is then null.
    /// </summary>
    internal static bool TryNormalize(VariableDef variable, JsonNode? node, out string? canonical)
    {
        canonical = null;
        if (node is null)
        {
            return true;
        }

        var normalized = variable.Type switch
        {
            VariableType.Code => JsonText.Text(node) is { } code ? Quote(code) : null,
            VariableType.Enum => JsonText.Text(node) is { } label && variable.AllowedValues?.Contains(label, StringComparer.Ordinal) == true ? Quote(label) : null,
            VariableType.Boolean => node.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? node.ToJsonString() : null,
            VariableType.Integer => IntegerText(node),
            VariableType.Decimal => JsonText.Text(node) is { } number && TryDecimal(number, out _) ? Quote(number) : null,
            VariableType.Money => MoneyText(node),
            VariableType.Date => JsonText.Text(node) is { } date && TryDate(date, out _) ? Quote(date) : null,
            VariableType.Reference => JsonText.Text(node) is { } guid && Guid.TryParseExact(guid, "D", out _) ? Quote(guid) : null,
            VariableType.Instant => JsonText.Text(node) is { } instant && TryInstant(instant, out var moment) ? Quote(FormatInstant(moment)) : null,
            _ => JsonText.Canonical(node),
        };
        canonical = normalized;
        return normalized is not null;
    }

    /// <summary>True when the text is a Decimal value of the document: the pattern of section 2.2, within the range of <see cref="decimal"/>.</summary>
    internal static bool TryDecimal(string text, out decimal value)
    {
        value = 0;
        return DecimalPattern().IsMatch(text)
            && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Reads a real calendar date written <c>YYYY-MM-DD</c>.</summary>
    internal static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Reads an ISO 8601 date-time with <c>Z</c> or an offset and returns it in UTC with the fraction truncated.</summary>
    internal static bool TryInstant(string text, out DateTimeOffset moment)
    {
        moment = default;
        var match = InstantPattern().Match(text);
        if (!match.Success
            || !DateTimeOffset.TryParseExact(
                match.Groups[1].Value + match.Groups[2].Value,
                "yyyy-MM-dd'T'HH:mm:ssK",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        moment = parsed.ToUniversalTime();
        return true;
    }

    /// <summary>The normalized text of an instant: UTC, seconds precision, <c>yyyy-MM-ddTHH:mm:ssZ</c>.</summary>
    internal static string FormatInstant(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>The engine value of a stored variable value. The variable must be mapped only when its type has a fact type.</summary>
    internal static DecisionValue ToDecisionValue(VariableType type, JsonNode value) => type switch
    {
        VariableType.Code => DecisionValue.Code(JsonText.Text(value)!),
        VariableType.Enum => DecisionValue.Enum(JsonText.Text(value)!),
        VariableType.Boolean => DecisionValue.Boolean(value.GetValue<bool>()),
        VariableType.Integer => DecisionValue.Integer(long.Parse(value.ToJsonString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
        VariableType.Decimal => DecisionValue.Decimal(ParseDecimal(JsonText.Text(value)!)),
        VariableType.Money => DecisionValue.Money(ParseDecimal(JsonText.Text(value["amount"])!), JsonText.Text(value["currency"])!),
        VariableType.Date => DecisionValue.Date(DateOnly.ParseExact(JsonText.Text(value)!, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
        VariableType.Reference => DecisionValue.Reference(Guid.ParseExact(JsonText.Text(value)!, "D")),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The variable type has no decision value."),
    };

    /// <summary>
    /// Writes the outcome member of a decision as the value of a variable of the same type. The outcome forms of
    /// Code, Enum, Boolean, Integer, Date and Reference are the document forms of section 2.2. The forms of Decimal and
    /// Money differ (the rule library writes them as JSON numbers, the document as strings), and the specification does
    /// not say how to convert them, so they are refused here until it does.
    /// </summary>
    // Section 7: the rule library writes a Decimal outcome as a JSON number and a Money outcome as { amount: number, currency }.
    // The variable receives the raw lexeme of the number as its string; nothing goes through a binary floating type.
    internal static bool TryFromOutcome(FactType type, VariableDef target, JsonNode? member, out string? canonical)
    {
        canonical = null;
        return type switch
        {
            FactType.Decimal => TryLexeme(member, out var lexeme) && TryNormalize(target, JsonValue.Create(lexeme), out canonical),
            FactType.Money => member is JsonObject money && money.Count == 2
                && TryLexeme(money["amount"], out var amount)
                && TryNormalize(target, new JsonObject { ["amount"] = amount, ["currency"] = money["currency"]?.DeepClone() }, out canonical),
            _ => TryNormalize(target, member, out canonical),
        };
    }

    private static bool TryLexeme(JsonNode? node, out string lexeme)
    {
        lexeme = node is JsonValue { } value && value.GetValueKind() == JsonValueKind.Number ? value.ToJsonString() : "";
        return lexeme.Length > 0 && DecimalPattern().IsMatch(lexeme);
    }

    private static decimal ParseDecimal(string text) =>
        decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private static string Quote(string text) => JsonText.Canonical(JsonValue.Create(text));

    private static string? IntegerText(JsonNode node)
    {
        if (node.GetValueKind() != JsonValueKind.Number
            || !decimal.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || decimal.Truncate(number) != number
            || Math.Abs(number) > (decimal)MaximumSafeInteger)
        {
            return null;
        }

        return ((long)number).ToString(CultureInfo.InvariantCulture);
    }

    private static string? MoneyText(JsonNode node)
    {
        if (node is not JsonObject money || money.Count != 2
            || JsonText.Text(money["amount"]) is not { } amount || !TryDecimal(amount, out _)
            || JsonText.Text(money["currency"]) is not { } currency || !CurrencyPattern().IsMatch(currency))
        {
            return null;
        }

        return JsonText.Canonical(new JsonObject { ["amount"] = amount, ["currency"] = currency });
    }

    [GeneratedRegex(@"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]{1,28})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalPattern();

    [GeneratedRegex(@"\A[A-Z]{3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();

    [GeneratedRegex(@"\A([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2})(?:\.[0-9]+)?(Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex InstantPattern();
}

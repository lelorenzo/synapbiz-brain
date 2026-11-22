using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Synapbiz.Brain.Spec.Json;

namespace Synapbiz.Brain.Spec;

/// <summary>
/// The lexical rules of the typed values of a rule document: <c>{ "type": ..., "value": ... }</c> with Integer and
/// Decimal written as strings so that no precision is lost in JSON.
/// </summary>
internal static partial class TaggedValues
{
    private const int MaximumIntegerLength = 20;
    private const int MaximumDecimalLength = 128;
    private const int MaximumDecimalScale = 28;
    private const string DecimalFormat = "0.############################";

    private static readonly BigInteger MaximumDecimalCoefficient = BigInteger.Parse("79228162514264337593543950335", CultureInfo.InvariantCulture);

    /// <summary>True when the value is a well-formed typed value of its own declared <c>type</c>.</summary>
    internal static bool IsReadable(JsonNode? input)
    {
        if (input is not JsonObject tagged || Text(tagged["type"]) is not { } type)
        {
            return false;
        }

        var value = tagged["value"];
        return type switch
        {
            "Code" => Text(value) is not null,
            "Enum" => Text(value) is { } label && tagged["allowedValues"] is JsonArray allowed
                && allowed.All(item => Text(item) is not null) && allowed.Any(item => Text(item) == label),
            "Boolean" => value is JsonValue flag && flag.TryGetValue<bool>(out _),
            "Integer" => Text(value) is { } integer && TryParseInteger(integer, out _),
            "Decimal" => Text(value) is { } number && TryParseDecimal(number, out _),
            "Money" => value is JsonObject money && Text(money["amount"]) is { } amount && TryParseDecimal(amount, out _)
                && Text(money["currency"]) is { } currency && CurrencyPattern().IsMatch(currency),
            "Date" => Text(value) is { Length: 10 } date && TryParseDate(date, out _),
            "Reference" => value is JsonObject reference && Text(reference["guid"]) is { Length: 36 } guid
                && Guid.TryParseExact(guid, "D", out _) && reference["labelSource"] is JsonObject source
                && Text(source["ownerModule"]) is { Length: > 0 } && Text(source["entityType"]) is { Length: > 0 },
            _ => false,
        };
    }

    internal static bool TryParseInteger(string text, out long number)
    {
        number = 0;
        return text.Length <= MaximumIntegerLength && NumberPattern().IsMatch(text)
            && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>Parses a decimal lexeme exactly, refusing more than 28 fraction digits or a coefficient above the decimal range.</summary>
    internal static bool TryParseDecimal(string text, out decimal number)
    {
        number = 0;
        if (text.Length > MaximumDecimalLength || !NumberPattern().IsMatch(text))
        {
            return false;
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var normalized = dot < 0 ? text : text.TrimEnd('0').TrimEnd('.');
        var coefficient = normalized.Replace(".", "", StringComparison.Ordinal);
        return !(dot >= 0 && normalized.Length - dot - 1 > MaximumDecimalScale)
            && BigInteger.Abs(BigInteger.Parse(coefficient, CultureInfo.InvariantCulture)) <= MaximumDecimalCoefficient
            && decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>
    /// The decimal the stored table holds for a decimal lexeme: the lexeme read as a decimal, written without trailing
    /// zeros and read again, so <c>1.50</c> and <c>1.5</c> give the same value with the same scale.
    /// </summary>
    internal static bool TryReadStored(string text, out decimal number)
    {
        number = 0;
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && decimal.TryParse(parsed.ToString(DecimalFormat, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    internal static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    [GeneratedRegex(@"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\A[A-Z]{3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();
}

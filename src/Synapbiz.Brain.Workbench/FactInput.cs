using System.Globalization;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Workbench;

/// <summary>The result of reading one fact's text.</summary>
/// <param name="IsValid">True when the text is a value of the fact (or empty for a nullable fact).</param>
/// <param name="Value">The typed value; null when the text was empty for a nullable fact, or when the text is not valid.</param>
/// <param name="ErrorCode">The <c>FactInput.Error*</c> code when the text is not valid; null otherwise.</param>
public sealed record FactInputResult(bool IsValid, DecisionValue? Value, string? ErrorCode);

/// <summary>The result of reading the texts of every fact of a kind.</summary>
/// <param name="Facts">The valid, non-null values by fact name.</param>
/// <param name="Errors">The error code of every invalid fact by fact name.</param>
public sealed record FactSetInputResult(IReadOnlyDictionary<string, DecisionValue> Facts, IReadOnlyDictionary<string, string> Errors)
{
    /// <summary>True when no fact has an error.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Reads what a person typed into a typed engine value, with a code for text that does not fit.</summary>
public static class FactInput
{
    /// <summary>The fact is required and the text is empty.</summary>
    public const string ErrorRequired = "workbench.fact.required";

    /// <summary>The text is not one of the fact's allowed values.</summary>
    public const string ErrorNotAllowed = "workbench.fact.value_not_allowed";

    /// <summary>The text is not <c>true</c> or <c>false</c>.</summary>
    public const string ErrorNotABoolean = "workbench.fact.not_a_boolean";

    /// <summary>The text is not a whole number.</summary>
    public const string ErrorNotAnInteger = "workbench.fact.not_an_integer";

    /// <summary>The text is not a decimal number.</summary>
    public const string ErrorNotADecimal = "workbench.fact.not_a_decimal";

    /// <summary>The text is not a date written <c>yyyy-MM-dd</c>.</summary>
    public const string ErrorNotADate = "workbench.fact.not_a_date";

    /// <summary>The text is not an amount and a three-letter currency separated by one space.</summary>
    public const string ErrorNotMoney = "workbench.fact.not_money";

    /// <summary>The text is not a GUID.</summary>
    public const string ErrorNotAReference = "workbench.fact.not_a_reference";

    /// <summary>
    /// Reads the text of one fact. Empty or blank text is valid with no value for a nullable fact and
    /// <see cref="ErrorRequired"/> otherwise; any other text is trimmed and read by the fact's type.
    /// </summary>
    /// <param name="fact">The fact the text is for.</param>
    /// <param name="text">What the person typed; null counts as empty.</param>
    public static FactInputResult Parse(FactDescriptor fact, string? text)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (string.IsNullOrWhiteSpace(text))
        {
            return fact.Nullable ? new FactInputResult(true, null, null) : Invalid(ErrorRequired);
        }

        var trimmed = text.Trim();
        return fact.Type switch
        {
            FactType.Code => Valid(DecisionValue.Code(trimmed)),
            FactType.Enum => fact.AllowedValues is null || fact.AllowedValues.Contains(trimmed, StringComparer.Ordinal)
                ? Valid(DecisionValue.Enum(trimmed))
                : Invalid(ErrorNotAllowed),
            FactType.Boolean => ParseBoolean(trimmed),
            FactType.Integer => long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
                ? Valid(DecisionValue.Integer(integer))
                : Invalid(ErrorNotAnInteger),
            FactType.Decimal => TryDecimal(trimmed, out var number) ? Valid(DecisionValue.Decimal(number)) : Invalid(ErrorNotADecimal),
            FactType.Date => DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? Valid(DecisionValue.Date(date))
                : Invalid(ErrorNotADate),
            FactType.Money => ParseMoney(trimmed),
            FactType.Reference => Guid.TryParse(trimmed, out var guid) ? Valid(DecisionValue.Reference(guid)) : Invalid(ErrorNotAReference),
            _ => throw new ArgumentOutOfRangeException(nameof(fact), fact.Type, "Unknown fact type."),
        };
    }

    /// <summary>
    /// Reads the texts of every fact of a kind that is not composed from another kind, in declaration order. A valid value goes
    /// to <see cref="FactSetInputResult.Facts"/>, an empty nullable fact is left out and an invalid one puts its code in
    /// <see cref="FactSetInputResult.Errors"/>. Keys of <paramref name="texts"/> that name no fact are ignored.
    /// </summary>
    /// <param name="kind">The decision kind.</param>
    /// <param name="texts">The typed texts by fact name; a fact with no entry counts as empty.</param>
    public static FactSetInputResult ParseAll(DecisionKindDescriptor kind, IReadOnlyDictionary<string, string?> texts)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(texts);

        var facts = new Dictionary<string, DecisionValue>(StringComparer.Ordinal);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fact in kind.Facts.Where(fact => fact.ComposedFromKind is null))
        {
            texts.TryGetValue(fact.Name, out var text);
            var result = Parse(fact, text);
            if (!result.IsValid)
            {
                errors[fact.Name] = result.ErrorCode!;
            }
            else if (result.Value is not null)
            {
                facts[fact.Name] = result.Value;
            }
        }

        return new FactSetInputResult(facts, errors);
    }

    private static FactInputResult Valid(DecisionValue value) => new(true, value, null);

    private static FactInputResult Invalid(string code) => new(false, null, code);

    private static bool TryDecimal(string text, out decimal number) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);

    private static FactInputResult ParseBoolean(string text)
    {
        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            return Valid(DecisionValue.Boolean(true));
        }

        return string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
            ? Valid(DecisionValue.Boolean(false))
            : Invalid(ErrorNotABoolean);
    }

    private static FactInputResult ParseMoney(string text)
    {
        var parts = text.Split(' ');
        return parts.Length == 2 && TryDecimal(parts[0], out var amount) && parts[1].Length == 3 && parts[1].All(letter => letter is >= 'A' and <= 'Z')
            ? Valid(DecisionValue.Money(amount, parts[1]))
            : Invalid(ErrorNotMoney);
    }
}

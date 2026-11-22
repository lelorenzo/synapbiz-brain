using System.Globalization;
using System.Text;
using System.Text.Json;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain;

namespace Nuvora.Decisions.Engine.Facts;

/// <summary>
/// Canonical JSON serialization and SHA-256 hashing of a fact value map
/// (D-04, D-11): keys sorted ordinally, invariant culture throughout,
/// decimals normalized to their shortest round-trip form so <c>1.50</c> and
/// <c>1.5</c> serialize identically, <c>Money</c> as <c>{"amount","currency"}</c>
/// and <c>Date</c> as <c>yyyy-MM-dd</c>. Hand-rolled (not
/// <see cref="System.Text.Json.JsonSerializer"/> number formatting) so the
/// exact byte sequence — and therefore the hash — never depends on a BCL or
/// library version. Pure: no clock, no I/O, no randomness (AT-DEC-02).
/// </summary>
/// <remarks>Introduced in P2-20.2 (paquete 0.A369).</remarks>
public static class CanonicalFacts
{
    /// <summary>Stable error code: the canonical fact-set JSON exceeds a kind's byte cap.</summary>
    public const string ErrorTooLarge = "decisions.facts.too_large";

    /// <summary>Serializes <paramref name="values"/> as canonical JSON (ordinal-sorted keys).</summary>
    public static string ToCanonicalJson(IReadOnlyDictionary<string, DecisionValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var sb = new StringBuilder();
        sb.Append('{');

        var first = true;
        foreach (var key in values.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            AppendJsonString(sb, key);
            sb.Append(':');
            AppendValue(sb, values[key]);
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>UTF-8 bytes of <see cref="ToCanonicalJson"/>.</summary>
    public static byte[] ToCanonicalJsonBytes(IReadOnlyDictionary<string, DecisionValue> values) =>
        Encoding.UTF8.GetBytes(ToCanonicalJson(values));

    /// <summary>SHA-256 of the canonical JSON's UTF-8 bytes.</summary>
    public static ContentHash Hash(IReadOnlyDictionary<string, DecisionValue> values) =>
        ContentHash.FromBytes(ToCanonicalJsonBytes(values));

    /// <summary>
    /// Throws <see cref="DecisionEngineException"/> with <see cref="ErrorTooLarge"/>
    /// when <paramref name="canonicalJsonBytes"/> exceeds <paramref name="maxBytes"/>.
    /// </summary>
    public static void EnsureWithinCap(byte[] canonicalJsonBytes, int maxBytes, string kindKey)
    {
        ArgumentNullException.ThrowIfNull(canonicalJsonBytes);

        if (canonicalJsonBytes.Length <= maxBytes)
        {
            return;
        }

        throw new DecisionEngineException(
            ErrorTooLarge,
            $"Canonical fact set for kind '{kindKey}' is {canonicalJsonBytes.Length} bytes, exceeding the {maxBytes}-byte cap.",
            new Dictionary<string, object?>
            {
                ["kind"] = kindKey,
                ["bytes"] = canonicalJsonBytes.Length,
                ["cap"] = maxBytes,
            });
    }

    /// <summary>
    /// Normalizes a decimal to its shortest round-trip textual form: trailing
    /// fractional zeros are stripped (<c>1.50</c> -&gt; <c>1.5</c>,
    /// <c>100.00</c> -&gt; <c>100</c>) and every zero (including a negative
    /// zero) normalizes to <c>"0"</c>, all independent of the ambient
    /// <see cref="CultureInfo.CurrentCulture"/>.
    /// </summary>
    internal static string NormalizeDecimal(decimal value)
    {
        if (value == 0m)
        {
            return "0";
        }

        var text = value.ToString(CultureInfo.InvariantCulture);
        if (!text.Contains('.', StringComparison.Ordinal))
        {
            return text;
        }

        var trimmed = text.TrimEnd('0');
        return trimmed.EndsWith('.') ? trimmed[..^1] : trimmed;
    }

    private static void AppendValue(StringBuilder sb, DecisionValue value)
    {
        switch (value)
        {
            case CodeValue v:
                AppendJsonString(sb, v.Value);
                break;
            case EnumValue v:
                AppendJsonString(sb, v.Value);
                break;
            case BooleanValue v:
                sb.Append(v.Value ? "true" : "false");
                break;
            case IntegerValue v:
                sb.Append(v.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case DecimalValue v:
                sb.Append(NormalizeDecimal(v.Value));
                break;
            case MoneyValue v:
                sb.Append("{\"amount\":").Append(NormalizeDecimal(v.Amount)).Append(",\"currency\":");
                AppendJsonString(sb, v.Currency);
                sb.Append('}');
                break;
            case DateValue v:
                AppendJsonString(sb, v.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                break;
            case ReferenceValue v:
                AppendJsonString(sb, v.Value.ToString("D", CultureInfo.InvariantCulture));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown DecisionValue case.");
        }
    }

    /// <summary>
    /// Appends <paramref name="value"/> as a fully-escaped, quoted JSON string.
    /// Delegates escaping to <see cref="JsonSerializer"/> (culture-insensitive
    /// for strings) rather than hand-rolling it, while every number/date/money
    /// case above is still hand-formatted for exact, version-independent bytes.
    /// </summary>
    private static void AppendJsonString(StringBuilder sb, string value) =>
        sb.Append(JsonSerializer.Serialize(value));
}

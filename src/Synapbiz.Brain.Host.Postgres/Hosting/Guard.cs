using System.Text.Json;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>Argument checks shared by the engine commands.</summary>
internal static class Guard
{
    internal const int MaxIdLength = 200;

    /// <summary>A tenant id, process id, business key, event id or task id: non-empty, at most 200 characters.</summary>
    internal static void Id(string? value, string name)
    {
        if (value is null)
        {
            throw new ArgumentNullException(name);
        }

        if (value.Length == 0 || value.Length > MaxIdLength)
        {
            throw new ArgumentException("The value must have between 1 and " + MaxIdLength + " characters.", name);
        }
    }

    /// <summary>An integer that must lie between two bounds, both included; the name is the one of the offending member (a record member, for a query record).</summary>
    internal static void InRange(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(name, value, "The value must be between " + minimum + " and " + maximum + ".");
        }
    }

    /// <summary>
    /// A text the cockpit queries take as a filter or a key: null is allowed, a control character (the NUL that PostgreSQL cannot
    /// store, a line break, and the rest) is an <see cref="ArgumentException"/>. It does not limit the length and is applied only on
    /// the paths of the cockpit queries.
    /// </summary>
    internal static void NoControlCharacters(string? value, string name)
    {
        if (value is not null && value.Any(char.IsControl))
        {
            throw new ArgumentException("The value must not contain a control character.", name);
        }
    }

    /// <summary>A text that must be the text of a JSON object.</summary>
    internal static void JsonObject(string? value, string name)
    {
        if (value is null)
        {
            throw new ArgumentNullException(name);
        }

        if (!IsJsonObject(value))
        {
            throw new ArgumentException("The value must be the text of a JSON object.", name);
        }
    }

    internal static bool IsJsonObject(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // A text that holds an unpaired surrogate cannot be turned into UTF-8, so it is not the text of any JSON value.
            return false;
        }
    }
}

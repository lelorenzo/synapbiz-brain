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
    }
}

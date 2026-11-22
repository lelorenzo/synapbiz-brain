using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Spec;

/// <summary>Small helpers over <see cref="JsonNode"/> shared by the validation steps.</summary>
internal static class Json
{
    internal const double MaximumSafeInteger = 9_007_199_254_740_991;

    /// <summary>The string a node holds, or null when the node is absent or not a string.</summary>
    internal static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>True when the node is the JSON literal <c>true</c>.</summary>
    internal static bool Flag(JsonNode? node) => node?.GetValueKind() == JsonValueKind.True;

    /// <summary>The numeric value of a JSON number node, or NaN when the node is absent or not a number.</summary>
    internal static double Number(JsonNode? node) => node?.GetValueKind() == JsonValueKind.Number
        && double.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        ? number : double.NaN;

    internal static bool IsSafeInteger(double number) => Math.Abs(number) <= MaximumSafeInteger && double.IsInteger(number);

    /// <summary>The elements of an array node with the JSON Pointer of each; empty when the node is not an array.</summary>
    internal static IEnumerable<(JsonNode? Value, string Path)> Items(JsonNode? node, string path) =>
        node is JsonArray array
            ? array.Select((value, index) => (value, Pointer(path, index.ToString(CultureInfo.InvariantCulture))))
            : [];

    /// <summary>The object elements of an array node; empty when the node is not an array.</summary>
    internal static IEnumerable<JsonObject> Objects(JsonNode? node) => (node as JsonArray)?.OfType<JsonObject>() ?? [];

    /// <summary>True when <paramref name="array"/> is an array holding the string <paramref name="text"/>.</summary>
    internal static bool Contains(JsonNode? array, string? text) =>
        text is not null && array is JsonArray values && values.Any(item => Text(item) == text);

    /// <summary>Appends an escaped member name to a JSON Pointer.</summary>
    internal static string Pointer(string path, string key) =>
        path + "/" + key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}

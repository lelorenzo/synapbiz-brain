using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>The two kinds of document the authoring tools work on.</summary>
public enum DocumentKind
{
    /// <summary>A <c>brain.process@1</c> document: a flow.</summary>
    Flow = 0,

    /// <summary>A <c>brain.rule@1</c> document: a rule.</summary>
    Rule = 1,
}

/// <summary>Small JSON helpers shared by the authoring tools: one formatting, tolerant reading, content hashes.</summary>
internal static class AuthoringJson
{
    private const string Slash = "/";

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The text of a node: indented with two spaces, <c>\n</c> line ends, no escaping beyond what JSON needs.</summary>
    internal static string Format(JsonNode? node) => node is null ? "null" : node.ToJsonString(Indented);

    /// <summary>The compact text of a node.</summary>
    internal static string CompactText(JsonNode? node) => node is null ? "null" : node.ToJsonString(Compact);

    /// <summary>Parses text as JSON; null when it is not JSON or repeats a member name.</summary>
    internal static JsonNode? TryParse(string? text)
    {
        if (text is null)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return null;
        }
    }

    internal static JsonObject? TryObject(string? text) => TryParse(text) as JsonObject;

    /// <summary>Appends a member name to a JSON Pointer, escaping <c>~</c> and the slash.</summary>
    internal static string Pointer(string path, string key) =>
        path + Slash + key.Replace("~", "~0", StringComparison.Ordinal).Replace(Slash, "~1", StringComparison.Ordinal);

    /// <summary>Appends an array index to a JSON Pointer.</summary>
    internal static string Pointer(string path, int index) =>
        path + Slash + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The string a node holds, or null when it is not a string.</summary>
    internal static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>The whole number a node holds, or null.</summary>
    internal static long? Whole(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var whole))
        {
            return whole;
        }

        return value.TryGetValue<double>(out var number) && double.IsInteger(number) && Math.Abs(number) < 9_007_199_254_740_992d ? (long)number : null;
    }

    /// <summary>The content hash of a document text as the library of that kind writes it: bare lower-case hex for a flow, <c>sha256:</c> and hex for a rule.</summary>
    internal static string Hash(DocumentKind kind, string text) =>
        kind == DocumentKind.Flow
            ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            : "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>The language of a request: <c>en</c> (the default) or <c>es</c>; null when it is anything else.</summary>
    internal static WorkbenchLocale? Locale(JsonNode? node)
    {
        if (node is null)
        {
            return WorkbenchLocale.En;
        }

        return Text(node) switch
        {
            "en" => WorkbenchLocale.En,
            "es" => WorkbenchLocale.Es,
            _ => null,
        };
    }

    internal static string Tag(WorkbenchLocale locale) => locale == WorkbenchLocale.Es ? "es" : "en";

    internal static JsonArray Array(IEnumerable<string> items) => new([.. items.Select(item => (JsonNode?)JsonValue.Create(item))]);
}

using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Workbench;

/// <summary>The one JSON formatting of drafts and proposals: two-space indent, <c>\n</c> line ends, non-ASCII letters not escaped.</summary>
internal static class DraftText
{
    /// <summary>Parses text as JSON; null when it is not JSON or repeats a member name.</summary>
    internal static JsonNode? TryParse(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            Visit(node);
            return node;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Walks the whole tree: System.Text.Json refuses a repeated member name only when the object is first read, so the walk makes
    /// a repeated name anywhere in the text fail here and not in the middle of an edit.
    /// </summary>
    private static void Visit(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject members:
                foreach (var member in members)
                {
                    Visit(member.Value);
                }

                break;
            case JsonArray items:
                foreach (var item in items)
                {
                    Visit(item);
                }

                break;
            default:
                break;
        }
    }

    /// <summary>The node written in the draft formatting.</summary>
    internal static string Format(JsonNode node)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var options = new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            node.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The string a node holds, or null when it is absent or not a string.</summary>
    internal static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>The whole number a node holds, or null when it is absent or not a whole number.</summary>
    internal static long? Whole(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<long>(out var number) ? number : null;
}

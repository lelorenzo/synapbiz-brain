using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>
/// JSON helpers of the flow surface: compact canonical text (members in ordinal order at every depth), parsing that never throws
/// (a text that repeats a member name is not usable and counts as invalid), and JSON equality.
/// </summary>
internal static class FlowJson
{
    private static readonly JsonWriterOptions Compact = new() { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Parses a JSON text; null when it is not valid JSON. The tree is read completely before it is returned: <see cref="JsonNode.Parse(string, JsonNodeOptions?, JsonDocumentOptions)"/>
    /// is lazy, and a text that repeats a member name would otherwise fail later, in the first call that walks the object.
    /// </summary>
    internal static JsonNode? TryParse(string? text, out bool valid)
    {
        valid = false;
        if (text is null)
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(text);
            _ = Canonical(node);
            valid = true;
            return node;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Parses a JSON text that must be an object; null when it is not.</summary>
    internal static JsonObject? TryObject(string? text) => TryParse(text, out var valid) is JsonObject node && valid ? node : null;

    /// <summary>The canonical text of a node: compact, members in ordinal order at every depth, scalars as written.</summary>
    internal static string Canonical(JsonNode? node)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Compact))
        {
            Write(writer, node);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>A copy of the node whose members are in ordinal order at every depth.</summary>
    internal static JsonNode? Sorted(JsonNode? node) => node switch
    {
        null => null,
        JsonObject members => SortedObject(members),
        JsonArray items => new JsonArray([.. items.Select(Sorted)]),
        _ => node.DeepClone(),
    };

    /// <summary>JSON equality: objects by member, arrays by position, numbers by value, everything else by text.</summary>
    internal static bool Equal(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return (left, right) switch
        {
            (JsonObject a, JsonObject b) => a.Count == b.Count && a.All(member => b.ContainsKey(member.Key) && Equal(member.Value, b[member.Key])),
            (JsonArray a, JsonArray b) => a.Count == b.Count && a.Zip(b).All(pair => Equal(pair.First, pair.Second)),
            (JsonValue a, JsonValue b) => ValueEqual(a, b),
            _ => false,
        };
    }

    /// <summary>A duration of whole seconds as the format writes it: <c>P1DT2H3M4S</c>.</summary>
    internal static string Duration(TimeSpan span)
    {
        var seconds = span.Ticks / TimeSpan.TicksPerSecond;
        var days = seconds / 86_400;
        var hours = seconds % 86_400 / 3_600;
        var minutes = seconds % 3_600 / 60;
        var rest = seconds % 60;
        var text = new StringBuilder("P");
        if (days > 0)
        {
            text.Append(days.ToString(CultureInfo.InvariantCulture)).Append('D');
        }

        if (hours + minutes + rest > 0)
        {
            text.Append('T');
            Part(text, hours, 'H');
            Part(text, minutes, 'M');
            Part(text, rest, 'S');
        }

        return text.ToString();
    }

    private static void Part(StringBuilder text, long value, char unit)
    {
        if (value > 0)
        {
            text.Append(value.ToString(CultureInfo.InvariantCulture)).Append(unit);
        }
    }

    private static JsonObject SortedObject(JsonObject members)
    {
        var copy = new JsonObject();
        foreach (var key in members.Select(member => member.Key).Order(StringComparer.Ordinal))
        {
            copy[key] = Sorted(members[key]);
        }

        return copy;
    }

    private static bool ValueEqual(JsonValue left, JsonValue right)
    {
        var kind = left.GetValueKind();
        if (kind != right.GetValueKind())
        {
            return false;
        }

        if (kind == JsonValueKind.Number
            && decimal.TryParse(left.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && decimal.TryParse(right.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
        {
            return a == b;
        }

        return string.Equals(left.ToJsonString(), right.ToJsonString(), StringComparison.Ordinal);
    }

    private static void Write(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject members:
                writer.WriteStartObject();
                foreach (var key in members.Select(member => member.Key).Order(StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    Write(writer, members[key]);
                }

                writer.WriteEndObject();
                break;
            case JsonArray items:
                writer.WriteStartArray();
                foreach (var item in items)
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }
}

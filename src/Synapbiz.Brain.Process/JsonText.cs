using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Process;

/// <summary>
/// Small helpers over <see cref="JsonNode"/>: parsing, canonical writing (compact, members in ordinal order at every
/// depth, scalars as written), JSON Pointers and JSON equality. Everything uses the invariant culture.
/// </summary>
internal static class JsonText
{
    private static readonly JsonWriterOptions Compact = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Parses JSON text; false when the text is not valid I-JSON (syntax, depth or duplicate member names).</summary>
    internal static bool TryParse(string? text, out JsonNode? node)
    {
        node = null;
        if (text is null)
        {
            return false;
        }

        try
        {
            node = JsonNode.Parse(text);
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return false;
        }

        if (HasDuplicateNames(text))
        {
            node = null;
            return false;
        }

        return true;
    }

    // Duplicate member names are not valid input anywhere. They are found eagerly, at every depth, so that no later access can throw.
    private static bool HasDuplicateNames(string text)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text));
        var objects = new Stack<HashSet<string>>();
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        objects.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.EndObject:
                        objects.Pop();
                        break;
                    case JsonTokenType.PropertyName when !objects.Peek().Add(reader.GetString()!):
                        return true;
                    default:
                        break;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    /// <summary>Parses JSON text that must be an object; null when it is not.</summary>
    internal static JsonObject? ParseObject(string? text) =>
        TryParse(text, out var node) ? node as JsonObject : null;

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

    /// <summary>The canonical text of a JSON text that is known to be valid; the text itself when it is not.</summary>
    internal static string CanonicalOf(string text) =>
        TryParse(text, out var node) ? Canonical(node) : text;

    /// <summary>Appends a member name to a JSON Pointer, escaping <c>~</c> and <c>/</c>.</summary>
    internal static string Pointer(string path, string key) =>
        path + "/" + key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    /// <summary>Appends an array index to a JSON Pointer.</summary>
    internal static string Pointer(string path, int index) =>
        path + "/" + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>The string a node holds, or null when the node is absent or not a string.</summary>
    internal static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>JSON equality: objects by member, arrays by position, numbers by value, everything else by text.</summary>
    internal static bool Equal(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return (left, right) switch
        {
            (JsonObject a, JsonObject b) => a.Count == b.Count
                && a.All(member => b.ContainsKey(member.Key) && Equal(member.Value, b[member.Key])),
            (JsonArray a, JsonArray b) => a.Count == b.Count && a.Zip(b).All(pair => Equal(pair.First, pair.Second)),
            (JsonValue a, JsonValue b) => ValueEqual(a, b),
            _ => false,
        };
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
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var key in obj.Select(member => member.Key).Order(StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    Write(writer, obj[key]);
                }

                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array)
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

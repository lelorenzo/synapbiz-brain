using System.Globalization;
using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Authoring;

/// <summary>An operation of a patch could not be applied.</summary>
public sealed class JsonPatchException : Exception
{
    /// <summary>Creates the exception.</summary>
    public JsonPatchException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public JsonPatchException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and its cause.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public JsonPatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The position of the failing operation, from 0; -1 when the patch as a whole is not valid.</summary>
    public int Index { get; init; } = -1;

    /// <summary>The JSON Pointer the failing operation names, when it has one.</summary>
    public string? Path { get; init; }
}

/// <summary>
/// RFC 6902 JSON Patch over <see cref="JsonNode"/>: <c>add</c>, <c>remove</c>, <c>replace</c>, <c>move</c>, <c>copy</c> and <c>test</c>. The patch is applied
/// to a copy; the document given is never changed, and a patch that fails at any operation changes nothing.
/// </summary>
public static class JsonPatch
{
    /// <summary>Applies the operations in order to a copy of the document.</summary>
    /// <param name="document">The document; it is not changed.</param>
    /// <param name="operations">An array of operation objects.</param>
    /// <exception cref="JsonPatchException">An operation is malformed or cannot be applied.</exception>
    public static JsonNode Apply(JsonNode document, JsonArray operations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(operations);
        var root = document.DeepClone();
        for (var index = 0; index < operations.Count; index++)
        {
            root = ApplyOne(root, operations[index], index);
        }

        return root;
    }

    private static JsonNode ApplyOne(JsonNode root, JsonNode? item, int index)
    {
        if (item is not JsonObject operation)
        {
            throw Fail(index, null, "The operation is not an object.");
        }

        var op = AuthoringJson.Text(operation["op"]);
        var path = AuthoringJson.Text(operation["path"]) ?? throw Fail(index, null, "The operation has no `path`.");
        try
        {
            return op switch
            {
                "add" => Add(root, path, Value(operation, index, path)),
                "remove" => Remove(root, path, out _),
                "replace" => Replace(root, path, Value(operation, index, path)),
                "move" => Move(root, operation, index, path),
                "copy" => Add(root, path, Read(root, From(operation, index, path)).DeepClone()),
                "test" => Test(root, path, Value(operation, index, path)),
                _ => throw Fail(index, path, "The operation `" + op + "` is not one of add, remove, replace, move, copy, test."),
            };
        }
        catch (JsonPatchException error) when (error.Index < 0)
        {
            throw Fail(index, path, error.Message);
        }
    }

    private static JsonNode? Value(JsonObject operation, int index, string path) =>
        operation.ContainsKey("value") ? operation["value"]?.DeepClone() : throw Fail(index, path, "The operation has no `value`.");

    private static string From(JsonObject operation, int index, string path) =>
        AuthoringJson.Text(operation["from"]) ?? throw Fail(index, path, "The operation has no `from`.");

    private static JsonNode Move(JsonNode root, JsonObject operation, int index, string path)
    {
        var from = From(operation, index, path);
        if (path.StartsWith(from + '/', StringComparison.Ordinal))
        {
            throw Fail(index, path, "A value cannot be moved into itself.");
        }

        var removed = Read(root, from);
        var without = Remove(root, from, out _);
        return Add(without, path, removed);
    }

    private static JsonNode Test(JsonNode root, string path, JsonNode? expected)
    {
        var actual = TryRead(root, path);
        if (!JsonNode.DeepEquals(actual, expected))
        {
            throw new JsonPatchException("The value at `" + path + "` is not the one the test expects.");
        }

        return root;
    }

    private static JsonNode Replace(JsonNode root, string path, JsonNode? value)
    {
        if (path.Length == 0)
        {
            return value ?? throw new JsonPatchException("The document cannot be replaced by null.");
        }

        _ = Read(root, path);
        var (parent, key) = Parent(root, path);
        Set(parent, key, value, adding: false);
        return root;
    }

    private static JsonNode Add(JsonNode root, string path, JsonNode? value)
    {
        if (path.Length == 0)
        {
            return value ?? throw new JsonPatchException("The document cannot be replaced by null.");
        }

        var (parent, key) = Parent(root, path);
        Set(parent, key, value, adding: true);
        return root;
    }

    private static JsonNode Remove(JsonNode root, string path, out JsonNode? removed)
    {
        if (path.Length == 0)
        {
            throw new JsonPatchException("The whole document cannot be removed.");
        }

        removed = Read(root, path);
        var (parent, key) = Parent(root, path);
        switch (parent)
        {
            case JsonObject owner:
                owner.Remove(key);
                break;
            case JsonArray list:
                list.RemoveAt(ArrayIndex(list, key, allowEnd: false));
                break;
            default:
                throw new JsonPatchException("The path `" + path + "` does not name a member.");
        }

        return root;
    }

    private static void Set(JsonNode parent, string key, JsonNode? value, bool adding)
    {
        switch (parent)
        {
            case JsonObject owner:
                owner[key] = value;
                break;
            case JsonArray list when adding:
                var at = key == "-" ? list.Count : ArrayIndex(list, key, allowEnd: true);
                list.Insert(at, value);
                break;
            case JsonArray list:
                list[ArrayIndex(list, key, allowEnd: false)] = value;
                break;
            default:
                throw new JsonPatchException("The parent of `" + key + "` is not an object or an array.");
        }
    }

    private static int ArrayIndex(JsonArray list, string key, bool allowEnd)
    {
        var plain = key.Length > 0 && key.All(char.IsAsciiDigit) && (key.Length == 1 || key[0] != '0');
        if (!plain || !int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var position) || position > list.Count || (position == list.Count && !allowEnd))
        {
            throw new JsonPatchException("`" + key + "` is not a position in the array.");
        }

        return position;
    }

    private static (JsonNode Parent, string Key) Parent(JsonNode root, string path)
    {
        var tokens = Tokens(path);
        var parent = root;
        foreach (var token in tokens.Take(tokens.Count - 1))
        {
            parent = Step(parent, token) ?? throw new JsonPatchException("`" + path + "` does not exist.");
        }

        return (parent, tokens[^1]);
    }

    private static JsonNode Read(JsonNode root, string path) =>
        TryRead(root, path) ?? throw new JsonPatchException("`" + path + "` does not exist.");

    private static JsonNode? TryRead(JsonNode root, string path)
    {
        JsonNode? current = root;
        foreach (var token in Tokens(path))
        {
            current = current is null ? null : Step(current, token);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static JsonNode? Step(JsonNode node, string token) => node switch
    {
        JsonObject owner => owner[token],
        JsonArray list when token.Length > 0 && token.All(char.IsAsciiDigit) && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var position) && position < list.Count => list[position],
        _ => null,
    };

    private static List<string> Tokens(string path)
    {
        if (path.Length == 0)
        {
            return [];
        }

        if (path[0] != '/')
        {
            throw new JsonPatchException("The path `" + path + "` is not a JSON Pointer: it must be empty or start with a slash.");
        }

        return [.. path[1..].Split('/').Select(token => token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))];
    }

    private static JsonPatchException Fail(int index, string? path, string message) =>
        new("Operation " + (index + 1).ToString(CultureInfo.InvariantCulture) + ": " + message) { Index = index, Path = path };
}

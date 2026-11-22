using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Synapbiz.Brain.Spec.Json;

namespace Synapbiz.Brain.Spec;

/// <summary>The identity of a section: its kind, stable id and format major.</summary>
internal sealed partial record SectionIdentity(string Kind, string Id, long Major)
{
    private const long MaximumVersion = 9_007_199_254_740_991;

    internal string Address => Kind + "/" + Id;

    internal string Format => Kind + "@" + Major.ToString(CultureInfo.InvariantCulture);

    internal static SectionIdentity? Read(JsonNode? document)
    {
        if (document is not JsonObject root || Text(root["id"]) is not { } id || !IsId(id)
            || Text(root["$spec"]) is not { } format)
        {
            return null;
        }

        var match = FormatPattern().Match(format);
        return match.Success && long.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out var major)
            && major <= MaximumVersion
            ? new SectionIdentity(match.Groups[1].Value, id, major)
            : null;
    }

    internal static bool IsId(string id) => IdPattern().IsMatch(id);

    internal static bool IsAddress(string address) => AddressPattern().IsMatch(address);

    /// <summary>The version of a node: a positive whole number that JSON can carry without loss, else null.</summary>
    internal static long? Version(JsonNode? node)
    {
        if (node is JsonValue value
            && decimal.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var version)
            && version >= 1 && version <= MaximumVersion && version == decimal.Truncate(version))
        {
            return (long)version;
        }

        return null;
    }

    [GeneratedRegex(@"\A([a-z][a-z0-9_]*\.[a-z][a-z0-9_]*)@([1-9][0-9]*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex FormatPattern();

    [GeneratedRegex(@"\A[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*/[A-Za-z0-9_][A-Za-z0-9_.-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex AddressPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}

/// <summary>Step 1 and 2 of loading: the envelope, the JSON Schema and the one structural check the schema cannot express.</summary>
internal static class StructureValidation
{
    internal static IEnumerable<BrainSpecError> Validate(JsonNode? document)
    {
        var identity = SectionIdentity.Read(document);
        if (identity is null)
        {
            yield return Diagnostics.Envelope();
        }
        else if (identity.Format != RuleSchema.Format)
        {
            yield return Diagnostics.UnknownFormat();
        }
        else
        {
            foreach (var path in RuleSchema.Violations(document))
            {
                yield return Diagnostics.Schema(path);
            }
        }

        foreach (var error in GoldensWithoutOutcome(document))
        {
            yield return error;
        }
    }

    private static IEnumerable<BrainSpecError> GoldensWithoutOutcome(JsonNode? document)
    {
        if (document is not JsonObject root || Text(root["$spec"]) != RuleSchema.Format || root["body"] is not JsonObject body)
        {
            yield break;
        }

        foreach (var (golden, path) in Items(body["goldens"], "/body/goldens"))
        {
            if (golden is JsonObject goldenObject && goldenObject["expect"] is JsonObject expect
                && Text(expect["row"]) is not null && !expect.ContainsKey("noDecision") && !expect.ContainsKey("outcome"))
            {
                yield return Diagnostics.GoldenOutcomeRequired(path + "/expect/outcome");
            }
        }
    }
}

/// <summary>The checks every section node goes through: ids and versions, references to other sections, translation keys.</summary>
internal static class NodeValidation
{
    internal static IEnumerable<BrainSpecError> Validate(JsonObject document, IReadOnlyCollection<string>? knownSectionRefs)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var identity = SectionIdentity.Read(document)!;
        foreach (var (value, path) in Walk(document))
        {
            if (path.Length == 0 || value.ContainsKey("id") || value.ContainsKey("version"))
            {
                var id = Text(value["id"]);
                if (id is null || !SectionIdentity.IsId(id) || SectionIdentity.Version(value["version"]) is null)
                {
                    yield return Diagnostics.Node(path);
                }
                else if (!ids.Add(id))
                {
                    yield return Diagnostics.DuplicateId(Pointer(path, "id"));
                }
            }

            if (value.TryGetPropertyValue("$ref", out var reference))
            {
                var error = CheckReference(identity.Address, knownSectionRefs, Text(reference), Pointer(path, "$ref"));
                if (error is not null)
                {
                    yield return error;
                }
            }

            if (!value.TryGetPropertyValue("$t", out var translation))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(Text(translation)))
            {
                yield return Diagnostics.Translation(Pointer(path, "$t"));
            }
        }
    }

    /// <summary>
    /// A reference resolves when it names the section itself (a cycle) or a section in the caller's list of known
    /// sections. The loader sees addresses only, so it cannot follow references through the other sections.
    /// </summary>
    private static BrainSpecError? CheckReference(
        string source, IReadOnlyCollection<string>? knownSectionRefs, string? target, string path)
    {
        if (target is null || !SectionIdentity.IsAddress(target))
        {
            return Diagnostics.Reference(path);
        }

        if (target == source)
        {
            return Diagnostics.Cycle(path);
        }

        return knownSectionRefs is not null && knownSectionRefs.Contains(target, StringComparer.Ordinal)
            ? null
            : Diagnostics.Reference(path);
    }

    /// <summary>Every object of the document in document order, without the reserved metadata members.</summary>
    private static IEnumerable<(JsonObject Value, string Path)> Walk(JsonNode? root)
    {
        var pending = new Stack<(JsonNode? Value, string Path)>();
        pending.Push((root, ""));
        while (pending.TryPop(out var item))
        {
            var (value, path) = item;
            if (value is JsonObject obj)
            {
                yield return (obj, path);
                foreach (var field in obj.OrderByDescending(field => field.Key, StringComparer.Ordinal))
                {
                    if (!IsMetadata(field.Key) && (path.Length != 0 || field.Key != "translations"))
                    {
                        pending.Push((field.Value, Pointer(path, field.Key)));
                    }
                }
            }
            else if (value is JsonArray array)
            {
                for (var index = array.Count - 1; index >= 0; index--)
                {
                    pending.Push((array[index], Pointer(path, index.ToString(CultureInfo.InvariantCulture))));
                }
            }
        }
    }

    private static bool IsMetadata(string key) => key.StartsWith('$') && key is not ("$spec" or "$ref" or "$t");
}

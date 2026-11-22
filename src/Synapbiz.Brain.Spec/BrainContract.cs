using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Kinds;

namespace Synapbiz.Brain.Spec;

/// <summary>
/// A module contract: the decision kinds a module declares, with the facts and outcome fields of each. It is the
/// vocabulary a <c>brain.rule@1</c> document is checked against. This file uses only the decision contracts and
/// System.Text.Json so that it compiles unchanged against any copy of the decision contracts.
/// </summary>
public sealed class BrainContract
{
    private const string Malformed = "brain.contract.malformed";

    private readonly List<DecisionKindDescriptor> kinds;
    private readonly Dictionary<string, DecisionKindDescriptor> kindsById;
    private readonly Dictionary<string, ContractKindSource> sources;

    private BrainContract(
        string module,
        int schemaVersion,
        List<DecisionKindDescriptor> kinds,
        Dictionary<string, DecisionKindDescriptor> kindsById,
        Dictionary<string, ContractKindSource> sources)
    {
        Module = module;
        SchemaVersion = schemaVersion;
        this.kinds = kinds;
        this.kindsById = kindsById;
        this.sources = sources;
    }

    /// <summary>The module that owns the contract (for example <c>sales</c>).</summary>
    public string Module { get; }

    /// <summary>The contract's schema version; a rule names it in <c>requires</c> as <c>&lt;module&gt;@&lt;version&gt;</c>.</summary>
    public int SchemaVersion { get; }

    /// <summary>The address a rule uses to require this contract: <c>&lt;module&gt;@&lt;schemaVersion&gt;</c>.</summary>
    public string Address => string.Create(CultureInfo.InvariantCulture, $"{Module}@{SchemaVersion}");

    /// <summary>The decision kinds the contract declares, in file order.</summary>
    public IReadOnlyList<DecisionKindDescriptor> Kinds => kinds;

    /// <summary>Finds a declared decision kind by its identifier (for example <c>sales.order_approval_routing</c>).</summary>
    public bool TryGetKind(string kindId, [NotNullWhen(true)] out DecisionKindDescriptor? kind)
    {
        ArgumentNullException.ThrowIfNull(kindId);
        return kindsById.TryGetValue(kindId, out kind);
    }

    /// <summary>
    /// Reads a contract file. Throws <see cref="DecisionEngineException"/> with the code
    /// <c>brain.contract.malformed</c>, naming the JSON path of the first defect, when the file is not a valid contract.
    /// </summary>
    public static BrainContract Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var root = ParseRoot(json);
        var module = RequiredString(root, "module", "$");
        var schemaVersion = RequiredInteger(root["schemaVersion"], "$.schemaVersion", 1);

        var kinds = new List<DecisionKindDescriptor>();
        var kindsById = new Dictionary<string, DecisionKindDescriptor>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kindObjects = new List<(string Id, JsonObject Kind, string Path)>();
        var kindIndex = 0;
        foreach (var item in OptionalArray(root, "kinds", "$"))
        {
            var path = IndexPath("$.kinds", kindIndex++);
            var kind = AsObject(item, path);
            var id = RequiredString(kind, "id", path);
            if (!seen.Add(id))
            {
                throw Fail(path + ".id", $"the kind '{id}' is declared twice");
            }

            kindObjects.Add((id, kind, path));
        }

        var factsByKind = kindObjects.ToDictionary(entry => entry.Id, _ => new List<FactDescriptor>(), StringComparer.Ordinal);
        var rawFacts = kindObjects.ToDictionary(entry => entry.Id, _ => new Dictionary<string, JsonObject>(StringComparer.Ordinal), StringComparer.Ordinal);
        var factIndex = 0;
        foreach (var item in OptionalArray(root, "facts", "$"))
        {
            var path = IndexPath("$.facts", factIndex++);
            var fact = AsObject(item, path);
            var kindId = RequiredString(fact, "kind", path);
            if (!factsByKind.TryGetValue(kindId, out var list))
            {
                throw Fail(path + ".kind", $"the fact points to the kind '{kindId}', which the contract does not declare");
            }

            var name = RequiredString(fact, "name", path);
            var descriptor = new FactDescriptor(
                name,
                RequiredType(fact, path),
                OptionalFlag(fact, "nullable", path),
                OptionalAllowedValues(fact, path),
                ComposedFromKind: null,
                LabelSource: OptionalLabelSource(fact, path),
                CatalogFactId: OptionalString(fact, "catalogFactId", path));
            if (!rawFacts[kindId].TryAdd(name, (JsonObject)fact.DeepClone()))
            {
                throw Fail(path + ".name", $"the kind '{kindId}' declares the fact '{name}' twice");
            }

            list.Add(descriptor);
        }

        var sources = new Dictionary<string, ContractKindSource>(StringComparer.Ordinal);
        foreach (var (id, kind, path) in kindObjects)
        {
            var outcomes = new List<OutcomeFieldDescriptor>();
            var rawOutcomes = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            var outcomeIndex = 0;
            foreach (var item in OptionalArray(kind, "outcomes", path))
            {
                var outcomePath = IndexPath(path + ".outcomes", outcomeIndex++);
                var outcome = AsObject(item, outcomePath);
                var name = RequiredString(outcome, "name", outcomePath);
                outcomes.Add(new OutcomeFieldDescriptor(
                    name,
                    RequiredType(outcome, outcomePath),
                    OptionalFlag(outcome, "nullable", outcomePath),
                    OptionalLabelSource(outcome, outcomePath)));
                if (!rawOutcomes.TryAdd(name, (JsonObject)outcome.DeepClone()))
                {
                    throw Fail(outcomePath + ".name", $"the kind '{id}' declares the outcome field '{name}' twice");
                }
            }

            var permissions = kind["permissions"] as JsonObject
                ?? throw Fail(path + ".permissions", "expected an object");
            var descriptor = new DecisionKindDescriptor
            {
                Key = ReadKey(id, path + ".id"),
                SchemaVersion = RequiredInteger(kind["schemaVersion"], path + ".schemaVersion", 1),
                Facts = factsByKind[id],
                OutcomeFields = outcomes,
                EvaluationMode = ReadMode(kind, path),
                ReadPermission = RequiredString(permissions, "read", path + ".permissions"),
                AuthorPermission = RequiredString(permissions, "author", path + ".permissions"),
                ApprovePermission = RequiredString(permissions, "approve", path + ".permissions"),
                ReadTracePermission = RequiredString(permissions, "readTrace", path + ".permissions"),
            };
            kinds.Add(descriptor);
            kindsById[id] = descriptor;
            sources.Add(id, new ContractKindSource(rawFacts[id], rawOutcomes));
        }

        return new BrainContract(module, schemaVersion, kinds, kindsById, sources);
    }

    /// <summary>The contract's own JSON entries for a kind, used to check a rule against the contract's wording.</summary>
    internal bool TryGetSource(string kindId, [NotNullWhen(true)] out ContractKindSource? source) =>
        sources.TryGetValue(kindId, out source);

    private static JsonObject ParseRoot(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new DecisionEngineException(
                Malformed,
                $"$: the contract is not valid JSON: {error.Message}",
                new Dictionary<string, object?> { ["path"] = "$" },
                error);
        }

        return node as JsonObject ?? throw Fail("$", "expected an object");
    }

    private static DecisionKindKey ReadKey(string id, string path)
    {
        try
        {
            return DecisionKindKey.From(id);
        }
        catch (DecisionEngineException error)
        {
            throw new DecisionEngineException(
                Malformed,
                $"{path}: '{id}' is not a valid decision kind key.",
                new Dictionary<string, object?> { ["path"] = path },
                error);
        }
    }

    private static EvaluationMode ReadMode(JsonObject kind, string path) => RequiredString(kind, "evaluationMode", path) switch
    {
        "Materialized" => EvaluationMode.Materialized,
        "OnDemand" => EvaluationMode.OnDemand,
        var other => throw Fail(path + ".evaluationMode", $"unknown evaluation mode '{other}'"),
    };

    private static FactType RequiredType(JsonObject owner, string path) => RequiredString(owner, "type", path) switch
    {
        "Code" => FactType.Code,
        "Enum" => FactType.Enum,
        "Boolean" => FactType.Boolean,
        "Integer" => FactType.Integer,
        "Decimal" => FactType.Decimal,
        "Money" => FactType.Money,
        "Date" => FactType.Date,
        "Reference" => FactType.Reference,
        var other => throw Fail(path + ".type", $"unknown fact type '{other}'"),
    };

    private static string RequiredString(JsonObject owner, string name, string path)
    {
        var node = owner[name];
        if (node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0)
        {
            return text;
        }

        var problem = node is null ? "the property is required" : "expected a non-empty string";
        throw Fail($"{path}.{name}", problem);
    }

    private static string? OptionalString(JsonObject owner, string name, string path)
    {
        var node = owner[name];
        if (node is null)
        {
            return null;
        }

        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : throw Fail($"{path}.{name}", "expected a string or null");
    }

    private static bool OptionalFlag(JsonObject owner, string name, string path)
    {
        var node = owner[name];
        if (node is null)
        {
            return false;
        }

        var kind = node.GetValueKind();
        if (kind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Fail($"{path}.{name}", "expected true or false");
        }

        return kind == JsonValueKind.True;
    }

    private static int RequiredInteger(JsonNode? node, string path, int minimum)
    {
        if (node is JsonValue value && node.GetValueKind() == JsonValueKind.Number
            && value.TryGetValue<int>(out var number) && number >= minimum)
        {
            return number;
        }

        var problem = node is null ? "the property is required" : $"expected a whole number of at least {minimum}";
        throw Fail(path, problem);
    }

    private static List<string>? OptionalAllowedValues(JsonObject owner, string path)
    {
        var node = owner["allowedValues"];
        if (node is null)
        {
            return null;
        }

        if (node is not JsonArray array)
        {
            throw Fail(path + ".allowedValues", "expected an array of strings or null");
        }

        var values = new List<string>(array.Count);
        for (var index = 0; index < array.Count; index++)
        {
            values.Add(array[index] is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : throw Fail(IndexPath(path + ".allowedValues", index), "expected a string"));
        }

        return values.Count == 0 ? null : values;
    }

    private static ReferenceLabelSource? OptionalLabelSource(JsonObject owner, string path)
    {
        var node = owner["labelSource"];
        if (node is null)
        {
            return null;
        }

        var source = node as JsonObject ?? throw Fail(path + ".labelSource", "expected an object or null");
        return new ReferenceLabelSource(
            RequiredString(source, "ownerModule", path + ".labelSource"),
            RequiredString(source, "entityType", path + ".labelSource"));
    }

    private static IEnumerable<JsonNode?> OptionalArray(JsonObject owner, string name, string path)
    {
        var node = owner[name];
        if (node is null)
        {
            return [];
        }

        return node as JsonArray ?? throw Fail($"{path}.{name}", "expected an array");
    }

    private static JsonObject AsObject(JsonNode? node, string path) =>
        node as JsonObject ?? throw Fail(path, "expected an object");

    private static string IndexPath(string path, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]");

    private static DecisionEngineException Fail(string path, string problem) => new(
        Malformed,
        $"{path}: {problem}.",
        new Dictionary<string, object?> { ["path"] = path, ["problem"] = problem });
}

/// <summary>The contract's own JSON entries for one kind: its facts and its outcome fields by name.</summary>
internal sealed record ContractKindSource(
    IReadOnlyDictionary<string, JsonObject> Facts,
    IReadOnlyDictionary<string, JsonObject> Outcomes);

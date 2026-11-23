using System.Text.Json.Nodes;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>The result of reading a catalog document.</summary>
/// <param name="Catalog">The catalog; null when <paramref name="Errors"/> is not empty.</param>
/// <param name="Errors">Every defect found, ordered by path.</param>
public sealed record CatalogParseResult(AuthoringCatalog? Catalog, IReadOnlyList<WorkbenchError> Errors)
{
    /// <summary>True when the document was read without errors.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Writes, reads and checks the <c>brain.catalog@1</c> document.</summary>
public static class CatalogJson
{
    /// <summary>The value of <c>$spec</c>.</summary>
    public const string Spec = "brain.catalog@1";

    /// <summary>The text is not JSON.</summary>
    public const string ErrorJsonInvalid = "authoring.catalog.json_invalid";

    /// <summary>The <c>$spec</c> is not <c>brain.catalog@1</c>.</summary>
    public const string ErrorUnknownFormat = "authoring.catalog.unknown_format";

    /// <summary>A member is missing, of the wrong kind or empty.</summary>
    public const string ErrorMalformed = "authoring.catalog.malformed";

    /// <summary>A name is used twice in one list.</summary>
    public const string ErrorDuplicate = "authoring.catalog.duplicate";

    /// <summary>A contract does not parse.</summary>
    public const string ErrorContract = "authoring.catalog.contract_invalid";

    private static readonly string[] PortKinds = ["read", "action", "ai"];
    private static readonly string[] Deliveries = ["atLeastOnce", "atMostOnce", "either"];

    /// <summary>Writes the catalog as indented JSON in a fixed order.</summary>
    /// <param name="catalog">The catalog.</param>
    public static string Serialize(AuthoringCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var node = new JsonObject { ["$spec"] = Spec };
        if (catalog.Ports is { } ports)
        {
            node["ports"] = new JsonArray([.. ports.Select(port =>
            {
                var item = new JsonObject
                {
                    ["name"] = port.Name,
                    ["kind"] = port.Kind,
                    ["delivery"] = port.Delivery,
                    ["contactsPerson"] = port.ContactsPerson,
                };
                if (port.Description is not null)
                {
                    item["description"] = port.Description;
                }

                if (port.InputSchema is not null)
                {
                    item["inputSchema"] = port.InputSchema.DeepClone();
                }

                if (port.OutputSchema is not null)
                {
                    item["outputSchema"] = port.OutputSchema.DeepClone();
                }

                return (JsonNode?)item;
            })]);
        }

        Names(node, "events", catalog.Events);
        Names(node, "queues", catalog.Queues);
        if (catalog.Stages is { } stages)
        {
            node["stages"] = AuthoringJson.Array(stages);
        }

        if (catalog.ContractDocuments.Count > 0)
        {
            node["contracts"] = new JsonArray([.. catalog.ContractDocuments.Select(document => document.DeepClone())]);
        }

        return AuthoringJson.Format(node);
    }

    /// <summary>Reads a catalog document without throwing on its content; every defect is collected, ordered by path.</summary>
    /// <param name="json">The text.</param>
    public static CatalogParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (AuthoringJson.TryParse(json) is not JsonObject document)
        {
            return Failed(ErrorJsonInvalid, string.Empty, "The text is not a JSON object.");
        }

        if (AuthoringJson.Text(document["$spec"]) != Spec)
        {
            return Failed(ErrorUnknownFormat, "/$spec", "The format is not brain.catalog@1.");
        }

        var errors = new List<WorkbenchError>();
        IReadOnlyList<CatalogPort>? ports = null;
        if (document.ContainsKey("ports"))
        {
            ports = ReadPorts(document["ports"], errors);
        }

        var events = document.ContainsKey("events") ? ReadNames(document["events"], "/events", errors) : null;
        var queues = document.ContainsKey("queues") ? ReadNames(document["queues"], "/queues", errors) : null;
        IReadOnlyList<string>? stages = null;
        if (document.ContainsKey("stages"))
        {
            stages = ReadStages(document["stages"], errors);
        }

        var contracts = new List<JsonObject>();
        if (document["contracts"] is { } contractNode)
        {
            ReadContracts(contractNode, contracts, errors);
        }

        foreach (var key in document.Select(member => member.Key).Where(key => key is not ("$spec" or "ports" or "events" or "queues" or "stages" or "contracts")))
        {
            errors.Add(new WorkbenchError(ErrorMalformed, AuthoringJson.Pointer(string.Empty, key), "The member is not part of the format."));
        }

        if (errors.Count > 0)
        {
            return new CatalogParseResult(null, [.. errors.OrderBy(error => error.Path, StringComparer.Ordinal)]);
        }

        return new CatalogParseResult(new AuthoringCatalog { Ports = ports, Events = events, Queues = queues, Stages = stages, ContractDocuments = contracts }, []);
    }

    private static CatalogParseResult Failed(string code, string path, string message) => new(null, [new WorkbenchError(code, path, message)]);

    private static void Names(JsonObject node, string member, IReadOnlyList<CatalogName>? names)
    {
        if (names is null)
        {
            return;
        }

        node[member] = new JsonArray([.. names.Select(name =>
        {
            var item = new JsonObject { ["name"] = name.Name };
            if (name.Description is not null)
            {
                item["description"] = name.Description;
            }

            return (JsonNode?)item;
        })]);
    }

    private static List<CatalogPort>? ReadPorts(JsonNode? node, List<WorkbenchError> errors)
    {
        if (node is not JsonArray items)
        {
            errors.Add(new WorkbenchError(ErrorMalformed, "/ports", "Expected an array."));
            return null;
        }

        var ports = new List<CatalogPort>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < items.Count; index++)
        {
            var path = "/ports/" + index;
            if (items[index] is not JsonObject item)
            {
                errors.Add(new WorkbenchError(ErrorMalformed, path, "Expected an object."));
                continue;
            }

            var name = RequiredText(item, "name", path, errors);
            var kind = RequiredText(item, "kind", path, errors);
            if (kind is not null && !PortKinds.Contains(kind, StringComparer.Ordinal))
            {
                errors.Add(new WorkbenchError(ErrorMalformed, path + "/kind", "Expected read, action or ai."));
            }

            var delivery = item.ContainsKey("delivery") ? AuthoringJson.Text(item["delivery"]) : "either";
            if (delivery is null || !Deliveries.Contains(delivery, StringComparer.Ordinal))
            {
                errors.Add(new WorkbenchError(ErrorMalformed, path + "/delivery", "Expected atLeastOnce, atMostOnce or either."));
            }

            bool? contacts = false;
            if (item["contactsPerson"] is not null)
            {
                contacts = item["contactsPerson"] is JsonValue flag && flag.TryGetValue<bool>(out var yes) ? yes : null;
            }

            if (contacts is null)
            {
                errors.Add(new WorkbenchError(ErrorMalformed, path + "/contactsPerson", "Expected true or false."));
            }

            if (name is not null && !seen.Add(name))
            {
                errors.Add(new WorkbenchError(ErrorDuplicate, path + "/name", "The port name is used twice."));
            }

            if (name is not null && kind is not null && delivery is not null && contacts is not null)
            {
                ports.Add(new CatalogPort(name, kind, delivery, contacts.Value, AuthoringJson.Text(item["description"]), item["inputSchema"] as JsonObject, item["outputSchema"] as JsonObject));
            }
        }

        return ports;
    }

    private static List<CatalogName>? ReadNames(JsonNode? node, string path, List<WorkbenchError> errors)
    {
        if (node is not JsonArray items)
        {
            errors.Add(new WorkbenchError(ErrorMalformed, path, "Expected an array."));
            return null;
        }

        var names = new List<CatalogName>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < items.Count; index++)
        {
            var at = path + "/" + index;
            if (items[index] is not JsonObject item)
            {
                errors.Add(new WorkbenchError(ErrorMalformed, at, "Expected an object."));
                continue;
            }

            var name = RequiredText(item, "name", at, errors);
            if (name is not null && !seen.Add(name))
            {
                errors.Add(new WorkbenchError(ErrorDuplicate, at + "/name", "The name is used twice."));
            }

            if (name is not null)
            {
                names.Add(new CatalogName(name, AuthoringJson.Text(item["description"])));
            }
        }

        return names;
    }

    private static List<string>? ReadStages(JsonNode? node, List<WorkbenchError> errors)
    {
        if (node is not JsonArray items || items.Any(item => string.IsNullOrEmpty(AuthoringJson.Text(item))))
        {
            errors.Add(new WorkbenchError(ErrorMalformed, "/stages", "Expected an array of names."));
            return null;
        }

        var stages = items.Select(item => AuthoringJson.Text(item)!).ToList();
        if (stages.Distinct(StringComparer.Ordinal).Count() != stages.Count)
        {
            errors.Add(new WorkbenchError(ErrorDuplicate, "/stages", "A stage name is used twice."));
        }

        return stages;
    }

    private static void ReadContracts(JsonNode node, List<JsonObject> contracts, List<WorkbenchError> errors)
    {
        if (node is not JsonArray items)
        {
            errors.Add(new WorkbenchError(ErrorMalformed, "/contracts", "Expected an array."));
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var path = "/contracts/" + index;
            if (items[index] is not JsonObject contract)
            {
                errors.Add(new WorkbenchError(ErrorMalformed, path, "Expected an object."));
                continue;
            }

            try
            {
                _ = BrainContract.Parse(AuthoringJson.CompactText(contract));
                contracts.Add(contract);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                errors.Add(new WorkbenchError(ErrorContract, path, "The contract does not parse: " + error.Message));
            }
        }
    }

    private static string? RequiredText(JsonObject owner, string name, string path, List<WorkbenchError> errors)
    {
        var text = AuthoringJson.Text(owner[name]);
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add(new WorkbenchError(ErrorMalformed, AuthoringJson.Pointer(path, name), "Expected a non-empty text."));
            return null;
        }

        return text;
    }
}

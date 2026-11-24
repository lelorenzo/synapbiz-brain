using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>A workspace with every reference process approved, with their rules, contracts and a catalog derived from what they use.</summary>
internal static class ReferenceWorkspace
{
    internal static (AuthoringTools Tools, InMemoryAuthoringStore Store) Rig(AuthoringOptions? options = null)
    {
        var flows = ReferenceDocs.Ids.ToDictionary(id => id, ReferenceDocs.Text, StringComparer.Ordinal);
        var rules = new Dictionary<string, string>(StringComparer.Ordinal);
        var contracts = new List<JsonObject>();
        foreach (var id in ReferenceDocs.Ids)
        {
            var folder = Path.GetDirectoryName(Locate("samples/process-reference/" + id + "/process.json"))!;
            foreach (var path in Directory.GetFiles(folder, "*.rule.json"))
            {
                var text = File.ReadAllText(path);
                rules[(string)JsonNode.Parse(text)!["id"]!] = text;
            }

            var contract = Path.Combine(folder, "contract.json");
            if (File.Exists(contract))
            {
                contracts.Add((JsonObject)JsonNode.Parse(File.ReadAllText(contract))!);
            }
        }

        var ports = new Dictionary<string, CatalogPort>(StringComparer.Ordinal);
        var queues = new HashSet<string>(StringComparer.Ordinal);
        var events = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in flows.Values)
        {
            foreach (var step in ((JsonObject)JsonNode.Parse(text)!["body"]!["steps"]!).Select(pair => pair.Value!.AsObject()))
            {
                var kind = step["kind"]!.GetValue<string>();
                if (step["port"]?.GetValue<string>() is { } port)
                {
                    var portKind = kind switch { "action" => "action", "ai" => "ai", _ => "read" };
                    ports[port] = new CatalogPort(port, portKind, portKind == "action" ? "either" : "atLeastOnce", step["contact"]?.GetValue<bool>() == true, "Does one thing for a visit.", null, null);
                }

                _ = step["queue"] is { } queue && queues.Add(queue.GetValue<string>());
                _ = step["event"] is { } name && events.Add(name.GetValue<string>());
            }
        }

        var catalog = new AuthoringCatalog
        {
            Ports = [.. ports.Values],
            Events = [.. events.Select(name => new CatalogName(name, null))],
            Queues = [.. queues.Select(name => new CatalogName(name, null))],
            ContractDocuments = contracts,
        };
        var store = new InMemoryAuthoringStore(flows, rules, CatalogJson.Serialize(catalog));
        var tools = new AuthoringTools(store, (options ?? new AuthoringOptions()) with { Clock = new FakeTimeProvider(Start) });
        return (tools, store);
    }
}

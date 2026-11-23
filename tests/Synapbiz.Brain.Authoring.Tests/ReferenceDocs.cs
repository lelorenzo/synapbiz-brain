using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>Loads the reference processes of the repository, optionally changed by a JSON Patch, the way a host loads them.</summary>
internal static class ReferenceDocs
{
    internal static readonly string[] Ids = ["previsit.outreach", "visit.note_wait", "postvisit.coding_review", "collection.followup", "case.lifecycle", "practice.daily_refresh"];

    private static string Folder(string id) => Path.GetDirectoryName(Locate("samples/process-reference/" + id + "/process.json"))!;

    internal static string Text(string id) => File.ReadAllText(Path.Combine(Folder(id), "process.json"));

    internal static IReadOnlyList<BrainRule> Rules(string id)
    {
        var contractPath = Path.Combine(Folder(id), "contract.json");
        var contracts = File.Exists(contractPath) ? new[] { BrainContract.Parse(File.ReadAllText(contractPath)) } : [];
        return [.. Directory.GetFiles(Folder(id), "*.rule.json").Order(StringComparer.Ordinal).Select(path => BrainRuleLoader.Load(File.ReadAllText(path), contracts).Rule!)];
    }

    /// <summary>The document text after the patch; with <paramref name="withoutTests"/> the embedded scenarios are dropped.</summary>
    internal static string Changed(string id, string? patch = null, bool withoutTests = false)
    {
        var document = JsonNode.Parse(Text(id))!;
        if (patch is not null)
        {
            document = JsonPatch.Apply(document, (JsonArray)JsonNode.Parse(patch)!);
        }

        if (withoutTests)
        {
            document["body"]!.AsObject().Remove("scenarios");
        }

        return AuthoringJson.Format(document);
    }

    internal static BrainProcessLoadResult TryLoad(string id, string text) => BrainProcessLoader.Load(text, new ProcessLoadContext(Rules(id), Ids));

    internal static BrainProcess Load(string id, string? patch = null, bool withoutTests = false)
    {
        var loaded = TryLoad(id, Changed(id, patch, withoutTests));
        return loaded.Process ?? throw new InvalidOperationException(id + " does not load: " + string.Join("; ", loaded.Errors.Select(error => error.Code + " at " + error.Path)));
    }
}

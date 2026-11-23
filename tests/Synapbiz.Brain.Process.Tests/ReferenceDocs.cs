using System.Text.Json.Nodes;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Finds and loads the reference documents of <c>samples/process-reference</c>, by path from the repository root.</summary>
internal static class ReferenceDocs
{
    internal const string Root = "samples/process-reference/";

    /// <summary>The reference processes, in the order of the lifecycle; every one has a folder of the same name.</summary>
    internal static readonly string[] Ids =
    [
        "previsit.outreach",
        "visit.note_wait",
        "postvisit.coding_review",
        "collection.followup",
        "case.lifecycle",
        "practice.daily_refresh",
    ];

    internal static string Folder(string id) => Path.GetDirectoryName(TestFiles.Locate(Root + id + "/process.json"))!;

    internal static string ProcessText(string id) => TestFiles.Read(Root + id + "/process.json");

    internal static JsonNode ProcessJson(string id) => JsonNode.Parse(ProcessText(id))!;

    /// <summary>The contract of the folder, or null when the process has none (it uses no rule).</summary>
    internal static BrainContract? Contract(string id)
    {
        var path = Path.Combine(Folder(id), "contract.json");
        return File.Exists(path) ? BrainContract.Parse(File.ReadAllText(path)) : null;
    }

    /// <summary>The names of the rule files of the folder, in ordinal order.</summary>
    internal static IReadOnlyList<string> RuleFiles(string id) =>
        [.. Directory.GetFiles(Folder(id), "*.rule.json").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    /// <summary>The loaded result of every rule file of the folder, with its contract.</summary>
    internal static IReadOnlyList<(string File, BrainRuleLoadResult Result)> RuleResults(string id)
    {
        var contract = Contract(id);
        return [.. RuleFiles(id).Select(file => (file, BrainRuleLoader.Load(File.ReadAllText(Path.Combine(Folder(id), file)), contract is null ? [] : [contract])))];
    }

    /// <summary>The rules of the folder; the test fails with the diagnostics when one does not load.</summary>
    internal static IReadOnlyList<BrainRule> Rules(string id) =>
        [.. RuleResults(id).Select(item => item.Result.Rule
            ?? throw new InvalidOperationException(item.File + " does not load: " + string.Join("; ", item.Result.Errors.Select(error => error.Code + "@" + error.Path))))];

    /// <summary>Loads a process the way a consumer would: its own rules, and every reference process as a known child.</summary>
    internal static BrainProcessLoadResult Load(string id) =>
        BrainProcessLoader.Load(ProcessText(id), new ProcessLoadContext(Rules(id), Ids));

    internal static BrainProcess Process(string id)
    {
        var loaded = Load(id);
        return loaded.Process ?? throw new InvalidOperationException(id + " does not load: " + string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path)));
    }
}

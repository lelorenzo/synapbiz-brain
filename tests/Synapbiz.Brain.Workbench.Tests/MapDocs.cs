using System.Text;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The synthetic processes of <c>tests/fixtures/workbench/map/</c>, which exercise what the six reference documents do not, and the text of a map for golden files.</summary>
internal static class MapDocs
{
    internal const string Folder = "tests/fixtures/workbench/map/";

    private static readonly string[] RuleFiles = ["conformance.flag.rule.json", "conformance.default.rule.json"];

    private static readonly Lazy<IReadOnlyList<BrainProcess>> Documents = new(() => Load(null));

    /// <summary>Every document of the map fixtures, in the order of their file names.</summary>
    internal static IReadOnlyList<BrainProcess> All => Documents.Value;

    internal static BrainProcess Process(string id) => All.Single(process => process.Id == id);

    /// <summary>The fixtures with one document changed through an edit of its JSON.</summary>
    internal static IReadOnlyList<BrainProcess> With(string id, Action<JsonObject> edit) => Load((id, edit));

    /// <summary>The host's labels the golden of the fixtures uses: one connector and one rule have a host label, the other connector has none.</summary>
    internal static IReadOnlyDictionary<string, string> HostLabels(WorkbenchLocale locale) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["connector:map.records"] = locale == WorkbenchLocale.Es ? "Sistema de registros" : "Records system",
        ["rule:conf.default_rule"] = locale == WorkbenchLocale.Es ? "Nivel por conteo" : "Level by count",
    };

    private static List<BrainProcess> Load((string Id, Action<JsonObject> Edit)? change)
    {
        var contract = BrainContract.Parse(TestFiles.Read(FlowDocs.ProcessFixtures + "conformance.contract.json"));
        var rules = RuleFiles
            .Select(file => BrainRuleLoader.Load(TestFiles.Read(FlowDocs.ProcessFixtures + file), [contract]).Rule!).ToList();
        var folder = Path.GetDirectoryName(TestFiles.Locate(Folder + "map.front.process.json"))!;
        var files = Directory.GetFiles(folder, "*.process.json").Order(StringComparer.Ordinal).ToList();
        var ids = files.Select(file => Path.GetFileName(file)[..^".process.json".Length]).ToList();
        return [.. files.Select(file =>
        {
            var text = File.ReadAllText(file);
            if (change is { } edited && Path.GetFileName(file) == edited.Id + ".process.json")
            {
                var document = JsonNode.Parse(text)!.AsObject();
                edited.Edit(document);
                text = document.ToJsonString();
            }

            return FlowDocs.Loaded(text, new ProcessLoadContext(rules, ids));
        })];
    }

    internal static string Golden(string fileName, string actual)
    {
        var path = Path.Combine(Path.GetDirectoryName(TestFiles.Locate(Folder + "map.front.process.json"))!, fileName);
        if (Environment.GetEnvironmentVariable("BRAIN_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual);
        }

        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>The kind of every document of a set (each described alone, then together), one line each.</summary>
    internal static string RenderKinds(IEnumerable<(string Name, BrainProcess Process)> documents, WorkbenchLocale locale)
    {
        var text = new StringBuilder();
        foreach (var (name, process) in documents)
        {
            var entry = FlowLibrary.From([process], locale).Single();
            text.Append(name).Append(": ").Append(entry.Kind);
            text.Append(entry.NoStartDefined ? " (no start defined)" : string.Empty).Append('\n');
        }

        return text.ToString();
    }

    private static string Piece(MapUse use) => (use.LabelIsName ? "`" + use.Label + "`" : use.Label) + " [" + use.Kind + ":" + use.Name + "]";

    private static void Process(StringBuilder text, string indent, MapProcess process)
    {
        text.Append(indent).Append(process.Kind).Append(' ').Append(process.Id).Append(" v").Append(process.Version).Append(": ").Append(process.Title).Append('\n');
        text.Append(indent).Append("  counts: ").Append(process.Counts.Steps).Append(" steps, ").Append(process.Counts.Scenarios).Append(" scenarios, ")
            .Append(process.Counts.ScenariosPass ? "pass" : "fail").Append(", ").Append(process.Counts.RoutesNotTried).Append(" routes not tried\n");
        if (process.NoStartDefined)
        {
            text.Append(indent).Append("  no start defined\n");
        }

        foreach (var trigger in process.Triggers)
        {
            text.Append(indent).Append("  trigger: ").Append(trigger).Append('\n');
        }

        foreach (var use in process.Uses)
        {
            text.Append(indent).Append("  uses: ").Append(Piece(use)).Append('\n');
        }
    }

    /// <summary>A map as text, one line per fact, so that a golden file shows every sentence and every grouping a screen would draw.</summary>
    internal static string Render(ProcessMap map)
    {
        var text = new StringBuilder();
        text.Append("processes: ").Append(map.Mains.Count).Append(" main, ").Append(map.SubprocessCount).Append(" sub, ").Append(map.Scheduled.Count).Append(" scheduled; rules: ").Append(map.RuleCount).Append('\n');
        foreach (var main in map.Mains)
        {
            text.Append("== main\n");
            Process(text, string.Empty, main.Process);
            foreach (var start in main.Starts)
            {
                text.Append("  starts: ").Append(start).Append('\n');
            }

            foreach (var phase in main.Phases)
            {
                text.Append("  phase ").Append(phase.IsLoose ? "(no phase)" : phase.StageId + " = " + phase.Label).Append('\n');
                text.Append("    ends when: ").Append(phase.EndsWhen ?? "(none)").Append('\n');
                foreach (var child in phase.Subprocesses)
                {
                    Process(text, "    > ", child.Process);
                    text.Append("      missing: ").Append(child.IsMissing).Append("; also started by: ").Append(string.Join(" | ", child.AlsoStartedBy)).Append('\n');
                }
            }

            text.Append("  ends: ").Append(string.Join(" | ", main.Ends)).Append('\n');
        }

        foreach (var task in map.Scheduled)
        {
            text.Append("== scheduled\n");
            Process(text, string.Empty, task.Process);
            foreach (var schedule in task.Schedules)
            {
                text.Append("  schedule: ").Append(schedule).Append('\n');
            }

            foreach (var child in task.Process.Starts)
            {
                text.Append("  starts process: ").Append(child.ProcessId).Append(" = ").Append(child.Title).Append(child.IsMissing ? " (missing)" : string.Empty).Append('\n');
            }
        }

        foreach (var shared in map.Shared)
        {
            text.Append("== shared: ").Append(Piece(shared.Use)).Append(" x").Append(shared.Count).Append(" by ").Append(string.Join(", ", shared.UsedBy)).Append('\n');
        }

        foreach (var cycle in map.Cycles)
        {
            text.Append("== cycle: ").Append(string.Join(" <-> ", cycle)).Append('\n');
        }

        foreach (var process in map.Detached)
        {
            text.Append("== detached: ").Append(process.Id).Append(" = ").Append(process.Title).Append('\n');
        }

        return text.ToString();
    }
}

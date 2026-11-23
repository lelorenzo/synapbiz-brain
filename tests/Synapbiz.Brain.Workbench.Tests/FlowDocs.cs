using System.Text;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The process documents the flow tests run on: the six reference documents, the three process fixtures and the fixtures of the flow workbench.</summary>
internal static class FlowDocs
{
    internal const string Root = "samples/process-reference/";
    internal const string ProcessFixtures = "tests/fixtures/brain-process/";
    internal const string Fixtures = "tests/fixtures/workbench/flows/";

    internal static readonly string[] ReferenceIds =
    [
        "previsit.outreach",
        "visit.note_wait",
        "postvisit.coding_review",
        "collection.followup",
        "case.lifecycle",
        "practice.daily_refresh",
    ];

    private static readonly Lazy<IReadOnlyList<(string Name, BrainProcess Process)>> Documents = new(LoadAll);

    /// <summary>Every document: reference ones by id, process fixtures as <c>fixture.&lt;name&gt;</c>, flow fixtures as <c>flow.&lt;name&gt;</c>.</summary>
    internal static IReadOnlyList<(string Name, BrainProcess Process)> All => Documents.Value;

    /// <summary>The documents the golden views pin: the six reference documents.</summary>
    internal static IReadOnlyList<(string Name, BrainProcess Process)> Reference => [.. All.Where(item => ReferenceIds.Contains(item.Name))];

    internal static BrainProcess Process(string name) => All.Single(item => item.Name == name).Process;

    internal static string ReferenceText(string id) => TestFiles.Read(Root + id + "/process.json");

    internal static string FixtureText(string name) => TestFiles.Read(ProcessFixtures + name + ".process.json");

    /// <summary>Loads the text of a reference document with its own rules and every reference process as a known child.</summary>
    internal static BrainProcess LoadReference(string id, string text)
    {
        var folder = Path.GetDirectoryName(TestFiles.Locate(Root + id + "/process.json"))!;
        var contractPath = Path.Combine(folder, "contract.json");
        var contract = File.Exists(contractPath) ? BrainContract.Parse(File.ReadAllText(contractPath)) : null;
        var rules = Directory.GetFiles(folder, "*.rule.json").Order(StringComparer.Ordinal)
            .Select(file => BrainRuleLoader.Load(File.ReadAllText(file), contract is null ? [] : [contract]).Rule!).ToList();
        return Loaded(text, new ProcessLoadContext(rules, ReferenceIds));
    }

    internal static BrainProcess Loaded(string text, ProcessLoadContext context)
    {
        var loaded = BrainProcessLoader.Load(text, context);
        return loaded.Process ?? throw new InvalidOperationException("The document does not load: " + string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path)));
    }

    /// <summary>The same document with one more scenario in <c>body.scenarios</c>, loaded with the same context as the original.</summary>
    internal static BrainProcess WithScenario(string name, string scenarioJson)
    {
        var (text, context) = SourceOf(name);
        var document = JsonNode.Parse(text)!;
        document["body"]!["scenarios"]!.AsArray().Add(JsonNode.Parse(scenarioJson));
        return Loaded(document.ToJsonString(), context);
    }

    internal static (string Text, ProcessLoadContext Context) SourceOf(string name)
    {
        if (ReferenceIds.Contains(name))
        {
            var folder = Path.GetDirectoryName(TestFiles.Locate(Root + name + "/process.json"))!;
            var contractPath = Path.Combine(folder, "contract.json");
            var contract = File.Exists(contractPath) ? BrainContract.Parse(File.ReadAllText(contractPath)) : null;
            var rules = Directory.GetFiles(folder, "*.rule.json").Order(StringComparer.Ordinal)
                .Select(file => BrainRuleLoader.Load(File.ReadAllText(file), contract is null ? [] : [contract]).Rule!).ToList();
            return (ReferenceText(name), new ProcessLoadContext(rules, ReferenceIds));
        }

        if (name.StartsWith("flow.", StringComparison.Ordinal))
        {
            return (TestFiles.Read(Fixtures + name["flow.".Length..] + ".process.json"), FlowContext());
        }

        var fixture = name["fixture.".Length..];
        return (FixtureText(fixture), fixture switch
        {
            "outreach" => new ProcessLoadContext([Rule("outreach")]),
            "followup" => new ProcessLoadContext([Rule("followup")]),
            _ => new ProcessLoadContext([]),
        });
    }

    private static BrainRule Rule(string name)
    {
        var contract = BrainContract.Parse(TestFiles.Read(ProcessFixtures + name + ".contract.json"));
        return BrainRuleLoader.Load(TestFiles.Read(ProcessFixtures + name + ".rule.json"), [contract]).Rule!;
    }

    // The flow fixtures use the rules of the conformance contract of the process tests.
    private static ProcessLoadContext FlowContext()
    {
        var contract = BrainContract.Parse(TestFiles.Read(ProcessFixtures + "conformance.contract.json"));
        var rules = FlowRuleFiles
            .Select(file => BrainRuleLoader.Load(TestFiles.Read(ProcessFixtures + file), [contract]).Rule!).ToList();
        return new ProcessLoadContext(rules, [.. ReferenceIds, "flow.child"]);
    }

    private static readonly string[] FlowRuleFiles = ["conformance.flag.rule.json", "conformance.default.rule.json", "conformance.list.rule.json"];

    private static List<(string Name, BrainProcess Process)> LoadAll()
    {
        var all = new List<(string Name, BrainProcess Process)>();
        foreach (var id in ReferenceIds)
        {
            var (text, context) = SourceOf(id);
            all.Add((id, Loaded(text, context)));
        }

        foreach (var fixture in new[] { "outreach", "followup", "reminder" })
        {
            var (text, context) = SourceOf("fixture." + fixture);
            all.Add(("fixture." + fixture, Loaded(text, context)));
        }

        var folder = Path.GetDirectoryName(TestFiles.Locate(Fixtures + "README.md"))!;
        foreach (var file in Directory.GetFiles(folder, "*.process.json").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file)[..^".process.json".Length];
            var (text, context) = SourceOf("flow." + name);
            all.Add(("flow." + name, Loaded(text, context)));
        }

        return all;
    }

    /// <summary>The golden file of a rendered text; written first when the variable <c>BRAIN_UPDATE_GOLDEN</c> is <c>1</c>.</summary>
    internal static string Golden(string fileName, string actual)
    {
        var path = Path.Combine(Path.GetDirectoryName(TestFiles.Locate(Fixtures + "README.md"))!, fileName);
        if (Environment.GetEnvironmentVariable("BRAIN_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual);
        }

        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>A view as text, one line per fact, so that a golden file shows every sentence a reader would see.</summary>
    internal static string Render(FlowView view)
    {
        var text = new StringBuilder();
        text.Append("flow ").Append(view.Id).Append(" v").Append(view.Version).Append('\n');
        text.Append("title: ").Append(view.Title).Append('\n');
        text.Append("start: ").Append(view.StartStep).Append('\n');
        foreach (var stage in view.Stages)
        {
            text.Append("stage ").Append(stage.Id).Append(": ").Append(stage.Label).Append('\n');
        }

        foreach (var step in view.Steps)
        {
            text.Append("step ").Append(step.Id).Append(" [").Append(step.KindName).Append("] ").Append(step.Label).Append('\n');
            text.Append("  stage: ").Append(step.StageLabel ?? "-").Append('\n');
            text.Append("  does: ").Append(step.Sentence).Append('\n');
            foreach (var route in step.Routes)
            {
                text.Append("  ").Append(route.Label).Append(": ").Append(route.Phrase).Append('\n');
            }

            if (step.Ai is { } ai)
            {
                text.Append("  ai input: ").Append(string.Join(", ", ai.InputNames)).Append('\n');
                text.Append("  ai input schema: ").Append(ai.InputSchemaJson).Append('\n');
                text.Append("  ai output schema: ").Append(ai.OutputSchemaJson).Append('\n');
                text.Append("  ai fills: ").Append(string.Join(", ", ai.OutputNames)).Append('\n');
                text.Append("  ai judged by: ").Append(ai.JudgingRule ?? "-").Append('\n');
                foreach (var note in ai.Notes)
                {
                    text.Append("  ai note: ").Append(note).Append('\n');
                }
            }
        }

        return text.ToString();
    }
}

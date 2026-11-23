using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Schema violations are reported only at the offending value (section 8, acceptance).</summary>
public sealed partial class SchemaLocationTests
{
    private const string File = TestFiles.Fixtures + "conformance.json";

    [Fact]
    public void AnUnknownMemberInAnyClosedObjectIsTheOnlyErrorOfTheDocument()
    {
        var documents = AllCases().Where(item => item["expect"] is JsonValue)
            .Select(item => (Name: item["name"]!.GetValue<string>(), Document: item, Rules: item["context"]!["rules"]!.AsArray().Select(rule => rule!.GetValue<string>()).ToList()))
            .ToList();
        var tested = 0;
        var failures = new List<string>();

        foreach (var (name, fixture, rules) in documents)
        {
            foreach (var path in ObjectPaths(fixture["document"]!, "").Where(path => IsClosed(path)))
            {
                var copy = JsonNode.Parse(fixture["document"]!.ToJsonString())!;
                Navigate(copy, path).AsObject()["zzz"] = 1;

                var result = BrainProcessLoader.Load(copy.ToJsonString(), new ProcessLoadContext([.. rules.Select(Rule)], KnownIds(fixture)));

                Check(failures, $"{name}: '{path}'", result, path);
                tested++;
            }
        }

        failures.Should().BeEmpty();
        tested.Should().BeGreaterThan(150);
    }

    [Fact]
    public void TheReferenceProcessHasOnlyTheOneErrorToo()
    {
        var text = TestFiles.Read(TestFiles.Fixtures + "outreach.process.json");
        var document = JsonNode.Parse(text)!;
        var tested = 0;
        var failures = new List<string>();

        foreach (var path in ObjectPaths(document, "").Where(path => IsClosed(path)))
        {
            var copy = JsonNode.Parse(text)!;
            Navigate(copy, path).AsObject()["zzz"] = 1;

            var result = BrainProcessLoader.Load(copy.ToJsonString(), new ProcessLoadContext([TestFiles.OutreachRule()]));

            Check(failures, $"outreach: '{path}'", result, path);
            tested++;
        }

        failures.Should().BeEmpty();
        tested.Should().BeGreaterThan(25);
    }

    [Fact]
    public void TheFollowupProcessHasOnlyTheOneErrorToo()
    {
        var text = TestFiles.Read(TestFiles.Fixtures + "followup.process.json");
        var document = JsonNode.Parse(text)!;
        var tested = 0;
        var failures = new List<string>();
        var paths = ObjectPaths(document, "").Where(path => IsClosed(path)).ToList();

        foreach (var path in paths)
        {
            var copy = JsonNode.Parse(text)!;
            Navigate(copy, path).AsObject()["zzz"] = 1;

            var result = BrainProcessLoader.Load(copy.ToJsonString(), new ProcessLoadContext([TestFiles.FollowupRule()]));

            Check(failures, $"followup: '{path}'", result, path);
            tested++;
        }

        failures.Should().BeEmpty();
        paths.Should().Contain(["/body/triggers/0", "/body/triggers/1", "/body/windows", "/body/windows/contact_hours", "/body/steps/send", "/body/steps/await_reply", "/body/scenarios/0/zones"]);
        tested.Should().BeGreaterThan(30);
    }

    private static void Check(List<string> failures, string where, BrainProcessLoadResult result, string path)
    {
        var actual = result.Errors.Select(error => error.Code + "@" + error.Path).ToList();
        if (!actual.SequenceEqual(["spec.structure.schema@" + path + "/zzz"]))
        {
            failures.Add(where + " => " + string.Join(" | ", actual));
        }
    }

    private static Synapbiz.Brain.Spec.BrainRule Rule(string name) => name switch
    {
        "flag" => Docs.FlagRule,
        "list" => Docs.ListRule,
        "default" => Docs.DefaultRule,
        _ => TestFiles.OutreachRule(),
    };

    private static IReadOnlyCollection<string>? KnownIds(JsonObject fixture) =>
        fixture["context"]!["knownProcessIds"] is JsonArray ids ? [.. ids.Select(id => id!.GetValue<string>())] : null;

    private static string Pointer(string path, string key) =>
        path + "/" + key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static List<JsonObject> AllCases() => [.. TestFiles.ReadJson(File).AsArray().OfType<JsonObject>()];

    private static JsonNode Navigate(JsonNode root, string pointer)
    {
        var node = root;
        foreach (var part in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)))
        {
            node = node is JsonArray array ? array[int.Parse(part, System.Globalization.CultureInfo.InvariantCulture)]! : node[part]!;
        }

        return node;
    }

    private static IEnumerable<string> ObjectPaths(JsonNode node, string path)
    {
        if (node is JsonObject obj)
        {
            yield return path;
            foreach (var (key, value) in obj.Where(member => member.Value is not null))
            {
                foreach (var inner in ObjectPaths(value!, Pointer(path, key)))
                {
                    yield return inner;
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                foreach (var inner in ObjectPaths(array[index]!, path + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    yield return inner;
                }
            }
        }
    }

    // The objects that the schema leaves open: meta, the JSON Schemas of ai steps, initial values (Json) and the payload objects of scenarios.
    private static bool IsClosed(string path) => !Open().IsMatch(path);

    [GeneratedRegex(@"^(/meta($|/)|.*/(inputSchema|outputSchema)($|/)|/body/variables/[^/]+/initial($|/)|/body/scenarios/\d+/start/input($|/)|/body/scenarios/\d+/events/\d+/(output|payload|data)($|/)|/body/scenarios/\d+/expect/variables($|/)|.*/\$defs($|/))", RegexOptions.CultureInvariant)]
    private static partial Regex Open();
}

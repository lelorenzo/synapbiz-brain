using System.Text.Json.Nodes;
using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Section 10.4 of the W1-C specification: no input text makes <see cref="BrainProcessLoader.Load"/> throw.</summary>
public sealed class LoaderTotalityTests
{
    private static readonly ProcessLoadContext Empty = new([]);

    // Numbers a schema evaluation that compares array items by value cannot represent, next to ones it can.
    public static TheoryData<string> Numbers => new()
    {
        "1e308",
        "1e400",
        "-1e400",
        "123456789012345678901234567890",
        "1.5e999999999",
        "0.0000000000000000000000000000000001",
    };

    private static string Document(string uses = "[]", string requires = "[]", string stages = "[]", string until = "[\"yes\"]") =>
        $$"""
        {
          "$spec": "brain.process@1", "id": "t.proc", "version": 1, "uses": {{uses}}, "requires": {{requires}}, "meta": {},
          "body": {
            "subject": "case", "stages": {{stages}}, "variables": {}, "start": "s",
            "steps": {
              "s": { "kind": "poll", "port": "p", "rule": "r.x", "facts": {}, "field": "f", "until": {{until}}, "every": "PT15M", "maxDuration": "PT1H", "next": "e", "onExpired": "e" },
              "e": { "kind": "end", "result": "ok" }
            }
          }
        }
        """;

    private static BrainProcessLoadResult Load(string text)
    {
        BrainProcessLoadResult? result = null;
        var load = () => result = BrainProcessLoader.Load(text, Empty);
        load.Should().NotThrow();
        return result!;
    }

    [Theory]
    [MemberData(nameof(Numbers))]
    public void ANumberInTheUntilListIsASchemaViolationAtItsPath(string number)
    {
        var loaded = Load(Document(until: $"[{number}]"));

        loaded.Process.Should().BeNull();
        loaded.Errors.Should().Contain(error => error.Code == "spec.structure.schema" && error.Path == "/body/steps/s/until/0");
    }

    [Theory]
    [MemberData(nameof(Numbers))]
    public void ANumberInTheStagesListIsASchemaViolationAtItsPath(string number)
    {
        var loaded = Load(Document(stages: $"[\"one\", {number}]"));

        loaded.Process.Should().BeNull();
        loaded.Errors.Should().Contain(error => error.Code == "spec.structure.schema" && error.Path == "/body/stages/1");
    }

    [Theory]
    [MemberData(nameof(Numbers))]
    public void ANumberInTheRequiresListIsASchemaViolationAtItsPath(string number)
    {
        var loaded = Load(Document(requires: $"[{number}]"));

        loaded.Process.Should().BeNull();
        loaded.Errors.Should().Contain(error => error.Code == "spec.structure.schema" && error.Path == "/requires/0");
    }

    [Theory]
    [MemberData(nameof(Numbers))]
    public void ANumberInTheUsesListIsASchemaViolationAtItsPath(string number)
    {
        var loaded = Load(Document(uses: $"[{number}]"));

        loaded.Process.Should().BeNull();
        loaded.Errors.Should().Contain(error => error.Code == "spec.structure.schema" && error.Path == "/uses/0");
    }

    [Theory]
    [MemberData(nameof(Numbers))]
    public void TwoEqualHugeNumbersInAListAreReportedAndNeverThrow(string number)
    {
        var loaded = Load(Document(until: $"[{number}, {number}]"));

        loaded.Process.Should().BeNull();
        loaded.Errors.Should().Contain(error => error.Code == "spec.structure.schema" && error.Path == "/body/steps/s/until/0");
        loaded.Errors.Should().Contain(error => error.Code == "spec.structure.schema" && error.Path == "/body/steps/s/until/1");
    }

    [Fact]
    public void AHugeNumberAmongStringsOnlyFlagsTheNumber()
    {
        var loaded = Load(Document(until: "[\"yes\", \"no\", 1e400, \"yes\"]"));

        loaded.Errors.Should().Contain(error => error.Path == "/body/steps/s/until/2");
        loaded.Errors.Should().NotContain(error => error.Path == "/body/steps/s/until/0" || error.Path == "/body/steps/s/until/1");
    }

    [Fact]
    public void AHugeNumberWhereAnIntegerIsExpectedIsAViolationAtItsPath()
    {
        var text = Document().Replace("\"version\": 1", "\"version\": 1e400", StringComparison.Ordinal);

        var loaded = Load(text);

        loaded.Process.Should().BeNull();
    }

    // The leaf values a hostile document puts where the format expects something else, written as raw JSON text.
    private static readonly string[] Hostile =
    [
        "1e400",
        "-1e400",
        "1e308",
        "123456789012345678901234567890",
        "-0.00000000000000000000000000000000001",
        "\"\\ud800\"",
        "\"a\\u0000b\"",
        "null",
        "[]",
        "{}",
    ];

    [Theory]
    [InlineData("outreach.process.json")]
    [InlineData("followup.process.json")]
    public void NoLeafOfAReferenceDocumentMakesTheLoaderThrowWhateverItIsReplacedBy(string file)
    {
        var text = TestFiles.Read(TestFiles.Fixtures + file);
        var rules = file.StartsWith("outreach", StringComparison.Ordinal) ? TestFiles.OutreachRule() : TestFiles.FollowupRule();
        var context = new ProcessLoadContext([rules]);
        var leaves = Leaves(JsonNode.Parse(text)!, []).ToList();
        leaves.Should().HaveCountGreaterThan(50);

        foreach (var path in leaves)
        {
            foreach (var replacement in Hostile)
            {
                var changed = Replace(text, path, replacement);
                var load = () => BrainProcessLoader.Load(changed, context);
                load.Should().NotThrow("/" + string.Join("/", path) + " replaced by " + replacement);
            }
        }
    }

    [Fact]
    public void AnUnreadableStringAnywhereInTheDocumentIsNotValidJsonAndNeverThrows()
    {
        foreach (var text in new[] { Document(until: "[\"\\ud800\"]"), Document(until: "[\"\\udc00x\"]"), Document().Replace("\"s\": {", "\"\\ud800\": {", StringComparison.Ordinal) })
        {
            var loaded = Load(text);

            loaded.Process.Should().BeNull();
            loaded.Errors.Should().ContainSingle().Which.Code.Should().Be("spec.json.invalid");
        }
    }

    private static IEnumerable<string[]> Leaves(JsonNode node, string[] path)
    {
        switch (node)
        {
            case JsonObject members:
                foreach (var (name, value) in members)
                {
                    foreach (var leaf in Leaves(value!, [.. path, name]))
                    {
                        yield return leaf;
                    }
                }

                break;
            case JsonArray items:
                for (var index = 0; index < items.Count; index++)
                {
                    foreach (var leaf in Leaves(items[index]!, [.. path, index.ToString(System.Globalization.CultureInfo.InvariantCulture)]))
                    {
                        yield return leaf;
                    }
                }

                break;
            default:
                yield return path;
                break;
        }
    }

    // The document with the leaf at the pointer replaced by the raw JSON text of the replacement; the text is built by substitution
    // because a replacement that holds an unpaired surrogate cannot be written by a JSON writer.
    private static string Replace(string text, string[] parts, string replacement)
    {
        const string Marker = "__replaced__";
        var root = JsonNode.Parse(text)!;
        var holder = root;
        foreach (var part in parts[..^1])
        {
            holder = holder is JsonArray array ? array[int.Parse(part, System.Globalization.CultureInfo.InvariantCulture)] : holder![part];
        }

        if (holder is JsonArray list)
        {
            list[int.Parse(parts[^1], System.Globalization.CultureInfo.InvariantCulture)] = Marker;
        }
        else
        {
            holder![parts[^1]] = Marker;
        }

        return root.ToJsonString().Replace("\"" + Marker + "\"", replacement, StringComparison.Ordinal);
    }
}

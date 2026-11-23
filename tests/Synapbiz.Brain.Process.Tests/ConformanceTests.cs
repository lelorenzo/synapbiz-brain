using System.Text.Json.Nodes;
using FluentAssertions;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>The conformance file: valid documents, one per step kind and more, and invalid documents, one per diagnostic code and more.</summary>
public sealed class ConformanceTests
{
    private const string File = TestFiles.Fixtures + "conformance.json";

    /// <summary>Every code of section 2.4 of the specification and the structure codes.</summary>
    private static readonly string[] Codes =
    [
        "spec.json.invalid", "spec.structure.envelope", "spec.structure.unknown_format", "spec.structure.schema",
        "process.step.unknown", "process.step.unreachable", "process.stage.unknown", "process.variable.unknown",
        "process.variable.type", "process.variable.initial", "process.rule.unavailable", "process.rule.fact_unknown",
        "process.rule.outcome_unknown", "process.rule.hit_policy", "process.route.incomplete", "process.route.unknown_value",
        "process.uses.missing", "process.uses.unused", "process.subprocess.unavailable", "process.ai.next_not_decide",
        "process.ai.schema_invalid", "process.cycle.unbounded", "process.duration.invalid", "process.scenario.invalid",
    ];

    private static readonly string[] StepKinds = ["read", "action", "ai", "decide", "humanTask", "wait", "timer", "loop", "subprocess", "end"];

    public static IEnumerable<object[]> CaseIndexes() => AllCases().Select((_, index) => new object[] { index });

    [Fact]
    public void TheFileHoldsAValidDocumentForEveryStepKind()
    {
        var valid = AllCases().Where(item => item["expect"] is JsonValue).Select(item => item["document"]!).ToList();
        var kinds = valid.SelectMany(document => document["body"]!["steps"]!.AsObject().Select(step => (string?)step.Value!["kind"])).ToHashSet();

        kinds.Should().Contain(StepKinds);
    }

    [Fact]
    public void TheFileHoldsAnInvalidDocumentForEveryCode()
    {
        var covered = AllCases().Where(item => item["expect"] is JsonObject)
            .SelectMany(item => item["expect"]!["errors"]!.AsArray().Select(error => (string?)error!["code"]))
            .ToHashSet();

        covered.Should().Contain(Codes);
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void EveryCaseLoadsAsTheFileExpects(int index)
    {
        var fixture = AllCases()[index];
        var result = Load(fixture);

        if (fixture["expect"] is JsonValue)
        {
            result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors.Select(error => $"{error.Code}@{error.Path}")));
            result.Process.Should().NotBeNull();
            result.Process!.RunScenarios().IsOk.Should().BeTrue();
            return;
        }

        var expected = fixture["expect"]!["errors"]!.AsArray()
            .Select(error => (Code: error!["code"]!.GetValue<string>(), Path: error["path"]!.GetValue<string>()))
            .ToList();
        result.IsValid.Should().BeFalse();
        result.Process.Should().BeNull();
        result.Errors.Select(error => (error.Code, error.Path)).Should().Equal(expected);
        result.Errors.Should().OnlyContain(error =>
            !string.IsNullOrWhiteSpace(error.MessageEn) && !string.IsNullOrWhiteSpace(error.MessageEs) && !string.IsNullOrWhiteSpace(error.FixHint));
        result.Errors.Should().BeInAscendingOrder(error => error.Path, StringComparer.Ordinal);
        result.Errors.Distinct().Should().HaveCount(result.Errors.Count);
    }

    private static BrainProcessLoadResult Load(JsonObject fixture)
    {
        var context = fixture["context"]!;
        var rules = context["rules"]!.AsArray().Select(name => RuleOf(name!.GetValue<string>())).ToList();
        IReadOnlyCollection<string>? known = context["knownProcessIds"] is JsonArray ids ? [.. ids.Select(id => id!.GetValue<string>())] : null;
        var document = fixture["document"]!;
        var text = document is JsonValue value && value.TryGetValue<string>(out var raw) ? raw : document.ToJsonString();
        return BrainProcessLoader.Load(text, new ProcessLoadContext(rules, known));
    }

    private static BrainRule RuleOf(string name) => name switch
    {
        "flag" => Docs.FlagRule,
        "list" => Docs.ListRule,
        "outreach" => TestFiles.OutreachRule(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown rule name in the conformance file."),
    };

    private static List<JsonObject> AllCases() => [.. TestFiles.ReadJson(File).AsArray().OfType<JsonObject>()];
}

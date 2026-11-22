using System.Text.Json.Nodes;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;

namespace Synapbiz.Brain.Spec.Tests;

/// <summary>The rule cases of the shared conformance suite: 77 documents, 60 valid and 17 with expected errors.</summary>
public sealed class ConformanceTests
{
    private const string RuleFormat = "brain.rule@1";
    private const string FlowFormat = "brain.flow@1";
    private const int RuleCases = 77;
    private const int FlowCases = 3;
    private const int ValidRuleCases = 60;
    private const int InvalidRuleCases = 17;

    /// <summary>Valid rules whose table passes the engine's static validation, so their goldens must run clean.</summary>
    private const int ValidRulesWithAValidTable = 51;

    public static IEnumerable<object[]> RuleCaseIndexes() =>
        AllCases().Select((item, index) => (item, index))
            .Where(entry => (string?)entry.item["document"]!["$spec"] == RuleFormat)
            .Select(entry => new object[] { entry.index });

    [Fact]
    public void TheSuiteHoldsSeventySevenRuleCasesAndThreeFlowCasesWhichAreFilteredBySpec()
    {
        var cases = AllCases();
        cases.Count(item => (string?)item["document"]!["$spec"] == FlowFormat).Should().Be(FlowCases);
        RuleCaseIndexes().Count().Should().Be(RuleCases);
        cases.Count.Should().Be(RuleCases + FlowCases);
        cases.Count(item => (string?)item["document"]!["$spec"] == RuleFormat && item["expect"] is JsonValue).Should().Be(ValidRuleCases);
        cases.Count(item => (string?)item["document"]!["$spec"] == RuleFormat && item["expect"] is JsonObject).Should().Be(InvalidRuleCases);
    }

    [Theory]
    [MemberData(nameof(RuleCaseIndexes))]
    public void EveryRuleCaseLoadsAsTheSuiteExpects(int index)
    {
        var fixture = AllCases()[index];
        var result = Load(fixture);

        if (fixture["expect"] is JsonValue)
        {
            result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors.Select(error => $"{error.Code}@{error.Path}")));
            result.Rule.Should().NotBeNull();
            result.DeferredToEngine.Should().Equal("overlap", "coverage", "goldens", "outcome-policy");
            return;
        }

        var expected = fixture["expect"]!["errors"]!.AsArray()
            .Select(error => (Code: error!["code"]!.GetValue<string>(), Path: error["path"]!.GetValue<string>()))
            .ToHashSet();
        result.IsValid.Should().BeFalse();
        result.Rule.Should().BeNull();
        result.DeferredToEngine.Should().BeEmpty();
        result.Errors.Select(error => (error.Code, error.Path)).ToHashSet().Should().BeEquivalentTo(expected);
        result.Errors.Should().OnlyContain(error =>
            !string.IsNullOrWhiteSpace(error.MessageEn) && !string.IsNullOrWhiteSpace(error.MessageEs) && !string.IsNullOrWhiteSpace(error.FixHint));
    }

    [Fact]
    public void EveryValidRuleValidatesItsTableWithoutThrowingAndRunsItsGoldensWhenTheTableIsValid()
    {
        var withValidTable = 0;
        var valid = 0;
        foreach (var fixture in AllCases().Where(item => (string?)item["document"]!["$spec"] == RuleFormat && item["expect"] is JsonValue))
        {
            var rule = Load(fixture).Rule!;
            valid++;
            var report = FluentActions.Invoking(rule.ValidateTable).Should().NotThrow().Subject;
            if (!report.IsValid)
            {
                continue;
            }

            withValidTable++;
            AssertGoldensMatchTheDocument(rule, fixture["document"]!["body"]!["goldens"]!.AsArray());
        }

        valid.Should().Be(ValidRuleCases);
        withValidTable.Should().Be(ValidRulesWithAValidTable);
    }

    internal static void AssertGoldensMatchTheDocument(BrainRule rule, JsonArray goldens)
    {
        var run = rule.RunGoldens();
        run.Results.Should().HaveCount(goldens.Count);
        for (var index = 0; index < goldens.Count; index++)
        {
            var golden = goldens[index]!;
            var actual = run.Results[index];
            actual.Name.Should().Be(golden["name"]!.GetValue<string>());
            actual.Passed.Should().BeTrue(because: $"golden '{actual.Name}' of rule '{rule.Id}'");
            var expect = golden["expect"]!;
            if ((bool?)expect["noDecision"] == true)
            {
                actual.Actual.Should().BeOfType<NoDecision>();
                continue;
            }

            var decided = actual.Actual.Should().BeOfType<Decided>().Subject;
            JsonNode.DeepEquals(JsonNode.Parse(decided.OutcomeJson), expect["outcome"]).Should().BeTrue();
            if (expect["row"] is { } rowId)
            {
                rule.RowIdsByKey[decided.RowKey].Should().Be(rowId.GetValue<string>());
            }
        }
    }

    private static BrainRuleLoadResult Load(JsonObject fixture)
    {
        var contracts = fixture["context"]!["contracts"]!.AsArray().Select(name => TestFiles.Contract(name!.GetValue<string>())).ToList();
        return BrainRuleLoader.Load(fixture["document"]!.ToJsonString(), contracts);
    }

    private static List<JsonObject> AllCases() =>
        [.. TestFiles.ReadJson("tests/fixtures/spec/brain/conformance.json").AsArray().OfType<JsonObject>()];
}

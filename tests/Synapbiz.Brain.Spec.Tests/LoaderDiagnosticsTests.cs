using System.Text.Json.Nodes;
using FluentAssertions;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Spec.Tests;

/// <summary>
/// The structure fixture past its first eight schema refusals: one document for each diagnostic the loader can report that
/// the shared conformance suite does not reach, and valid documents for the range, run and collect edges. The same
/// documents are the parity scenarios <c>spec/structure/NN</c>, so the source repository's validator must agree on each.
/// </summary>
public sealed class LoaderDiagnosticsTests
{
    private const string FixturePath = "tests/fixtures/brain-spec/structure-documents.json";
    private static readonly DateOnly AsOf = new(2026, 11, 18);

    /// <summary>
    /// Every code the loader can report for a rule that the source repository's validator can report too. Left out, and why:
    /// <c>spec.json.invalid</c> (the source validator takes an already parsed node, so only its canonical serialiser raises
    /// it) and the projection's own <c>decisions.definition.malformed</c> path (reached only by a document the earlier
    /// stages accept, which none can be).
    /// </summary>
    private static readonly string[] ComparableCodes =
    [
        "spec.structure.envelope", "spec.structure.unknown_format", "spec.structure.schema", "brain.rule.golden_outcome_missing",
        "spec.semantics.node", "spec.semantics.duplicate_id", "spec.semantics.reference", "spec.semantics.cycle", "spec.semantics.translation",
        "brain.contract.unavailable", "brain.contract.module_required", "decisions.kind.not_registered",
        "decisions.validation.duplicate_row_key", "decisions.validation.unknown_fact", "decisions.condition.operator_not_allowed",
        "decisions.validation.money_currency_mismatch", "decisions.condition.range_type_mismatch", "decisions.validation.cell_type_mismatch",
        "brain.fact.label_source_mismatch", "brain.fact.enum_not_allowed", "decisions.facts.missing", "decisions.facts.type_mismatch",
        "brain.golden.unknown_row", "decisions.definition.malformed", "brain.outcome.unknown_field", "brain.outcome.type_mismatch",
        "brain.outcome.required_field",
    ];

    public static IEnumerable<object[]> RefusedDocuments() => Select(item => item["expectedErrors"] is not null);

    public static IEnumerable<object[]> ValidDocuments() => Select(item => item["valid"] is not null);

    [Theory]
    [MemberData(nameof(RefusedDocuments))]
    public void ARefusedDocumentIsReportedWithExactlyTheExpectedCodesAndPaths(int index)
    {
        var fixture = Fixtures()[index]!;
        var result = BrainRuleLoader.Load(fixture["document"]!.ToJsonString(), [TestFiles.ContractOf(fixture)]);

        result.Rule.Should().BeNull();
        result.DeferredToEngine.Should().BeEmpty();
        result.Errors.Select(error => $"{error.Code}@{error.Path}").Should().Equal(
            fixture["expectedErrors"]!.AsArray().Select(error => error!.GetValue<string>()), because: fixture["name"]!.GetValue<string>());
        result.Errors.Should().OnlyContain(error => error.MessageEn.Length > 0 && error.MessageEs.Length > 0 && error.FixHint.Length > 0);
    }

    [Theory]
    [MemberData(nameof(ValidDocuments))]
    public void AValidDocumentLoadsAndItsTableCompiles(int index)
    {
        var rule = LoadValid(index);

        rule.Compile().Should().NotBeNull();
        rule.ValidateTable().Should().NotBeNull();
    }

    [Fact]
    public void EveryComparableCodeIsReportedByAtLeastOneConformanceCaseOrStructureDocument()
    {
        var reported = Fixtures().SelectMany(item => item!["expectedErrors"]?.AsArray().Select(error => error!.GetValue<string>()) ?? [])
            .Concat(Fixtures().Where(item => item!["errorAt"] is not null).Select(_ => "spec.structure.schema@"))
            .Concat(TestFiles.ReadJson("tests/fixtures/spec/brain/conformance.json").AsArray()
                .Where(item => item!["expect"] is JsonObject)
                .SelectMany(item => item!["expect"]!["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>() + "@")))
            .Select(entry => entry[..entry.IndexOf('@', StringComparison.Ordinal)])
            .ToHashSet();

        ComparableCodes.Where(code => !reported.Contains(code)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, false)]
    [InlineData(11, true)]
    [InlineData(19, true)]
    [InlineData(20, false)]
    [InlineData(21, false)]
    public void ARangeWithBothBoundsExclusiveExcludesTheBounds(int score, bool matches)
    {
        var rule = LoadNamed("range with both bounds exclusive");

        var range = rule.Table.Rows.Single().Conditions.Single();
        range.LowerInclusive.Should().BeFalse();
        range.UpperInclusive.Should().BeFalse();
        Decided(rule, score).Should().Be(matches);
        rule.RunGoldens().IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData(100, "A")]
    [InlineData(5000, "A")]
    [InlineData(99, null)]
    [InlineData(5, "B")]
    [InlineData(-3, "B")]
    [InlineData(6, null)]
    public void ARangeWithANullBoundIsOpenOnThatSide(int score, string? expectedKey)
    {
        var rule = LoadNamed("open-ended ranges");

        rule.Table.Rows[0].Conditions.Single().UpperValue.Should().BeNull();
        rule.Table.Rows[1].Conditions.Single().Value.Should().BeNull();
        var outcome = rule.Evaluate(new Dictionary<string, DecisionValue> { ["score"] = DecisionValue.Integer(score) }, AsOf).Outcome;
        (outcome as Decided)?.RowKey.Should().Be(expectedKey);
        (outcome is Decided).Should().Be(expectedKey is not null);
        rule.RunGoldens().IsOk.Should().BeTrue();
    }

    [Fact]
    public void ARangeConditionWithoutInclusivityFlagsIncludesBothBounds()
    {
        var rule = LoadNamed("open-ended ranges");

        rule.Table.Rows[0].Conditions.Single().LowerInclusive.Should().BeTrue();
        rule.Table.Rows[0].Conditions.Single().UpperInclusive.Should().BeTrue();
    }

    [Fact]
    public void AGoldenThatDoesNotMatchItsExpectationFailsWhenRun()
    {
        var run = LoadNamed("a golden that fails when run").RunGoldens();

        run.IsOk.Should().BeFalse();
        run.Failed.Select(result => result.Name).Should().Equal("expects the wrong outcome");
        run.Results.Single(result => result.Name == "passes").Passed.Should().BeTrue();
    }

    [Fact]
    public void ARowNoGoldenCoversIsReportedAsUncovered()
    {
        var run = LoadNamed("a row no golden covers").RunGoldens();

        run.IsOk.Should().BeFalse();
        run.Failed.Should().BeEmpty();
        run.UncoveredRowKeys.Should().Equal("B");
    }

    [Fact]
    public void ACollectRuleReturnsEveryMatchingRowInRowOrder()
    {
        var rule = LoadNamed("collect with two matching rows");

        var facts = new Dictionary<string, DecisionValue> { ["score"] = DecisionValue.Integer(5), ["stage"] = DecisionValue.Enum("Open") };
        var decided = rule.Evaluate(facts, AsOf).Outcome.Should().BeOfType<Decided>().Subject;

        decided.OutcomeJson.Should().Be("""[{"note":"a"},{"note":"b"}]""");
        rule.Table.HitPolicy.Should().Be(HitPolicy.Collect);
        rule.RunGoldens().IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData("""[]""", "brain.fact.enum_not_allowed")]
    [InlineData("""["Open","Closed"]""", null)]
    [InlineData(null, "decisions.facts.type_mismatch|decisions.validation.cell_type_mismatch")]
    public void AnEnumFactIsReadableOnlyAgainstItsList(string? allowed, string? expectedCodes)
    {
        var contract = JsonNode.Parse(TestFiles.Read("tests/fixtures/brain-spec/exercise.contract.json"))!;
        var stage = contract["facts"]!.AsArray().Single(fact => fact!["name"]!.GetValue<string>() == "stage")!.AsObject();
        stage.Remove("allowedValues");
        if (allowed is not null)
        {
            stage["allowedValues"] = JsonNode.Parse(allowed);
        }

        var result = BrainRuleLoader.Load(FailingGoldenDocument().ToJsonString(), [BrainContract.Parse(contract.ToJsonString())]);

        result.Errors.Select(error => error.Code).Distinct().Order(StringComparer.Ordinal)
            .Should().Equal((expectedCodes?.Split('|') ?? []).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnEnumValueMayCarryItsOwnListWhenTheFactDeclaresNone()
    {
        var contract = JsonNode.Parse(TestFiles.Read("tests/fixtures/brain-spec/exercise.contract.json"))!;
        contract["facts"]!.AsArray().Single(fact => fact!["name"]!.GetValue<string>() == "stage")!.AsObject().Remove("allowedValues");
        var document = FailingGoldenDocument();
        foreach (var node in EnumValues(document))
        {
            node["allowedValues"] = new JsonArray("Open", "Closed");
        }

        var result = BrainRuleLoader.Load(document.ToJsonString(), [BrainContract.Parse(contract.ToJsonString())]);

        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""[]""", "brain.fact.enum_not_allowed")]
    [InlineData("""["a","b"]""", "brain.fact.enum_not_allowed")]
    [InlineData("""["low","high"]""", null)]
    public void AnEnumOutcomeFieldChecksItsValuesAgainstItsOwnList(string allowed, string? expectedCode)
    {
        var contract = JsonNode.Parse(TestFiles.Read("tests/fixtures/brain-spec/exercise.contract.json"))!;
        var level = contract["kinds"]![0]!["outcomes"]!.AsArray().Single(field => field!["name"]!.GetValue<string>() == "level")!.AsObject();
        level["allowedValues"] = JsonNode.Parse(allowed);
        var document = FailingGoldenDocument();
        document["body"]!["rows"]![0]!["outcome"]!.AsObject()["level"] = "low";

        var result = BrainRuleLoader.Load(document.ToJsonString(), [BrainContract.Parse(contract.ToJsonString())]);

        string[] expected = expectedCode is null ? [] : [expectedCode];
        result.Errors.Select(error => error.Code).Distinct().Should().Equal(expected);
    }

    [Fact]
    public void AnEnumOutcomeFieldWithNoListAcceptsAnyString()
    {
        var contract = JsonNode.Parse(TestFiles.Read("tests/fixtures/brain-spec/exercise.contract.json"))!;
        contract["kinds"]![0]!["outcomes"]!.AsArray().Single(field => field!["name"]!.GetValue<string>() == "level")!.AsObject().Remove("allowedValues");
        var document = FailingGoldenDocument();
        document["body"]!["rows"]![0]!["outcome"]!.AsObject()["level"] = "anything";

        BrainRuleLoader.Load(document.ToJsonString(), [BrainContract.Parse(contract.ToJsonString())]).Errors.Should().BeEmpty();
    }

    private static JsonNode FailingGoldenDocument() =>
        Fixtures().Single(item => item!["name"]!.GetValue<string>() == "a golden that fails when run")!["document"]!.DeepClone();

    private static List<JsonObject> EnumValues(JsonNode node) => node switch
    {
        JsonObject obj when obj["type"]?.GetValue<string>() == "Enum" && obj.ContainsKey("value") => [obj],
        JsonObject obj => [.. obj.SelectMany(member => member.Value is null ? [] : EnumValues(member.Value))],
        JsonArray array => [.. array.SelectMany(item => item is null ? [] : EnumValues(item))],
        _ => [],
    };

    private static bool Decided(BrainRule rule, int score) =>
        rule.Evaluate(new Dictionary<string, DecisionValue> { ["score"] = DecisionValue.Integer(score) }, AsOf).Outcome is Decided;

    private static JsonArray Fixtures() => TestFiles.ReadJson(FixturePath).AsArray();

    private static IEnumerable<object[]> Select(Func<JsonNode, bool> predicate) =>
        Fixtures().Select((item, index) => (item: item!, index)).Where(entry => predicate(entry.item)).Select(entry => new object[] { entry.index });

    private static BrainRule LoadNamed(string name) =>
        LoadValid(Fixtures().Select((item, index) => (item, index)).Single(entry => entry.item!["name"]!.GetValue<string>() == name).index);

    private static BrainRule LoadValid(int index)
    {
        var fixture = Fixtures()[index]!;
        var result = BrainRuleLoader.Load(fixture["document"]!.ToJsonString(), [TestFiles.ContractOf(fixture)]);
        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors.Select(error => $"{error.Code}@{error.Path}")));
        return result.Rule!;
    }
}

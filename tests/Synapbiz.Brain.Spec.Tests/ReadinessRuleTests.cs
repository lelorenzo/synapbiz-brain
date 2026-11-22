using FluentAssertions;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Spec.Tests;

/// <summary>A synthetic clinical-style rule: collect policy, an Enum fact and an Integer fact with ranges, its own contract.</summary>
public sealed class ReadinessRuleTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 18);

    [Fact]
    public void TheExampleContractDeclaresTheReadinessKind()
    {
        var contract = ReadinessContract();

        contract.Address.Should().Be("synthetic@1");
        contract.TryGetKind("synthetic.readiness", out var kind).Should().BeTrue();
        kind!.Facts.Should().BeEquivalentTo(
            [new FactDescriptor("stage", FactType.Enum, false, ["Intake", "Review", "Complete"]), new FactDescriptor("score", FactType.Integer)],
            options => options.WithStrictOrdering());
        kind.OutcomeFields.Should().Equal(new OutcomeFieldDescriptor("note", FactType.Code));
    }

    [Fact]
    public void TheExampleRuleLoadsAndItsTableIsValid()
    {
        var rule = LoadRule();

        rule.Id.Should().Be("readiness-notes");
        rule.Version.Should().Be(3);
        rule.Table.HitPolicy.Should().Be(HitPolicy.Collect);
        rule.Table.Rows.Select(row => row.Key).Should().Equal("low-score", "mid-score", "late-stage");
        rule.Table.DefaultRow.Should().BeNull();
        rule.RowIdsByKey.Should().Equal(new Dictionary<string, string>
        {
            ["low-score"] = "low-score",
            ["mid-score"] = "mid-score",
            ["late-stage"] = "late-stage",
        });
        var report = rule.ValidateTable();
        report.Errors.Should().BeEmpty(because: string.Join("; ", report.Errors.Select(issue => issue.Code)));
    }

    [Fact]
    public void TheRangeConditionsKeepTheirBoundsAndInclusivity()
    {
        var range = LoadRule().Table.Rows[0].Conditions.Single();

        range.Operator.Should().Be(Operator.Range);
        range.Value.Should().Be(DecisionValue.Integer(0));
        range.UpperValue.Should().Be(DecisionValue.Integer(49));
        range.LowerInclusive.Should().BeTrue();
        range.UpperInclusive.Should().BeTrue();
    }

    [Fact]
    public void TheGoldensRunCleanAndCoverEveryRow()
    {
        var rule = LoadRule();

        var run = rule.RunGoldens();

        run.IsOk.Should().BeTrue();
        run.Results.Should().HaveCount(3).And.OnlyContain(result => result.Passed);
        run.UncoveredRowKeys.Should().BeEmpty();
        ConformanceTests.AssertGoldensMatchTheDocument(rule, TestFiles.ReadJson("tests/fixtures/brain-spec/readiness.rule.json")["body"]!["goldens"]!.AsArray());
    }

    [Fact]
    public void ACollectEvaluationReturnsEveryMatchedRowInRowOrder()
    {
        var rule = LoadRule();
        var facts = new Dictionary<string, DecisionValue>
        {
            ["stage"] = DecisionValue.Enum("Complete"),
            ["score"] = DecisionValue.Integer(79),
        };

        var result = rule.Evaluate(facts, AsOf);

        var decided = result.Outcome.Should().BeOfType<Decided>().Subject;
        decided.MatchedRowKeys.Should().Equal("mid-score", "late-stage");
        decided.OutcomeJson.Should().Be("""[{"note":"mid-score"},{"note":"late-stage"}]""");
        result.Trace.MatchedRowKeys.Should().Equal("mid-score", "late-stage");
        result.Trace.VersionNumber.Should().Be(3);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(49, true)]
    [InlineData(50, false)]
    [InlineData(-1, false)]
    public void TheLowScoreRowIncludesBothBounds(int score, bool matches)
    {
        var facts = new Dictionary<string, DecisionValue>
        {
            ["stage"] = DecisionValue.Enum("Intake"),
            ["score"] = DecisionValue.Integer(score),
        };

        var decided = LoadRule().Evaluate(facts, AsOf).Outcome as Decided;

        (decided?.MatchedRowKeys.Contains("low-score") ?? false).Should().Be(matches);
    }

    [Fact]
    public void AValueOutsideTheContractEnumerationIsRefusedWithItsPath()
    {
        var document = TestFiles.ReadJson("tests/fixtures/brain-spec/readiness.rule.json");
        document["body"]!["rows"]![2]!["conditions"]![0]!["values"]![0]!["value"] = "Archived";

        var result = BrainRuleLoader.Load(document.ToJsonString(), [ReadinessContract()]);

        var error = result.Errors.Should().ContainSingle().Subject;
        (error.Code, error.Path).Should().Be(("brain.fact.enum_not_allowed", "/body/rows/2/conditions/0/values/0"));
    }

    [Fact]
    public void AnOutcomeFieldTheKindDoesNotDeclareIsRefusedWithItsPath()
    {
        var json = TestFiles.Read("tests/fixtures/brain-spec/readiness.rule.json").Replace("\"outcome\": { \"note\": \"mid-score\" }", "\"outcome\": { \"label\": \"mid-score\" }", StringComparison.Ordinal);

        var errors = BrainRuleLoader.Load(json, [ReadinessContract()]).Errors.Select(error => (error.Code, error.Path)).ToList();

        errors.Should().Contain(("brain.outcome.unknown_field", "/body/rows/1/outcome/label"));
        errors.Should().Contain(("brain.outcome.required_field", "/body/rows/1/outcome/note"));
    }

    [Fact]
    public void ACollectGoldenWithASingleObjectOutcomeIsRefused()
    {
        var json = TestFiles.Read("tests/fixtures/brain-spec/readiness.rule.json")
            .Replace("\"outcome\": [{ \"note\": \"low-score\" }]", "\"outcome\": { \"note\": \"low-score\" }", StringComparison.Ordinal);

        var result = BrainRuleLoader.Load(json, [ReadinessContract()]);

        var error = result.Errors.Should().ContainSingle().Subject;
        (error.Code, error.Path).Should().Be(("decisions.definition.malformed", "/body/goldens/0/expect/outcome"));
    }

    private static BrainContract ReadinessContract() => BrainContract.Parse(TestFiles.Read("tests/fixtures/brain-spec/readiness.contract.json"));

    private static BrainRule LoadRule()
    {
        var result = BrainRuleLoader.Load(TestFiles.Read("tests/fixtures/brain-spec/readiness.rule.json"), [ReadinessContract()]);
        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors.Select(error => $"{error.Code}@{error.Path}")));
        return result.Rule!;
    }
}

using FluentAssertions;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Compilation;
using Nuvora.Decisions.Engine.Evaluation;
using Nuvora.Decisions.Engine.Facts;
using Nuvora.Decisions.Engine.Goldens;
using Nuvora.Decisions.Engine.Validation;

namespace Nuvora.UnitTests.Decisions.Engine;

public sealed class CollectHitPolicyTests
{
    private const HitPolicy Collect = HitPolicy.Collect;
    private static readonly DateOnly AsOf = new(2026, 10, 30);
    private static readonly DecisionKindDescriptor Descriptor = new()
    {
        Key = DecisionKindKey.From("test.collect"), SchemaVersion = 1,
        Facts = [new("amount", FactType.Integer), new("vip", FactType.Boolean)],
        OutcomeFields = [new("approval", FactType.Code)], EvaluationMode = EvaluationMode.OnDemand,
        ReadPermission = "test.read", AuthorPermission = "test.author", ApprovePermission = "test.approve",
        ReadTracePermission = "test.trace", ContainsPersonalData = false,
    };

    private static Dictionary<string, DecisionValue> Facts(long amount = 100) => new()
    {
        ["amount"] = DecisionValue.Integer(amount), ["vip"] = DecisionValue.Boolean(true),
    };

    private static (DecisionOutcome Outcome, DecisionTrace Trace) Evaluate(DecisionTable table, long amount = 100) =>
        TableEvaluator.Evaluate(TableCompiler.Compile(table, Descriptor), FactSet.Build(Descriptor, Facts(amount)), AsOf);

    [Fact]
    public void OverlappingMatches_PreserveRowOrder_DeduplicateCanonicalOutcomes_AndTraceEveryRow()
    {
        var table = new DecisionTable(Collect,
        [
            new("manager", [], """{"approval":"manager","meta":{"z":2,"a":1}}"""),
            new("cfo", [Condition.Equals("vip", DecisionValue.Boolean(true))], """{"approval":"cfo"}"""),
            new("manager-again", [], """{ "meta": {"a":1,"z":2}, "approval":"manager" }"""),
        ]);

        var (outcome, trace) = Evaluate(table);

        var decided = outcome.Should().BeOfType<Decided>().Subject;
        decided.OutcomeJson.Should().Be("""[{"approval":"manager","meta":{"a":1,"z":2}},{"approval":"cfo"}]""");
        decided.RowKey.Should().Be("manager");
        decided.MatchedRowKeys.Should().Equal("manager", "cfo", "manager-again");
        trace.MatchedRowKeys.Should().Equal("manager", "cfo", "manager-again");
        trace.MatchedRowKey.Should().Be("manager");
        trace.OutcomeJson.Should().Be(decided.OutcomeJson);
    }

    [Theory]
    [InlineData(100, "specific", """[{"approval":"cfo"}]""")]
    [InlineData(1, "default", """[{"approval":"manager"}]""")]
    public void Default_ContributesOnlyWhenNoOtherRowMatches(long amount, string rowKey, string expected)
    {
        var table = new DecisionTable(Collect,
            [new("specific", [Condition.Equals("amount", DecisionValue.Integer(100))], """{"approval":"cfo"}""")],
            new("default", [], """{"approval":"manager"}"""));

        var (outcome, _) = Evaluate(table, amount);

        outcome.Should().BeOfType<Decided>().Which.OutcomeJson.Should().Be(expected);
        ((Decided)outcome).RowKey.Should().Be(rowKey);
    }

    [Fact]
    public void NoMatchesAndNoDefault_ReturnsNoDecision_WithNoMatchedKeys()
    {
        var (outcome, trace) = Evaluate(new(Collect, []));
        outcome.Should().BeOfType<NoDecision>();
        trace.MatchedRowKey.Should().BeNull();
        trace.MatchedRowKeys.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HitPolicy.Unique)]
    [InlineData(HitPolicy.First)]
    public void ExistingPolicies_ExposeTheirSingleMatchedKey(HitPolicy policy)
    {
        var (outcome, trace) = Evaluate(new(policy, [new("only", [], """{"approval":"manager"}""")]));
        ((Decided)outcome).MatchedRowKeys.Should().Equal("only");
        trace.MatchedRowKeys.Should().Equal("only");
    }

    [Fact]
    public void Validator_AllowsOverlapAndNarrowerRowsThatAddOutcomes()
    {
        var table = new DecisionTable(Collect,
        [
            new("broad", [], """{"approval":"manager"}"""),
            new("narrow", [Condition.Equals("vip", DecisionValue.Boolean(true))], """{"approval":"cfo"}"""),
        ]);
        TableValidator.Validate(table, Descriptor).Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validator_OnlyExactConditionSetAndCanonicalOutcomeDuplicatesAreUnreachable()
    {
        var amount = Condition.Equals("amount", DecisionValue.Integer(100));
        var vip = Condition.Equals("vip", DecisionValue.Boolean(true));
        var table = new DecisionTable(Collect,
        [
            new("original", [amount, vip], """{"approval":"manager","rank":1}"""),
            new("adds-outcome", [vip, amount], """{"approval":"cfo"}"""),
            new("duplicate", [vip, amount], """{ "rank":1, "approval":"manager" }"""),
        ], new("default", [], """{"approval":"fallback"}"""));
        var errors = TableValidator.Validate(table, Descriptor).Errors;
        errors.Should().ContainSingle().Which.RowKeys.Should().Equal("duplicate");
        errors[0].Code.Should().Be(TableValidator.CodeUnreachable);
    }

    [Theory]
    [InlineData(false, "{broken")]
    [InlineData(true, "{broken")]
    [InlineData(false, "[]")]
    [InlineData(true, "[]")]
    [InlineData(false, "")]
    [InlineData(true, "")]
    public void Validator_MalformedCollectOutcome_ReturnsAnIssueInsteadOfThrowing(bool defaultRow, string outcome)
    {
        var malformed = new Row("malformed", [], outcome);
        var table = defaultRow ? new DecisionTable(Collect, [], malformed) : new DecisionTable(Collect, [malformed]);
        var validate = () => TableValidator.Validate(table, Descriptor);

        validate.Should().NotThrow("the public validator is total even when a public Row bypasses the codec");
        validate().Errors.Should().ContainSingle(issue => issue.Code == TableValidator.CodeOutcomeInvalid
            && issue.RowKeys.Contains("malformed"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void Validator_RejectsUnsupportedPolicies(int policy)
    {
        TableValidator.Validate(new((HitPolicy)policy, []), Descriptor).Errors
            .Should().ContainSingle(e => e.Code == "decisions.validation.hit_policy_unsupported");
    }

    [Fact]
    public void Goldens_CoverEveryMatchedRow_IncludingRowsThatOnlyMatchTogether()
    {
        var table = TableCompiler.Compile(new(Collect,
            [new("manager", [], """{"approval":"manager"}"""), new("cfo", [], """{"approval":"cfo"}""")]), Descriptor);
        var result = GoldenRunner.Run(table,
            [new("all", Facts(), new ExpectedDecided("""[{"approval":"manager"},{"approval":"cfo"}]"""))], AsOf);
        result.IsOk.Should().BeTrue();
        result.UncoveredRowKeys.Should().BeEmpty();
    }

    [Fact]
    public void Goldens_CompareCanonicalArraysWithoutReorderingOutcomes()
    {
        var table = TableCompiler.Compile(new(Collect,
            [new("manager", [], """{"approval":"manager","rank":1}""")]), Descriptor);
        var result = GoldenRunner.Run(table,
            [new("canonical", Facts(), new ExpectedDecided("""[ { "rank":1, "approval":"manager" } ]"""))], AsOf);
        result.IsOk.Should().BeTrue();
    }

    [Fact]
    public void GoldenMissingAnOutcome_Fails_AndDoesNotCoverRows()
    {
        var table = TableCompiler.Compile(new(Collect,
            [new("manager", [], """{"approval":"manager"}"""), new("cfo", [], """{"approval":"cfo"}""")]), Descriptor);
        var result = GoldenRunner.Run(table,
            [new("missing-cfo", Facts(), new ExpectedDecided("""[{"approval":"manager"}]"""))], AsOf);
        result.Failed.Should().ContainSingle();
        result.UncoveredRowKeys.Should().Equal("manager", "cfo");
    }
}

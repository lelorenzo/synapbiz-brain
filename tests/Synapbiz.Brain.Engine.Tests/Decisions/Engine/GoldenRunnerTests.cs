using FluentAssertions;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Compilation;
using Nuvora.Decisions.Engine.Evaluation;
using Nuvora.Decisions.Engine.Goldens;

namespace Nuvora.UnitTests.Decisions.Engine;

/// <summary>
/// P2-20.2 step 5 (paquete 0.A369): all-pass with full row coverage; a wrong
/// expected outcome; an uncovered row (including the default row); an empty
/// golden set as an ERROR, never a vacuous pass.
/// </summary>
public sealed class GoldenRunnerTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 1);

    private static CompiledTable BuildTable() => TableCompiler.Compile(
        new DecisionTable(
            HitPolicy.Unique,
            [
                Row("web", Condition.Equals("channel", DecisionValue.Enum("Web")), """{"class":"A"}"""),
                Row("pos", Condition.Equals("channel", DecisionValue.Enum("Pos")), """{"class":"B"}"""),
            ],
            DefaultRow: Row("default", [], """{"class":"FALLBACK"}""")),
        ChannelDescriptor());

    [Fact]
    public void AllPassingCases_CoveringEveryRowAndTheDefault_IsOk()
    {
        var table = BuildTable();
        List<GoldenCase> cases =
        [
            new("web_case", Facts("Web"), new ExpectedDecided("""{"class":"A"}""", "web")),
            new("pos_case", Facts("Pos"), new ExpectedDecided("""{"class":"B"}""", "pos")),
            new("fallback_case", Facts("B2B"), new ExpectedDecided("""{"class":"FALLBACK"}""", "default")),
        ];

        var result = GoldenRunner.Run(table, cases, AsOf);

        result.IsOk.Should().BeTrue();
        result.Failed.Should().BeEmpty();
        result.UncoveredRowKeys.Should().BeEmpty();
    }

    [Fact]
    public void AWrongExpectedOutcome_IsReportedAsFailed_NamingTheCaseAndTheActualRow()
    {
        var table = BuildTable();
        List<GoldenCase> cases =
        [
            new("web_case_wrong", Facts("Web"), new ExpectedDecided("""{"class":"WRONG"}""", "web")),
            new("pos_case", Facts("Pos"), new ExpectedDecided("""{"class":"B"}""", "pos")),
            new("fallback_case", Facts("B2B"), new ExpectedDecided("""{"class":"FALLBACK"}""", "default")),
        ];

        var result = GoldenRunner.Run(table, cases, AsOf);

        result.IsOk.Should().BeFalse();
        result.Failed.Should().ContainSingle(f => f.Name == "web_case_wrong");
        var failure = result.Failed.Single();
        failure.Actual.Should().BeOfType<Decided>();
        ((Decided)failure.Actual).RowKey.Should().Be("web");
        result.ToIssues().Should().Contain(i => i.Code == GoldenRunner.ErrorGoldenFailed);
    }

    /// <summary>MUTATION (performed and reverted at gate time): skipping the default row in the coverage computation lets this fact pass without a case covering it.</summary>
    [Fact]
    public void ARowNoPassingCaseHits_IsReportedUncovered_IncludingTheDefaultRow()
    {
        var table = BuildTable();
        List<GoldenCase> cases = [new("web_case", Facts("Web"), new ExpectedDecided("""{"class":"A"}""", "web"))];

        var result = GoldenRunner.Run(table, cases, AsOf);

        result.IsOk.Should().BeFalse();
        result.UncoveredRowKeys.Should().Contain(["pos", "default"]);
        result.ToIssues().Should().Contain(i => i.Code == GoldenRunner.ErrorRowUncovered && i.RowKeys.Contains("default"));
    }

    /// <summary>MUTATION (performed and reverted at gate time): treating an empty golden set as a pass turns this fact RED (the empty-set pass failure mode, GUARD-VERIFICATION-STANDARD §2).</summary>
    [Fact]
    public void AnEmptyGoldenSet_IsAnError_NeverAPass()
    {
        var table = BuildTable();

        var act = () => GoldenRunner.Run(table, [], AsOf);

        act.Should().Throw<DecisionEngineException>().Where(e => e.ErrorCode == GoldenRunner.ErrorEmptySet);
    }

    [Fact]
    public void AnExpectedNoDecisionCase_ThatActuallyHasNoDecision_Passes()
    {
        var table = BuildTableWithoutDefault();

        var result = GoldenRunner.Run(table, [new GoldenCase("pos_has_no_decision", Facts("Pos"), new ExpectedNoDecision())], AsOf);

        result.Failed.Should().BeEmpty();
        result.Results.Single().Passed.Should().BeTrue();
    }

    /// <summary>The expectation type (ExpectedNoDecision) does not match the actual outcome type (Decided) — the Matches catch-all case.</summary>
    [Fact]
    public void AnExpectedNoDecisionCase_ThatActuallyDecides_Fails()
    {
        var table = BuildTableWithoutDefault();

        var result = GoldenRunner.Run(table, [new GoldenCase("web_but_expected_no_decision", Facts("Web"), new ExpectedNoDecision())], AsOf);

        result.Failed.Should().ContainSingle(f => f.Name == "web_but_expected_no_decision");
    }

    [Fact]
    public void AFailedCase_IssueCarriesTheActualAndThePinnedRowKeys()
    {
        var table = BuildTable();
        List<GoldenCase> cases = [new("pos_expected_as_web", Facts("Pos"), new ExpectedDecided("""{"class":"A"}""", "web"))];

        var issues = GoldenRunner.Run(table, cases, AsOf).ToIssues();

        issues.Should().Contain(i => i.Code == GoldenRunner.ErrorGoldenFailed
            && i.RowKeys.Contains("pos") && i.RowKeys.Contains("web"));
    }

    [Fact]
    public void ACaseOverAComposedTable_RunsThroughTheSuppliedResolver()
    {
        var childKey = DecisionKindKey.From("test.golden_child");
        var descriptor = new DecisionKindDescriptor
        {
            Key = DecisionKindKey.From("test.golden_composed"),
            SchemaVersion = 1,
            Facts = [new FactDescriptor("tier", FactType.Code, ComposedFromKind: childKey)],
            OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
            EvaluationMode = EvaluationMode.OnDemand,
            ReadPermission = "p",
            AuthorPermission = "p",
            ApprovePermission = "p",
            ContainsPersonalData = false,
            ReadTracePermission = "test.decisions.read_trace",
        };
        var table = TableCompiler.Compile(
            new DecisionTable(HitPolicy.Unique, [Row("gold", Condition.Equals("tier", DecisionValue.Code("gold")), """{"class":"G"}""")]),
            descriptor);
        List<GoldenCase> cases = [new("gold_case", new Dictionary<string, DecisionValue>(), new ExpectedDecided("""{"class":"G"}""", "gold"))];

        var result = GoldenRunner.Run(table, cases, AsOf, new GoldResolver(childKey));

        result.IsOk.Should().BeTrue();
    }

    private sealed class GoldResolver(DecisionKindKey childKey) : IComposedFactResolver
    {
        public ComposedFactResolution Resolve(DecisionKindKey childKind, DateOnly asOf) => new(
            DecisionValue.Code("gold"),
            new DecisionTrace { KindKey = childKey, IsDecided = true, FactsJson = "{}", FactsHash = "0" });
    }

    private static CompiledTable BuildTableWithoutDefault() => TableCompiler.Compile(
        new DecisionTable(HitPolicy.Unique, [Row("web", Condition.Equals("channel", DecisionValue.Enum("Web")), """{"class":"A"}""")]),
        ChannelDescriptor());

    private static Row Row(string key, IReadOnlyList<Condition> conditions, string outcome) => new(key, conditions, outcome);

    private static Row Row(string key, Condition condition, string outcome) => new(key, [condition], outcome);

    private static Dictionary<string, DecisionValue> Facts(string channel) =>
        new() { ["channel"] = DecisionValue.Enum(channel) };

    private static DecisionKindDescriptor ChannelDescriptor() => new()
    {
        Key = DecisionKindKey.From("test.golden_fixture"),
        SchemaVersion = 1,
        Facts = [new FactDescriptor("channel", FactType.Enum, AllowedValues: ["Web", "Pos", "B2B"])],
        OutcomeFields = [new OutcomeFieldDescriptor("class", FactType.Code)],
        EvaluationMode = EvaluationMode.OnDemand,
        ReadPermission = "p",
        AuthorPermission = "p",
        ApprovePermission = "p",
        ContainsPersonalData = false,
        ReadTracePermission = "test.decisions.read_trace",
    };
}

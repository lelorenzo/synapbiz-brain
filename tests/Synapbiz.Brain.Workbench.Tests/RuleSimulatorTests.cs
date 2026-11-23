using FluentAssertions;
using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Compilation;
using Nuvora.Decisions.Engine.Evaluation;
using Nuvora.Decisions.Engine.Facts;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class RuleSimulatorTests
{
    [Theory]
    [MemberData(nameof(FixtureNames.RuleNames), MemberType = typeof(FixtureNames))]
    public void ForEveryGoldenTheOutcomeAndTraceAreTheEnginesOwn(string name)
    {
        var rule = TestFiles.LoadByName(name);

        foreach (var golden in rule.Goldens)
        {
            var simulated = RuleSimulator.Simulate(rule, golden.Facts, Samples.AsOf);
            var evaluated = rule.Evaluate(golden.Facts, Samples.AsOf);

            simulated.Succeeded.Should().BeTrue(golden.Name);
            simulated.Outcome.Should().BeEquivalentTo(evaluated.Outcome, golden.Name);
            simulated.Trace.Should().BeEquivalentTo(evaluated.Trace, golden.Name);
            simulated.Trace!.VersionNumber.Should().Be((int)rule.Version, golden.Name);
        }
    }

    [Theory]
    [MemberData(nameof(FixtureNames.RuleNames), MemberType = typeof(FixtureNames))]
    public void ForEveryRowAndGoldenTheExplanationMatchesTheCompiledRowAndTheDecidedRows(string name)
    {
        var rule = TestFiles.LoadByName(name);
        var compiled = rule.Compile();
        var compiledRows = compiled.DefaultRow is null ? compiled.Rows : [.. compiled.Rows, compiled.DefaultRow];

        foreach (var golden in rule.Goldens)
        {
            var result = RuleSimulator.Simulate(rule, golden.Facts, Samples.AsOf);

            result.Rows.Select(row => row.Key).Should().Equal(compiledRows.Select(row => row.Key), golden.Name);
            result.Rows.Select(row => row.Matched).Should().Equal(compiledRows.Select(row => row.Matches(golden.Facts)), golden.Name);
            var decidedKeys = (result.Outcome as Decided)?.MatchedRowKeys ?? [];
            result.Rows.Where(row => row.Selected).Select(row => row.Key).Should().Equal(decidedKeys, golden.Name);
            result.Rows.Where(row => row.IsDefault).Should().HaveCount(rule.Table.DefaultRow is null ? 0 : 1);
        }
    }

    [Fact]
    public void ConditionChecksNameTheFactItsActualValueAndWhetherItPassed()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var facts = Samples.Intake("Phone", "billing-late", true, 100, withAccount: true);

        var first = RuleSimulator.Simulate(rule, facts, Samples.AsOf).Rows[0];

        first.Should().BeEquivalentTo(new RowExplanation(
            "urgent-phone", false, true, true,
            [
                new ConditionCheck(0, "urgent", true, DecisionValue.Boolean(true)),
                new ConditionCheck(1, "channel", true, DecisionValue.Enum("Phone")),
                new ConditionCheck(2, "topic", true, DecisionValue.Code("billing-late")),
            ]));
    }

    [Fact]
    public void AFactLeftOutIsReportedAsANullActualValue()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var facts = Samples.Intake("Web", "general", false, 3, withAccount: true);

        var fresh = RuleSimulator.Simulate(rule, facts, Samples.AsOf).Rows.Single(row => row.Key == "fresh-request");

        fresh.Conditions.Should().BeEquivalentTo(
        [
            new ConditionCheck(0, "ageDays", true, DecisionValue.Integer(3)),
            new ConditionCheck(1, "rating", true, null),
        ], options => options.WithStrictOrdering());
        fresh.Selected.Should().BeTrue();
    }

    [Fact]
    public void WithFirstPolicyALaterMatchingRowIsMatchedButNotSelected()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var facts = Samples.Intake("Phone", "billing-late", true, 100, withAccount: true);

        var rows = RuleSimulator.Simulate(rule, facts, Samples.AsOf).Rows;

        rows.Single(row => row.Key == "urgent-phone").Should().Match<RowExplanation>(row => row.Matched && row.Selected);
        rows.Single(row => row.Key == "billing-topic").Should().Match<RowExplanation>(row => row.Matched && !row.Selected);
        rows.Single(row => row.Key == "old-request").Should().Match<RowExplanation>(row => row.Matched && !row.Selected);
        rows.Single(row => row.Key == "email-invoice").Should().Match<RowExplanation>(row => !row.Matched && !row.Selected);
    }

    [Fact]
    public void WithCollectPolicyEveryMatchedRowIsSelected()
    {
        var rule = TestFiles.LoadReadiness();
        var facts = new Dictionary<string, DecisionValue> { ["stage"] = DecisionValue.Enum("Review"), ["score"] = DecisionValue.Integer(65) };

        var result = RuleSimulator.Simulate(rule, facts, Samples.AsOf);

        result.Rows.Select(row => (row.Key, row.Matched, row.Selected)).Should().Equal(
            ("low-score", false, false), ("mid-score", true, true), ("late-stage", true, true));
        ((Decided)result.Outcome!).MatchedRowKeys.Should().Equal("mid-score", "late-stage");
    }

    [Fact]
    public void TheDefaultRowIsSelectedWhenNoRowMatches()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var facts = Samples.Intake("Web", "general", false, 10, 4m, withAccount: true);

        var result = RuleSimulator.Simulate(rule, facts, Samples.AsOf);

        result.Rows.Take(8).Should().OnlyContain(row => !row.Matched && !row.Selected);
        result.Rows[8].Should().BeEquivalentTo(new RowExplanation("fallback", true, true, true, []));
        ((Decided)result.Outcome!).RowKey.Should().Be("fallback");
    }

    [Fact]
    public void ADefaultRowIsOnlyMatchedAndNotSelectedWhenARowWins()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");

        var rows = RuleSimulator.Simulate(rule, Samples.Intake("Phone", "x", true, 1, withAccount: true), Samples.AsOf).Rows;

        rows[^1].Should().Match<RowExplanation>(row => row.IsDefault && row.Matched && !row.Selected);
    }

    [Fact]
    public void NoDecisionMarksNoRowAsSelected()
    {
        var rule = TestFiles.LoadIntake("intake.unique.rule.json");

        var result = RuleSimulator.Simulate(rule, Samples.Intake("Web", "general", false, 5), Samples.AsOf);

        result.Outcome.Should().BeOfType<NoDecision>();
        result.Rows.Should().OnlyContain(row => !row.Matched && !row.Selected);
        result.Trace!.IsDecided.Should().BeFalse();
    }

    [Fact]
    public void AMissingRequiredFactIsTheEnginesOwnFailureWithNoRows()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var facts = Samples.Intake("Web", "general", false, 5);
        facts.Remove("ageDays");

        var result = RuleSimulator.Simulate(rule, facts, Samples.AsOf);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Code.Should().Be(FactSet.ErrorMissing);
        result.Failure.Message.Should().Contain("ageDays");
        result.Outcome.Should().BeNull();
        result.Trace.Should().BeNull();
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void AMistypedFactIsTheEnginesOwnFailureWithNoRows()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var facts = Samples.Intake("Web", "general", false, 5);
        facts["ageDays"] = DecisionValue.Code("five");

        var result = RuleSimulator.Simulate(rule, facts, Samples.AsOf);

        result.Failure!.Code.Should().Be("decisions.facts.type_mismatch");
        result.Rows.Should().BeEmpty();
        result.Outcome.Should().BeNull();
    }

    [Fact]
    public void AnUnknownFactIsRefusedAsMissing()
    {
        var facts = Samples.Intake("Web", "general", false, 5);
        facts["nothing"] = DecisionValue.Code("x");

        RuleSimulator.Simulate(TestFiles.LoadIntake("intake.first.rule.json"), facts, Samples.AsOf).Failure!.Code
            .Should().Be("decisions.facts.missing");
    }

    [Fact]
    public void AnOverlapUnderTheUniquePolicyIsTheEnginesMultipleMatchesFailureWithTwoMatchedUnselectedRows()
    {
        var rule = TestFiles.LoadIntake("intake.unique.rule.json");

        var result = RuleSimulator.Simulate(rule, Samples.Intake("Phone", "general", true, 5), Samples.AsOf);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Code.Should().Be("decisions.evaluation.multiple_matches");
        result.Outcome.Should().BeNull();
        result.Trace.Should().BeNull();
        result.Rows.Select(row => (row.Key, row.Matched, row.Selected)).Should().Equal(
            ("urgent-any-channel", true, false), ("phone-any-urgency", true, false));
    }

    [Fact]
    public void ARuleThatDoesNotCompileIsReportedAsAFailureWithNoRows()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var selfComposed = rule.Kind with { Facts = [.. rule.Kind.Facts, new FactDescriptor("loop", FactType.Integer, false, null, rule.Kind.Key)] };

        var result = RuleSimulator.Simulate(Samples.WithTable(rule, rule.Table, selfComposed), Samples.Intake("Web", "x", false, 1), Samples.AsOf);

        result.Failure!.Code.Should().Be(TableCompiler.ErrorCompositionCycle);
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void AKindWithComposedFactsHasNoRowExplanationsAndNeedsAResolver()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var composed = rule.Kind with
        {
            Facts = [.. rule.Kind.Facts, new FactDescriptor("derived", FactType.Integer, false, null, DecisionKindKey.From("sample.child"))],
        };
        var table = new DecisionTable(HitPolicy.First, [Samples.RowOf("big", """{"route":"Fast","queue":"q"}""", Condition.Equals("derived", DecisionValue.Integer(7)))], null);
        var built = Samples.WithTable(rule, table, composed);
        var facts = Samples.Intake("Web", "x", false, 1);

        var withoutResolver = RuleSimulator.Simulate(built, facts, Samples.AsOf);
        var withResolver = RuleSimulator.Simulate(built, facts, Samples.AsOf, new FixedResolver(7));

        withoutResolver.Failure!.Code.Should().Be(TableEvaluator.ErrorResolverRequired);
        withoutResolver.Rows.Should().BeEmpty();
        withResolver.Succeeded.Should().BeTrue();
        withResolver.Rows.Should().BeEmpty();
        ((Decided)withResolver.Outcome!).RowKey.Should().Be("big");
        withResolver.Trace!.Children.Should().ContainSingle();
    }

    [Fact]
    public void ASimulationDoesNotChangeTheFactsItWasGiven()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var facts = Samples.Intake("Web", "general", false, 5);
        var before = new Dictionary<string, DecisionValue>(facts);

        RuleSimulator.Simulate(rule, facts, Samples.AsOf);

        facts.Should().Equal(before);
    }

    private sealed class FixedResolver(long value) : IComposedFactResolver
    {
        public ComposedFactResolution Resolve(DecisionKindKey childKind, DateOnly asOf) =>
            new(DecisionValue.Integer(value), new DecisionTrace
            {
                KindKey = childKind,
                IsDecided = true,
                FactsJson = "{}",
                FactsHash = "0",
            });
    }
}

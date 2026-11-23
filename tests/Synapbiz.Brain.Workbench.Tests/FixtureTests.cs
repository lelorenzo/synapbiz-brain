using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The synthetic fixtures every other test relies on must themselves be sound.</summary>
public sealed class FixtureTests
{
    [Theory]
    [MemberData(nameof(FixtureNames.RuleNames), MemberType = typeof(FixtureNames))]
    public void EveryFixtureLoadsItsGoldensPassAndCoverEveryRow(string name)
    {
        var rule = TestFiles.LoadByName(name);

        var run = rule.RunGoldens();

        run.Failed.Should().BeEmpty();
        run.UncoveredRowKeys.Should().BeEmpty();
        run.Results.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("readiness")]
    [InlineData("intake.first")]
    [InlineData("intake.first.v2")]
    public void EveryFixtureExceptTheUniqueOneHasAValidTable(string name)
    {
        var report = TestFiles.LoadByName(name).ValidateTable();

        report.Errors.Should().BeEmpty(because: string.Join("; ", report.Errors.Select(issue => issue.Code + " " + issue.Message)));
    }

    [Fact]
    public void TheUniqueFixtureIsExemptFromTheValidTableCheckBecauseItsRowsOverlapOnPurpose()
    {
        var rule = TestFiles.LoadByName("intake.unique");

        rule.Table.HitPolicy.Should().Be(HitPolicy.Unique);
        var report = rule.ValidateTable();
        report.Errors.Should().NotBeEmpty("the two rows overlap for an urgent call, which the validator reports; the simulator tests use that overlap");
    }

    [Fact]
    public void TheIntakeContractDeclaresAllEightFactTypesAndTheOutcomeFieldsOfTheBrief()
    {
        var contract = TestFiles.IntakeContract();

        contract.TryGetKind("sample.intake", out var kind).Should().BeTrue();
        kind!.Facts.Select(fact => fact.Type).Distinct().Should().HaveCount(8);
        kind.Facts.Count(fact => fact.Nullable).Should().BeGreaterThanOrEqualTo(3);
        kind.Facts.Single(fact => fact.Type == Nuvora.Decisions.Contracts.Kinds.FactType.Enum).AllowedValues.Should().NotBeEmpty();
        kind.OutcomeFields.Select(field => (field.Type, field.Nullable)).Should().Contain(
        [
            (Nuvora.Decisions.Contracts.Kinds.FactType.Enum, false),
            (Nuvora.Decisions.Contracts.Kinds.FactType.Code, false),
            (Nuvora.Decisions.Contracts.Kinds.FactType.Boolean, true),
            (Nuvora.Decisions.Contracts.Kinds.FactType.Integer, true),
        ]);
    }

    [Fact]
    public void TheFirstFixtureUsesEveryOperator()
    {
        var operators = TestFiles.LoadIntake("intake.first.rule.json").Table.Rows
            .SelectMany(row => row.Conditions).Select(condition => condition.Operator).Distinct().ToList();

        operators.Should().BeEquivalentTo(Enum.GetValues<Operator>());
    }
}

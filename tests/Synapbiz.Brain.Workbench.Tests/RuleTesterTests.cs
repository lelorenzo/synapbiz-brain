using FluentAssertions;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Evaluation;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class RuleTesterTests
{
    [Theory]
    [MemberData(nameof(FixtureNames.RuleNames), MemberType = typeof(FixtureNames))]
    public void TheRunAgreesWithTheEnginesGoldenRunnerOnCoverageAndOnEveryCase(string name)
    {
        var rule = TestFiles.LoadByName(name);
        var runner = rule.RunGoldens();

        var run = RuleTester.Run(rule);

        run.UncoveredRowKeys.Should().Equal(runner.UncoveredRowKeys);
        run.Cases.Select(testCase => testCase.Passed).Should().Equal(runner.Results.Select(result => result.Passed));
        run.Cases.Select(testCase => testCase.Name).Should().Equal(runner.Results.Select(result => result.Name));
        run.Cases.Should().OnlyContain(testCase => testCase.FromDocument && testCase.Failure == null);
        run.Cases.Select(testCase => testCase.Actual).Should().BeEquivalentTo(runner.Results.Select(result => result.Actual), options => options.WithStrictOrdering());
        run.IsOk.Should().Be(runner.IsOk);
        run.PassedCount.Should().Be(runner.Results.Count);
        run.FailedCount.Should().Be(0);
    }

    [Fact]
    public void ACaseCarriesItsFactsExpectationAndTheMatchedRowKeys()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");

        var run = RuleTester.Run(rule);

        var first = run.Cases[0];
        first.Facts.Should().BeSameAs(rule.Goldens[0].Facts);
        first.Expected.Should().BeSameAs(rule.Goldens[0].Expected);
        first.MatchedRowKeys.Should().Equal("urgent-phone");
        run.Cases[^1].MatchedRowKeys.Should().Equal("fallback");
    }

    [Fact]
    public void ACaseThatExpectsNoDecisionHasNoMatchedRowKeys()
    {
        var run = RuleTester.Run(TestFiles.LoadIntake("intake.unique.rule.json"));

        run.Cases[2].Passed.Should().BeTrue();
        run.Cases[2].Actual.Should().BeOfType<NoDecision>();
        run.Cases[2].MatchedRowKeys.Should().BeEmpty();
    }

    [Fact]
    public void ACollectCaseListsEveryMatchedRowKey()
    {
        var run = RuleTester.Run(TestFiles.LoadReadiness());

        run.Cases[1].MatchedRowKeys.Should().Equal("mid-score", "late-stage");
    }

    [Fact]
    public void AFailingExtraCaseIsReportedWithItsExpectedAndActualOutcome()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var wrong = Samples.Case(
            "Expects the wrong route",
            Samples.Intake("Phone", "x", true, 1, withAccount: true),
            new ExpectedDecided("""{"queue":"urgent","route":"Fast"}""", "urgent-phone"));

        var run = RuleTester.Run(rule, [wrong]);

        var extra = run.Cases[^1];
        extra.FromDocument.Should().BeFalse();
        extra.Passed.Should().BeFalse();
        extra.Failure.Should().BeNull();
        extra.Expected.Should().BeSameAs(wrong.Expected);
        ((Decided)extra.Actual!).RowKey.Should().Be("urgent-phone");
        extra.MatchedRowKeys.Should().Equal("urgent-phone");
        run.FailedCount.Should().Be(1);
        run.PassedCount.Should().Be(rule.Goldens.Count);
        run.IsOk.Should().BeFalse();
        run.UncoveredRowKeys.Should().BeEmpty("the document's own cases still cover every row");
    }

    [Fact]
    public void APassingExtraCaseIsFlaggedAsNotFromTheDocument()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var extra = Samples.Case("Extra", Samples.Intake("Web", "export", false, 1), new ExpectedDecided("""{"queue":"new","route":"Fast"}""", "fresh-request"));

        var run = RuleTester.Run(rule, [extra]);

        run.Cases.Count(testCase => testCase.FromDocument).Should().Be(rule.Goldens.Count);
        run.Cases[^1].Passed.Should().BeTrue();
        run.IsOk.Should().BeTrue();
    }

    [Fact]
    public void AnExtraCaseWithAMissingRequiredFactFailsWithTheEnginesCodeAndDoesNotStopTheRun()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var broken = Samples.Case("Missing facts", new Dictionary<string, DecisionValue>(), new ExpectedNoDecision());
        var good = Samples.Case("Fine", Samples.Intake("Web", "export", false, 1), new ExpectedDecided("""{"queue":"new","route":"Fast"}"""));

        var run = RuleTester.Run(rule, [broken, good]);

        var failed = run.Cases[^2];
        failed.Passed.Should().BeFalse();
        failed.Actual.Should().BeNull();
        failed.MatchedRowKeys.Should().BeEmpty();
        failed.Failure!.Code.Should().Be("decisions.facts.missing");
        failed.Failure.Message.Should().NotBeEmpty();
        run.Cases[^1].Passed.Should().BeTrue();
        run.FailedCount.Should().Be(1);
    }

    [Fact]
    public void ARuleWithNoCasesReportsEveryRowUncoveredAndDoesNotThrow()
    {
        var rule = TestFiles.LoadIntakeText(TestFiles.WithoutGoldens(TestFiles.IntakeText("intake.first.rule.json")));

        var run = RuleTester.Run(rule);

        run.Cases.Should().BeEmpty();
        run.UncoveredRowKeys.Should().Equal(rule.Table.Rows.Select(row => row.Key).Append("fallback"));
        run.PassedCount.Should().Be(0);
        run.FailedCount.Should().Be(0);
        run.IsOk.Should().BeFalse();
    }

    [Fact]
    public void ExtraCasesCanCoverTheRowsOfARuleWithNoDocumentCases()
    {
        var rule = TestFiles.LoadIntakeText(TestFiles.WithoutGoldens(TestFiles.IntakeText("intake.unique.rule.json")));
        var urgent = Samples.Case("Urgent", Samples.Intake("Web", "x", true, 1), new ExpectedDecided("""{"priority":1,"queue":"urgent","route":"Escalate"}"""));
        var failing = Samples.Case("Phone, wrong", Samples.Intake("Phone", "x", false, 1), new ExpectedNoDecision());

        var run = RuleTester.Run(rule, [urgent, failing]);

        run.UncoveredRowKeys.Should().Equal("phone-any-urgency");
        run.Cases.Select(testCase => testCase.Passed).Should().Equal(true, false);
    }

    [Fact]
    public void AnOverlapIsAFailureOfTheCaseNotOfTheRun()
    {
        var rule = TestFiles.LoadIntake("intake.unique.rule.json");
        var overlap = Samples.Case("Overlap", Samples.Intake("Phone", "x", true, 1), new ExpectedNoDecision());

        var run = RuleTester.Run(rule, [overlap]);

        run.Cases[^1].Failure!.Code.Should().Be("decisions.evaluation.multiple_matches");
        run.Cases[^1].Passed.Should().BeFalse();
    }

    [Fact]
    public void AResolverIsPassedOnToTheRunner()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var composed = rule.Kind with
        {
            Facts = [.. rule.Kind.Facts, new FactDescriptor("derived", FactType.Integer, false, null, DecisionKindKey.From("sample.child"))],
        };
        var table = new DecisionTable(HitPolicy.First, [Samples.RowOf("big", """{"queue":"q","route":"Fast"}""", Condition.Equals("derived", DecisionValue.Integer(7)))], null);
        var golden = Samples.Case("Composed", Samples.Intake("Web", "x", false, 1), new ExpectedDecided("""{"queue":"q","route":"Fast"}""", "big"));
        var built = Samples.WithTable(rule, table, composed, [golden]);

        RuleTester.Run(built).Cases[0].Failure!.Code.Should().Be(TableEvaluator.ErrorResolverRequired);
        var run = RuleTester.Run(built, null, new Resolver());

        run.Cases[0].Passed.Should().BeTrue();
        run.IsOk.Should().BeTrue();
    }

    private sealed class Resolver : IComposedFactResolver
    {
        public ComposedFactResolution Resolve(DecisionKindKey childKind, DateOnly asOf) =>
            new(DecisionValue.Integer(7), new DecisionTrace { KindKey = childKind, IsDecided = true, FactsJson = "{}", FactsHash = "0" });
    }
}

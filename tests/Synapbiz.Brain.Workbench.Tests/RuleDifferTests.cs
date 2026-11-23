using FluentAssertions;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class RuleDifferTests
{
    private static BrainRule First => TestFiles.LoadIntake("intake.first.rule.json");

    private static BrainRule SecondRevision => TestFiles.LoadIntake("intake.first.v2.rule.json");

    private static BrainRule WithRows(BrainRule rule, params Row[] rows) =>
        Samples.WithTable(rule, new DecisionTable(rule.Table.HitPolicy, rows, rule.Table.DefaultRow));

    private static Row Row(string key, params Condition[] conditions) => Samples.RowOf(key, """{"route":"Fast"}""", conditions);

    // ---- structure -----------------------------------------------------------------------------

    [Fact]
    public void ARevisionComparedWithItselfHasNoChanges()
    {
        var diff = RuleDiffer.Diff(First, First);

        diff.HasChanges.Should().BeFalse();
        diff.KindChanged.Should().BeFalse();
        diff.HitPolicyChanged.Should().BeFalse();
        diff.RowOrderChanged.Should().BeFalse();
        diff.Rows.Should().HaveCount(8).And.OnlyContain(row => row.Kind == ChangeKind.Unchanged && !row.ConditionsChanged && !row.OutcomeChanged);
        diff.Rows.Select(row => (row.IndexBefore, row.IndexAfter)).Should().Equal(Enumerable.Range(0, 8).Select(index => ((int?)index, (int?)index)));
        diff.DefaultRow.Should().Be(new RowChange("fallback", ChangeKind.Unchanged, null, null, false, false));
        diff.Cases.Should().HaveCount(9).And.OnlyContain(testCase => testCase.Kind == ChangeKind.Unchanged);
        diff.VersionBefore.Should().Be(1);
        diff.VersionAfter.Should().Be(1);
    }

    [Fact]
    public void TheSecondRevisionDiffersByTheExactRowDefaultRowAndCaseChanges()
    {
        var diff = RuleDiffer.Diff(First, SecondRevision);

        diff.VersionBefore.Should().Be(1);
        diff.VersionAfter.Should().Be(2);
        diff.HasChanges.Should().BeTrue();
        diff.KindChanged.Should().BeFalse();
        diff.HitPolicyChanged.Should().BeFalse();
        diff.RowOrderChanged.Should().BeTrue();
        diff.Rows.Should().Equal(
            new RowChange("urgent-phone", ChangeKind.Unchanged, 0, 0, false, false),
            new RowChange("billing-topic", ChangeKind.Changed, 1, 1, false, true),
            new RowChange("fresh-request", ChangeKind.Changed, 3, 2, true, false),
            new RowChange("high-rating", ChangeKind.Unchanged, 4, 3, false, false),
            new RowChange("old-request", ChangeKind.Unchanged, 6, 4, false, false),
            new RowChange("mid-amount", ChangeKind.Unchanged, 5, 5, false, false),
            new RowChange("no-account", ChangeKind.Unchanged, 7, 6, false, false),
            new RowChange("export-topic", ChangeKind.Added, null, 7, false, false),
            new RowChange("email-invoice", ChangeKind.Removed, 2, null, false, false));
        diff.DefaultRow.Should().Be(new RowChange("fallback", ChangeKind.Changed, null, null, false, true));
        diff.Cases.Should().Equal(
            new CaseChange("An urgent phone request is escalated", ChangeKind.Unchanged),
            new CaseChange("A billing topic on the web goes to billing", ChangeKind.Changed),
            new CaseChange("A new request without a rating is fast", ChangeKind.Unchanged),
            new CaseChange("A good rating with a date is followed up", ChangeKind.Changed),
            new CaseChange("A mid amount is reviewed by hand", ChangeKind.Unchanged),
            new CaseChange("An old request goes to the backlog", ChangeKind.Unchanged),
            new CaseChange("A request with no account is left unassigned", ChangeKind.Unchanged),
            new CaseChange("Anything else uses the fallback", ChangeKind.Changed),
            new CaseChange("An export topic goes to exports", ChangeKind.Added),
            new CaseChange("An invoice by email goes to the billing mailbox", ChangeKind.Removed));
    }

    [Fact]
    public void TheReverseDiffSwapsAddedAndRemoved()
    {
        var diff = RuleDiffer.Diff(SecondRevision, First);

        diff.Rows.Single(row => row.Key == "export-topic").Should().Be(new RowChange("export-topic", ChangeKind.Removed, 7, null, false, false));
        diff.Rows.Single(row => row.Key == "email-invoice").Should().Be(new RowChange("email-invoice", ChangeKind.Added, null, 2, false, false));
        diff.Rows[^1].Key.Should().Be("export-topic");
        diff.Cases.Single(testCase => testCase.Name.StartsWith("An export", StringComparison.Ordinal)).Kind.Should().Be(ChangeKind.Removed);
        diff.RowOrderChanged.Should().BeTrue();
    }

    [Fact]
    public void AVersionDifferenceAloneIsNotAChange()
    {
        var rule = First;
        var newer = Samples.WithTable(rule, rule.Table, version: 9);

        var diff = RuleDiffer.Diff(rule, newer);

        diff.HasChanges.Should().BeFalse();
        diff.VersionAfter.Should().Be(9);
    }

    [Fact]
    public void AChangedHitPolicyIsReported()
    {
        var diff = RuleDiffer.Diff(First, TestFiles.LoadIntake("intake.unique.rule.json"));

        diff.HitPolicyChanged.Should().BeTrue();
        diff.KindChanged.Should().BeFalse();
        diff.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void AChangedKindIsReported()
    {
        var rule = First;
        var other = Samples.WithTable(rule, rule.Table, rule.Kind with { Key = DecisionKindKey.From("sample.other") });

        var diff = RuleDiffer.Diff(rule, other);

        diff.KindChanged.Should().BeTrue();
        diff.HitPolicyChanged.Should().BeFalse();
        diff.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void ARowOrderChangeAloneCountsAsAChange()
    {
        var rule = First;
        var rows = rule.Table.Rows;
        var swapped = Samples.WithTable(rule, new DecisionTable(HitPolicy.First, [rows[1], rows[0], .. rows.Skip(2)], rule.Table.DefaultRow));

        var diff = RuleDiffer.Diff(rule, swapped);

        diff.RowOrderChanged.Should().BeTrue();
        diff.Rows.Should().OnlyContain(row => row.Kind == ChangeKind.Unchanged);
        diff.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void AddingOrRemovingRowsAtTheEndDoesNotChangeTheOrderOfTheSharedRows()
    {
        var rule = First;
        var shorter = WithRows(rule, [.. rule.Table.Rows.Take(3)]);

        var diff = RuleDiffer.Diff(shorter, rule);

        diff.RowOrderChanged.Should().BeFalse();
        diff.Rows.Count(row => row.Kind == ChangeKind.Added).Should().Be(5);
        diff.HasChanges.Should().BeTrue();
        RuleDiffer.Diff(rule, shorter).Rows.Count(row => row.Kind == ChangeKind.Removed).Should().Be(5);
    }

    // ---- conditions ----------------------------------------------------------------------------

    private static readonly DecisionValue A = DecisionValue.Code("a");
    private static readonly DecisionValue B = DecisionValue.Code("b");
    private static readonly DecisionValue C = DecisionValue.Code("c");

    [Fact]
    public void TwoConditionsWithEqualValuesInDifferentListInstancesAreUnchanged()
    {
        var rule = First;
        var before = WithRows(rule, Row("r", Condition.In("topic", [A, B])));
        var after = WithRows(rule, Row("r", Condition.In("topic", new List<DecisionValue> { DecisionValue.Code("a"), DecisionValue.Code("b") })));

        var diff = RuleDiffer.Diff(before, after);

        diff.Rows.Should().Equal(new RowChange("r", ChangeKind.Unchanged, 0, 0, false, false));
        diff.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void AConditionDifferingOnlyInTheContentOfItsValuesIsChanged()
    {
        var rule = First;
        var before = WithRows(rule, Row("r", Condition.In("topic", [A, B])));

        RuleDiffer.Diff(before, WithRows(rule, Row("r", Condition.In("topic", [A, C])))).Rows.Single()
            .Should().Be(new RowChange("r", ChangeKind.Changed, 0, 0, true, false));
        RuleDiffer.Diff(before, WithRows(rule, Row("r", Condition.In("topic", [B, A])))).Rows.Single().ConditionsChanged.Should().BeTrue();
        RuleDiffer.Diff(before, WithRows(rule, Row("r", Condition.In("topic", [A])))).Rows.Single().ConditionsChanged.Should().BeTrue();
    }

    public static TheoryData<int> ConditionVariants => [0, 1, 2, 3, 4, 5, 6, 7];

    [Theory]
    [MemberData(nameof(ConditionVariants))]
    public void EveryPartOfAConditionCountsWhenItDiffers(int variant)
    {
        var rule = First;
        var baseline = Condition.Range("ageDays", DecisionValue.Integer(1), DecisionValue.Integer(5), true, true);
        Condition[] changed =
        [
            baseline with { FactName = "rating" },
            baseline with { Operator = Operator.Equals },
            baseline with { Value = DecisionValue.Integer(2) },
            baseline with { UpperValue = DecisionValue.Integer(6) },
            baseline with { LowerInclusive = false },
            baseline with { UpperInclusive = false },
            baseline with { Value = null },
            baseline with { Values = [A] },
        ];

        var diff = RuleDiffer.Diff(WithRows(rule, Row("r", baseline)), WithRows(rule, Row("r", changed[variant])));

        diff.Rows.Single().Kind.Should().Be(ChangeKind.Changed);
        diff.Rows.Single().ConditionsChanged.Should().BeTrue();
        diff.Rows.Single().OutcomeChanged.Should().BeFalse();
    }

    [Fact]
    public void AnExtraConditionCountsAsAChange()
    {
        var rule = First;

        var diff = RuleDiffer.Diff(WithRows(rule, Row("r")), WithRows(rule, Row("r", Condition.Any("topic"))));

        diff.Rows.Single().ConditionsChanged.Should().BeTrue();
    }

    [Fact]
    public void AnOutcomeCompareIsOrdinalOnTheText()
    {
        var rule = First;
        var before = WithRows(rule, Samples.RowOf("r", """{"route":"Fast"}"""));

        RuleDiffer.Diff(before, WithRows(rule, Samples.RowOf("r", """{"route":"fast"}"""))).Rows.Single().OutcomeChanged.Should().BeTrue();
        RuleDiffer.Diff(before, WithRows(rule, Samples.RowOf("r", """{"route":"Fast"}"""))).Rows.Single().OutcomeChanged.Should().BeFalse();
    }

    // ---- default row ---------------------------------------------------------------------------

    [Fact]
    public void WithoutADefaultRowOnEitherSideTheDefaultChangeIsNull()
    {
        var rule = TestFiles.LoadIntake("intake.unique.rule.json");

        RuleDiffer.Diff(rule, rule).DefaultRow.Should().BeNull();
    }

    [Fact]
    public void ADefaultRowAddedRemovedOrRenamedIsReported()
    {
        var rule = First;
        var without = Samples.WithTable(rule, new DecisionTable(HitPolicy.First, rule.Table.Rows, null));
        var renamed = Samples.WithTable(rule, new DecisionTable(HitPolicy.First, rule.Table.Rows, rule.Table.DefaultRow! with { Key = "otherwise" }));

        RuleDiffer.Diff(without, rule).DefaultRow.Should().Be(new RowChange("fallback", ChangeKind.Added, null, null, false, false));
        RuleDiffer.Diff(rule, without).DefaultRow.Should().Be(new RowChange("fallback", ChangeKind.Removed, null, null, false, false));
        RuleDiffer.Diff(rule, renamed).DefaultRow.Should().Be(new RowChange("otherwise", ChangeKind.Changed, null, null, false, false));
        RuleDiffer.Diff(without, rule).HasChanges.Should().BeTrue();
    }

    [Fact]
    public void ADefaultRowWithChangedConditionsIsChanged()
    {
        var rule = First;
        var other = Samples.WithTable(rule, new DecisionTable(HitPolicy.First, rule.Table.Rows, rule.Table.DefaultRow! with { Conditions = [Condition.Any("topic")] }));

        RuleDiffer.Diff(rule, other).DefaultRow.Should().Be(new RowChange("fallback", ChangeKind.Changed, null, null, true, false));
    }

    // ---- cases ---------------------------------------------------------------------------------

    private static BrainRule WithCases(params GoldenCase[] cases) => Samples.WithTable(First, First.Table, goldens: cases);

    private static readonly IReadOnlyDictionary<string, DecisionValue> SomeFacts = Samples.Intake("Web", "x", false, 1);

    [Fact]
    public void OnlyTheFirstCaseOfANameCounts()
    {
        var before = WithCases(Samples.Case("one", SomeFacts, new ExpectedNoDecision()), Samples.Case("one", Samples.Intake("Phone", "y", true, 9), new ExpectedNoDecision()));
        var after = WithCases(Samples.Case("one", SomeFacts, new ExpectedNoDecision()));

        RuleDiffer.Diff(before, after).Cases.Should().Equal(new CaseChange("one", ChangeKind.Unchanged));
        var (laterRule, earlierRule) = (before, after);
        RuleDiffer.Diff(laterRule, earlierRule).Cases.Should().Equal(new CaseChange("one", ChangeKind.Unchanged));
    }

    [Fact]
    public void ACaseChangesWithItsFactsItsExpectationKindItsOutcomeOrItsRow()
    {
        var baseline = Samples.Case("c", SomeFacts, new ExpectedDecided("""{"route":"Fast"}""", "r"));
        var other = new Dictionary<string, DecisionValue>(SomeFacts) { ["ageDays"] = DecisionValue.Integer(2) };
        var extra = new Dictionary<string, DecisionValue>(SomeFacts) { ["rating"] = DecisionValue.Decimal(1m) };
        var swapped = new Dictionary<string, DecisionValue>(SomeFacts);
        swapped.Remove("topic");
        swapped["other"] = DecisionValue.Code("x");

        ChangeKind Kind(GoldenCase later) => RuleDiffer.Diff(WithCases(baseline), WithCases(later)).Cases.Single().Kind;

        Kind(Samples.Case("c", new Dictionary<string, DecisionValue>(SomeFacts), baseline.Expected)).Should().Be(ChangeKind.Unchanged);
        Kind(Samples.Case("c", other, baseline.Expected)).Should().Be(ChangeKind.Changed);
        Kind(Samples.Case("c", extra, baseline.Expected)).Should().Be(ChangeKind.Changed);
        Kind(Samples.Case("c", swapped, baseline.Expected)).Should().Be(ChangeKind.Changed);
        Kind(Samples.Case("c", SomeFacts, new ExpectedNoDecision())).Should().Be(ChangeKind.Changed);
        Kind(Samples.Case("c", SomeFacts, new ExpectedDecided("""{"route":"Slow"}""", "r"))).Should().Be(ChangeKind.Changed);
        Kind(Samples.Case("c", SomeFacts, new ExpectedDecided("""{"route":"Fast"}""", "s"))).Should().Be(ChangeKind.Changed);
        Kind(Samples.Case("c", SomeFacts, new ExpectedDecided("""{"route":"Fast"}""", null))).Should().Be(ChangeKind.Changed);
        Kind(Samples.Case("c", SomeFacts, new ExpectedDecided("""{"route":"Fast"}""", "r"))).Should().Be(ChangeKind.Unchanged);
    }

    [Fact]
    public void TwoNoDecisionExpectationsAreEqualAndANumericScaleIsNotADifference()
    {
        var one = Samples.Case("c", new Dictionary<string, DecisionValue> { ["rating"] = DecisionValue.Decimal(1.50m) }, new ExpectedNoDecision());
        var two = Samples.Case("c", new Dictionary<string, DecisionValue> { ["rating"] = DecisionValue.Decimal(1.5m) }, new ExpectedNoDecision());

        RuleDiffer.Diff(WithCases(one), WithCases(two)).Cases.Single().Kind.Should().Be(ChangeKind.Unchanged);
    }

    // ---- behaviour -----------------------------------------------------------------------------

    [Fact]
    public void SamplesAreTheGoldensOfTheLaterRevisionThenTheOnesOnlyTheEarlierHad()
    {
        var samples = RuleDiffer.SamplesFromGoldens(First, SecondRevision);

        samples.Select(sample => sample.Name).Should().Equal(
            "An urgent phone request is escalated",
            "A billing topic on the web goes to billing",
            "A new request without a rating is fast",
            "A good rating with a date is followed up",
            "A mid amount is reviewed by hand",
            "An old request goes to the backlog",
            "A request with no account is left unassigned",
            "Anything else uses the fallback",
            "An export topic goes to exports",
            "An invoice by email goes to the billing mailbox");
        samples[3].Facts["receivedOn"].Should().Be(DecisionValue.Date(new DateOnly(2026, 4, 10)), "the later revision's case wins");
    }

    [Fact]
    public void SamplesTakeTheFirstCaseOfAName()
    {
        var before = WithCases(Samples.Case("x", Samples.Intake("Web", "first", false, 1), new ExpectedNoDecision()), Samples.Case("x", Samples.Intake("Web", "second", false, 1), new ExpectedNoDecision()));
        var after = WithCases(Samples.Case("y", SomeFacts, new ExpectedNoDecision()), Samples.Case("y", Samples.Intake("Web", "later", false, 1), new ExpectedNoDecision()));

        var samples = RuleDiffer.SamplesFromGoldens(before, after);

        samples.Select(sample => sample.Name).Should().Equal("y", "x");
        samples[0].Facts["topic"].Should().Be(DecisionValue.Code("x"));
        samples[1].Facts["topic"].Should().Be(DecisionValue.Code("first"));
    }

    [Fact]
    public void TheTwoRevisionsDecideDifferentlyOnExactlyTheChangedSamples()
    {
        var before = First;
        var after = SecondRevision;
        var samples = RuleDiffer.SamplesFromGoldens(before, after);

        var changes = RuleDiffer.CompareBehaviour(before, after, samples, Samples.AsOf);

        changes.Select(change => (change.Name, change.Differs)).Should().Equal(
            ("An urgent phone request is escalated", false),
            ("A billing topic on the web goes to billing", true),
            ("A new request without a rating is fast", false),
            ("A good rating with a date is followed up", false),
            ("A mid amount is reviewed by hand", false),
            ("An old request goes to the backlog", false),
            ("A request with no account is left unassigned", false),
            ("Anything else uses the fallback", true),
            ("An export topic goes to exports", true),
            ("An invoice by email goes to the billing mailbox", true));
        var billing = changes[1];
        billing.Before.Succeeded.Should().BeTrue();
        billing.Before.Trace!.OutcomeJson.Should().Contain("\"billing\"");
        billing.After.Trace!.OutcomeJson.Should().Contain("\"billing-desk\"");
        changes[9].After.Rows.Single(row => row.Selected).Key.Should().Be("fresh-request");
    }

    [Fact]
    public void ARevisionComparedWithItselfNeverDiffers()
    {
        var rule = First;

        RuleDiffer.CompareBehaviour(rule, rule, RuleDiffer.SamplesFromGoldens(rule, rule), Samples.AsOf).Should().OnlyContain(change => !change.Differs);
    }

    [Fact]
    public void ASampleThatFailsOnOneSideOnlyDiffers()
    {
        var overlap = new BehaviourSample("An urgent call", Samples.Intake("Phone", "x", true, 1, withAccount: true));

        var changes = RuleDiffer.CompareBehaviour(First, TestFiles.LoadIntake("intake.unique.rule.json"), [overlap], Samples.AsOf);

        changes.Single().Differs.Should().BeTrue();
        changes.Single().Before.Succeeded.Should().BeTrue();
        changes.Single().After.Failure!.Code.Should().Be("decisions.evaluation.multiple_matches");
        RuleDiffer.CompareBehaviour(TestFiles.LoadIntake("intake.unique.rule.json"), First, [overlap], Samples.AsOf).Single().Differs.Should().BeTrue();
    }

    [Fact]
    public void BothFailingWithTheSameCodeIsNotADifferenceAndDifferentCodesIs()
    {
        var rule = First;
        var missing = new BehaviourSample("Nothing", new Dictionary<string, DecisionValue>());
        var selfComposed = rule.Kind with { Facts = [.. rule.Kind.Facts, new FactDescriptor("loop", FactType.Integer, false, null, rule.Kind.Key)] };
        var cyclic = Samples.WithTable(rule, rule.Table, selfComposed);

        RuleDiffer.CompareBehaviour(rule, rule, [missing], Samples.AsOf).Single().Differs.Should().BeFalse();
        var different = RuleDiffer.CompareBehaviour(rule, cyclic, [missing], Samples.AsOf).Single();
        different.Before.Failure!.Code.Should().Be("decisions.facts.missing");
        different.After.Failure!.Code.Should().Be("decisions.composition.cycle");
        different.Differs.Should().BeTrue();
    }

    [Fact]
    public void ADecisionAgainstNoDecisionDiffersAndTwoNoDecisionsDoNot()
    {
        var unique = TestFiles.LoadIntake("intake.unique.rule.json");
        var calm = new BehaviourSample("A calm web request", Samples.Intake("Web", "x", false, 5));

        RuleDiffer.CompareBehaviour(unique, First, [calm], Samples.AsOf).Single().Differs.Should().BeTrue();
        RuleDiffer.CompareBehaviour(First, unique, [calm], Samples.AsOf).Single().Differs.Should().BeTrue();
        RuleDiffer.CompareBehaviour(unique, unique, [calm], Samples.AsOf).Single().Differs.Should().BeFalse();
    }

    [Fact]
    public void TheSameOutcomeFromDifferentRowsDiffersByMatchedRowKeysAndBySequence()
    {
        var rule = First;
        var facts = Samples.Intake("Web", "x", false, 1);
        var sample = new BehaviourSample("s", facts);
        var one = WithRows(rule, Row("one"));
        var two = WithRows(rule, Row("two"));
        var both = Samples.WithTable(rule, new DecisionTable(HitPolicy.Collect, [Row("one"), Row("two")], null));
        var reversed = Samples.WithTable(rule, new DecisionTable(HitPolicy.Collect, [Row("two"), Row("one")], null));

        RuleDiffer.CompareBehaviour(Samples.WithTable(one, new DecisionTable(HitPolicy.First, one.Table.Rows, null)), Samples.WithTable(two, new DecisionTable(HitPolicy.First, two.Table.Rows, null)), [sample], Samples.AsOf)
            .Single().Differs.Should().BeTrue();
        RuleDiffer.CompareBehaviour(both, reversed, [sample], Samples.AsOf).Single().Differs.Should().BeTrue();
        RuleDiffer.CompareBehaviour(both, both, [sample], Samples.AsOf).Single().Differs.Should().BeFalse();
    }

    [Fact]
    public void ADifferentOutcomeFromTheSameRowDiffers()
    {
        var rule = First;
        var before = Samples.WithTable(rule, new DecisionTable(HitPolicy.First, [Samples.RowOf("r", """{"route":"Fast"}""")], null));
        var after = Samples.WithTable(rule, new DecisionTable(HitPolicy.First, [Samples.RowOf("r", """{"route":"Manual"}""")], null));

        RuleDiffer.CompareBehaviour(before, after, [new BehaviourSample("s", Samples.Intake("Web", "x", false, 1))], Samples.AsOf).Single().Differs.Should().BeTrue();
    }
}

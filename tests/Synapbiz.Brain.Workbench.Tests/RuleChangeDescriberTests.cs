using FluentAssertions;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using static Synapbiz.Brain.Workbench.Tests.DraftKit;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class RuleChangeDescriberTests
{
    private static string NoCases => TestFiles.WithoutGoldens(FirstText);

    private static DecisionValue Member(string member) => DecisionValue.Enum(member);

    private static RuleChangeView Describe(string baseText, RuleEdit[] edits, WorkbenchLocale locale = WorkbenchLocale.En, IReadOnlyList<BehaviourSample>? extra = null)
    {
        var before = TestFiles.LoadIntakeText(baseText);
        var after = TestFiles.LoadIntakeText(Draft(baseText, edits));
        return RuleChangeDescriber.Describe(before, after, RuleLabels.FromMeta(before.Meta, locale), RuleLabels.FromMeta(after.Meta, locale), locale, extra);
    }

    private static RuleChangeView Describe(params RuleEdit[] edits) => Describe(FirstText, edits);

    private static SetOutcome Rename(string key, int number) => new SetOutcome(key, "queue", DecisionValue.Code("q" + number));

    // ---- rows ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(WorkbenchLocale.En, "This draft does not change the rule.")]
    [InlineData(WorkbenchLocale.Es, "Este borrador no cambia la regla.")]
    public void ADraftWithNoEditsChangesNothing(WorkbenchLocale locale, string summary)
    {
        var view = Describe(FirstText, [], locale);

        view.Summary.Should().Be(summary);
        view.Rows.Should().BeEmpty();
        view.HitPolicyChanged.Should().BeFalse();
        view.DefaultRowChanged.Should().BeFalse();
        view.CasesThatChange.Should().BeEmpty();
    }

    [Fact]
    public void AChangedRowCarriesBothSentencesBothNumbersAndItsLabel()
    {
        var view = Describe(new SetOutcome("urgent-phone", "route", Member("Manual")));

        view.Summary.Should().Be("This draft changes 1 row, adds none and removes none.");
        view.Rows.Should().Equal(new RowChangeView(
            "urgent-phone",
            "Urgent calls",
            RowChangeTag.Changed,
            1,
            1,
            "If urgent is yes and Contact channel is the phone line, then Route is escalation, queue is urgent, notify is yes and priority is 1.",
            "If urgent is yes and Contact channel is the phone line, then Route is manual review, queue is urgent, notify is yes and priority is 1.",
            "Urgent calls",
            "Urgent calls"));
    }

    [Fact]
    public void TheSpanishViewUsesTheSpanishLabelsAndSentences()
    {
        var view = Describe(FirstText, [new SetOutcome("urgent-phone", "route", Member("Manual"))], WorkbenchLocale.Es);

        view.Summary.Should().Be("Este borrador cambia 1 fila, no agrega ninguna y no quita ninguna.");
        view.Rows.Single().Label.Should().Be("Llamadas urgentes");
        view.Rows.Single().SentenceAfter.Should().Be("Si urgent es sí y Canal de contacto es la línea telefónica, entonces Ruta es revisión manual, queue es urgent, notify es sí y priority es 1.");
    }

    [Fact]
    public void ARowWithNoTitleIsLabelledByItsHumanisedKey()
    {
        var view = Describe(new SetOutcome("billing-topic", "queue", DecisionValue.Code("x")));

        view.Rows.Single().Label.Should().Be("billing topic");
    }

    [Fact]
    public void AnAddedRowHasNoBeforeSide()
    {
        var view = Describe(new AddRow("new-row", "email-invoice", [Condition.Equals("channel", Member("Web"))], Outcome(("route", Member("Fast")), ("queue", DecisionValue.Code("w")))));

        view.Summary.Should().Be("This draft changes no rows, adds 1 and removes none.");
        view.Rows.Should().Equal(new RowChangeView(
            "new-row", "new row", RowChangeTag.Added, null, 4, null, "If Contact channel is the website, then Route is fast handling and queue is w.", null, null));
    }

    [Fact]
    public void ARemovedRowHasNoAfterSide()
    {
        var view = Describe(FirstText, [new RemoveRow("mid-amount"), new RemoveCase("A mid amount is reviewed by hand")]);

        view.Summary.Should().Be("This draft changes no rows, adds none and removes 1.");
        var row = view.Rows.Should().ContainSingle().Subject;
        (row.Key, row.Tag, row.NumberBefore, row.NumberAfter, row.SentenceAfter).Should().Be(("mid-amount", RowChangeTag.Removed, 6, null, null));
        row.SentenceBefore.Should().StartWith("If amount is greater than 100 USD");
    }

    [Fact]
    public void ARemovedRowIsListedWhereItWasAndAnAddedOneWhereItIsNow()
    {
        var view = Describe(
            FirstText,
            [
                new RemoveRow("urgent-phone"),
                new RemoveCase("An urgent phone request is escalated"),
                new RemoveRow("fresh-request"),
                new RemoveCase("A new request without a rating is fast"),
                new AddRow("new-row", "email-invoice", [], Outcome(("route", Member("Fast")), ("queue", DecisionValue.Code("w")))),
                new SetOutcome("high-rating", "queue", DecisionValue.Code("zz")),
            ]);

        view.Rows.Select(row => (row.Key, row.Tag, row.NumberBefore, row.NumberAfter)).Should().Equal(
            ("urgent-phone", RowChangeTag.Removed, 1, null),
            ("fresh-request", RowChangeTag.Removed, 4, null),
            ("new-row", RowChangeTag.Added, null, 3),
            ("high-rating", RowChangeTag.Changed, 5, 4));
        view.Summary.Should().Be("This draft changes 1 row, adds 1 and removes 2.");
    }

    [Fact]
    public void AMovedRowIsTheOnlyOneReportedAndKeepsItsSentence()
    {
        var view = Describe(new MoveRow("no-account", null));

        view.Summary.Should().Be("This draft changes no rows, adds none and removes none. Moves 1 row.");
        var row = view.Rows.Should().ContainSingle().Subject;
        (row.Key, row.Tag, row.NumberBefore, row.NumberAfter).Should().Be(("no-account", RowChangeTag.Moved, 8, 1));
        row.SentenceBefore.Should().NotBeNull().And.Be(row.SentenceAfter);
    }

    [Fact]
    public void AMovedRowIsDetectedWhetherItMovesUpOrDown()
    {
        Describe(new MoveRow("urgent-phone", "high-rating")).Rows.Select(row => (row.Key, row.Tag, row.NumberAfter)).Should().Equal(("urgent-phone", RowChangeTag.Moved, 5));
        Describe(new MoveRow("high-rating", "urgent-phone")).Rows.Select(row => (row.Key, row.Tag, row.NumberBefore, row.NumberAfter)).Should().Equal(("high-rating", RowChangeTag.Moved, 5, 2));
    }

    [Fact]
    public void ARowThatIsBothChangedAndMovedIsChangedAndCarriesBothNumbers()
    {
        var view = Describe(new SetOutcome("no-account", "queue", DecisionValue.Code("zz")), new MoveRow("no-account", null));

        var row = view.Rows.Should().ContainSingle().Subject;
        (row.Key, row.Tag, row.NumberBefore, row.NumberAfter).Should().Be(("no-account", RowChangeTag.Changed, 8, 1));
        view.Summary.Should().Be("This draft changes 1 row, adds none and removes none.");
    }

    [Fact]
    public void RowsThatOnlyShiftBecauseOfAnAddedRowAreNotReported()
    {
        var view = Describe(new AddRow("first", null, [], Outcome(("route", Member("Fast")), ("queue", DecisionValue.Code("w")))));

        view.Rows.Select(row => row.Key).Should().Equal("first");
    }

    [Fact]
    public void TwoRowsMovedToTheFrontAreBothReported()
    {
        var view = Describe(new MoveRow("no-account", null), new MoveRow("old-request", null));

        view.Rows.Select(row => (row.Key, row.Tag)).Should().Equal(("old-request", RowChangeTag.Moved), ("no-account", RowChangeTag.Moved));
        view.Summary.Should().Be("This draft changes no rows, adds none and removes none. Moves 2 rows.");
        Describe(FirstText, [new MoveRow("no-account", null), new MoveRow("old-request", null)], WorkbenchLocale.Es).Summary
            .Should().Be("Este borrador no cambia ninguna fila, no agrega ninguna y no quita ninguna. Mueve 2 filas.");
    }

    [Fact]
    public void ATheLabelOfARowComesFromTheDraftWhenItHasOneAndFromTheBaseWhenItIsRemoved()
    {
        var view = Describe(
            new SetRowLabel("billing-topic", WorkbenchLocale.En, "Billing"),
            new SetOutcome("billing-topic", "queue", DecisionValue.Code("x")),
            new RemoveRow("urgent-phone"),
            new RemoveCase("An urgent phone request is escalated"));

        view.Rows.Select(row => (row.Key, row.Label)).Should().Equal(("urgent-phone", "Urgent calls"), ("billing-topic", "Billing"));
    }

    // ---- a row whose only change is its name ---------------------------------------------------

    [Fact]
    public void AnUnlabelledRowGivenANameIsListedAsChangedWithEqualSentencesAndBothNames()
    {
        var view = Describe(new SetRowLabel("billing-topic", WorkbenchLocale.En, "Billing"));

        view.Summary.Should().Be("This draft changes 1 row, adds none and removes none.");
        var row = view.Rows.Should().ContainSingle().Subject;
        (row.Key, row.Label, row.Tag, row.NumberBefore, row.NumberAfter).Should().Be(("billing-topic", "Billing", RowChangeTag.Changed, 2, 2));
        row.SentenceBefore.Should().NotBeNull().And.Be(row.SentenceAfter);
        (row.NameBefore, row.NameAfter).Should().Be((null, "Billing"));
    }

    [Fact]
    public void ARenamedRowIsListedAsChangedWithTheOldAndTheNewName()
    {
        var view = Describe(new SetRowLabel("urgent-phone", WorkbenchLocale.En, "Phone escalations"));

        var row = view.Rows.Should().ContainSingle().Subject;
        (row.Tag, row.Label, row.NameBefore, row.NameAfter).Should().Be((RowChangeTag.Changed, "Phone escalations", "Urgent calls", "Phone escalations"));
        row.SentenceBefore.Should().Be(row.SentenceAfter);
    }

    [Fact]
    public void ARowWhoseNameIsClearedIsListedAsChangedWithNoNameAfter()
    {
        var view = Describe(new SetRowLabel("urgent-phone", WorkbenchLocale.En, null));

        var row = view.Rows.Should().ContainSingle().Subject;
        (row.Tag, row.Label, row.NameBefore, row.NameAfter).Should().Be((RowChangeTag.Changed, "urgent phone", "Urgent calls", null));
    }

    [Fact]
    public void ANameAndAStructuralChangeOnOneRowCountOnce()
    {
        var view = Describe(new SetRowLabel("urgent-phone", WorkbenchLocale.En, "Phone escalations"), new SetOutcome("urgent-phone", "route", Member("Manual")));

        view.Summary.Should().Be("This draft changes 1 row, adds none and removes none.");
        var row = view.Rows.Should().ContainSingle().Subject;
        (row.Tag, row.NameBefore, row.NameAfter).Should().Be((RowChangeTag.Changed, "Urgent calls", "Phone escalations"));
        row.SentenceBefore.Should().NotBe(row.SentenceAfter);
    }

    [Fact]
    public void AMovedRowThatIsAlsoRenamedStaysMovedAndCountsOnce()
    {
        var view = Describe(new SetRowLabel("no-account", WorkbenchLocale.En, "No account"), new MoveRow("no-account", null));

        var row = view.Rows.Should().ContainSingle().Subject;
        (row.Tag, row.NameBefore, row.NameAfter).Should().Be((RowChangeTag.Moved, null, "No account"));
        view.Summary.Should().Be("This draft changes no rows, adds none and removes none. Moves 1 row.");
    }

    [Fact]
    public void AWhiteSpaceNameCountsAsNoName()
    {
        var before = TestFiles.LoadIntakeText(FirstText);
        var named = RuleLabels.FromMeta(before.Meta, WorkbenchLocale.En);
        var blank = new RuleLabels { Rows = new Dictionary<string, string> { ["urgent-phone"] = "   " } };

        var view = RuleChangeDescriber.Describe(before, before, named, blank, WorkbenchLocale.En);

        var row = view.Rows.Should().ContainSingle().Subject;
        (row.NameBefore, row.NameAfter).Should().Be(("Urgent calls", null));
        RuleChangeDescriber.Describe(before, before, blank, new RuleLabels { Rows = new Dictionary<string, string> { ["urgent-phone"] = "" } }, WorkbenchLocale.En)
            .Rows.Should().BeEmpty();
    }

    [Fact]
    public void EveryRowCarriesTheNamesOfItsSides()
    {
        var view = Describe(
            new SetRowLabel("billing-topic", WorkbenchLocale.En, "Billing"),
            new RemoveRow("urgent-phone"),
            new RemoveCase("An urgent phone request is escalated"),
            new AddRow("new-row", null, [], Outcome(("route", Member("Fast")), ("queue", DecisionValue.Code("w")))));

        view.Rows.Select(row => (row.Key, row.Tag, row.NameBefore, row.NameAfter)).Should().Equal(
            ("urgent-phone", RowChangeTag.Removed, "Urgent calls", null),
            ("new-row", RowChangeTag.Added, null, null),
            ("billing-topic", RowChangeTag.Changed, null, "Billing"));
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "This draft changes 2 rows, adds none and removes none.")]
    [InlineData(WorkbenchLocale.Es, "Este borrador cambia 2 filas, no agrega ninguna y no quita ninguna.")]
    public void NameOnlyRowsAreCountedInTheSummaryInBothLocales(WorkbenchLocale locale, string summary)
    {
        var view = Describe(
            FirstText,
            [new SetRowLabel("billing-topic", locale, "Billing"), new SetRowLabel("urgent-phone", locale, "Phone escalations")],
            locale);

        view.Summary.Should().Be(summary);
        view.Rows.Select(row => row.Key).Should().Equal("urgent-phone", "billing-topic");
        view.Rows.Select(row => row.Tag).Should().OnlyContain(tag => tag == RowChangeTag.Changed);
    }

    [Fact]
    public void ANameInTheOtherLocaleOnlyChangesNothingInThisOne()
    {
        var view = Describe(FirstText, [new SetRowLabel("billing-topic", WorkbenchLocale.Es, "Facturación")], WorkbenchLocale.En);

        view.Rows.Should().BeEmpty();
        view.Summary.Should().Be("This draft does not change the rule.");
    }

    // ---- summary forms ---------------------------------------------------------------------------

    private static RuleEdit[] Counted(int changed, int added, int removed)
    {
        var rows = new[] { "urgent-phone", "billing-topic", "email-invoice", "fresh-request", "high-rating", "mid-amount", "old-request", "no-account" };
        return
        [
            .. rows.Take(changed).Select((key, index) => Rename(key, index)),
            .. Enumerable.Range(0, added).Select(index => new AddRow("added" + index, null, [], Outcome(("route", Member("Fast")), ("queue", DecisionValue.Code("a")))) as RuleEdit),
            .. rows.TakeLast(removed).Select(key => new RemoveRow(key) as RuleEdit),
        ];
    }

    [Theory]
    [InlineData(0, 0, 0, "This draft does not change the rule.", "Este borrador no cambia la regla.")]
    [InlineData(1, 0, 0, "This draft changes 1 row, adds none and removes none.", "Este borrador cambia 1 fila, no agrega ninguna y no quita ninguna.")]
    [InlineData(2, 0, 0, "This draft changes 2 rows, adds none and removes none.", "Este borrador cambia 2 filas, no agrega ninguna y no quita ninguna.")]
    [InlineData(0, 1, 0, "This draft changes no rows, adds 1 and removes none.", "Este borrador no cambia ninguna fila, agrega 1 y no quita ninguna.")]
    [InlineData(0, 2, 0, "This draft changes no rows, adds 2 and removes none.", "Este borrador no cambia ninguna fila, agrega 2 y no quita ninguna.")]
    [InlineData(0, 0, 1, "This draft changes no rows, adds none and removes 1.", "Este borrador no cambia ninguna fila, no agrega ninguna y quita 1.")]
    [InlineData(0, 0, 3, "This draft changes no rows, adds none and removes 3.", "Este borrador no cambia ninguna fila, no agrega ninguna y quita 3.")]
    [InlineData(1, 1, 1, "This draft changes 1 row, adds 1 and removes 1.", "Este borrador cambia 1 fila, agrega 1 y quita 1.")]
    [InlineData(3, 2, 2, "This draft changes 3 rows, adds 2 and removes 2.", "Este borrador cambia 3 filas, agrega 2 y quita 2.")]
    public void EveryCountFormOfTheSummaryReadsRightInBothLocales(int changed, int added, int removed, string english, string spanish)
    {
        var edits = Counted(changed, added, removed);

        Describe(NoCases, edits).Summary.Should().Be(english);
        Describe(NoCases, edits, WorkbenchLocale.Es).Summary.Should().Be(spanish);
    }

    [Fact]
    public void TheDefaultRowCountsAsARowInTheSummaryAndSetsItsFlag()
    {
        var changed = Describe(new SetDefaultOutcome("queue", DecisionValue.Code("other")));
        changed.DefaultRowChanged.Should().BeTrue();
        changed.Rows.Should().BeEmpty();
        changed.Summary.Should().Be("This draft changes 1 row, adds none and removes none.");

        var document = Parse(NoCases);
        var withoutDefault = document.DeepClone().AsObject();
        withoutDefault["body"]!.AsObject().Remove("defaultRow");
        var added = RuleChangeDescriber.Describe(
            TestFiles.LoadIntakeText(withoutDefault.ToJsonString()), TestFiles.LoadIntakeText(NoCases), RuleLabels.Empty, RuleLabels.Empty, WorkbenchLocale.En);
        var removed = RuleChangeDescriber.Describe(
            TestFiles.LoadIntakeText(NoCases), TestFiles.LoadIntakeText(withoutDefault.ToJsonString()), RuleLabels.Empty, RuleLabels.Empty, WorkbenchLocale.Es);

        added.DefaultRowChanged.Should().BeTrue();
        added.Summary.Should().Be("This draft changes no rows, adds 1 and removes none.");
        removed.DefaultRowChanged.Should().BeTrue();
        removed.Summary.Should().Be("Este borrador no cambia ninguna fila, no agrega ninguna y quita 1.");
    }

    [Fact]
    public void AChangedHitPolicySetsItsFlagAndIsMentionedInTheSummary()
    {
        var other = Parse(NoCases);
        other["body"]!["hitPolicy"] = "unique";
        var before = TestFiles.LoadIntakeText(NoCases);
        var after = TestFiles.LoadIntakeText(other.ToJsonString());

        var english = RuleChangeDescriber.Describe(before, after, RuleLabels.Empty, RuleLabels.Empty, WorkbenchLocale.En);
        var spanish = RuleChangeDescriber.Describe(before, after, RuleLabels.Empty, RuleLabels.Empty, WorkbenchLocale.Es);

        english.HitPolicyChanged.Should().BeTrue();
        english.Summary.Should().Be("This draft changes no rows, adds none and removes none. The hit policy changes.");
        spanish.Summary.Should().Be("Este borrador no cambia ninguna fila, no agrega ninguna y no quita ninguna. Cambia la política de coincidencia.");
    }

    // ---- cases that change -----------------------------------------------------------------------------

    [Fact]
    public void ACaseWhoseOutcomeChangesIsListedWithBothOutcomesAndTheDecidingRows()
    {
        var view = Describe(new SetOutcome("urgent-phone", "route", Member("Manual")));

        var change = view.CasesThatChange.Should().ContainSingle().Subject;
        change.Should().Be(new CaseOutcomeChange(
            "An urgent phone request is escalated",
            "Route is escalation, queue is urgent, notify is yes and priority is 1",
            "Route is manual review, queue is urgent, notify is yes and priority is 1",
            "urgent-phone",
            1,
            "urgent-phone",
            1));
    }

    [Fact]
    public void TheDecidingRowsFollowTheRowsWhenTheyChangePlace()
    {
        var view = Describe(new SetOutcome("no-account", "queue", DecisionValue.Code("zz")), new MoveRow("no-account", "urgent-phone"));

        var change = view.CasesThatChange.Should().ContainSingle().Subject;
        change.Name.Should().Be("A request with no account is left unassigned");
        (change.RowKeyBefore, change.RowNumberBefore, change.RowKeyAfter, change.RowNumberAfter).Should().Be(("no-account", 8, "no-account", 2));
        change.Before.Should().Be("route is Manual, queue is unassigned and notify is no".Replace("route", "Route", StringComparison.Ordinal).Replace("Manual", "manual review", StringComparison.Ordinal));
        change.After.Should().Contain("queue is zz");
    }

    [Fact]
    public void TheDefaultRowIsNotADecidingRowWithANumber()
    {
        var view = Describe(new SetDefaultOutcome("queue", DecisionValue.Code("other")));

        var change = view.CasesThatChange.Should().ContainSingle().Subject;
        change.Name.Should().Be("Anything else uses the fallback");
        (change.RowKeyBefore, change.RowNumberBefore, change.RowKeyAfter, change.RowNumberAfter).Should().Be((null, null, null, null));
        change.After.Should().Contain("queue is other");
    }

    [Fact]
    public void ASampleThatNoLongerDecidesReadsNoResultOrSinResultado()
    {
        var withoutDefault = Parse(NoCases);
        withoutDefault["body"]!.AsObject().Remove("defaultRow");
        var fallbackFacts = Samples.Intake("Web", "general", false, 10, rating: 4m);
        var sample = new BehaviourSample("A fallback sample", fallbackFacts);
        var before = TestFiles.LoadIntakeText(NoCases);
        var after = TestFiles.LoadIntakeText(withoutDefault.ToJsonString());

        var english = RuleChangeDescriber.Describe(before, after, RuleLabels.Empty, RuleLabels.Empty, WorkbenchLocale.En, [sample]);
        var spanish = RuleChangeDescriber.Describe(before, after, RuleLabels.Empty, RuleLabels.Empty, WorkbenchLocale.Es, [sample]);

        english.CasesThatChange.Should().Equal(new CaseOutcomeChange("A fallback sample", "route is Standard and queue is general", "no result", null, null, null, null));
        spanish.CasesThatChange.Single().After.Should().Be("sin resultado");
        spanish.CasesThatChange.Single().Before.Should().Be("route es Standard y queue es general");
    }

    [Fact]
    public void ASampleOneRevisionRefusesReadsNoResultOnThatSide()
    {
        var rule = TestFiles.LoadIntake(First);
        var kind = rule.Kind with { Facts = [.. rule.Kind.Facts, new FactDescriptor("extra", FactType.Integer)] };
        var stricter = Samples.WithTable(rule, rule.Table, kind);

        var view = RuleChangeDescriber.Describe(rule, stricter, RuleLabels.Empty, RuleLabels.Empty, WorkbenchLocale.En);

        view.CasesThatChange.Should().HaveCount(9);
        view.CasesThatChange.Should().OnlyContain(change => change.After == "no result" && change.RowKeyAfter == null && change.RowNumberAfter == null);
        view.CasesThatChange.Should().OnlyContain(change => change.Before != "no result");
        view.CasesThatChange.Single(change => change.Name == "An old request goes to the backlog").RowKeyBefore.Should().Be("old-request");
        view.Rows.Should().BeEmpty();
    }

    [Fact]
    public void ASampleBothRevisionsRefuseIsNotACaseThatChanges()
    {
        var facts = new Dictionary<string, DecisionValue>();

        var view = Describe(FirstText, [new SetOutcome("urgent-phone", "route", Member("Manual"))], extra: [new BehaviourSample("Nothing given", facts)]);

        view.CasesThatChange.Select(change => change.Name).Should().NotContain("Nothing given");
    }

    [Fact]
    public void ExtraSamplesAreComparedBesideTheTestCases()
    {
        var extra = new BehaviourSample("An extra phone request", Samples.Intake("Phone", "x", true, 2, withAccount: true));

        var view = Describe(FirstText, [new SetOutcome("urgent-phone", "queue", DecisionValue.Code("zz"))], extra: [extra]);

        view.CasesThatChange.Select(change => change.Name).Should().Equal("An urgent phone request is escalated", "An extra phone request");
    }

    [Fact]
    public void TheSamplesAreTheCasesOfBothRevisionsAndTheNewerFactsWinByName()
    {
        // The draft re-adds a case under the same name with other facts; the sample is run once, with the draft's facts.
        var draft = new RuleEdit[]
        {
            new RemoveCase("Anything else uses the fallback"),
            new AddCase(new ProposedCase("Anything else uses the fallback", Samples.Intake("Phone", "x", true, 2, withAccount: true), false, """{"route":"Escalate","queue":"urgent","notify":true,"priority":1}""", "urgent-phone")),
            new SetOutcome("urgent-phone", "queue", DecisionValue.Code("zz")),
        };

        var view = Describe(FirstText, draft);

        var change = view.CasesThatChange.Single(change => change.Name == "Anything else uses the fallback");
        change.RowKeyBefore.Should().Be("urgent-phone");
        change.After.Should().Contain("queue is zz");
    }

    [Fact]
    public void AnOutcomeThatReadsTheSameFromAnotherDecidingRowIsNotListed()
    {
        var view = Describe(new AddRow(
            "urgent-clone",
            null,
            [Condition.Equals("urgent", DecisionValue.Boolean(true)), Condition.Equals("channel", Member("Phone"))],
            Outcome(("route", Member("Escalate")), ("queue", DecisionValue.Code("urgent")), ("notify", DecisionValue.Boolean(true)), ("priority", DecisionValue.Integer(1)))));

        view.CasesThatChange.Should().BeEmpty();
        view.Rows.Select(row => row.Key).Should().Equal("urgent-clone");
    }

    [Fact]
    public void ACollectRuleListsEveryMatchedRowAndNamesTheFirstDecidingRow()
    {
        var text = TestFiles.Read("tests/fixtures/brain-spec/readiness.rule.json");
        var before = TestFiles.Load(text, TestFiles.ReadinessContract());
        var after = TestFiles.Load(Draft(text, new SetOutcome("late-stage", "note", DecisionValue.Code("changed"))), TestFiles.ReadinessContract());

        var view = RuleChangeDescriber.Describe(before, after, RuleLabels.Empty, RuleLabels.Empty, WorkbenchLocale.En);

        var change = view.CasesThatChange.Should().ContainSingle().Subject;
        change.Should().Be(new CaseOutcomeChange(
            "A mid score in review matches two rows", "note is mid-score; note is late-stage", "note is mid-score; note is changed", "mid-score", 2, "mid-score", 2));
    }

    [Fact]
    public void ACollectRuleThatNowMatchesTheSameOutcomeTwiceWritesItOnce()
    {
        var text = TestFiles.Read("tests/fixtures/brain-spec/readiness.rule.json");
        var before = TestFiles.Load(text, TestFiles.ReadinessContract());
        var after = TestFiles.Load(Draft(text, new SetOutcome("late-stage", "note", DecisionValue.Code("mid-score"))), TestFiles.ReadinessContract());
        var sample = new BehaviourSample("Mid score in review", new Dictionary<string, DecisionValue> { ["stage"] = Member("Review"), ["score"] = DecisionValue.Integer(65) });

        var view = RuleChangeDescriber.Describe(before, after, RuleLabels.Empty, RuleLabels.Empty, WorkbenchLocale.En, [sample]);

        view.CasesThatChange.Single(change => change.Name == "Mid score in review").After.Should().Be("note is mid-score");
    }

    [Fact]
    public void NullArgumentsAreRefusedAndOptionalSamplesMayBeNull()
    {
        var rule = TestFiles.LoadIntake(First);
        var labels = RuleLabels.Empty;

        new Action[]
        {
            () => RuleChangeDescriber.Describe(null!, rule, labels, labels, WorkbenchLocale.En),
            () => RuleChangeDescriber.Describe(rule, null!, labels, labels, WorkbenchLocale.En),
            () => RuleChangeDescriber.Describe(rule, rule, null!, labels, WorkbenchLocale.En),
            () => RuleChangeDescriber.Describe(rule, rule, labels, null!, WorkbenchLocale.En),
        }.Should().AllSatisfy(call => call.Should().Throw<ArgumentNullException>());
        RuleChangeDescriber.Describe(rule, rule, labels, labels, WorkbenchLocale.En, null).Rows.Should().BeEmpty();
    }

    [Fact]
    public void AnUnknownLocaleIsRefused()
    {
        var rule = TestFiles.LoadIntake(First);

        var call = () => RuleChangeDescriber.Describe(rule, rule, RuleLabels.Empty, RuleLabels.Empty, (WorkbenchLocale)9);

        call.Should().Throw<ArgumentOutOfRangeException>();
    }
}

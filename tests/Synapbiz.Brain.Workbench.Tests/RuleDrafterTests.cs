using System.Text.Json.Nodes;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using static Synapbiz.Brain.Workbench.Tests.DraftKit;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>Every edit goes through the real loader: the draft text is loaded and the loaded rule is what is asserted.</summary>
public sealed class RuleDrafterTests
{
    private static DecisionValue Member(string member) => DecisionValue.Enum(member);

    private static Operator[] Operators(FactType type) =>
        [.. Enum.GetValues<Operator>().Where(op => Nuvora.Decisions.Engine.Conditions.OperatorMatrix.IsAllowed(op, type))];

    // ---- round trip: one test per edit kind ----------------------------------------------------

    [Fact]
    public void SetConditionReplacesTheConditionOnThatFactInPlace()
    {
        var rule = LoadFirst(new SetCondition("urgent-phone", Condition.Equals("channel", Member("Web"))));

        var row = RowOf(rule, "urgent-phone");
        row.Conditions.Select(condition => condition.FactName).Should().Equal("urgent", "channel", "topic");
        Same(ConditionOn(row, "channel"), Condition.Equals("channel", Member("Web"))).Should().BeTrue();
        Same(ConditionOn(row, "urgent"), Condition.Equals("urgent", DecisionValue.Boolean(true))).Should().BeTrue();
    }

    [Fact]
    public void SetConditionAddsTheConditionAtTheEndWhenTheRowHasNone()
    {
        var rule = LoadFirst(new SetCondition("fresh-request", Condition.Equals("urgent", DecisionValue.Boolean(false))));

        RowOf(rule, "fresh-request").Conditions.Select(condition => condition.FactName).Should().Equal("ageDays", "rating", "urgent");
    }

    [Fact]
    public void SetConditionLeavesExactlyOneConditionOnTheFactEvenWhenTheRowHadTwo()
    {
        var document = Parse(FirstText);
        RowNode(document, "billing-topic")["conditions"]!.AsArray().Add(Parse("""{"fact":"topic","op":"isNotNull"}"""));

        var rule = Load(document.ToJsonString(), new SetCondition("billing-topic", Condition.Equals("topic", DecisionValue.Code("x"))));

        RowOf(rule, "billing-topic").Conditions.Select(condition => condition.FactName).Should().Equal("topic", "channel");
    }

    [Fact]
    public void ClearConditionRemovesTheConditionSoTheFactAcceptsAnyValue()
    {
        var rule = LoadFirst(new ClearCondition("urgent-phone", "channel"));

        RowOf(rule, "urgent-phone").Conditions.Select(condition => condition.FactName).Should().Equal("urgent", "topic");
    }

    [Fact]
    public void SetOutcomeReplacesAddsAndRemovesFields()
    {
        var rule = LoadFirst(
            new SetOutcome("urgent-phone", "route", Member("Manual")),
            new SetOutcome("fresh-request", "priority", DecisionValue.Integer(9)),
            new SetOutcome("urgent-phone", "notify", null));

        RowOf(rule, "urgent-phone").Outcome.Should().Be("""{"priority":1,"queue":"urgent","route":"Manual"}""");
        RowOf(rule, "fresh-request").Outcome.Should().Be("""{"priority":9,"queue":"new","route":"Fast"}""");
    }

    [Fact]
    public void AddRowPutsTheRowFirstOrAfterTheNamedRow()
    {
        var conditions = new[] { Condition.Equals("channel", Member("Web")), Condition.IsNull("rating") };
        var rule = LoadFirst(
            new AddRow("web-first", null, conditions, Outcome(("route", Member("Fast")), ("queue", DecisionValue.Code("web")))),
            new AddRow("after-billing", "billing-topic", [], Outcome(("route", Member("Manual")), ("queue", DecisionValue.Code("q")), ("priority", DecisionValue.Integer(3)))));

        rule.Table.Rows.Select(row => row.Key).Should().Equal(
            "web-first", "urgent-phone", "billing-topic", "after-billing", "email-invoice", "fresh-request", "high-rating", "mid-amount", "old-request", "no-account");
        var added = RowOf(rule, "web-first");
        added.Conditions.Should().HaveCount(2);
        ConditionOn(added, "rating").Operator.Should().Be(Operator.IsNull);
        added.Outcome.Should().Be("""{"queue":"web","route":"Fast"}""");
        RowOf(rule, "after-billing").Conditions.Should().BeEmpty();
        rule.RowIdsByKey["web-first"].Should().Be("web-first");
    }

    [Fact]
    public void RemoveRowRemovesTheRowAndItsLabelsInEveryLocale()
    {
        var text = Draft(FirstText, new RemoveRow("urgent-phone"));
        var document = Parse(text);

        document["body"]!["rows"]!.AsArray().Select(row => (string?)row!["legacyKey"]).Should().NotContain("urgent-phone");
        document["meta"]!["display"]!["en"]!["rows"]!.AsObject().Should().BeEmpty();
        document["meta"]!["display"]!["es"]!["rows"]!.AsObject().Should().BeEmpty();
        document["meta"]!["display"]!["en"]!["facts"]!.AsObject().Should().NotBeEmpty("only the row labels go");
    }

    [Fact]
    public void RemoveRowLeavesTheTestCasesThatExpectItSoTheyFailWhenRun()
    {
        var text = Draft(FirstText, new RemoveRow("fresh-request"));

        Parse(text)["body"]!["goldens"]!.AsArray().Should().HaveCount(9);
        var load = TryLoad(text);
        load.IsValid.Should().BeFalse("the case that pins the removed row id is refused by the loader, which is the signal");
        load.Errors.Should().ContainSingle().Which.Code.Should().Be("brain.golden.unknown_row");
    }

    [Fact]
    public void RemoveRowThatNoCaseExpectsLoadsAndItsCasesRunWithoutIt()
    {
        var text = Draft(FirstText, new RemoveRow("urgent-phone"), new RemoveCase("An urgent phone request is escalated"));

        var rule = TestFiles.LoadIntakeText(text);
        rule.Table.Rows.Should().HaveCount(7);
        rule.Goldens.Should().HaveCount(8);
    }

    [Fact]
    public void MoveRowMovesToTheFrontAndAfterAnotherRowInBothDirections()
    {
        LoadFirst(new MoveRow("no-account", null)).Table.Rows.Select(row => row.Key).Should().Equal(
            "no-account", "urgent-phone", "billing-topic", "email-invoice", "fresh-request", "high-rating", "mid-amount", "old-request");
        LoadFirst(new MoveRow("urgent-phone", "high-rating")).Table.Rows.Select(row => row.Key).Should().Equal(
            "billing-topic", "email-invoice", "fresh-request", "high-rating", "urgent-phone", "mid-amount", "old-request", "no-account");
        LoadFirst(new MoveRow("high-rating", "urgent-phone")).Table.Rows.Select(row => row.Key).Should().Equal(
            "urgent-phone", "high-rating", "billing-topic", "email-invoice", "fresh-request", "mid-amount", "old-request", "no-account");
        LoadFirst(new MoveRow("urgent-phone", "billing-topic")).Table.Rows.Select(row => row.Key).Take(2)
            .Should().Equal("billing-topic", "urgent-phone");
    }

    [Fact]
    public void SetDefaultOutcomeChangesTheDefaultRow()
    {
        var rule = LoadFirst(new SetDefaultOutcome("queue", DecisionValue.Code("other")), new SetDefaultOutcome("priority", DecisionValue.Integer(7)));

        rule.Table.DefaultRow!.Outcome.Should().Be("""{"priority":7,"queue":"other","route":"Standard"}""");
    }

    [Fact]
    public void SetCaseExpectationChangesTheRowAndTheOutcomeAndCanLeaveTheRowUnpinned()
    {
        var name = "A billing topic on the web goes to billing";
        var outcome = Outcome(("route", Member("Manual")), ("queue", DecisionValue.Code("review")));

        var pinned = LoadFirst(new SetCaseExpectation(name, "mid-amount", outcome));
        var loose = LoadFirst(new SetCaseExpectation(name, null, outcome));
        var toDefault = LoadFirst(new SetCaseExpectation(name, "fallback", outcome));

        pinned.Goldens.Single(golden => golden.Name == name).Expected.Should().Be(new ExpectedDecided("""{"queue":"review","route":"Manual"}""", "mid-amount"));
        loose.Goldens.Single(golden => golden.Name == name).Expected.Should().Be(new ExpectedDecided("""{"queue":"review","route":"Manual"}""", null));
        toDefault.Goldens.Single(golden => golden.Name == name).Expected.Should().Be(new ExpectedDecided("""{"queue":"review","route":"Manual"}""", "fallback"));
    }

    [Fact]
    public void SetCaseExpectationOnACollectRuleWritesAOneElementList()
    {
        var text = TestFiles.Read("tests/fixtures/brain-spec/readiness.rule.json");
        var draft = Draft(text, new SetCaseExpectation("A low score at intake", "low-score", Outcome(("note", DecisionValue.Code("changed")))));

        var rule = TestFiles.Load(draft, TestFiles.ReadinessContract());
        rule.Goldens.Single(golden => golden.Name == "A low score at intake").Expected.Should().Be(new ExpectedDecided("""[{"note":"changed"}]""", "low-score"));
        rule.Version.Should().Be(4);
    }

    [Fact]
    public void AddCaseAppendsACaseOfEveryFactTypeAndRemoveCaseRemovesByName()
    {
        var facts = Samples.Intake("Phone", "billing-1", true, 4, rating: 2.5m, amount: 10m, receivedOn: new DateOnly(2026, 3, 4), withAccount: true);
        var added = new ProposedCase("A new case", facts, false, """{"route":"Escalate","queue":"urgent","notify":true,"priority":1}""", "urgent-phone");
        var none = new ProposedCase("Nothing expected", Samples.Intake("Web", "x", false, 1), true, null, null);

        var rule = LoadFirst(new AddCase(added), new AddCase(none), new RemoveCase("Anything else uses the fallback"));

        rule.Goldens.Should().HaveCount(10);
        rule.Goldens[^2].Name.Should().Be("A new case");
        rule.Goldens[^2].Facts.Should().BeEquivalentTo(facts);
        rule.Goldens[^2].Expected.Should().Be(new ExpectedDecided("""{"notify":true,"priority":1,"queue":"urgent","route":"Escalate"}""", "urgent-phone"));
        rule.Goldens[^1].Expected.Should().BeOfType<ExpectedNoDecision>();
        rule.Goldens.Select(golden => golden.Name).Should().NotContain("Anything else uses the fallback");
    }

    [Fact]
    public void AnAddedCaseThatTheRuleMeetsPassesWhenRun()
    {
        var facts = Samples.Intake("Phone", "billing-1", true, 4, withAccount: true);
        var added = new ProposedCase("Phone and urgent", facts, false, """{"route":"Escalate","queue":"urgent","notify":true,"priority":1}""", "urgent-phone");

        var run = RuleTester.Run(LoadFirst(new AddCase(added)));

        run.FailedCount.Should().Be(0);
        run.PassedCount.Should().Be(10);
    }

    [Fact]
    public void SetRowLabelWritesAndClearsTheLabelUnderTheRowIdForOneLocale()
    {
        var set = LoadFirst(new SetRowLabel("billing-topic", WorkbenchLocale.Es, "Facturación"), new SetRowLabel("urgent-phone", WorkbenchLocale.En, "Urgent phone"));

        RuleLabels.FromMeta(set.Meta, WorkbenchLocale.Es).Rows.Should().Contain("billing-topic", "Facturación").And.Contain("urgent-phone", "Llamadas urgentes");
        RuleLabels.FromMeta(set.Meta, WorkbenchLocale.En).Rows.Should().Contain("urgent-phone", "Urgent phone").And.NotContainKey("billing-topic");

        var cleared = LoadFirst(new SetRowLabel("urgent-phone", WorkbenchLocale.Es, null), new SetRowLabel("billing-topic", WorkbenchLocale.Es, null));
        RuleLabels.FromMeta(cleared.Meta, WorkbenchLocale.Es).Rows.Should().BeEmpty();
        RuleLabels.FromMeta(cleared.Meta, WorkbenchLocale.En).Rows.Should().Contain("urgent-phone", "Urgent calls");
    }

    [Fact]
    public void SetRowLabelCreatesTheDisplayPathWhenTheRuleHasNone()
    {
        var text = TestFiles.IntakeText("intake.unique.rule.json");

        var rule = TestFiles.LoadIntakeText(Draft(text, new SetRowLabel("phone-any-urgency", WorkbenchLocale.En, "Phone")));

        RuleLabels.FromMeta(rule.Meta, WorkbenchLocale.En).Rows.Should().Equal(new Dictionary<string, string> { ["phone-any-urgency"] = "Phone" });
        rule.Meta["title"]!.GetValue<string>().Should().Be("Intake, unique");
    }

    [Fact]
    public void ClearingALabelThatDoesNotExistChangesNothingButTheVersion()
    {
        var text = TestFiles.IntakeText("intake.unique.rule.json");

        var draft = Draft(text, new SetRowLabel("phone-any-urgency", WorkbenchLocale.Es, null));

        Parse(draft)["meta"]!.ToJsonString().Should().Be(Parse(text)["meta"]!.ToJsonString());
    }

    // ---- every operator the catalogue offers loads -----------------------------------------------

    private static DecisionValue SampleOf(FactType type) => type switch
    {
        FactType.Code => DecisionValue.Code("abc"),
        FactType.Enum => Member("Web"),
        FactType.Boolean => DecisionValue.Boolean(true),
        FactType.Integer => DecisionValue.Integer(5),
        FactType.Decimal => DecisionValue.Decimal(2.5m),
        FactType.Money => DecisionValue.Money(10.25m, "USD"),
        FactType.Date => DecisionValue.Date(new DateOnly(2026, 6, 30)),
        FactType.Reference => DecisionValue.Reference(Samples.Account),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static Condition ConditionFor(string fact, FactType type, Operator op)
    {
        var sample = SampleOf(type);
        return op switch
        {
            Operator.Any => Condition.Any(fact),
            Operator.Equals => Condition.Equals(fact, sample),
            Operator.NotEquals => Condition.NotEquals(fact, sample),
            Operator.In => Condition.In(fact, [sample]),
            Operator.NotIn => Condition.NotIn(fact, [sample]),
            Operator.IsNull => Condition.IsNull(fact),
            Operator.IsNotNull => Condition.IsNotNull(fact),
            Operator.Range => Condition.Range(fact, sample, sample, true, false),
            Operator.StartsWith => Condition.StartsWith(fact, "ab"),
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
    }

    public static TheoryData<string, FactType, Operator> EveryOfferedPair()
    {
        var data = new TheoryData<string, FactType, Operator>();
        var kind = TestFiles.LoadIntake(First).Kind;
        foreach (var fact in kind.Facts)
        {
            foreach (var op in Operators(fact.Type))
            {
                data.Add(fact.Name, fact.Type, op);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryOfferedPair))]
    public void EveryOperatorOfferedForEveryValueTypeIsWrittenAndLoadsBack(string fact, FactType type, Operator op)
    {
        var sent = ConditionFor(fact, type, op);

        var rule = LoadFirst(new SetCondition("fresh-request", sent));

        var loaded = ConditionOn(RowOf(rule, "fresh-request"), fact);
        Same(loaded, sent).Should().BeTrue($"{op} on {fact} ({type}) should load back as written: {loaded}");
    }

    [Fact]
    public void EveryFactTypeOfTheFixturesIsCoveredByThePairs()
    {
        EveryOfferedPair().Select(row => (FactType)row[1]).Distinct().Should().BeEquivalentTo(Enum.GetValues<FactType>());
    }

    [Fact]
    public void ARangeWithOneBoundWritesOnlyThatBoundAndKeepsTheInclusiveFlags()
    {
        var lower = Condition.Range("ageDays", DecisionValue.Integer(3), null, false, true);
        var upper = Condition.Range("ageDays", null, DecisionValue.Integer(9), true, false);

        var rule = LoadFirst(new SetCondition("fresh-request", lower), new SetCondition("old-request", upper));

        Same(ConditionOn(RowOf(rule, "fresh-request"), "ageDays"), lower).Should().BeTrue();
        Same(ConditionOn(RowOf(rule, "old-request"), "ageDays"), upper).Should().BeTrue();
    }

    [Fact]
    public void EveryOutcomeValueTypeIsWrittenInTheShapeTheLoaderReads()
    {
        // The intake kind has Enum, Code, Boolean and Integer outcome fields; the other types are pinned through the text.
        var text = Draft(
            FirstText,
            new SetOutcome("urgent-phone", "dec", DecisionValue.Decimal(1.50m)),
            new SetOutcome("urgent-phone", "money", DecisionValue.Money(2.5m, "EUR")),
            new SetOutcome("urgent-phone", "day", DecisionValue.Date(new DateOnly(2026, 1, 2))),
            new SetOutcome("urgent-phone", "ref", DecisionValue.Reference(Samples.Account)),
            new SetOutcome("urgent-phone", "yes", DecisionValue.Boolean(true)),
            new SetOutcome("urgent-phone", "n", DecisionValue.Integer(-3)));

        var outcome = RowNode(Parse(text), "urgent-phone")["outcome"]!.AsObject();
        outcome["dec"]!.ToJsonString().Should().Be("1.50");
        outcome["money"]!.ToJsonString().Should().Be("""{"amount":2.5,"currency":"EUR"}""");
        outcome["day"]!.GetValue<string>().Should().Be("2026-01-02");
        outcome["ref"]!.GetValue<string>().Should().Be("3f2b8c1e-5a4d-4e6f-9b7a-1c2d3e4f5a6b");
        outcome["yes"]!.GetValue<bool>().Should().BeTrue();
        outcome["n"]!.ToJsonString().Should().Be("-3");
    }

    // ---- simulation after an edit ------------------------------------------------------------------

    private static string RowKeyOf(BrainRule rule, IReadOnlyDictionary<string, DecisionValue> facts) =>
        ((Decided)RuleSimulator.Simulate(rule, facts, Samples.AsOf).Outcome!).RowKey;

    private static string OutcomeOf(BrainRule rule, IReadOnlyDictionary<string, DecisionValue> facts) =>
        ((Decided)RuleSimulator.Simulate(rule, facts, Samples.AsOf).Outcome!).OutcomeJson;

    [Fact]
    public void ChangingAConditionChangesWhichRowDecides()
    {
        var facts = Samples.Intake("Web", "billing-late", false, 3);
        var before = TestFiles.LoadIntake(First);
        var after = LoadFirst(new SetCondition("billing-topic", Condition.StartsWith("topic", "pay")));

        RowKeyOf(before, facts).Should().Be("billing-topic");
        RowKeyOf(after, facts).Should().Be("fresh-request");
    }

    [Fact]
    public void ChangingAnOutcomeChangesTheDecision()
    {
        var facts = Samples.Intake("Phone", "billing-late", true, 100, withAccount: true);
        var before = TestFiles.LoadIntake(First);
        var after = LoadFirst(new SetOutcome("urgent-phone", "route", Member("Manual")), new SetOutcome("urgent-phone", "priority", null));

        OutcomeOf(before, facts).Should().Be("""{"notify":true,"priority":1,"queue":"urgent","route":"Escalate"}""");
        OutcomeOf(after, facts).Should().Be("""{"notify":true,"queue":"urgent","route":"Manual"}""");
    }

    [Fact]
    public void ClearingAConditionWidensWhatARowAccepts()
    {
        var facts = Samples.Intake("Web", "general", true, 3);
        var before = TestFiles.LoadIntake(First);
        var after = LoadFirst(new ClearCondition("urgent-phone", "channel"));

        RowKeyOf(before, facts).Should().Be("fresh-request");
        RowKeyOf(after, facts).Should().Be("urgent-phone");
    }

    [Fact]
    public void MovingARowChangesWhichOverlappingRowWinsUnderTheFirstPolicy()
    {
        var facts = Samples.Intake("Phone", "billing-late", true, 100);

        RowKeyOf(TestFiles.LoadIntake(First), facts).Should().Be("urgent-phone");
        RowKeyOf(LoadFirst(new MoveRow("billing-topic", null)), facts).Should().Be("billing-topic");
    }

    [Fact]
    public void ChangingTheDefaultOutcomeChangesTheFallbackDecision()
    {
        var facts = Samples.Intake("Web", "general", false, 10, rating: 4m);
        var after = LoadFirst(new SetDefaultOutcome("queue", DecisionValue.Code("elsewhere")));

        OutcomeOf(TestFiles.LoadIntake(First), facts).Should().Be("""{"queue":"general","route":"Standard"}""");
        OutcomeOf(after, facts).Should().Be("""{"queue":"elsewhere","route":"Standard"}""");
    }

    // ---- preservation ----------------------------------------------------------------------------------

    private static string Keys(JsonObject node) => string.Join(",", node.Select(entry => entry.Key));

    [Fact]
    public void AnEmptyEditListChangesOnlyTheVersion()
    {
        var draft = Parse(Draft(FirstText));
        var expected = Parse(FirstText);
        expected["version"] = 2;

        JsonNode.DeepEquals(draft, expected).Should().BeTrue();
        draft.ToJsonString().Should().Be(expected.ToJsonString());
        Keys(draft).Should().Be(Keys(expected));
    }

    [Fact]
    public void AnEditOnOneRowLeavesEveryOtherRowMetaUsesRequiresAndUnknownPropertyIdentical()
    {
        var baseDocument = Parse(FirstText);
        baseDocument["$v0"] = Parse("""{"note":"kept","n":[1,2,{"a":null}]}""");
        baseDocument["extra"] = "unknown";
        RowNode(baseDocument, "billing-topic")["x-note"] = "row note";
        baseDocument["body"]!["goldens"]![0]!["x-case"] = 1;
        var text = baseDocument.ToJsonString();

        var draft = Parse(Draft(text, new SetOutcome("urgent-phone", "route", Member("Manual"))));

        foreach (var member in new[] { "$spec", "id", "uses", "requires", "meta", "$v0", "extra" })
        {
            JsonNode.DeepEquals(draft[member], baseDocument[member]).Should().BeTrue($"{member} is kept");
        }

        foreach (var key in new[] { "billing-topic", "email-invoice", "fresh-request", "high-rating", "mid-amount", "old-request", "no-account" })
        {
            RowNode(draft, key).ToJsonString().Should().Be(RowNode(baseDocument, key).ToJsonString(), $"{key} is untouched");
        }

        JsonNode.DeepEquals(draft["body"]!["goldens"], baseDocument["body"]!["goldens"]).Should().BeTrue();
        JsonNode.DeepEquals(draft["body"]!["defaultRow"], baseDocument["body"]!["defaultRow"]).Should().BeTrue();
        Keys(draft).Should().Be(Keys(baseDocument));
        Keys(RowNode(draft, "urgent-phone")).Should().Be(Keys(RowNode(baseDocument, "urgent-phone")));
        Keys(RowNode(draft, "urgent-phone")["outcome"]!.AsObject()).Should().Be("route,queue,notify,priority");
    }

    [Fact]
    public void NumbersAndNonAsciiTextAreKeptAsWritten()
    {
        var document = Parse(FirstText);
        document["meta"]!["n"] = JsonNode.Parse("1.50");
        document["meta"]!["big"] = JsonNode.Parse("12345678901234567890");
        document["meta"]!["exp"] = JsonNode.Parse("1e3");
        var text = document.ToJsonString();

        var draft = Draft(text, new SetOutcome("urgent-phone", "route", Member("Manual")));

        draft.Should().Contain("\"n\": 1.50").And.Contain("12345678901234567890").And.Contain("1e3");
        draft.Should().Contain("Antigüedad en días").And.NotContain("\\u00");
    }

    [Fact]
    public void TheDraftIsIndentedWithTwoSpacesAndUsesNewLineOnly()
    {
        var draft = Draft(FirstText.Replace("\n", "\r\n", StringComparison.Ordinal));

        draft.Should().NotContain("\r");
        draft.Should().StartWith("{\n  \"$spec\": \"brain.rule@1\",\n  \"id\": \"intake-routing\",\n  \"version\": 2,\n");
        draft.EndsWith('\n').Should().BeFalse();
        draft.Should().NotContain("\t");
    }

    // ---- version rule --------------------------------------------------------------------------------------

    private static long RowVersion(string draft, string key) => (long)RowNode(Parse(draft), key)["version"]!;

    [Fact]
    public void ATouchedRowIsBumpedOnceHoweverManyEditsTouchIt()
    {
        var draft = Draft(
            FirstText,
            new SetOutcome("urgent-phone", "route", Member("Manual")),
            new SetCondition("urgent-phone", Condition.IsNull("rating")),
            new ClearCondition("urgent-phone", "rating"),
            new SetOutcome("billing-topic", "queue", DecisionValue.Code("q")));

        Parse(draft)["version"]!.GetValue<long>().Should().Be(2);
        RowVersion(draft, "urgent-phone").Should().Be(2);
        RowVersion(draft, "billing-topic").Should().Be(2);
        RowVersion(draft, "email-invoice").Should().Be(1);
    }

    [Fact]
    public void ANewRowStartsAtVersionOneEvenWhenLaterEditsTouchIt()
    {
        var draft = Draft(
            FirstText,
            new AddRow("fresh", null, [], Outcome(("route", Member("Fast")), ("queue", DecisionValue.Code("q")))),
            new SetOutcome("fresh", "queue", DecisionValue.Code("r")));

        RowVersion(draft, "fresh").Should().Be(1);
    }

    [Fact]
    public void AMovedRowKeepsItsVersionAndAFailedRunWritesNothing()
    {
        var draft = Draft(FirstText, new MoveRow("no-account", null), new SetRowLabel("no-account", WorkbenchLocale.En, "x"));

        RowVersion(draft, "no-account").Should().Be(1);
    }

    [Fact]
    public void TheDefaultRowIsBumpedOnceAndADocumentVersionOfAnyBaseIsBumpedByOne()
    {
        var draft = Draft(FirstText, new SetDefaultOutcome("queue", DecisionValue.Code("a")), new SetDefaultOutcome("route", Member("Fast")));

        Parse(draft)["body"]!["defaultRow"]!["version"]!.GetValue<long>().Should().Be(2);
        var readiness = Draft(TestFiles.Read("tests/fixtures/brain-spec/readiness.rule.json"));
        Parse(readiness)["version"]!.GetValue<long>().Should().Be(4);
    }

    [Fact]
    public void ARemovedRowIsNotBumped()
    {
        var draft = Draft(FirstText, new SetOutcome("urgent-phone", "route", Member("Manual")), new RemoveRow("urgent-phone"));

        Parse(draft)["version"]!.GetValue<long>().Should().Be(2);
    }

    // ---- edit order and determinism -------------------------------------------------------------------------

    [Fact]
    public void LaterEditsSeeTheEffectOfEarlierOnes()
    {
        var rule = LoadFirst(
            new AddRow("extra", "urgent-phone", [], Outcome(("route", Member("Fast")), ("queue", DecisionValue.Code("q")))),
            new SetCondition("extra", Condition.Equals("channel", Member("Web"))),
            new MoveRow("extra", null),
            new RemoveCase("Anything else uses the fallback"),
            new AddCase(new ProposedCase("Anything else uses the fallback", Samples.Intake("Web", "x", false, 10, rating: 4m), false, """{"route":"Standard","queue":"general"}""", "fallback")));

        rule.Table.Rows[0].Key.Should().Be("extra");
        ConditionOn(rule.Table.Rows[0], "channel").Value.Should().Be(Member("Web"));
        rule.Goldens[^1].Name.Should().Be("Anything else uses the fallback");
    }

    [Fact]
    public void ARunStopsAtTheFirstEditThatCannotBeAppliedAndNamesItsPosition()
    {
        var result = Refused(
            FirstText,
            new SetOutcome("urgent-phone", "route", Member("Manual")),
            new RemoveRow("urgent-phone"),
            new SetOutcome("urgent-phone", "route", Member("Fast")),
            new RemoveRow("never-reached"));

        result.Errors[0].Code.Should().Be("workbench.draft.rowNotFound");
        result.Errors[0].Path.Should().Be("/edits/2");
        result.Errors[0].Message.Should().Contain("urgent-phone");
    }

    [Fact]
    public void TheSameInputGivesTheSameText()
    {
        RuleEdit[] edits =
        [
            new SetCondition("billing-topic", Condition.Range("ageDays", DecisionValue.Integer(1), DecisionValue.Integer(2), true, true)),
            new AddCase(new ProposedCase("c", Samples.Intake("Web", "x", false, 1), true, null, null)),
            new SetRowLabel("billing-topic", WorkbenchLocale.En, "B"),
        ];

        Draft(FirstText, edits).Should().Be(Draft(FirstText, edits));
    }

    [Fact]
    public void ADraftCanBeDraftedAgainFromItself()
    {
        var once = Draft(FirstText, new SetOutcome("urgent-phone", "route", Member("Manual")));
        var twice = Draft(once, new SetOutcome("urgent-phone", "route", Member("Fast")));

        Parse(twice)["version"]!.GetValue<long>().Should().Be(3);
        RowVersion(twice, "urgent-phone").Should().Be(3);
        TestFiles.LoadIntakeText(twice).Table.Rows[0].Outcome.Should().Contain("\"Fast\"");
    }

    // ---- error codes ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"$spec\":\"brain.process@1\",\"version\":1,\"body\":{\"rows\":[]}}")]
    [InlineData("{\"version\":1,\"body\":{\"rows\":[]}}")]
    [InlineData("{\"$spec\":\"brain.rule@1\",\"version\":1}")]
    [InlineData("{\"$spec\":\"brain.rule@1\",\"version\":1,\"body\":{\"rows\":{}}}")]
    [InlineData("{\"$spec\":\"brain.rule@1\",\"body\":{\"rows\":[]}}")]
    [InlineData("{\"$spec\":\"brain.rule@1\",\"version\":\"1\",\"body\":{\"rows\":[]}}")]
    [InlineData("{\"$spec\":\"brain.rule@1\",\"version\":1.5,\"body\":{\"rows\":[]}}")]
    public void ABaseThatIsNotARuleDocumentIsRefusedWithOneError(string text)
    {
        var result = Refused(text, new RemoveRow("x"));

        result.Errors[0].Should().Be(new WorkbenchError("workbench.draft.baseNotReadable", string.Empty, result.Errors[0].Message));
    }

    [Fact]
    public void ABaseIsRefusedEvenWithNoEdits() =>
        RuleDrafter.Apply("nope", []).Errors.Single().Code.Should().Be("workbench.draft.baseNotReadable");

    [Fact]
    public void ARowTheTableDoesNotHaveIsRefusedByEveryRowEdit()
    {
        RuleEdit[] edits =
        [
            new SetCondition("nope", Condition.Any("channel")),
            new ClearCondition("nope", "channel"),
            new SetOutcome("nope", "route", null),
            new RemoveRow("nope"),
            new MoveRow("nope", null),
            new SetRowLabel("nope", WorkbenchLocale.En, "x"),
            new SetCaseExpectation("An old request goes to the backlog", "nope", Outcome()),
            new AddRow("fresh", "nope", [], Outcome()),
            new MoveRow("urgent-phone", "nope"),
        ];

        foreach (var edit in edits)
        {
            Refused(FirstText, edit).Errors[0].Code.Should().Be("workbench.draft.rowNotFound", edit.ToString());
        }
    }

    [Fact]
    public void TheDefaultRowIsNotARowOfTheTableForRowEdits() =>
        Refused(FirstText, new SetOutcome("fallback", "queue", null)).Errors[0].Code.Should().Be("workbench.draft.rowNotFound");

    [Fact]
    public void AddRowRefusesAKeyThatIsInUseByAKeyAnIdOrTheDefaultRow()
    {
        Refused(FirstText, new AddRow("urgent-phone", null, [], Outcome())).Errors[0].Code.Should().Be("workbench.draft.rowKeyTaken");
        Refused(FirstText, new AddRow("fallback", null, [], Outcome())).Errors[0].Code.Should().Be("workbench.draft.rowKeyTaken");
        Refused(FirstText, new AddRow("a", null, [], Outcome()), new AddRow("a", null, [], Outcome())).Errors[0].Path.Should().Be("/edits/1");

        var document = Parse(FirstText);
        RowNode(document, "billing-topic")["id"] = "other-id";
        Refused(document.ToJsonString(), new AddRow("other-id", null, [], Outcome())).Errors[0].Code.Should().Be("workbench.draft.rowKeyTaken");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has space")]
    [InlineData("-leading")]
    [InlineData(".dot")]
    [InlineData("slash/key")]
    [InlineData("tab\t")]
    [InlineData("trailing\n")]
    [InlineData("café")]
    public void AddRowRefusesAKeyThatIsNotAValidSectionId(string key) =>
        Refused(FirstText, new AddRow(key, null, [], Outcome())).Errors[0].Code.Should().Be("workbench.draft.rowKeyNotValid");

    [Theory]
    [InlineData("a")]
    [InlineData("A_1")]
    [InlineData("_x")]
    [InlineData("x.y-z")]
    public void AddRowAcceptsAValidSectionId(string key) =>
        Draft(FirstText, new AddRow(key, null, [], Outcome())).Should().Contain($"\"legacyKey\": \"{key}\"");

    [Fact]
    public void ClearConditionRefusesAFactTheRowHasNoConditionOn()
    {
        var result = Refused(FirstText, new ClearCondition("urgent-phone", "ageDays"));

        result.Errors[0].Code.Should().Be("workbench.draft.conditionNotFound");
        result.Errors[0].Message.Should().Contain("urgent-phone").And.Contain("ageDays");
    }

    [Fact]
    public void SetDefaultOutcomeRefusesARuleWithNoDefaultRow()
    {
        var withoutDefault = TestFiles.Read("tests/fixtures/brain-spec/readiness.rule.json");
        Refused(withoutDefault, new SetDefaultOutcome("note", null)).Errors[0].Code.Should().Be("workbench.draft.noDefaultRow");

        var nullDefault = Parse(FirstText);
        nullDefault["body"]!["defaultRow"] = null;
        Refused(nullDefault.ToJsonString(), new SetDefaultOutcome("queue", null)).Errors[0].Code.Should().Be("workbench.draft.noDefaultRow");
    }

    [Fact]
    public void ACaseThatDoesNotExistIsRefusedByCaseEdits()
    {
        Refused(FirstText, new SetCaseExpectation("nope", null, Outcome())).Errors[0].Code.Should().Be("workbench.draft.caseNotFound");
        Refused(FirstText, new RemoveCase("nope")).Errors[0].Code.Should().Be("workbench.draft.caseNotFound");

        var noGoldens = Parse(FirstText);
        noGoldens["body"]!.AsObject().Remove("goldens");
        Refused(noGoldens.ToJsonString(), new RemoveCase("x")).Errors[0].Code.Should().Be("workbench.draft.caseNotFound");
    }

    [Fact]
    public void AddCaseRefusesANameInUseAndCreatesTheCaseListWhenTheDocumentHasNone()
    {
        var proposed = new ProposedCase("An old request goes to the backlog", Samples.Intake("Web", "x", false, 1), true, null, null);
        Refused(FirstText, new AddCase(proposed)).Errors[0].Code.Should().Be("workbench.draft.caseNameTaken");

        var noGoldens = Parse(FirstText);
        noGoldens["body"]!.AsObject().Remove("goldens");
        var draft = Draft(noGoldens.ToJsonString(), new AddCase(proposed));
        Parse(draft)["body"]!["goldens"]!.AsArray().Should().ContainSingle();
    }

    [Fact]
    public void AddCaseRefusesADecidedCaseWhoseOutcomeIsNotAJsonObjectOrList()
    {
        var facts = Samples.Intake("Web", "x", false, 1);
        foreach (var outcome in new string?[] { null, "not json", "5", "null", "\"text\"" })
        {
            Refused(FirstText, new AddCase(new ProposedCase("c", facts, false, outcome, null))).Errors[0].Code.Should().Be("workbench.draft.caseNotValid", outcome);
        }
    }

    [Fact]
    public void ARowMoveAfterItselfIsRefusedBeforeTheAnchorIsLookedUp() =>
        Refused(FirstText, new MoveRow("urgent-phone", "urgent-phone")).Errors[0].Code.Should().Be("workbench.draft.moveOntoItself");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void ALabelThatIsEmptyOrWhiteSpaceIsRefused(string text) =>
        Refused(FirstText, new SetRowLabel("urgent-phone", WorkbenchLocale.En, text)).Errors[0].Code.Should().Be("workbench.draft.labelNotValid");

    [Fact]
    public void ADocumentWhoseDisplayLabelsAreNotObjectsIsReportedAsNotReadable()
    {
        foreach (var broken in new[] { "meta", "display", "en", "rows" })
        {
            var document = Parse(FirstText);
            var meta = document["meta"]!.AsObject();
            switch (broken)
            {
                case "meta": document["meta"] = "text"; break;
                case "display": meta["display"] = 3; break;
                case "en": meta["display"]!["en"] = new JsonArray(); break;
                default: meta["display"]!["en"]!["rows"] = "x"; break;
            }

            Refused(document.ToJsonString(), new SetRowLabel("urgent-phone", WorkbenchLocale.En, "x")).Errors[0].Code
                .Should().Be("workbench.draft.baseNotReadable", broken);
        }
    }

    [Fact]
    public void ARowWhoseMembersAreNotReadableIsReportedAndNothingIsWritten()
    {
        var conditions = Parse(FirstText);
        RowNode(conditions, "urgent-phone")["conditions"] = "x";
        var outcome = Parse(FirstText);
        RowNode(outcome, "urgent-phone")["outcome"] = 5;
        var version = Parse(FirstText);
        RowNode(version, "urgent-phone")["version"] = "x";
        var defaultOutcome = Parse(FirstText);
        defaultOutcome["body"]!["defaultRow"]!["outcome"] = 5;

        Refused(conditions.ToJsonString(), new SetCondition("urgent-phone", Condition.Any("a"))).Errors[0].Path.Should().Be("/body/rows/0/conditions");
        Refused(conditions.ToJsonString(), new ClearCondition("urgent-phone", "a")).Errors[0].Code.Should().Be("workbench.draft.baseNotReadable");
        Refused(outcome.ToJsonString(), new SetOutcome("urgent-phone", "route", null)).Errors[0].Path.Should().Be("/body/rows/0/outcome");
        Refused(version.ToJsonString(), new SetOutcome("urgent-phone", "route", null)).Errors[0].Path.Should().Be("/body/rows/0/version");
        Refused(defaultOutcome.ToJsonString(), new SetDefaultOutcome("queue", null)).Errors[0].Path.Should().Be("/body/defaultRow/outcome");
        var defaultVersion = Parse(FirstText);
        defaultVersion["body"]!["defaultRow"]!["version"] = "x";
        Refused(defaultVersion.ToJsonString(), new SetDefaultOutcome("queue", null)).Errors[0].Path.Should().Be("/body/defaultRow/version");
    }

    [Fact]
    public void ARowWithNoConditionsMemberGetsOneWhenAConditionIsSet()
    {
        var document = Parse(FirstText);
        RowNode(document, "fresh-request").Remove("conditions");

        var rule = Load(document.ToJsonString(), new SetCondition("fresh-request", Condition.IsNull("rating")));

        RowOf(rule, "fresh-request").Conditions.Should().ContainSingle();
    }

    [Fact]
    public void ARowsWithoutAnIdOrKeyIsSkippedByLookups()
    {
        var document = Parse(FirstText);
        document["body"]!["rows"]!.AsArray().Insert(0, "not an object");
        RowNode(document, "billing-topic").Remove("id");

        Refused(document.ToJsonString(), new SetOutcome("billing-topic", "queue", null)).Errors[0].Code.Should().Be("workbench.draft.rowNotFound");
        Draft(document.ToJsonString(), new SetOutcome("urgent-phone", "queue", DecisionValue.Code("z"))).Should().Contain("\"z\"");
    }

    // ---- guards -------------------------------------------------------------------------------------------

    [Fact]
    public void NullArgumentsAndNullEditsAreRefused()
    {
        var facts = Samples.Intake("Web", "x", false, 1);
        Action[] calls =
        [
            () => RuleDrafter.Apply(null!, []),
            () => RuleDrafter.Apply(FirstText, null!),
            () => RuleDrafter.Apply(FirstText, [null!]),
            () => RuleDrafter.Apply(FirstText, [new SetCondition(null!, Condition.Any("a"))]),
            () => RuleDrafter.Apply(FirstText, [new SetCondition("a", null!)]),
            () => RuleDrafter.Apply(FirstText, [new ClearCondition(null!, "a")]),
            () => RuleDrafter.Apply(FirstText, [new ClearCondition("a", null!)]),
            () => RuleDrafter.Apply(FirstText, [new SetOutcome(null!, "a", null)]),
            () => RuleDrafter.Apply(FirstText, [new SetOutcome("a", null!, null)]),
            () => RuleDrafter.Apply(FirstText, [new AddRow(null!, null, [], Outcome())]),
            () => RuleDrafter.Apply(FirstText, [new AddRow("a", null, null!, Outcome())]),
            () => RuleDrafter.Apply(FirstText, [new AddRow("a", null, [], null!)]),
            () => RuleDrafter.Apply(FirstText, [new RemoveRow(null!)]),
            () => RuleDrafter.Apply(FirstText, [new MoveRow(null!, null)]),
            () => RuleDrafter.Apply(FirstText, [new SetDefaultOutcome(null!, null)]),
            () => RuleDrafter.Apply(FirstText, [new SetCaseExpectation(null!, null, Outcome())]),
            () => RuleDrafter.Apply(FirstText, [new SetCaseExpectation("a", null, null!)]),
            () => RuleDrafter.Apply(FirstText, [new AddCase(null!)]),
            () => RuleDrafter.Apply(FirstText, [new AddCase(new ProposedCase(null!, facts, true, null, null))]),
            () => RuleDrafter.Apply(FirstText, [new AddCase(new ProposedCase("a", null!, true, null, null))]),
            () => RuleDrafter.Apply(FirstText, [new RemoveCase(null!)]),
            () => RuleDrafter.Apply(FirstText, [new SetRowLabel(null!, WorkbenchLocale.En, "x")]),
        ];

        foreach (var call in calls)
        {
            call.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    public void AnUnknownOperatorOrValueTypeIsAProgrammingErrorNotAContentError()
    {
        var badOperator = new Condition { FactName = "channel", Operator = (Operator)99 };

        Action op = () => RuleDrafter.Apply(FirstText, [new SetCondition("urgent-phone", badOperator)]);
        Action value = () => RuleDrafter.Apply(FirstText, [new SetOutcome("urgent-phone", "x", new UnknownValue())]);
        Action tagged = () => RuleDrafter.Apply(FirstText, [new SetCondition("urgent-phone", Condition.Equals("channel", new UnknownValue()))]);

        op.Should().Throw<ArgumentOutOfRangeException>();
        value.Should().Throw<ArgumentOutOfRangeException>();
        tagged.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed record UnknownValue : DecisionValue;
}

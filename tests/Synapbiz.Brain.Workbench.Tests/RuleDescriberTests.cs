using System.Text.Json.Nodes;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class RuleDescriberTests
{
    private static RowView ReadinessRow(string key, WorkbenchLocale locale) =>
        RuleDescriber.Describe(TestFiles.LoadReadiness(), locale).Rows.Single(row => row.Key == key);

    [Fact]
    public void ALowScoreIsDescribedInEnglishAsThePinnedSentence() =>
        ReadinessRow("low-score", WorkbenchLocale.En).Sentence.Should().Be("If score is between 0 and 49, then note is low-score.");

    [Fact]
    public void ALowScoreIsDescribedInSpanishAsThePinnedSentence() =>
        ReadinessRow("low-score", WorkbenchLocale.Es).Sentence.Should().Be("Si score está entre 0 y 49, entonces note es low-score.");

    [Fact]
    public void AnEnumSetIsListedWithItsMembers() =>
        ReadinessRow("late-stage", WorkbenchLocale.En).Sentence.Should().Be("If stage is one of Review, Complete, then note is late-stage.");

    [Fact]
    public void TheReadinessViewCarriesTheRuleFactsAndRowsInOrder()
    {
        var view = RuleDescriber.Describe(TestFiles.LoadReadiness(), WorkbenchLocale.En);

        view.Id.Should().Be("readiness-notes");
        view.Version.Should().Be(3);
        view.Title.Should().Be("readiness-notes");
        view.Kind.Should().Be("synthetic.readiness");
        view.HitPolicy.Should().Be(HitPolicy.Collect);
        view.Facts.Select(fact => fact.Name).Should().Equal("stage", "score");
        view.Facts[0].AllowedValues.Select(option => (option.Value, option.Label)).Should().Equal(("Intake", "Intake"), ("Review", "Review"), ("Complete", "Complete"));
        view.Facts[1].AllowedValues.Should().BeEmpty();
        view.OutcomeFields.Select(field => (field.Name, field.Label, field.Type, field.Nullable)).Should().Equal(("note", "note", FactType.Code, false));
        view.Rows.Select(row => (row.Key, row.RowId, row.Index, row.IsDefault)).Should().Equal(
            ("low-score", "low-score", 0, false), ("mid-score", "mid-score", 1, false), ("late-stage", "late-stage", 2, false));
        view.DefaultRow.Should().BeNull();
    }

    private static readonly string[] EnglishSentences =
    [
        "If urgent is yes and channel is Phone, then route is Escalate, queue is urgent, notify is yes, priority is 1.",
        "If topic starts with bill and channel is not Email, then route is Fast, queue is billing, notify is no, priority is 2.",
        "If topic is one of invoice, refund and channel is one of Email, Web, then route is Standard, queue is billing-email, priority is 2.",
        "If age days is at least 0 and less than 7 and rating is empty, then route is Fast, queue is new.",
        "If rating is greater than 3 and at most 5 and received on is not empty, then route is Standard, queue is followup, notify is yes.",
        "If amount is greater than 100 USD and less than 500 USD and age days is between 7 and 60, then route is Manual, queue is review, priority is 4.",
        "If age days is at least 61, then route is Manual, queue is backlog, priority is 5.",
        "If account is empty and channel is none of Phone, Email and age days is less than 365 and received on is at most 06/30/2026, then route is Manual, queue is unassigned, notify is no.",
    ];

    private static readonly string[] SpanishSentences =
    [
        "Si urgent es sí y channel es Phone, entonces route es Escalate, queue es urgent, notify es sí, priority es 1.",
        "Si topic empieza por bill y channel no es Email, entonces route es Fast, queue es billing, notify es no, priority es 2.",
        "Si topic es uno de invoice, refund y channel es uno de Email, Web, entonces route es Standard, queue es billing-email, priority es 2.",
        "Si age days es como mínimo 0 y menor que 7 y rating está vacío, entonces route es Fast, queue es new.",
        "Si rating es mayor que 3 y como máximo 5 y received on no está vacío, entonces route es Standard, queue es followup, notify es sí.",
        "Si amount es mayor que 100 USD y menor que 500 USD y age days está entre 7 y 60, entonces route es Manual, queue es review, priority es 4.",
        "Si age days es como mínimo 61, entonces route es Manual, queue es backlog, priority es 5.",
        "Si account está vacío y channel no es ninguno de Phone, Email y age days es menor que 365 y received on es como máximo 30/06/2026, entonces route es Manual, queue es unassigned, notify es no.",
    ];

    [Fact]
    public void EveryRowOfTheIntakeRuleHasItsExactEnglishSentence()
    {
        var view = RuleDescriber.Describe(TestFiles.LoadIntake("intake.first.rule.json"), WorkbenchLocale.En);

        view.Rows.Select(row => row.Sentence).Should().Equal(EnglishSentences);
        view.DefaultRow!.Sentence.Should().Be("Otherwise, route is Standard, queue is general.");
    }

    [Fact]
    public void EveryRowOfTheIntakeRuleHasItsExactSpanishSentence()
    {
        var view = RuleDescriber.Describe(TestFiles.LoadIntake("intake.first.rule.json"), WorkbenchLocale.Es);

        view.Rows.Select(row => row.Sentence).Should().Equal(SpanishSentences);
        view.DefaultRow!.Sentence.Should().Be("En cualquier otro caso, route es Standard, queue es general.");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "received on is at most 30 Jun 2026")]
    [InlineData(WorkbenchLocale.Es, "received on es como máximo 30 Jun 2026")]
    public void AHostDateFormatterChangesTheSentenceWithADateRangeAndLeavesLabelledEnumValuesAlone(WorkbenchLocale locale, string expectedPart)
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var labels = RuleLabels.FromMeta(rule.Meta, locale) with
        {
            ValueFormatter = value => value is DateValue date
                ? date.Value.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : null,
        };

        var view = RuleDescriber.Describe(rule, locale, labels);

        var dated = view.Rows[^1];
        dated.Sentence.Should().Contain(expectedPart);
        dated.Conditions.Last(condition => condition.FactName == "receivedOn").Text.Should().EndWith("30 Jun 2026");
        view.Rows[0].Sentence.Should().Contain(locale == WorkbenchLocale.En ? "the phone line" : "la línea telefónica");
        view.Rows[0].Sentence.Should().Contain(locale == WorkbenchLocale.En ? "urgent is yes" : "urgent es sí");
    }

    [Fact]
    public void AFormatterThatReturnsNullLeavesTheSentencesAsTheyWere()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");

        var view = RuleDescriber.Describe(rule, WorkbenchLocale.En, new RuleLabels { ValueFormatter = _ => null });

        view.Rows.Select(row => row.Sentence).Should().Equal(EnglishSentences);
    }

    [Fact]
    public void TheDefaultRowFollowsTheLastRowAndIsMarkedAsDefault()
    {
        var view = RuleDescriber.Describe(TestFiles.LoadIntake("intake.first.rule.json"), WorkbenchLocale.En);

        view.DefaultRow!.IsDefault.Should().BeTrue();
        view.DefaultRow.Index.Should().Be(view.Rows.Count);
        view.DefaultRow.Key.Should().Be("fallback");
        view.DefaultRow.Conditions.Should().BeEmpty();
        view.Rows.Should().OnlyContain(row => !row.IsDefault);
    }

    [Fact]
    public void ConditionsKeepTheirOrderIndexOperatorAndText()
    {
        var row = RuleDescriber.Describe(TestFiles.LoadIntake("intake.first.rule.json"), WorkbenchLocale.En).Rows[0];

        row.Conditions.Select(condition => (condition.Index, condition.FactName, condition.FactLabel, condition.Operator, condition.Text)).Should().Equal(
            (0, "urgent", "urgent", Operator.Equals, "urgent is yes"),
            (1, "channel", "channel", Operator.Equals, "channel is Phone"),
            (2, "topic", "topic", Operator.Any, "topic can be anything"));
    }

    [Fact]
    public void AnAnyConditionIsLeftOutOfTheSentenceButStaysInTheConditionList()
    {
        var row = RuleDescriber.Describe(TestFiles.LoadIntake("intake.first.rule.json"), WorkbenchLocale.Es).Rows[0];

        row.Conditions[2].Text.Should().Be("topic puede ser cualquier valor");
        row.Sentence.Should().NotContain("cualquier valor");
    }

    [Fact]
    public void LabelsFromTheRuleAreApplied()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var labels = RuleLabels.FromMeta(rule.Meta, WorkbenchLocale.En);

        var view = RuleDescriber.Describe(rule, WorkbenchLocale.En, labels);

        view.Title.Should().Be("Intake routing");
        view.Facts.Single(fact => fact.Name == "channel").Label.Should().Be("Contact channel");
        view.Facts.Single(fact => fact.Name == "channel").AllowedValues.Select(option => (option.Value, option.Label)).Should().Equal(
            ("Web", "the website"), ("Phone", "the phone line"), ("Email", "email"));
        view.Facts.Single(fact => fact.Name == "topic").Label.Should().Be("topic");
        view.OutcomeFields.Single(field => field.Name == "route").Label.Should().Be("Route");
        view.Rows[0].Title.Should().Be("Urgent calls");
        view.Rows[1].Title.Should().BeNull();
        view.Rows[0].Sentence.Should().Be(
            "If urgent is yes and Contact channel is the phone line, then Route is escalation, queue is urgent, notify is yes, priority is 1.");
        view.Rows[0].Outcome[0].Should().Be(new OutcomeValueView("route", "Route", "Escalate", "escalation"));
        view.Rows[5].Sentence.Should().Contain("Age in days is between 7 and 60");
    }

    [Fact]
    public void SpanishLabelsFromTheRuleAreApplied()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var labels = RuleLabels.FromMeta(rule.Meta, WorkbenchLocale.Es);

        var view = RuleDescriber.Describe(rule, WorkbenchLocale.Es, labels);

        view.Title.Should().Be("Enrutamiento de solicitudes");
        view.Rows[0].Title.Should().Be("Llamadas urgentes");
        view.Rows[0].Sentence.Should().Be(
            "Si urgent es sí y Canal de contacto es la línea telefónica, entonces Ruta es escalado, queue es urgent, notify es sí, priority es 1.");
    }

    [Fact]
    public void OutcomeValuesFollowTheKindOrderAndRawValuesAreKept()
    {
        var row = RuleDescriber.Describe(TestFiles.LoadIntake("intake.first.rule.json"), WorkbenchLocale.En).Rows[0];

        row.Outcome.Should().Equal(
            new OutcomeValueView("route", "route", "Escalate", "Escalate"),
            new OutcomeValueView("queue", "queue", "urgent", "urgent"),
            new OutcomeValueView("notify", "notify", "true", "yes"),
            new OutcomeValueView("priority", "priority", "1", "1"));
    }

    [Fact]
    public void ARowThatLeavesAnOptionalFieldOutDoesNotListIt()
    {
        var row = RuleDescriber.Describe(TestFiles.LoadIntake("intake.first.rule.json"), WorkbenchLocale.En).Rows[3];

        row.Outcome.Select(value => value.Field).Should().Equal("route", "queue");
    }

    [Fact]
    public void UnknownOutcomeMembersFollowTheDeclaredOnesAndNullsAreSkipped()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var outcome = """{"zeta":"z","priority":null,"route":"Fast","queue":"q","extra":{"amount":"5.5","currency":"USD"},"flags":[1,2],"n":3,"ok":true,"no":false,"obj":{"a":1}}""";
        var table = new DecisionTable(HitPolicy.First, [Samples.RowOf("r", outcome)], null);

        var row = RuleDescriber.Describe(Samples.WithTable(rule, table), WorkbenchLocale.En).Rows.Single();

        row.Outcome.Select(value => (value.Field, value.RawValue, value.Text)).Should().Equal(
            ("route", "Fast", "Fast"),
            ("queue", "q", "q"),
            ("zeta", "z", "z"),
            ("extra", """{"amount":"5.5","currency":"USD"}""", "5.5 USD"),
            ("flags", "[1,2]", "[1,2]"),
            ("n", "3", "3"),
            ("ok", "true", "yes"),
            ("no", "false", "no"),
            ("obj", """{"a":1}""", """{"a":1}"""));
        row.Sentence.Should().StartWith("In every case, route is Fast, queue is q, zeta is z, extra is 5.5 USD,");
    }

    [Fact]
    public void ARowWithNoOutcomeValuesSaysSo()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var table = new DecisionTable(HitPolicy.First, [Samples.RowOf("empty", "{}"), Samples.RowOf("nulls", """{"priority":null}""")], Samples.RowOf("nothing", "[]"));

        var view = RuleDescriber.Describe(Samples.WithTable(rule, table), WorkbenchLocale.En);

        view.Rows.Select(row => row.Sentence).Should().Equal(
            "In every case, no outcome fields are set.", "In every case, no outcome fields are set.");
        view.DefaultRow!.Sentence.Should().Be("Otherwise, no outcome fields are set.");
        view.DefaultRow.Outcome.Should().BeEmpty();
    }

    [Fact]
    public void AnEnumOutcomeIsHumanisedAndACodeOutcomeIsNot()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var table = new DecisionTable(HitPolicy.First, [Samples.RowOf("r", """{"route":"Fast","queue":"to_confirm"}""")], null);

        var row = RuleDescriber.Describe(Samples.WithTable(rule, table), WorkbenchLocale.En).Rows.Single();

        row.Outcome.Select(value => value.Text).Should().Equal("Fast", "to_confirm");
        RuleDescriber.Describe(Samples.WithTable(rule, new DecisionTable(HitPolicy.First, [Samples.RowOf("r", """{"route":"to_confirm"}""")], null)), WorkbenchLocale.En)
            .Rows.Single().Outcome[0].Text.Should().Be("to confirm");
    }

    [Fact]
    public void ARangeWithNoBoundAndAnAnyConditionAreLeftOutOfTheSentence()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var table = new DecisionTable(
            HitPolicy.First,
            [
                Samples.RowOf("open", """{"route":"Fast"}""", Condition.Range("ageDays", null, null, true, true), Condition.Any("topic")),
                Samples.RowOf("mixed", """{"route":"Fast"}""", Condition.Range("ageDays", null, null, true, true), Condition.Equals("urgent", DecisionValue.Boolean(false))),
            ],
            null);

        var view = RuleDescriber.Describe(Samples.WithTable(rule, table), WorkbenchLocale.En);

        view.Rows[0].Conditions.Select(condition => condition.Text).Should().Equal("age days can be anything", "topic can be anything");
        view.Rows[0].Sentence.Should().Be("In every case, route is Fast.");
        view.Rows[1].Sentence.Should().Be("If urgent is no, then route is Fast.");
    }

    [Fact]
    public void AMissingRowIdFallsBackToTheRowKey()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var table = new DecisionTable(HitPolicy.First, [Samples.RowOf("key-only", """{"route":"Fast"}""")], null);

        var row = RuleDescriber.Describe(Samples.WithTable(rule, table), WorkbenchLocale.En, new RuleLabels { Rows = new Dictionary<string, string> { ["key-only"] = "Titled" } }).Rows.Single();

        row.RowId.Should().Be("key-only");
        row.Title.Should().Be("Titled");
    }

    [Fact]
    public void EveryOperatorHasATextInBothLocales()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var conditions = new[]
        {
            Condition.Equals("topic", DecisionValue.Code("a")),
            Condition.NotEquals("topic", DecisionValue.Code("a")),
            Condition.In("topic", [DecisionValue.Code("a"), DecisionValue.Code("b")]),
            Condition.NotIn("topic", [DecisionValue.Code("a"), DecisionValue.Code("b")]),
            Condition.IsNull("rating"),
            Condition.IsNotNull("rating"),
            Condition.StartsWith("topic", "a"),
            Condition.Range("ageDays", DecisionValue.Integer(1), DecisionValue.Integer(2), true, true),
            Condition.Range("ageDays", DecisionValue.Integer(1), null, false, true),
            Condition.Range("ageDays", null, DecisionValue.Integer(2), true, true),
        };
        var table = new DecisionTable(HitPolicy.First, [Samples.RowOf("all", """{"route":"Fast"}""", conditions)], null);
        var built = Samples.WithTable(rule, table);

        RuleDescriber.Describe(built, WorkbenchLocale.En).Rows[0].Conditions.Select(condition => condition.Text).Should().Equal(
            "topic is a", "topic is not a", "topic is one of a, b", "topic is none of a, b", "rating is empty", "rating is not empty",
            "topic starts with a", "age days is between 1 and 2", "age days is greater than 1", "age days is at most 2");
        RuleDescriber.Describe(built, WorkbenchLocale.Es).Rows[0].Conditions.Select(condition => condition.Text).Should().Equal(
            "topic es a", "topic no es a", "topic es uno de a, b", "topic no es ninguno de a, b", "rating está vacío", "rating no está vacío",
            "topic empieza por a", "age days está entre 1 y 2", "age days es mayor que 1", "age days es como máximo 2");
    }

    [Fact]
    public void AnUnknownOperatorIsRefused()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var condition = new Condition { FactName = "topic", Operator = (Operator)99 };
        var table = new DecisionTable(HitPolicy.First, [Samples.RowOf("bad", """{"route":"Fast"}""", condition)], null);

        var act = () => RuleDescriber.Describe(Samples.WithTable(rule, table), WorkbenchLocale.En);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ANumericOutcomeAndAMoneyOutcomeWithNumbersAreShownAsWritten()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var table = new DecisionTable(HitPolicy.First, [Samples.RowOf("r", """{"cost":{"amount":12.5,"currency":"EUR"}}""")], null);

        var row = RuleDescriber.Describe(Samples.WithTable(rule, table), WorkbenchLocale.Es).Rows.Single();

        row.Outcome.Single().Text.Should().Be("12.5 EUR");
        JsonNode.Parse(row.Outcome.Single().RawValue)!["amount"]!.ToJsonString().Should().Be("12.5");
    }

    [Fact]
    public void ANonObjectOutcomeGivesNoOutcomeValues()
    {
        var rule = TestFiles.LoadIntake("intake.first.rule.json");
        var table = new DecisionTable(HitPolicy.First, [Samples.RowOf("r", "null")], null);

        RuleDescriber.Describe(Samples.WithTable(rule, table), WorkbenchLocale.En).Rows.Single().Outcome.Should().BeEmpty();
    }
}

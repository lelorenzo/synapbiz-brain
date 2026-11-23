using FluentAssertions;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Conditions;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class RuleChoiceCatalogTests
{
    private static RuleChoices Intake(WorkbenchLocale locale, RuleLabels? labels = null)
    {
        var rule = TestFiles.LoadIntake(DraftKit.First);
        return RuleChoiceCatalog.For(rule, labels ?? RuleLabels.FromMeta(rule.Meta, locale), locale);
    }

    [Fact]
    public void EveryFactAndOutcomeFieldOfTheKindIsOfferedInTheDescriptorsOrder()
    {
        var rule = TestFiles.LoadIntake(DraftKit.First);

        var choices = Intake(WorkbenchLocale.En);

        choices.Facts.Select(fact => fact.Fact).Should().Equal(rule.Kind.Facts.Select(fact => fact.Name));
        choices.Facts.Select(fact => fact.Fact).Should().Equal("channel", "topic", "urgent", "ageDays", "rating", "amount", "receivedOn", "account");
        choices.Outcomes.Select(field => field.Field).Should().Equal("route", "queue", "notify", "priority");
        choices.Facts.Select(fact => fact.ValueType).Should().Equal("Enum", "Code", "Boolean", "Integer", "Decimal", "Money", "Date", "Reference");
        choices.Outcomes.Select(field => (field.ValueType, field.Required)).Should().Equal(("Enum", true), ("Code", true), ("Boolean", false), ("Integer", false));
        choices.HasDefaultRow.Should().BeTrue();
        choices.HitPolicy.Should().Be("first");
    }

    [Fact]
    public void AnEnumFactOffersItsMembersWithTheirLabelsAndEveryOtherFactOffersNone()
    {
        var english = Intake(WorkbenchLocale.En);
        var spanish = Intake(WorkbenchLocale.Es);

        english.Facts[0].Values.Select(choice => (choice.Value, choice.Label)).Should().Equal(
            (DecisionValue.Enum("Web"), "the website"), (DecisionValue.Enum("Phone"), "the phone line"), (DecisionValue.Enum("Email"), "email"));
        spanish.Facts[0].Values.Select(choice => choice.Label).Should().Equal("el sitio web", "la línea telefónica", "el correo");
        english.Facts.Skip(1).Should().OnlyContain(fact => fact.Values.Count == 0);
        english.Outcomes.Should().OnlyContain(field => field.Values.Count == 0);
    }

    [Fact]
    public void LabelsComeFromTheLabelsOfTheLocaleWithAHumanisedFallback()
    {
        var english = Intake(WorkbenchLocale.En);
        var spanish = Intake(WorkbenchLocale.Es);

        english.Facts.Select(fact => fact.Label).Should().Equal(
            "Contact channel", "topic", "urgent", "Age in days", "rating", "amount", "received on", "account");
        spanish.Facts[0].Label.Should().Be("Canal de contacto");
        spanish.Facts[3].Label.Should().Be("Antigüedad en días");
        english.Outcomes.Select(field => field.Label).Should().Equal("Route", "queue", "notify", "priority");
        spanish.Outcomes[0].Label.Should().Be("Ruta");
    }

    [Fact]
    public void NoLabelsGiveHumanisedNamesAndTheHostFormatterIsUsedForUnlabelledMembers()
    {
        var plain = Intake(WorkbenchLocale.En, RuleLabels.Empty);
        var formatted = Intake(WorkbenchLocale.En, new RuleLabels { ValueFormatter = value => value is EnumValue member ? "<" + member.Value + ">" : null });

        plain.Facts[3].Label.Should().Be("age days");
        plain.Facts[0].Values.Select(choice => choice.Label).Should().Equal("Web", "Phone", "Email");
        formatted.Facts[0].Values.Select(choice => choice.Label).Should().Equal("<Web>", "<Phone>", "<Email>");
    }

    [Fact]
    public void ARuleWithoutADefaultRowAndACollectPolicyIsReportedAsSuch()
    {
        var rule = TestFiles.LoadReadiness();

        var choices = RuleChoiceCatalog.For(rule, RuleLabels.Empty, WorkbenchLocale.Es);

        choices.HasDefaultRow.Should().BeFalse();
        choices.HitPolicy.Should().Be("collect");
        choices.Facts.Select(fact => (fact.Fact, fact.ValueType)).Should().Equal(("stage", "Enum"), ("score", "Integer"));
        choices.Facts[0].Values.Select(choice => choice.Label).Should().Equal("Intake", "Review", "Complete");
        choices.Outcomes.Single().Should().Be(new OutcomeChoices("note", "note", "Code", true, []));
    }

    [Fact]
    public void TheUniqueRuleFixtureReportsTheUniquePolicy() =>
        RuleChoiceCatalog.For(TestFiles.LoadIntake("intake.unique.rule.json"), RuleLabels.Empty, WorkbenchLocale.En).HitPolicy.Should().Be("unique");

    // ---- operators ---------------------------------------------------------------------------------------

    private static readonly Dictionary<FactType, string[]> Expected = new()
    {
        [FactType.Code] = ["Any", "Equals", "NotEquals", "In", "NotIn", "IsNull", "IsNotNull", "StartsWith"],
        [FactType.Enum] = ["Any", "Equals", "NotEquals", "In", "NotIn", "IsNull", "IsNotNull"],
        [FactType.Boolean] = ["Any", "Equals", "NotEquals", "IsNull", "IsNotNull"],
        [FactType.Integer] = ["Any", "Equals", "NotEquals", "In", "NotIn", "IsNull", "IsNotNull", "Range"],
        [FactType.Decimal] = ["Any", "Equals", "NotEquals", "In", "NotIn", "IsNull", "IsNotNull", "Range"],
        [FactType.Money] = ["Any", "Equals", "NotEquals", "In", "NotIn", "IsNull", "IsNotNull", "Range"],
        [FactType.Date] = ["Any", "Equals", "NotEquals", "In", "NotIn", "IsNull", "IsNotNull", "Range"],
        [FactType.Reference] = ["Any", "Equals", "NotEquals", "In", "NotIn", "IsNull", "IsNotNull"],
    };

    [Fact]
    public void TheOperatorsOfEachFactAreTheOnesOfItsTypeInTheEnginesOrder()
    {
        foreach (var fact in Intake(WorkbenchLocale.En).Facts)
        {
            var type = Enum.Parse<FactType>(fact.ValueType);
            fact.Operators.Should().Equal(Expected[type], fact.Fact);
        }
    }

    public static TheoryData<FactType, Operator> EveryPair()
    {
        var data = new TheoryData<FactType, Operator>();
        foreach (var type in Enum.GetValues<FactType>())
        {
            foreach (var op in Enum.GetValues<Operator>())
            {
                data.Add(type, op);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void ACatalogueOperatorIsExactlyWhatTheEngineMatcherAcceptsForTheType(FactType type, Operator op)
    {
        var offered = Expected[type].Contains(op.ToString());

        var accepted = true;
        try
        {
            ConditionMatcher.Create(new Condition { FactName = "f", Operator = op }, type);
        }
        catch (Nuvora.Decisions.Contracts.DecisionEngineException)
        {
            accepted = false;
        }

        offered.Should().Be(accepted, $"{op} on {type}");
    }

    [Fact]
    public void ARealCatalogueNeverOffersAnOperatorTheMatcherRefuses()
    {
        var rule = TestFiles.LoadIntake(DraftKit.First);

        foreach (var fact in Intake(WorkbenchLocale.En).Facts)
        {
            var type = rule.Kind.Facts.Single(declared => declared.Name == fact.Fact).Type;
            foreach (var op in fact.Operators)
            {
                var create = () => ConditionMatcher.Create(new Condition { FactName = fact.Fact, Operator = Enum.Parse<Operator>(op) }, type);
                create.Should().NotThrow($"{op} on {fact.Fact}");
            }
        }
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        var rule = TestFiles.LoadIntake(DraftKit.First);

        new Action[]
        {
            () => RuleChoiceCatalog.For(null!, RuleLabels.Empty, WorkbenchLocale.En),
            () => RuleChoiceCatalog.For(rule, null!, WorkbenchLocale.En),
        }.Should().AllSatisfy(call => call.Should().Throw<ArgumentNullException>());
    }
}

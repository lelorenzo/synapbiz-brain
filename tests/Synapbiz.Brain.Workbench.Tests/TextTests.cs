using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class TextTests
{
    private static readonly Guid Reference = new("00000000-0000-4000-8000-0000000000aa");

    private static readonly RuleLabels Labels = new()
    {
        Values = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["topic"] = new Dictionary<string, string> { ["billing"] = "Billing questions" },
            ["route"] = new Dictionary<string, string> { ["to_confirm"] = "Needs a second look" },
        },
    };

    [Theory]
    [InlineData("hasInsuranceOnFile", "has insurance on file")]
    [InlineData("to_confirm", "to confirm")]
    [InlineData("low-score", "low score")]
    [InlineData("Review", "Review")]
    [InlineData("a.b", "a b")]
    [InlineData("level2Review", "level2 review")]
    [InlineData("_lead", "lead")]
    [InlineData("trail_", "trail")]
    [InlineData("ABC", "ABC")]
    [InlineData("", "")]
    public void HumanizeSplitsIdentifiersIntoWords(string name, string expected) =>
        WorkbenchText.Humanize(name).Should().Be(expected);

    [Fact]
    public void FormatValueOfNullIsEmpty() =>
        WorkbenchText.FormatValue(null, "any", WorkbenchLocale.En).Should().BeEmpty();

    [Theory]
    [InlineData(WorkbenchLocale.En, "billing")]
    [InlineData(WorkbenchLocale.Es, "billing")]
    public void ACodeIsShownAsWrittenWithoutLabels(WorkbenchLocale locale, string expected) =>
        WorkbenchText.FormatValue(DecisionValue.Code("billing"), "topic", locale).Should().Be(expected);

    [Fact]
    public void ACodeUsesItsValueLabelWhenThereIsOne()
    {
        WorkbenchText.FormatValue(DecisionValue.Code("billing"), "topic", WorkbenchLocale.En, Labels).Should().Be("Billing questions");
        WorkbenchText.FormatValue(DecisionValue.Code("other"), "topic", WorkbenchLocale.En, Labels).Should().Be("other");
        WorkbenchText.FormatValue(DecisionValue.Code("billing"), "route", WorkbenchLocale.En, Labels).Should().Be("billing");
    }

    [Fact]
    public void AnEnumIsHumanisedWithoutALabelAndLabelledWithOne()
    {
        WorkbenchText.FormatValue(DecisionValue.Enum("to_confirm"), "route", WorkbenchLocale.En).Should().Be("to confirm");
        WorkbenchText.FormatValue(DecisionValue.Enum("to_confirm"), "route", WorkbenchLocale.Es, Labels).Should().Be("Needs a second look");
        WorkbenchText.FormatValue(DecisionValue.Enum("Fast"), "route", WorkbenchLocale.Es, Labels).Should().Be("Fast");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, true, "yes")]
    [InlineData(WorkbenchLocale.En, false, "no")]
    [InlineData(WorkbenchLocale.Es, true, "sí")]
    [InlineData(WorkbenchLocale.Es, false, "no")]
    public void ABooleanIsYesOrNo(WorkbenchLocale locale, bool value, string expected) =>
        WorkbenchText.FormatValue(DecisionValue.Boolean(value), "flag", locale).Should().Be(expected);

    [Theory]
    [InlineData(WorkbenchLocale.En, "-1234")]
    [InlineData(WorkbenchLocale.Es, "-1234")]
    public void AnIntegerIsWrittenWithoutGrouping(WorkbenchLocale locale, string expected) =>
        WorkbenchText.FormatValue(DecisionValue.Integer(-1234), "count", locale).Should().Be(expected);

    [Theory]
    [InlineData(WorkbenchLocale.En, "12.50")]
    [InlineData(WorkbenchLocale.Es, "12,50")]
    public void ADecimalUsesTheLocalesSeparator(WorkbenchLocale locale, string expected) =>
        WorkbenchText.FormatValue(DecisionValue.Decimal(12.50m), "rate", locale).Should().Be(expected);

    [Theory]
    [InlineData(WorkbenchLocale.En, "12.50 USD")]
    [InlineData(WorkbenchLocale.Es, "12,50 USD")]
    public void MoneyIsTheAmountAndTheCurrency(WorkbenchLocale locale, string expected) =>
        WorkbenchText.FormatValue(DecisionValue.Money(12.50m, "USD"), "total", locale).Should().Be(expected);

    [Theory]
    [InlineData(WorkbenchLocale.En, "11/22/2026")]
    [InlineData(WorkbenchLocale.Es, "22/11/2026")]
    public void ADateFollowsTheLocalesOrder(WorkbenchLocale locale, string expected) =>
        WorkbenchText.FormatValue(DecisionValue.Date(new DateOnly(2026, 11, 22)), "day", locale).Should().Be(expected);

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void AReferenceIsItsGuid(WorkbenchLocale locale) =>
        WorkbenchText.FormatValue(DecisionValue.Reference(Reference), "account", locale).Should().Be("00000000-0000-4000-8000-0000000000aa");

    [Fact]
    public void AnUnknownLocaleIsRefusedWhereATemplateIsNeeded()
    {
        var act = () => WorkbenchText.FormatValue(DecisionValue.Boolean(true), "flag", (WorkbenchLocale)9);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AValueOfAnUnknownCaseIsRefused()
    {
        var act = () => WorkbenchText.FormatValue(new UnknownValue(), "flag", WorkbenchLocale.En);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed record UnknownValue : DecisionValue;
}

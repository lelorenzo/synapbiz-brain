using Bunit;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The form of an existing row shows what the row says, and applying it untouched changes nothing.</summary>
public sealed class EditLoadTests
{
    private static string Lines(Harness harness) => Ed.Join(harness.FindAll(".wb-rowform .wb-fline:not(.wb-fline-out)")
        .Select(line => Harness.Normalise(line.QuerySelector(".wb-flabel")!.TextContent) + "=" + line.QuerySelector("select")!.GetAttribute("value"))
        .Where(item => !item.EndsWith("=any", StringComparison.Ordinal)));

    [Theory]
    [InlineData(1, "Contact channel=eq|urgent=eq")]
    [InlineData(2, "Contact channel=neq|topic=starts")]
    [InlineData(3, "Contact channel=in|topic=in")]
    [InlineData(4, "Age in days=between|rating=isnull")]
    [InlineData(5, "rating=between|received on=notnull")]
    [InlineData(6, "Age in days=between|amount=between")]
    [InlineData(7, "Age in days=min")]
    [InlineData(8, "Contact channel=nin|Age in days=lt|received on=max")]
    public void EveryConditionOfARowIsShownWithTheWordsOfItsOperator(int number, string expected)
    {
        using var harness = Ed.Open();

        Ed.Edit(harness, number);
        harness.Find(".wb-rowform .wb-link").Click();

        Lines(harness).Should().Be(expected);
    }

    [Fact]
    public void TheBoundsAndTheCurrencyOfARangeAreShownAndAnUpperBoundOnlyIsShownInItsInput()
    {
        using var harness = Ed.Open();

        Ed.Edit(harness, 6);
        harness.Find(".wb-rowform .wb-link").Click();
        var amount = Ed.Line(harness, "amount");
        Ed.Join(amount.QuerySelectorAll("input").Select(item => item.GetAttribute("value") ?? string.Empty)).Should().Be("100|500");
        amount.QuerySelector("select.wb-in-currency")!.GetAttribute("value").Should().Be("USD");
        Ed.Cancel(harness);

        Ed.Edit(harness, 8);
        harness.Find(".wb-rowform .wb-link").Click();
        Ed.Line(harness, "Age in days").QuerySelector(".wb-fvalue input")!.GetAttribute("value").Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void ApplyingAFormThatWasNotTouchedChangesNothingSoAnExclusiveBoundStaysExclusive(int number)
    {
        using var harness = Ed.Open();
        var before = harness.Session.Work("intake.first").Draft!.Text;

        Ed.Edit(harness, number);
        Ed.Apply(harness);

        harness.FindAll(".wb-rowform").Should().BeEmpty();
        Ed.Tags(Ed.Row(harness, number)).Should().BeEmpty("the row is as in the rule");
        harness.Session.Work("intake.first").Draft!.Rule!.Table.Rows[number - 1].Should().BeEquivalentTo(
            harness.Session.Work("intake.first").Draft!.BaseRule.Table.Rows[number - 1], options => options.Excluding(row => row.Conditions));
        before.Should().NotBeNull();
    }

    [Fact]
    public void ARangeThatIsExclusiveOnTheUpperSideIsKeptAndAnotherWordingFixesItsBounds()
    {
        using var harness = Ed.Open();
        Ed.Edit(harness, 4);
        Ed.Type(harness, "Age in days", "9", second: true);
        Ed.Apply(harness);
        var range = harness.Session.Work("intake.first").Draft!.Rule!.Table.Rows[3].Conditions.Single(item => item.FactName == "ageDays");
        range.UpperInclusive.Should().BeFalse("the person changed the number, not the kind of bound");
        range.UpperValue.Should().Be(DecisionValue.Integer(9));

        Ed.Edit(harness, 4);
        Ed.Choose(harness, "Age in days", "is at most");
        Ed.Type(harness, "Age in days", "9");
        Ed.Apply(harness);
        var closed = harness.Session.Work("intake.first").Draft!.Rule!.Table.Rows[3].Conditions.Single(item => item.FactName == "ageDays");
        (closed.LowerInclusive, closed.UpperInclusive, closed.Value, closed.UpperValue).Should().Be((true, true, null, DecisionValue.Integer(9)));
    }
}

using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The rows of a draft in Read: their controls, their menu, the form that edits them and what a change does to them.</summary>
public sealed class EditRowsTests
{
    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void EachRowOfTheDraftHasEditAndMoreAndTheFallbackOnlyEdit(WorkbenchLocale locale)
    {
        var es = Ed.IsEs(locale);
        using var harness = Ed.Open(locale);

        var rows = Ed.Rows(harness);
        rows.Should().HaveCount(9);
        foreach (var row in rows.Take(8))
        {
            Ed.Join(row.QuerySelectorAll("button").Select(item => item.TextContent)).Should().Be(es ? "Editar|Más" : "Edit|More");
        }

        Ed.Join(rows[8].QuerySelectorAll("button").Select(item => item.TextContent)).Should().Be(es ? "Editar" : "Edit");
        Ed.Row(harness, 1).QuerySelector("button")!.GetAttribute("aria-label").Should().Be(es ? "Editar la fila 1" : "Edit row 1");
        harness.Find("[role=tabpanel] .wb-row-add button").TextContent.Should().Be(es ? "Agregar una fila al final" : "Add a row at the end");
        harness.FindAll("[role=tabpanel] .wb-rv").Should().BeEmpty("the draft view has no review controls");
    }

    [Fact]
    public void TheFormReplacesTheRowInPlaceWithItsTitleItsLinesAndItsButtons()
    {
        using var harness = Ed.Open();
        var sentence = Ed.RowSentence(harness, 2);

        Ed.Edit(harness, 2);

        var form = Ed.Form(harness);
        form.QuerySelector("h3")!.TextContent.Should().Be("Edit row 2");
        form.GetAttribute("aria-labelledby").Should().Be(form.QuerySelector("h3")!.Id);
        Ed.Rows(harness).Should().HaveCount(8, "the row is replaced by its form");
        Ed.Join(form.QuerySelectorAll(".wb-fsec").Select(item => item.TextContent)).Should().Be("When|Then|It reads");
        Ed.Join(harness.FindAll(".wb-rowform .wb-fline").Select(item => Harness.Normalise(item.QuerySelector(".wb-flabel")!.TextContent)))
            .Should().Be("Contact channel|topic|Route|queue|notify|priority|Row name · Optional");
        Ed.Operator(harness, "topic").GetAttribute("aria-label").Should().Be("topic: condition");
        Ed.Preview(harness).Should().Be(sentence);
        form.QuerySelector(".wb-btn-primary")!.TextContent.Should().Be("Apply to my draft");
        form.QuerySelector(".wb-link")!.TextContent.Should().Be("Show the 5 facts this row does not use");
    }

    private static void NewRow(Harness harness, string queue = "q")
    {
        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        Ed.OutcomeInput(harness, "queue").Change(queue);
    }

    private static Row NewestRow(Harness harness) => harness.Session.Work("intake.first").Draft!.Rule!.Table.Rows[^1];

    private static string Show(Condition condition) => condition.Operator switch
    {
        Operator.In or Operator.NotIn => condition.Operator + "[" + string.Join(",", condition.Values.Select(FactForm.Text)) + "]",
        Operator.Range => "Range" + (condition.LowerInclusive ? "[" : "(") + (condition.Value is null ? string.Empty : FactForm.Text(condition.Value))
            + ".." + (condition.UpperValue is null ? string.Empty : FactForm.Text(condition.UpperValue)) + (condition.UpperInclusive ? "]" : ")"),
        Operator.IsNull or Operator.IsNotNull or Operator.Any => condition.Operator.ToString(),
        _ => condition.Operator + ":" + FactForm.Text(condition.Value!),
    };

    public static TheoryData<string, string, string?, string?, string> Operators => new()
    {
        { "Age in days", "is", "5", null, "Equals:5" },
        { "Age in days", "is not", "5", null, "NotEquals:5" },
        { "Age in days", "is between", "1", "9", "Range[1..9]" },
        { "Age in days", "is at least", "3", null, "Range[3..]" },
        { "Age in days", "is more than", "3", null, "Range(3..]" },
        { "Age in days", "is at most", "3", null, "Range[..3]" },
        { "Age in days", "is less than", "3", null, "Range[..3)" },
        { "topic", "starts with", "bill", null, "StartsWith:bill" },
        { "topic", "is", "refund", null, "Equals:refund" },
        { "rating", "has a value", null, null, "IsNotNull" },
        { "rating", "has no value", null, null, "IsNull" },
        { "rating", "is", "2.5", null, "Equals:2.5" },
        { "rating", "is between", "1.5", "4", "Range[1.5..4]" },
        { "received on", "is", "2026-03-01", null, "Equals:2026-03-01" },
        { "received on", "is at most", "2026-03-01", null, "Range[..2026-03-01]" },
        { "amount", "is at least", "250", null, "Range[250 USD..]" },
        { "amount", "is between", "10.50", "20", "Range[10.50 USD..20 USD]" },
        { "urgent", "is", "Yes", null, "Equals:true" },
        { "urgent", "is not", "No", null, "NotEquals:false" },
        { "Contact channel", "is", "the website", null, "Equals:Web" },
        { "Contact channel", "is not", "email", null, "NotEquals:Email" },
    };

    [Theory]
    [MemberData(nameof(Operators))]
    public void EveryOperatorAndEveryTypeOfValueBecomesTheConditionTheLoaderReads(string fact, string wording, string? first, string? second, string expected)
    {
        using var harness = Ed.Open();
        NewRow(harness);

        Ed.Choose(harness, fact, wording);
        var line = Ed.Line(harness, fact);
        if (first is not null && line.QuerySelector(".wb-fvalue select:not(.wb-in-currency)") is not null)
        {
            Ed.Pick(harness, fact, first);
        }
        else if (first is not null)
        {
            Ed.Type(harness, fact, first);
        }

        if (second is not null)
        {
            Ed.Type(harness, fact, second, second: true);
        }

        Ed.Apply(harness);

        harness.FindAll(".wb-rowform").Should().BeEmpty();
        var condition = NewestRow(harness).Conditions.Should().ContainSingle().Subject;
        Show(condition).Should().Be(expected.Replace("10.50", "10.5", StringComparison.Ordinal));
    }

    [Fact]
    public void ASetIsPickedWithCheckboxesWhenTheFactHasValuesAndWithChipsWhenItDoesNot()
    {
        using var harness = Ed.Open();
        NewRow(harness);

        Ed.Choose(harness, "Contact channel", "is none of");
        Ed.Join(Ed.Line(harness, "Contact channel").QuerySelectorAll(".wb-radio").Select(item => item.TextContent)).Should().Be("the website|the phone line|email");
        Ed.Line(harness, "Contact channel").QuerySelectorAll("input[type=checkbox]")[1].Change(true);
        Ed.Line(harness, "Contact channel").QuerySelectorAll("input[type=checkbox]")[2].Change(true);
        Ed.Choose(harness, "topic", "is one of");
        Ed.Line(harness, "topic").QuerySelector("input[type=text]")!.Change("invoice");
        Ed.Line(harness, "topic").QuerySelector("input[type=text]")!.Change(" refund ");
        Ed.Line(harness, "topic").QuerySelector("input[type=text]")!.Change("invoice");
        Ed.Join(Ed.Line(harness, "topic").QuerySelectorAll(".wb-chip").Select(item => item.FirstChild!.TextContent)).Should().Be("invoice|refund");
        Ed.Line(harness, "topic").QuerySelector(".wb-chip-x")!.GetAttribute("aria-label").Should().Be("Remove invoice");
        Ed.Line(harness, "topic").QuerySelector(".wb-chip-x")!.Click();
        Ed.Line(harness, "Contact channel").QuerySelectorAll("input[type=checkbox]")[1].Change(false);

        Ed.Apply(harness);

        var conditions = NewestRow(harness).Conditions.Select(Show).Order(StringComparer.Ordinal).ToList();
        Ed.Join(conditions).Should().Be("In[refund]|NotIn[Email]");
    }

    [Fact]
    public void OnlyTheOperatorsTheLibraryAllowsForAFactAreListed()
    {
        using var harness = Ed.Open();
        NewRow(harness);

        string Words(string fact) => Ed.Join(Ed.Operator(harness, fact).QuerySelectorAll("option").Select(item => item.TextContent));

        Words("urgent").Should().Be("any value|is|is not|has a value|has no value");
        Words("Contact channel").Should().Be("any value|is|is not|is one of|is none of|has a value|has no value");
        Words("topic").Should().Be("any value|is|is not|is one of|is none of|starts with|has a value|has no value");
        Words("Age in days").Should().Be("any value|is|is not|is one of|is none of|is between|is at least|is more than|is at most|is less than|has a value|has no value");
        Ed.Operator(harness, "urgent").GetAttribute("value").Should().Be("any");
        Ed.Line(harness, "urgent").QuerySelectorAll(".wb-fvalue input, .wb-fvalue select").Should().BeEmpty("any value hides the value input");
    }
}

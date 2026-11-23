using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The "More" menu of a row: moving, adding and removing rows, and what undo and restore take back.</summary>
public sealed class EditMenuTests
{
    private static string Items(Harness harness) => Ed.Join(harness.Find(".wb-menu").QuerySelectorAll("button").Select(item => item.TextContent));

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void TheMenuListsTheFourActionsAndDisablesTheMovesAtTheEnds(WorkbenchLocale locale)
    {
        var es = Ed.IsEs(locale);
        using var harness = Ed.Open(locale);

        Ed.OpenMenu(harness, 1, locale);
        var button = Ed.Row(harness, 1).QuerySelector("[aria-haspopup=menu]")!;
        button.GetAttribute("aria-expanded").Should().Be("true");
        button.GetAttribute("aria-label").Should().Be(es ? "Más acciones de la fila 1" : "More actions for row 1");
        harness.Find(".wb-menu").GetAttribute("role").Should().Be("menu");
        Items(harness).Should().Be(es ? "Subir|Bajar|Agregar una fila debajo|Quitar la fila" : "Move up|Move down|Add a row below|Remove the row");
        harness.Find(".wb-menu").QuerySelectorAll("[role=menuitem]").Select(item => item.HasAttribute("disabled")).Should().Equal(true, false, false, false);

        Ed.Button(Ed.Row(harness, 1), es ? "Más" : "More").Click();
        harness.FindAll(".wb-menu").Should().BeEmpty();
        Ed.OpenMenu(harness, 8, locale);
        harness.Find(".wb-menu").QuerySelectorAll("[role=menuitem]").Select(item => item.HasAttribute("disabled")).Should().Equal(false, true, false, false);
    }

    [Fact]
    public void EscapeClosesTheMenuAndTheArrowsMoveBetweenItsEnabledItems()
    {
        using var harness = Ed.Open();
        Ed.OpenMenu(harness, 1);
        var calls = harness.FocusCount;

        Ed.MenuItem(harness, "Move down").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Ed.MenuItem(harness, "Add a row below").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Ed.MenuItem(harness, "Add a row below").KeyDown(new KeyboardEventArgs { Key = "x" });
        harness.FocusCount.Should().Be(calls + 2);
        harness.FindAll(".wb-menu").Should().ContainSingle();

        Ed.MenuItem(harness, "Add a row below").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        harness.FindAll(".wb-menu").Should().BeEmpty();
        harness.FocusCount.Should().Be(calls + 3);
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void MovingARowDownSaysSoInALiveRegionAndTagsTheRowAndUndoPutsItBack(WorkbenchLocale locale)
    {
        var es = Ed.IsEs(locale);
        using var harness = Ed.Open(locale);
        var first = Ed.RowSentence(harness, 1);
        var second = Ed.RowSentence(harness, 2);
        Ed.OpenMenu(harness, 1, locale);
        var calls = harness.FocusCount;

        Ed.MenuItem(harness, es ? "Bajar" : "Move down").Click();

        Ed.RowSentence(harness, 1).Should().Be(second);
        Ed.RowSentence(harness, 2).Should().Be(first);
        harness.FindAll(".wb-menu").Should().BeEmpty();
        harness.Find("[role=tabpanel] .wb-sr").TextContent.Should().Be(es ? "La fila 1 ahora es la fila 2." : "Row 1 is now row 2.");
        Ed.Tags(Ed.Row(harness, 2)).Should().Contain(es ? "Movida" : "Moved");
        Harness.Normalise(Ed.Row(harness, 2).QuerySelector(".wb-sub")!.TextContent).Should().Be(es ? "Antes era la fila 1." : "It was row 1.");
        Ed.Draft(harness).Steps.Should().ContainSingle().Which.Kind.Should().Be(StepKind.Move);
        harness.FocusCount.Should().BeGreaterThan(calls);

        Ed.Button(Ed.Row(harness, 2), es ? "Deshacer" : "Undo").Click();
        Ed.RowSentence(harness, 1).Should().Be(first);
        Ed.Draft(harness).Steps.Should().BeEmpty();
    }

    [Fact]
    public void MovingARowUpAndMovingAChangedRowShowBothTags()
    {
        using var harness = Ed.Open();
        Ed.Edit(harness, 3);
        Ed.Pick(harness, "Route", "manual review");
        Ed.Apply(harness);
        var third = Ed.RowSentence(harness, 3);
        Ed.OpenMenu(harness, 3);

        Ed.MenuItem(harness, "Move up").Click();
        harness.Find("[role=tabpanel] .wb-sr").TextContent.Should().Be("Row 3 is now row 2.");
        Ed.RowSentence(harness, 2).Should().Be(third);
        Ed.Tags(Ed.Row(harness, 2)).Should().Be("Changed|Moved");

        Ed.OpenMenu(harness, 2);
        Ed.MenuItem(harness, "Move up").Click();
        harness.Find("[role=tabpanel] .wb-sr").TextContent.Should().Be("Row 2 is now row 1.");
        Ed.RowSentence(harness, 1).Should().Be(third);
    }

    [Fact]
    public void AddARowBelowOpensANewRowFormInPlaceAndTheRowCanBeRemovedAgain()
    {
        using var harness = Ed.Open();
        Ed.OpenMenu(harness, 2);

        Ed.MenuItem(harness, "Add a row below").Click();

        var rows = harness.FindAll("[role=tabpanel] .wb-row, [role=tabpanel] .wb-rowform").Select(item => item.ClassList.Contains("wb-rowform") ? "form" : "row").ToList();
        Ed.Join(rows.Take(4)).Should().Be("row|row|form|row");
        harness.Find(".wb-rowform h3").TextContent.Should().Be("New row");
        Ed.OutcomeInput(harness, "queue").Change("late");
        Ed.Choose(harness, "Age in days", "is more than");
        Ed.Type(harness, "Age in days", "30");
        Ed.Apply(harness);

        harness.FindAll(".wb-rowform").Should().BeEmpty();
        Ed.Tags(Ed.Row(harness, 3)).Should().Be("New");
        Ed.Draft(harness).Steps.Should().ContainSingle().Which.Kind.Should().Be(StepKind.Add);
        Ed.Draft(harness).Rule!.Table.Rows[2].Key.Should().StartWith("row-");
        Ed.Draft(harness).Rule!.Table.Rows[2].Key.Should().MatchRegex("^row-[0-9a-f]{6}$");

        Ed.OpenMenu(harness, 3);
        Ed.MenuItem(harness, "Remove the row").Click();
        Ed.Rows(harness).Should().HaveCount(9);
        Ed.Draft(harness).Steps.Should().BeEmpty();
    }

    [Fact]
    public void ANewRowAtTheEndOpensItsFormBeforeTheFallbackRow()
    {
        using var harness = Ed.Open();

        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        Ed.OutcomeInput(harness, "queue").Change("late");
        Ed.Cancel(harness);

        harness.FindAll(".wb-rowform").Should().BeEmpty();
        Ed.Draft(harness).Steps.Should().BeEmpty();
        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        var children = harness.FindAll("[role=tabpanel] .wb-row, [role=tabpanel] .wb-rowform, [role=tabpanel] .wb-row-add");
        children[^4].ClassList.Should().Contain("wb-row", "the last numbered row");
        children[^3].ClassList.Should().Contain("wb-rowform");
        children[^1].ClassList.Should().Contain("wb-row-default");
    }

    [Fact]
    public void ARowThatACaseExpectsIsNotRemovedAndTheRefusalNamesTheCaseUnderTheRow()
    {
        using var harness = Ed.Open();
        Ed.OpenMenu(harness, 2);

        Ed.MenuItem(harness, "Remove the row").Click();

        var box = harness.Find("[role=tabpanel] .wb-refusal");
        box.GetAttribute("role").Should().Be("alert");
        box.QuerySelector(".wb-refusal-t")!.TextContent.Should().Be("This change cannot be applied");
        Ed.Join(box.QuerySelectorAll("li").Select(item => item.TextContent)).Should().Be(
            "The case \"A billing topic on the web goes to billing\" expected a row that no longer exists. Change what it expects or remove the case.");
        box.QuerySelector(".wb-refusal-stays")!.TextContent.Should().Be("Your draft stays as it was. Correct the marked value or cancel.");
        Ed.Draft(harness).Steps.Should().BeEmpty();
        Ed.Rows(harness).Should().HaveCount(9);
    }

    [Fact]
    public void ARowNoCaseExpectsIsRemovedInPlaceStruckThroughAndRestoredWithItsNumbersKept()
    {
        using var harness = Ed.Open(rule: Ed.WithoutCaseFor("no-account"));
        var sentence = Ed.RowSentence(harness, 8);
        Ed.OpenMenu(harness, 8);

        Ed.MenuItem(harness, "Remove the row").Click();

        var row = Ed.Row(harness, 8);
        row.ClassList.Should().Contain("wb-row-removed");
        row.QuerySelector(".wb-num")!.TextContent.Should().BeEmpty();
        row.QuerySelector(".wb-sent")!.ClassList.Should().Contain("wb-struck");
        Ed.RowSentence(harness, 8).Should().Be(sentence);
        Ed.Tags(row).Should().Be("Removed");
        Ed.Join(row.QuerySelectorAll("button").Select(item => item.TextContent)).Should().Be("Restore");
        Ed.Draft(harness).Rule!.Table.Rows.Should().HaveCount(7);
        Ed.Row(harness, 9).QuerySelector(".wb-num")!.TextContent.Should().Be("–", "the fallback row has no number");

        Ed.Button(row, "Restore").Click();

        Ed.Draft(harness).Steps.Should().BeEmpty();
        Ed.Tags(Ed.Row(harness, 8)).Should().BeEmpty();
        Ed.Row(harness, 8).QuerySelector(".wb-num")!.TextContent.Should().Be("8");
    }

    [Fact]
    public void TheOnlyRowOfARuleWithNoFallbackIsNotRemoved()
    {
        var rule = Ed.Variant(node =>
        {
            var body = node["body"]!.AsObject();
            var rows = body["rows"]!.AsArray();
            while (rows.Count > 1)
            {
                rows.RemoveAt(1);
            }

            body.Remove("defaultRow");
            body["goldens"]!.AsArray().Clear();
        });
        using var harness = Ed.Open(rule: rule);
        Ed.OpenMenu(harness, 1);

        Ed.MenuItem(harness, "Remove the row").Click();

        harness.Find("[role=tabpanel] .wb-refusal li").TextContent.Should().Be("The rule needs at least one row.");
        Ed.Draft(harness).Steps.Should().BeEmpty();
    }

    [Fact]
    public void ARuleWithNoRowsOffersToAddTheFirstOne()
    {
        var rule = Ed.Variant(node =>
        {
            var body = node["body"]!.AsObject();
            body["rows"]!.AsArray().Clear();
            body["goldens"]!.AsArray().Clear();
        });
        using var harness = Ed.Open(rule: rule);

        Ed.Rows(harness).Should().ContainSingle("only the fallback row");
        harness.Find("[role=tabpanel] .wb-row-add button").TextContent.Should().Be("Add the first row");
        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        Ed.OutcomeInput(harness, "queue").Change("first");
        Ed.Apply(harness);

        Ed.Row(harness, 1).QuerySelector(".wb-num")!.TextContent.Should().Be("1");
        Ed.Tags(Ed.Row(harness, 1)).Should().Be("New");
    }
}

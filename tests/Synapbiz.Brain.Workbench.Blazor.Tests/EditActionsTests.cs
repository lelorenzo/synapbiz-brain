using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>What a form, a menu action or an undo does to the draft, and how the rows say so.</summary>
public sealed class EditActionsTests
{
    [Fact]
    public void ThePreviewReadsLikeTheRowWillAndFollowsEveryChange()
    {
        using var harness = Ed.Open();
        Ed.Edit(harness, 2);
        var before = Ed.Preview(harness);

        Ed.Pick(harness, "Route", "manual review");

        var preview = Ed.Preview(harness);
        preview.Should().NotBe(before).And.Contain("manual review");
        harness.Find(".wb-preview-t").GetAttribute("aria-live").Should().Be("polite");
        Ed.Apply(harness);
        Ed.RowSentence(harness, 2).Should().Be(preview);
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void AChangedRowShowsItsTagItsOldSentenceAndUndoPutsItBack(WorkbenchLocale locale)
    {
        var es = Ed.IsEs(locale);
        using var harness = Ed.Open(locale);
        var original = Ed.RowSentence(harness, 2);
        Ed.Edit(harness, 2, locale);
        Ed.Pick(harness, es ? "Ruta" : "Route", es ? "revisión manual" : "manual review");

        Ed.Apply(harness, locale);

        var row = Ed.Row(harness, 2);
        Ed.Tags(row).Should().Be(es ? "Cambiada" : "Changed");
        Ed.RowSentence(harness, 2).Should().NotBe(original);
        Harness.Normalise(row.QuerySelector(".wb-sub")!.TextContent).Should().Be((es ? "Antes: " : "Before: ") + original);
        Ed.Join(row.QuerySelectorAll("button").Select(item => item.TextContent)).Should().Be(es ? "Deshacer|Editar|Más" : "Undo|Edit|More");
        Ed.Draft(harness).Steps.Should().ContainSingle().Which.Kind.Should().Be(StepKind.Row);
        harness.Session.UnexportedDraftCount.Should().Be(1);

        Ed.Button(row, es ? "Deshacer" : "Undo").Click();

        Ed.RowSentence(harness, 2).Should().Be(original);
        Ed.Tags(Ed.Row(harness, 2)).Should().BeEmpty();
        Ed.Draft(harness).Steps.Should().BeEmpty();
    }

    [Fact]
    public void ARowNameIsSetAndClearedThroughTheRowsOwnLabels()
    {
        using var harness = Ed.Open();

        Ed.Edit(harness, 2);
        Ed.OutcomeInput(harness, "Row name · Optional").Change("Billing on any channel");
        Ed.Apply(harness);
        Ed.Row(harness, 2).QuerySelector(".wb-row-title")!.TextContent.Should().Be("Billing on any channel");

        Ed.Edit(harness, 1);
        Ed.OutcomeInput(harness, "Row name · Optional").GetAttribute("value").Should().Be("Urgent calls");
        Ed.OutcomeInput(harness, "Row name · Optional").Change("  ");
        Ed.Apply(harness);
        Ed.Row(harness, 1).QuerySelector(".wb-row-title").Should().BeNull();
        Ed.Tags(Ed.Row(harness, 1)).Should().Be("Changed");
    }

    [Fact]
    public void AnOptionalOutcomeStartsWithNoValueAndIsRemovedWhenChosen()
    {
        using var harness = Ed.Open();
        Ed.Edit(harness, 1);
        var notify = Ed.OutcomeInput(harness, "notify");
        Ed.Join(notify.QuerySelectorAll("option").Select(item => item.TextContent)).Should().Be("No value|Yes|No");

        Ed.Pick(harness, "notify", "No value");
        Ed.Apply(harness);

        Ed.Draft(harness).Rule!.Table.Rows[0].Outcome.Should().NotContain("notify");
    }

    [Fact]
    public void ARequiredFieldWithNoTextIsAFieldErrorAndTheLoaderIsNeverAsked()
    {
        using var harness = Ed.Open();
        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        var revision = Ed.Draft(harness).Revision;
        var calls = harness.FocusCount;

        Ed.Apply(harness);

        Ed.Line(harness, "queue").QuerySelector(".wb-errt")!.TextContent.Should().Be("This is required.");
        Ed.Line(harness, "queue").QuerySelector("input")!.GetAttribute("aria-invalid").Should().Be("true");
        Ed.Draft(harness).Revision.Should().Be(revision);
        harness.FindAll(".wb-refusal").Should().BeEmpty();
        harness.FocusCount.Should().BeGreaterThan(calls, "focus goes to the first field with an error");
    }

    [Fact]
    public void ThePanelChecksARangeThatIsReversedAValueThatIsMissingAndAValueThatIsNotANumber()
    {
        using var harness = Ed.Open();
        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        Ed.OutcomeInput(harness, "queue").Change("q");
        Ed.Choose(harness, "Age in days", "is between");
        Ed.Type(harness, "Age in days", "9");
        Ed.Type(harness, "Age in days", "1", second: true);

        Ed.Apply(harness);
        Ed.Line(harness, "Age in days").QuerySelector(".wb-errt")!.TextContent.Should().Be("The first value cannot be greater than the second.");

        Ed.Type(harness, "Age in days", "", second: true);
        Ed.Apply(harness);
        Ed.Line(harness, "Age in days").QuerySelector(".wb-errt")!.TextContent.Should().Be("The value of Age in days is missing.");

        Ed.Type(harness, "Age in days", "many", second: true);
        Ed.Apply(harness);
        Ed.Line(harness, "Age in days").QuerySelector(".wb-errt")!.TextContent.Should().Be("Enter a whole number, for example 12.");
        Ed.Draft(harness).Steps.Should().BeEmpty();
        Ed.Type(harness, "Age in days", "12", second: true);
        Ed.Line(harness, "Age in days").QuerySelector(".wb-errt").Should().BeNull("the error goes away when the field changes");
    }

    [Fact]
    public void OnlyOneFormIsOpenAndAskingForAnotherSaysSoWhenTheFirstWasTouched()
    {
        using var harness = Ed.Open();
        Ed.Edit(harness, 2);

        Ed.Edit(harness, 3);
        harness.FindAll(".wb-rowform h3").Should().ContainSingle().Which.TextContent.Should().Be("Edit row 3", "an untouched form is replaced");

        Ed.Pick(harness, "Route", "manual review");
        Ed.Edit(harness, 1);
        harness.Find(".wb-rowform h3").TextContent.Should().Be("Edit row 3");
        Harness.Normalise(harness.Find(".wb-rowform [role=status]").TextContent).Should().Be("Apply or cancel row 3 before editing another one.");
    }

    [Fact]
    public void CancellingClosesTheFormWithoutAChange()
    {
        using var harness = Ed.Open();
        Ed.Edit(harness, 2);
        Ed.Pick(harness, "Route", "manual review");
        var calls = harness.FocusCount;

        Ed.Cancel(harness);

        harness.FindAll(".wb-rowform").Should().BeEmpty();
        Ed.Draft(harness).Steps.Should().BeEmpty();
        Ed.Draft(harness).Form.Should().BeNull();
        harness.FocusCount.Should().BeGreaterThan(calls);
    }

    [Fact]
    public void TheFallbackRowIsEditedWithItsOutcomeOnly()
    {
        using var harness = Ed.Open();
        Ed.Button(Ed.Row(harness, 9), "Edit").Click();

        harness.Find(".wb-rowform h3").TextContent.Should().Be("Edit the fallback row");
        harness.FindAll(".wb-rowform .wb-fsec").Select(item => item.TextContent).Should().NotContain("When");
        Ed.Pick(harness, "Route", "manual review");
        Ed.Apply(harness);

        Ed.Tags(Ed.Row(harness, 9)).Should().Be("Changed");
        Ed.Button(Ed.Row(harness, 9), "Undo").Click();
        Ed.Tags(Ed.Row(harness, 9)).Should().BeEmpty();
        Ed.Draft(harness).Steps.Should().BeEmpty();
    }
}

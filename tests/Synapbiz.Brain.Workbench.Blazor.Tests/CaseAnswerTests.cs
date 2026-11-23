using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>What a changed test case says: the answers name only the fields that differ, and bold marks only the parts that differ.</summary>
public sealed class CaseAnswerTests
{
    private const string UrgentCase = "An urgent phone request is escalated";

    private static DraftStep Row(string row, params RuleEdit[] edits) => new(StepKind.Row, row, edits);

    private static Harness Open(WorkbenchLocale locale, params (string Field, DecisionValue Value)[] outcome)
    {
        var harness = Ed.Open(locale);
        Ed.Step(harness, Row("urgent-phone", [.. outcome.Select(item => new SetOutcome("urgent-phone", item.Field, item.Value))]));
        harness.Open(WorkbenchTab.Tests);
        return harness;
    }

    private static Harness OpenUnique(RuleEdit edit, string row)
    {
        var harness = Ed.Open(name: "intake.unique", rule: Fx.Editable("intake.unique"));
        Ed.Step(harness, Row(row, edit), "intake.unique");
        harness.Open(WorkbenchTab.Tests);
        return harness;
    }

    private static IReadOnlyList<string> Asks(Harness harness) =>
        [.. harness.FindAll(".wb-review-line").Select(item => Harness.Normalise(item.TextContent))];

    private static IReadOnlyList<string> Buttons(IElement ask) => [.. ask.QuerySelectorAll("button").Select(item => Harness.Normalise(item.TextContent))];

    private static IReadOnlyList<string> Lines(IElement ask) => [.. ask.QuerySelectorAll(".wb-ask-diff > div").Select(item => Harness.Normalise(item.TextContent))];

    private static IElement DraftCell(Harness harness, string name) =>
        harness.FindAll(".wb-table tbody tr:not(.wb-review-line)").First(item => item.QuerySelector("td")!.TextContent.StartsWith(name, StringComparison.Ordinal)).QuerySelectorAll("td")[3];

    /// <summary>The text of a cell without the line under the result.</summary>
    private static string ResultText(IElement cell) =>
        Harness.Normalise(string.Join(string.Empty, cell.ChildNodes.Where(item => item is not IElement { ClassName: "wb-sub" }).Select(item => item.TextContent)));

    private static IReadOnlyList<string> Bold(IElement cell) => [.. cell.QuerySelectorAll("strong").Select(item => item.TextContent)];

    private static IReadOnlyList<IElement> ChangesRows(Harness harness)
    {
        harness.Open(WorkbenchTab.Changes);
        return harness.FindAll("[role=tabpanel] .wb-table tbody tr");
    }

    // ---- B1: the answers --------------------------------------------------------------------------

    [Theory]
    [InlineData(WorkbenchLocale.En, "Route: manual review · accept the new result", "Route: escalation · review my change")]
    [InlineData(WorkbenchLocale.Es, "Ruta: revisión manual · aceptar el nuevo resultado", "Ruta: escalado · revisar mi cambio")]
    public void OneDifferingFieldIsNamedOnBothButtons(WorkbenchLocale locale, string accept, string review)
    {
        using var harness = Open(locale, ("route", DecisionValue.Enum("Manual")));

        var ask = harness.Find(".wb-review-line");

        Buttons(ask).Should().Equal(accept, review);
        Lines(ask).Should().BeEmpty();
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "Route: manual review, queue: Phone queue · accept the new result", "Route: escalation, queue: Urgent queue · review my change")]
    [InlineData(WorkbenchLocale.Es, "Ruta: revisión manual, queue: Cola telefónica · aceptar el nuevo resultado", "Ruta: escalado, queue: Cola urgente · revisar mi cambio")]
    public void TwoDifferingFieldsAreJoinedByACommaOnBothButtons(WorkbenchLocale locale, string accept, string review)
    {
        using var harness = Open(locale, ("route", DecisionValue.Enum("Manual")), ("queue", DecisionValue.Code("phone")));

        var ask = harness.Find(".wb-review-line");

        Buttons(ask).Should().Equal(accept, review);
        Lines(ask).Should().BeEmpty();
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "Accept the new result", "Review my change", "Route: before escalation, now manual review|queue: before Urgent queue, now Phone queue|priority: before 1, now 3")]
    [InlineData(WorkbenchLocale.Es, "Aceptar el nuevo resultado", "Revisar mi cambio", "Ruta: antes escalado, ahora revisión manual|queue: antes Cola urgente, ahora Cola telefónica|priority: antes 1, ahora 3")]
    public void MoreThanTwoDifferingFieldsLeaveThePlainButtonsAndOneLineEachOverThem(WorkbenchLocale locale, string accept, string review, string lines)
    {
        using var harness = Open(locale, ("route", DecisionValue.Enum("Manual")), ("queue", DecisionValue.Code("phone")), ("priority", DecisionValue.Integer(3)));

        var ask = harness.Find(".wb-review-line");

        Buttons(ask).Should().Equal(accept, review);
        Ed.Join(Lines(ask)).Should().Be(lines);
        ask.QuerySelector(".wb-ask-diff")!.NextElementSibling!.TagName.Should().Be("BUTTON", "the lines come over the buttons");
    }

    [Fact]
    public void ASideWithNoResultSaysSoInOneLineAndLeavesThePlainButtons()
    {
        using var harness = OpenUnique(new SetCondition("urgent-any-channel", Condition.Equals("channel", DecisionValue.Enum("Phone"))), "urgent-any-channel");

        var ask = harness.Find(".wb-review-line");

        Lines(ask).Should().Equal("Result: before Escalate, Urgent queue, priority: 1, now no result");
        Buttons(ask).Should().Equal("Accept the new result", "Review my change");
    }

    [Fact]
    public void ARuleThatDecidesAfterDecidingNothingSaysNoResultOnTheOtherSide()
    {
        using var harness = OpenUnique(new SetCondition("phone-any-urgency", Condition.Equals("channel", DecisionValue.Enum("Web"))), "phone-any-urgency");

        Asks(harness).Should().Contain(text => text.Contains("Result: before no result, now Fast, Phone queue, notify: yes", StringComparison.Ordinal));
        Asks(harness).Should().Contain(text => text.Contains("Result: before Fast, Phone queue, notify: yes, now no result", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "Result: before Middle score; Late stage, now changed; Late stage")]
    [InlineData(WorkbenchLocale.Es, "Resultado: antes Puntaje medio; Etapa avanzada, ahora changed; Etapa avanzada")]
    public void SeveralResultsOfACollectRuleAreOneLineOfTheWholeResult(WorkbenchLocale locale, string line)
    {
        using var harness = Ed.Open(locale, Fx.Editable("readiness", locale), "readiness");
        Ed.Step(harness, Row("mid-score", new SetOutcome("mid-score", "note", DecisionValue.Code("changed"))), "readiness");
        harness.Open(WorkbenchTab.Tests);

        var ask = harness.Find(".wb-review-line");

        Lines(ask).Should().Equal(line);
        Ed.Join(Buttons(ask)).Should().Be(Ed.IsEs(locale) ? "Aceptar el nuevo resultado|Revisar mi cambio" : "Accept the new result|Review my change");
    }

    [Fact]
    public void AcceptingStillMakesTheCaseExpectTheNewResultWhateverTheButtonSays()
    {
        using var harness = Open(WorkbenchLocale.En, ("route", DecisionValue.Enum("Manual")), ("queue", DecisionValue.Code("phone")), ("priority", DecisionValue.Integer(3)));

        harness.Find(".wb-review-line .wb-btn-primary").Click();

        Ed.Draft(harness).Rule!.Goldens.Single(item => item.Name == UrgentCase).Expected.Should().BeOfType<Nuvora.Decisions.Contracts.Goldens.ExpectedDecided>()
            .Which.OutcomeJson.Should().Contain("Manual").And.Contain("phone").And.Contain("3");
        harness.FindAll(".wb-review-line").Should().BeEmpty();
    }

    // ---- B2: bold marks only what differs ---------------------------------------------------------

    [Theory]
    [InlineData(WorkbenchLocale.En, "manual review, Phone queue, notify: yes, priority: 1", "manual review|Phone queue")]
    [InlineData(WorkbenchLocale.Es, "revisión manual, Cola telefónica, notify: sí, priority: 1", "revisión manual|Cola telefónica")]
    public void InTheDraftColumnOnlyTheDifferingPartsAreBold(WorkbenchLocale locale, string text, string bold)
    {
        using var harness = Open(locale, ("route", DecisionValue.Enum("Manual")), ("queue", DecisionValue.Code("phone")));

        var cell = DraftCell(harness, UrgentCase);

        ResultText(cell).Should().Be(text);
        Ed.Join(Bold(cell)).Should().Be(bold);
    }

    [Fact]
    public void WhenOneFieldDiffersExactlyOnePartIsBold()
    {
        using var harness = Open(WorkbenchLocale.En, ("route", DecisionValue.Enum("Manual")));

        Bold(DraftCell(harness, UrgentCase)).Should().Equal("manual review");
    }

    [Fact]
    public void AFailingCaseThatDidNotChangeStaysWhollyBold()
    {
        using var harness = Ed.Open();
        harness.Open(WorkbenchTab.Tests);
        Ed.CaseAction(harness, harness.FindAll(".wb-table tbody tr")[1], "Change what is expected");
        harness.Find(".wb-case-form select").Change("none");
        Ed.Button(harness.Find(".wb-case-form"), "Apply to my draft").Click();

        var failing = harness.FindAll(".wb-fail-row").Single();

        failing.QuerySelectorAll("td")[3].QuerySelectorAll("strong").Should().ContainSingle();
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "escalation, Urgent queue, notify: yes, priority: 1", "manual review, Phone queue, notify: yes, priority: 1", "escalation|Urgent queue", "manual review|Phone queue")]
    [InlineData(WorkbenchLocale.Es, "escalado, Cola urgente, notify: sí, priority: 1", "revisión manual, Cola telefónica, notify: sí, priority: 1", "escalado|Cola urgente", "revisión manual|Cola telefónica")]
    public void TheChangesTabShowsTheShortResultsWithOnlyTheDifferingPartsBold(WorkbenchLocale locale, string before, string now, string boldBefore, string boldNow)
    {
        using var harness = Open(locale, ("route", DecisionValue.Enum("Manual")), ("queue", DecisionValue.Code("phone")));

        var cells = ChangesRows(harness)[0].QuerySelectorAll("td");

        Harness.Normalise(cells[1].TextContent).Should().Be(before);
        Harness.Normalise(cells[2].TextContent).Should().Be(now);
        Ed.Join(Bold(cells[1])).Should().Be(boldBefore);
        Ed.Join(Bold(cells[2])).Should().Be(boldNow);
        cells[1].TextContent.Should().NotContain(" is ", "it is the short form, not a sentence");
    }

    [Fact]
    public void ASideThatDecidesNothingIsBoldWhenTheOtherDecides()
    {
        using var harness = OpenUnique(new SetCondition("urgent-any-channel", Condition.Equals("channel", DecisionValue.Enum("Phone"))), "urgent-any-channel");

        var cells = ChangesRows(harness)[0].QuerySelectorAll("td");

        Harness.Normalise(cells[1].TextContent).Should().Be("Escalate, Urgent queue, priority: 1");
        Ed.Join(Bold(cells[1])).Should().Be("Escalate|Urgent queue|priority: 1");
        Harness.Normalise(cells[2].TextContent).Should().Be("The rule does not decide");
        Bold(cells[2]).Should().Equal("The rule does not decide");
    }

    [Fact]
    public void ResultsOfACollectRuleAreComparedGroupByGroup()
    {
        using var harness = Ed.Open(rule: Fx.Editable("readiness"), name: "readiness");
        Ed.Step(harness, Row("late-stage", new SetOutcome("late-stage", "note", DecisionValue.Code("later"))), "readiness");

        var cells = ChangesRows(harness).Single().QuerySelectorAll("td");

        Harness.Normalise(cells[1].TextContent).Should().Be("Middle score; Late stage");
        Harness.Normalise(cells[2].TextContent).Should().Be("Middle score; later");
        Bold(cells[1]).Should().Equal("Late stage");
        Bold(cells[2]).Should().Equal("later");
    }
}

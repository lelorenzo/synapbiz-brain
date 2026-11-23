using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class TestsTabTests
{
    private static Harness Open(WorkbenchRule rule, WorkbenchLocale locale = WorkbenchLocale.En)
    {
        var harness = new Harness();
        harness.Render([rule], locale);
        harness.Open(WorkbenchTab.Tests);
        return harness;
    }

    private static string Summary(Harness harness) => harness.Find(".wb-bar .wb-h2").TextContent;

    private static void FillIntakeForm(Harness harness, string name, string expected)
    {
        harness.Type("Case name", name);
        harness.Type("Contact channel", "Phone");
        harness.Type("topic", "x");
        harness.Choose("urgent", "true");
        harness.Type("Age in days", "3");
        harness.Type("Expected result", expected);
    }

    [Fact]
    public void TheSummaryLineAndTheTableShowEveryCase()
    {
        using var harness = Open(Fx.Rule("intake.first"));

        Summary(harness).Should().Be("9 of 9 cases pass");
        harness.FindAll(".wb-table thead th").Select(item => item.TextContent).Should().Equal("Case", "Data", "Expected", "The rule gives", "Status");
        harness.FindAll(".wb-table tbody tr").Should().HaveCount(9);
        harness.Find(".wb-band").TextContent.Should().Be("SimulationTest cases are made-up examples.");
        var first = harness.FindAll(".wb-table tbody tr")[0].QuerySelectorAll("td");
        first[0].TextContent.Should().Be("An urgent phone request is escalated");
        first[1].TextContent.Should().Be("Contact channel the phone line, topic billing-late, urgent yes, Age in days 100");
        first[2].TextContent.Should().Be("escalation, Urgent queue, yes, 1");
        first[3].TextContent.Should().Be("escalation, Urgent queue, yes, 1");
        first[4].QuerySelector(".wb-pill")!.TextContent.Should().Be("Passes");
        harness.FindAll(".wb-fail-row").Should().BeEmpty();
        harness.FindAll(".wb-notice").Should().BeEmpty();
    }

    [Fact]
    public void ThePanelIsInSpanish()
    {
        using var harness = Open(Fx.Rule("intake.first", WorkbenchLocale.Es), WorkbenchLocale.Es);
        Summary(harness).Should().Be("9 de 9 casos pasan");
        harness.FindAll(".wb-table thead th").Select(item => item.TextContent).Should().Equal("Caso", "Datos", "Se espera", "La regla da", "Estado");
        harness.Find(".wb-band").TextContent.Should().Be("SimulaciónLos casos de prueba son ejemplos inventados.");
        harness.ButtonByText("Agregar un caso").Should().NotBeNull();
        harness.ButtonByText("Probar todos").ClassList.Should().Contain("wb-btn-primary");
    }

    [Fact]
    public void AFailingCaseIsBoldHasTheNoteAndAButtonToTheSimulator()
    {
        using var harness = Open(Fx.FromText("readiness", Fx.FailingReadiness()));

        Summary(harness).Should().Be("2 of 3 cases pass · 1 fails");
        var row = harness.Find(".wb-fail-row");
        row.QuerySelector("td strong")!.TextContent.Should().Be("A low score at intake");
        var cells = row.QuerySelectorAll("td");
        cells[2].TextContent.Should().Be("Middle score");
        cells[3].QuerySelector("strong")!.TextContent.Should().Be("Low score");
        cells[3].QuerySelector(".wb-sub")!.TextContent.Should().Be("Row 1 decided.");
        cells[4].QuerySelector(".wb-pill")!.TextContent.Should().Be("Fails");
        cells[4].QuerySelector(".wb-pill")!.ClassList.Should().Contain("wb-pill-bad");
        harness.Find(".wb-notice strong").TextContent.Should().Be("A failing case is not your mistake.");
        harness.Find(".wb-notice").TextContent.Should().Contain("The case travels with your review");

        harness.Press("Open in the simulator");
        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Simulate);
        harness.Input("Stage").GetAttribute("value").Should().Be("Intake");
        harness.Input("Score").GetAttribute("value").Should().Be("20");
    }

    [Fact]
    public void ACaseTheEngineRefusesFailsWithTheRefusalInWordsAndDoesNotStopTheRun()
    {
        using var harness = Open(Fx.FromText("intake.first", Fx.TextOf("intake.first"), Fx.RequiredAccountContract()));

        Summary(harness).Should().Be("8 of 9 cases pass · 1 fails");
        var row = harness.Find(".wb-fail-row");
        row.QuerySelectorAll("td")[3].TextContent.Should().Be("A required value is missing: account.");
        row.QuerySelectorAll("td")[3].QuerySelector(".wb-sub").Should().BeNull();
        row.QuerySelector(".wb-pill-bad")!.TextContent.Should().Be("Fails");
        harness.Find(".wb-table").TextContent.Should().NotContain("decisions.");
    }

    [Fact]
    public void ARuleWithNoCasesSaysSoAndListsEveryRowAsUncovered()
    {
        using var harness = Open(Fx.FromText("readiness", Fx.WithoutCases()));

        Summary(harness).Should().Be("This rule has no test cases. Add the first one from the simulator.");
        harness.FindAll(".wb-table").Should().BeEmpty();
        harness.FindAll(".wb-row").Select(item => item.QuerySelector(".wb-num")!.TextContent).Should().Equal("1", "2", "3");
        harness.FindAll(".wb-row button").Select(item => item.TextContent).Should().OnlyContain(text => text == "Create a case for this row");
    }

    [Fact]
    public void WhenEveryRowIsCoveredTheSectionSaysSo()
    {
        using var harness = Open(Fx.Rule("intake.first"));
        harness.FindAll("[role=tabpanel] .wb-h2")[^1].TextContent.Should().Be("Rows with no test case");
        harness.Find(".wb-sub-flush").TextContent.Should().Be("Every row has at least one passing case.");
    }

    [Fact]
    public void AnUncoveredRowOffersACaseThatPinsItsOutcome()
    {
        using var harness = Open(Fx.FromText("intake.first", Fx.TextOf("intake.first"), Fx.RequiredAccountContract()));
        harness.FindAll(".wb-row").Should().ContainSingle().Which.QuerySelector(".wb-num")!.TextContent.Should().Be("8");

        harness.Press("Create a case for this row");

        harness.Find(".wb-case-form").Should().NotBeNull();
        harness.Input("Expected result").GetAttribute("value").Should().Be("7");
        harness.Ctx.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Should().NotBeEmpty();
    }

    [Fact]
    public void ACollectRuleOffersTheRowsOutcomeAsAChecked()
    {
        using var harness = Open(Fx.FromText("readiness", Fx.WithoutCases()));
        harness.FindAll(".wb-row button")[1].Click();

        harness.FindAll(".wb-radios-stack input[type=checkbox]").Select(item => item.HasAttribute("checked")).Should().Equal(false, true, false, false);
    }

    [Fact]
    public void TheAddFormShowsThePrivacyNoticeTheNameTheFactsAndTheExpectedResult()
    {
        using var harness = Open(Fx.Rule("intake.first"));
        harness.Press("Add a case");

        var form = harness.Find(".wb-case-form");
        form.QuerySelector(".wb-notice")!.TextContent.Should().StartWith("Do not identify real people.");
        form.QuerySelector(".wb-notice strong")!.TextContent.Should().Be("Do not identify real people.");
        harness.Input("Case name").GetAttribute("maxlength").Should().Be("200");
        harness.Input("Expected result").QuerySelectorAll("option").Select(item => item.TextContent).Take(3)
            .Should().Equal("Choose a result…", "The rule does not decide", "escalation, Urgent queue, yes, 1");
        harness.FindAll(".wb-case-form button").Select(item => item.TextContent).Should().Equal("Save case", "Cancel");
        harness.Ctx.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Should().NotBeEmpty();
    }

    [Fact]
    public void SavingAnEmptyFormShowsEveryMistakeAtOnce()
    {
        using var harness = Open(Fx.Rule("intake.first"));
        harness.Press("Add a case");
        harness.Press("Save case");

        var form = harness.Find(".wb-case-form");
        form.QuerySelectorAll(".wb-errt").Select(item => item.TextContent).Should().Equal(
            "Enter a name for the case.", "This is required.", "This is required.", "This is required.", "Choose the expected result.");
        harness.Input("Case name").GetAttribute("aria-invalid").Should().Be("true");
        harness.Input("Case name").GetAttribute("aria-describedby").Should().Be(form.QuerySelector(".wb-errt")!.Id);
        harness.FindAll(".wb-table tbody tr").Should().HaveCount(9);
    }

    [Fact]
    public void ACaseWithATakenNameIsRefused()
    {
        using var harness = Open(Fx.Rule("intake.first"));
        harness.Press("Add a case");
        FillIntakeForm(harness, "An urgent phone request is escalated", "0");
        harness.Press("Save case");
        harness.Find(".wb-case-form .wb-errt").TextContent.Should().Be("A case with that name already exists.");
    }

    [Fact]
    public void CancelClosesTheForm()
    {
        using var harness = Open(Fx.Rule("intake.first"));
        harness.Press("Add a case");
        harness.Press("Cancel");
        harness.FindAll(".wb-case-form").Should().BeEmpty();
    }

    [Fact]
    public void AProposedCaseIsAddedAndMarkedAndItsResultIsRun()
    {
        using var harness = Open(Fx.Rule("intake.first"));
        harness.Press("Add a case");
        FillIntakeForm(harness, "A made-up urgent call", "0");
        harness.Press("Save case");

        harness.FindAll(".wb-case-form").Should().BeEmpty();
        Summary(harness).Should().Be("10 of 10 cases pass");
        var last = harness.FindAll(".wb-table tbody tr")[^1].QuerySelectorAll("td");
        last[0].TextContent.Should().Be("A made-up urgent callProposed by you");
        last[0].QuerySelector(".wb-pill")!.TextContent.Should().Be("Proposed by you");
        last[4].QuerySelector(".wb-pill")!.TextContent.Should().Be("Passes");
        last[4].QuerySelectorAll(".wb-rv").Should().BeEmpty();
        harness.Tab(WorkbenchTab.Tests).QuerySelector(".wb-pill")!.TextContent.Should().Be("10");
        harness.Session.UnexportedRuleCount.Should().Be(1);
    }

    [Fact]
    public void AProposedCaseThatTheRuleDoesNotMeetFailsAndSaysWhichRowDecided()
    {
        using var harness = Open(Fx.Rule("intake.first"));
        harness.Press("Add a case");
        FillIntakeForm(harness, "A wrong expectation", "1");
        harness.Press("Save case");

        Summary(harness).Should().Be("9 of 10 cases pass · 1 fails");
        harness.Find(".wb-fail-row").QuerySelectorAll("td")[3].QuerySelector(".wb-sub")!.TextContent.Should().Be("Row 1 decided.");
        harness.Tab(WorkbenchTab.Tests).QuerySelector(".wb-pill")!.TextContent.Should().Be("1 fails");
    }

    [Fact]
    public void ACaseThatExpectsNoDecisionIsRun()
    {
        using var harness = Open(Fx.Rule("intake.first"));
        harness.Press("Add a case");
        FillIntakeForm(harness, "Expects nothing", "none");
        harness.Press("Save case");
        Summary(harness).Should().Be("9 of 10 cases pass · 1 fails");
        harness.Find(".wb-fail-row").QuerySelectorAll("td")[2].TextContent.Should().Be("The rule does not decide");
    }

    [Fact]
    public void ACollectRuleTakesSeveralExpectedOutcomes()
    {
        using var harness = Open(Fx.Rule("readiness"));
        harness.Press("Add a case");
        harness.Type("Case name", "Two notes");
        harness.Type("Stage", "Review");
        harness.Type("Score", "65");
        harness.Group("Expected result").Should().NotBeNull();
        var boxes = harness.FindAll(".wb-radios-stack input[type=checkbox]");
        boxes.Select(item => item.ParentElement!.TextContent).Should().Equal("Low score", "Middle score", "Late stage", "The rule does not decide");
        boxes[1].Change(true);
        harness.FindAll(".wb-radios-stack input[type=checkbox]")[2].Change(true);
        harness.Press("Save case");

        Summary(harness).Should().Be("4 of 4 cases pass");
        harness.FindAll(".wb-table tbody tr")[^1].QuerySelectorAll("td")[2].TextContent.Should().Be("Middle score; Late stage");
    }

    [Fact]
    public void ACollectRuleCanExpectNoDecisionAndUntickingAnOutcomeRemovesIt()
    {
        using var harness = Open(Fx.Rule("readiness"));
        harness.Press("Add a case");
        harness.Type("Case name", "Nothing at all");
        harness.Type("Stage", "Intake");
        harness.Type("Score", "95");
        harness.FindAll(".wb-radios-stack input[type=checkbox]")[0].Change(true);
        harness.FindAll(".wb-radios-stack input[type=checkbox]")[0].Change(false);
        harness.Press("Save case");
        harness.Find(".wb-case-form .wb-errt").TextContent.Should().Be("Choose the expected result.");

        harness.FindAll(".wb-radios-stack input[type=checkbox]")[3].Change(true);
        harness.FindAll(".wb-radios-stack input[type=checkbox]")[1].Change(true);
        harness.FindAll(".wb-radios-stack input[type=checkbox]")[3].Change(true);
        harness.Press("Save case");
        Summary(harness).Should().Be("4 of 4 cases pass");
    }

    [Fact]
    public void ACaseSavedFromTheSimulatorOpensTheFormFilledWithTheSimulatedFactsAndResult()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("intake.first")]);
        harness.Open(WorkbenchTab.Simulate);
        harness.Type("Contact channel", "Phone");
        harness.Type("topic", "billing-late");
        harness.Choose("urgent", "true");
        harness.Type("Age in days", "100");
        harness.Press("Simulate");
        harness.Press("Save as a test case");

        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Tests);
        harness.Input("Case name").GetAttribute("value").Should().BeNullOrEmpty();
        harness.Input("Contact channel").GetAttribute("value").Should().Be("Phone");
        harness.Input("topic").GetAttribute("value").Should().Be("billing-late");
        harness.Input("Age in days").GetAttribute("value").Should().Be("100");
        harness.Input("Expected result").GetAttribute("value").Should().Be("0");

        harness.Type("Case name", "From the simulator");
        harness.Press("Save case");
        Summary(harness).Should().Be("10 of 10 cases pass");
    }

    [Fact]
    public void ASimulationThatDecidedNothingBecomesACaseExpectingNoDecision()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        harness.Open(WorkbenchTab.Simulate);
        harness.Type("Stage", "Intake");
        harness.Type("Score", "95");
        harness.Press("Simulate");
        harness.Press("Save as a test case");

        harness.FindAll(".wb-radios-stack input[type=checkbox]")[3].HasAttribute("checked").Should().BeTrue();
    }

    [Fact]
    public void ASimulationWithMoneyAndAnOptionalFactIsCarriedIntoTheForm()
    {
        using var harness = new Harness();
        harness.Render([Fx.FromText("intake.first", Fx.TextOf("intake.first"), Fx.OptionalContract())]);
        harness.Open(WorkbenchTab.Simulate);
        harness.Type("Contact channel", "Web");
        harness.Type("topic", "x");
        harness.Choose("urgent", "false");
        harness.Type("Age in days", "30");
        harness.Type("rating", "2");
        harness.Type("amount", "250.00");
        harness.Find("input.wb-in-currency").Change("usd");
        harness.Press("Simulate");
        harness.Press("Save as a test case");

        harness.Input("amount").GetAttribute("value").Should().Be("250.00");
        harness.Find(".wb-case-form input.wb-in-currency").GetAttribute("value").Should().Be("USD");
    }

    [Fact]
    public void TheFormForARowOfAFirstRuleDoesNotPinWhenTheOutcomeIsNotTheRows()
    {
        using var harness = Open(Fx.FromText("intake.first", Fx.TextOf("intake.first"), Fx.RequiredAccountContract()));
        harness.Press("Create a case for this row");
        harness.Input("Expected result").Change("");
        harness.Input("Expected result").GetAttribute("value").Should().BeNullOrEmpty();
        harness.Input("Expected result").Change("none");
        harness.Input("Expected result").GetAttribute("value").Should().Be("none");
    }

    [Fact]
    public void RunAllRunsTheCasesAgainAndTheButtonComesBack()
    {
        using var harness = Open(Fx.Rule("intake.first"));
        harness.ButtonByText("Run all").Click();
        harness.Cut.WaitForAssertion(() => harness.ButtonByText("Run all").HasAttribute("disabled").Should().BeFalse());
        Summary(harness).Should().Be("9 of 9 cases pass");
    }

    [Fact]
    public void ACaseOfTheDocumentCanBeReviewedFromTheTable()
    {
        using var harness = Open(Fx.Rule("intake.first"));
        var control = harness.FindAll(".wb-table .wb-rv")[0];
        control.GetAttribute("aria-label").Should().Be("Review of the case An urgent phone request is escalated");

        control.QuerySelectorAll("button")[1].Click();
        harness.FindAll(".wb-table textarea").Should().ContainSingle();
        harness.Find(".wb-table textarea").Change("The urgency should depend on the age.");

        harness.FindAll(".wb-table .wb-rv")[1].QuerySelectorAll("button")[0].Click();
        harness.FindAll(".wb-table button.wb-link").Should().ContainSingle().Which.TextContent.Should().Be("Add a note");
        harness.Tab(WorkbenchTab.Review).QuerySelector(".wb-pill")!.TextContent.Should().Be("2");
    }

    [Fact]
    public void TheCasesAndTheirFormSurviveSwitchingTab()
    {
        using var harness = Open(Fx.Rule("intake.first"));
        harness.Press("Add a case");
        FillIntakeForm(harness, "Kept", "0");
        harness.Press("Save case");
        harness.Open(WorkbenchTab.Read);
        harness.Open(WorkbenchTab.Tests);
        Summary(harness).Should().Be("10 of 10 cases pass");
    }

    [Fact]
    public void TheFormBelongsToTheRuleItWasOpenedFor()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("intake.first"), Fx.Rule("readiness")]);
        harness.Open(WorkbenchTab.Tests);
        harness.Press("Add a case");
        harness.FindAll(".wb-rule")[1].Click();
        harness.FindAll(".wb-case-form").Should().BeEmpty();
    }
}

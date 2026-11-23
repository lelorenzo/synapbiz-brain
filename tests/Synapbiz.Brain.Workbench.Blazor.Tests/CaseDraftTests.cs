using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The test cases in the draft view: the ones whose result changed, what the reviewer decides, and the cases the draft adds or removes.</summary>
public sealed class CaseDraftTests
{
    private const string UrgentCase = "An urgent phone request is escalated";

    /// <summary>Opens the draft of the intake rule and changes the route of row 1, which makes the urgent case give another result.</summary>
    private static Harness ChangedRow(WorkbenchLocale locale = WorkbenchLocale.En, WorkbenchRule? rule = null)
    {
        var es = Ed.IsEs(locale);
        var harness = Ed.Open(locale, rule);
        Ed.Edit(harness, 1, locale);
        Ed.Pick(harness, es ? "Ruta" : "Route", es ? "revisión manual" : "manual review");
        Ed.Apply(harness, locale);
        return harness;
    }

    private static IReadOnlyList<IElement> Lines(Harness harness) => harness.FindAll(".wb-table tbody tr:not(.wb-review-line)");

    private static IElement LineOf(Harness harness, string name) =>
        Lines(harness).First(line => line.QuerySelector("td")!.TextContent.StartsWith(name, StringComparison.Ordinal));

    private static string Ask(Harness harness) => Harness.Normalise(harness.Find(".wb-review-line").TextContent);

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void ACaseThatChangesIsAnnouncedInReadAndOnTheTabAndKeepsItsOwnQuestionInTheTests(WorkbenchLocale locale)
    {
        var es = Ed.IsEs(locale);
        using var harness = ChangedRow(locale);

        var notice = harness.Find("[role=tabpanel] .wb-notice-act");
        Harness.Normalise(notice.QuerySelector("span")!.TextContent).Should().Be(es
            ? "Con sus cambios, 1 caso de prueba da otro resultado. Decida qué hacer con él en Casos de prueba."
            : "With your changes, 1 test case gives another result. Decide what to do with it in Test cases.");
        notice.QuerySelector("strong")!.TextContent.Should().StartWith(es ? "Con sus cambios" : "With your changes");
        harness.Tab(WorkbenchTab.Tests).QuerySelector(".wb-pill-bad")!.TextContent.Should().Be(es ? "1 cambia" : "1 changes");

        Ed.Button(notice, es ? "Ver el caso" : "See the case").Click();

        harness.Tab(WorkbenchTab.Tests).GetAttribute("aria-selected").Should().Be("true");
        Ed.Join(harness.FindAll(".wb-table thead th").Select(item => item.TextContent)).Should().Be(es
            ? "Caso|Datos|Se espera|Su borrador da|Estado"
            : "Case|Data|Expected|Your draft gives|Status");
        var line = LineOf(harness, UrgentCase);
        var cells = line.QuerySelectorAll("td");
        cells[4].QuerySelector(".wb-pill")!.TextContent.Should().Be(es ? "Cambia" : "Changes");
        cells[4].QuerySelector(".wb-pill")!.ClassList.Should().Contain("wb-pill-bad");
        cells[2].TextContent.Should().Contain(es ? "La regla actual da esto." : "The current rule gives this.");
        cells[3].QuerySelector("strong")!.TextContent.Should().Contain(es ? "revisión manual" : "manual review");
        cells[3].TextContent.Should().Contain(es ? "Decide la fila 1, que usted cambió." : "Row 1 decides, which you changed.");
        Ask(harness).Should().StartWith(es ? "¿Cuál es el resultado correcto para este caso?" : "What is the correct result for this case?");
        Ask(harness).Should().Contain(es ? "aceptar el nuevo resultado" : "accept the new result").And.Contain(es ? "revisar mi cambio" : "review my change");
    }

    [Fact]
    public void ReviewControlsAndTheRulesOwnProposedCasesStayOutOfTheDraftView()
    {
        using var harness = ChangedRow();
        harness.Open(WorkbenchTab.Tests);

        var panel = harness.PanelText;

        panel.Should().NotContain("Proposed by you");
        harness.FindAll("[role=tabpanel] button").Select(item => item.TextContent).Should().NotContain("Review");
        harness.FindAll(".wb-pill-btn").Should().BeEmpty();
        Ed.HasButton(LineOf(harness, UrgentCase), "Change what is expected").Should().BeTrue();
        Ed.HasButton(LineOf(harness, UrgentCase), "Remove the case").Should().BeTrue();
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void AcceptingTheNewResultMakesTheCasePassWithAnUndoAndUndoBringsTheQuestionBack(WorkbenchLocale locale)
    {
        var es = Ed.IsEs(locale);
        using var harness = ChangedRow(locale);
        harness.Open(WorkbenchTab.Tests);
        var focus = harness.FocusCount;

        harness.Find(".wb-review-line .wb-btn-primary").Click();

        var draft = Ed.Draft(harness);
        draft.Steps[^1].Kind.Should().Be(StepKind.CaseAccept);
        draft.Steps[^1].Key.Should().Be(UrgentCase);
        draft.DecidedCases.Should().Equal(UrgentCase);
        harness.FindAll(".wb-review-line").Should().BeEmpty();
        var line = LineOf(harness, UrgentCase);
        Ed.Join(line.QuerySelectorAll("td")[4].QuerySelectorAll(".wb-pill").Select(item => item.TextContent)).Should().Be(es ? "Pasa|Resultado esperado cambiado" : "Passes|Expected result changed");
        line.QuerySelectorAll("td")[2].TextContent.Should().Contain(es ? "revisión manual" : "manual review");
        harness.Tab(WorkbenchTab.Tests).QuerySelector(".wb-pill-bad").Should().BeNull();
        harness.FocusCount.Should().BeGreaterThan(focus);
        harness.Session.UnexportedDraftCount.Should().Be(1);

        Ed.Button(line, es ? "Deshacer" : "Undo").Click();

        draft.DecidedCases.Should().BeEmpty();
        Ed.Join(LineOf(harness, UrgentCase).QuerySelectorAll("td")[4].QuerySelectorAll(".wb-pill").Select(item => item.TextContent)).Should().Be(es ? "Cambia" : "Changes");
        harness.FindAll(".wb-review-line").Should().HaveCount(1);
        harness.Open(WorkbenchTab.Read);
        harness.FindAll("[role=tabpanel] .wb-notice-act").Should().ContainSingle();
    }

    [Fact]
    public void AnAcceptedCaseIsWrittenAgainWithTheSameFactsAndTheNewOutcome()
    {
        using var harness = ChangedRow();
        harness.Open(WorkbenchTab.Tests);
        var facts = Ed.Draft(harness).Rule!.Goldens.Single(item => item.Name == UrgentCase).Facts;

        harness.Find(".wb-review-line .wb-btn-primary").Click();

        var golden = Ed.Draft(harness).Rule!.Goldens.Single(item => item.Name == UrgentCase);
        golden.Facts.Should().BeEquivalentTo(facts);
        golden.Expected.Should().BeOfType<ExpectedDecided>().Which.OutcomeJson.Should().Contain("Manual");
        Ed.Draft(harness).Run!.Cases.Single(item => item.Name == UrgentCase).Passed.Should().BeTrue();
    }

    [Fact]
    public void ReviewMyChangeGoesToReadAndFocusesTheRowThatDecidesNow()
    {
        using var harness = ChangedRow();
        harness.Open(WorkbenchTab.Tests);
        var focus = harness.FocusCount;
        var steps = Ed.Draft(harness).Steps.Count;

        harness.FindAll(".wb-review-line button")[1].Click();

        harness.Tab(WorkbenchTab.Read).GetAttribute("aria-selected").Should().Be("true");
        harness.FocusCount.Should().BeGreaterThan(focus);
        Ed.Draft(harness).Steps.Should().HaveCount(steps);
    }

    [Fact]
    public void ReviewMyChangeOpensTheChangesWhenOnlyAMoveExplainsTheDifference()
    {
        using var harness = Ed.Open();
        Ed.OpenMenu(harness, 1);
        Ed.MenuItem(harness, "Move down").Click();
        harness.Open(WorkbenchTab.Tests);
        var line = Lines(harness).First(item => item.QuerySelector(".wb-pill-bad") is not null && item.QuerySelector(".wb-pill-bad")!.TextContent == "Changes");

        line.NextElementSibling!.QuerySelectorAll("button")[1].Click();

        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Changes);
    }

    [Fact]
    public void ReviewMyChangeFocusesARemovedRowsRestoreButton()
    {
        var rule = Ed.Variant(node =>
        {
            var golden = node["body"]!["goldens"]!.AsArray().First(item => (string?)item!["expect"]!["row"] == "old-request")!;
            golden["expect"]!.AsObject().Remove("row");
        });
        using var harness = Ed.Open(rule: rule);
        Ed.OpenMenu(harness, 7);
        Ed.MenuItem(harness, "Remove the row").Click();
        harness.Open(WorkbenchTab.Tests);
        var focus = harness.FocusCount;
        var line = Lines(harness).First(item => item.QuerySelector(".wb-pill-bad") is not null);

        line.NextElementSibling!.QuerySelectorAll("button")[1].Click();

        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Read);
        harness.FocusCount.Should().BeGreaterThan(focus);
    }

    [Fact]
    public void ACaseThatNoRowDecidesAnymoreIsAcceptedAsExpectingNoDecision()
    {
        var rule = Ed.Variant(node =>
        {
            var body = node["body"]!.AsObject();
            body["defaultRow"] = null;
            var goldens = body["goldens"]!.AsArray();
            var kept = goldens.Where(golden => (string?)golden!["name"] != "Anything else uses the fallback").Select(golden => golden!.DeepClone()).ToList();
            goldens.Clear();
            foreach (var golden in kept)
            {
                goldens.Add(golden);
            }
        });
        using var harness = Ed.Open(rule: rule);
        Ed.Edit(harness, 7);
        Ed.Type(harness, "Age in days", "200");
        Ed.Apply(harness);
        harness.Open(WorkbenchTab.Tests);
        var line = LineOf(harness, "An old request goes to the backlog");
        line.QuerySelectorAll("td")[3].TextContent.Should().Contain("No row decides.");

        harness.Find(".wb-review-line .wb-btn-primary").Click();

        Ed.Draft(harness).Rule!.Goldens.Single(item => item.Name == "An old request goes to the backlog").Expected.Should().BeOfType<ExpectedNoDecision>();
        Ed.Draft(harness).Steps[^1].Kind.Should().Be(StepKind.CaseAccept);
    }

    [Fact]
    public void AnAcceptedCaseKeepsTheRowItPinnedAndAnUnpinnedCaseStaysUnpinned()
    {
        using var harness = ChangedRow();
        harness.Open(WorkbenchTab.Tests);
        var before = Ed.Draft(harness).Rule!.Goldens.Single(item => item.Name == UrgentCase).Expected;

        harness.Find(".wb-review-line .wb-btn-primary").Click();

        var after = Ed.Draft(harness).Rule!.Goldens.Single(item => item.Name == UrgentCase).Expected;
        ((ExpectedDecided)after).RowKey.Should().Be(((ExpectedDecided)before).RowKey);
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void WhatACaseExpectsCanBeChangedAndTheCaseTellsSo(WorkbenchLocale locale)
    {
        var es = Ed.IsEs(locale);
        using var harness = Ed.Open(locale);
        harness.Open(WorkbenchTab.Tests);
        var name = "A billing topic on the web goes to billing";
        Ed.Button(LineOf(harness, name), es ? "Cambiar lo que se espera" : "Change what is expected").Click();

        var form = harness.Find(".wb-case-form");
        form.QuerySelector("h2")!.TextContent.Should().Be(es ? $"Cambiar lo que se espera del caso {name}" : $"Change what the case {name} expects");
        form.QuerySelector("input[type=text]").Should().BeNull();
        form.QuerySelector("select")!.GetAttribute("class").Should().NotContain("wb-in-err");
        var select = form.QuerySelector("select")!;
        select.Change("none");
        Ed.Button(form, es ? "Aplicar a mi borrador" : "Apply to my draft").Click();

        harness.FindAll(".wb-case-form").Should().BeEmpty();
        var draft = Ed.Draft(harness);
        draft.Steps[^1].Kind.Should().Be(StepKind.CaseEdit);
        draft.Rule!.Goldens.Single(item => item.Name == name).Expected.Should().BeOfType<ExpectedNoDecision>();
        var line = LineOf(harness, name);
        line.QuerySelectorAll("td")[4].TextContent.Should().Contain(es ? "Resultado esperado cambiado" : "Expected result changed");
        line.QuerySelectorAll("td")[4].TextContent.Should().Contain(es ? "Falla" : "Fails");
        draft.DecidedCases.Should().Equal(name);
    }

    [Fact]
    public void TheExpectationFormStartsFromWhatTheCaseExpectsAndCancelsAndCannotBeEmpty()
    {
        using var harness = Ed.Open();
        harness.Open(WorkbenchTab.Tests);
        var name = "A billing topic on the web goes to billing";
        Ed.Button(LineOf(harness, name), "Change what is expected").Click();
        var select = harness.Find(".wb-case-form select");
        select.GetAttribute("value").Should().NotBeNullOrEmpty();

        select.Change(string.Empty);
        Ed.Button(harness.Find(".wb-case-form"), "Apply to my draft").Click();

        harness.Find(".wb-case-form .wb-errt").TextContent.Should().Be("Choose the expected result.");
        Ed.Draft(harness).Steps.Should().BeEmpty();
        Ed.Button(harness.Find(".wb-case-form"), "Cancel").Click();
        harness.FindAll(".wb-case-form").Should().BeEmpty();
    }

    [Fact]
    public void ACaseThatExpectsNoDecisionStartsTheFormWithThatChosen()
    {
        using var harness = Ed.Open();
        harness.Open(WorkbenchTab.Tests);
        var name = "A billing topic on the web goes to billing";
        Ed.Button(LineOf(harness, name), "Change what is expected").Click();
        harness.Find(".wb-case-form select").Change("none");
        Ed.Button(harness.Find(".wb-case-form"), "Apply to my draft").Click();

        Ed.Button(LineOf(harness, name), "Change what is expected").Click();

        harness.Find(".wb-case-form select").GetAttribute("value").Should().Be("none");
    }

    [Fact]
    public void ARuleThatCollectsOffersCheckboxesWhenWhatACaseExpectsIsChanged()
    {
        using var harness = Ed.Open(rule: Fx.Editable("readiness"));
        harness.Open(WorkbenchTab.Tests);
        Ed.Button(Lines(harness)[0], "Change what is expected").Click();

        var boxes = harness.FindAll(".wb-case-form input[type=checkbox]");
        boxes.Should().NotBeEmpty();
        boxes[0].Change(true);
        Ed.Button(harness.Find(".wb-case-form"), "Apply to my draft").Click();

        harness.Session.Work("readiness").Draft!.Steps[^1].Kind.Should().Be(StepKind.CaseEdit);
    }

    [Fact]
    public void ACaseIsRemovedStaysStruckThroughAndIsRestored()
    {
        using var harness = Ed.Open();
        harness.Open(WorkbenchTab.Tests);
        var name = "A billing topic on the web goes to billing";
        Ed.Button(LineOf(harness, name), "Remove the case").Click();

        var draft = Ed.Draft(harness);
        draft.Steps[^1].Kind.Should().Be(StepKind.CaseRemove);
        draft.Rule!.Goldens.Select(item => item.Name).Should().NotContain(name);
        var line = LineOf(harness, name);
        line.QuerySelector(".wb-struck")!.TextContent.Should().Be(name);
        line.QuerySelector("td .wb-pill")!.TextContent.Should().Be("Removed");
        Lines(harness).ToList().IndexOf(line).Should().Be(1);
        Ed.HasButton(line, "Change what is expected").Should().BeFalse();
        harness.Find("[role=tabpanel] .wb-bar .wb-h2").TextContent.Should().Be("8 of 8 cases pass");

        Ed.Button(line, "Restore").Click();

        draft.Steps.Should().BeEmpty();
        draft.Rule!.Goldens.Select(item => item.Name).Should().Contain(name);
        LineOf(harness, name).QuerySelector(".wb-struck").Should().BeNull();
    }

    [Fact]
    public void ARemovedCaseWithADecisionIsRestoredToItsDecision()
    {
        using var harness = ChangedRow();
        harness.Open(WorkbenchTab.Tests);
        harness.Find(".wb-review-line .wb-btn-primary").Click();
        Ed.Button(LineOf(harness, UrgentCase), "Remove the case").Click();

        Ed.Button(LineOf(harness, UrgentCase), "Restore").Click();

        Ed.Draft(harness).DecidedCases.Should().Equal(UrgentCase);
        Ed.Draft(harness).Rule!.Goldens.Select(item => item.Name).Should().Contain(UrgentCase);
    }

    [Fact]
    public void ACaseAddedInTheDraftIsTaggedNewAndRemovingItDropsItsSteps()
    {
        using var harness = Ed.Open();
        harness.Open(WorkbenchTab.Tests);
        harness.Press("Add a case");
        Add(harness, "Chat from the web", "New requests");

        var draft = Ed.Draft(harness);
        draft.Steps.Should().ContainSingle().Which.Kind.Should().Be(StepKind.CaseAdd);
        var line = LineOf(harness, "Chat from the web");
        line.QuerySelector("td .wb-pill")!.TextContent.Should().Be("New");
        line.QuerySelectorAll("td")[4].TextContent.Should().Contain("Passes");
        harness.PanelText.Should().NotContain("Proposed by you");
        harness.Session.Work("intake.first").Cases.Should().BeEmpty();
        Ed.Button(line, "Change what is expected").Click();
        harness.Find(".wb-case-form select").Change("none");
        Ed.Button(harness.Find(".wb-case-form"), "Apply to my draft").Click();
        draft.Steps.Select(item => item.Kind).Should().Equal(StepKind.CaseAdd, StepKind.CaseEdit);

        Ed.Button(LineOf(harness, "Chat from the web"), "Remove the case").Click();

        draft.Steps.Should().BeEmpty();
        Lines(harness).Should().HaveCount(9);
    }

    [Fact]
    public void ANewCaseNeedsANameAnAvailableNameAndAnExpectedResult()
    {
        using var harness = Ed.Open();
        harness.Open(WorkbenchTab.Tests);
        harness.Press("Add a case");
        harness.Press("Apply to my draft");
        harness.FindAll(".wb-case-form .wb-errt").Select(item => item.TextContent).Should().Contain("Enter a name for the case.").And.Contain("Choose the expected result.");

        harness.Type("Case name", UrgentCase);
        harness.Press("Apply to my draft");

        harness.FindAll(".wb-case-form .wb-errt").Select(item => item.TextContent).Should().Contain("A case with that name already exists.");
        Ed.Draft(harness).Steps.Should().BeEmpty();
        Ed.Button(harness.Find(".wb-case-form"), "Cancel").Click();
        harness.FindAll(".wb-case-form").Should().BeEmpty();
    }

    [Fact]
    public void ANewCaseTheLoaderRefusesShowsTheReasonInTheFormAndChangesNothing()
    {
        var host = Fx.Editable("intake.first") with
        {
            LoadDraft = text => text.Contains("Refused case", StringComparison.Ordinal) ? BrainRuleLoader.Load("{}", []) : BrainRuleLoader.Load(text, [Fx.ContractOf("intake.first")]),
        };
        using var harness = Ed.Open(rule: host);
        harness.Open(WorkbenchTab.Tests);
        harness.Press("Add a case");

        Add(harness, "Refused case", "Standard");

        harness.Find(".wb-case-form .wb-refusal").GetAttribute("role").Should().Be("alert");
        Ed.Draft(harness).Steps.Should().BeEmpty();
        harness.Find(".wb-case-form").QuerySelector("input[type=text]").Should().NotBeNull();
    }

    [Fact]
    public void AnExpectationTheLoaderRefusesShowsTheReasonInTheFormAndChangesNothing()
    {
        var refuse = false;
        var host = Fx.Editable("intake.first") with
        {
            LoadDraft = text => refuse ? BrainRuleLoader.Load("{}", []) : BrainRuleLoader.Load(text, [Fx.ContractOf("intake.first")]),
        };
        using var harness = Ed.Open(rule: host);
        harness.Open(WorkbenchTab.Tests);
        Ed.Button(Lines(harness)[0], "Change what is expected").Click();
        harness.Find(".wb-case-form select").Change("none");
        refuse = true;

        Ed.Button(harness.Find(".wb-case-form"), "Apply to my draft").Click();

        harness.Find(".wb-case-form .wb-refusal").Should().NotBeNull();
        Ed.Draft(harness).Steps.Should().BeEmpty();
    }

    [Fact]
    public void AnAcceptTheLoaderRefusesAndARemovalItRefusesLeaveTheDraftAndSayWhy()
    {
        var refuse = false;
        var host = Fx.Editable("intake.first") with
        {
            LoadDraft = text => refuse ? BrainRuleLoader.Load("{}", []) : BrainRuleLoader.Load(text, [Fx.ContractOf("intake.first")]),
        };
        using var harness = Ed.Open(rule: host);
        Ed.Edit(harness, 1);
        Ed.Pick(harness, "Route", "manual review");
        Ed.Apply(harness);
        harness.Open(WorkbenchTab.Tests);
        refuse = true;

        harness.Find(".wb-review-line .wb-btn-primary").Click();

        harness.Find("[role=tabpanel] > .wb-refusal, [role=tabpanel] .wb-refusal").Should().NotBeNull();
        Ed.Draft(harness).DecidedCases.Should().BeEmpty();
        Ed.Button(LineOf(harness, "A billing topic on the web goes to billing"), "Remove the case").Click();
        Ed.Draft(harness).Rule!.Goldens.Select(item => item.Name).Should().Contain("A billing topic on the web goes to billing");
        harness.Find("[role=tabpanel] .wb-refusal").Should().NotBeNull();
    }

    [Fact]
    public void ACaseCreatedFromTheSimulatorOrAnUncoveredRowInTheDraftViewBecomesAStepOfTheDraft()
    {
        using var harness = Ed.Open(rule: Ed.WithoutCaseFor("billing-topic"));
        harness.Open(WorkbenchTab.Tests);
        harness.Press("Create a case for this row");
        harness.Type("Case name", "A billing case");
        harness.Type("Contact channel", "Web");
        harness.Type("topic", "billing-x");
        harness.Choose("urgent", "false");
        harness.Type("Age in days", "3");

        harness.Press("Apply to my draft");

        Ed.Draft(harness).Steps.Should().ContainSingle().Which.Kind.Should().Be(StepKind.CaseAdd);
        Ed.Draft(harness).Rule!.Goldens.Select(item => item.Name).Should().Contain("A billing case");
    }

    [Fact]
    public void RunAllRunsTheDraftsCasesAgainAndTheDraftKeepsItsRule()
    {
        using var harness = Ed.Open();
        harness.Open(WorkbenchTab.Tests);
        var run = Ed.Draft(harness).Run;

        harness.Press("Run all");

        harness.Cut.WaitForAssertion(() => Ed.Draft(harness).Run.Should().NotBeSameAs(run), TimeSpan.FromSeconds(10));
        harness.PanelText.Should().Contain("9 of 9 cases pass");
    }

    [Fact]
    public void ACaseThatAlreadyFailedOnTheRuleKeepsTheFailTreatmentInTheDraftView()
    {
        using var harness = Ed.Open(rule: Fx.EditableText("readiness", Fx.FailingReadiness()));
        harness.Open(WorkbenchTab.Tests);

        var failing = Lines(harness).Where(item => item.QuerySelector(".wb-fail-row") is not null || item.ClassList.Contains("wb-fail-row")).ToList();

        failing.Should().NotBeEmpty();
        failing.Should().OnlyContain(item => item.QuerySelector(".wb-pill")!.TextContent == "Fails" || item.QuerySelector("td:last-child .wb-pill")!.TextContent == "Fails");
        harness.FindAll(".wb-review-line").Should().BeEmpty();
        harness.FindAll("[role=tabpanel] .wb-notice").Should().NotBeEmpty();
        Ed.HasButton(failing[0], "Open in the simulator").Should().BeTrue();
    }

    [Fact]
    public void ADraftThatTheRuleLeftBehindShowsTheCasesButOffersNoDecision()
    {
        using var harness = ChangedRow();
        harness.Render([Fx.EditableText("intake.first", Fx.TextOf("intake.first") + "\n")], session: harness.Session);
        harness.Open(WorkbenchTab.Tests);

        harness.FindAll(".wb-review-line").Should().BeEmpty();
        Ed.HasButton(harness.Find("[role=tabpanel]"), "Add a case").Should().BeFalse();
        Ed.HasButton(harness.Find("[role=tabpanel]"), "Change what is expected").Should().BeFalse();
        harness.Open(WorkbenchTab.Read);
        harness.FindAll("[role=tabpanel] .wb-notice-act").Should().BeEmpty();
    }

    [Fact]
    public void TheRuleViewShowsTheRuleCasesAsBeforeAndTheDraftCasesDoNotLeakIntoIt()
    {
        using var harness = ChangedRow();
        harness.Find(".wb-switch button").Click();
        harness.Open(WorkbenchTab.Tests);

        harness.FindAll(".wb-table thead th").Select(item => item.TextContent).Should().Equal("Case", "Data", "Expected", "The rule gives", "Status");
        harness.FindAll(".wb-case-form").Should().BeEmpty();
        harness.Tab(WorkbenchTab.Tests).QuerySelector(".wb-pill-bad").Should().BeNull();
    }

    private static void Add(Harness harness, string name, string expected)
    {
        harness.Type("Case name", name);
        harness.Type("Contact channel", "Phone");
        harness.Type("topic", "x");
        harness.Choose("urgent", "false");
        harness.Type("Age in days", "3");
        var select = harness.Input("Expected result");
        var option = select.QuerySelectorAll("option").First(item => item.TextContent.Contains(expected, StringComparison.OrdinalIgnoreCase));
        select.Change(option.GetAttribute("value")!);
        Ed.Button(harness.Find(".wb-case-form"), "Apply to my draft").Click();
    }

    private static string Heading(Harness harness) => Harness.Normalise(harness.Find("[role=tabpanel] .wb-bar h2").TextContent);

    private static void ChangeRoute(Harness harness, int row, WorkbenchLocale locale)
    {
        var es = Ed.IsEs(locale);
        Ed.Edit(harness, row, locale);
        Ed.Pick(harness, es ? "Ruta" : "Route", es ? "revisión manual" : "manual review");
        Ed.Apply(harness, locale);
    }

    private static WorkbenchRule WithWrongCases(int count, WorkbenchLocale locale = WorkbenchLocale.En) => Ed.Variant(
        node =>
    {
        var goldens = node["body"]!["goldens"]!.AsArray();
        for (var index = 0; index < count; index++)
        {
            goldens[2 + index]!["expect"]!["outcome"]!["priority"] = 9;
        }
    },
        locale);

    [Theory]
    [InlineData(WorkbenchLocale.En, "8 of 9 cases pass · 1 changes")]
    [InlineData(WorkbenchLocale.Es, "8 de 9 casos pasan · 1 cambia")]
    public void ACaseThatChangesIsCountedApartAndNeverCalledAFailure(WorkbenchLocale locale, string expected)
    {
        using var harness = ChangedRow(locale);

        harness.Open(WorkbenchTab.Tests);

        Heading(harness).Should().Be(expected);
        Heading(harness).Should().NotContain(Ed.IsEs(locale) ? "fall" : "fail");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "7 of 9 cases pass · 2 change")]
    [InlineData(WorkbenchLocale.Es, "7 de 9 casos pasan · 2 cambian")]
    public void SeveralCasesThatChangeUseThePluralForm(WorkbenchLocale locale, string expected)
    {
        using var harness = ChangedRow(locale);
        ChangeRoute(harness, 2, locale);

        harness.Open(WorkbenchTab.Tests);

        Heading(harness).Should().Be(expected);
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, 2, "5 of 9 cases pass · 2 change · 2 fail")]
    [InlineData(WorkbenchLocale.Es, 2, "5 de 9 casos pasan · 2 cambian · 2 fallan")]
    [InlineData(WorkbenchLocale.En, 1, "6 of 9 cases pass · 2 change · 1 fails")]
    [InlineData(WorkbenchLocale.Es, 1, "6 de 9 casos pasan · 2 cambian · 1 falla")]
    public void CasesThatAlreadyFailedOnTheRuleKeepTheirOwnCountAfterTheOnesThatChange(WorkbenchLocale locale, int wrong, string expected)
    {
        using var harness = Ed.Open(locale, WithWrongCases(wrong, locale));
        ChangeRoute(harness, 1, locale);
        ChangeRoute(harness, 2, locale);

        harness.Open(WorkbenchTab.Tests);

        Heading(harness).Should().Be(expected);
    }

    [Fact]
    public void WithNoCaseChangingAFailureStaysAFailureAndTheRuleViewKeepsItsOwnHeading()
    {
        using var harness = Ed.Open(rule: WithWrongCases(1));
        harness.Open(WorkbenchTab.Tests);
        Heading(harness).Should().Be("8 of 9 cases pass · 1 fails");

        harness.Open(WorkbenchTab.Read);
        ChangeRoute(harness, 1, WorkbenchLocale.En);
        harness.Open(WorkbenchTab.Tests);
        Heading(harness).Should().Be("7 of 9 cases pass · 1 changes · 1 fails");

        harness.Find(".wb-switch button").Click();
        harness.Open(WorkbenchTab.Tests);
        Heading(harness).Should().Be("8 of 9 cases pass · 1 fails");
    }

    [Fact]
    public void AcceptingTheNewResultTakesTheCaseOutOfTheChangingCount()
    {
        using var harness = ChangedRow();
        harness.Open(WorkbenchTab.Tests);
        Heading(harness).Should().Be("8 of 9 cases pass · 1 changes");

        harness.Find(".wb-review-line .wb-btn-primary").Click();

        Heading(harness).Should().Be("9 of 9 cases pass");
    }

    private static List<string> CaseOrder(Harness harness, string key = "intake.first") => [.. harness.Session.Work(key).Draft!.Rule!.Goldens.Select(item => item.Name)];

    [Fact]
    public void AcceptingTheNewResultKeepsTheCaseWhereItWasInTheDraftAndInTheDocument()
    {
        using var harness = ChangedRow();
        harness.Open(WorkbenchTab.Tests);
        var order = CaseOrder(harness);
        order[0].Should().Be(UrgentCase);

        harness.Find(".wb-review-line .wb-btn-primary").Click();

        CaseOrder(harness).Should().Equal(order);
        Ed.Join(Lines(harness).Select(line => line.QuerySelector("td")!.TextContent.Split("Changes", 2)[0].Trim())).Should().StartWith(UrgentCase);
        var text = System.Text.Json.Nodes.JsonNode.Parse(Ed.Draft(harness).Text!)!["body"]!["goldens"]!.AsArray().Select(golden => (string)golden!["name"]!).ToList();
        text.Should().Equal(order);
        var step = Ed.Draft(harness).Steps[^1];
        step.Edits.Should().ContainSingle().Which.Should().BeOfType<SetCaseExpectation>();
        ((SetCaseExpectation)step.Edits[0]).CaseName.Should().Be(UrgentCase);
    }

    [Fact]
    public void ChangingWhatACaseExpectsToAnotherResultKeepsTheCaseWhereItWas()
    {
        using var harness = Ed.Open();
        harness.Open(WorkbenchTab.Tests);
        var name = "An urgent phone request is escalated";
        var order = CaseOrder(harness);
        Ed.Button(LineOf(harness, name), "Change what is expected").Click();
        var select = harness.Find(".wb-case-form select");
        var current = select.GetAttribute("value") ?? select.QuerySelector("option[selected]")?.GetAttribute("value");
        var other = select.QuerySelectorAll("option").Select(item => item.GetAttribute("value")).First(value => !string.IsNullOrEmpty(value) && value != "none" && value != current);
        select.Change(other);

        Ed.Button(harness.Find(".wb-case-form"), "Apply to my draft").Click();

        CaseOrder(harness).Should().Equal(order);
        var step = Ed.Draft(harness).Steps[^1];
        step.Kind.Should().Be(StepKind.CaseEdit);
        step.Edits.Should().ContainSingle().Which.Should().BeOfType<SetCaseExpectation>();
        Ed.Draft(harness).Rule!.Goldens.Single(item => item.Name == name).Expected.Should().BeOfType<ExpectedDecided>();
    }

    private static List<string> DocumentOrder(Harness harness, string key = "intake.first") =>
        [.. System.Text.Json.Nodes.JsonNode.Parse(harness.Session.Work(key).Draft!.Text!)!["body"]!["goldens"]!.AsArray().Select(golden => (string)golden!["name"]!)];

    [Fact]
    public void ChangingWhatACaseExpectsToNoRowDecidesKeepsTheCaseWhereItWasInTheDraftAndInTheDocument()
    {
        using var harness = Ed.Open();
        harness.Open(WorkbenchTab.Tests);
        var name = "An invoice by email goes to the billing mailbox";
        var order = CaseOrder(harness);
        order.IndexOf(name).Should().BeLessThan(order.Count - 1);
        Ed.Button(LineOf(harness, name), "Change what is expected").Click();
        harness.Find(".wb-case-form select").Change("none");

        Ed.Button(harness.Find(".wb-case-form"), "Apply to my draft").Click();

        CaseOrder(harness).Should().Equal(order);
        DocumentOrder(harness).Should().Equal(order);
        Ed.Draft(harness).Rule!.Goldens.Single(item => item.Name == name).Expected.Should().BeOfType<ExpectedNoDecision>();
        var edit = Ed.Draft(harness).Steps[^1].Edits.Should().ContainSingle().Which.Should().BeOfType<SetCaseExpectation>().Subject;
        edit.ExpectNoDecision.Should().BeTrue();
    }

    [Fact]
    public void OnACollectRuleChangingTheResultsToNoneAndToTwoKeepsTheCaseWhereItWas()
    {
        using var harness = Ed.Open(rule: Fx.Editable("readiness"));
        harness.Open(WorkbenchTab.Tests);
        var order = CaseOrder(harness, "readiness");
        var name = order[0];

        Ed.Button(Lines(harness)[0], "Change what is expected").Click();
        harness.FindAll(".wb-case-form input[type=checkbox]").Last().Change(true);
        Ed.Button(harness.Find(".wb-case-form"), "Apply to my draft").Click();

        CaseOrder(harness, "readiness").Should().Equal(order);
        DocumentOrder(harness, "readiness").Should().Equal(order);
        harness.Session.Work("readiness").Draft!.Rule!.Goldens.Single(item => item.Name == name).Expected.Should().BeOfType<ExpectedNoDecision>();

        Ed.Button(LineOf(harness, name), "Change what is expected").Click();
        harness.FindAll(".wb-case-form input[type=checkbox]")[0].Change(true);
        harness.FindAll(".wb-case-form input[type=checkbox]")[1].Change(true);
        Ed.Button(harness.Find(".wb-case-form"), "Apply to my draft").Click();

        CaseOrder(harness, "readiness").Should().Equal(order);
        DocumentOrder(harness, "readiness").Should().Equal(order);
        var expected = harness.Session.Work("readiness").Draft!.Rule!.Goldens.Single(item => item.Name == name).Expected;
        expected.Should().BeOfType<ExpectedDecided>().Which.OutcomeJson.Should().StartWith("[{").And.Contain("},{");
        harness.Session.Work("readiness").Draft!.Steps[^1].Edits.Should().ContainSingle().Which.Should().BeOfType<SetCaseExpectation>().Which.Results.Should().HaveCount(2);
    }

    [Fact]
    public void OnACollectRuleAcceptingTheNewResultsKeepsTheCaseWhereItWas()
    {
        using var harness = Ed.Open(rule: Fx.Editable("readiness"));
        harness.Open(WorkbenchTab.Tests);
        var order = CaseOrder(harness, "readiness");
        var name = order[1];
        harness.Session.Work("readiness").Draft!.Steps.Should().BeEmpty();
        Ed.Button(LineOf(harness, name), "Change what is expected").Click();
        harness.FindAll(".wb-case-form input[type=checkbox]")[0].Change(true);
        Ed.Button(harness.Find(".wb-case-form"), "Apply to my draft").Click();

        CaseOrder(harness, "readiness").Should().Equal(order);
        DocumentOrder(harness, "readiness").Should().Equal(order);
        harness.Session.Work("readiness").Draft!.Rule!.Goldens.Single(item => item.Name == name).Expected.Should().BeOfType<ExpectedDecided>();
    }

    [Fact]
    public void AReadingOfAnExpectedOutcomeAsValues()
    {
        RuleWorkbench.OutcomeResults("{\"queue\":\"billing\",\"notify\":true,\"priority\":2,\"score\":1.5,\"fee\":{\"amount\":2.5,\"currency\":\"USD\"}}", false)
            .Should().ContainSingle().Which.Should().BeEquivalentTo(new Dictionary<string, DecisionValue>
            {
                ["queue"] = new CodeValue("billing"),
                ["notify"] = new BooleanValue(true),
                ["priority"] = new IntegerValue(2),
                ["score"] = new DecimalValue(1.5m),
                ["fee"] = new MoneyValue(2.5m, "USD"),
            });
        RuleWorkbench.OutcomeResults("[{\"note\":\"mid\"}]", true).Should().ContainSingle().Which
            .Should().BeEquivalentTo(new Dictionary<string, DecisionValue> { ["note"] = new CodeValue("mid") });
    }

    [Fact]
    public void ACollectOutcomeIsReadAsZeroOneOrSeveralResultsInOrder()
    {
        RuleWorkbench.OutcomeResults("[]", true).Should().BeEmpty();
        RuleWorkbench.OutcomeResults("[{\"note\":\"a\"},{\"note\":\"b\"}]", true)!.Select(result => ((CodeValue)result["note"]).Value).Should().Equal("a", "b");
    }

    [Theory]
    [InlineData("{\"note\":\"a\"}", true)]
    [InlineData("[{\"note\":\"a\"}]", false)]
    [InlineData("[{\"note\":\"a\"},1]", true)]
    [InlineData("not json", false)]
    [InlineData("{\"note\":null}", false)]
    [InlineData("{\"note\":[1]}", false)]
    [InlineData("{\"fee\":{\"currency\":\"USD\"}}", false)]
    public void AnOutcomeThatIsNotMadeOfFlatOutcomesIsNotRead(string json, bool collect)
    {
        RuleWorkbench.OutcomeResults(json, collect).Should().BeNull();
    }
}

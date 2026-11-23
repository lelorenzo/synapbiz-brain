using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The Changes tab of a draft and the proposal it exports.</summary>
public sealed class ProposalTests
{
    private static Harness WithChange(WorkbenchLocale locale = WorkbenchLocale.En, WorkbenchRule? rule = null, bool openChanges = true) =>
        Change(Ed.Open(locale, rule), locale, openChanges);

    private static Harness Change(Harness harness, WorkbenchLocale locale, bool openChanges)
    {
        var es = Ed.IsEs(locale);
        Ed.Edit(harness, 2, locale);
        Ed.Pick(harness, es ? "Ruta" : "Route", es ? "revisión manual" : "manual review");
        Ed.Apply(harness, locale);
        if (openChanges)
        {
            harness.Open(WorkbenchTab.Changes);
        }

        return harness;
    }

    private static IElement Card(Harness harness)
    {
        var cards = harness.FindAll("[role=tabpanel] .wb-card");
        return cards[cards.Count - 1];
    }

    private static void Sign(Harness harness, string reason = "The queue was wrong.", string name = "Reviewer One")
    {
        harness.Type("Reason for the change", reason);
        harness.Type("Your name", name);
    }

    [Fact]
    public void TheChangesTabExistsOnlyWhileTheRuleHasADraftAndKeyboardNavigationCountsIt()
    {
        using var harness = new Harness();
        harness.Render([Fx.Editable("intake.first")]);
        harness.FindAll("[role=tab]").Should().HaveCount(4);
        harness.Open(WorkbenchTab.Review);
        harness.Tab(WorkbenchTab.Review).KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Read);

        harness.Find(".wb-head button").Click();

        Ed.Join(harness.FindAll("[role=tab]").Select(item => item.FirstChild!.TextContent.Trim())).Should().Be("Read|Simulate|Test cases|Review|Changes");
        harness.Open(WorkbenchTab.Review);
        harness.Tab(WorkbenchTab.Review).KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Changes);
        harness.Tab(WorkbenchTab.Changes).KeyDown(new KeyboardEventArgs { Key = "End" });
        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Changes);
        harness.Tab(WorkbenchTab.Changes).KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Read);
    }

    [Fact]
    public void ARuleWithoutADraftShowsReadEvenWhenTheSessionAsksForTheChanges()
    {
        using var harness = new Harness();
        harness.Render([Fx.Editable("intake.first")]);
        harness.Session.SelectedTab = WorkbenchTab.Changes;

        harness.Cut.Render();

        harness.Tab(WorkbenchTab.Read).GetAttribute("aria-selected").Should().Be("true");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void TheChangesSayWhatChangedBeforeAndNowWithTheTagAndAnUndo(WorkbenchLocale locale)
    {
        var es = Ed.IsEs(locale);
        using var harness = Ed.Open(locale);
        var before = Ed.RowSentence(harness, 2);
        Change(harness, locale, openChanges: true);

        var notice = harness.Find("[role=tabpanel] .wb-notice");
        notice.TextContent.Should().Contain(es ? "en el motivo" : "in the reason");
        harness.Tab(WorkbenchTab.Changes).QuerySelector(".wb-pill-n")!.TextContent.Should().Be("1");
        var card = harness.Find(".wb-changed");
        card.QuerySelector(".wb-pill")!.TextContent.Should().Be(es ? "Cambiada" : "Changed");
        card.QuerySelector("strong")!.TextContent.Should().Be(es ? "Fila 2" : "Row 2");
        Harness.Normalise(card.QuerySelector(".wb-sub")!.TextContent).Should().Be((es ? "Antes: " : "Before: ") + before);
        Harness.Normalise(card.QuerySelector(".wb-sent")!.TextContent).Should().StartWith(es ? "Ahora: " : "Now: ").And.Contain(es ? "revisión manual" : "manual review");
        harness.Find("[role=tabpanel] .wb-sub").TextContent.Should().NotBeEmpty();

        var focus = harness.FocusCount;
        Ed.Button(card, es ? "Deshacer" : "Undo").Click();

        harness.FindAll(".wb-changed").Should().BeEmpty();
        Ed.Draft(harness).Steps.Should().BeEmpty();
        harness.FocusCount.Should().BeGreaterThan(focus);
        harness.Find("[role=tabpanel]").TextContent.Should().Contain(es ? "Su borrador no tiene cambios." : "Your draft has no changes.");
    }

    [Fact]
    public void AddedRemovedAndMovedRowsAreEachShownWithTheirOwnUndo()
    {
        using var harness = Ed.Open(rule: Ed.WithoutCaseFor("old-request"));
        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        Ed.OutcomeInput(harness, "queue").Change("late");
        Ed.Apply(harness);
        Ed.OpenMenu(harness, 7);
        Ed.MenuItem(harness, "Remove the row").Click();
        Ed.OpenMenu(harness, 1);
        Ed.MenuItem(harness, "Move down").Click();
        harness.Open(WorkbenchTab.Changes);

        var tags = harness.FindAll(".wb-changed").Select(item => item.GetAttribute("data-tag")).ToList();

        tags.Should().Contain(["added", "removed", "moved"]);
        var removed = harness.Find(".wb-changed[data-tag=removed]");
        removed.QuerySelector(".wb-struck").Should().NotBeNull();
        Ed.Button(removed, "Restore").Click();
        harness.FindAll(".wb-changed[data-tag=removed]").Should().BeEmpty();
        Ed.Button(harness.Find(".wb-changed[data-tag=added]"), "Undo").Click();
        harness.FindAll(".wb-changed[data-tag=added]").Should().BeEmpty();
        Ed.Button(harness.Find(".wb-changed[data-tag=moved]"), "Undo").Click();
        Ed.Draft(harness).Steps.Should().BeEmpty();
    }

    [Fact]
    public void ADefaultRowThatChangedIsShownAsTheFallbackRow()
    {
        using var harness = Ed.Open();
        Ed.Button(Ed.Row(harness, 9), "Edit").Click();
        Ed.Pick(harness, "Route", "manual review");
        Ed.Apply(harness);
        harness.Open(WorkbenchTab.Changes);

        var card = harness.Find(".wb-changed[data-tag=default]");

        card.QuerySelector("strong")!.TextContent.Should().Be("Fallback row");
        Harness.Normalise(card.TextContent).Should().Contain("Before:").And.Contain("Now:").And.Contain("manual review");
        Ed.Button(card, "Undo").Click();
        harness.FindAll(".wb-changed").Should().BeEmpty();
    }

    [Fact]
    public void TheCasesThatChangeAreListedWithTheDecisionTakenOnEachAndExportIsHeldUntilTheyAreDecided()
    {
        using var harness = WithChange(rule: null, openChanges: false);
        harness.Open(WorkbenchTab.Tests);
        harness.Open(WorkbenchTab.Changes);

        var rows = harness.FindAll("[role=tabpanel] .wb-table tbody tr");
        rows.Should().NotBeEmpty();
        var row = rows[0];
        Harness.Normalise(row.QuerySelectorAll("td")[3].TextContent).Should().Be("Not decided");
        var button = Card(harness).QuerySelector(".wb-btn-primary")!;
        button.HasAttribute("disabled").Should().BeTrue();
        Harness.Normalise(Card(harness).TextContent).Should().Contain("Decide what to do with 1 case that changed result before exporting.");

        harness.Open(WorkbenchTab.Tests);
        harness.Find(".wb-review-line .wb-btn-primary").Click();
        harness.Open(WorkbenchTab.Changes);

        Harness.Normalise(harness.FindAll("[role=tabpanel] .wb-table tbody tr")[0].QuerySelectorAll("td")[3].TextContent).Should().Be("Expected result changed");
        Card(harness).QuerySelector(".wb-btn-primary")!.HasAttribute("disabled").Should().BeFalse();
        Card(harness).TextContent.Should().NotContain("Decide what to do");
    }

    [Fact]
    public void ARemovedCaseIsListedAsRemovedInTheDecisionColumn()
    {
        using var harness = WithChange(openChanges: false);
        harness.Open(WorkbenchTab.Tests);
        var line = harness.FindAll(".wb-table tbody tr").First(item => item.ClassList.Contains("wb-fail-row"));
        Ed.CaseAction(harness, line, "Remove the case");
        harness.Open(WorkbenchTab.Changes);

        Harness.Normalise(harness.FindAll("[role=tabpanel] .wb-table tbody tr")[0].QuerySelectorAll("td")[3].TextContent).Should().Be("Removed");
        Card(harness).QuerySelector(".wb-btn-primary")!.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void WhenNoCaseChangesTheTableSaysSo()
    {
        using var harness = Ed.Open();
        Ed.Edit(harness, 2);
        Ed.OutcomeInput(harness, "Row name · Optional").Change("Billing on any channel");
        Ed.Apply(harness);
        harness.Open(WorkbenchTab.Changes);

        harness.Find("[role=tabpanel]").TextContent.Should().Contain("No test case changes result.");
        harness.FindAll("[role=tabpanel] .wb-table").Should().BeEmpty();
    }

    [Fact]
    public void TheWarningsThatRemainTrueAreListedAndTravelInTheReason()
    {
        using var harness = Ed.Open();
        Ed.OpenMenu(harness, 1);
        Ed.MenuItem(harness, "Add a row below").Click();
        Ed.OutcomeInput(harness, "queue").Change("all");
        Ed.Apply(harness);
        harness.Open(WorkbenchTab.Changes);

        var warnings = harness.FindAll(".wb-warnings li").Select(item => item.TextContent).ToList();

        warnings.Should().NotBeEmpty().And.OnlyContain(item => item.EndsWith("will never decide: an earlier row always matches first.", StringComparison.Ordinal));
        harness.Find("[role=tabpanel]").TextContent.Should().Contain("Warnings");
        var steps = Ed.Draft(harness).Steps.Count;
        harness.Open(WorkbenchTab.Tests);
        while (harness.FindAll(".wb-review-line .wb-btn-primary").Count > 0)
        {
            harness.Find(".wb-review-line .wb-btn-primary").Click();
        }

        harness.Open(WorkbenchTab.Changes);
        Sign(harness);
        Card(harness).QuerySelector(".wb-btn-primary")!.Click();

        var proposal = harness.Exports.Single().Files.Single().Proposal!;
        proposal.Note.Should().StartWith("The queue was wrong.").And.Contain("will never decide");
        Ed.Draft(harness).Steps.Count.Should().BeGreaterThanOrEqualTo(steps);
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void ExportingNeedsAReasonAndANameAndSaysWhichIsMissing(WorkbenchLocale locale)
    {
        var es = Ed.IsEs(locale);
        using var harness = WithChange(locale);
        harness.Open(WorkbenchTab.Tests);
        harness.Find(".wb-review-line .wb-btn-primary").Click();
        harness.Open(WorkbenchTab.Changes);
        var card = Card(harness);
        var focus = harness.FocusCount;

        card.QuerySelector(".wb-btn-primary")!.Click();

        harness.Exports.Should().BeEmpty();
        Card(harness).QuerySelector(".wb-errt")!.TextContent.Should().Be(es ? "Escriba el motivo del cambio." : "Write the reason for the change.");
        harness.FocusCount.Should().BeGreaterThan(focus);
        harness.Input(es ? "Motivo del cambio" : "Reason for the change").GetAttribute("aria-invalid").Should().Be("true");

        harness.Type(es ? "Motivo del cambio" : "Reason for the change", "Because");
        Card(harness).QuerySelector(".wb-btn-primary")!.Click();

        harness.Exports.Should().BeEmpty();
        Card(harness).QuerySelector(".wb-errt")!.TextContent.Should().Be(es ? "Escriba su nombre para firmar la propuesta." : "Enter your name to sign the proposal.");
        harness.Type(es ? "Su nombre" : "Your name", "A");
        Card(harness).QuerySelectorAll(".wb-errt").Should().BeEmpty();
    }

    [Fact]
    public void ExportingWritesAProposalBoundToTheBaseThatTheLoaderReadsBackAndTheDraftStays()
    {
        var rule = Fx.Editable("intake.first");
        using var harness = new Harness();
        harness.Render([rule]);
        harness.Find(".wb-head button").Click();
        Ed.Edit(harness, 2);
        Ed.Pick(harness, "Route", "manual review");
        Ed.Apply(harness);
        harness.Open(WorkbenchTab.Tests);
        harness.Find(".wb-review-line .wb-btn-primary").Click();
        harness.Open(WorkbenchTab.Changes);
        Sign(harness);
        harness.Session.UnexportedDraftCount.Should().Be(1);
        var unexportedReviews = harness.Session.UnexportedRuleCount;

        Card(harness).QuerySelector(".wb-btn-primary")!.Click();

        var file = harness.Exports.Single().Files.Single();
        file.Review.Should().BeNull();
        var proposal = file.Proposal!;
        file.SuggestedName.Should().Be("proposal-" + rule.Rule!.Id + "-v" + rule.Rule.Version + ".json");
        proposal.RuleId.Should().Be(rule.Rule.Id);
        proposal.BaseVersion.Should().Be(rule.Rule.Version);
        proposal.BaseContentHash.Should().Be(RuleReviewJson.ContentHash(rule.RuleText!));
        proposal.Author.Should().Be("Reviewer One");
        proposal.CreatedAt.Should().Be(Fx.Moment);
        proposal.Note.Should().Be("The queue was wrong.");
        var parsed = RuleProposalJson.Parse(file.Json);
        parsed.IsValid.Should().BeTrue();
        parsed.Proposal!.DraftJson.Should().Be(RuleProposalJson.Parse(RuleProposalJson.Serialize(proposal)).Proposal!.DraftJson);
        BrainRuleLoader.Load(parsed.Proposal.DraftJson, [Fx.ContractOf("intake.first")]).IsValid.Should().BeTrue();
        Ed.Draft(harness).IsExported.Should().BeTrue();
        harness.Session.UnexportedDraftCount.Should().Be(0);
        harness.Session.UnexportedRuleCount.Should().Be(unexportedReviews);
        Harness.Normalise(harness.Find(".wb-saved").TextContent).Should().Be("Proposal saved to the demo folder. Your draft is still here; if you change it, export again.");
        harness.Find(".wb-draftbar-t").TextContent.Should().NotContain("after exporting");

        harness.Open(WorkbenchTab.Read);
        Ed.Edit(harness, 2);
        Ed.Pick(harness, "Route", "escalation");
        Ed.Apply(harness);

        harness.Session.UnexportedDraftCount.Should().Be(1);
        harness.Find(".wb-draftbar-t").TextContent.Should().Contain("You changed your draft after exporting it.");
        harness.Open(WorkbenchTab.Changes);
        harness.FindAll(".wb-saved").Should().BeEmpty();
    }

    [Fact]
    public void AHostThatSaysNothingAboutWhereGetsTheShortSavedLine()
    {
        using var harness = new Harness();
        harness.Render([Fx.Editable("intake.first")], place: null);
        harness.Find(".wb-head button").Click();
        Ed.Edit(harness, 2);
        Ed.OutcomeInput(harness, "Row name · Optional").Change("Billing on any channel");
        Ed.Apply(harness);
        harness.Open(WorkbenchTab.Changes);
        Sign(harness);

        Card(harness).QuerySelector(".wb-btn-primary")!.Click();

        Harness.Normalise(harness.Find(".wb-saved").TextContent).Should().Be("Proposal saved. Your draft is still here; if you change it, export again.");
    }

    [Fact]
    public void ADraftWithNoChangesCannotBeExportedAndSaysSo()
    {
        using var harness = Ed.Open();
        harness.Open(WorkbenchTab.Changes);

        Card(harness).QuerySelector(".wb-btn-primary")!.HasAttribute("disabled").Should().BeTrue();
        Card(harness).TextContent.Should().Contain("Your draft has no changes.");
        harness.Exports.Should().BeEmpty();
    }

    [Fact]
    public void TheExplanationOfTheExportIsAlwaysThere()
    {
        using var harness = Ed.Open();
        harness.Open(WorkbenchTab.Changes);

        Harness.Normalise(Card(harness).TextContent).Should().Contain("A file is saved on this computer. You send it to the rule's maintainer. Exporting does not change, approve or activate the rule.");
    }

    [Fact]
    public void TheCardOffersNoExportWhenTheHostGivesNoPlaceToSave()
    {
        using var harness = new Harness();
        harness.Render([Fx.Editable("intake.first")], withExport: false);
        harness.Find(".wb-head button").Click();
        harness.Open(WorkbenchTab.Changes);

        Card(harness).QuerySelector("button").Should().BeNull();
        Card(harness).QuerySelector("h2")!.TextContent.Should().Be("Send the proposal");
    }

    [Fact]
    public void AReasonNearTheLimitShowsACounter()
    {
        using var harness = WithChange();
        harness.Type("Reason for the change", new string('a', 950));

        Card(harness).TextContent.Should().Contain("950");
        harness.Input("Reason for the change").GetAttribute("maxlength").Should().Be("1000");
        harness.Open(WorkbenchTab.Read);
        harness.Open(WorkbenchTab.Changes);
        Ed.Draft(harness).Reason.Should().HaveLength(950);
    }

    [Fact]
    public void ADraftThatTheRuleLeftBehindStillExportsBoundToItsBase()
    {
        using var harness = WithChange();
        harness.Open(WorkbenchTab.Tests);
        harness.Find(".wb-review-line .wb-btn-primary").Click();
        var original = Fx.TextOf("intake.first");
        harness.Render([Fx.EditableText("intake.first", original + "\n")], session: harness.Session);
        harness.Open(WorkbenchTab.Changes);
        Sign(harness);

        Card(harness).QuerySelector(".wb-btn-primary")!.Click();

        harness.Exports.Single().Files.Single().Proposal!.BaseContentHash.Should().Be(RuleReviewJson.ContentHash(original));
        harness.FindAll(".wb-changed button").Should().BeEmpty();
    }

    [Fact]
    public void ADraftThatChangedUnderneathExportsEvenWithACaseThatChangedAndOffersNoUndo()
    {
        using var harness = WithChange();
        harness.Render([Fx.EditableText("intake.first", Fx.TextOf("intake.first") + "\n")], session: harness.Session);
        harness.Open(WorkbenchTab.Changes);
        Sign(harness);

        Card(harness).QuerySelector(".wb-btn-primary")!.HasAttribute("disabled").Should().BeFalse();
        Card(harness).QuerySelector(".wb-btn-primary")!.Click();

        harness.Exports.Should().ContainSingle();
    }

    [Fact]
    public void DiscardingFromTheChangesTabGoesBackToRead()
    {
        using var harness = WithChange();

        harness.Find(".wb-head button").Click();
        harness.FindAll(".wb-dialog button")[0].Click();

        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Read);
        harness.FindAll("[role=tab]").Should().HaveCount(4);
    }

    [Fact]
    public void ADraftTheLoaderCannotReadHasNoChangesToShow()
    {
        var host = Fx.Editable("intake.first") with { LoadDraft = _ => BrainRuleLoader.Load("{}", []) };
        using var harness = new Harness();
        harness.Render([host]);
        harness.Find(".wb-head button").Click();

        harness.Open(WorkbenchTab.Changes);

        harness.Find("[role=tabpanel]").TextContent.Should().Contain("Fix the draft to continue.");
    }

    [Fact]
    public void TheChangesAreOfTheDraftEvenWhileTheRuleIsShown()
    {
        using var harness = WithChange();
        harness.Find(".wb-switch button").Click();

        harness.Open(WorkbenchTab.Changes);

        harness.FindAll(".wb-changed").Should().ContainSingle();
    }

    [Fact]
    public void NothingOfTheChangesTabShowsAnIdentifier()
    {
        var rule = Fx.Editable("intake.first");
        using var harness = WithChange(rule: rule);
        Ed.Draft(harness).Reason = "x";
        harness.Open(WorkbenchTab.Tests);
        harness.Open(WorkbenchTab.Changes);

        var markup = harness.Cut.Markup;

        foreach (var word in new[] { "urgent-phone", "billing-topic", "email-invoice", "decisions.", "brain.", "workbench.", "/body", rule.Rule!.Id })
        {
            markup.Should().NotContain(word);
        }

        System.Text.RegularExpressions.Regex.IsMatch(markup, "row-[0-9a-f]{6}").Should().BeFalse();
    }

    private static void Rename(Harness harness, int number, string name, WorkbenchLocale locale = WorkbenchLocale.En)
    {
        Ed.Edit(harness, number, locale);
        Ed.OutcomeInput(harness, Ed.IsEs(locale) ? "Nombre de la fila · Opcional" : "Row name · Optional").Change(name);
        Ed.Apply(harness, locale);
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void ARowWhoseOnlyChangeIsItsNameIsListedWithTheWordsOfWhatItWasAndIs(WorkbenchLocale locale)
    {
        var es = Ed.IsEs(locale);
        using var harness = Ed.Open(locale);
        Rename(harness, 2, "Billing on any channel", locale);
        Rename(harness, 1, "Phone emergencies", locale);
        Ed.Edit(harness, 3, locale);
        Ed.OutcomeInput(harness, es ? "Nombre de la fila · Opcional" : "Row name · Optional").Change("  ");
        Ed.Cancel(harness, locale);

        harness.Open(WorkbenchTab.Changes);

        var cards = harness.FindAll("[role=tabpanel] .wb-changed");
        cards.Should().HaveCount(2);
        cards.Select(card => card.QuerySelector(".wb-pill")!.TextContent).Should().OnlyContain(tag => tag == (es ? "Cambiada" : "Changed"));
        var first = Harness.Normalise(cards[0].TextContent);
        var second = Harness.Normalise(cards[1].TextContent);
        first.Should().Contain(es ? "Fila 1" : "Row 1").And.Contain(es
            ? "Antes se llamaba «Llamadas urgentes». Ahora se llama «Phone emergencies»."
            : "It was named \"Urgent calls\". It is now named \"Phone emergencies\".");
        second.Should().Contain(es ? "Fila 2" : "Row 2").And.Contain(es
            ? "Antes no tenía nombre. Ahora se llama «Billing on any channel»."
            : "It had no name. It is now named \"Billing on any channel\".");
        first.Should().NotContain("→").And.NotContain(es ? "Antes:" : "Before:");
        cards.Select(card => card.QuerySelector(".wb-btn")!.TextContent).Should().OnlyContain(text => text == (es ? "Deshacer" : "Undo"));
        harness.Tab(WorkbenchTab.Changes).QuerySelector(".wb-pill")!.TextContent.Should().Be("2");
        Harness.Normalise(Card(harness).QuerySelector(".wb-sub")!.TextContent).Should().StartWith(es ? "2 cambios de filas" : "2 row changes");
        harness.Find(".wb-switch button").Click();
        Harness.Normalise(harness.Find(".wb-draftbar-t").TextContent).Should().Contain(es ? "2 cambios" : "2 changes");
    }

    [Fact]
    public void AClearedNameIsSaidInWordsAndUndoTakesTheRowOutOfTheList()
    {
        using var harness = Ed.Open();
        Rename(harness, 1, "  ");

        harness.Open(WorkbenchTab.Changes);

        var card = harness.Find("[role=tabpanel] .wb-changed");
        Harness.Normalise(card.TextContent).Should().Contain("It was named \"Urgent calls\". It now has no name.");
        card.QuerySelector(".wb-btn")!.Click();

        harness.FindAll("[role=tabpanel] .wb-changed").Should().BeEmpty();
        Ed.Draft(harness).Steps.Should().BeEmpty();
    }

    [Fact]
    public void ARowNamedAgainWithItsOwnNameIsNotAChangeAndLeerAgreesWithTheChanges()
    {
        using var harness = Ed.Open();
        Rename(harness, 1, "Phone emergencies");
        Ed.Tags(Ed.Row(harness, 1)).Should().Be("Changed");
        Rename(harness, 1, "Urgent calls");

        Ed.Tags(Ed.Row(harness, 1)).Should().BeEmpty();
        harness.Open(WorkbenchTab.Changes);
        harness.FindAll("[role=tabpanel] .wb-changed").Should().BeEmpty();
        harness.Tab(WorkbenchTab.Changes).QuerySelector(".wb-pill").Should().BeNull();
    }

    [Fact]
    public void LeerTagsExactlyTheRowsTheChangesTabLists()
    {
        using var harness = Ed.Open();
        Rename(harness, 2, "Billing on any channel");
        Ed.Edit(harness, 3);
        Ed.Pick(harness, "Route", "manual review");
        Ed.Apply(harness);

        var tagged = Ed.Rows(harness).Select((row, index) => (row, number: index + 1)).Where(pair => Ed.Tags(pair.row).Contains("Changed", StringComparison.Ordinal)).Select(pair => pair.number).ToList();
        harness.Open(WorkbenchTab.Changes);

        tagged.Should().Equal(2, 3);
        harness.FindAll("[role=tabpanel] .wb-changed").Select(card => Harness.Normalise(card.QuerySelector("strong")!.TextContent)).Should().Equal("Row 2 · Billing on any channel", "Row 3");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "This draft changes 3 rows", "3 row changes", "3 changes")]
    [InlineData(WorkbenchLocale.Es, "Este borrador cambia 3 filas", "3 cambios de filas", "3 cambios")]
    public void TheSummaryThePillTheExportCardTheDraftBarAndTheDiscardDialogAllCountTheSameRows(WorkbenchLocale locale, string summary, string rows, string bar)
    {
        var es = Ed.IsEs(locale);
        using var harness = Ed.Open(locale);
        Rename(harness, 2, "Billing on any channel", locale);
        Rename(harness, 1, "Phone emergencies", locale);
        Ed.Edit(harness, 3, locale);
        Ed.Pick(harness, es ? "Ruta" : "Route", es ? "revisión manual" : "manual review");
        Ed.Apply(harness, locale);

        harness.Open(WorkbenchTab.Changes);
        harness.FindAll("[role=tabpanel] .wb-changed").Should().HaveCount(3);
        Harness.Normalise(Card(harness).QuerySelector(".wb-h2 + .wb-sub")!.TextContent).Should().StartWith(rows);
        Harness.Normalise(harness.Find("[role=tabpanel] .wb-sub").TextContent).Should().StartWith(summary);
        harness.Tab(WorkbenchTab.Changes).QuerySelector(".wb-pill")!.TextContent.Should().Be("3");
        harness.Find(".wb-switch button").Click();
        Harness.Normalise(harness.Find(".wb-draftbar-t").TextContent).Should().Contain(bar);
        harness.Find(".wb-head button").Click();
        harness.Find("[role=dialog] p").TextContent.Should().Contain(rows);
    }

    private static IReadOnlyList<string> CardTitles(Harness harness) =>
        [.. harness.FindAll("[role=tabpanel] .wb-changed").Select(card => Harness.Normalise(card.QuerySelector("strong")!.TextContent))];

    [Theory]
    [InlineData(WorkbenchLocale.En, "Row 1 · Urgent calls", "Row 1 · Phone emergencies", "Row 2")]
    [InlineData(WorkbenchLocale.Es, "Fila 1 · Llamadas urgentes", "Fila 1 · Phone emergencies", "Fila 2")]
    public void ACardIsTitledWithTheRowNameWhenTheRowHasOneAndWithTheNumberAloneWhenItHasNone(WorkbenchLocale locale, string named, string renamed, string unnamed)
    {
        var es = Ed.IsEs(locale);
        using var harness = Ed.Open(locale);
        Ed.Edit(harness, 1, locale);
        Ed.Pick(harness, es ? "Ruta" : "Route", es ? "revisión manual" : "manual review");
        Ed.Apply(harness, locale);
        Ed.Edit(harness, 2, locale);
        Ed.Pick(harness, es ? "Ruta" : "Route", es ? "revisión manual" : "manual review");
        Ed.Apply(harness, locale);

        harness.Open(WorkbenchTab.Changes);
        CardTitles(harness).Should().Equal(named, unnamed);

        harness.Open(WorkbenchTab.Read);
        Rename(harness, 1, "Phone emergencies", locale);
        harness.Open(WorkbenchTab.Changes);
        CardTitles(harness).Should().Equal(renamed, unnamed);
    }

    [Fact]
    public void ARemovedRowIsTitledWithTheNameItHadInTheRuleAndAnAddedRowWithItsNewName()
    {
        using var harness = Ed.Open(rule: Ed.WithoutCaseFor("urgent-phone"));
        Ed.OpenMenu(harness, 1);
        Ed.MenuItem(harness, "Remove the row").Click();
        harness.Find("[role=tabpanel] .wb-row-add button").Click();
        Ed.OutcomeInput(harness, "queue").Change("late");
        Ed.OutcomeInput(harness, "Row name · Optional").Change("Late arrivals");
        Ed.Apply(harness);

        harness.Open(WorkbenchTab.Changes);

        CardTitles(harness).Should().Equal("Row 1 · Urgent calls", "Row 8 · Late arrivals");
        harness.Find(".wb-changed[data-tag=removed]").TextContent.Should().Contain("Removed");
    }

    [Fact]
    public void AMovedRowCarriesItsNameInTheTitleAndTheTitleNeverHoldsAKey()
    {
        using var harness = Ed.Open();
        Ed.OpenMenu(harness, 1);
        Ed.MenuItem(harness, "Move down").Click();

        harness.Open(WorkbenchTab.Changes);

        CardTitles(harness).Should().Contain("Row 2 · Urgent calls");
        harness.Find("[role=tabpanel]").TextContent.Should().NotContain("urgent-phone");
    }

    [Fact]
    public void ADraftWhoseOnlyChangeIsARowNameCanBeExported()
    {
        using var harness = Ed.Open();
        Rename(harness, 2, "Billing on any channel");
        harness.Open(WorkbenchTab.Changes);
        Sign(harness);
        var button = Card(harness).QuerySelector(".wb-btn-primary")!;
        button.HasAttribute("disabled").Should().BeFalse();
        Card(harness).TextContent.Should().NotContain("Your draft has no changes.");

        button.Click();

        harness.Exports.Should().ContainSingle();
        harness.Find(".wb-saved").TextContent.Should().Contain("Proposal saved");
        Ed.Draft(harness).IsExported.Should().BeTrue();
    }
}

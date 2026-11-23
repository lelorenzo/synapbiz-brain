using Bunit;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class ReviewTests
{
    private static void RowButton(Harness harness, int row, string word) =>
        harness.FindAll(".wb-row .wb-rv")[row].QuerySelectorAll("button").First(item => item.TextContent.StartsWith(word, StringComparison.Ordinal)).Click();

    private static void WriteNote(Harness harness, string note) => harness.FindAll("textarea")[^1].Change(note);

    private static Harness OpenReview(params WorkbenchRule[] rules)
    {
        var harness = new Harness();
        harness.Render(rules);
        harness.Open(WorkbenchTab.Review);
        return harness;
    }

    /// <summary>Reviews the first readiness rule the way the export tests expect and leaves the Review tab open.</summary>
    private static void ReviewReadiness(Harness harness)
    {
        harness.Open(WorkbenchTab.Read);
        RowButton(harness, 0, "Fix");
        WriteNote(harness, "The limit should be 50.");
        RowButton(harness, 1, "Confirm");
        harness.Open(WorkbenchTab.Tests);
        harness.FindAll(".wb-table .wb-rv")[0].QuerySelectorAll("button")[2].Click();
        harness.Find(".wb-table textarea").Change("Is a high score really no decision?");
        harness.Press("Add a case");
        harness.Type("Case name", "A score of 50");
        harness.Type("Stage", "Intake");
        harness.Type("Score", "50");
        harness.FindAll(".wb-radios-stack input[type=checkbox]")[1].Change(true);
        harness.Press("Save case");
        harness.Open(WorkbenchTab.Review);
        harness.Find(".wb-card-head .wb-rv").QuerySelectorAll("button")[0].Click();
    }

    [Fact]
    public void ThePrivacyNoticeIsAlwaysOnTheReviewTab()
    {
        using var harness = OpenReview(Fx.Rule("readiness"));
        var notice = harness.Find("[role=tabpanel] .wb-notice");
        notice.GetAttribute("role").Should().Be("note");
        notice.QuerySelector("strong")!.TextContent.Should().Be("Do not identify real people.");
        notice.TextContent.Should().Contain("Use made-up examples.");
    }

    [Fact]
    public void WithNothingReviewedTheTabSaysSoAndExportIsOff()
    {
        using var harness = OpenReview(Fx.Rule("readiness"));

        harness.Find("[role=tabpanel] .wb-h2:not(.wb-h2-flush)").TextContent.Should().Be("Your review of this rule");
        harness.Find(".wb-card-head strong").TextContent.Should().Be("The whole rule");
        harness.Find(".wb-card-head .wb-rv").GetAttribute("aria-label").Should().Be("Review of the whole rule");
        harness.Find(".wb-grid-review").TextContent.Should().Contain("You have not reviewed this rule yet. Start in Read.");
        harness.FindAll(".wb-grid-review .wb-table").Should().BeEmpty();
        harness.Find(".wb-grid-review .wb-card .wb-sub").TextContent.Should().Be("0 of 3 rows reviewed");
        harness.ButtonByText("Export the review").HasAttribute("disabled").Should().BeTrue();
        harness.Find(".wb-grid-review").TextContent.Should().Contain("There is nothing to export.");
        harness.Find(".wb-grid-review").TextContent.Should().Contain("Exporting does not change or approve the rule.");
    }

    [Fact]
    public void TheTabIsInSpanish()
    {
        var harness = new Harness();
        using var _ = harness;
        harness.Render([Fx.Rule("readiness", WorkbenchLocale.Es)], WorkbenchLocale.Es);
        harness.Open(WorkbenchTab.Review);
        harness.Find(".wb-grid-review").TextContent.Should().Contain("Todavía no ha revisado esta regla. Empiece en Leer.");
        harness.ButtonByText("Exportar la revisión").Should().NotBeNull();
        harness.Find(".wb-notice strong").TextContent.Should().Be("No identifique a personas reales.");
        harness.Find(".wb-grid-review .wb-card .wb-sub").TextContent.Should().Be("0 de 3 filas revisadas");
    }

    [Fact]
    public void WholeRuleConfirmationIsAnItemOfTheReview()
    {
        using var harness = OpenReview(Fx.Rule("readiness"));
        harness.Find(".wb-card-head .wb-rv").QuerySelectorAll("button")[0].Click();

        harness.Find(".wb-card-head .wb-rv").QuerySelectorAll("button")[0].GetAttribute("aria-pressed").Should().Be("true");
        harness.Find(".wb-grid-review .wb-card-gap button.wb-link").TextContent.Should().Be("Add a note");
        harness.ButtonByText("Export the review").HasAttribute("disabled").Should().BeFalse();
        harness.Find(".wb-rule-s").TextContent.Should().EndWith("reviewed");
    }

    [Fact]
    public void ReviewedItemsAreInATableAndTheUnreviewedRowsAreOneLine()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        RowButton(harness, 0, "Fix");
        WriteNote(harness, "The limit should be 50.");
        harness.Open(WorkbenchTab.Tests);
        harness.FindAll(".wb-table .wb-rv")[1].QuerySelectorAll("button")[0].Click();
        harness.Open(WorkbenchTab.Review);

        harness.FindAll(".wb-grid-review .wb-table thead th").Select(item => item.TextContent).Should().Equal("About", "State", "Note");
        var rows = harness.FindAll(".wb-grid-review .wb-table tbody tr");
        rows.Should().HaveCount(3);
        rows[0].QuerySelectorAll("td")[0].TextContent.Should().StartWith("Row 1");
        rows[0].QuerySelectorAll("td")[0].QuerySelector(".wb-sub")!.TextContent.Should().StartWith("If Score is between 0 and 49");
        rows[0].QuerySelectorAll("td")[1].TextContent.Should().Be("Fix");
        rows[0].QuerySelectorAll("td")[1].QuerySelector(".wb-pill")!.ClassList.Should().Contain("wb-pill-warn");
        rows[0].QuerySelectorAll("td")[2].TextContent.Should().Be("The limit should be 50.");
        rows[1].QuerySelectorAll("td")[0].TextContent.Should().Be("Case «A mid score in review matches two rows»");
        rows[1].QuerySelectorAll("td")[1].TextContent.Should().Be("Confirmed");
        rows[1].QuerySelectorAll("td")[2].TextContent.Should().Be("No note");
        rows[2].QuerySelectorAll("td")[0].TextContent.Should().Be("Rows 2 and 3");
        rows[2].QuerySelectorAll("td")[1].TextContent.Should().Be("Not reviewed");

        rows[2].QuerySelector("button")!.TextContent.Should().Be("Go to Read");
        rows[2].QuerySelector("button")!.Click();
        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Read);
    }

    [Fact]
    public void ASingleUnreviewedRowIsNamedInTheSingular()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        RowButton(harness, 0, "Confirm");
        RowButton(harness, 1, "Confirm");
        harness.Open(WorkbenchTab.Review);
        harness.FindAll(".wb-grid-review .wb-table tbody tr")[^1].QuerySelectorAll("td")[0].TextContent.Should().Be("Row 3");
    }

    [Fact]
    public void TheSummaryCountsRowsQuestionsFixesAndProposedCases()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        ReviewReadiness(harness);
        harness.Find(".wb-grid-review .wb-card .wb-sub").TextContent.Should().Be("2 of 3 rows reviewed · 1 question · 1 to fix · 1 proposed case");
        harness.Find(".wb-grid-review .wb-card .wb-hint").TextContent.Should().Be("1 rule has a review in this session.");
        harness.FindAll(".wb-grid-review .wb-table tbody tr")
            .Select(item => item.QuerySelectorAll("td")[0].TextContent).Should().Contain("Case «A score of 50»");
        var proposed = harness.FindAll(".wb-grid-review .wb-table tbody tr").First(item => item.TextContent.Contains("A score of 50", StringComparison.Ordinal));
        proposed.QuerySelectorAll("td")[1].TextContent.Should().Be("Proposed case");
        proposed.QuerySelectorAll("td")[2].TextContent.Should().Be("Expected: Middle score. Today the rule gives: Middle score.");
    }

    [Fact]
    public void WithNoExportHandlerThereIsNoNameFieldAndNoButton()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")], withExport: false);
        harness.Open(WorkbenchTab.Review);

        harness.FindAll("label.wb-label").Select(item => item.TextContent).Should().NotContain("Your name");
        harness.FindAll("button").Select(item => item.TextContent).Should().NotContain("Export the review");
        harness.Find(".wb-grid-review .wb-card .wb-h2").TextContent.Should().Be("Send the review");
    }

    [Fact]
    public async Task ExportingWithoutANameShowsTheFieldErrorAndExportsNothing()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        ReviewReadiness(harness);

        harness.FindAll(".wb-errt").Should().BeEmpty();
        harness.Press("Export the review");
        await Task.Yield();

        harness.Exports.Should().BeEmpty();
        var field = harness.Input("Your name");
        field.GetAttribute("aria-invalid").Should().Be("true");
        var error = harness.Find(".wb-grid-review .wb-errt");
        error.TextContent.Should().Be("Enter your name to sign the review.");
        field.GetAttribute("aria-describedby").Should().Be(error.Id);
        harness.Ctx.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Should().NotBeEmpty();

        harness.Type("Your name", "   ");
        harness.Press("Export the review");
        harness.Exports.Should().BeEmpty();
        harness.Type("Your name", "Reviewer One");
        harness.FindAll(".wb-grid-review .wb-errt").Should().BeEmpty();
    }

    [Fact]
    public void TheExportHoldsOneFilePerRuleWithItsItemsAndProposedCases()
    {
        using var harness = new Harness();
        var rules = new List<WorkbenchRule> { Fx.Rule("readiness"), Fx.Rule("intake.first"), Fx.Rule("intake.unique") };
        harness.Render(rules);
        ReviewReadiness(harness);
        harness.FindAll(".wb-rule")[2].Click();
        harness.Open(WorkbenchTab.Read);
        RowButton(harness, 1, "Question");
        WriteNote(harness, "Why should phone win?");
        harness.Open(WorkbenchTab.Review);
        harness.Type("Your name", "  Reviewer One ");
        harness.Press("Export the review");

        harness.Exports.Should().ContainSingle();
        var files = harness.Exports[0].Files;
        files.Select(item => item.SuggestedName).Should().Equal("review-readiness-notes-v3.json", "review-intake-unique-v1.json");

        foreach (var file in files)
        {
            var parsed = RuleReviewJson.Parse(file.Json);
            parsed.IsValid.Should().BeTrue();
            parsed.Review.Should().BeEquivalentTo(file.Review);
            file.Review.Reviewer.Should().Be("Reviewer One");
            file.Review.CreatedAt.Should().Be(Fx.Moment);
        }

        var readiness = files[0].Review;
        readiness.RuleId.Should().Be("readiness-notes");
        readiness.RuleVersion.Should().Be(3);
        readiness.RuleContentHash.Should().Be(RuleReviewJson.ContentHash(Fx.TextOf("readiness")));
        readiness.Items.Select(item => (item.Target, item.State, item.Note)).Should().Equal(
            ("rule", ReviewState.Confirmed, string.Empty),
            ("row:low-score", ReviewState.NeedsFix, "The limit should be 50."),
            ("row:mid-score", ReviewState.Confirmed, string.Empty),
            ("golden:A low score at intake", ReviewState.Question, "Is a high score really no decision?"));
        var proposed = readiness.ProposedCases.Should().ContainSingle().Subject;
        proposed.Name.Should().Be("A score of 50");
        proposed.ExpectNoDecision.Should().BeFalse();
        proposed.ExpectedOutcomeJson.Should().Be("[{\"note\":\"mid-score\"}]");
        proposed.ExpectedRowId.Should().BeNull();
        proposed.Facts.Should().Equal(new Dictionary<string, DecisionValue>
        {
            ["stage"] = DecisionValue.Enum("Intake"),
            ["score"] = DecisionValue.Integer(50),
        });

        var unique = files[1].Review;
        unique.RuleContentHash.Should().Be(RuleReviewJson.ContentHash(Fx.TextOf("intake.unique")));
        unique.Items.Should().ContainSingle().Which.Should().Be(new ReviewItem("row:phone-any-urgency", ReviewState.Question, "Why should phone win?"));
        unique.ProposedCases.Should().BeEmpty();
    }

    [Fact]
    public void AProposedCaseThatPinsARowCarriesItsId()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("intake.first")]);
        harness.Open(WorkbenchTab.Tests);
        harness.Press("Add a case");
        harness.Type("Case name", "Phone first");
        harness.Type("Contact channel", "Phone");
        harness.Type("topic", "x");
        harness.Choose("urgent", "true");
        harness.Type("Age in days", "3");
        harness.Type("Expected result", "none");
        harness.Press("Save case");
        harness.Open(WorkbenchTab.Simulate);
        harness.Type("Contact channel", "Phone");
        harness.Type("topic", "x");
        harness.Choose("urgent", "true");
        harness.Type("Age in days", "3");
        harness.Press("Simulate");
        harness.Press("Save as a test case");
        harness.Type("Case name", "Pinned");
        harness.Press("Save case");
        harness.Open(WorkbenchTab.Review);
        harness.Type("Your name", "Reviewer One");
        harness.Press("Export the review");

        var cases = harness.Exports[0].Files[0].Review.ProposedCases;
        cases.Select(item => (item.Name, item.ExpectNoDecision, item.ExpectedRowId)).Should().Equal(
            ("Phone first", true, null),
            ("Pinned", false, "urgent-phone"));
        cases[1].ExpectedOutcomeJson.Should().Contain("\"Escalate\"");
    }

    [Fact]
    public void AnUnreviewedTargetWithNoNoteIsNotExportedButOneWithANoteIs()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        RowButton(harness, 0, "Confirm");
        RowButton(harness, 0, "Confirm");
        harness.Open(WorkbenchTab.Review);
        harness.ButtonByText("Export the review").HasAttribute("disabled").Should().BeTrue();

        harness.Open(WorkbenchTab.Read);
        RowButton(harness, 2, "Confirm");
        harness.Find(".wb-row:nth-of-type(3) button.wb-link").Click();
        WriteNote(harness, "Only a note.");
        RowButton(harness, 2, "Confirm");
        harness.Open(WorkbenchTab.Review);
        harness.Type("Your name", "Reviewer One");
        harness.Press("Export the review");

        harness.Exports[0].Files[0].Review.Items.Should().ContainSingle().Which.Should().Be(new ReviewItem("row:late-stage", ReviewState.Unreviewed, "Only a note."));
    }

    [Fact]
    public async Task TheSavedSentenceNamesThePlaceTheHostGave()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")], place: " the demo folder ");
        harness.Open(WorkbenchTab.Review);
        harness.Find(".wb-card-head .wb-rv").QuerySelectorAll("button")[0].Click();
        harness.Type("Your name", "Reviewer One");
        harness.Session.UnexportedRuleCount.Should().Be(1);

        harness.Press("Export the review");
        await Task.Yield();

        harness.Find(".wb-saved").TextContent.Should().Be("Review saved to the demo folder.");
        harness.Find(".wb-saved").GetAttribute("role").Should().Be("status");
        harness.Session.UnexportedRuleCount.Should().Be(0);
    }

    [Fact]
    public void TheSavedSentenceHasNoPlaceWhenTheHostReturnsNone()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")], place: null);
        harness.Open(WorkbenchTab.Review);
        harness.Find(".wb-card-head .wb-rv").QuerySelectorAll("button")[0].Click();
        harness.Type("Your name", "Reviewer One");
        harness.Press("Export the review");
        harness.Find(".wb-saved").TextContent.Should().Be("Review saved.");

        harness.Render([Fx.Rule("readiness", WorkbenchLocale.Es)], WorkbenchLocale.Es, place: "   ");
        harness.Press("Exportar la revisión");
        harness.Find(".wb-saved").TextContent.Should().Be("Revisión guardada.");
    }

    [Fact]
    public void ChangingAnythingAfterTheExportBringsTheCountBackAndHidesTheSentence()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness"), Fx.Rule("intake.unique")]);
        harness.Open(WorkbenchTab.Review);
        harness.Find(".wb-card-head .wb-rv").QuerySelectorAll("button")[0].Click();
        harness.Type("Your name", "Reviewer One");
        harness.Press("Export the review");
        harness.Session.UnexportedRuleCount.Should().Be(0);

        harness.Find(".wb-card-head .wb-rv").QuerySelectorAll("button")[2].Click();
        harness.Session.UnexportedRuleCount.Should().Be(1);
        harness.FindAll(".wb-saved").Should().BeEmpty();

        harness.Press("Export the review");
        harness.Session.UnexportedRuleCount.Should().Be(0);
        harness.FindAll(".wb-saved").Should().ContainSingle();

        harness.FindAll(".wb-rule")[1].Click();
        harness.Find(".wb-card-head .wb-rv").QuerySelectorAll("button")[1].Click();
        harness.Session.UnexportedRuleCount.Should().Be(1);
    }

    [Fact]
    public void TheReviewerNameIsKeptInTheSession()
    {
        using var harness = OpenReview(Fx.Rule("readiness"));
        harness.Type("Your name", "Reviewer One");
        harness.Session.Reviewer.Should().Be("Reviewer One");

        using var again = new Harness();
        again.Render([Fx.Rule("readiness")], session: harness.Session);
        again.Open(WorkbenchTab.Review);
        again.Input("Your name").GetAttribute("value").Should().Be("Reviewer One");
    }

    [Fact]
    public void ARefusalAddedToTheReviewTravelsWithItsEngineCodeButNeverShowsIt()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("intake.unique")]);
        harness.Open(WorkbenchTab.Simulate);
        harness.Type("channel", "Phone");
        harness.Type("topic", "x");
        harness.Choose("urgent", "true");
        harness.Type("age days", "3");
        harness.Press("Simulate");
        harness.Press("Add to the review");
        harness.Find("textarea").Change("Both rows match.");
        harness.Type("Your name", "Reviewer One");
        harness.Find(".wb-detail").TextContent.Should().NotContain("engine code");
        harness.Press("Export the review");

        var item = harness.Exports[0].Files[0].Review.Items.Should().ContainSingle().Subject;
        item.Target.Should().Be("rule");
        item.State.Should().Be(ReviewState.NeedsFix);
        item.Note.Should().Be("Both rows match.\n(engine code: decisions.evaluation.multiple_matches)");
        harness.Find(".wb-detail").TextContent.Should().NotContain("decisions.");
    }

    [Fact]
    public void ARefusalWithNoNoteTravelsWithTheCodeAlone()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("intake.unique")]);
        harness.Open(WorkbenchTab.Simulate);
        harness.Type("channel", "Phone");
        harness.Type("topic", "x");
        harness.Choose("urgent", "true");
        harness.Type("age days", "3");
        harness.Press("Simulate");
        harness.Press("Add to the review");
        harness.Type("Your name", "Reviewer One");
        harness.Press("Export the review");
        harness.Exports[0].Files[0].Review.Items[0].Note.Should().Be("(engine code: decisions.evaluation.multiple_matches)");
    }

    [Fact]
    public void MarkupInANoteOrACaseNameIsShownAsTextAndExportedAsTyped()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        RowButton(harness, 0, "Fix");
        WriteNote(harness, "<b>bold</b> & <script>x</script>");
        harness.Open(WorkbenchTab.Tests);
        harness.Press("Add a case");
        harness.Type("Case name", "<i>name</i>");
        harness.Type("Stage", "Intake");
        harness.Type("Score", "95");
        harness.FindAll(".wb-radios-stack input[type=checkbox]")[3].Change(true);
        harness.Press("Save case");
        harness.Open(WorkbenchTab.Review);

        var table = harness.Find(".wb-grid-review .wb-table");
        table.TextContent.Should().Contain("<b>bold</b> & <script>x</script>").And.Contain("<i>name</i>");
        table.QuerySelectorAll("b, i, script").Should().BeEmpty();
        harness.Type("Your name", "Reviewer One");
        harness.Press("Export the review");
        harness.Exports[0].Files[0].Review.Items[0].Note.Should().Be("<b>bold</b> & <script>x</script>");
    }

    [Fact]
    public void AnUnreadableRuleIsNotInTheExportEvenWhenItHadAReview()
    {
        using var harness = new Harness();
        var rule = Fx.Rule("readiness");
        harness.Render([rule]);
        RowButton(harness, 0, "Confirm");
        harness.Render([rule with { RuleText = null }], session: harness.Session);
        harness.FindAll("[role=tab]").Should().BeEmpty();
        harness.Session.UnexportedRuleCount.Should().Be(1);
    }

    [Fact]
    public void ANoteIsKeptWhenTheNoteIsEditedBackToEmptyAndStateIsUnreviewed()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        RowButton(harness, 0, "Fix");
        WriteNote(harness, "Text");
        RowButton(harness, 0, "Fix");
        harness.FindAll("textarea").Should().ContainSingle();
        WriteNote(harness, "");
        harness.Open(WorkbenchTab.Review);
        harness.ButtonByText("Export the review").HasAttribute("disabled").Should().BeTrue();
    }
}

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class ListTests
{
    private static string Names(Harness harness) =>
        string.Join("|", harness.FindAll(".wb-rule-name").Select(item => item.TextContent));

    [Fact]
    public void RulesKeepTheOrderGivenAndAHeadingIsDrawnWhenTheGroupChanges()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());

        harness.FindAll("h3.wb-group").Select(item => item.TextContent).Should().Equal("Scoring", "Intake");
        Names(harness).Should().Be("Readiness notes|Intake routing|Intake routing|Unique intake");
        harness.FindAll("ul.wb-rules").Select(item => item.GetAttribute("aria-labelledby")).Should().OnlyContain(id => id != null);
    }

    [Fact]
    public void ARunOfRulesWithNoGroupGetsNoHeading()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness"), Fx.Rule("intake.unique")]);

        harness.FindAll("h3.wb-group").Should().BeEmpty();
        harness.FindAll("ul.wb-rules").Should().ContainSingle().Which.HasAttribute("aria-labelledby").Should().BeFalse();
    }

    [Fact]
    public void TheCountLineGivesRulesApprovedAndDrafts()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());
        harness.Find(".wb-list-panel .wb-meta").TextContent.Should().Be("4 rules · 1 approved · 3 drafts");

        harness.Render([Fx.Rule("readiness")]);
        harness.Find(".wb-list-panel .wb-meta").TextContent.Should().Be("1 rule · 0 approved · 1 draft");
    }

    [Fact]
    public void TheCountLineIsInSpanishWhenTheLocaleIs()
    {
        using var harness = new Harness();
        harness.Render(Fx.All(WorkbenchLocale.Es), WorkbenchLocale.Es);
        harness.Find(".wb-list-panel .wb-meta").TextContent.Should().Be("4 reglas · 1 aprobada · 3 en borrador");
        harness.Find(".wb-root").GetAttribute("lang").Should().Be("es");
    }

    [Fact]
    public void EveryRuleShowsItsStatePill()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());

        var pills = harness.FindAll(".wb-rule .wb-pill").Select(item => item.TextContent + ":" + item.ClassList.Last()).ToList();
        pills.Should().Equal("Draft:wb-pill-warn", "Approved:wb-pill-ok", "Draft:wb-pill-warn", "Draft:wb-pill-warn");
    }

    [Fact]
    public void EveryRuleShowsRowsCasesAndReview()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());
        harness.FindAll(".wb-rule-s").Select(item => item.TextContent).Should().Equal(
            "3 rows · 3 of 3 cases pass · not reviewed",
            "8 rows · 9 of 9 cases pass · not reviewed",
            "8 rows · 9 of 9 cases pass · not reviewed",
            "2 rows · 3 of 3 cases pass · not reviewed");

        harness.Render(Fx.All(WorkbenchLocale.Es), WorkbenchLocale.Es);
        harness.FindAll(".wb-rule-s")[0].TextContent.Should().Be("3 filas · 3 de 3 casos pasan · sin revisar");
    }

    [Fact]
    public void AFailingCaseMakesTheTestPartBold()
    {
        using var harness = new Harness();
        harness.Render([Fx.FromText("readiness", Fx.FailingReadiness())]);

        var summary = harness.Find(".wb-rule-s");
        summary.TextContent.Should().Be("3 rows · 1 of 3 cases fails · not reviewed");
        summary.QuerySelector("strong")!.TextContent.Should().Be("1 of 3 cases fails");
    }

    [Fact]
    public void SeveralFailingCasesAreCountedAndASingleFailingCaseIsSaid()
    {
        using var harness = new Harness();
        harness.Render([Fx.FromText("readiness", Fx.TwoFailingReadiness())]);
        harness.Find(".wb-rule-s strong").TextContent.Should().Be("2 of 3 cases fail");

        harness.Render([Fx.FromText("readiness", Fx.OneFailingCase())]);
        harness.Find(".wb-rule-s strong").TextContent.Should().Be("the case fails");
    }

    [Fact]
    public void ARuleWithNoCasesSaysSoAndOneWithAllRowsReviewedIsReviewed()
    {
        using var harness = new Harness();
        harness.Render([Fx.FromText("readiness", Fx.WithoutCases())]);
        harness.Find(".wb-rule-s").TextContent.Should().Be("3 rows · no test cases · not reviewed");

        harness.Press("Confirm");
        harness.Find(".wb-rule-s").TextContent.Should().EndWith("review in progress");
        foreach (var index in new[] { 1, 2 })
        {
            harness.FindAll("[role=tabpanel] .wb-seg-part").Where(item => item.TextContent == "Confirm").ToList()[0].Click();
            _ = index;
        }

        harness.Find(".wb-rule-s").TextContent.Should().EndWith("reviewed");
    }

    [Fact]
    public void QuestionsAndFixesAreCountedInTheListLine()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        harness.FindAll("[role=tabpanel] .wb-seg-part").Where(item => item.TextContent == "Fix").ToList()[0].Click();
        harness.FindAll("[role=tabpanel] .wb-seg-part").Where(item => item.TextContent == "Question").ToList()[1].Click();
        harness.Find(".wb-rule-s").TextContent.Should().Be("3 rows · 3 of 3 cases pass · 1 question, 1 to fix");
    }

    [Fact]
    public void TheFirstRuleIsSelectedAndOnlyTheSelectedOneIsCurrent()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());

        var current = harness.FindAll(".wb-rule").Select(item => item.GetAttribute("aria-current")).ToList();
        current.Should().Equal("true", null, null, null);
        harness.Find(".wb-detail h1").TextContent.Should().Be("Readiness notes");
    }

    [Fact]
    public void ClickingARuleSelectsItAndTheSessionRemembersIt()
    {
        using var harness = new Harness();
        var rules = Fx.All();
        harness.Render(rules);

        harness.FindAll(".wb-rule")[3].Click();

        harness.Session.SelectedRuleKey.Should().Be("intake.unique");
        harness.FindAll(".wb-rule").Select(item => item.GetAttribute("aria-current")).Should().Equal(null, null, null, "true");
        harness.Find(".wb-detail h1").TextContent.Should().Be("Unique intake");
    }

    [Fact]
    public void TheDownAndUpKeysMoveTheSelectionAndStopAtTheEnds()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());

        harness.FindAll(".wb-rule")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        harness.Session.SelectedRuleKey.Should().Be("intake.first");
        harness.FindAll(".wb-rule")[1].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        harness.Session.SelectedRuleKey.Should().Be("intake.first.v2");
        harness.FindAll(".wb-rule")[2].KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        harness.Session.SelectedRuleKey.Should().Be("intake.first");
        harness.FindAll(".wb-rule")[1].KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        harness.FindAll(".wb-rule")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        harness.Session.SelectedRuleKey.Should().Be("readiness");
        harness.FindAll(".wb-rule")[0].KeyDown(new KeyboardEventArgs { Key = "Enter" });
        harness.Session.SelectedRuleKey.Should().Be("readiness");

        harness.Ctx.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Should().NotBeEmpty();
    }

    [Fact]
    public void TheDownKeyAtTheLastRuleChangesNothing()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());
        harness.Session.SelectedRuleKey = "intake.unique";
        harness.FindAll(".wb-rule")[3].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        harness.Session.SelectedRuleKey.Should().Be("intake.unique");
    }

    [Fact]
    public void TheNarrowSelectListsEveryRuleAndSelectsOne()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());

        var select = harness.Find(".wb-list-select");
        select.QuerySelectorAll("option").Select(item => item.TextContent).Should().Equal("Readiness notes", "Intake routing", "Intake routing", "Unique intake");
        select.GetAttribute("aria-label").Should().Be("Rule list");

        select.Change("3");
        harness.Session.SelectedRuleKey.Should().Be("intake.unique");
        harness.Find(".wb-list-select").Change("9");
        harness.Find(".wb-list-select").Change("x");
        harness.Session.SelectedRuleKey.Should().Be("intake.unique");
    }

    [Fact]
    public void WhileTheRulesLoadTheListShowsSkeletonsAndTheDetailSaysSo()
    {
        using var harness = new Harness();
        harness.Render(null);

        harness.FindAll(".wb-skeleton").Should().HaveCount(3);
        harness.Find(".wb-detail [role=status]").TextContent.Should().Be("Loading rules…");
        harness.FindAll(".wb-list-panel .wb-meta").Should().BeEmpty();
    }

    [Fact]
    public void WithNoRulesTheListSaysThereAreNone()
    {
        using var harness = new Harness();
        harness.Render([]);

        harness.Find(".wb-detail").TextContent.Trim().Should().Be("No rules are loaded on this computer.");
        harness.Find(".wb-list-panel .wb-meta").TextContent.Should().Be("0 rules · 0 approved · 0 drafts");
    }

    [Fact]
    public void ARuleThatCouldNotBeReadIsListedByItsFallbackTitleAndTheDetailSaysSo()
    {
        using var harness = new Harness();
        harness.Render([new WorkbenchRule { Key = "broken", FallbackTitle = "Folder entry" }, Fx.Rule("readiness")]);

        harness.FindAll(".wb-rule-name")[0].TextContent.Should().Be("Folder entry");
        harness.FindAll(".wb-rule")[0].QuerySelector(".wb-pill")!.TextContent.Should().Be("Could not be read");
        harness.FindAll(".wb-rule")[0].QuerySelector(".wb-rule-s").Should().BeNull();

        harness.FindAll(".wb-rule")[0].Click();
        harness.Find(".wb-detail h1").TextContent.Should().Be("Folder entry");
        harness.Find(".wb-detail [role=status]").TextContent.Should().Be("This rule could not be read on this computer.");
        harness.FindAll("[role=tab]").Should().BeEmpty();
    }

    [Fact]
    public void ARuleWithNoTextIsTreatedAsOneThatCouldNotBeRead()
    {
        var rule = Fx.Rule("readiness") with { RuleText = null, FallbackTitle = null };
        using var harness = new Harness();
        harness.Render([rule]);

        harness.Find(".wb-rule-name").TextContent.Should().Be("Readiness notes");
        harness.Find(".wb-rule .wb-pill").TextContent.Should().Be("Could not be read");
        harness.Find(".wb-detail [role=status]").TextContent.Should().Be("This rule could not be read on this computer.");

        harness.Render([new WorkbenchRule { Key = "nameless" }]);
        harness.Find(".wb-rule-name").TextContent.Should().Be("Untitled rule");
    }

    [Fact]
    public void AnUnreadableRuleIsLeftOutOfTheExport()
    {
        using var harness = new Harness();
        harness.Render([new WorkbenchRule { Key = "broken", FallbackTitle = "Broken" }, Fx.Rule("readiness") with { RuleText = null }]);
        harness.Session.SelectedTab = WorkbenchTab.Review;
        harness.Session.SelectedRuleKey = "broken";
        harness.FindAll("[role=tab]").Should().BeEmpty();
        harness.Session.UnexportedRuleCount.Should().Be(0);
    }

    [Fact]
    public void ARuleWithoutLabelsIsTitledFromItsIdentifier()
    {
        var plain = Fx.Rule("readiness") with { Labels = RuleLabels.Empty };
        using var harness = new Harness();
        harness.Render([plain]);
        harness.Find(".wb-rule-name").TextContent.Should().Be("readiness notes");
    }
}

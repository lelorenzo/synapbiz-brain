using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>Starting, viewing and discarding a draft: the button, the draft bar on every tab, the view switch and the discard dialog.</summary>
public sealed class DraftBarTests
{
    private static bool IsEs(WorkbenchLocale locale) => locale == WorkbenchLocale.Es;

    private static string Start(WorkbenchLocale locale) => IsEs(locale) ? "Proponer cambios" : "Propose changes";

    private static string Discard(WorkbenchLocale locale) => IsEs(locale) ? "Descartar mi borrador" : "Discard my draft";

    private static IElement HeaderButton(Harness harness) => harness.Find(".wb-head button");

    private static Harness Started(WorkbenchLocale locale = WorkbenchLocale.En, string name = "intake.first", WorkbenchRule? rule = null)
    {
        var harness = new Harness();
        harness.Render([rule ?? Fx.Editable(name, locale)], locale);
        HeaderButton(harness).Click();
        return harness;
    }

    /// <summary>Applies one change to the draft of the only rule, as a form would.</summary>
    internal static void Change(Harness harness, string key = "intake.first", string row = "billing-topic", string route = "Manual")
    {
        var host = harness.Cut.Instance.Rules!.Single(item => item.Key == key);
        var draft = harness.Session.Work(key).Draft!;
        DraftEngine.Apply(host, draft, [.. draft.Steps, new DraftStep(StepKind.Row, row, [new SetOutcome(row, "route", DecisionValue.Enum(route))])]).IsOk.Should().BeTrue();
        harness.Session.Touch();
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void ARuleThatTheHostCannotLoadDraftsOfOffersNoButton(WorkbenchLocale locale)
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("intake.first", locale)], locale);

        harness.FindAll(".wb-head button").Should().BeEmpty();
        harness.Cut.Markup.Should().NotContain(Start(locale));
        harness.FindAll(".wb-draftbar").Should().BeEmpty();
    }

    [Fact]
    public void ARuleWithoutItsTextOrItsLoaderOffersNoButton()
    {
        using var harness = new Harness();
        var rule = Fx.Editable("intake.first");
        harness.Render([rule with { LoadDraft = null }]);
        harness.FindAll(".wb-head button").Should().BeEmpty();
        harness.Dispose();
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void TheHeaderButtonStartsADraftAndTheBarAppearsWithItsPillItsSentenceAndTheSwitch(WorkbenchLocale locale)
    {
        using var harness = new Harness();
        harness.Render([Fx.Editable("intake.first", locale)], locale);
        HeaderButton(harness).TextContent.Should().Be(Start(locale));
        HeaderButton(harness).ClassList.Should().Contain("wb-btn-secondary");
        harness.FindAll(".wb-draftbar").Should().BeEmpty();

        HeaderButton(harness).Click();

        var bar = harness.Find(".wb-draftbar");
        bar.GetAttribute("role").Should().Be("note");
        bar.QuerySelector(".wb-pill-draft")!.TextContent.Should().Be(IsEs(locale) ? "Borrador de cambios" : "Draft changes");
        Harness.Normalise(bar.QuerySelector(".wb-draftbar-t")!.TextContent).Should().Be(IsEs(locale)
            ? "Está viendo su borrador. Nada de esto está en uso: la regla actual sigue igual hasta que se apruebe e instale una versión nueva."
            : "You are viewing your draft. None of this is in use: the current rule stays as it is until a new version is approved and installed.");
        bar.QuerySelector(".wb-draftbar-t strong")!.TextContent.Should().Be(IsEs(locale) ? "Está viendo su borrador." : "You are viewing your draft.");
        var parts = bar.QuerySelectorAll(".wb-switch button");
        string.Join("|", parts.Select(item => item.TextContent)).Should().Be(IsEs(locale) ? "Regla actual|Su borrador" : "Current rule|Your draft");
        parts.Select(item => item.GetAttribute("aria-pressed")).Should().Equal("false", "true");
        bar.QuerySelector(".wb-switch")!.GetAttribute("role").Should().Be("group");
        HeaderButton(harness).TextContent.Should().Be(Discard(locale));
        harness.Session.Work("intake.first").Draft.Should().NotBeNull();
    }

    [Fact]
    public void TheBarSitsBetweenTheHeaderAndTheTabsAndIsNeverDismissable()
    {
        using var harness = Started();

        var detail = harness.Find(".wb-detail");
        var classes = detail.Children.Select(item => item.ClassName ?? string.Empty).ToList();
        classes.Select(item => item.Split(' ')[0]).Take(3).Should().Equal("wb-ph", "wb-draftbar", "wb-tabs");
        harness.Find(".wb-draftbar").QuerySelectorAll("button").Should().OnlyContain(button => button.ClassList.Contains("wb-switch-part"));
    }

    [Theory]
    [InlineData(WorkbenchTab.Read)]
    [InlineData(WorkbenchTab.Simulate)]
    [InlineData(WorkbenchTab.Tests)]
    [InlineData(WorkbenchTab.Review)]
    public void TheBarIsOnEveryTab(WorkbenchTab tab)
    {
        using var harness = Started();
        harness.Open(tab);
        harness.FindAll(".wb-draftbar").Should().ContainSingle();
    }

    [Fact]
    public void ADraftWithNoChangeIsDiscardedAtOnceAndTheButtonOffersToStartAgain()
    {
        using var harness = Started();

        HeaderButton(harness).Click();

        harness.FindAll(".wb-draftbar").Should().BeEmpty();
        harness.FindAll("[role=dialog]").Should().BeEmpty();
        harness.Session.Work("intake.first").Draft.Should().BeNull();
        HeaderButton(harness).TextContent.Should().Be("Propose changes");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void TheSwitchShowsTheRuleAsItIsWithItsReviewControlsAndTheNumberOfChanges(WorkbenchLocale locale)
    {
        using var harness = Started(locale);
        Change(harness);
        harness.FindAll(".wb-row .wb-rv").Should().BeEmpty("the draft view has no review controls");

        harness.FindAll(".wb-switch button")[0].Click();

        harness.FindAll(".wb-row .wb-rv").Should().NotBeEmpty("the rule view has the review controls exactly as before");
        Harness.Normalise(harness.Find(".wb-draftbar-t").TextContent).Should().Be(IsEs(locale)
            ? "Está viendo la regla actual. Su borrador tiene 1 cambio."
            : "You are viewing the current rule. Your draft has 1 change.");
        harness.FindAll(".wb-switch button").Select(item => item.GetAttribute("aria-pressed")).Should().Equal("true", "false");

        harness.FindAll(".wb-switch button")[1].Click();
        harness.FindAll(".wb-row .wb-rv").Should().BeEmpty();
        harness.FindAll(".wb-switch button")[1].Click();
        harness.FindAll(".wb-row .wb-rv").Should().BeEmpty("pressing the part already shown changes nothing");
    }

    [Fact]
    public void TheRuleViewSaysManyChangesInThePlural()
    {
        using var harness = Started();
        Change(harness);
        Change(harness, row: "fresh-request");
        harness.FindAll(".wb-switch button")[0].Click();

        Harness.Normalise(harness.Find(".wb-draftbar-t").TextContent).Should().Be("You are viewing the current rule. Your draft has 2 changes.");
    }

    [Fact]
    public void TheListSaysWhichRuleHasProposedChangesInBold()
    {
        using var harness = new Harness();
        harness.Render([Fx.Editable("intake.first"), Fx.Rule("readiness")]);
        harness.Find(".wb-detail .wb-head button").Click();

        var summaries = harness.FindAll(".wb-rule-s");
        summaries[0].QuerySelector("strong")!.TextContent.Should().Be("with proposed changes");
        summaries[0].TextContent.Should().NotContain("reviewed").And.NotContain("not reviewed");
        summaries[1].TextContent.Should().Contain("not reviewed");
    }

    [Fact]
    public void SimulatingOnTheDraftUsesTheDraftAndTheBandSaysSo()
    {
        using var harness = Started();
        Change(harness, row: "urgent-phone", route: "Manual");
        harness.Open(WorkbenchTab.Simulate);
        Harness.Normalise(harness.Find(".wb-band").TextContent).Should().Be(
            "SimulationSimulating your draft, not the current rule. Simulated with example data. This is not a real case, and nothing changes.");

        harness.Find("select[id$=-load]").Change("0");
        harness.Press("Simulate");
        harness.Find(".wb-result .wb-h2").TextContent.Should().Contain("manual review");

        harness.FindAll(".wb-switch button")[0].Click();
        harness.FindAll(".wb-result").Should().BeEmpty("a result of the draft is not left on the rule view");
        Harness.Normalise(harness.Find(".wb-band").TextContent).Should().NotContain("your draft");
        harness.Find("select[id$=-load]").Change("0");
        harness.Press("Simulate");
        harness.Find(".wb-result .wb-h2").TextContent.Should().Contain("escalation");
    }

    [Fact]
    public void TheTestsOfTheDraftRunOnTheDraftAndTheBandSaysSo()
    {
        using var harness = Started();
        Change(harness, row: "urgent-phone", route: "Manual");
        harness.Open(WorkbenchTab.Tests);

        Harness.Normalise(harness.Find(".wb-band").TextContent).Should().Contain("Testing your draft, not the current rule.");
        harness.FindAll(".wb-table tbody tr").Should().NotBeEmpty();
        harness.PanelText.Should().Contain("manual review");
    }

    [Fact]
    public void TheReviewTabIsAlwaysAboutTheRuleAsItIs()
    {
        using var harness = Started();
        Change(harness, row: "urgent-phone", route: "Manual");
        harness.Open(WorkbenchTab.Review);

        harness.PanelText.Should().Contain("Your review of this rule").And.Contain("0 of 8 rows reviewed");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void ADraftWithChangesAsksBeforeItIsDiscardedAndKeepEditingIsTheDefault(WorkbenchLocale locale)
    {
        using var harness = Started(locale);
        Change(harness);
        Change(harness, row: "fresh-request");

        HeaderButton(harness).Click();

        var dialog = harness.Find("[role=dialog]");
        dialog.GetAttribute("aria-modal").Should().Be("true");
        dialog.GetAttribute("aria-labelledby").Should().Be(dialog.QuerySelector("h2")!.Id);
        dialog.GetAttribute("aria-describedby").Should().Be(dialog.QuerySelector("p")!.Id);
        dialog.QuerySelector("h2")!.TextContent.Should().Be(IsEs(locale) ? "¿Descartar su borrador de esta regla?" : "Discard your draft of this rule?");
        dialog.QuerySelector("p")!.TextContent.Should().Be(IsEs(locale)
            ? "Se pierden 2 cambios de filas y 0 decisiones sobre casos de prueba. La regla actual no cambia."
            : "2 row changes and 0 test case decisions are lost. The current rule does not change.");
        var buttons = dialog.QuerySelectorAll("button");
        string.Join("|", buttons.Select(item => item.TextContent)).Should().Be(IsEs(locale) ? "Descartar el borrador|Seguir editando" : "Discard the draft|Keep editing");
        buttons[0].ClassList.Should().Contain("wb-btn-secondary");
        buttons[1].ClassList.Should().Contain("wb-btn-primary");
        harness.IsFocused(harness.Find("[role=dialog] .wb-btn-primary")).Should().BeTrue();
        harness.Find(".wb-main").HasAttribute("inert").Should().BeTrue("the rest of the component is inert while the dialog is open");
        harness.Find(".wb-scrim").Should().NotBeNull();
    }

    [Fact]
    public void TheDialogCountsOneChangeAndOneDecisionInTheSingular()
    {
        using var harness = Started();
        Change(harness);
        harness.Session.Work("intake.first").Draft!.Steps.Add(new DraftStep(StepKind.CaseAccept, "x", []));

        HeaderButton(harness).Click();

        harness.Find("[role=dialog] p").TextContent.Should().Be("1 row change and 1 test case decision are lost. The current rule does not change.");
    }

    [Fact]
    public void KeepEditingAndEscapeKeepTheDraftAndBringFocusBackToTheButton()
    {
        using var harness = Started();
        Change(harness);

        HeaderButton(harness).Click();
        var asked = harness.FocusCount;
        harness.Find("[role=dialog] .wb-btn-primary").Click();
        harness.FindAll("[role=dialog]").Should().BeEmpty();
        harness.Find(".wb-main").HasAttribute("inert").Should().BeFalse();
        harness.Session.Work("intake.first").Draft.Should().NotBeNull();
        harness.FocusCount.Should().BeGreaterThan(asked, "focus goes back to the button");

        HeaderButton(harness).Click();
        var reopened = harness.FocusCount;
        harness.Find("[role=dialog]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "a" });
        harness.FindAll("[role=dialog]").Should().ContainSingle();
        harness.Find("[role=dialog]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        harness.FindAll("[role=dialog]").Should().BeEmpty();
        harness.Session.Work("intake.first").Draft.Should().NotBeNull();
        harness.FocusCount.Should().BeGreaterThan(reopened);
    }

    [Fact]
    public void DiscardingFromTheDialogDropsTheDraftAndLeavesTheRuleAsItWas()
    {
        using var harness = Started();
        Change(harness);
        HeaderButton(harness).Click();
        var asked = harness.FocusCount;

        harness.Find("[role=dialog] .wb-btn-secondary").Click();

        harness.FindAll("[role=dialog]").Should().BeEmpty();
        harness.FindAll(".wb-draftbar").Should().BeEmpty();
        harness.Session.Work("intake.first").Draft.Should().BeNull();
        harness.Session.UnexportedDraftCount.Should().Be(0);
        HeaderButton(harness).TextContent.Should().Be("Propose changes");
        harness.FocusCount.Should().BeGreaterThan(asked);
    }

    [Fact]
    public void SelectingAnotherRuleClosesTheDialogAndTheDraftIsKeptWhenYouLeaveTheRule()
    {
        using var harness = new Harness();
        harness.Render([Fx.Editable("intake.first"), Fx.Editable("readiness")]);
        harness.Find(".wb-detail .wb-head button").Click();
        Change(harness);
        harness.Find(".wb-detail .wb-head button").Click();
        harness.FindAll("[role=dialog]").Should().ContainSingle();

        harness.FindAll(".wb-rule")[1].Click();

        harness.FindAll("[role=dialog]").Should().BeEmpty();
        harness.FindAll(".wb-draftbar").Should().BeEmpty("the other rule has no draft");
        harness.FindAll(".wb-rule")[0].Click();
        harness.FindAll(".wb-draftbar").Should().ContainSingle("leaving the rule keeps its draft");
    }

    [Fact]
    public void ClearingTheSessionDropsTheDrafts()
    {
        using var harness = Started();
        Change(harness);
        harness.Session.UnexportedDraftCount.Should().Be(1);

        harness.Session.Clear();

        harness.FindAll(".wb-draftbar").Should().BeEmpty();
        harness.Session.UnexportedDraftCount.Should().Be(0);
    }

    [Fact]
    public void ADraftWhoseBaseChangedUnderneathSaysSoInBothViews()
    {
        var original = Fx.Editable("intake.first");
        using var harness = new Harness();
        harness.Render([original]);
        HeaderButton(harness).Click();
        Change(harness);

        var changed = Fx.EditableText("intake.first", Fx.TextOf("intake.first") + "\n");
        harness.Render([changed], session: harness.Session);

        Harness.Normalise(harness.Find(".wb-draftbar-t").TextContent).Should().Be("The rule changed since you started this draft. Export what you have or discard it.");
        harness.FindAll(".wb-switch button")[0].Click();
        Harness.Normalise(harness.Find(".wb-draftbar-t").TextContent).Should().Be("The rule changed since you started this draft. Export what you have or discard it.");
    }

    [Fact]
    public void ADraftTheLoaderCannotReadShowsTheReasonsAndDisablesSimulateAndTests()
    {
        var host = Fx.Editable("intake.first") with { LoadDraft = _ => Synapbiz.Brain.Spec.BrainRuleLoader.Load("{}", []) };
        using var harness = Started(rule: host);

        var box = harness.Find(".wb-refusal");
        box.GetAttribute("role").Should().Be("alert");
        box.QuerySelector("h2")!.TextContent.Should().Be("This draft cannot be read");
        box.QuerySelectorAll("li").Should().NotBeEmpty();
        box.TextContent.Should().NotContain("spec.").And.NotContain("decisions.");
        var buttons = box.QuerySelectorAll("button");
        buttons.Select(item => item.TextContent).Should().Equal("Undo the last change", "Discard my draft");
        buttons[0].HasAttribute("disabled").Should().BeTrue();

        harness.Open(WorkbenchTab.Simulate);
        harness.PanelText.Should().Be("Fix the draft to continue.");
        harness.Open(WorkbenchTab.Tests);
        harness.PanelText.Should().Be("Fix the draft to continue.");

        harness.Open(WorkbenchTab.Read);
        harness.FindAll(".wb-refusal button")[1].Click();
        harness.FindAll(".wb-draftbar").Should().BeEmpty();
    }
}

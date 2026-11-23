using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class ReadTests
{
    [Theory]
    [InlineData("intake.first", WorkbenchLocale.En, "This rule decides in this order. The first row that matches wins.")]
    [InlineData("intake.unique", WorkbenchLocale.En, "Only one row can match at a time.")]
    [InlineData("readiness", WorkbenchLocale.En, "Every row that matches applies.")]
    [InlineData("intake.first", WorkbenchLocale.Es, "Esta regla decide en este orden. Gana la primera fila que se cumple.")]
    [InlineData("intake.unique", WorkbenchLocale.Es, "Solo una fila puede cumplirse a la vez.")]
    [InlineData("readiness", WorkbenchLocale.Es, "Se aplican todas las filas que se cumplen.")]
    public void TheLeadSaysHowTheHitPolicyDecides(string name, WorkbenchLocale locale, string lead)
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule(name, locale)], locale);
        harness.Find("[role=tabpanel] .wb-h2").TextContent.Should().Be(lead);
    }

    [Theory]
    [InlineData("readiness", WorkbenchLocale.En)]
    [InlineData("intake.first", WorkbenchLocale.En)]
    [InlineData("intake.unique", WorkbenchLocale.En)]
    [InlineData("readiness", WorkbenchLocale.Es)]
    [InlineData("intake.first", WorkbenchLocale.Es)]
    [InlineData("intake.unique", WorkbenchLocale.Es)]
    public void EverySentenceIsTheOneTheHeadlessLibraryWrites(string name, WorkbenchLocale locale)
    {
        var rule = Fx.Rule(name, locale);
        var view = RuleDescriber.Describe(rule.Rule!, locale, rule.Labels);
        using var harness = new Harness();
        harness.Render([rule], locale);

        var shown = harness.FindAll(".wb-row .wb-sent").Select(item => item.TextContent).ToList();
        shown.Take(view.Rows.Count).Should().Equal(view.Rows.Select(row => row.Sentence));
        shown.Should().HaveCount(view.Rows.Count + 1);
        harness.FindAll(".wb-row:not(.wb-row-default) .wb-num").Select(item => item.TextContent)
            .Should().Equal(Enumerable.Range(1, view.Rows.Count).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void TheDefaultRowIsDrawnApartWithItsSentence()
    {
        var rule = Fx.Rule("intake.first");
        var view = RuleDescriber.Describe(rule.Rule!, WorkbenchLocale.En, rule.Labels);
        using var harness = new Harness();
        harness.Render([rule]);

        var block = harness.Find(".wb-row-default");
        block.QuerySelector(".wb-num")!.TextContent.Should().Be("–");
        block.QuerySelector(".wb-sent")!.TextContent.Should().Be(view.DefaultRow!.Sentence);
        block.QuerySelector(".wb-sub")!.TextContent.Should().Be("Text shown: «Everything else goes to the general queue.»");
        block.QuerySelectorAll("button").Should().BeEmpty();
    }

    [Fact]
    public void ARuleWithNoDefaultRowSaysTheRuleDoesNotDecideOtherwise()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        harness.Find(".wb-row-default .wb-sent").TextContent.Should().Be("Otherwise, the rule does not decide.");

        harness.Render([Fx.Rule("readiness", WorkbenchLocale.Es)], WorkbenchLocale.Es);
        harness.Find(".wb-row-default .wb-sent").TextContent.Should().Be("En cualquier otro caso, la regla no decide.");
    }

    [Fact]
    public void ARowShowsItsTitleAndTheTextTheHostGaveForIt()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("intake.first")]);

        var first = harness.FindAll(".wb-row")[0];
        first.QuerySelector(".wb-row-title")!.TextContent.Should().Be("Urgent calls");
        first.QuerySelector(".wb-sub")!.TextContent.Should().Be("Text shown: «Handled first by phone.»");
        harness.FindAll(".wb-row")[1].QuerySelector(".wb-sub").Should().BeNull();

        harness.Render([Fx.Rule("intake.first", WorkbenchLocale.Es)], WorkbenchLocale.Es);
        harness.FindAll(".wb-row")[0].QuerySelector(".wb-sub")!.TextContent.Should().Be("Texto que se muestra: «Se atiende primero por teléfono.»");
    }

    [Fact]
    public void TheDataTheRuleUsesIsListedWithItsTypeAndWhetherItIsRequired()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("intake.first")]);

        harness.FindAll("[role=tabpanel] .wb-h2")[1].TextContent.Should().Be("Data it uses");
        var terms = harness.FindAll(".wb-kv dt").Select(item => item.TextContent).ToList();
        var details = harness.FindAll(".wb-kv dd").Select(item => item.TextContent).ToList();
        terms.Should().Equal("Contact channel", "topic", "urgent", "Age in days", "rating", "amount", "received on", "account");
        details.Should().Equal(
            "One option: the website, the phone line, email · required",
            "Text · required",
            "Yes or no · required",
            "Whole number · required",
            "Number · optional",
            "Amount · optional",
            "Date · optional",
            "Reference · optional");
    }

    [Fact]
    public void TheHeaderGivesTitleStateVersionAndTheHostLines()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness", state: RuleApprovalState.Approved, stateLine: "Approved on a fixed date.", sourceLine: "From the demo folder.")]);

        harness.Find(".wb-detail h1").TextContent.Should().Be("Readiness notes");
        harness.Find(".wb-detail .wb-head .wb-pill").TextContent.Should().Be("Approved");
        harness.Find(".wb-detail .wb-head .wb-meta").TextContent.Should().Be("Version 3");
        harness.FindAll(".wb-detail .wb-ph > .wb-meta")[0].TextContent.Should().Be("Approved on a fixed date. From the demo folder.");
        harness.FindAll(".wb-detail .wb-ph > .wb-meta")[1].TextContent.Should().Be("Rules are not approved or activated from this screen.");
    }

    [Fact]
    public void TheDefaultStateLinesAreUsedWhenTheHostGivesNone()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness"), Fx.Rule("intake.first", state: RuleApprovalState.Approved)]);
        harness.FindAll(".wb-detail .wb-ph > .wb-meta")[0].TextContent.Should().Be("Draft: approval is pending.");
        harness.FindAll(".wb-rule")[1].Click();
        harness.FindAll(".wb-detail .wb-ph > .wb-meta")[0].TextContent.Should().Be("Approved.");
    }

    [Fact]
    public void EveryRowCanBeConfirmedAndTheControlTogglesBackToNotReviewed()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);

        var group = harness.FindAll(".wb-rv")[0];
        group.GetAttribute("role").Should().Be("group");
        group.GetAttribute("aria-label").Should().Be("Review of row 1");
        group.QuerySelectorAll("button").Select(item => item.TextContent + "=" + item.GetAttribute("aria-pressed"))
            .Should().Equal("Confirm=false", "Fix=false", "Question=false");

        group.QuerySelectorAll("button")[0].Click();
        harness.FindAll(".wb-rv")[0].QuerySelectorAll("button").Select(item => item.TextContent + "=" + item.GetAttribute("aria-pressed"))
            .Should().Equal("Confirmed=true", "Fix=false", "Question=false");

        harness.FindAll(".wb-rv")[0].QuerySelectorAll("button")[0].Click();
        harness.FindAll(".wb-rv")[0].QuerySelectorAll("button").Select(item => item.TextContent + "=" + item.GetAttribute("aria-pressed"))
            .Should().Equal("Confirm=false", "Fix=false", "Question=false");
    }

    [Theory]
    [InlineData(1, "Fix")]
    [InlineData(2, "Question")]
    public void FixAndQuestionOpenTheNoteAndShowTheirStateWord(int index, string pressed)
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        harness.FindAll(".wb-rv")[0].QuerySelectorAll("button")[index].Click();

        harness.FindAll(".wb-rv")[0].QuerySelectorAll("button")[index].GetAttribute("aria-pressed").Should().Be("true");
        harness.FindAll(".wb-rv")[0].QuerySelectorAll("button")[index].TextContent.Should().Be(pressed);
        var label = harness.Find("label.wb-label");
        label.TextContent.Should().Be("Note for the rule's maintainer");
        harness.Find("#" + label.GetAttribute("for")).TagName.Should().Be("TEXTAREA");
        harness.Ctx.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Should().BeEmpty();
    }

    [Fact]
    public void AConfirmedRowOffersToAddANoteAndAFilledNoteStaysOpen()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        harness.FindAll(".wb-rv")[0].QuerySelectorAll("button")[0].Click();

        harness.Find("button.wb-link").TextContent.Should().Be("Add a note");
        harness.Find("button.wb-link").Click();
        harness.FindAll("textarea").Should().ContainSingle();
        harness.Ctx.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Should().NotBeEmpty();

        harness.Find("textarea").Change("The limit looks right.");
        harness.FindAll(".wb-rv")[0].QuerySelectorAll("button")[0].Click();
        harness.Find("textarea").GetAttribute("value").Should().Be("The limit looks right.");
    }

    [Fact]
    public void ANoteIsTrimmedCutAtTheLimitAndCountedNearIt()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")]);
        harness.FindAll(".wb-rv")[0].QuerySelectorAll("button")[1].Click();

        harness.Find("textarea").Change("  " + new string('a', 950) + "  ");
        harness.Find(".wb-note .wb-hint").TextContent.Should().Be("950 of 1000");
        harness.Find("textarea").Change(new string('b', 1200));
        harness.Find(".wb-note .wb-hint").TextContent.Should().Be("1000 of 1000");
        harness.Find("textarea").Change("short");
        harness.FindAll(".wb-note .wb-hint").Should().BeEmpty();
    }

    [Fact]
    public void TheReviewStateIsKeptWhenTheRuleOrTheTabChangesAndWhenTheComponentIsRenderedAgain()
    {
        using var harness = new Harness();
        var rules = new List<WorkbenchRule> { Fx.Rule("readiness"), Fx.Rule("intake.unique") };
        harness.Render(rules);
        harness.FindAll(".wb-rv")[1].QuerySelectorAll("button")[0].Click();

        harness.FindAll(".wb-rule")[1].Click();
        harness.Open(WorkbenchTab.Simulate);
        harness.Open(WorkbenchTab.Read);
        harness.FindAll(".wb-rule")[0].Click();
        harness.FindAll(".wb-rv")[1].QuerySelectorAll("button")[0].GetAttribute("aria-pressed").Should().Be("true");

        var session = harness.Session;
        using var again = new Harness();
        again.Render(rules, session: session);
        again.FindAll(".wb-rv")[1].QuerySelectorAll("button")[0].GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void ARuleReadFromAnotherLocaleIsDescribedAgain()
    {
        var rule = Fx.Rule("readiness");
        using var harness = new Harness();
        var session = harness.Session;
        harness.Render([rule]);
        harness.FindAll(".wb-sent")[0].TextContent.Should().StartWith("If Score is between");

        using var spanish = new Harness();
        spanish.Render([Fx.Rule("readiness", WorkbenchLocale.Es)], WorkbenchLocale.Es, session);
        spanish.FindAll(".wb-sent")[0].TextContent.Should().StartWith("Si Puntaje está entre");
    }

    [Fact]
    public void ChangingTheSessionFromOutsideRedrawsThePanel()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());
        harness.Session.SelectedRuleKey = "intake.unique";
        harness.Session.SelectedTab = WorkbenchTab.Review;
        harness.Find(".wb-detail h1").TextContent.Should().Be("Unique intake");
        harness.Tab(WorkbenchTab.Review).GetAttribute("aria-selected").Should().Be("true");

        harness.Session.Clear();
        harness.Find(".wb-detail h1").TextContent.Should().Be("Readiness notes");
        harness.Tab(WorkbenchTab.Read).GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void ASessionHandedOverToAnotherSessionIsReleasedFromTheFirst()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());
        var first = harness.Session;
        var second = new WorkbenchSession();

        harness.Cut.SetParametersAndRender(parameters => parameters.Add(component => component.Session, second));
        second.SelectedTab = WorkbenchTab.Simulate;
        harness.Tab(WorkbenchTab.Simulate).GetAttribute("aria-selected").Should().Be("true");
        first.SelectedTab = WorkbenchTab.Tests;
        harness.Tab(WorkbenchTab.Simulate).GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void TheComponentRefusesAMissingSessionOrClock()
    {
        using var harness = new Harness();
        var act = () => harness.Ctx.RenderComponent<RuleWorkbench>(parameters => parameters
            .Add(component => component.Rules, Fx.All())
            .Add(component => component.Now, () => Fx.Moment));
        act.Should().Throw<ArgumentNullException>();

        var noClock = () => harness.Ctx.RenderComponent<RuleWorkbench>(parameters => parameters
            .Add(component => component.Rules, Fx.All())
            .Add(component => component.Session, new WorkbenchSession()));
        noClock.Should().Throw<ArgumentNullException>();
    }
}

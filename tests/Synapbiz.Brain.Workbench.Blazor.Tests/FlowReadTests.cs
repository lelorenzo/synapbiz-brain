using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class FlowReadTests
{
    public static TheoryData<string, WorkbenchLocale> EveryFlowInBothLanguages()
    {
        var data = new TheoryData<string, WorkbenchLocale>();
        foreach (var id in FlowDocs.ReferenceIds)
        {
            data.Add(id, WorkbenchLocale.En);
            data.Add(id, WorkbenchLocale.Es);
        }

        return data;
    }

    private static string Plain(string sentence) => FlowHarness.Normalise(sentence.Replace("`", string.Empty, StringComparison.Ordinal));

    [Theory]
    [MemberData(nameof(EveryFlowInBothLanguages))]
    public void EveryStepSentenceOfTheHeadlessViewIsOnTheReadTab(string id, WorkbenchLocale locale)
    {
        using var harness = new FlowHarness();
        var flows = FlowHarness.Reference();
        harness.Render(flows, locale);
        harness.Select(id);
        var process = flows.First(flow => flow.Key == id).Process!;
        var view = FlowDescriber.Describe(process, locale, flows.Select(flow => flow.Process!));
        var text = harness.PanelText;
        foreach (var step in view.Steps)
        {
            text.Should().Contain(Plain(step.Sentence), step.Id);
            text.Should().Contain(step.Label);
            text.Should().Contain(step.KindName);
            foreach (var route in step.Routes)
            {
                text.Should().Contain(Plain(route.Phrase), step.Id + "/" + route.Label);
            }
        }

        foreach (var stage in view.Stages)
        {
            harness.FindAll("section.wb-flow-stage h2").Select(heading => heading.TextContent).Should().Contain(stage.Label);
        }
    }

    [Theory]
    [MemberData(nameof(EveryFlowInBothLanguages))]
    public void TheNotTriedMarksAreExactlyTheRoutesThatCoverageNeverTakes(string id, WorkbenchLocale locale)
    {
        using var harness = new FlowHarness();
        var flows = FlowHarness.Reference();
        harness.Render(flows, locale);
        harness.Select(id);
        var coverage = FlowCoverage.Of(flows.First(flow => flow.Key == id).Process!, null, locale);
        harness.FindAll(".wb-route .wb-pill-warn").Should().HaveCount(coverage.NeverTaken.Count);
    }

    [Fact]
    public void TheStartingSentencesAndTheRulesAreShown()
    {
        using var harness = new FlowHarness();
        var flows = FlowHarness.Reference();
        harness.Render(flows);
        harness.Select("previsit.outreach");
        var entry = FlowLibrary.From(flows.Select(flow => flow.Process!), WorkbenchLocale.En).First(item => item.Id == "previsit.outreach");
        harness.PanelText.Should().Contain("How it starts");
        foreach (var rule in entry.Rules)
        {
            harness.PanelText.Should().Contain(rule);
        }
    }

    [Fact]
    public void AChildFlowThatIsInTheListIsALinkThatSelectsIt()
    {
        using var harness = new FlowHarness();
        var flows = FlowHarness.Reference();
        harness.Render(flows);
        var parent = flows.First(flow => FlowLibrary.From(flows.Select(item => item.Process!), WorkbenchLocale.En).First(entry => entry.Id == flow.Process!.Id).Children.Any(child => !child.IsMissing));
        harness.Select(parent.Key);
        var link = harness.Find(".wb-kv dd button.wb-link");
        link.Click();
        harness.Session.SelectedFlowKey.Should().NotBe(parent.Key);
    }

    [Fact]
    public void ARouteLinkMovesFocusToTheStepItNames()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Select("previsit.outreach");
        var focus = harness.FocusCount;
        harness.FindAll(".wb-route-go")[0].Click();
        harness.FocusCount.Should().BeGreaterThan(focus);
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Read);
    }

    [Fact]
    public void ATargetStepIsAFocusableRegionWithAHeading()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Select("previsit.outreach");
        var step = harness.Find("section.wb-step");
        step.GetAttribute("tabindex").Should().Be("-1");
        harness.Find("#" + step.GetAttribute("aria-labelledby")).TagName.Should().Be("H3");
    }

    [Fact]
    public void AnAiStepShowsTheReviewersBlockUnderItsFixedHeading()
    {
        using var harness = new FlowHarness();
        var flows = FlowHarness.Reference();
        harness.Render(flows);
        var withAi = flows.Where(flow => FlowDescriber.Describe(flow.Process!, WorkbenchLocale.En, flows.Select(item => item.Process!)).Steps.Any(step => step.Ai is not null)).ToList();
        withAi.Should().NotBeEmpty();
        foreach (var flow in withAi)
        {
            harness.Select(flow.Key);
            var views = FlowDescriber.Describe(flow.Process!, WorkbenchLocale.En, flows.Select(item => item.Process!)).Steps.Where(step => step.Ai is not null).ToList();
            harness.FindAll(".wb-ai").Should().HaveCount(views.Count);
            harness.Find(".wb-ai h4").TextContent.Should().Be("Step with artificial intelligence");
            foreach (var step in views)
            {
                harness.Find(".wb-ai").TextContent.Should().Contain(step.Ai!.Model);
            }
        }
    }

    [Fact]
    public void StepsWithNoStageComeUnderALastHeading()
    {
        using var harness = new FlowHarness();
        var process = FlowDocs.Process("flow.bare");
        harness.Render([new WorkbenchFlow { Key = "bare", Process = process }]);
        harness.PanelText.Should().Contain("Steps with no stage");
        harness.FindAll("section.wb-step").Should().HaveCount(ProcessOutline.From(process).Steps.Count);
    }

    [Fact]
    public void TheStateAndSourceLinesOfTheHostAreShown()
    {
        using var harness = new FlowHarness();
        var flow = FlowHarness.One("previsit.outreach");
        harness.Render([new WorkbenchFlow { Key = flow.Key, Process = flow.Process, StateLine = "Approved by the practice.", SourceLine = "From the demo folder." }]);
        harness.Find(".wb-detail .wb-ph").TextContent.Should().Contain("Approved by the practice.").And.Contain("From the demo folder.");
    }

    [Fact]
    public void ThePanelHasNoControlThatEditsTheFlow()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Select("previsit.outreach");
        harness.FindAll("[role=tabpanel] input, [role=tabpanel] select").Should().BeEmpty();
        harness.FindAll("[role=tabpanel] textarea").Should().BeEmpty();
    }

    [Fact]
    public void EveryButtonOfTheReadTabIsReachableByKeyboardAndNamed()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Select("previsit.outreach");
        harness.FindAll("[role=tabpanel] button").Should().OnlyContain(button => button.GetAttribute("tabindex") != "-1");
        harness.FindAll("[role=tabpanel] button").Should().OnlyContain(button => button.TextContent.Trim().Length > 0);
        harness.Cut.FindAll(".wb-rv").Should().OnlyContain(group => group.GetAttribute("role") == "group" && !string.IsNullOrWhiteSpace(group.GetAttribute("aria-label")));
    }
}

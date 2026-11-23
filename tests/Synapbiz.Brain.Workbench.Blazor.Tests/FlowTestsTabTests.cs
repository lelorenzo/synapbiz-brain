using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class FlowTestsTabTests
{
    [Theory]
    [MemberData(nameof(FlowReadTests.EveryFlowInBothLanguages), MemberType = typeof(FlowReadTests))]
    public void TheResultsOfEveryScenarioAreTheHeadlessResultsAndTheCoverageIsTheHeadlessCoverage(string id, WorkbenchLocale locale)
    {
        using var harness = new FlowHarness();
        var flows = FlowHarness.Reference();
        harness.Render(flows, locale);
        harness.Select(id);
        harness.Open(FlowWorkbenchTab.Tests);
        var process = flows.First(flow => flow.Key == id).Process!;
        var results = process.RunScenarios().Results;
        var cards = harness.FindAll("li.wb-scenario");
        cards.Select(card => card.QuerySelector("h3")!.TextContent).Should().Equal(results.Select(result => result.Name));
        for (var index = 0; index < results.Count; index++)
        {
            var pill = cards[index].QuerySelector(".wb-pill")!;
            pill.ClassList.Contains(results[index].Passed ? "wb-pill-ok" : "wb-pill-bad").Should().BeTrue();
        }

        var coverage = FlowCoverage.Of(process, null, locale);
        harness.PanelText.Should().Contain(locale == WorkbenchLocale.En
            ? $"{coverage.CoveredRoutes} of {coverage.TotalRoutes} routes taken by some test."
            : $"{coverage.CoveredRoutes} de {coverage.TotalRoutes} salidas recorridas por alguna prueba.");
        harness.FindAll("section.wb-card h3 + ul li").Count.Should().Be(coverage.NeverTaken.Count + coverage.StepsNeverEntered.Count + coverage.Problems.Count);
    }

    [Fact]
    public void EachScenarioSaysWhichStepsItGoesThroughInOrder()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Select("previsit.outreach");
        harness.Open(FlowWorkbenchTab.Tests);
        var process = FlowDocs.Process("previsit.outreach");
        var words = FlowWords.For(process, WorkbenchLocale.En);
        var replay = FlowSimulation.Replay(process, "nothing is missing");
        var expected = replay.History.Where(item => item.Kind == ProcessHistoryKind.StepEntered).Select(item => words.Step(item.Step!)).ToList();
        harness.FindAll("li.wb-scenario")[0].QuerySelectorAll("ol.wb-trace li").Select(item => item.TextContent).Should().Equal(expected);
    }

    [Fact]
    public void AScenarioThatFailsIsMarkedWithAWordAndAnIconAndItsFailureIsShown()
    {
        using var harness = new FlowHarness();
        var broken = FlowDocs.Edited("previsit.outreach", document => document["body"]!["scenarios"]![0]!["expect"]!["result"] = "something_else");
        harness.Render([new WorkbenchFlow { Key = "broken", Process = broken }]);
        harness.ListText.Should().Contain("1 of 16 tests fails");
        harness.Tab(FlowWorkbenchTab.Tests).TextContent.Should().Contain("1 fails");
        harness.Open(FlowWorkbenchTab.Tests);
        var card = harness.FindAll("li.wb-scenario")[0];
        card.QuerySelector(".wb-pill")!.TextContent.Should().Contain("Fails").And.Contain("✗");
        card.QuerySelector(".wb-errt")!.TextContent.Should().Contain("This test fails.").And.Contain("Technical detail:");
        card.QuerySelector(".wb-errt code.wb-code")!.TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AFlowWithNoScenariosSaysSoAndOffersNoReplay()
    {
        using var harness = new FlowHarness();
        harness.Render([new WorkbenchFlow { Key = "clock", Process = FlowDocs.Process("flow.clock") }]);
        harness.Open(FlowWorkbenchTab.Tests);
        harness.PanelText.Should().Contain("This flow comes with no tests.");
        harness.Buttons.Should().NotContain(button => button.TextContent.StartsWith("Replay", StringComparison.Ordinal));
        harness.ListText.Should().Contain("no tests");
    }

    [Fact]
    public void AProposedTestThatDoesNotPassIsMarkedFailedAfterTheFlowChanges()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Start("previsit.outreach", "nothing is missing");
        harness.UseAnswer("nothing is missing");
        harness.Type("Name of the proposed test", "kept");
        harness.Press("Save as a proposed test");
        harness.Open(FlowWorkbenchTab.Tests);
        harness.FindAll("li.wb-scenario")[^1].QuerySelector(".wb-pill-ok")!.TextContent.Should().Contain("Passes");
    }

    [Fact]
    public void TheScenarioLineHasTheSmallReviewControlAndItsMarkShowsInTheReviewTab()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Select("previsit.outreach");
        harness.Open(FlowWorkbenchTab.Tests);
        var group = harness.FindAll("li.wb-scenario")[0].QuerySelector(".wb-rv")!;
        group.GetAttribute("aria-label").Should().Be("Review of the test \u201Cnothing is missing\u201D");
        group.QuerySelectorAll("button").Select(button => button.TextContent).Should().Equal("Confirm", "Needs a fix", "Question");
        group.QuerySelectorAll("button")[0].Click();
        harness.FindAll("li.wb-scenario")[0].QuerySelector(".wb-rv button[aria-pressed=true]")!.TextContent.Should().Be("Confirmed");
        harness.Open(FlowWorkbenchTab.Review);
        harness.PanelText.Should().Contain("Test nothing is missing").And.Contain("Confirmed");
    }
}

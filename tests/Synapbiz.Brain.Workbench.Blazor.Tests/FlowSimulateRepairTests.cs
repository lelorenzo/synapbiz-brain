using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>What the Simulate tab does after a replay, with the answers of tests, the answer sources, the clock, the child flows and the words it uses for refusals.</summary>
public sealed class FlowSimulateRepairTests
{
    private const string Outreach = "previsit.outreach";
    private static readonly string[] BatchAndChild = ["flow.batch", "flow.child"];
    private static readonly string[] PickFields = ["pick", "size"];
    private const string Reply = "a reply after the first reminder closes the stage";

    private sealed class Named(string name, Func<FlowEffectRequest, CancellationToken, ValueTask<FlowAnswer?>> answer) : IFlowAnswerSource
    {
        public string Name => name;

        public ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken) => answer(request, cancellationToken);
    }

    private static FlowHarness Open(string id = Outreach, IFlowAnswerSource? source = null, IReadOnlyList<WorkbenchFlow>? flows = null, WorkbenchLocale locale = WorkbenchLocale.En)
    {
        var harness = new FlowHarness();
        harness.Render(flows ?? FlowHarness.Reference(), locale, source: source);
        harness.Select(id);
        return harness;
    }

    /// <summary>Starts a case like a test, answers the first read like it, and keeps what is on screen as a proposed test with a case that is still running.</summary>
    private static void KeepARunningCase(FlowHarness harness, string name)
    {
        harness.Open(FlowWorkbenchTab.Try);
        harness.Input("Start like the test").Change(Reply);
        harness.Press("Start");
        harness.UseAnswer(Reply);
        harness.Type("Name of the proposed test", name);
        harness.Press("Save as a proposed test");
    }

    private static IElement? AskButton(FlowHarness harness, string name) =>
        harness.Buttons.FirstOrDefault(button => FlowHarness.Normalise(button.TextContent) == "Ask “" + name + "”");

    // ---------------------------------------------------------------- Ask after a replay

    [Fact]
    public void AfterAReplayTheEffectThatIsWaitingCanBeAskedOfTheAnswerSourceAndTheReplayWasGivenTheSource()
    {
        var asked = new List<string>();
        var source = new Named("Practice AI", (request, _) =>
        {
            asked.Add(request.StepId);
            return ValueTask.FromResult<FlowAnswer?>(FlowAnswer.Output("""{}"""));
        });
        using var harness = Open(source: source);
        KeepARunningCase(harness, "mine");
        harness.Open(FlowWorkbenchTab.Tests);
        harness.Press("Replay “mine”");
        for (var turn = 0; turn < 5 && harness.HasButton("Next step"); turn++)
        {
            AskButton(harness, "Practice AI")!.HasAttribute("disabled").Should().BeTrue();
            harness.Press("Next step");
        }

        harness.HasButton("Next step").Should().BeFalse();
        var ask = AskButton(harness, "Practice AI");
        ask.Should().NotBeNull();
        ask!.HasAttribute("disabled").Should().BeFalse();
        ask.Click();
        asked.Should().ContainSingle().Which.Should().Be("send");
        harness.PanelText.Should().Contain("Practice AI");
    }

    [Fact]
    public void AfterAReplayWithoutASourceThereIsNoAskButton()
    {
        using var harness = Open();
        KeepARunningCase(harness, "mine");
        harness.Open(FlowWorkbenchTab.Tests);
        harness.Press("Replay “mine”");
        harness.PanelText.Should().Contain("Waiting for an answer");
        harness.Buttons.Select(button => FlowHarness.Normalise(button.TextContent)).Should().NotContain(text => text.StartsWith("Ask ", StringComparison.Ordinal));
    }

    [Fact]
    public void ASourceThatFailsAfterAReplayIsAVisibleRefusalAndTheCaseStaysWaiting()
    {
        var source = new Named("Practice AI", (_, _) => throw new InvalidOperationException("boom"));
        using var harness = Open(source: source);
        KeepARunningCase(harness, "mine");
        harness.Open(FlowWorkbenchTab.Tests);
        harness.Press("Replay “mine”");
        while (harness.HasButton("Next step"))
        {
            harness.Press("Next step");
        }

        AskButton(harness, "Practice AI")!.Click();
        harness.PanelText.Should().Contain("the answer source failed").And.Contain("Refused");
        harness.PanelText.Should().Contain("Waiting for an answer");
    }

    // ---------------------------------------------------------------- the answers of the tests

    [Fact]
    public void TheSecondVisitOfAnAnsweredStepOffersTheSecondAnswerOfTheTests()
    {
        const string transient = "a transient failure of the reminder is retried inside the window";
        using var harness = Open();
        harness.Open(FlowWorkbenchTab.Try);
        harness.Input("Start like the test").Change(transient);
        harness.Press("Start");
        harness.UseAnswer(transient);
        var card = harness.FindAll("section.wb-wait").First(item => item.TextContent.Contains("Waiting for an answer at", StringComparison.Ordinal));
        var first = card.QuerySelector("select")!.QuerySelector("option[value=\"" + transient + "\"]")!.TextContent;
        first.Should().Contain("(fails)");
        harness.UseAnswer(transient);

        harness.Buttons.First(button => button.TextContent.Contains("Go to that moment", StringComparison.Ordinal)).Click();
        harness.PanelText.Should().Contain("Waiting for an answer at");
        var again = harness.FindAll("section.wb-wait").First(item => item.TextContent.Contains("Waiting for an answer at", StringComparison.Ordinal));
        again.QuerySelector("select")!.QuerySelector("option[value=\"" + transient + "\"]")!.TextContent.Should().NotContain("(fails)");
    }

    [Fact]
    public void TheAiAnswerCardListsTheFieldsTheOutputMustHave()
    {
        var flows = BatchAndChild.Select(id => new WorkbenchFlow { Key = id, Group = "g", Process = FlowDocs.Process(id) }).ToList();
        using var harness = Open("flow.batch", flows: flows);
        harness.Open(FlowWorkbenchTab.Try);
        harness.Input("Start like the test").Change("both items run, the pick is large and the state settles after a second read");
        harness.Press("Start");
        for (var turn = 0; turn < 20 && !harness.PanelText.Contains("Waiting for an answer at", StringComparison.Ordinal); turn++)
        {
            if (harness.HasButton("Finish the child flow with this result"))
            {
                harness.Press("Finish the child flow with this result");
            }
            else if (harness.Buttons.FirstOrDefault(button => button.TextContent.Contains("Go to that moment", StringComparison.Ordinal)) is { } go)
            {
                go.Click();
            }
            else
            {
                harness.Press("1 day");
            }
        }

        var card = harness.FindAll("section.wb-wait").First(item => item.TextContent.Contains("Waiting for an answer at", StringComparison.Ordinal));
        FlowHarness.Normalise(card.TextContent).Should().Contain("It must return:");
        var fields = card.QuerySelectorAll("ul li code").Select(code => code.TextContent).ToList();
        fields.Should().Contain(PickFields);
    }

    // ---------------------------------------------------------------- refusals in words

    [Fact]
    public void AStartTheFlowRejectsForItsInputIsRefusedInWordsWithTheFormKept()
    {
        // The form reads the text of a reference as text, which the flow does not accept for such a variable.
        var process = FlowDocs.Edited("visit.note_wait", document => document["body"]!["variables"]!["case_ref"]!["type"] = "Reference");
        using var harness = new FlowHarness();
        harness.Render([new WorkbenchFlow { Key = "visit.note_wait", Group = "g", Process = process }]);
        harness.Open(FlowWorkbenchTab.Try);
        harness.Type("case_ref", "c-1");
        harness.Press("Start");
        harness.Find("[role=alert]").TextContent.Should().Contain("a value does not have the type the process expects");
        harness.PanelText.Should().Contain("Start a made-up case").And.NotContain("process.variable");
        harness.Input("case_ref").GetAttribute("value").Should().Be("c-1");
    }

    [Fact]
    public void ATimeZoneThatIsNotInTheListOfTheScreenIsSaidInWordsAtTheWindow()
    {
        using var harness = Open();
        harness.Open(FlowWorkbenchTab.Try);
        harness.Type("case_ref", "c-1");
        harness.Type("practice_zone", "Mars/Olympus");
        harness.Press("Start");
        harness.UseAnswer(Reply);
        harness.PanelText.Should().Contain("could not be worked out: the time zone is not in the list of this screen");
        harness.PanelText.Should().NotContain("process.window.zone_unknown");
    }

    // ---------------------------------------------------------------- the clock

    [Fact]
    public void ToTheNextTimerGoesToTheEarlierMomentWhenTheCaseWaitsForAWindow()
    {
        var now = DateTimeOffset.Parse("2027-01-08T23:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        using var harness = new FlowHarness { Now = () => now };
        harness.Render(FlowHarness.Reference());
        harness.Select(Outreach);
        harness.Open(FlowWorkbenchTab.Try);
        harness.Type("case_ref", "c-1");
        harness.Type("practice_zone", "America/New_York");
        harness.Press("Start");
        harness.UseAnswer(Reply);
        harness.PanelText.Should().Contain("Waiting for a time window");
        harness.Buttons.Should().Contain(button => button.TextContent.StartsWith("To the next timer", StringComparison.Ordinal));
        harness.Buttons.First(button => button.TextContent.StartsWith("To the next timer", StringComparison.Ordinal)).Click();
        harness.PanelText.Should().NotContain("Waiting for a time window");
    }

    // ---------------------------------------------------------------- the child flow

    [Fact]
    public void AfterBringingTheResultOfTheChildTheParentTookTheRouteOfThatResult()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Select("case.lifecycle");
        harness.Open(FlowWorkbenchTab.Try);
        var scenario = FlowDocs.Process("case.lifecycle").RunScenarios().Results[0].Name;
        harness.Input("Start like the test").Change(scenario);
        harness.Press("Start");
        for (var turn = 0; turn < 30 && !harness.PanelText.Contains("Waiting for the result of the flow Visit note wait", StringComparison.Ordinal); turn++)
        {
            Advance(harness, scenario);
        }

        harness.PanelText.Should().Contain("Waiting for the result of the flow Visit note wait");
        harness.Press("Open the child flow");
        for (var turn = 0; turn < 60 && !harness.PanelText.Contains("The case has finished.", StringComparison.Ordinal); turn++)
        {
            Advance(harness, null);
        }

        var parent = FlowDocs.Process("case.lifecycle");
        var child = FlowDocs.Process("visit.note_wait");
        var childWords = FlowWords.For(child, WorkbenchLocale.En);
        var result = FlowLibrary.ResultsOf(child).Single(id => harness.PanelText.Contains(childWords.Result(id), StringComparison.Ordinal));
        var step = ProcessOutline.From(parent).Steps.Single(item => item.Kind == OutlineStepKind.Subprocess && item.Process == "visit.note_wait");
        var target = step.Routes.Single(route => route.Label == "on:" + result).Target;
        var expected = FlowWords.For(parent, WorkbenchLocale.En).Step(target);

        harness.Press("Bring its result to the parent flow");
        harness.PanelText.Should().NotContain("You are simulating the child flow");
        FlowHarness.Normalise(harness.FindAll("ol.wb-timeline li")[^1].TextContent).Should().Contain("The child flow of").And.Contain("Then: " + expected);
    }

    private static void Advance(FlowHarness harness, string? scenario)
    {
        foreach (var card in harness.FindAll("section.wb-wait"))
        {
            var heading = FlowHarness.Normalise(card.QuerySelector("h3")!.TextContent);
            IElement? Find(string start) => card.QuerySelectorAll("button").FirstOrDefault(button => FlowHarness.Normalise(button.TextContent).StartsWith(start, StringComparison.Ordinal));
            if (heading.StartsWith("Waiting for an answer", StringComparison.Ordinal) && harness.UseAnswer(scenario))
            {
                return;
            }

            var button = heading switch
            {
                _ when heading.StartsWith("Waiting for an answer", StringComparison.Ordinal) => Find("It fails for good"),
                _ when heading.StartsWith("A person must act", StringComparison.Ordinal) => card.QuerySelectorAll(".wb-actions button").FirstOrDefault(),
                _ when heading.StartsWith("Waiting for an event", StringComparison.Ordinal) => Find("The event arrives"),
                _ when heading.StartsWith("Waiting for another flow", StringComparison.Ordinal) => Find("Finish the child flow"),
                _ => Find("Go to that moment"),
            };
            if (button is not null)
            {
                button.Click();
                return;
            }
        }

        harness.Press("1 day");
    }

    // ---------------------------------------------------------------- scenarios are played once

    [Fact]
    public void MountingSixFlowsPlaysEachScenarioOnceAndSwitchingTabsAndFlowsPlaysNothingMore()
    {
        using var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference());
        harness.Cut.Instance.ReportCount.Should().Be(6);
        var played = harness.Cut.Instance.ScenariosPlayedTotal;
        played.Should().Be(FlowDocs.Reference.Sum(item => ProcessOutline.From(item.Process).Scenarios.Count));
        foreach (var id in FlowDocs.ReferenceIds)
        {
            harness.Select(id);
            foreach (var tab in new[] { FlowWorkbenchTab.Diagram, FlowWorkbenchTab.Tests, FlowWorkbenchTab.Try, FlowWorkbenchTab.Review, FlowWorkbenchTab.Read })
            {
                harness.Open(tab);
            }
        }

        harness.Cut.Instance.ReportCount.Should().Be(6);
        harness.Cut.Instance.ScenariosPlayedTotal.Should().Be(played);

        harness.Cut.SetParametersAndRender(parameters => parameters.Add(component => component.Locale, WorkbenchLocale.Es));
        harness.Cut.Instance.ReportCount.Should().Be(12);
    }

    [Fact]
    public void TheComponentHoldsItsScenarioReportsInItsOwnInstanceAndInNoStaticField()
    {
        var folder = Path.GetDirectoryName(TestFiles.Locate("src/Synapbiz.Brain.Workbench.Blazor/FlowWorkbench.razor"))!;
        var files = Directory.GetFiles(folder, "Flow*.cs").Concat(Directory.GetFiles(folder, "Flow*.razor")).ToList();
        files.Should().NotBeEmpty();
        var pattern = new System.Text.RegularExpressions.Regex(@"^\s*(private|internal|public|protected)?\s*static\s+(?!readonly\b|class\b|async\b|partial\b)[\w<>,\[\]?. ]*(Dictionary|List|HashSet|Report|Cache|Table|Set|Map)[\w<>,\[\]?. ]*\s+\w+\s*(=|;)", System.Text.RegularExpressions.RegexOptions.Multiline);
        foreach (var file in files)
        {
            pattern.Matches(File.ReadAllText(file)).Select(match => match.Value.Trim()).Should().BeEmpty(Path.GetFileName(file));
        }

        var caches = new System.Text.RegularExpressions.Regex(@"static\s+readonly\s+(Concurrent)?Dictionary|ConditionalWeakTable|MemoryCache");
        files.Where(file => caches.IsMatch(File.ReadAllText(file))).Should().BeEmpty();
        files.Sum(file => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), @"FlowScenarioReport\.Of\(").Count).Should().Be(1);
    }

    // ---------------------------------------------------------------- the shapes of the diagram

    [Theory]
    [InlineData("check", "action")]
    [InlineData("assess", "decision")]
    [InlineData("escalate", "person")]
    [InlineData("reply", "wait")]
    [InlineData("done", "end")]
    public void AStepIsDrawnWithTheClassOfItsFamily(string step, string family)
    {
        using var harness = Open();
        harness.Open(FlowWorkbenchTab.Diagram);
        harness.Find("g.wb-node[aria-label*=\"" + FlowDescriber.Describe(FlowDocs.Process(Outreach), WorkbenchLocale.En).Steps.Single(item => item.Id == step).Label + "\"]").ClassList.Should().Contain("wb-node-" + family);
    }

    [Theory]
    [InlineData("flow.batch", "ask", "ai")]
    [InlineData("flow.batch", "child", "flow")]
    public void AnAiStepAndAChildStepAreDrawnWithTheClassOfTheirFamily(string document, string step, string family)
    {
        var flows = new[] { document, "flow.child" }.Distinct().Select(id => new WorkbenchFlow { Key = id, Group = "g", Process = FlowDocs.Process(id) }).ToList();
        using var harness = Open(document, flows: flows);
        harness.Open(FlowWorkbenchTab.Diagram);
        var label = FlowDescriber.Describe(FlowDocs.Process(document), WorkbenchLocale.En).Steps.Single(item => item.Id == step).Label;
        harness.Find("g.wb-node[aria-label*=\"" + label + "\"]").ClassList.Should().Contain("wb-node-" + family);
    }
}

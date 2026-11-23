using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class FlowTryStartTests
{
    private static string Humanized(string name)
    {
        var words = WorkbenchText.Humanize(name);
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static FlowHarness Open(string id, Func<DateTimeOffset>? now = null, IReadOnlyDictionary<string, TimeSpan>? zones = null)
    {
        var harness = new FlowHarness();
        if (now is not null)
        {
            harness.Now = now;
        }

        harness.Render(FlowHarness.Reference(), zones: zones);
        harness.Select(id);
        harness.Open(FlowWorkbenchTab.Try);
        return harness;
    }

    [Fact]
    public void TheStartFormHasOneControlPerInputVariableAndTheStartTime()
    {
        foreach (var id in FlowDocs.ReferenceIds)
        {
            using var harness = Open(id);
            var inputs = FlowForms.StartFields(FlowDocs.Process(id));
            harness.FindAll(".wb-card .wb-field label.wb-label").Select(label => FlowHarness.Normalise(label.TextContent)).Should().Contain(inputs.Select(field => Humanized(field.Name)));
            harness.PanelText.Should().Contain("Start a made-up case");
            harness.FindAll("[role=tabpanel] label.wb-label").Select(label => label.GetAttribute("for")).Should().OnlyContain(target => target != null && harness.FindAll("#" + target).Count == 1);
        }
    }

    [Fact]
    public void EachTypeOfVariableGetsItsControl()
    {
        using var yesNo = Open("case.lifecycle");
        var select = yesNo.Input("Assist enabled");
        select.TagName.Should().Be("SELECT");
        select.QuerySelectorAll("option").Select(option => option.TextContent).Should().Equal("Not chosen", "Yes", "No");

        using var date = Open("practice.daily_refresh");
        date.PanelText.Should().Contain("A date such as 2026-11-23.");

        using var batch = new FlowHarness();
        batch.Render([new WorkbenchFlow { Key = "batch", Process = FlowDocs.Process("flow.batch"), ProcessText = "{}" }]);
        batch.Open(FlowWorkbenchTab.Try);
        batch.Input("Items").TagName.Should().Be("TEXTAREA");
        batch.PanelText.Should().Contain("One value on each line, or a JSON text.").And.Contain("UTC date and time such as");

        using var text = Open("previsit.outreach");
        text.Input("Case ref").TagName.Should().Be("INPUT");
    }

    [Fact]
    public void TheStartTimeDefaultsToNowTruncatedToTheMinute()
    {
        using var harness = Open("previsit.outreach", () => new DateTimeOffset(2026, 11, 22, 21, 7, 45, TimeSpan.Zero));
        harness.Input("Start time (UTC)").GetAttribute("value").Should().Be("2026-11-22T21:07");
    }

    [Fact]
    public void StartLikeATestFillsTheFormFromTheScenario()
    {
        using var harness = Open("previsit.outreach");
        var select = harness.Input("Start like the test");
        select.QuerySelectorAll("option").Select(option => option.TextContent).Should().Contain(["None, by hand", "nothing is missing"]);
        select.Change("nothing is missing");
        harness.Input("Case ref").GetAttribute("value").Should().Be("c-1001");
        harness.Input("Practice zone").GetAttribute("value").Should().Be("America/New_York");
        harness.Input("Start time (UTC)").GetAttribute("value").Should().Be("2027-01-04T14:00");
    }

    [Fact]
    public void TypingInAFieldClearsTheTestChoice()
    {
        using var harness = Open("previsit.outreach");
        harness.Input("Start like the test").Change("nothing is missing");
        harness.Type("Case ref", "c-2");
        harness.Input("Start like the test").GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public void ATextThatCannotBeReadKeepsTheFormNamesTheFieldAndMovesFocusToIt()
    {
        using var harness = new FlowHarness();
        harness.Render([new WorkbenchFlow { Key = "batch", Process = FlowDocs.Process("flow.batch"), ProcessText = "{}" }]);
        harness.Open(FlowWorkbenchTab.Try);
        harness.Type("Batch ref", "b-1");
        var due = harness.Input("Visit at");
        due.Change("yesterday-ish");
        var focus = harness.FocusCount;
        harness.Press("Start");
        harness.PanelText.Should().Contain("Start a made-up case").And.Contain("This text cannot be read as what the field needs.");
        harness.Input("Visit at").GetAttribute("aria-invalid").Should().Be("true");
        harness.Input("Visit at").GetAttribute("aria-describedby").Should().Contain("-err");
        harness.FocusCount.Should().BeGreaterThan(focus);
        harness.Input("Batch ref").GetAttribute("value").Should().Be("b-1");
        harness.Session.Should().NotBeNull();
    }

    [Fact]
    public void AStartTimeThatCannotBeReadIsRefusedTheSameWay()
    {
        using var harness = Open("previsit.outreach");
        harness.Input("Start time (UTC)").Change("tomorrow");
        harness.Press("Start");
        harness.Input("Start time (UTC)").GetAttribute("aria-invalid").Should().Be("true");
        harness.PanelText.Should().Contain("Start a made-up case");
    }

    [Fact]
    public void AZoneTheScreenWasGivenThatIsNotValidIsSaidInWordsAndTheFormStays()
    {
        using var harness = Open("previsit.outreach", zones: new Dictionary<string, TimeSpan> { ["Nowhere/Land"] = TimeSpan.FromHours(30) });
        harness.Press("Start");
        harness.PanelText.Should().Contain("a time zone given to this screen is not valid").And.Contain("Start a made-up case");
        harness.FindAll("[role=alert]").Should().NotBeEmpty();
    }

    [Fact]
    public void StartingShowsWhereTheCaseIsWithTheMarkingsAndTheLiveRegionSaysSo()
    {
        using var harness = Open("previsit.outreach");
        harness.Input("Start like the test").Change("nothing is missing");
        harness.Press("Start");
        harness.PanelText.Should().Contain("Where the case is").And.Contain("Simulated time").And.Contain("Case data");
        harness.Find(".wb-band .wb-pill-sim").TextContent.Should().Be("Simulation");
        harness.Find("[role=tabpanel] .wb-sr[role=status]").GetAttribute("aria-live").Should().Be("polite");
        harness.Find("[role=tabpanel] .wb-sr[role=status]").TextContent.Should().StartWith("The case is at");
    }

    [Fact]
    public void ATryOutIsKeptWhenTheReaderLeavesTheTabAndComesBack()
    {
        using var harness = Open("previsit.outreach");
        harness.Press("Start");
        harness.Open(FlowWorkbenchTab.Read);
        harness.Open(FlowWorkbenchTab.Try);
        harness.PanelText.Should().Contain("Where the case is");
    }
}

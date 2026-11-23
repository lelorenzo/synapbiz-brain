using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class FlowTryStartTests
{
    /// <summary>The label of a field: the name of its variable as the document writes it, and for a date and time the word that says it is UTC.</summary>
    private static string LabelOf(FlowFormField field) => field.Kind == FlowFieldKind.DateTime ? field.Name + " (UTC)" : field.Name;

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
    public void TheStartFormHasOneControlPerInputVariableNamedAsTheDocumentWritesItAndTheStartTime()
    {
        foreach (var id in FlowDocs.ReferenceIds)
        {
            using var harness = Open(id);
            var inputs = FlowForms.StartFields(FlowDocs.Process(id));
            harness.FindAll(".wb-card .wb-field label.wb-label").Select(label => FlowHarness.Normalise(label.TextContent)).Should().Contain(inputs.Select(LabelOf));
            harness.FindAll(".wb-card .wb-field label.wb-label code.wb-code").Select(code => code.TextContent).Should().Contain(inputs.Select(field => field.Name));
            harness.PanelText.Should().Contain("Start a made-up case");
            harness.FindAll("[role=tabpanel] label.wb-label").Select(label => label.GetAttribute("for")).Should().OnlyContain(target => target != null && harness.FindAll("#" + target).Count == 1);
        }
    }

    [Fact]
    public void EachTypeOfVariableGetsItsOwnTypedControl()
    {
        using var yesNo = Open("case.lifecycle");
        var box = yesNo.Input("assist_enabled");
        box.TagName.Should().Be("INPUT");
        box.GetAttribute("type").Should().Be("checkbox");
        box.HasAttribute("checked").Should().BeFalse();

        using var date = Open("practice.daily_refresh");
        date.Input("run_for").GetAttribute("type").Should().Be("date");

        using var batch = new FlowHarness();
        batch.Render([new WorkbenchFlow { Key = "batch", Process = FlowDocs.Process("flow.batch") }]);
        batch.Open(FlowWorkbenchTab.Try);
        batch.Input("items").TagName.Should().Be("TEXTAREA");
        batch.Input("visit_at (UTC)").GetAttribute("type").Should().Be("datetime-local");
        batch.PanelText.Should().Contain("One value on each line, or a JSON text.").And.Contain("The time is read as UTC.");
        batch.Input("Start time (UTC)").GetAttribute("type").Should().Be("datetime-local");

        using var text = Open("previsit.outreach");
        text.Input("case_ref").TagName.Should().Be("INPUT");
        text.Input("case_ref").GetAttribute("type").Should().Be("text");
    }

    [Theory]
    [InlineData("Integer", "1", "A whole number, for example 12.")]
    [InlineData("Decimal", "any", "A number, for example 12.5.")]
    public void ANumberVariableGetsANumberControlWithItsHint(string type, string step, string hint)
    {
        var process = FlowDocs.Edited("flow.bare", document => document["body"]!["variables"]!["ref"]!["type"] = type);
        using var harness = new FlowHarness();
        harness.Render([new WorkbenchFlow { Key = "numbers", Process = process }]);
        harness.Open(FlowWorkbenchTab.Try);
        var control = harness.Input("ref");
        control.GetAttribute("type").Should().Be("number");
        control.GetAttribute("step").Should().Be(step);
        harness.PanelText.Should().Contain(hint);
        control.Change("12");
        harness.Input("ref").GetAttribute("value").Should().Be("12");
    }

    [Fact]
    public void ACheckboxIsReadAsYesAndThenNoAndADateTimeIsReadAsUtc()
    {
        using var harness = Open("postvisit.coding_review");
        harness.Input("assist_enabled").Change(true);
        harness.Press("Start");
        harness.FindAll("dl.wb-kv")[1].TextContent.Should().Contain("assist_enabled").And.Contain("true");

        using var again = Open("postvisit.coding_review");
        again.Input("assist_enabled").Change(true);
        again.Input("assist_enabled").Change(false);
        again.Press("Start");
        again.FindAll("dl.wb-kv")[1].TextContent.Should().Contain("false");

        using var instant = new FlowHarness();
        instant.Render([new WorkbenchFlow { Key = "clock", Process = FlowDocs.Process("flow.clock") }]);
        instant.Open(FlowWorkbenchTab.Try);
        instant.Input("due (UTC)").Change("2026-11-23T09:30");
        instant.Press("Start");
        instant.FindAll("dl.wb-kv")[1].TextContent.Should().Contain("2026-11-23T09:30:00Z");
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
        select.QuerySelectorAll("option").Select(option => option.TextContent).Should().Contain(["None, by hand", "\u201Cnothing is missing\u201D"]);
        select.Change("nothing is missing");
        harness.Input("case_ref").GetAttribute("value").Should().Be("c-1001");
        harness.Input("practice_zone").GetAttribute("value").Should().Be("America/New_York");
        harness.Input("Start time (UTC)").GetAttribute("value").Should().Be("2027-01-04T14:00");
    }

    [Fact]
    public void TypingInAFieldClearsTheTestChoice()
    {
        using var harness = Open("previsit.outreach");
        harness.Input("Start like the test").Change("nothing is missing");
        harness.Type("case_ref", "c-2");
        harness.Input("Start like the test").GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public void ATextThatCannotBeReadKeepsTheFormNamesTheFieldAndMovesFocusToIt()
    {
        using var harness = new FlowHarness();
        harness.Render([new WorkbenchFlow { Key = "batch", Process = FlowDocs.Process("flow.batch") }]);
        harness.Open(FlowWorkbenchTab.Try);
        harness.Type("batch_ref", "b-1");
        var due = harness.Input("visit_at (UTC)");
        due.Change("yesterday-ish");
        var focus = harness.FocusCount;
        harness.Press("Start");
        harness.PanelText.Should().Contain("Start a made-up case").And.Contain("This text cannot be read as what the field needs.");
        harness.Input("visit_at (UTC)").GetAttribute("aria-invalid").Should().Be("true");
        harness.Input("visit_at (UTC)").GetAttribute("aria-describedby").Should().Contain("-err");
        harness.FocusCount.Should().BeGreaterThan(focus);
        harness.Input("batch_ref").GetAttribute("value").Should().Be("b-1");
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

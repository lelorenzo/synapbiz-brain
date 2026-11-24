using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class FlowAccessibilityTests
{
    private static readonly string[] AriaReferences = ["aria-labelledby", "aria-describedby", "aria-controls"];

    private static readonly string[] StructuralHooks = ["wb-band-g", "wb-detail", "wb-edge-g", "wb-list-panel", "wb-rule-name"];

    private static FlowHarness Open(FlowWorkbenchTab tab, WorkbenchLocale locale = WorkbenchLocale.En, bool started = false)
    {
        var harness = new FlowHarness();
        harness.Render(FlowHarness.Reference(), locale);
        harness.Select("previsit.outreach");
        if (started)
        {
            harness.Start("previsit.outreach", "nothing is missing");
        }

        harness.Open(tab);
        return harness;
    }

    public static TheoryData<FlowWorkbenchTab> Tabs() => new(Enum.GetValues<FlowWorkbenchTab>());

    [Theory]
    [MemberData(nameof(Tabs))]
    public void EveryControlHasAnAccessibleNameAndEveryLabelAndReferencePointsAtOneElement(FlowWorkbenchTab tab)
    {
        using var harness = Open(tab, started: true);
        foreach (var control in harness.FindAll("input, select, textarea"))
        {
            var named = control.GetAttribute("aria-label") is { Length: > 0 }
                || control.Id is { Length: > 0 } id && harness.FindAll("label[for=\"" + id + "\"]").Count == 1;
            named.Should().BeTrue(control.OuterHtml);
        }

        foreach (var button in harness.FindAll("button"))
        {
            (button.TextContent.Trim().Length > 0 || button.GetAttribute("aria-label") is { Length: > 0 }).Should().BeTrue(button.OuterHtml);
        }

        var ids = harness.FindAll("[id]").Select(element => element.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
        foreach (var reference in harness.FindAll("[aria-labelledby], [aria-describedby], [aria-controls]")
                     .SelectMany(element => AriaReferences.Select(name => element.GetAttribute(name))).Where(value => value is not null).SelectMany(value => value!.Split(' ')))
        {
            ids.Should().Contain(reference);
        }
    }

    [Fact]
    public void TheTabsFollowTheTabsPatternWithOneTabStopArrowKeysHomeAndEnd()
    {
        using var harness = Open(FlowWorkbenchTab.Read);
        harness.Find("[role=tablist]").GetAttribute("aria-label").Should().Be("Process sections");
        var tabs = harness.FindAll("[role=tab]");
        tabs.Select(tab => tab.GetAttribute("aria-selected")).Should().Equal("true", "false", "false", "false", "false");
        tabs.Select(tab => tab.GetAttribute("tabindex")).Should().Equal("0", "-1", "-1", "-1", "-1");
        tabs.Should().OnlyContain(tab => tab.GetAttribute("aria-controls") == harness.Find("[role=tabpanel]").Id);
        harness.Find("[role=tabpanel]").GetAttribute("aria-labelledby").Should().Be(tabs[0].Id);

        var focus = harness.FocusCount;
        tabs[0].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Diagram);
        harness.FocusCount.Should().BeGreaterThan(focus);
        harness.FindAll("[role=tab]")[1].KeyDown(new KeyboardEventArgs { Key = "End" });
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Review);
        harness.FindAll("[role=tab]")[4].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Read);
        harness.FindAll("[role=tab]")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Review);
        harness.FindAll("[role=tab]")[4].KeyDown(new KeyboardEventArgs { Key = "Home" });
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Read);
        harness.FindAll("[role=tab]")[0].KeyDown(new KeyboardEventArgs { Key = "x" });
        harness.Session.SelectedTab.Should().Be(FlowWorkbenchTab.Read);
    }

    [Fact]
    public void TheReviewButtonsAreAGroupOfToggleButtonsWithTheirStateInWordsAndInAriaPressed()
    {
        using var harness = Open(FlowWorkbenchTab.Read);
        harness.Find("section.wb-step .wb-rv button").Click();
        var pressed = harness.Find("section.wb-step .wb-rv button[aria-pressed=true]");
        pressed.TextContent.Should().Be("Confirmed");
        harness.FindAll("section.wb-step .wb-rv").Should().OnlyContain(group => group.GetAttribute("role") == "group" && group.GetAttribute("aria-label")!.Length > 0);
        harness.FindAll("section.wb-step .wb-rv button").Should().OnlyContain(button => button.GetAttribute("aria-pressed") != null);
    }

    [Fact]
    public void AStateIsNeverShownByColourAloneAWordOrAMarkGoesWithEveryPillTone()
    {
        using var harness = Open(FlowWorkbenchTab.Tests, started: true);
        foreach (var pill in harness.FindAll(".wb-pill-ok, .wb-pill-bad, .wb-pill-warn"))
        {
            pill.TextContent.Trim().Length.Should().BeGreaterThan(0, pill.OuterHtml);
        }

        var untried = Synapbiz.Brain.Workbench.Tests.FlowDocs.All.First(item => FlowCoverage.Of(item.Process).NeverTaken.Count > 0);
        using var diagram = new FlowHarness();
        diagram.Render([new WorkbenchFlow { Key = untried.Name, Process = untried.Process }]);
        diagram.Open(FlowWorkbenchTab.Diagram);
        diagram.FindAll("svg.wb-diagram-svg circle.wb-untried").Should().NotBeEmpty();
        diagram.FindAll("svg.wb-diagram-svg circle.wb-untried").Should().OnlyContain(circle => circle.QuerySelector("title") != null);
        diagram.FindAll("text.wb-untried-t").Should().OnlyContain(mark => mark.TextContent == "?");
    }

    [Fact]
    public void AResultThatAppearsAfterAnActionIsAnnouncedInALiveRegionAndAnErrorInAnAlert()
    {
        using var harness = Open(FlowWorkbenchTab.Try, started: true);
        var live = harness.Find("[role=tabpanel] .wb-sr[role=status]");
        live.GetAttribute("aria-live").Should().Be("polite");
        harness.FindAll("section.wb-wait")[0].QuerySelector("textarea")!.Change("nope");
        harness.FindAll("section.wb-wait")[0].QuerySelectorAll("button").First(button => button.TextContent.Contains("Send this answer", StringComparison.Ordinal)).Click();
        harness.Find("[role=tabpanel] .wb-sr[role=status]").TextContent.Should().Contain("The process refused the command");
        harness.FindAll("section.wb-wait [role=alert]").Should().NotBeEmpty();
    }

    [Fact]
    public void ATextFieldWhoseTextWasRefusedIsMarkedInvalidAndDescribedByItsError()
    {
        using var harness = Open(FlowWorkbenchTab.Try);
        harness.Input("Start time (UTC)").Change("when?");
        harness.Press("Start");
        var field = harness.Input("Start time (UTC)");
        field.GetAttribute("aria-invalid").Should().Be("true");
        field.GetAttribute("aria-describedby")!.Split(' ').Should().Contain(id => harness.FindAll("#" + id).Count == 1 && harness.Find("#" + id).TextContent.Contains("cannot be read", StringComparison.Ordinal));
    }

    [Fact]
    public void TheNarrowListIsASelectWithALabelAndTheWideListIsANavigationLandmark()
    {
        using var harness = Open(FlowWorkbenchTab.Read);
        harness.Find("select.wb-list-select").GetAttribute("aria-label").Should().Be("Process list");
        harness.Find("nav.wb-list").GetAttribute("aria-label").Should().Be("Process list");
        harness.FindAll("button.wb-rule[aria-current=true]").Should().HaveCount(1);
    }

    [Fact]
    public void EveryLanguageSetsTheLangAttributeOfTheRoot()
    {
        using var english = Open(FlowWorkbenchTab.Read);
        english.Find(".wb-root").GetAttribute("lang").Should().Be("en");
        using var spanish = Open(FlowWorkbenchTab.Read, WorkbenchLocale.Es);
        spanish.Find(".wb-root").GetAttribute("lang").Should().Be("es");
    }

    [Fact]
    public void TheSpanishPanelsRenderEveryTabWithoutAnEnglishFixedString()
    {
        foreach (var tab in Enum.GetValues<FlowWorkbenchTab>())
        {
            using var harness = Open(tab, WorkbenchLocale.Es);
            if (tab == FlowWorkbenchTab.Try)
            {
                harness.Input("Empezar como la prueba").Change("nothing is missing");
                harness.Press("Empezar");
            }

            var text = harness.DetailText;
            text.Should().NotContain("Simulation").And.NotContain("Start over").And.NotContain("Let time pass").And.NotContain("Export the review");
        }
    }

    [Fact]
    public void EveryClassThePanelUsesIsInTheStylesheetExceptForTheStructuralHooks()
    {
        var sheet = Fx.Read("src/Synapbiz.Brain.Workbench.Blazor/wwwroot/workbench.css");
        var defined = Regex.Matches(sheet, "\\.(wb-[a-z0-9-]+)").Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in new[] { "FlowWorkbench.razor", "FlowWorkbench.razor.cs", "FlowWorkbench.Try.cs", "FlowWorkbench.Review.cs" })
        {
            var source = Fx.Read("src/Synapbiz.Brain.Workbench.Blazor/" + file);
            foreach (Match match in Regex.Matches(source, "\"([^\"]*\\bwb-[a-z0-9 -]*)\""))
            {
                foreach (Match name in Regex.Matches(match.Groups[1].Value, "wb-[a-z0-9-]+"))
                {
                    used.Add(name.Value);
                }
            }
        }

        used.Should().NotBeEmpty();
        used.Where(name => !name.EndsWith('-')).Except(defined).Except(StructuralHooks).Should().BeEmpty();
        used.Should().OnlyContain(name => name.StartsWith("wb-", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePanelUsesNoInlineStyleAndNoScript()
    {
        using var harness = Open(FlowWorkbenchTab.Diagram, started: true);
        harness.FindAll("[style]").Should().BeEmpty();
        harness.FindAll("script").Should().BeEmpty();
        Fx.Read("src/Synapbiz.Brain.Workbench.Blazor/FlowWorkbench.razor").Should().NotContain("IJSRuntime").And.NotContain("style=\"");
    }
}

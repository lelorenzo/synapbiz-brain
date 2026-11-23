using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class TabsTests
{
    [Fact]
    public void TheTabsHaveTheirRolesAndTheSelectedOneIsMarked()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());

        var list = harness.Find("[role=tablist]");
        list.GetAttribute("aria-label").Should().Be("Rule sections");
        var tabs = harness.FindAll("[role=tab]");
        tabs.Select(item => item.TextContent.Trim()).Should().Equal("Read", "Simulate", "Test cases3", "Review");
        tabs.Select(item => item.GetAttribute("aria-selected")).Should().Equal("true", "false", "false", "false");
        tabs.Select(item => item.GetAttribute("tabindex")).Should().Equal("0", "-1", "-1", "-1");

        var panel = harness.Find("[role=tabpanel]");
        panel.GetAttribute("aria-labelledby").Should().Be(tabs[0].Id);
        tabs.Select(item => item.GetAttribute("aria-controls")).Should().OnlyContain(id => id == panel.Id);
        panel.GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public void TheTabsAreInSpanishWhenTheLocaleIs()
    {
        using var harness = new Harness();
        harness.Render(Fx.All(WorkbenchLocale.Es), WorkbenchLocale.Es);
        harness.FindAll("[role=tab]").Select(item => item.TextContent.Trim()).Should().Equal("Leer", "Simular", "Casos de prueba3", "Revisión");
        harness.Find("[role=tablist]").GetAttribute("aria-label").Should().Be("Secciones de la regla");
    }

    [Theory]
    [InlineData(WorkbenchTab.Read)]
    [InlineData(WorkbenchTab.Simulate)]
    [InlineData(WorkbenchTab.Tests)]
    [InlineData(WorkbenchTab.Review)]
    public void PressingATabOpensItAndTheSessionKeepsIt(WorkbenchTab tab)
    {
        using var harness = new Harness();
        harness.Render(Fx.All());

        harness.Open(tab);

        harness.Session.SelectedTab.Should().Be(tab);
        harness.FindAll("[role=tab]").Select(item => item.GetAttribute("aria-selected")).Should().Equal(
            Enumerable.Range(0, 4).Select(index => index == (int)tab ? "true" : "false"));
        harness.Find("[role=tabpanel]").GetAttribute("aria-labelledby").Should().Be(harness.Tab(tab).Id);
    }

    [Fact]
    public void TheTabIsKeptWhenTheRuleChangesAndTheRuleWhenTheTabChanges()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());

        harness.Open(WorkbenchTab.Tests);
        harness.FindAll(".wb-rule")[3].Click();
        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Tests);
        harness.Tab(WorkbenchTab.Tests).GetAttribute("aria-selected").Should().Be("true");
        harness.Find(".wb-detail h1").TextContent.Should().Be("Unique intake");

        harness.Open(WorkbenchTab.Review);
        harness.Find(".wb-detail h1").TextContent.Should().Be("Unique intake");
        harness.Session.SelectedRuleKey.Should().Be("intake.unique");
    }

    [Fact]
    public void TheArrowHomeAndEndKeysMoveBetweenTabsAndWrap()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());

        harness.Tab(WorkbenchTab.Read).KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Simulate);
        harness.Tab(WorkbenchTab.Simulate).KeyDown(new KeyboardEventArgs { Key = "End" });
        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Review);
        harness.Tab(WorkbenchTab.Review).KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Read);
        harness.Tab(WorkbenchTab.Read).KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Review);
        harness.Tab(WorkbenchTab.Review).KeyDown(new KeyboardEventArgs { Key = "Home" });
        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Read);
        harness.Tab(WorkbenchTab.Read).KeyDown(new KeyboardEventArgs { Key = "x" });
        harness.Session.SelectedTab.Should().Be(WorkbenchTab.Read);
        harness.Ctx.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Should().NotBeEmpty();
    }

    [Fact]
    public void TheTestsTabCountsTheCasesAndTurnsRedWhenOneFails()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());
        var pill = harness.Tab(WorkbenchTab.Tests).QuerySelector(".wb-pill")!;
        pill.TextContent.Should().Be("3");
        pill.ClassList.Should().NotContain("wb-pill-bad");

        harness.Render([Fx.FromText("readiness", Fx.FailingReadiness())]);
        pill = harness.Tab(WorkbenchTab.Tests).QuerySelector(".wb-pill")!;
        pill.TextContent.Should().Be("1 fails");
        pill.ClassList.Should().Contain("wb-pill-bad");
    }

    [Fact]
    public void TheReviewTabCountsTheReviewedItems()
    {
        using var harness = new Harness();
        harness.Render(Fx.All());
        harness.Tab(WorkbenchTab.Review).QuerySelector(".wb-pill").Should().BeNull();

        harness.Press("Confirm");
        harness.Tab(WorkbenchTab.Review).QuerySelector(".wb-pill")!.TextContent.Should().Be("1");
    }

    [Fact]
    public void ARuleWithNoCasesHasNoCountOnTheTestsTab()
    {
        using var harness = new Harness();
        harness.Render([Fx.FromText("readiness", Fx.WithoutCases())]);
        harness.Tab(WorkbenchTab.Tests).QuerySelector(".wb-pill").Should().BeNull();
    }
}

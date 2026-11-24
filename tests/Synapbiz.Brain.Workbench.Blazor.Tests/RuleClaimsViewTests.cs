using System.Text.Json.Nodes;
using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The sources and the authority a rule states about itself, on the Read tab and on its list line. The data is synthetic.</summary>
public sealed class RuleClaimsViewTests
{
    private const string Cited = """
        {"authority":"payment_condition","sources":[
          {"source":"guide.alpha","version":"2024","relation":"adapts","differences":[{"en":"Uses a lower threshold.","es":"Usa un umbral menor."},{"en":"Adds a second check.","es":"Agrega una segunda revisión."}]},
          {"source":"guide.beta","version":"1.2","relation":"implements"},
          {"source":"guide.gamma","version":"3","relation":"informed_by"}]}
        """;

    private static WorkbenchRule RuleWith(string metaJson, WorkbenchLocale locale = WorkbenchLocale.En) =>
        Fx.FromText("readiness", Fx.Edited("readiness", root =>
        {
            var meta = (JsonObject)root["meta"]!;
            foreach (var (key, value) in (JsonObject)JsonNode.Parse(metaJson)!)
            {
                meta[key] = value?.DeepClone();
            }
        }), locale: locale);

    private static readonly Dictionary<string, string> EnLabels = new() { ["guide.alpha"] = "Alpha guideline" };

    private static readonly Dictionary<string, string> EnAuthority = new() { ["payment_condition"] = "Payment condition" };

    [Fact]
    public void TheReadTabListsOneLineForEachSourceWithItsLabelVersionAndRelation()
    {
        using var harness = new Harness();
        harness.Render([RuleWith(Cited)], sourceLabels: EnLabels, authorityLabels: EnAuthority);
        var section = harness.Find("section.wb-claims");
        var lines = section.QuerySelectorAll("li.wb-source").ToList();
        lines.Should().HaveCount(3);
        Harness.Normalise(lines[0].QuerySelector(".wb-source-line")!.TextContent).Should().Be("Alpha guideline version 2024 adapts");
        Harness.Normalise(lines[1].QuerySelector(".wb-source-line")!.TextContent).Should().Be("guide.beta version 1.2 implements");
        Harness.Normalise(lines[2].QuerySelector(".wb-source-line")!.TextContent).Should().Be("guide.gamma version 3 is informed by");
        lines[0].QuerySelectorAll(".wb-source-diff li").Select(item => item.TextContent).Should().Equal("Uses a lower threshold.", "Adds a second check.");
        lines[1].QuerySelectorAll(".wb-source-diff").Should().BeEmpty();
    }

    [Fact]
    public void ASourceWithNoLabelIsShownAsCodeAndALabelledOneIsNot()
    {
        using var harness = new Harness();
        harness.Render([RuleWith(Cited)], sourceLabels: EnLabels);
        harness.FindAll("li.wb-source code.wb-source-name").Select(item => item.TextContent).Should().Equal("guide.beta", "guide.gamma");
        harness.FindAll("li.wb-source strong.wb-source-name").Select(item => item.TextContent).Should().Equal("Alpha guideline");
    }

    [Fact]
    public void TheDifferencesAndTheRelationFollowTheLanguage()
    {
        using var harness = new Harness();
        harness.Render([RuleWith(Cited, WorkbenchLocale.Es)], WorkbenchLocale.Es);
        var section = Harness.Normalise(harness.Find("section.wb-claims").TextContent);
        section.Should().Contain("Fuentes que sigue").And.Contain("Usa un umbral menor.").And.Contain("Agrega una segunda revisión.").And.Contain("versión 2024")
            .And.Contain("adapta").And.Contain("implementa").And.Contain("se inspira en");
        section.Should().NotContain("Uses a lower threshold.");
    }

    [Fact]
    public void AnEntryThatCannotBeReadIsSaidSoAndTheRestIsStillShown()
    {
        using var harness = new Harness();
        harness.Render([RuleWith("""{"sources":[{"source":"guide.alpha","version":"1","relation":"nope"},{"source":"guide.beta","version":"2","relation":"implements"},"text"]}""")]);
        harness.FindAll("li.wb-source").Should().HaveCount(3);
        harness.FindAll("li.wb-source-unreadable").Select(item => item.TextContent).Should().Equal("Source entry not readable", "Source entry not readable");
        harness.Find("li.wb-source:not(.wb-source-unreadable)").TextContent.Should().Contain("guide.beta");
    }

    [Fact]
    public void ASourcesMemberThatIsNotAnArrayGivesOneUnreadableEntryAndDoesNotBreakThePanel()
    {
        using var harness = new Harness();
        harness.Render([RuleWith("""{"sources":"guide.alpha"}""")]);
        harness.FindAll("li.wb-source-unreadable").Should().HaveCount(1);
        harness.FindAll(".wb-row").Should().NotBeEmpty();
    }

    [Fact]
    public void ARuleThatStatesNeitherHasNoBlock()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("readiness")], sourceLabels: EnLabels, authorityLabels: EnAuthority);
        harness.FindAll("section.wb-claims").Should().BeEmpty();
        harness.FindAll(".wb-badge-authority").Should().BeEmpty();
    }

    [Fact]
    public void TheAuthorityBadgeIsOnTheListLineAndTheReadTabWithTheHostsLabelAndNeverWithoutOne()
    {
        using var harness = new Harness();
        harness.Render([RuleWith(Cited)], authorityLabels: EnAuthority);
        harness.Find(".wb-rule .wb-badge-authority").TextContent.Should().Be("Payment condition");
        harness.Find("section.wb-claims .wb-badge-authority").TextContent.Should().Be("Payment condition");
        harness.Find("section.wb-claims .wb-claims-authority").TextContent.Should().Contain("Authority: Payment condition");

        harness.Render([RuleWith(Cited)]);
        harness.FindAll(".wb-badge-authority").Should().BeEmpty("a host with no label for the code gets no badge");
        harness.FindAll("section.wb-claims li.wb-source").Should().HaveCount(3, "the sources are shown even when the authority has no label");

        harness.Render([RuleWith(Cited)], authorityLabels: new Dictionary<string, string> { ["recommendation"] = "Recommendation" });
        harness.FindAll(".wb-badge-authority").Should().BeEmpty();
    }

    [Fact]
    public void AnAuthorityThatIsNotATextGivesNoBadge()
    {
        using var harness = new Harness();
        harness.Render([RuleWith("""{"authority":3}""")], authorityLabels: EnAuthority);
        harness.FindAll(".wb-badge-authority").Should().BeEmpty();
        harness.FindAll("section.wb-claims").Should().BeEmpty();
    }
}

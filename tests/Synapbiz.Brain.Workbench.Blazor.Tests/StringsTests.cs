using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class StringsTests
{
    private static readonly string[] Markings = ["band.simulate", "band.tests", "pill.simulation", "notice.privacy", "draft.bar", "pill.draft", "export.proposal.explain"];

    private static Harness Render(WorkbenchTab tab, IReadOnlyDictionary<string, string>? overrides, WorkbenchLocale locale = WorkbenchLocale.En, string? place = "the demo folder")
    {
        var harness = new Harness();
        harness.Render([Fx.Rule("readiness", locale)], locale, place: place, overrides: overrides);
        harness.Open(tab);
        return harness;
    }

    [Fact]
    public void EveryKeyHasASpanishAndAnEnglishDefault()
    {
        WorkbenchStrings.Keys.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
        foreach (var key in WorkbenchStrings.Keys)
        {
            WorkbenchStrings.Default(key, WorkbenchLocale.Es).Should().NotBeNullOrWhiteSpace(key);
            WorkbenchStrings.Default(key, WorkbenchLocale.En).Should().NotBeNullOrWhiteSpace(key);
        }
    }

    [Fact]
    public void TheKeysAreInOrdinalOrderAndNamedAreaDotNameInLowerCamelCase()
    {
        WorkbenchStrings.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
        WorkbenchStrings.Keys.Should().OnlyContain(key => System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z][A-Za-z0-9]*(\\.[a-z][A-Za-z0-9]*)+$"));
    }

    [Fact]
    public void PluralKeysComeInPairs()
    {
        var keys = WorkbenchStrings.Keys;
        foreach (var one in keys.Where(key => key.EndsWith(".one", StringComparison.Ordinal)))
        {
            keys.Should().Contain(one[..^4] + ".other");
        }
    }

    [Fact]
    public void AnUnknownKeyOrLocaleIsRefused()
    {
        var unknown = () => WorkbenchStrings.Default("no.such.key", WorkbenchLocale.En);
        unknown.Should().Throw<ArgumentException>();
        var none = () => WorkbenchStrings.Default(null!, WorkbenchLocale.En);
        none.Should().Throw<ArgumentNullException>();
        var locale = () => WorkbenchStrings.Default("tabs.read", (WorkbenchLocale)99);
        locale.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("band.simulate", "Simulado con datos de ejemplo. No es un caso real y no cambia nada.", "Simulated with example data. This is not a real case, and nothing changes.")]
    [InlineData("band.tests", "Los casos de prueba son ejemplos inventados.", "Test cases are made-up examples.")]
    [InlineData("notice.privacy", "**No identifique a personas reales.** No escriba nombres, fechas de nacimiento, números de identificación ni otros datos de una persona real en las notas ni en los nombres de los casos. Use ejemplos inventados.", "**Do not identify real people.** Do not write names, dates of birth, identification numbers or any other detail of a real person in notes or case names. Use made-up examples.")]
    [InlineData("state.draftLine", "Borrador: falta la aprobación.", "Draft: approval is pending.")]
    [InlineData("read.rowTextPrefix", "Texto que se muestra:", "Text shown:")]
    [InlineData("state.approvedLine", "Aprobada.", "Approved.")]
    [InlineData("load.failedBody", "Esta regla no se pudo leer en este equipo.", "This rule could not be read on this computer.")]
    [InlineData("pill.simulation", "Simulación", "Simulation")]
    public void TheFixedKeysHaveExactlyTheAgreedDefaults(string key, string es, string en)
    {
        WorkbenchStrings.Default(key, WorkbenchLocale.Es).Should().Be(es);
        WorkbenchStrings.Default(key, WorkbenchLocale.En).Should().Be(en);
    }

    [Fact]
    public void AnOverrideReplacesTheTextOfItsKeyForTheCurrentLocale()
    {
        using var harness = Render(WorkbenchTab.Read, new Dictionary<string, string>
        {
            ["tabs.read"] = "Overview",
            ["panel.version"] = "Revision {n}",
            ["read.facts"] = "Inputs",
        });
        harness.Tab(WorkbenchTab.Read).TextContent.Should().Be("Overview");
        harness.Find(".wb-head .wb-meta").TextContent.Should().Be("Revision 3");
        harness.FindAll("[role=tabpanel] .wb-h2")[1].TextContent.Should().Be("Inputs");
        harness.Tab(WorkbenchTab.Simulate).TextContent.Should().Be("Simulate");

        using var spanish = Render(WorkbenchTab.Read, new Dictionary<string, string> { ["tabs.read"] = "Resumen" }, WorkbenchLocale.Es);
        spanish.Tab(WorkbenchTab.Read).TextContent.Should().Be("Resumen");
    }

    [Fact]
    public void AnOverrideOfAKeyThatDoesNotExistIsIgnored()
    {
        using var harness = Render(WorkbenchTab.Read, new Dictionary<string, string> { ["no.such.key"] = "x" });
        harness.Tab(WorkbenchTab.Read).TextContent.Should().Be("Read");
    }

    [Fact]
    public void AnOrdinaryStringMayBeOverriddenWithAnEmptyText()
    {
        using var harness = Render(WorkbenchTab.Read, new Dictionary<string, string> { ["state.elsewhere"] = string.Empty });
        harness.FindAll(".wb-detail .wb-ph > .wb-meta")[1].TextContent.Should().BeEmpty();
    }

    [Fact]
    public void ABoldPairBecomesStrongAndNothingElseIsInterpreted()
    {
        using var harness = Render(WorkbenchTab.Read, new Dictionary<string, string>
        {
            ["list.title"] = "A **bold** title and *single* and **unmatched",
            ["panel.version"] = "**{n}**",
        });
        var title = harness.Find(".wb-list-panel .wb-title");
        title.InnerHtml.Should().Be("A <strong>bold</strong> title and *single* and **unmatched");
        harness.Find(".wb-head .wb-meta strong").TextContent.Should().Be("3");
    }

    [Fact]
    public void MarkupInAStringIsEncoded()
    {
        using var harness = Render(WorkbenchTab.Read, new Dictionary<string, string> { ["list.title"] = "<img src=x onerror=alert(1)> & <b>text</b>" });
        var title = harness.Find(".wb-list-panel .wb-title");
        title.TextContent.Should().Be("<img src=x onerror=alert(1)> & <b>text</b>");
        title.QuerySelectorAll("img, b").Should().BeEmpty();
        harness.FindAll("img").Should().BeEmpty();
    }

    [Fact]
    public void AStringUsedAsAnAttributeHasItsBoldMarksRemoved()
    {
        using var harness = Render(WorkbenchTab.Read, new Dictionary<string, string> { ["tabs.label"] = "**Sections** of the rule" });
        harness.Find("[role=tablist]").GetAttribute("aria-label").Should().Be("Sections of the rule");
    }

    [Fact]
    public void ABoldMarkInAValueThatIsFilledInIsNotInterpreted()
    {
        using var harness = Render(WorkbenchTab.Review, new Dictionary<string, string> { ["review.saved"] = "Saved to {place}." }, place: null);
        harness.Find(".wb-card-head .wb-rv").QuerySelectorAll("button")[0].Click();
        harness.Type("Your name", "Reviewer One");
        harness.Press("Export the review");
        harness.Find(".wb-saved").TextContent.Should().Be("Review saved.");
    }

    [Fact]
    public void TheFilledInPlaceIsEncodedToo()
    {
        using var harness = Render(WorkbenchTab.Review, null, place: "**a** <b>folder</b>");
        harness.Find(".wb-card-head .wb-rv").QuerySelectorAll("button")[0].Click();
        harness.Type("Your name", "Reviewer One");
        harness.Press("Export the review");
        harness.Find(".wb-saved").TextContent.Should().Be("Review saved to **a** <b>folder</b>.");
        harness.Find(".wb-saved").QuerySelectorAll("b, strong").Should().BeEmpty();
    }

    [Fact]
    public void TheMarkingsMayBeRewordedButNeverEmptied()
    {
        var reworded = new Dictionary<string, string>
        {
            ["band.simulate"] = "Example run only.",
            ["band.tests"] = "Invented examples.",
            ["pill.simulation"] = "Example",
            ["notice.privacy"] = "**Keep it anonymous.**",
        };
        using var simulate = Render(WorkbenchTab.Simulate, reworded);
        simulate.Find(".wb-band").TextContent.Should().Be("ExampleExample run only.");
        using var tests = Render(WorkbenchTab.Tests, reworded);
        tests.Find(".wb-band").TextContent.Should().Be("ExampleInvented examples.");
        using var review = Render(WorkbenchTab.Review, reworded);
        review.Find(".wb-notice strong").TextContent.Should().Be("Keep it anonymous.");
    }

    [Fact]
    public void AnEmptyNullOrBlankOverrideOfAMarkingIsIgnored()
    {
        var emptied = new Dictionary<string, string>
        {
            ["band.simulate"] = string.Empty,
            ["band.tests"] = "   ",
            ["pill.simulation"] = null!,
            ["notice.privacy"] = "\n\t",
        };
        using var simulate = Render(WorkbenchTab.Simulate, emptied);
        simulate.Find(".wb-band").TextContent.Should().Be("SimulationSimulated with example data. This is not a real case, and nothing changes.");
        using var tests = Render(WorkbenchTab.Tests, emptied);
        tests.Find(".wb-band").TextContent.Should().Be("SimulationTest cases are made-up examples.");
        using var review = Render(WorkbenchTab.Review, emptied);
        review.Find(".wb-notice").TextContent.Should().StartWith("Do not identify real people.");
    }

    [Fact]
    public void TheDraftMarkingsMayBeRewordedButNeverEmptied()
    {
        var reworded = new Dictionary<string, string>
        {
            ["draft.bar"] = "**Draft only.**",
            ["pill.draft"] = "Mine",
            ["export.proposal.explain"] = "Only a file.",
        };
        var emptied = new Dictionary<string, string>
        {
            ["draft.bar"] = " ",
            ["pill.draft"] = string.Empty,
            ["export.proposal.explain"] = null!,
        };
        foreach (var (overrides, bar, pill, explain) in new[]
        {
            (reworded, "Draft only.", "Mine", "Only a file."),
            (emptied, "You are viewing your draft.", "Draft changes", "A file is saved on this computer."),
        })
        {
            using var harness = new Harness();
            harness.Render([Fx.Editable("intake.first")], overrides: overrides);
            harness.Find(".wb-head button").Click();
            harness.Find(".wb-draftbar .wb-pill-draft").TextContent.Should().Be(pill);
            harness.Find(".wb-draftbar-t strong").TextContent.Should().Be(bar);
            harness.Open(WorkbenchTab.Changes);
            harness.Find("[role=tabpanel] .wb-card").TextContent.Should().Contain(explain);
        }
    }

    [Theory]
    [InlineData(WorkbenchTab.Simulate)]
    [InlineData(WorkbenchTab.Tests)]
    public void TheSimulationBandAndPillAreOnEveryTabThatSimulatesOrTests(WorkbenchTab tab)
    {
        using var harness = Render(tab, null, WorkbenchLocale.Es);
        harness.Find(".wb-band .wb-pill-sim").TextContent.Should().Be("Simulación");
        harness.Find(".wb-band").TextContent.Should().Contain(tab == WorkbenchTab.Simulate ? "Simulado con datos de ejemplo" : "Los casos de prueba son ejemplos inventados");
    }

    [Fact]
    public void ThePrivacyNoticeIsOnTheReviewTabAndInTheCaseForm()
    {
        using var harness = Render(WorkbenchTab.Tests, null);
        harness.FindAll(".wb-notice").Should().BeEmpty();
        harness.Press("Add a case");
        harness.Find(".wb-case-form .wb-notice").TextContent.Should().StartWith("Do not identify real people.");
        harness.Open(WorkbenchTab.Review);
        harness.Find(".wb-notice").TextContent.Should().StartWith("Do not identify real people.");
    }

    [Fact]
    public void NoParameterHidesAMarking()
    {
        var parameters = typeof(RuleWorkbench).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<ParameterAttribute>() is not null)
            .Select(property => property.Name);
        parameters.Should().BeEquivalentTo("Rules", "Locale", "Session", "Now", "OnExport", "TextOverrides");
    }

    [Fact]
    public void TheMandatoryKeysAreAllKeysOfTheTable()
    {
        WorkbenchStrings.Keys.Should().Contain(Markings);
    }

    [Fact]
    public void ThePublicSurfaceIsTheRootComponentAndTheDocumentedTypes()
    {
        var names = typeof(RuleWorkbench).Assembly.GetExportedTypes().Select(type => type.Name).Order(StringComparer.Ordinal);
        names.Should().Equal(
            "RuleApprovalState", "RuleWorkbench", "WorkbenchExport", "WorkbenchExportFile", "WorkbenchOutcomeText", "WorkbenchRule", "WorkbenchSession", "WorkbenchStrings", "WorkbenchTab");
    }
}

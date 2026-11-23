using Bunit;
using FluentAssertions;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>No screen of the panels shows an identifier the reviewer never wrote: rule id, row key, row id, kind key or an error code.</summary>
public sealed class IdentifierGuardTests
{
    public static TheoryData<string, WorkbenchLocale> Cases()
    {
        var data = new TheoryData<string, WorkbenchLocale>();
        foreach (var name in Fx.Names)
        {
            data.Add(name, WorkbenchLocale.En);
            data.Add(name, WorkbenchLocale.Es);
        }

        return data;
    }

    private static List<string> Forbidden(WorkbenchRule rule)
    {
        var brain = rule.Rule!;
        var words = new List<string> { brain.Id, brain.Kind.Key.Value, "workbench.", "decisions.", "engine code" };
        words.AddRange(brain.Table.Rows.Select(row => row.Key));
        words.AddRange(brain.RowIdsByKey.Values);
        if (brain.Table.DefaultRow is { } fallback)
        {
            words.Add(fallback.Key);
        }

        return [.. words.Where(word => word.Length > 0).Distinct(StringComparer.Ordinal)];
    }

    private static void Check(Harness harness, List<string> forbidden, string where, WorkbenchRule? rule = null)
    {
        // A case name is the author's own sentence and may use any word, so it is taken out before the identifiers are looked for.
        var markup = harness.Cut.Markup;
        foreach (var golden in rule?.Rule?.Goldens ?? [])
        {
            markup = markup.Replace(System.Net.WebUtility.HtmlEncode(golden.Name), string.Empty, StringComparison.Ordinal).Replace(golden.Name, string.Empty, StringComparison.Ordinal);
        }

        foreach (var word in forbidden)
        {
            markup.Should().NotContain(word, where);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NoTabShowsAnIdentifier(string name, WorkbenchLocale locale)
    {
        var rule = Fx.Rule(name, locale);
        var forbidden = Forbidden(rule);
        var es = locale == WorkbenchLocale.Es;
        using var harness = new Harness();
        harness.Render([rule], locale);
        Check(harness, forbidden, "list and Read", rule);

        harness.FindAll(".wb-rv")[0].QuerySelectorAll("button")[1].Click();
        harness.FindAll("textarea")[0].Change("A note.");
        Check(harness, forbidden, "Read with a note", rule);

        harness.Open(WorkbenchTab.Simulate);
        Check(harness, forbidden, "Simulate before a run", rule);
        harness.Press(es ? "Simular" : "Simulate");
        Check(harness, forbidden, "Simulate with input errors", rule);
        harness.Find("select[id$=-load]").Change("0");
        harness.Press(es ? "Simular" : "Simulate");
        Check(harness, forbidden, "Simulate with a result", rule);
        if (name == "intake.unique")
        {
            harness.Type("channel", "Phone");
            harness.Choose("urgent", "true");
            harness.Press(es ? "Simular" : "Simulate");
            Check(harness, forbidden, "Simulate with a refusal", rule);
            harness.FindAll("[role=alert]").Should().ContainSingle();
            harness.Press(es ? "Agregar a la revisión" : "Add to the review");
            Check(harness, forbidden, "Review after a refusal", rule);
            harness.Open(WorkbenchTab.Simulate);
            harness.Type("channel", "Web");
            harness.Press(es ? "Simular" : "Simulate");
        }

        harness.Press(es ? "Guardar como caso de prueba" : "Save as a test case");
        Check(harness, forbidden, "case form from a simulation", rule);
        harness.Press(es ? "Guardar caso" : "Save case");
        Check(harness, forbidden, "case form with errors", rule);
        harness.Type(es ? "Nombre del caso" : "Case name", "A made-up case");
        harness.Press(es ? "Guardar caso" : "Save case");
        Check(harness, forbidden, "Tests with a proposed case", rule);

        harness.Open(WorkbenchTab.Review);
        Check(harness, forbidden, "Review", rule);
        harness.Type(es ? "Su nombre" : "Your name", "Reviewer One");
        harness.Press(es ? "Exportar la revisión" : "Export the review");
        Check(harness, forbidden, "Review after the export", rule);
    }

    [Fact]
    public void ARefusedCaseOfTheDocumentShowsNoCode()
    {
        var rule = Fx.FromText("intake.first", Fx.TextOf("intake.first"), Fx.RequiredAccountContract());
        using var harness = new Harness();
        harness.Render([rule]);
        harness.Open(WorkbenchTab.Tests);
        Check(harness, Forbidden(rule), "Tests with a refused case", rule);
    }

    [Fact]
    public void FactsWithNoLabelAreHumanisedNotShownAsTheirNames()
    {
        using var harness = new Harness();
        harness.Render([Fx.Rule("intake.unique")]);
        var markup = harness.Cut.Markup;
        markup.Should().NotContain("ageDays").And.NotContain("receivedOn");
        harness.FindAll(".wb-kv dt").Select(item => item.TextContent).Should().Contain(["age days", "received on"]);
    }
}

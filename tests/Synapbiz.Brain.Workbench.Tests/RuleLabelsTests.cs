using System.Text.Json.Nodes;
using FluentAssertions;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class RuleLabelsTests
{
    private static JsonObject Meta(string json) => (JsonObject)JsonNode.Parse(json)!;

    private const string Both = """
        {
          "display": {
            "en": { "title": "Intake", "facts": { "channel": "Channel" }, "outcomes": { "route": "Route" },
                    "values": { "channel": { "Web": "the website" } }, "rows": { "r1": "First row" } },
            "es": { "title": "Admisión", "facts": { "channel": "Canal" }, "outcomes": { "route": "Ruta" },
                    "values": { "channel": { "Web": "el sitio web" } }, "rows": { "r1": "Primera fila" } }
          }
        }
        """;

    [Fact]
    public void EmptyHasNothingAndNeverNullMembers()
    {
        var empty = RuleLabels.Empty;

        empty.Title.Should().BeNull();
        empty.Facts.Should().BeEmpty();
        empty.OutcomeFields.Should().BeEmpty();
        empty.Values.Should().BeEmpty();
        empty.Rows.Should().BeEmpty();
    }

    [Fact]
    public void ANullDictionaryIsKeptAsAnEmptyOne()
    {
        var labels = new RuleLabels { Facts = null!, OutcomeFields = null!, Values = null!, Rows = null! };

        labels.Facts.Should().BeEmpty();
        labels.OutcomeFields.Should().BeEmpty();
        labels.Values.Should().BeEmpty();
        labels.Rows.Should().BeEmpty();
    }

    [Fact]
    public void EnglishLabelsAreReadFromTheEnTag()
    {
        var labels = RuleLabels.FromMeta(Meta(Both), WorkbenchLocale.En);

        labels.Title.Should().Be("Intake");
        labels.Facts.Should().Equal(new Dictionary<string, string> { ["channel"] = "Channel" });
        labels.OutcomeFields.Should().Equal(new Dictionary<string, string> { ["route"] = "Route" });
        labels.Values["channel"].Should().Equal(new Dictionary<string, string> { ["Web"] = "the website" });
        labels.Rows.Should().Equal(new Dictionary<string, string> { ["r1"] = "First row" });
    }

    [Fact]
    public void SpanishLabelsAreReadFromTheEsTag()
    {
        var labels = RuleLabels.FromMeta(Meta(Both), WorkbenchLocale.Es);

        labels.Title.Should().Be("Admisión");
        labels.Facts["channel"].Should().Be("Canal");
        labels.OutcomeFields["route"].Should().Be("Ruta");
        labels.Values["channel"]["Web"].Should().Be("el sitio web");
        labels.Rows["r1"].Should().Be("Primera fila");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "display": "none" }""")]
    [InlineData("""{ "display": [] }""")]
    [InlineData("""{ "display": { "fr": { "title": "x" } } }""")]
    [InlineData("""{ "display": { "en": "x" } }""")]
    [InlineData("""{ "display": { "en": null } }""")]
    public void AMissingOrMisshapenDisplayGivesNothing(string meta)
    {
        var labels = RuleLabels.FromMeta(Meta(meta), WorkbenchLocale.En);

        labels.Title.Should().BeNull();
        labels.Facts.Should().BeEmpty();
        labels.OutcomeFields.Should().BeEmpty();
        labels.Values.Should().BeEmpty();
        labels.Rows.Should().BeEmpty();
    }

    [Fact]
    public void AMemberOfTheWrongKindIsIgnored()
    {
        var labels = RuleLabels.FromMeta(
            Meta("""{ "display": { "en": { "title": 7, "facts": [ "a" ], "outcomes": "x", "values": 3, "rows": null } } }"""),
            WorkbenchLocale.En);

        labels.Title.Should().BeNull();
        labels.Facts.Should().BeEmpty();
        labels.OutcomeFields.Should().BeEmpty();
        labels.Values.Should().BeEmpty();
        labels.Rows.Should().BeEmpty();
    }

    [Fact]
    public void AnEntryOfTheWrongKindIsIgnoredAndItsNeighboursKept()
    {
        var labels = RuleLabels.FromMeta(
            Meta("""
                { "display": { "en": {
                    "facts": { "a": "Alpha", "b": 2, "c": null, "d": { "x": "y" } },
                    "values": { "a": { "v": "Vee", "w": 5 }, "b": "text", "c": { "only": 1 }, "d": [] }
                } } }
                """),
            WorkbenchLocale.En);

        labels.Facts.Should().Equal(new Dictionary<string, string> { ["a"] = "Alpha" });
        labels.Values.Should().ContainSingle().Which.Key.Should().Be("a");
        labels.Values["a"].Should().Equal(new Dictionary<string, string> { ["v"] = "Vee" });
    }

    [Fact]
    public void ANullMetaIsRefused()
    {
        var act = () => RuleLabels.FromMeta(null!, WorkbenchLocale.En);

        act.Should().Throw<ArgumentNullException>();
    }
}

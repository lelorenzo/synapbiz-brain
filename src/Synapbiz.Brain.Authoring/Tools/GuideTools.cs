using System.Text.Json.Nodes;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>The tools that teach the assistant the format and tell it what the host offers.</summary>
internal static class GuideTools
{
    internal static IEnumerable<AuthoringTool> Create(ToolContext context)
    {
        yield return new AuthoringTool(
            "brain_guide",
            "The format reference for assistants, in sections: what a process document is, step kinds, routes, timeouts and windows, loops, triggers, scenarios, labels, what a good process has, rules, the working method and two worked examples. "
            + "Without `section` it lists the sections and gives the working method. Read it before you write anything.",
            ToolSchemas.Object([("section", ToolSchemas.Enum("The section to read.", [.. BrainGuide.Sections.Select(section => section.Id)]))]),
            context,
            Guide);

        yield return new AuthoringTool(
            "brain_catalog",
            "What the host offers to processes: the ports (with kind, delivery and whether they contact a person), the events, the task queues, the stage names in use and the approved rules. A process that names anything outside the catalog is refused as not available here. A list that is `null` is not restricted. Definitions only.",
            ToolSchemas.Object([]),
            context,
            Catalog);
    }

    private static JsonObject Guide(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var section = AuthoringJson.Text(input["section"]);
        var data = BrainGuide.ToJson(section);
        return Results.Ok(AuthoringText.Text(locale, section is null ? "say.guide.list" : "say.guide.section"), data);
    }

    private static JsonObject Catalog(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        var catalog = context.Catalog(locale, out var refusal);
        if (catalog is null)
        {
            return refusal!;
        }

        var rules = context.ApprovedRules(catalog).Select(item => (JsonNode?)new JsonObject { ["id"] = item.Id, ["loads"] = item.Rule is not null, ["kind"] = item.Rule?.Kind.Key.Value }).ToList();
        var data = new JsonObject
        {
            ["restricted"] = catalog.Ports is not null || catalog.Events is not null || catalog.Queues is not null || catalog.Stages is not null,
            ["ports"] = catalog.Ports is null ? null : new JsonArray([.. catalog.Ports.Select(port => (JsonNode?)new JsonObject
            {
                ["name"] = port.Name,
                ["kind"] = port.Kind,
                ["delivery"] = port.Delivery,
                ["contactsPerson"] = port.ContactsPerson,
                ["description"] = port.Description,
                ["inputSchema"] = port.InputSchema?.DeepClone(),
                ["outputSchema"] = port.OutputSchema?.DeepClone(),
            })]),
            ["events"] = Names(catalog.Events),
            ["queues"] = Names(catalog.Queues),
            ["stages"] = catalog.Stages is null ? null : Views.Strings(catalog.Stages),
            ["rules"] = new JsonArray([.. rules]),
        };
        var say = AuthoringText.Text(
            locale,
            catalog.Ports is null && catalog.Events is null && catalog.Queues is null ? "say.catalog.open" : "say.catalog.some",
            ("ports", AuthoringText.Number(catalog.Ports?.Count ?? 0)),
            ("events", AuthoringText.Number(catalog.Events?.Count ?? 0)),
            ("queues", AuthoringText.Number(catalog.Queues?.Count ?? 0)),
            ("rules", AuthoringText.Number(rules.Count)));
        return Results.Ok(say, data);
    }

    private static JsonArray? Names(IReadOnlyList<CatalogName>? names) =>
        names is null ? null : new JsonArray([.. names.Select(item => (JsonNode?)new JsonObject { ["name"] = item.Name, ["description"] = item.Description })]);
}

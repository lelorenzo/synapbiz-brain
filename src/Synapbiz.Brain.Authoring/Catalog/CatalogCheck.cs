using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Authoring;

/// <summary>A name in a draft that the catalog does not offer, with what it does offer.</summary>
/// <param name="Code">The code, <c>authoring.catalog.*_unavailable</c> or a mismatch code.</param>
/// <param name="Path">The JSON Pointer of the name in the draft.</param>
/// <param name="What">What kind of thing: <c>port</c>, <c>event</c>, <c>queue</c> or <c>stage</c>.</param>
/// <param name="Name">The name the draft wrote.</param>
/// <param name="Available">The names the catalog offers for that kind of thing, in the catalog's order.</param>
/// <param name="Step">The step that wrote it; null for a stage, a trigger or <c>accepts</c>.</param>
/// <param name="Detail">For a mismatch, what the catalog says (the kind of the port, its deliveries); null otherwise.</param>
public sealed record CatalogProblem(string Code, string Path, string What, string Name, IReadOnlyList<string> Available, string? Step, string? Detail);

/// <summary>Checks a draft against the catalog: "not available here", with the list of what is.</summary>
public static class CatalogCheck
{
    /// <summary>A port that is not in the catalog.</summary>
    public const string PortUnavailable = "authoring.catalog.port_unavailable";

    /// <summary>A port used by a kind of step it is not offered for.</summary>
    public const string PortKind = "authoring.catalog.port_kind";

    /// <summary>An <c>action</c> with a delivery its port does not support.</summary>
    public const string DeliveryUnsupported = "authoring.catalog.delivery_unsupported";

    /// <summary>An event that is not in the catalog.</summary>
    public const string EventUnavailable = "authoring.catalog.event_unavailable";

    /// <summary>A task queue that is not in the catalog.</summary>
    public const string QueueUnavailable = "authoring.catalog.queue_unavailable";

    /// <summary>A stage name that is not in the catalog.</summary>
    public const string StageUnavailable = "authoring.catalog.stage_unavailable";

    /// <summary>Every code this check can report.</summary>
    public static IReadOnlyList<string> Codes { get; } = [PortUnavailable, PortKind, DeliveryUnsupported, EventUnavailable, QueueUnavailable, StageUnavailable];

    /// <summary>
    /// Reads the document as plain JSON, so that a draft the loader refuses can still be checked, and reports every port, event, queue and
    /// stage it names that the catalog does not offer. A list that the catalog does not have is not checked.
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="document">The draft document.</param>
    public static IReadOnlyList<CatalogProblem> Draft(AuthoringCatalog catalog, JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(document);
        var problems = new List<CatalogProblem>();
        if (document["body"] is not JsonObject body)
        {
            return problems;
        }

        if (catalog.Stages is { } stages && body["stages"] is JsonArray used)
        {
            for (var index = 0; index < used.Count; index++)
            {
                if (AuthoringJson.Text(used[index]) is { } stage && !stages.Contains(stage, StringComparer.Ordinal))
                {
                    problems.Add(new CatalogProblem(StageUnavailable, "/body/stages/" + index, "stage", stage, stages, null, null));
                }
            }
        }

        if (catalog.Events is { } events)
        {
            Names(problems, body["accepts"] as JsonArray, "/body/accepts", events.Select(item => item.Name).ToList());
            if (body["triggers"] is JsonArray triggers)
            {
                for (var index = 0; index < triggers.Count; index++)
                {
                    if (triggers[index] is JsonObject trigger && AuthoringJson.Text(trigger["kind"]) == "event" && AuthoringJson.Text(trigger["event"]) is { } name
                        && !events.Any(item => item.Name == name))
                    {
                        problems.Add(new CatalogProblem(EventUnavailable, "/body/triggers/" + index + "/event", "event", name, [.. events.Select(item => item.Name)], null, null));
                    }
                }
            }
        }

        if (body["steps"] is JsonObject steps)
        {
            foreach (var (id, node) in steps)
            {
                if (node is JsonObject step)
                {
                    Step(catalog, problems, id, step);
                }
            }
        }

        return problems;
    }

    private static void Names(List<CatalogProblem> problems, JsonArray? used, string path, List<string> available)
    {
        for (var index = 0; used is not null && index < used.Count; index++)
        {
            if (AuthoringJson.Text(used[index]) is { } name && !available.Contains(name, StringComparer.Ordinal))
            {
                problems.Add(new CatalogProblem(EventUnavailable, AuthoringJson.Pointer(path, index), "event", name, available, null, null));
            }
        }
    }

    private static void Step(AuthoringCatalog catalog, List<CatalogProblem> problems, string id, JsonObject step)
    {
        var kind = AuthoringJson.Text(step["kind"]);
        var at = "/body/steps/" + Escape(id);
        if (catalog.Ports is { } ports && kind is "read" or "action" or "ai" or "poll" && AuthoringJson.Text(step["port"]) is { } portName)
        {
            var port = ports.FirstOrDefault(item => item.Name == portName);
            var wanted = kind == "poll" ? "read" : kind;
            if (port is null)
            {
                problems.Add(new CatalogProblem(PortUnavailable, at + "/port", "port", portName, [.. ports.Where(item => item.Kind == wanted).Select(item => item.Name)], id, null));
            }
            else if (port.Kind != wanted)
            {
                problems.Add(new CatalogProblem(PortKind, at + "/port", "port", portName, [.. ports.Where(item => item.Kind == wanted).Select(item => item.Name)], id, port.Kind));
            }
            else if (kind == "action" && AuthoringJson.Text(step["delivery"]) is { } delivery && port.Delivery != "either" && port.Delivery != delivery)
            {
                problems.Add(new CatalogProblem(DeliveryUnsupported, at + "/delivery", "port", portName, [], id, port.Delivery));
            }
        }

        if (catalog.Events is { } events)
        {
            var available = events.Select(item => item.Name).ToList();
            if (kind == "wait" && AuthoringJson.Text(step["event"]) is { } waited && !available.Contains(waited, StringComparer.Ordinal))
            {
                problems.Add(new CatalogProblem(EventUnavailable, at + "/event", "event", waited, available, id, null));
            }

            if (step["interruptBy"] is JsonObject interrupt && AuthoringJson.Text(interrupt["event"]) is { } interrupting && !available.Contains(interrupting, StringComparer.Ordinal))
            {
                problems.Add(new CatalogProblem(EventUnavailable, at + "/interruptBy/event", "event", interrupting, available, id, null));
            }
        }

        if (catalog.Queues is { } queues && kind == "humanTask" && AuthoringJson.Text(step["queue"]) is { } queue && !queues.Any(item => item.Name == queue))
        {
            problems.Add(new CatalogProblem(QueueUnavailable, at + "/queue", "queue", queue, [.. queues.Select(item => item.Name)], id, null));
        }
    }

    private static string Escape(string id) => id.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}

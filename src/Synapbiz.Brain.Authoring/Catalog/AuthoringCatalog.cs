using System.Text.Json.Nodes;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Authoring;

/// <summary>A port the host offers to flows.</summary>
/// <param name="Name">The port name a step writes in <c>port</c>.</param>
/// <param name="Kind">The kind of step that may use it: <c>read</c> (also for a <c>poll</c> step), <c>action</c> or <c>ai</c>.</param>
/// <param name="Delivery">The deliveries it supports: <c>atLeastOnce</c>, <c>atMostOnce</c> or <c>either</c>.</param>
/// <param name="ContactsPerson">True when calling it reaches a person.</param>
/// <param name="Description">What it does, in a sentence; may be null.</param>
/// <param name="InputSchema">A JSON Schema of what it takes; may be null.</param>
/// <param name="OutputSchema">A JSON Schema of what it gives; may be null.</param>
public sealed record CatalogPort(string Name, string Kind, string Delivery, bool ContactsPerson, string? Description, JsonObject? InputSchema, JsonObject? OutputSchema);

/// <summary>An event or a task queue the host offers.</summary>
/// <param name="Name">The name a step writes.</param>
/// <param name="Description">What it is, in a sentence; may be null.</param>
public sealed record CatalogName(string Name, string? Description);

/// <summary>
/// What a host offers to the flows being drafted: ports, events, task queues, stage names and the fact contracts of the rules. A list that is
/// null is not restricted; a list that is present, even empty, is all there is.
/// </summary>
public sealed record AuthoringCatalog
{
    /// <summary>The catalog of a workspace that supplied none: nothing is restricted.</summary>
    public static AuthoringCatalog Unrestricted { get; } = new();

    /// <summary>The ports, or null for no restriction.</summary>
    public IReadOnlyList<CatalogPort>? Ports { get; init; }

    /// <summary>The events, or null for no restriction.</summary>
    public IReadOnlyList<CatalogName>? Events { get; init; }

    /// <summary>The task queues, or null for no restriction.</summary>
    public IReadOnlyList<CatalogName>? Queues { get; init; }

    /// <summary>The stage names in use, or null for no restriction.</summary>
    public IReadOnlyList<string>? Stages { get; init; }

    /// <summary>The module contracts the rules are written against, as the documents the host supplied.</summary>
    public IReadOnlyList<JsonObject> ContractDocuments { get; init; } = [];

    /// <summary>The contracts, parsed.</summary>
    public IReadOnlyList<BrainContract> Contracts => [.. ContractDocuments.Select(document => BrainContract.Parse(AuthoringJson.CompactText(document)))];
}

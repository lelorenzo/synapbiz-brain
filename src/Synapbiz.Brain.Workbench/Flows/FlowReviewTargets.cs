using System.Collections.ObjectModel;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>
/// The things of a flow a reviewer can mark, as the targets of a <c>brain.process-review@1</c> document, in the order a screen lists
/// and an export writes them: the flow, its triggers, its stages, its steps, its routes and its scenarios.
/// </summary>
public static class FlowReviewTargets
{
    /// <summary>The target of the whole flow.</summary>
    public const string Flow = "process";

    /// <summary>Every valid target of a flow, in display order: <c>process</c>, then <c>trigger:&lt;id&gt;</c> for each trigger, <c>stage:&lt;id&gt;</c>, <c>step:&lt;id&gt;</c>, <c>route:&lt;step&gt;/&lt;label&gt;</c> and <c>scenario:&lt;name&gt;</c>, each in document order and none twice.</summary>
    /// <param name="process">The loaded process.</param>
    public static IReadOnlyList<string> Of(BrainProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var outline = ProcessOutline.From(process);
        var targets = new List<string> { Flow };
        targets.AddRange(TriggerIds(process).Select(Trigger));
        targets.AddRange(outline.Stages.Select(stage => "stage:" + stage));
        targets.AddRange(outline.Steps.Select(step => "step:" + step.Id));
        targets.AddRange(outline.Steps.SelectMany(step => step.Routes.Select(route => Route(step.Id, route.Label))));
        targets.AddRange(outline.Scenarios.Select(name => "scenario:" + name));
        return new ReadOnlyCollection<string>([.. targets.Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>The id a trigger has as a review target: the id of a schedule, or the event name of an event trigger. One for each trigger, in document order, none twice.</summary>
    /// <param name="process">The loaded process.</param>
    public static IReadOnlyList<string> TriggerIds(BrainProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return new ReadOnlyCollection<string>([.. ProcessOutline.From(process).Triggers.Select(IdOf).Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>The target of a route.</summary>
    /// <param name="step">The id of the step the route leaves.</param>
    /// <param name="label">The label of the route.</param>
    public static string Route(string step, string label)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(label);
        return "route:" + step + "/" + label;
    }

    /// <summary>The target of a trigger.</summary>
    /// <param name="id">The id from <see cref="TriggerIds"/>.</param>
    public static string Trigger(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return "trigger:" + id;
    }

    internal static string IdOf(ProcessTriggerDeclaration trigger) => trigger switch
    {
        ScheduleTriggerDeclaration schedule => schedule.Id,
        EventTriggerDeclaration declared => declared.Event,
        _ => string.Empty,
    };
}

using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>A child process a flow starts, resolved inside the set of documents the library was given.</summary>
/// <param name="ProcessId">The id of the child process as the step writes it.</param>
/// <param name="Title">The title of the child in the asked language (the highest version in the set), or null when the child is missing.</param>
/// <param name="IsMissing">True when no document of the set has that id.</param>
public sealed record FlowChildLink(string ProcessId, string? Title, bool IsMissing);

/// <summary>One flow of the library: what a list screen shows about a document.</summary>
public sealed record FlowEntry
{
    /// <summary>The id of the document.</summary>
    public required string Id { get; init; }

    /// <summary>The version of the document.</summary>
    public required long Version { get; init; }

    /// <summary>The content hash of the document text that was loaded.</summary>
    public required string ContentHash { get; init; }

    /// <summary>The title in the asked language, else English, else a readable form of the id.</summary>
    public required string Title { get; init; }

    /// <summary>The case type the flow runs for.</summary>
    public required string Subject { get; init; }

    /// <summary>The labels of the stages, in document order.</summary>
    public required IReadOnlyList<string> StageLabels { get; init; }

    /// <summary>What starts the flow, in words, one sentence per trigger (<c>Starts when `case.opened` arrives</c>, <c>Runs every day at 06:00 America/New_York</c>).</summary>
    public required IReadOnlyList<string> Triggers { get; init; }

    /// <summary>How many steps there are of each kind; kinds with none are left out.</summary>
    public required IReadOnlyDictionary<OutlineStepKind, int> StepCounts { get; init; }

    /// <summary>The number of embedded scenarios.</summary>
    public required int ScenarioCount { get; init; }

    /// <summary>True when every embedded scenario passes (also true when there are none).</summary>
    public required bool ScenariosPass { get; init; }

    /// <summary>The number of routes at least one embedded scenario takes.</summary>
    public required int CoveredRoutes { get; init; }

    /// <summary>The number of routes of the document.</summary>
    public required int TotalRoutes { get; init; }

    /// <summary>The child processes the flow starts, in order of first use.</summary>
    public required IReadOnlyList<FlowChildLink> Children { get; init; }

    /// <summary>The ids of the documents of the set that start this flow as a child, in ordinal order.</summary>
    public required IReadOnlyList<string> StartedBy { get; init; }

    /// <summary>The rule references (<c>module/rule</c>) the flow uses.</summary>
    public required IReadOnlyList<string> Rules { get; init; }
}

/// <summary>The list of flows a host has loaded, as plain data for a screen.</summary>
public static class FlowLibrary
{
    /// <summary>Describes every document of the set, ordered by id, then by version, then by content hash.</summary>
    /// <param name="processes">The loaded processes.</param>
    /// <param name="locale">The language of the titles, labels and sentences.</param>
    public static IReadOnlyList<FlowEntry> From(IEnumerable<BrainProcess> processes, WorkbenchLocale locale)
    {
        ArgumentNullException.ThrowIfNull(processes);
        var set = processes.OrderBy(process => process.Id, StringComparer.Ordinal).ThenBy(process => process.Version).ThenBy(process => ProcessOutline.From(process).ContentHash, StringComparer.Ordinal).ToList();
        var outlines = set.Select(ProcessOutline.From).ToList();
        return [.. set.Select((process, index) => Entry(process, outlines[index], set, outlines, locale))];
    }

    private static FlowEntry Entry(BrainProcess process, ProcessOutline outline, List<BrainProcess> set, List<ProcessOutline> outlines, WorkbenchLocale locale)
    {
        var labels = new FlowLabels(process, locale);
        var coverage = FlowCoverage.Of(process);
        var children = outline.Steps.Select(step => step.Process).OfType<string>().Distinct(StringComparer.Ordinal)
            .Select(id => Child(id, set, locale)).ToList();
        return new FlowEntry
        {
            Id = outline.Id,
            Version = outline.Version,
            ContentHash = outline.ContentHash,
            Title = labels.Title(),
            Subject = outline.Subject,
            StageLabels = [.. outline.Stages.Select(labels.Stage)],
            Triggers = [.. outline.Triggers.Select(trigger => FlowDescriber.DescribeTrigger(trigger, locale))],
            StepCounts = outline.Steps.GroupBy(step => step.Kind).OrderBy(group => group.Key).ToDictionary(group => group.Key, group => group.Count()),
            ScenarioCount = outline.Scenarios.Count,
            ScenariosPass = process.RunScenarios().IsOk,
            CoveredRoutes = coverage.CoveredRoutes,
            TotalRoutes = coverage.TotalRoutes,
            Children = children,
            StartedBy = [.. outlines.Where(other => other.Steps.Any(step => string.Equals(step.Process, outline.Id, StringComparison.Ordinal))).Select(other => other.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            Rules = outline.Uses,
        };
    }

    private static FlowChildLink Child(string id, List<BrainProcess> set, WorkbenchLocale locale)
    {
        var document = set.Where(process => string.Equals(process.Id, id, StringComparison.Ordinal)).OrderByDescending(process => process.Version).FirstOrDefault();
        return document is null ? new FlowChildLink(id, null, true) : new FlowChildLink(id, new FlowLabels(document, locale).Title(), false);
    }
}

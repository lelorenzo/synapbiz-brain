using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>The five tabs of the flow panel.</summary>
public enum FlowWorkbenchTab
{
    /// <summary>The flow in plain language, stage by stage.</summary>
    Read = 0,

    /// <summary>The flow as a diagram.</summary>
    Diagram = 1,

    /// <summary>A made-up case run through the flow on a virtual clock.</summary>
    Try = 2,

    /// <summary>The tests the document carries, the ones the reviewer proposes, and what they cover.</summary>
    Tests = 3,

    /// <summary>The reviewer's marks and notes and the export.</summary>
    Review = 4,
}

/// <summary>The views of the process workbench.</summary>
public enum FlowWorkbenchView
{
    /// <summary>The process map: main processes with their phases and the subprocesses each phase starts, scheduled tasks, shared pieces. The default.</summary>
    Map = 0,

    /// <summary>The list of processes, grouped by kind.</summary>
    List = 1,

    /// <summary>The map with the open cases counted on each process and phase, and the path of one case over its diagram. Offered only when the host gives a live source.</summary>
    Live = 2,
}

/// <summary>One flow of the list, as the host supplies it.</summary>
public sealed record WorkbenchFlow
{
    /// <summary>A stable key, unique in the list and chosen by the host. It is never shown.</summary>
    public required string Key { get; init; }

    /// <summary>The heading the flow is listed under. Flows keep the order given; a heading is drawn each time the group changes.</summary>
    public string? Group { get; init; }

    /// <summary>The loaded flow; null when the host could not load it.</summary>
    public BrainProcess? Process { get; init; }

    /// <summary>The title to show when <see cref="Process"/> is null.</summary>
    public string? FallbackTitle { get; init; }

    /// <summary>A sentence the host writes under the title; null draws nothing.</summary>
    public string? StateLine { get; init; }

    /// <summary>A text the host writes after the state line, for example where the flow comes from; null draws nothing.</summary>
    public string? SourceLine { get; init; }

    /// <summary>True when the panels can show the flow: it is loaded.</summary>
    internal bool IsReadable => Process is not null;
}

/// <summary>One file of an export: the review of one flow.</summary>
/// <param name="SuggestedName">A file name the host may use, <c>review-&lt;flow id&gt;-v&lt;version&gt;.json</c>.</param>
/// <param name="Json">The <c>brain.process-review@1</c> document.</param>
/// <param name="Review">The review the document was written from.</param>
public sealed record FlowWorkbenchExportFile(string SuggestedName, string Json, FlowReview Review);

/// <summary>What the reviewer exports: one file for each flow that has a mark, a note or a proposed scenario.</summary>
/// <param name="Files">The files, in the order of the flow list.</param>
public sealed record FlowWorkbenchExport(IReadOnlyList<FlowWorkbenchExportFile> Files);

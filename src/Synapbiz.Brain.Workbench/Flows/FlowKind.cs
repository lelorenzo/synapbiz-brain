using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>
/// The kind of a process of a library, derived from its triggers and from who starts whom, with no host input. The rule is pinned on
/// every reference document and fixture (see <see cref="FlowKinds.Of"/>).
/// </summary>
public enum FlowKind
{
    /// <summary>An event (or nothing in the document) starts it and no other process does: the processes a case lives in.</summary>
    Main = 0,

    /// <summary>Another process of the library starts it through a subprocess step and it has no trigger of its own.</summary>
    Subprocess = 1,

    /// <summary>It has a schedule trigger: it runs by the clock, not for a case.</summary>
    Scheduled = 2,
}

/// <summary>The rule that gives a process its <see cref="FlowKind"/>, and the words for a kind.</summary>
public static class FlowKinds
{
    /// <summary>
    /// The kind of a process: <see cref="FlowKind.Scheduled"/> when it has a schedule trigger (whatever else it has);
    /// else <see cref="FlowKind.Main"/> when it has an event trigger or when no other process starts it; else
    /// <see cref="FlowKind.Subprocess"/> (another process starts it and it has no trigger). A process that only starts itself counts as
    /// started by nobody.
    /// </summary>
    /// <param name="outline">The outline of the process.</param>
    /// <param name="startedBy">The ids of the processes of the library that start it through a subprocess step; its own id is ignored.</param>
    public static FlowKind Of(ProcessOutline outline, IEnumerable<string> startedBy)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(startedBy);
        if (outline.Triggers.Any(trigger => trigger is ScheduleTriggerDeclaration))
        {
            return FlowKind.Scheduled;
        }

        var hasEvent = outline.Triggers.Any(trigger => trigger is EventTriggerDeclaration);
        var started = startedBy.Any(id => !string.Equals(id, outline.Id, StringComparison.Ordinal));
        return hasEvent || !started ? FlowKind.Main : FlowKind.Subprocess;
    }

    /// <summary>True for a process nothing starts and that has no trigger: a main process the document gives no way to begin.</summary>
    /// <param name="outline">The outline of the process.</param>
    /// <param name="startedBy">The ids of the processes of the library that start it; its own id is ignored.</param>
    public static bool HasNoStart(ProcessOutline outline, IEnumerable<string> startedBy)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(startedBy);
        return outline.Triggers.Count == 0 && !startedBy.Any(id => !string.Equals(id, outline.Id, StringComparison.Ordinal));
    }

    /// <summary>The kind in words: "Main process", "Subprocess" or "Scheduled task".</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="locale">The language.</param>
    public static string Word(FlowKind kind, WorkbenchLocale locale) => FlowTemplates.Text(locale, kind switch
    {
        FlowKind.Main => "process.kind.main",
        FlowKind.Subprocess => "process.kind.subprocess",
        FlowKind.Scheduled => "process.kind.scheduled",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind."),
    });

    /// <summary>The kind in words for several processes ("Main processes", "Subprocesses", "Scheduled tasks").</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="locale">The language.</param>
    public static string Plural(FlowKind kind, WorkbenchLocale locale) => FlowTemplates.Text(locale, kind switch
    {
        FlowKind.Main => "process.kinds.main",
        FlowKind.Subprocess => "process.kinds.subprocess",
        FlowKind.Scheduled => "process.kinds.scheduled",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind."),
    });
}

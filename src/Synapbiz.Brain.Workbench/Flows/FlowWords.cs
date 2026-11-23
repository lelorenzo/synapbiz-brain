using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>
/// The words of one flow in one language, for a screen that has to name something a document talks about: its title, stages, steps,
/// results, queues, outcomes, events and delivery windows. Every word is the label of <c>meta.display</c> in the asked language, else in
/// English, else a readable form of the id, exactly as <see cref="FlowDescriber"/> words the same things.
/// </summary>
public sealed class FlowWords
{
    private readonly FlowLabels labels;
    private readonly ProcessDisplay display;
    private readonly string language;

    private FlowWords(BrainProcess process, WorkbenchLocale locale, IEnumerable<BrainProcess>? knownProcesses)
    {
        labels = new FlowLabels(process, locale, knownProcesses);
        display = ProcessDisplay.From(process);
        language = locale == WorkbenchLocale.Es ? "es" : "en";
    }

    /// <summary>Reads the words of a document.</summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="locale">The language.</param>
    /// <param name="knownProcesses">Other documents the caller knows, to word the result of a child process by the child's own labels; null for none.</param>
    public static FlowWords For(BrainProcess process, WorkbenchLocale locale, IEnumerable<BrainProcess>? knownProcesses = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        return new FlowWords(process, locale, knownProcesses);
    }

    /// <summary>The title of the flow.</summary>
    public string Title() => labels.Title();

    /// <summary>The label of a stage.</summary>
    /// <param name="id">The stage id.</param>
    public string Stage(string id) => labels.Stage(Checked(id));

    /// <summary>The label of a step.</summary>
    /// <param name="id">The step id.</param>
    public string Step(string id) => labels.Step(Checked(id));

    /// <summary>The label of a result of an <c>end</c> step.</summary>
    /// <param name="id">The result.</param>
    public string Result(string id) => labels.Result(Checked(id));

    /// <summary>The label of a queue of a task for a person.</summary>
    /// <param name="id">The queue.</param>
    public string Queue(string id) => labels.Queue(Checked(id));

    /// <summary>The label of an outcome a person may choose.</summary>
    /// <param name="id">The outcome.</param>
    public string Outcome(string id) => labels.Outcome(Checked(id));

    /// <summary>The label of an event.</summary>
    /// <param name="id">The event name.</param>
    public string Event(string id) => display.Event(language, Checked(id)) ?? WorkbenchText.Humanize(id);

    /// <summary>The label of a delivery window.</summary>
    /// <param name="id">The window name.</param>
    public string Window(string id) => labels.Window(Checked(id));

    /// <summary>The label of a result of a child process, from the child's own labels; null when the child is not known or has no label for it.</summary>
    /// <param name="processId">The id of the child process.</param>
    /// <param name="result">The result.</param>
    public string? ChildResult(string? processId, string result) => labels.ChildResult(processId, Checked(result));

    private static string Checked(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id;
    }
}

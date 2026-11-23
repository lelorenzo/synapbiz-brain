namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>
/// The in-memory state of one reader's session of <see cref="FlowWorkbench"/>: the selected flow, tab and step, the reviewer's name
/// and, for each flow, the review marks and notes, the proposed scenarios, the try-out in progress and the scenario being replayed. The
/// host owns its lifetime (one per user session). It reads and writes nothing outside memory, so closing the application discards it
/// unless the host exports first.
/// </summary>
public sealed class FlowWorkbenchSession
{
    private readonly Dictionary<string, FlowWork> work = new(StringComparer.Ordinal);

    private string? selectedFlowKey;
    private FlowWorkbenchTab selectedTab;
    private string? selectedStepId;
    private string reviewer = string.Empty;

    /// <summary>Raised after any change of the session, whoever made it.</summary>
    public event EventHandler? Changed;

    /// <summary>The key of the selected flow; null selects the first flow of the list.</summary>
    public string? SelectedFlowKey
    {
        get => selectedFlowKey;
        set
        {
            if (!string.Equals(selectedFlowKey, value, StringComparison.Ordinal))
            {
                selectedFlowKey = value;
                selectedStepId = null;
                Touch();
            }
        }
    }

    /// <summary>The selected tab. It is kept when the flow changes.</summary>
    public FlowWorkbenchTab SelectedTab
    {
        get => selectedTab;
        set
        {
            if (selectedTab != value)
            {
                selectedTab = value;
                Touch();
            }
        }
    }

    /// <summary>The id of the step whose card is open in the diagram; null when none is. It is cleared when the flow changes.</summary>
    public string? SelectedStepId
    {
        get => selectedStepId;
        set
        {
            if (!string.Equals(selectedStepId, value, StringComparison.Ordinal))
            {
                selectedStepId = value;
                Touch();
            }
        }
    }

    /// <summary>The reviewer's name, as it is written in an exported review. Never null.</summary>
    public string Reviewer
    {
        get => reviewer;
        set
        {
            var text = value ?? string.Empty;
            if (!string.Equals(reviewer, text, StringComparison.Ordinal))
            {
                reviewer = text;
                Touch();
            }
        }
    }

    /// <summary>The number of flows whose review marks, notes or proposed scenarios changed since the last export.</summary>
    public int UnexportedFlowCount => work.Values.Count(entry => entry.HasUnexportedChanges);

    /// <summary>The keys of the flows whose work changed since the last export, whether or not the flow is still in the list.</summary>
    internal IReadOnlyList<string> UnexportedFlowKeys => [.. work.Where(pair => pair.Value.HasUnexportedChanges).Select(pair => pair.Key)];

    /// <summary>Forgets everything: the selection, the reviewer's name, the try-outs and the work on every flow.</summary>
    public void Clear()
    {
        work.Clear();
        selectedFlowKey = null;
        selectedStepId = null;
        selectedTab = FlowWorkbenchTab.Read;
        reviewer = string.Empty;
        Touch();
    }

    internal FlowWork Work(string key)
    {
        if (!work.TryGetValue(key, out var entry))
        {
            entry = new FlowWork();
            work[key] = entry;
        }

        return entry;
    }

    /// <summary>Records that the work on the flows with these keys has been exported; the work on any other flow stays unexported.</summary>
    /// <param name="keys">The keys of the flows whose files were written.</param>
    internal void MarkExported(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (work.TryGetValue(key, out var entry))
            {
                entry.ExportedSignature = entry.Signature();
            }
        }

        Touch();
    }

    internal void Touch() => Changed?.Invoke(this, EventArgs.Empty);
}

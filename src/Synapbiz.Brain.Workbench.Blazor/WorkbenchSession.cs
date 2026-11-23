namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>
/// The in-memory state of one reviewer's session: the selected rule and tab, the reviewer's name and, for each rule, the review
/// state and notes, the proposed cases, the simulator fields and the last result. The host owns its lifetime (one per user
/// session). It reads and writes nothing outside memory, so closing the application discards it unless the host exports first.
/// </summary>
public sealed class WorkbenchSession
{
    private readonly Dictionary<string, RuleWork> work = new(StringComparer.Ordinal);

    private string? selectedRuleKey;
    private WorkbenchTab selectedTab;
    private string reviewer = string.Empty;

    /// <summary>Raised after any change of the session, whoever made it.</summary>
    public event EventHandler? Changed;

    /// <summary>The key of the selected rule; null selects the first rule of the list.</summary>
    public string? SelectedRuleKey
    {
        get => selectedRuleKey;
        set
        {
            if (!string.Equals(selectedRuleKey, value, StringComparison.Ordinal))
            {
                selectedRuleKey = value;
                Touch();
            }
        }
    }

    /// <summary>The selected tab. It is kept when the rule changes.</summary>
    public WorkbenchTab SelectedTab
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

    /// <summary>The number of rules whose review items or proposed cases changed since the last export.</summary>
    public int UnexportedRuleCount => work.Values.Count(entry => entry.HasUnexportedChanges);

    /// <summary>Forgets everything: the selection, the reviewer's name and the work on every rule.</summary>
    public void Clear()
    {
        work.Clear();
        selectedRuleKey = null;
        selectedTab = WorkbenchTab.Read;
        reviewer = string.Empty;
        Touch();
    }

    internal RuleWork Work(string key)
    {
        if (!work.TryGetValue(key, out var entry))
        {
            entry = new RuleWork();
            work[key] = entry;
        }

        return entry;
    }

    /// <summary>Records that everything now in the session has been exported.</summary>
    internal void MarkExported()
    {
        foreach (var entry in work.Values)
        {
            entry.ExportedSignature = entry.Signature();
        }

        Touch();
    }

    internal void Touch() => Changed?.Invoke(this, EventArgs.Empty);
}

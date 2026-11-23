using Microsoft.AspNetCore.Components;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>Guided editing: what the draft changes, and the export of the proposal.</summary>
public sealed partial class RuleWorkbench
{
    private const string ReasonRef = "proposal:reason";
    private const string ProposalNameRef = "proposal:name";

    /// <summary>The warnings that remain true in the draft and were not true in the rule: rows that never decide and a gap in coverage.</summary>
    private List<string> DraftWarnings(Ctx c)
    {
        var draft = c.Draft!;
        var findings = draft.Findings!;
        var rows = findings.Unreachable
            .Where(key => !draft.BaseFindings.Unreachable.Contains(key))
            .Select(key => SimulationPresenter.RowByKey(c.View, key))
            .OfType<RowView>()
            .OrderBy(row => row.Index)
            .Select(row => Plain("edit.unreachable", ("n", SimulationPresenter.Number(row))))
            .ToList();
        if (findings.Incomplete && !draft.BaseFindings.Incomplete)
        {
            rows.Add(Plain("edit.incomplete"));
        }

        return rows;
    }

    /// <summary>The tabs the rule's panel shows: the fifth only while the rule has a draft.</summary>
    private int TabCount => SelectedRule is { CanDraft: true } rule && Session.Work(rule.Key).Draft is not null ? SelectorTabs.Length : SelectorTabs.Length - 1;

    private void UndoFromChanges(Ctx c, string key, Func<DraftStep, bool> remove)
    {
        UndoRow(c, key, remove);
        pendingFocus = TabRef((int)WorkbenchTab.Changes);
    }

    private static void SetReason(DraftState draft, ChangeEventArgs args)
    {
        draft.Reason = args.Value?.ToString() ?? string.Empty;
        draft.ReasonError = draft.ReasonError && draft.Reason.Trim().Length == 0;
    }

    private void SetProposalName(DraftState draft, ChangeEventArgs args)
    {
        Session.Reviewer = args.Value?.ToString() ?? string.Empty;
        draft.NameError = draft.NameError && Session.Reviewer.Trim().Length == 0;
    }

    /// <summary>Writes the proposal of the draft and gives it to the host; the draft stays, marked as exported.</summary>
    private async Task ExportProposalAsync(WorkbenchRule rule)
    {
        var draft = Session.Work(rule.Key).Draft!;
        var shown = Context(rule, forceDraft: true);
        if (OnExport is null || draft.Rule is null || !draft.HasChanges || (!IsStale(rule, draft) && ChangingCount(shown) > 0))
        {
            return;
        }

        draft.ReasonError = draft.Reason.Trim().Length == 0;
        draft.NameError = Session.Reviewer.Trim().Length == 0;
        if (draft.ReasonError || draft.NameError)
        {
            pendingFocus = draft.ReasonError ? ReasonRef : ProposalNameRef;
            Session.Touch();
            return;
        }

        var warnings = DraftWarnings(shown);
        var note = warnings.Count == 0 ? draft.Reason.Trim() : draft.Reason.Trim() + "\n\n" + string.Join("\n", warnings);
        var proposal = new RuleProposal(draft.BaseRule.Id, draft.BaseRule.Version, draft.BaseHash, Session.Reviewer.Trim(), Now(), note, draft.Text!);
        var name = "proposal-" + proposal.RuleId + "-v" + proposal.BaseVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json";
        var place = await OnExport(new WorkbenchExport([new WorkbenchExportFile(name, RuleProposalJson.Serialize(proposal), null, proposal)])).ConfigureAwait(true);
        draft.ExportedRevision = draft.Revision;
        draft.ExportPlace = string.IsNullOrWhiteSpace(place) ? null : place.Trim();
        Session.Touch();
    }
}

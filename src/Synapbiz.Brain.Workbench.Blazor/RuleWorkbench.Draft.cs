using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>Guided editing: the draft of a rule, its view switch, its start and its discard.</summary>
public sealed partial class RuleWorkbench
{
    private const string DraftToggleRef = "draft:toggle";
    private const string KeepRef = "dialog:keep";

    private bool discardOpen;

    /// <summary>What a tab draws: the rule as it is, or the draft, and the numbers that go with it.</summary>
    /// <param name="Rule">The host's rule.</param>
    /// <param name="Brain">The rule the tab shows: the draft's when the draft is shown.</param>
    /// <param name="View">The rule read as sentences.</param>
    /// <param name="Work">The reviewer's work on the rule.</param>
    /// <param name="Shown">The facts a person types.</param>
    /// <param name="Labels">The labels of the rule the tab shows.</param>
    /// <param name="Run">The test cases run on the rule the tab shows.</param>
    /// <param name="Draft">The reviewer's draft of the rule; null when there is none.</param>
    /// <param name="Editing">True when the draft is what the tab shows.</param>
    /// <param name="Broken">True when the draft is what the tab shows and the loader cannot read it.</param>
    private sealed record Ctx(
        WorkbenchRule Rule, BrainRule Brain, RuleView View, RuleWork Work, List<ShownFact> Shown, RuleLabels Labels, TestRunView Run, DraftState? Draft, bool Editing, bool Broken);

    private static readonly Dictionary<string, string> EmptyWords = [];

    private bool DialogOpen => discardOpen;

    /// <summary>The context of a rule's panels. <paramref name="baseOnly"/> asks for the rule as it is even when the draft is shown (the review is of the rule), and <paramref name="forceDraft"/> for the draft even when the rule is shown (the changes are of the draft).</summary>
    private Ctx Context(WorkbenchRule rule, bool baseOnly = false, bool forceDraft = false)
    {
        var work = Session.Work(rule.Key);
        var draft = rule.CanDraft ? work.Draft : null;
        if (!baseOnly && draft is { Rule: { } drawn } && (forceDraft || draft.ViewDraft))
        {
            var labels = draft.Labels(Locale, rule.Labels);
            var view = draft.View(Locale, labels);
            return new Ctx(rule, drawn, view, work, FactForm.Shown(drawn, view), labels, draft.Run!, draft, true, false);
        }

        var brain = rule.Rule!;
        var baseView = work.View(rule, Locale);
        return new Ctx(rule, brain, baseView, work, FactForm.Shown(brain, baseView), rule.Labels, work.Tests(brain), draft, false, !baseOnly && draft is not null && (forceDraft || draft.ViewDraft));
    }

    /// <summary>True when the text of the rule is no longer the text the draft was started from.</summary>
    private static bool IsStale(WorkbenchRule rule, DraftState draft) =>
        !string.Equals(draft.BaseHash, RuleReviewJson.ContentHash(rule.RuleText!), StringComparison.Ordinal);

    /// <summary>What the draft changes in its base, from the headless library.</summary>
    private RuleChangeView ChangesOf(WorkbenchRule rule, DraftState draft) =>
        draft.Change(Locale, rule.Labels, draft.Labels(Locale, rule.Labels));

    /// <summary>
    /// The number of rows the draft changes, adds, removes, moves or only names, the default row counted. It is the length of the
    /// library's rows, so the summary sentence, the Changes tab, the draft bar and the discard dialog agree.
    /// </summary>
    private int ChangeCount(WorkbenchRule rule, DraftState draft)
    {
        if (draft.Rule is null)
        {
            return 0;
        }

        var change = ChangesOf(rule, draft);
        return change.Rows.Count + (change.DefaultRowChanged ? 1 : 0);
    }

    /// <summary>True when the row is on both sides and its name differs: set, changed or cleared.</summary>
    private static bool IsRenamed(RowChangeView row) =>
        row.Tag is RowChangeTag.Changed or RowChangeTag.Moved && !string.Equals(row.NameBefore, row.NameAfter, StringComparison.Ordinal);

    /// <summary>True when the row's only change is its name: its two sentences are the same.</summary>
    private static bool IsNameOnly(RowChangeView row) =>
        row.Tag == RowChangeTag.Changed && string.Equals(row.SentenceBefore, row.SentenceAfter, StringComparison.Ordinal);

    /// <summary>"Fila 3 · Cobros" for a row with a name on the side it is numbered by (the draft's, the base's for a removed row), else "Fila 3".</summary>
    private string ChangeTitle(RowChangeView row, string number)
    {
        var title = Plain("row.label", ("n", number));
        var name = row.Tag == RowChangeTag.Removed ? row.NameBefore : row.NameAfter;
        return name is null ? title : title + Separator + name;
    }

    /// <summary>"Antes se llamaba «a». Ahora se llama «b».", with the words for a side that has no name.</summary>
    private string RenameText(RowChangeView row)
    {
        if (row.NameBefore is null)
        {
            return Plain("changes.named", ("b", row.NameAfter!));
        }

        return row.NameAfter is null
            ? Plain("changes.unnamed", ("a", row.NameBefore))
            : Plain("changes.renamed", ("a", row.NameBefore), ("b", row.NameAfter));
    }

    // ----- start, switch and discard ----------------------------------------------------------------------------------

    /// <summary>Starts a draft when there is none; discards the draft, after asking when it has changes, when there is one.</summary>
    private void ToggleDraft(WorkbenchRule rule)
    {
        var work = Session.Work(rule.Key);
        if (work.Draft is null)
        {
            work.Draft = DraftEngine.Start(rule);
            Session.Touch();
        }
        else if (work.Draft.HasChanges)
        {
            discardOpen = true;
            pendingFocus = KeepRef;
            Session.Touch();
        }
        else
        {
            Discard(rule);
        }
    }

    private void Discard(WorkbenchRule rule)
    {
        var work = Session.Work(rule.Key);
        work.Draft = null;
        work.Result = null;
        work.Stale = false;
        caseForm = null;
        discardOpen = false;
        pendingFocus = DraftToggleRef;
        if (Session.SelectedTab == WorkbenchTab.Changes)
        {
            Session.SelectedTab = WorkbenchTab.Read;
        }

        Session.Touch();
    }

    private void CloseDialog()
    {
        discardOpen = false;
        pendingFocus = DraftToggleRef;
        Session.Touch();
    }

    private void OnDialogKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseDialog();
        }
    }

    /// <summary>Shows the draft or the rule as it is. What a simulation or a form held for the other view is dropped.</summary>
    private void SetView(WorkbenchRule rule, bool showDraft)
    {
        var work = Session.Work(rule.Key);
        if (work.Draft is { } draft && draft.ViewDraft != showDraft)
        {
            draft.ViewDraft = showDraft;
            work.Result = null;
            work.Stale = false;
            work.Simulator.Errors.Clear();
            caseForm = null;
            Session.Touch();
        }
    }

    /// <summary>Takes the last step off the draft; the draft stays as it was when the loader refuses what is left.</summary>
    private void UndoLast(WorkbenchRule rule, DraftState draft) => Undo(rule, draft, null, step => ReferenceEquals(step, draft.Steps[^1]));

    /// <summary>Removes the steps the predicate names and reads the draft again; a refusal is shown under the row, or at the top.</summary>
    private void Undo(WorkbenchRule rule, DraftState draft, string? rowKey, Func<DraftStep, bool> remove)
    {
        var result = DraftEngine.Apply(rule, draft, DraftEngine.Without(draft, remove));
        draft.Refusal = result.IsOk ? null : new DraftRefusal(rowKey, DraftWording.Lines(text, result.Problems, Context(rule).View, result.Rule, EmptyWords));
        Session.Touch();
    }

    // ----- pieces ------------------------------------------------------------------------------------------------------

    /// <summary>The sentence of the draft bar: the draft view, the rule view with the number of changes, or the rule that changed underneath.</summary>
    private RenderFragment DraftSentence(WorkbenchRule rule, DraftState draft) => builder =>
    {
        var count = ChangeCount(rule, draft);
        var shown = draft.ViewDraft ? "draft.bar" : UiText.Plural("draft.barRule", count);
        var key = IsStale(rule, draft) ? "draft.stale" : shown;
        builder.AddContent(0, Rich(key, ("n", OutcomeFormat.Number(count))));
        if (draft.ChangedAfterExport)
        {
            builder.AddContent(1, " " + Plain("draft.changedAfterExport"));
        }
    };
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>Guided editing: the test cases in the draft view, and what the reviewer decides about the ones whose result changed.</summary>
public sealed partial class RuleWorkbench
{
    private const string CaseAddRef = "case:add";
    private const string CaseExpectedRef = "case:expected";

    /// <summary>One line of the test case table in the draft view.</summary>
    /// <param name="Name">The case name.</param>
    /// <param name="Case">The case as the draft runs it; for a removed case, as the rule runs it.</param>
    /// <param name="Change">What the library says differs between the rule and the draft for this case; null when nothing does.</param>
    /// <param name="Removed">True for a case the draft removed.</param>
    /// <param name="IsNew">True for a case the draft added.</param>
    /// <param name="Decided">True when the reviewer accepted or changed what the case expects.</param>
    /// <param name="Changes">True when the case passes on the rule, gives another result on the draft and nobody decided on it yet.</param>
    private sealed record CaseLine(string Name, TestCaseView Case, CaseOutcomeChange? Change, bool Removed, bool IsNew, bool Decided, bool Changes);

    private static string? CaseRowClass(CaseLine line)
    {
        if (line.Removed)
        {
            return "wb-row-gone";
        }

        return line.Case.Passed ? null : "wb-fail-row";
    }

    private static string CasePillKey(CaseLine line)
    {
        if (line.Changes)
        {
            return "case.changes";
        }

        return line.Case.Passed ? "tests.pass" : "tests.fail";
    }

    private static string AcceptRef(string name) => "case:accept:" + name;

    private static string CaseChangeRef(string name) => "case:change:" + name;

    private static string CaseUndoRef(string name) => "case:undo:" + name;

    private static string CaseRestoreRef(string name) => "case:restore:" + name;

    // ----- what the draft shows ---------------------------------------------------------------------------------------

    /// <summary>The cases of the draft in order, each removed case where it was, and what the draft does to each.</summary>
    private List<CaseLine> CaseLines(Ctx c)
    {
        var draft = c.Draft!;
        var before = c.Work.Tests(c.Rule.Rule!).Cases;
        var earlier = new Dictionary<string, TestCaseView>(StringComparer.Ordinal);
        foreach (var item in before)
        {
            earlier.TryAdd(item.Name, item);
        }

        var changes = new Dictionary<string, CaseOutcomeChange>(StringComparer.Ordinal);
        foreach (var item in ChangesOf(c.Rule, draft).CasesThatChange)
        {
            changes.TryAdd(item.Name, item);
        }

        var decided = draft.DecidedCases;
        var added = draft.Steps.Where(step => step.Kind == StepKind.CaseAdd).Select(step => step.Key).ToHashSet(StringComparer.Ordinal);
        var lines = new List<CaseLine>();
        foreach (var item in c.Run.Cases)
        {
            var change = changes.GetValueOrDefault(item.Name);
            var isNew = added.Contains(item.Name);
            var isDecided = decided.Contains(item.Name);
            var willChange = !isNew && !isDecided && change is not null && earlier.TryGetValue(item.Name, out var was) && was.Passed && !item.Passed && item.Failure is null;
            lines.Add(new CaseLine(item.Name, item, change, false, isNew, isDecided, willChange));
        }

        var removed = draft.Steps.Where(step => step.Kind == StepKind.CaseRemove).Select(step => step.Key).ToHashSet(StringComparer.Ordinal);
        var names = before.Select(item => item.Name).ToList();
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            if (!removed.Contains(name) || lines.Exists(line => line.Name == name))
            {
                continue;
            }

            var previous = names.Take(index).Reverse().Select(other => lines.FindIndex(line => line.Name == other)).FirstOrDefault(found => found >= 0, -1);
            lines.Insert(previous + 1, new CaseLine(name, earlier[name], null, true, false, false, false));
        }

        return lines;
    }

    /// <summary>How many cases of the draft passed on the rule and now give another result that nobody has decided on.</summary>
    private int ChangingCount(Ctx c) => c.Editing ? CaseLines(c).Count(line => line.Changes) : 0;

    /// <summary>"Ver el caso": goes to the tests with focus on the first case that changed.</summary>
    private void SeeChangingCases(Ctx c)
    {
        Session.SelectedTab = WorkbenchTab.Tests;
        pendingFocus = AcceptRef(CaseLines(c).First(line => line.Changes).Name);
        Session.Touch();
    }

    /// <summary>The line under a changed case that says what decides now.</summary>
    private string NowDecidedBy(Ctx c, CaseLine line, HashSet<string> changedRows)
    {
        var keys = line.Case.MatchedRowKeys;
        if (keys.Count == 0)
        {
            return Plain("case.noRowDecides");
        }

        return keys.Any(changedRows.Contains)
            ? Plain("case.decidesChanged", ("n", SimulationPresenter.RowNumbers(text, c.View, keys)))
            : DecidedByText(c.View, line.Case);
    }

    /// <summary>The keys of the rows the draft added or changed, the ones "que usted cambió" can be said of.</summary>
    private HashSet<string> ChangedRowKeys(Ctx c) =>
        ChangesOf(c.Rule, c.Draft!).Rows.Where(row => row.Tag is RowChangeTag.Added or RowChangeTag.Changed).Select(row => row.Key).ToHashSet(StringComparer.Ordinal);

    // ----- steps --------------------------------------------------------------------------------------------------------

    private static ProposedCase ProposedOf(BrainRule brain, string name, IReadOnlyDictionary<string, DecisionValue> facts, GoldenExpectation expectation) =>
        new(
            name,
            facts,
            expectation is ExpectedNoDecision,
            (expectation as ExpectedDecided)?.OutcomeJson,
            expectation is ExpectedDecided { RowKey: { } key } && brain.RowIdsByKey.TryGetValue(key, out var id) ? id : null);

    /// <summary>Adds a step for a case and reads the draft again; a refusal is returned and the draft stays as it was.</summary>
    private static RebuildResult ApplyCaseStep(Ctx c, StepKind kind, string name, IReadOnlyList<RuleEdit> edits)
    {
        var draft = c.Draft!;
        var result = DraftEngine.Apply(c.Rule, draft, [.. draft.Steps, new DraftStep(kind, name, edits)]);
        if (result.IsOk)
        {
            draft.Refusal = null;
            c.Work.Stale = c.Work.Result is not null;
        }

        return result;
    }

    /// <summary>
    /// The edits that make a case expect something else. A decision is a <see cref="SetCaseExpectation"/>, which keeps the case
    /// where it is. Expecting no decision, or a list of several results, is something that edit cannot say: the case is then
    /// removed and added again, which writes it at the end of the cases.
    /// </summary>
    private static IReadOnlyList<RuleEdit> Replace(Ctx c, TestCaseView testCase, GoldenExpectation expectation)
    {
        if (expectation is ExpectedDecided decided && OutcomeFields(decided.OutcomeJson, c.View.HitPolicy == HitPolicy.Collect) is { } fields)
        {
            return [new SetCaseExpectation(testCase.Name, decided.RowKey, fields)];
        }

        return [new RemoveCase(testCase.Name), new AddCase(ProposedOf(c.Brain, testCase.Name, testCase.Facts, expectation))];
    }

    /// <summary>The fields of an expected outcome as values, or null when the text is not a single flat outcome.</summary>
    internal static Dictionary<string, DecisionValue>? OutcomeFields(string outcomeJson, bool collect)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(outcomeJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (collect)
        {
            node = node is JsonArray { Count: 1 } list ? list[0] : null;
        }

        if (node is not JsonObject outcome)
        {
            return null;
        }

        var fields = new Dictionary<string, DecisionValue>(StringComparer.Ordinal);
        foreach (var (name, value) in outcome)
        {
            if (FieldValue(value) is not { } read)
            {
                return null;
            }

            fields[name] = read;
        }

        return fields;
    }

    private static DecisionValue? FieldValue(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return node is JsonObject money
                && money["currency"] is JsonValue { } currency && currency.TryGetValue<string>(out var code)
                && money["amount"] is JsonValue { } amount && amount.TryGetValue<decimal>(out var sum)
                    ? new MoneyValue(sum, code)
                    : null;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return new CodeValue(text);
        }

        if (value.TryGetValue<bool>(out var flag))
        {
            return new BooleanValue(flag);
        }

        if (value.TryGetValue<long>(out var whole))
        {
            return new IntegerValue(whole);
        }

        return value.TryGetValue<decimal>(out var number) ? new DecimalValue(number) : null;
    }

    /// <summary>Shows the refusal of a case step at the top of the tests.</summary>
    private void RefuseCase(Ctx c, RebuildResult result, string focus)
    {
        c.Draft!.Refusal = new DraftRefusal(null, DraftWording.Lines(text, result.Problems, c.View, result.Rule, EmptyWords));
        pendingFocus = focus;
    }

    /// <summary>"Aceptar el nuevo resultado": the case expects what the draft gives, and keeps the row it pinned when it pinned one.</summary>
    private void AcceptCase(Ctx c, CaseLine line)
    {
        var testCase = line.Case;
        GoldenExpectation expectation = new ExpectedNoDecision();
        if (testCase.Actual is Decided decided)
        {
            var pinned = testCase.Expected is ExpectedDecided { RowKey: not null };
            expectation = new ExpectedDecided(decided.OutcomeJson, pinned ? decided.RowKey : null);
        }

        var result = ApplyCaseStep(c, StepKind.CaseAccept, line.Name, Replace(c, testCase, expectation));
        if (result.IsOk)
        {
            pendingFocus = CaseUndoRef(line.Name);
        }
        else
        {
            RefuseCase(c, result, AcceptRef(line.Name));
        }

        Session.Touch();
    }

    /// <summary>"Revisar mi cambio": goes to the row that explains the difference, or to the changes when no row does.</summary>
    private void ReviewMyChange(Ctx c, CaseLine line)
    {
        var change = ChangesOf(c.Rule, c.Draft!);
        var defaultKey = c.Brain.Table.DefaultRow?.Key;

        string? Tagged(string? key)
        {
            if (key is null)
            {
                return null;
            }

            var row = change.Rows.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
            if (row is { Tag: RowChangeTag.Removed })
            {
                return RestoreRef(key);
            }

            var isDefault = change.DefaultRowChanged && string.Equals(key, defaultKey, StringComparison.Ordinal);
            if (row is { Tag: not RowChangeTag.Moved } || isDefault)
            {
                return EditRef(key);
            }

            return null;
        }

        var target = Tagged(line.Change?.RowKeyAfter) ?? Tagged(line.Change?.RowKeyBefore);
        Session.SelectedTab = target is null ? WorkbenchTab.Changes : WorkbenchTab.Read;
        pendingFocus = target ?? TabRef((int)WorkbenchTab.Changes);
        Session.Touch();
    }

    /// <summary>Takes the decisions about a case away.</summary>
    private void UndoCaseDecision(Ctx c, string name)
    {
        var draft = c.Draft!;
        Undo(c.Rule, draft, null, step => step.IsCaseDecision && step.Key == name);
        c.Work.Stale = c.Work.Result is not null;
        pendingFocus = CaseChangeRef(name);
    }

    /// <summary>Removes a case: a case the draft added goes away with its steps, any other is removed by a step the reviewer can restore.</summary>
    private void RemoveCaseAction(Ctx c, CaseLine line)
    {
        var draft = c.Draft!;
        if (line.IsNew)
        {
            Undo(c.Rule, draft, null, step => step.IsCaseStep && step.Key == line.Name);
            pendingFocus = CaseAddRef;
            return;
        }

        var result = ApplyCaseStep(c, StepKind.CaseRemove, line.Name, [new RemoveCase(line.Name)]);
        if (result.IsOk)
        {
            pendingFocus = CaseRestoreRef(line.Name);
        }
        else
        {
            RefuseCase(c, result, CaseChangeRef(line.Name));
        }

        Session.Touch();
    }

    private void RestoreCase(Ctx c, string name)
    {
        Undo(c.Rule, c.Draft!, null, step => step.Kind == StepKind.CaseRemove && step.Key == name);
        pendingFocus = CaseChangeRef(name);
    }

    // ----- the form -----------------------------------------------------------------------------------------------------

    /// <summary>Opens the form that changes what a case expects, with what it expects now chosen.</summary>
    private void OpenCaseEdit(Ctx c, CaseLine line)
    {
        var options = CaseForm.Options(c.Brain, c.View, c.Labels, Locale);
        var form = new CaseForm(c.Rule.Key) { EditName = line.Name };
        switch (line.Case.Expected)
        {
            case ExpectedNoDecision:
                form.ExpectNone = true;
                break;
            case ExpectedDecided decided when options.FirstOrDefault(option => string.Equals(option.OutcomeJson, decided.OutcomeJson, StringComparison.Ordinal)) is { } found:
                form.Chosen.Add(found.Index);
                form.PinnedRowKey = c.View.HitPolicy == HitPolicy.Collect ? null : decided.RowKey;
                break;
        }

        caseForm = form;
        pendingFocus = CaseExpectedRef;
        Session.Touch();
    }

    private void SaveCaseEdit(Ctx c, CaseForm form, CaseLine line)
    {
        var options = CaseForm.Options(c.Brain, c.View, c.Labels, Locale);
        if (form.Expectation(options, c.View.HitPolicy) is not { } expectation)
        {
            form.ExpectedError = "case.expectedRequired";
            pendingFocus = CaseExpectedRef;
            Session.Touch();
            return;
        }

        var result = ApplyCaseStep(c, StepKind.CaseEdit, line.Name, Replace(c, line.Case, expectation));
        if (result.IsOk)
        {
            caseForm = null;
            pendingFocus = CaseChangeRef(line.Name);
        }
        else
        {
            form.Refusal = DraftWording.Lines(text, result.Problems, c.View, result.Rule, EmptyWords);
            pendingFocus = BoxRef;
        }

        Session.Touch();
    }

    /// <summary>Saves the form of the draft view: a new case becomes a step of the draft.</summary>
    private void SaveDraftCase(Ctx c)
    {
        var form = caseForm!;
        if (form.EditName is { } name)
        {
            SaveCaseEdit(c, form, CaseLines(c).First(line => line.Name == name));
            return;
        }

        var brain = c.Brain;
        form.NameError = null;
        form.ExpectedError = null;
        form.Refusal = null;
        form.Facts.Errors.Clear();

        var title = form.Name.Trim();
        if (title.Length == 0)
        {
            form.NameError = "case.nameRequired";
        }
        else if (brain.Goldens.Any(golden => string.Equals(golden.Name, title, StringComparison.Ordinal)))
        {
            form.NameError = "case.nameTaken";
        }

        var parsed = form.Facts.Parse(brain, c.Shown);
        foreach (var (fact, code) in parsed.Errors)
        {
            form.Facts.Errors[fact] = code;
        }

        var expectation = form.Expectation(CaseForm.Options(brain, c.View, c.Labels, Locale), c.View.HitPolicy);
        if (expectation is null)
        {
            form.ExpectedError = "case.expectedRequired";
        }

        if (form.NameError is not null || form.Facts.Errors.Count > 0 || expectation is null)
        {
            pendingFocus = form.NameError is not null ? CaseNameRef : null;
            Session.Touch();
            return;
        }

        var result = ApplyCaseStep(c, StepKind.CaseAdd, title, [new AddCase(ProposedOf(brain, title, parsed.Facts, expectation))]);
        if (result.IsOk)
        {
            caseForm = null;
            pendingFocus = CaseChangeRef(title);
        }
        else
        {
            form.Refusal = DraftWording.Lines(text, result.Problems, c.View, result.Rule, EmptyWords);
            pendingFocus = BoxRef;
        }

        Session.Touch();
    }

    private void SaveAnyCase(Ctx c)
    {
        if (c.Editing)
        {
            SaveDraftCase(c);
        }
        else
        {
            SaveCase(c.Rule, c.Work, c.Shown, c.View, c.Labels);
        }
    }

    /// <summary>Runs the draft's test cases again.</summary>
    private async Task RunDraftAgainAsync(DraftState draft)
    {
        testsRunning = true;
        StateHasChanged();
        await Task.Delay(1).ConfigureAwait(true);
        draft.RunAgain();
        testsRunning = false;
        Session.Touch();
    }
}

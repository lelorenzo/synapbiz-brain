using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>Guided editing: the rows of the draft, their menu and their form.</summary>
public sealed partial class RuleWorkbench
{
    private const string BoxRef = "form:box";

    private string? menuFor;
    private string liveMessage = string.Empty;

    /// <summary>One line of the draft's Leer: a row of the draft, the default row or a row the draft removed.</summary>
    /// <param name="Row">The row as a sentence; null for a removed row.</param>
    /// <param name="Change">What the draft changes in the row; null when nothing.</param>
    /// <param name="Key">The key of the row.</param>
    private sealed record DraftItem(RowView? Row, RowChangeView? Change, string Key);

    private static string EditRef(string key) => "row:edit:" + key;

    private static string MoreRef(string key) => "row:more:" + key;

    private static string RestoreRef(string key) => "row:restore:" + key;

    private static string MenuItemRef(string key, int index) => "row:item:" + key + ":" + index.ToString(CultureInfo.InvariantCulture);

    private static string FactCtlRef(string fact) => "form:fact:" + fact;

    private static string OutCtlRef(string field) => "form:out:" + field;

    private const string AddEndRef = "row:addend";

    // ----- what the draft shows ---------------------------------------------------------------------------------------

    /// <summary>The rows of the draft in order, each removed row where it was, and what the draft changes in each.</summary>
    private List<DraftItem> DraftItems(Ctx c)
    {
        var draft = c.Draft!;
        var changes = ChangesOf(c.Rule, draft).Rows.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var present = c.View.Rows.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        var baseKeys = draft.BaseRule.Table.Rows.Select(row => row.Key).ToList();
        var removed = changes.Values.Where(item => item.Tag == RowChangeTag.Removed).OrderBy(item => item.NumberBefore).ToList();
        var after = removed.ToLookup(item => baseKeys.Take((item.NumberBefore ?? 1) - 1).LastOrDefault(present.Contains) ?? string.Empty, StringComparer.Ordinal);
        var items = new List<DraftItem>();
        items.AddRange(after[string.Empty].Select(item => new DraftItem(null, item, item.Key)));
        foreach (var row in c.View.Rows)
        {
            items.Add(new DraftItem(row, changes.GetValueOrDefault(row.Key), row.Key));
            items.AddRange(after[row.Key].Select(item => new DraftItem(null, item, item.Key)));
        }

        return items;
    }

    private static bool IsMoved(DraftState draft, DraftItem item) =>
        item.Change is { Tag: RowChangeTag.Moved or RowChangeTag.Changed, NumberBefore: { } before, NumberAfter: { } now }
        && before != now && draft.Steps.Any(step => step.Kind == StepKind.Move && step.Key == item.Key);

    private static string TagKey(RowChangeTag tag) => tag switch
    {
        RowChangeTag.Added => "tag.new",
        RowChangeTag.Removed => "tag.removed",
        RowChangeTag.Changed => "tag.changed",
        _ => "tag.moved",
    };

    private static string TagTone(RowChangeTag tag) => tag switch
    {
        RowChangeTag.Added => "ok",
        RowChangeTag.Changed => "warn",
        _ => "neutral",
    };

    /// <summary>True when the draft names or clears the row's name and the row changes in nothing else, so the library's comparison of rows does not show it.</summary>
    private static bool IsRenamed(DraftState draft, string key) =>
        draft.Steps.Any(step => step.Key == key && step.Kind == StepKind.Row && step.Edits.OfType<SetRowLabel>().Any());

    /// <summary>True when the draft has a step for the row that "Deshacer" would take away.</summary>
    private static bool HasRowSteps(DraftState draft, string key) =>
        draft.Steps.Any(step => step.Key == key && step.Kind is StepKind.Row or StepKind.Move);

    // ----- opening and closing the form -------------------------------------------------------------------------------

    private static string NewRowKey(DraftState draft)
    {
        var used = draft.Steps.Select(step => step.Key).Concat(draft.BaseRule.Table.Rows.Select(row => row.Key)).ToHashSet(StringComparer.Ordinal);
        while (true)
        {
            var seed = System.Text.Encoding.UTF8.GetBytes(draft.BaseHash + ":" + draft.NextKeyNumber().ToString(CultureInfo.InvariantCulture));
            var key = "row-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(seed))[..6];
            if (used.Add(key))
            {
                return key;
            }
        }
    }

    /// <summary>The text a typed value has in a form: a money amount without its currency.</summary>
    private static string FormText(DecisionValue value) => value is MoneyValue money ? money.Amount.ToString(CultureInfo.InvariantCulture) : FactForm.Text(value);

    private void OpenForm(Ctx c, RowForm form)
    {
        var draft = c.Draft!;
        if (draft.Form is { Touched: true } open)
        {
            open.Blocked = BlockedText(open);
            Session.Touch();
            return;
        }

        draft.Form = form;
        draft.Refusal = null;
        menuFor = null;
        Preview(c, form);
        pendingFocus = FirstControl(c, form);
        Session.Touch();
    }

    private string BlockedText(RowForm open)
    {
        if (open.IsNew)
        {
            return Plain("form.otherNew");
        }

        return open.IsDefault ? Plain("form.otherDefault") : Plain("form.other", ("n", open.Number?.ToString(CultureInfo.InvariantCulture) ?? "–"));
    }

    private string TitleOf(RowForm form)
    {
        if (form.IsNew)
        {
            return Plain("form.titleNew");
        }

        return form.IsDefault ? Plain("form.titleDefault") : Plain("form.titleEdit", ("n", form.Number?.ToString(CultureInfo.InvariantCulture) ?? "–"));
    }

    /// <summary>The ref key of the control of a field key (<c>fact:name</c> or <c>out:name</c>).</summary>
    private static string ControlRef(string fieldKey) =>
        fieldKey.StartsWith("fact:", StringComparison.Ordinal) ? FactCtlRef(fieldKey[5..]) : OutCtlRef(fieldKey[4..]);

    private static string FirstControl(Ctx c, RowForm form)
    {
        if (!form.IsDefault)
        {
            var first = c.Shown.FirstOrDefault(item => FactVisible(form, item.Fact.Name));
            if (first is not null)
            {
                return FactCtlRef(first.Fact.Name);
            }
        }

        return OutCtlRef(c.Brain.Kind.OutcomeFields[0].Name);
    }

    private static bool FactVisible(RowForm form, string fact) =>
        form.ShowUnused || form.IsNew || form.Used.Contains(fact) || (form.Lines.TryGetValue(fact, out var line) && line.Wording != RowForm.AnyWording);

    private void OpenEdit(Ctx c, string key, bool isDefault)
    {
        var brain = c.Brain;
        var row = isDefault ? brain.Table.DefaultRow! : brain.Table.Rows.First(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        var view = isDefault ? c.View.DefaultRow! : c.View.Rows.First(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        var form = new RowForm(isDefault ? null : key, isDefault, NewRowKey(c.Draft!), null, EditRef(key)) { Number = isDefault ? null : view.Index + 1 };
        foreach (var condition in row.Conditions)
        {
            form.Lines[condition.FactName] = RowForm.LineOf(condition);
            form.Used.Add(condition.FactName);
        }

        var typed = OutcomeValues.Typed(brain.Kind, row.Outcome);
        foreach (var (field, value) in typed)
        {
            form.Outcome[field] = FormText(value);
            if (value is MoneyValue money)
            {
                form.OutcomeCurrency[field] = money.Currency;
            }
        }

        form.Name = view.Title ?? string.Empty;
        form.InitialName = form.Name;
        SeedDefaults(c, form);
        OpenForm(c, form);
    }

    private void OpenNew(Ctx c, string? afterKey, string trigger)
    {
        var form = new RowForm(null, false, NewRowKey(c.Draft!), afterKey, trigger);
        SeedDefaults(c, form);
        OpenForm(c, form);
    }

    /// <summary>Gives a select that has no "no value" choice its first choice, so the form never shows a value that is not set.</summary>
    private void SeedDefaults(Ctx c, RowForm form)
    {
        foreach (var field in c.Brain.Kind.OutcomeFields.Where(field => !field.Nullable && !form.Outcome.ContainsKey(field.Name)))
        {
            if (OutcomeChoicesOf(c, field) is { Count: > 0 } choices)
            {
                form.Outcome[field.Name] = choices[0].Value;
            }
        }

        foreach (var fact in c.Shown.Select(item => item.Fact))
        {
            var line = form.LineFor(fact.Name);
            if (fact.Type == FactType.Money && line.Currency.Length == 0)
            {
                line.Currency = FactForm.DefaultCurrency;
            }
        }

        foreach (var field in c.Brain.Kind.OutcomeFields.Where(field => field.Type == FactType.Money && !form.OutcomeCurrency.ContainsKey(field.Name)))
        {
            form.OutcomeCurrency[field.Name] = FactForm.DefaultCurrency;
        }
    }

    private void CancelForm(Ctx c)
    {
        var draft = c.Draft!;
        pendingFocus = draft.Form?.Trigger;
        draft.Form = null;
        Session.Touch();
    }

    // ----- what a change of the form does -----------------------------------------------------------------------------

    /// <summary>Called after the person changed a field: the problem of the field is dropped and the sentence is read again.</summary>
    private void Changed(Ctx c, RowForm form, string fieldKey)
    {
        form.Touched = true;
        form.Problems.Remove(fieldKey);
        form.Marked.Remove(fieldKey);
        form.Refusal = null;
        form.Blocked = string.Empty;
        Preview(c, form);
    }

    private static DraftStep StepOf(Ctx c, RowForm form, FormCompiled compiled) =>
        new(form.IsNew ? StepKind.Add : StepKind.Row, form.IsDefault ? c.Brain.Table.DefaultRow!.Key : form.StepKey, compiled.Edits);

    private bool HasLabel(Ctx c, RowForm form) =>
        form.RowKey is { } key && RuleLabels.FromMeta(c.Brain.Meta, Locale).Rows.ContainsKey(c.Brain.RowIdsByKey[key]);

    private FormCompiled Compile(Ctx c, RowForm form) => form.Compile(c.Brain, c.Shown, Locale, HasLabel(c, form));

    /// <summary>Reads the form as a candidate: the sentence of the row and the warnings. A form with a problem, or a candidate the loader refuses, keeps the last sentence.</summary>
    private void Preview(Ctx c, RowForm form)
    {
        var draft = c.Draft!;
        var compiled = Compile(c, form);
        if (compiled.Problems.Count > 0)
        {
            return;
        }

        var step = StepOf(c, form, compiled);
        var key = step.Key;
        var result = DraftEngine.Rebuild(c.Rule, draft, [.. draft.Steps, step]);
        if (result.Rule is not { } candidate)
        {
            return;
        }

        var labels = DraftState.LabelsOf(draft.BaseRule, candidate, Locale, c.Rule.Labels);
        var view = RuleDescriber.Describe(candidate, Locale, labels);
        var shown = form.IsDefault ? view.DefaultRow : view.Rows.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.Ordinal));
        form.Preview = shown?.Sentence ?? form.Preview;
        form.Warnings.Clear();
        WarningsOf(form, view, shown, result.Findings!, draft);
    }

    private void WarningsOf(RowForm form, RuleView view, RowView? row, DraftFindings findings, DraftState draft)
    {
        if (row is null)
        {
            return;
        }

        var conditions = row.Conditions.Count(condition => condition.Operator != Operator.Any);
        if (!form.IsDefault && conditions == 0 && view.HitPolicy == HitPolicy.First)
        {
            form.Warnings.Add(Plain("warn.always"));
        }

        if (!form.IsDefault && conditions > 0)
        {
            var signature = Signature(row);
            var same = view.Rows.FirstOrDefault(other => !string.Equals(other.Key, row.Key, StringComparison.Ordinal) && Signature(other) == signature);
            if (same is not null)
            {
                form.Warnings.Add(Plain("warn.same", ("n", SimulationPresenter.Number(same))));
            }
        }

        foreach (var key in findings.Unreachable.Where(item => !draft.BaseFindings.Unreachable.Contains(item)).Order(StringComparer.Ordinal))
        {
            if (view.Rows.FirstOrDefault(other => string.Equals(other.Key, key, StringComparison.Ordinal)) is { } unreachable)
            {
                form.Warnings.Add(Plain("edit.unreachable", ("n", SimulationPresenter.Number(unreachable))));
            }
        }

        if (findings.Incomplete && !draft.BaseFindings.Incomplete)
        {
            form.Warnings.Add(Plain("edit.incomplete"));
        }
    }

    private static string Signature(RowView row) =>
        string.Join(";", row.Conditions.Where(condition => condition.Operator != Operator.Any).Select(condition => condition.FactName + "|" + condition.Text).Order(StringComparer.Ordinal));

    // ----- apply ------------------------------------------------------------------------------------------------------

    private string ProblemText(Ctx c, string key, FieldProblem problem)
    {
        var label = key.StartsWith("fact:", StringComparison.Ordinal)
            ? c.View.Facts.FirstOrDefault(fact => fact.Name == key[5..])?.Label ?? WorkbenchText.Humanize(key[5..])
            : c.View.OutcomeFields.FirstOrDefault(field => field.Name == key[4..])?.Label ?? WorkbenchText.Humanize(key[4..]);
        return problem.Kind switch
        {
            "reversed" => Plain("form.errReversed"),
            "missing" when key.StartsWith("out:", StringComparison.Ordinal) => Plain("err.required"),
            "missing" => Plain("form.errMissing", ("fact", label)),
            _ => Plain(ErrorKey(problem.Code ?? string.Empty)),
        };
    }

    private void ApplyForm(Ctx c)
    {
        var draft = c.Draft!;
        var form = draft.Form!;
        var compiled = Compile(c, form);
        if (compiled.Problems.Count > 0)
        {
            form.Problems = new Dictionary<string, FieldProblem>(compiled.Problems, StringComparer.Ordinal);
            form.Refusal = null;
            form.Blocked = string.Empty;
            var first = compiled.Problems.Keys.First();
            pendingFocus = ControlRef(first);
            Session.Touch();
            return;
        }

        var step = StepOf(c, form, compiled);
        List<DraftStep> steps = [.. draft.Steps, step];
        var probe = DraftEngine.Rebuild(c.Rule, draft, steps);
        if (probe.IsOk && string.Equals(probe.Text, draft.Text, StringComparison.Ordinal))
        {
            draft.Form = null;
            pendingFocus = EditRef(step.Key);
            Session.Touch();
            return;
        }

        var result = DraftEngine.Apply(c.Rule, draft, steps);
        if (!result.IsOk)
        {
            Refuse(c, form, result);
            return;
        }

        draft.Form = null;
        draft.Refusal = null;
        var work = c.Work;
        work.Stale = work.Result is not null;
        pendingFocus = EditRef(step.Key);
        Session.Touch();
    }

    private void Refuse(Ctx c, RowForm form, RebuildResult result)
    {
        form.Refusal = new DraftRefusal(null, DraftWording.Lines(text, result.Problems, c.View, result.Rule, form.Chosen(wording => Plain("op." + wording))));
        form.RefusalIsInternal = DraftWording.IsInternal(result.Problems);
        form.Marked.Clear();
        foreach (var place in result.Problems.Select(problem => problem.Place))
        {
            if (place.OutcomeField is { } field)
            {
                form.Marked.Add("out:" + field);
            }
            else if (place.Fact is { } fact)
            {
                form.Marked.Add("fact:" + fact);
            }
        }

        var marked = form.Marked.FirstOrDefault();
        pendingFocus = marked is null ? BoxRef : ControlRef(marked);
        Session.Touch();
    }

    // ----- changes of the fields --------------------------------------------------------------------------------------

    private void SetWording(Ctx c, RowForm form, ShownFact item, ChangeEventArgs args)
    {
        var line = form.LineFor(item.Fact.Name);
        line.Wording = args.Value?.ToString() ?? RowForm.AnyWording;
        line.LowerInclusive = true;
        line.UpperInclusive = true;
        if (!RowForm.TakesNoValue(line.Wording) && line.Value.Length == 0)
        {
            line.Value = item.Fact.Type switch
            {
                FactType.Boolean => "true",
                FactType.Enum when item.View.AllowedValues.Count > 0 && !RowForm.IsSet(line.Wording) => item.View.AllowedValues[0].Value,
                _ => string.Empty,
            };
        }

        Changed(c, form, "fact:" + item.Fact.Name);
    }

    private void SetLineText(Ctx c, RowForm form, string fact, bool upper, ChangeEventArgs args)
    {
        var line = form.LineFor(fact);
        if (upper)
        {
            line.Upper = args.Value?.ToString() ?? string.Empty;
        }
        else
        {
            line.Value = args.Value?.ToString() ?? string.Empty;
        }

        Changed(c, form, "fact:" + fact);
    }

    private void SetLineCurrency(Ctx c, RowForm form, string fact, ChangeEventArgs args)
    {
        form.LineFor(fact).Currency = args.Value?.ToString() ?? string.Empty;
        Changed(c, form, "fact:" + fact);
    }

    private void ToggleItem(Ctx c, RowForm form, string fact, string value, bool on)
    {
        var line = form.LineFor(fact);
        if (on && !line.Items.Contains(value, StringComparer.Ordinal))
        {
            line.Items.Add(value);
        }
        else if (!on)
        {
            line.Items.Remove(value);
        }

        Changed(c, form, "fact:" + fact);
    }

    private void AddChip(Ctx c, RowForm form, string fact, ChangeEventArgs args)
    {
        var line = form.LineFor(fact);
        var chip = args.Value?.ToString()?.Trim() ?? string.Empty;
        line.ChipText = string.Empty;
        if (chip.Length > 0 && !line.Items.Contains(chip, StringComparer.Ordinal))
        {
            line.Items.Add(chip);
        }

        Changed(c, form, "fact:" + fact);
    }

    private void RemoveChip(Ctx c, RowForm form, string fact, string chip)
    {
        form.LineFor(fact).Items.Remove(chip);
        Changed(c, form, "fact:" + fact);
    }

    private void SetOutcomeText(Ctx c, RowForm form, string field, ChangeEventArgs args)
    {
        form.Outcome[field] = args.Value?.ToString() ?? string.Empty;
        Changed(c, form, "out:" + field);
    }

    private void SetOutcomeCurrency(Ctx c, RowForm form, string field, ChangeEventArgs args)
    {
        form.OutcomeCurrency[field] = args.Value?.ToString() ?? string.Empty;
        Changed(c, form, "out:" + field);
    }

    private void SetRowName(Ctx c, RowForm form, ChangeEventArgs args)
    {
        form.Name = args.Value?.ToString() ?? string.Empty;
        Changed(c, form, "name");
    }

    private void ShowUnused(RowForm form)
    {
        form.ShowUnused = true;
        Session.Touch();
    }

    /// <summary>The choices of an outcome field that has a list: the sentences the host knows, a flag, or the labelled values; null when the person types it.</summary>
    private List<(string Value, string Label)>? OutcomeChoicesOf(Ctx c, OutcomeFieldDescriptor field)
    {
        List<(string Value, string Label)>? choices = null;
        if (c.Rule.OutcomeTexts is { } texts && texts.TryGetValue(field.Name, out var sentences))
        {
            choices = [.. sentences.Select(sentence => (FactForm.Text(sentence.Value), sentence.Text))];
        }
        else if (field.Type == FactType.Boolean)
        {
            choices = [("true", Plain("sim.yes")), ("false", Plain("sim.no"))];
        }
        else if (field.Type == FactType.Enum && c.Labels.Values.TryGetValue(field.Name, out var labelled) && labelled.Count > 0)
        {
            choices = [.. labelled.Select(pair => (pair.Key, pair.Value))];
        }

        if (choices is not null && field.Nullable)
        {
            choices.Insert(0, (string.Empty, Plain("form.noValue")));
        }

        return choices;
    }

    // ----- the row menu and the buttons of a row ----------------------------------------------------------------------

    private void ToggleMenu(string key, int firstEnabled)
    {
        menuFor = menuFor == key ? null : key;
        pendingFocus = menuFor is null ? MoreRef(key) : MenuItemRef(key, firstEnabled);
        Session.Touch();
    }

    private void OnMenuKey(KeyboardEventArgs args, string key, IReadOnlyList<int> enabled, int at)
    {
        if (args.Key == "Escape")
        {
            menuFor = null;
            pendingFocus = MoreRef(key);
            Session.Touch();
        }
        else if (args.Key is "ArrowDown" or "ArrowUp" && enabled.Count > 0)
        {
            var position = enabled.ToList().IndexOf(at);
            var step = args.Key == "ArrowDown" ? 1 : enabled.Count - 1;
            pendingFocus = MenuItemRef(key, enabled[(position + step) % enabled.Count]);
            Session.Touch();
        }
    }

    private void Remember(DraftState draft, string key, RebuildResult result, Ctx c, string focus)
    {
        menuFor = null;
        if (result.IsOk)
        {
            draft.Refusal = null;
            draft.Form = draft.Form is { } open && open.StepKey == key ? null : draft.Form;
            c.Work.Stale = c.Work.Result is not null;
            pendingFocus = focus;
        }
        else
        {
            draft.Refusal = new DraftRefusal(key, DraftWording.Lines(text, result.Problems, c.View, result.Rule, EmptyWords));
            pendingFocus = MoreRef(key);
        }

        Session.Touch();
    }

    private void MoveRow(Ctx c, string key, int delta)
    {
        var draft = c.Draft!;
        var keys = c.View.Rows.Select(row => row.Key).ToList();
        var at = keys.IndexOf(key);
        var target = at + delta;
        string? after = delta > 0 ? keys[at + 1] : null;
        if (delta < 0 && at >= 2)
        {
            after = keys[at - 2];
        }

        var step = new DraftStep(StepKind.Move, key, [new MoveRow(key, after)]);
        var result = DraftEngine.Apply(c.Rule, draft, [.. draft.Steps, step]);
        Remember(draft, key, result, c, MoreRef(key));
        if (result.IsOk)
        {
            liveMessage = Plain("row.movedNow", ("a", (at + 1).ToString(CultureInfo.InvariantCulture)), ("b", (target + 1).ToString(CultureInfo.InvariantCulture)));
        }
    }

    private void RemoveRowAction(Ctx c, string key)
    {
        var draft = c.Draft!;
        if (c.View.Rows.Count == 1 && c.View.DefaultRow is null)
        {
            menuFor = null;
            draft.Refusal = new DraftRefusal(key, [Plain("form.errNeedRow")]);
            pendingFocus = MoreRef(key);
            Session.Touch();
            return;
        }

        if (draft.Steps.Any(step => step.Kind == StepKind.Add && step.Key == key))
        {
            menuFor = null;
            Undo(c.Rule, draft, key, step => step.Key == key && step.Kind is StepKind.Add or StepKind.Row or StepKind.Move);
            pendingFocus = c.View.Rows.Count > 1 ? MoreRef(c.View.Rows.First(row => row.Key != key).Key) : AddEndRef;
            return;
        }

        var removal = new DraftStep(StepKind.Remove, key, [new RemoveRow(key)]);
        var result = DraftEngine.Apply(c.Rule, draft, [.. draft.Steps, removal]);
        Remember(draft, key, result, c, RestoreRef(key));
    }

    private void UndoRow(Ctx c, string key, Func<DraftStep, bool> remove)
    {
        var draft = c.Draft!;
        Undo(c.Rule, draft, key, remove);
        if (draft.Refusal is null)
        {
            if (draft.Form is { } open && open.StepKey == key)
            {
                draft.Form = null;
            }

            pendingFocus = EditRef(key);
            c.Work.Stale = c.Work.Result is not null;
        }
    }

    private void AddBelow(Ctx c, string key)
    {
        menuFor = null;
        OpenNew(c, key, MoreRef(key));
    }

    private void AddAtEnd(Ctx c) => OpenNew(c, c.View.Rows.Count == 0 ? null : c.View.Rows[^1].Key, AddEndRef);
}

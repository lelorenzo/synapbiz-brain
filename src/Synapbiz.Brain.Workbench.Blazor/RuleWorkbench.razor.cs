using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Nuvora.Decisions.Contracts.Evaluation;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>
/// The rule workbench: a list of rules and, for the selected rule, four tabs to read it as sentences, simulate it with example
/// data, run and add test cases, and review it and export the review. Every result comes from the headless
/// <c>Synapbiz.Brain.Workbench</c> library and so from the real engine; the panels add presentation only. The host styles it
/// with the <c>--wb-*</c> custom properties of <c>workbench.css</c> and owns what the panels do not: the leave guard, where the
/// state of a rule comes from and where an export is saved.
/// </summary>
public sealed partial class RuleWorkbench : IDisposable
{
    private const int NoteMax = 1000;
    private const int NoteCounterFrom = 900;

    private static readonly string[] SelectorTabs = ["tabs.read", "tabs.simulate", "tabs.tests", "tabs.review", "tabs.changes"];

    private static int instances;

    private readonly string idp = "wb" + Interlocked.Increment(ref instances).ToString(CultureInfo.InvariantCulture);
    private readonly Dictionary<string, ElementReference> refs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> noteIds = new(StringComparer.Ordinal);

    private WorkbenchSession? subscribedTo;
    private UiText text = new(WorkbenchLocale.En, null);
    private string? pendingFocus;
    private bool testsRunning;
    private bool nameError;
    private bool exportDone;
    private bool notesHeld;
    private string? exportPlace;
    private CaseForm? caseForm;

    /// <summary>The rules, in the order of the list; null while they load.</summary>
    [Parameter]
    public IReadOnlyList<WorkbenchRule>? Rules { get; set; }

    /// <summary>The language of the fixed strings and of the rule sentences.</summary>
    [Parameter]
    public WorkbenchLocale Locale { get; set; }

    /// <summary>The reviewer's state. The host owns its lifetime.</summary>
    [Parameter]
    [EditorRequired]
    public WorkbenchSession Session { get; set; } = default!;

    /// <summary>The host's clock. The library has none; the date of a simulation and the time of an export come from it.</summary>
    [Parameter]
    [EditorRequired]
    public Func<DateTimeOffset> Now { get; set; } = default!;

    /// <summary>
    /// Saves an export and returns the place to name in "saved to {place}", or null to say it without a place. When it is null
    /// the export button is not drawn.
    /// </summary>
    [Parameter]
    public Func<WorkbenchExport, Task<string?>>? OnExport { get; set; }

    /// <summary>Replaces fixed strings by key for the current locale. The four markings ignore an empty replacement.</summary>
    [Parameter]
    public IReadOnlyDictionary<string, string>? TextOverrides { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        ArgumentNullException.ThrowIfNull(Session);
        ArgumentNullException.ThrowIfNull(Now);

        text = new UiText(Locale, TextOverrides);
        if (!ReferenceEquals(subscribedTo, Session))
        {
            if (subscribedTo is not null)
            {
                subscribedTo.Changed -= OnSessionChanged;
            }

            Session.Changed += OnSessionChanged;
            subscribedTo = Session;
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (pendingFocus is { } key)
        {
            pendingFocus = null;
            if (refs.TryGetValue(key, out var element))
            {
                await element.FocusAsync().ConfigureAwait(true);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (subscribedTo is not null)
        {
            subscribedTo.Changed -= OnSessionChanged;
            subscribedTo = null;
        }

    }

    private void OnSessionChanged(object? sender, EventArgs args) => _ = InvokeAsync(StateHasChanged);

    // ----- text helpers -----------------------------------------------------------------------------------------------

    private string Plain(string key, params (string Name, string Value)[] values) => text.Plain(key, values);

    private RenderFragment Rich(string key, params (string Name, string Value)[] values) => text.Rich(key, values);

    private string PluralText(string key, long count, params (string Name, string Value)[] values) =>
        text.Plain(UiText.Plural(key, count), [.. values, ("n", OutcomeFormat.Number(count))]);

    private static string CurrentOption(CaseForm form)
    {
        if (form.ExpectNone)
        {
            return "none";
        }

        return form.Chosen.Count > 0 ? form.Chosen.Min.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private string LangCode => Locale == WorkbenchLocale.Es ? "es" : "en";

    /// <summary>
    /// The "You entered ..." sentence of a condition line in Row by row. A condition that is met and whose operator is equals,
    /// one-of or empty drops it, because the condition text already says the value; any other line keeps it, or says nothing was entered.
    /// </summary>
    private string EnteredText(bool met, Operator op, string typed)
    {
        if (met && !SimulationPresenter.AddsTypedValue(op))
        {
            return string.Empty;
        }

        return typed.Length > 0 ? Plain("sim.typed", ("value", typed)) : Plain("sim.notTyped");
    }

    private static string RowClass(bool deciding) => deciding ? "wb-row wb-win" : "wb-row";

    private static string PillClass(string tone) => tone switch
    {
        "ok" => "wb-pill wb-pill-ok",
        "warn" => "wb-pill wb-pill-warn",
        "bad" => "wb-pill wb-pill-bad",
        "sim" => "wb-pill wb-pill-sim",
        _ => "wb-pill",
    };

    // ----- selection --------------------------------------------------------------------------------------------------

    private WorkbenchRule? SelectedRule
    {
        get
        {
            var rules = Rules ?? [];
            var chosen = rules.FirstOrDefault(rule => string.Equals(rule.Key, Session.SelectedRuleKey, StringComparison.Ordinal));
            return chosen ?? (rules.Count > 0 ? rules[0] : null);
        }
    }

    private static bool IsReadable(WorkbenchRule rule) => rule.Rule is not null && rule.RuleText is not null;

    private void SelectRule(string key)
    {
        caseForm = null;
        discardOpen = false;
        Session.SelectedRuleKey = key;
    }

    private void SelectFromList(ChangeEventArgs args)
    {
        if (args.Value?.ToString() is { } value && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && Rules is not null && index < Rules.Count)
        {
            SelectRule(Rules[index].Key);
        }
    }

    private void OnListKeyDown(KeyboardEventArgs args)
    {
        if (Rules is null || Rules.Count == 0 || args.Key is not ("ArrowDown" or "ArrowUp"))
        {
            return;
        }

        var current = Math.Max(0, Rules.ToList().FindIndex(rule => string.Equals(rule.Key, SelectedRule?.Key, StringComparison.Ordinal)));
        var next = Math.Clamp(current + (args.Key == "ArrowDown" ? 1 : -1), 0, Rules.Count - 1);
        if (next != current)
        {
            pendingFocus = RuleRef(next);
            SelectRule(Rules[next].Key);
        }
    }

    private static readonly string[] RuleTarget = ["rule"];

    private const string CaseNameRef = "case:name";
    private const string ReviewerRef = "reviewer";

    private static string FieldSlotRef(string scope, int factIndex, int slot) =>
        FieldRef(scope, factIndex) + (slot == 0 ? string.Empty : "#" + slot.ToString(CultureInfo.InvariantCulture));

    private static string InputMode(FactType type) => type switch
    {
        FactType.Integer => "numeric",
        FactType.Decimal => "decimal",
        _ => string.Empty,
    };

    private static void SetCaseName(CaseForm form, ChangeEventArgs args) => form.Name = args.Value?.ToString() ?? string.Empty;

    private RenderFragment TestsHeading(TestRunView run) => run.Cases.Count == 0
        ? Rich("tests.empty")
        : builder => builder.AddContent(0, TestSummary(run));

    private static string RuleRef(int index) => "rule:" + index.ToString(CultureInfo.InvariantCulture);

    private static string TabRef(int index) => "tab:" + index.ToString(CultureInfo.InvariantCulture);

    private void SelectTab(WorkbenchTab tab) => Session.SelectedTab = tab;

    private void OnTabKeyDown(KeyboardEventArgs args)
    {
        var current = (int)Session.SelectedTab;
        var next = args.Key switch
        {
            "ArrowRight" => (current + 1) % TabCount,
            "ArrowLeft" => (current + TabCount - 1) % TabCount,
            "Home" => 0,
            "End" => TabCount - 1,
            _ => current,
        };
        if (next != current)
        {
            pendingFocus = TabRef(next);
            SelectTab((WorkbenchTab)next);
        }
    }

    // ----- review -----------------------------------------------------------------------------------------------------

    private void ToggleReview(RuleWork work, string target, ReviewState state)
    {
        var entry = work.ReviewForEdit(target);
        entry.State = entry.State == state ? ReviewState.Unreviewed : state;
        if (entry.State is ReviewState.NeedsFix or ReviewState.Question)
        {
            work.OpenNotes.Add(target);
        }

        exportDone = false;
        Session.Touch();
    }

    private void ToggleLine(RuleWork work, string target)
    {
        if (!work.OpenLines.Add(target))
        {
            work.OpenLines.Remove(target);
        }

        Session.Touch();
    }

    /// <summary>
    /// Opens the note field of an item that needs its note and focuses it. The field of a row lives in Read, the field of a case
    /// in the review line of its row in Test cases, and the field of the whole rule in Review itself.
    /// </summary>
    private void WriteNote(RuleWork work, string target)
    {
        work.OpenNotes.Add(target);
        if (target.StartsWith("row:", StringComparison.Ordinal))
        {
            Session.SelectedTab = WorkbenchTab.Read;
        }
        else if (target.StartsWith("golden:", StringComparison.Ordinal))
        {
            work.OpenLines.Add(target);
            Session.SelectedTab = WorkbenchTab.Tests;
        }

        pendingFocus = NoteRef(target);
        Session.Touch();
    }

    private void SetNote(RuleWork work, string target, string? note)
    {
        var value = (note ?? string.Empty).Trim();
        work.ReviewForEdit(target).Note = value.Length > NoteMax ? value[..NoteMax] : value;
        exportDone = false;
        notesHeld = notesHeld && MissingNoteCount() > 0;
        Session.Touch();
    }

    private void OpenNote(RuleWork work, string target)
    {
        work.OpenNotes.Add(target);
        pendingFocus = NoteRef(target);
        Session.Touch();
    }

    private static bool NoteVisible(RuleWork work, string target)
    {
        var entry = work.Review(target);
        return entry.State is ReviewState.NeedsFix or ReviewState.Question || entry.Note.Length > 0 || work.OpenNotes.Contains(target);
    }

    private string StateWord(ReviewState state) => state switch
    {
        ReviewState.Confirmed => Plain("review.confirmed"),
        ReviewState.NeedsFix => Plain("review.fixState"),
        ReviewState.Question => Plain("review.questionState"),
        _ => Plain("review.unreviewed"),
    };

    private static string StateTone(ReviewState state) => state switch
    {
        ReviewState.Confirmed => "ok",
        ReviewState.NeedsFix or ReviewState.Question => "warn",
        _ => "neutral",
    };

    private string NoteId(string target) => idp + "-note-" + TargetNumber(target).ToString(CultureInfo.InvariantCulture);

    private string LineId(string target) => idp + "-line-" + TargetNumber(target).ToString(CultureInfo.InvariantCulture);

    private int TargetNumber(string target)
    {
        if (!noteIds.TryGetValue(target, out var number))
        {
            number = noteIds.Count + 1;
            noteIds[target] = number;
        }

        return number;
    }

    private static string NoteRef(string target) => "note:" + target;

    private static string RowTarget(RowView row) => "row:" + row.RowId;

    private static string CaseTarget(string name) => "golden:" + name;

    // ----- simulator --------------------------------------------------------------------------------------------------

    private void FieldChanged(RuleWork work)
    {
        work.Stale = work.Result is not null;
        work.LoadedCase = string.Empty;
        Session.Touch();
    }

    private static void SetField(FactForm form, string name, ChangeEventArgs args, Action? changed)
    {
        form.SetText(name, args.Value?.ToString());
        changed?.Invoke();
    }

    private static void SetCurrency(FactForm form, string name, ChangeEventArgs args, Action? changed)
    {
        form.SetCurrency(name, args.Value?.ToString());
        changed?.Invoke();
    }

    private void RunSimulation(Ctx c, RuleWork work)
    {
        var brain = c.Brain;
        var shown = c.Shown;
        var parsed = work.Simulator.Parse(brain, shown);
        work.Simulator.Errors.Clear();
        work.RefusalCode = null;
        if (!parsed.IsValid)
        {
            foreach (var (name, code) in parsed.Errors)
            {
                work.Simulator.Errors[name] = code;
            }

            work.Result = null;
            work.Stale = false;
            var first = shown.FirstOrDefault(item => parsed.Errors.ContainsKey(item.Fact.Name));
            if (first is not null)
            {
                pendingFocus = FieldRef("sim", first.Index);
            }

            Session.Touch();
            return;
        }

        work.ResultFacts = parsed.Facts;
        work.Result = RuleSimulator.Simulate(brain, parsed.Facts, DateOnly.FromDateTime(Now().DateTime));
        work.Stale = false;
        work.ShowAllRows = false;
        Session.Touch();
    }

    private static string FieldRef(string scope, int factIndex) =>
        "field:" + scope + ":" + factIndex.ToString(CultureInfo.InvariantCulture);

    private void ClearSimulation(RuleWork work, IReadOnlyList<ShownFact> shown)
    {
        work.Simulator.Reset(shown);
        work.Result = null;
        work.Stale = false;
        work.LoadedCase = string.Empty;
        work.RefusalCode = null;
        Session.Touch();
    }

    private void LoadCase(RuleWork work, IReadOnlyList<ShownFact> shown, IReadOnlyList<TestCaseView> cases, ChangeEventArgs args)
    {
        if (args.Value?.ToString() is { } value && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < cases.Count)
        {
            OpenCaseInSimulator(work, shown, cases[index]);
            work.LoadedCase = value;
        }
    }

    private void OpenCaseInSimulator(RuleWork work, IReadOnlyList<ShownFact> shown, TestCaseView testCase)
    {
        work.Simulator.Load(shown, testCase.Facts);
        work.Result = null;
        work.Stale = false;
        work.LoadedCase = string.Empty;
        work.RefusalCode = null;
        Session.SelectedTab = WorkbenchTab.Simulate;
        Session.Touch();
    }

    private void AddRefusalToReview(RuleWork work, string code)
    {
        work.ReviewForEdit("rule").State = ReviewState.NeedsFix;
        work.OpenNotes.Add("rule");
        work.RefusalCode = code;
        exportDone = false;
        pendingFocus = NoteRef("rule");
        Session.SelectedTab = WorkbenchTab.Review;
        Session.Touch();
    }

    // ----- tests ------------------------------------------------------------------------------------------------------

    private async Task RunAllAsync(WorkbenchRule rule, RuleWork work)
    {
        testsRunning = true;
        StateHasChanged();
        await Task.Delay(1).ConfigureAwait(true);
        work.Tests(rule.Rule!, force: true);
        testsRunning = false;
        Session.Touch();
    }

    private void OpenCaseForm(WorkbenchRule rule, IReadOnlyList<ShownFact> shown, Func<CaseForm, CaseForm>? fill = null)
    {
        var form = new CaseForm(rule.Key);
        form.Facts.Reset(shown);
        caseForm = fill is null ? form : fill(form);
        pendingFocus = CaseNameRef;
        Session.Touch();
    }

    private void OpenFormFromSimulation(WorkbenchRule rule, BrainRule brain, RuleWork work, IReadOnlyList<ShownFact> shown, RuleView view, RuleLabels labels)
    {
        var options = CaseForm.Options(brain, view, labels, Locale);
        OpenCaseForm(rule, shown, form =>
        {
            foreach (var fact in shown.Select(item => item.Fact))
            {
                if (work.ResultFacts is { } facts && facts.TryGetValue(fact.Name, out var value))
                {
                    if (value is MoneyValue money)
                    {
                        form.Facts.SetText(fact.Name, money.Amount.ToString(CultureInfo.InvariantCulture));
                        form.Facts.SetCurrency(fact.Name, money.Currency);
                    }
                    else
                    {
                        form.Facts.SetText(fact.Name, FactForm.Text(value));
                    }
                }
                else if (fact.Type == FactType.Enum && fact.Nullable)
                {
                    form.Facts.SetText(fact.Name, string.Empty);
                }
            }

            if (work.Result?.Outcome is Decided)
            {
                var deciding = SimulationPresenter.DecidingKeys(work.Result);
                foreach (var key in deciding)
                {
                    if (CaseForm.OptionOf(brain, options, key) is { } index)
                    {
                        form.Chosen.Add(index);
                    }
                }

                if (view.HitPolicy != HitPolicy.Collect && deciding.Count == 1)
                {
                    form.PinnedRowKey = deciding[0];
                }
            }
            else if (work.Result is { Succeeded: true })
            {
                form.ExpectNone = true;
            }

            return form;
        });
        Session.SelectedTab = WorkbenchTab.Tests;
    }

    private void OpenFormForRow(WorkbenchRule rule, BrainRule brain, IReadOnlyList<ShownFact> shown, RuleView view, RuleLabels labels, string rowKey)
    {
        var options = CaseForm.Options(brain, view, labels, Locale);
        OpenCaseForm(rule, shown, form =>
        {
            if (CaseForm.OptionOf(brain, options, rowKey) is { } index)
            {
                form.Chosen.Add(index);
                form.PinnedRowKey = view.HitPolicy == HitPolicy.Collect ? null : rowKey;
            }

            return form;
        });
    }

    private void CancelCaseForm()
    {
        caseForm = null;
        Session.Touch();
    }

    private static void ChooseOption(CaseForm form, int index, HitPolicy policy, bool on)
    {
        form.ExpectedError = null;
        form.PinnedRowKey = null;
        form.ExpectNone = false;
        if (policy != HitPolicy.Collect)
        {
            form.Chosen.Clear();
        }

        if (on)
        {
            form.Chosen.Add(index);
        }
        else
        {
            form.Chosen.Remove(index);
        }
    }

    private static void ChooseOptionFromSelect(CaseForm form, ChangeEventArgs args, HitPolicy policy)
    {
        var value = args.Value?.ToString() ?? string.Empty;
        if (value == "none")
        {
            form.Chosen.Clear();
            form.PinnedRowKey = null;
            form.ExpectNone = true;
            form.ExpectedError = null;
        }
        else if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            ChooseOption(form, index, policy, true);
        }
        else
        {
            form.Chosen.Clear();
            form.PinnedRowKey = null;
            form.ExpectNone = false;
        }
    }

    private static void ChooseNone(CaseForm form, bool on)
    {
        form.ExpectNone = on;
        form.ExpectedError = null;
        form.PinnedRowKey = null;
        if (on)
        {
            form.Chosen.Clear();
        }
    }

    private void SaveCase(WorkbenchRule rule, RuleWork work, IReadOnlyList<ShownFact> shown, RuleView view, RuleLabels labels)
    {
        var form = caseForm!;
        var brain = rule.Rule!;
        form.NameError = null;
        form.ExpectedError = null;
        form.Facts.Errors.Clear();

        var name = form.Name.Trim();
        var taken = brain.Goldens.Select(golden => golden.Name).Concat(work.Cases.Select(entry => entry.Name))
            .Any(existing => string.Equals(existing, name, StringComparison.Ordinal));
        if (name.Length == 0)
        {
            form.NameError = "case.nameRequired";
        }
        else if (taken)
        {
            form.NameError = "case.nameTaken";
        }

        var parsed = form.Facts.Parse(brain, shown);
        foreach (var (fact, code) in parsed.Errors)
        {
            form.Facts.Errors[fact] = code;
        }

        var options = CaseForm.Options(brain, view, labels, Locale);
        var expectation = form.Expectation(options, view.HitPolicy);
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

        work.AddCase(new ProposedCaseEntry(name, parsed.Facts, expectation, (expectation as ExpectedDecided)?.RowKey));
        caseForm = null;
        exportDone = false;
        Session.Touch();
    }

    // ----- export -----------------------------------------------------------------------------------------------------

    /// <summary>How many items, in every readable rule of the session, are a fix or a question with no note.</summary>
    private int MissingNoteCount()
    {
        var count = 0;
        foreach (var rule in (Rules ?? []).Where(IsReadable))
        {
            var work = Session.Work(rule.Key);
            var targets = RuleTarget
                .Concat(work.View(rule, Locale).Rows.Select(RowTarget))
                .Concat(rule.Rule!.Goldens.Select(golden => CaseTarget(golden.Name)));
            count += targets.Count(target => work.Review(target).NeedsNote);
        }

        return count;
    }

    private int ExportableRuleCount() =>
        Rules?.Count(rule => IsReadable(rule) && Session.Work(rule.Key).HasExportable) ?? 0;

    private WorkbenchExport BuildExport()
    {
        var createdAt = Now();
        var reviewer = Session.Reviewer.Trim();
        var files = new List<WorkbenchExportFile>();
        foreach (var rule in Rules ?? [])
        {
            if (!IsReadable(rule))
            {
                continue;
            }

            var brain = rule.Rule!;
            var work = Session.Work(rule.Key);
            var view = work.View(rule, Locale);

            var items = new List<ReviewItem>();
            var targets = new List<string> { "rule" };
            targets.AddRange(view.Rows.Select(RowTarget));
            targets.AddRange(brain.Goldens.Select(golden => CaseTarget(golden.Name)));
            foreach (var target in targets)
            {
                var entry = work.Review(target);
                if (entry.IsExported)
                {
                    items.Add(new ReviewItem(target, entry.State, ExportNote(work, target, entry)));
                }
            }

            var cases = work.Cases.Select(entry => new ProposedCase(
                entry.Name,
                entry.Facts,
                entry.Expected is ExpectedNoDecision,
                (entry.Expected as ExpectedDecided)?.OutcomeJson,
                entry.ExpectedRowKey is { } key && brain.RowIdsByKey.TryGetValue(key, out var rowId) ? rowId : null)).ToList();

            if (items.Count == 0 && cases.Count == 0)
            {
                continue;
            }

            var review = new RuleReview(brain.Id, brain.Version, RuleReviewJson.ContentHash(rule.RuleText!), reviewer, createdAt, items, cases);
            files.Add(new WorkbenchExportFile(
                "review-" + brain.Id + "-v" + brain.Version.ToString(CultureInfo.InvariantCulture) + ".json",
                RuleReviewJson.Serialize(review),
                review));
        }

        return new WorkbenchExport(files);
    }

    private static string ExportNote(RuleWork work, string target, ReviewEntry entry)
    {
        if (target != "rule" || work.RefusalCode is not { } code)
        {
            return entry.Note;
        }

        var separator = entry.Note.Length > 0 ? "\n" : string.Empty;
        return entry.Note + separator + "(engine code: " + code + ")";
    }

    private async Task ExportAsync()
    {
        if (OnExport is null)
        {
            return;
        }

        notesHeld = MissingNoteCount() > 0;
        if (string.IsNullOrWhiteSpace(Session.Reviewer))
        {
            nameError = true;
            pendingFocus = ReviewerRef;
            return;
        }

        nameError = false;
        if (notesHeld)
        {
            return;
        }

        var export = BuildExport();
        if (export.Files.Count == 0)
        {
            return;
        }

        var place = await OnExport(export).ConfigureAwait(true);
        exportDone = true;
        exportPlace = string.IsNullOrWhiteSpace(place) ? null : place.Trim();
        Session.MarkExported();
    }

    private void SetReviewer(ChangeEventArgs args)
    {
        Session.Reviewer = args.Value?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(Session.Reviewer))
        {
            nameError = false;
        }
    }
}

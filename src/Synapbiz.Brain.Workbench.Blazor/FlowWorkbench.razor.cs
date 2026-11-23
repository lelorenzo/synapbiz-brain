using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>
/// The flow workbench: a list of flows and, for the selected flow, five tabs to read it in plain language, see it as a diagram, try it
/// with a made-up case on a virtual clock, see which routes the tests cover, and review it and export the review. Every sentence,
/// graph, simulation step, coverage figure and review document comes from the headless <c>Synapbiz.Brain.Workbench.Flows</c> library;
/// the panels add presentation only. Nothing here edits a flow. The host styles it with the <c>--wb-*</c> custom properties of
/// <c>workbench.css</c> and owns what the panels do not: the leave guard, where the flows come from and where an export is saved.
/// </summary>
public sealed partial class FlowWorkbench : IDisposable
{
    private const int NoteMax = 1000;
    private const int NoteCounterFrom = 900;

    private static readonly string[] TabKeys = ["flow.tabs.read", "flow.tabs.diagram", "flow.tabs.try", "flow.tabs.tests", "flow.tabs.review"];

    private static int instances;

    private readonly string idp = "wf" + Interlocked.Increment(ref instances).ToString(CultureInfo.InvariantCulture);
    private readonly Dictionary<string, ElementReference> refs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> noteIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FlowData> data = new(StringComparer.Ordinal);

    private FlowWorkbenchSession? subscribedTo;
    private UiText text = new(WorkbenchLocale.En, null);
    private string? pendingFocus;
    private IReadOnlyList<WorkbenchFlow>? builtFor;
    private WorkbenchLocale builtLocale;
    private IReadOnlyList<FlowEntry> library = [];
    private List<BrainProcess> known = [];

    /// <summary>The flows, in the order of the list; null while they load.</summary>
    [Parameter]
    public IReadOnlyList<WorkbenchFlow>? Flows { get; set; }

    /// <summary>The language of the fixed strings, of the sentences and of the labels.</summary>
    [Parameter]
    public WorkbenchLocale Locale { get; set; }

    /// <summary>The reader's state. The host owns its lifetime.</summary>
    [Parameter]
    [EditorRequired]
    public FlowWorkbenchSession Session { get; set; } = default!;

    /// <summary>The host's clock. The library has none; the default start of a try-out and the time of an export come from it.</summary>
    [Parameter]
    [EditorRequired]
    public Func<DateTimeOffset> Now { get; set; } = default!;

    /// <summary>
    /// Fixed UTC offsets for the zones a try-out may meet, by zone id. A zone that is not listed makes its delivery window fail in the
    /// try-out, and the panel says so in words.
    /// </summary>
    [Parameter]
    public IReadOnlyDictionary<string, TimeSpan>? Zones { get; set; }

    /// <summary>The host's source of answers for effects (for example the practice's own AI account). When null, the "ask" buttons are not drawn.</summary>
    [Parameter]
    public IFlowAnswerSource? AnswerSource { get; set; }

    /// <summary>
    /// Saves an export and returns the place to name in "saved to {place}", or null to say it without a place. When it is not set the
    /// export button is not drawn.
    /// </summary>
    [Parameter]
    public Func<FlowWorkbenchExport, Task<string?>>? OnExport { get; set; }

    /// <summary>Replaces fixed strings by key for the current locale. The markings ignore an empty replacement.</summary>
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

        if (!ReferenceEquals(builtFor, Flows) || builtLocale != Locale)
        {
            data.Clear();
            frameInfo.Clear();
            known = [.. (Flows ?? []).Where(flow => flow.IsReadable).Select(flow => flow.Process!)];
            library = FlowLibrary.From(known, Locale);
            builtFor = Flows;
            builtLocale = Locale;
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

        asking?.Cancel();
        asking?.Dispose();
    }

    private void OnSessionChanged(object? sender, EventArgs args) => _ = InvokeAsync(StateHasChanged);

    // ----- text helpers -----------------------------------------------------------------------------------------------

    private string LangCode => Locale == WorkbenchLocale.Es ? "es" : "en";

    private string Plain(string key, params (string Name, string Value)[] values) => text.Plain(key, values);

    private RenderFragment Rich(string key, params (string Name, string Value)[] values) => text.Rich(key, values);

    private string PluralText(string key, long count, params (string Name, string Value)[] values) =>
        text.Plain(UiText.Plural(key, count), [.. values, ("n", Number(count))]);

    /// <summary>The name of a variable or a member as a label: words, the first one capitalised.</summary>
    private static string Labelled(string name)
    {
        var words = WorkbenchText.Humanize(name);
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private string TimeText(DateTimeOffset moment) =>
        Plain("flow.time", ("time", moment.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));

    /// <summary>A sentence of the library: the ids it writes between backticks are drawn as code.</summary>
    private static RenderFragment Sentence(string value) => builder =>
    {
        var parts = value.Split('`');
        for (var index = 0; index < parts.Length; index++)
        {
            var isCode = index % 2 == 1 && index < parts.Length - 1;
            if (isCode)
            {
                builder.OpenElement(0, "code");
                builder.AddAttribute(1, "class", "wb-code");
                builder.AddContent(2, parts[index]);
                builder.CloseElement();
            }
            else
            {
                // An unmatched backtick stays as typed.
                builder.AddContent(3, index % 2 == 1 ? "`" + parts[index] : parts[index]);
            }
        }
    };

    /// <summary>An id or a value written as code.</summary>
    private static RenderFragment Code(string value) => builder =>
    {
        builder.OpenElement(0, "code");
        builder.AddAttribute(1, "class", "wb-code");
        builder.AddContent(2, value);
        builder.CloseElement();
    };

    private static string PillClass(string tone) => tone switch
    {
        "ok" => "wb-pill wb-pill-ok",
        "warn" => "wb-pill wb-pill-warn",
        "bad" => "wb-pill wb-pill-bad",
        "sim" => "wb-pill wb-pill-sim",
        _ => "wb-pill",
    };

    // ----- the list ---------------------------------------------------------------------------------------------------

    private sealed record ListEntry(WorkbenchFlow Flow, int Index);

    private sealed record ListGroup(int Ordinal, string? Heading, List<ListEntry> Entries);

    private List<ListGroup> FlowGroups()
    {
        var groups = new List<ListGroup>();
        var flows = Flows ?? [];
        for (var index = 0; index < flows.Count; index++)
        {
            if (groups.Count == 0 || !string.Equals(groups[^1].Heading, flows[index].Group, StringComparison.Ordinal))
            {
                groups.Add(new ListGroup(groups.Count + 1, flows[index].Group, []));
            }

            groups[^1].Entries.Add(new ListEntry(flows[index], index));
        }

        return groups;
    }

    private WorkbenchFlow? SelectedFlow
    {
        get
        {
            var flows = Flows ?? [];
            var chosen = flows.FirstOrDefault(flow => string.Equals(flow.Key, Session.SelectedFlowKey, StringComparison.Ordinal));
            return chosen ?? (flows.Count > 0 ? flows[0] : null);
        }
    }

    private FlowData? Data(WorkbenchFlow flow)
    {
        if (!flow.IsReadable)
        {
            return null;
        }

        if (!data.TryGetValue(flow.Key, out var built))
        {
            var process = flow.Process!;
            var entry = library.First(item => item.Id == process.Id && item.Version == process.Version && item.ContentHash == process.ContentHash);
            built = FlowData.Build(process, Locale, known, entry);
            data[flow.Key] = built;
        }

        return built;
    }

    private string TitleOf(WorkbenchFlow flow)
    {
        if (Data(flow) is { } d)
        {
            return d.Entry.Title;
        }

        return string.IsNullOrWhiteSpace(flow.FallbackTitle) ? Plain("flow.load.untitled") : flow.FallbackTitle;
    }

    /// <summary>The title of the flow with an id (the highest version of it among the readable flows), for "started by".</summary>
    private string TitleOfId(string id) =>
        library.Where(entry => entry.Id == id).OrderByDescending(entry => entry.Version).Select(entry => entry.Title).FirstOrDefault() ?? WorkbenchText.Humanize(id);

    private WorkbenchFlow? FlowWithId(string id) =>
        (Flows ?? []).Where(flow => flow.IsReadable && flow.Process!.Id == id).OrderByDescending(flow => flow.Process!.Version).FirstOrDefault();

    private void SelectFlow(string key)
    {
        ResetTransient();
        Session.SelectedFlowKey = key;
    }

    private void SelectFromList(ChangeEventArgs args)
    {
        if (args.Value?.ToString() is { } value && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && Flows is not null && index < Flows.Count)
        {
            SelectFlow(Flows[index].Key);
        }
    }

    private void OnListKeyDown(KeyboardEventArgs args)
    {
        if (Flows is null || Flows.Count == 0 || args.Key is not ("ArrowDown" or "ArrowUp"))
        {
            return;
        }

        var current = Math.Max(0, Flows.ToList().FindIndex(flow => string.Equals(flow.Key, SelectedFlow?.Key, StringComparison.Ordinal)));
        var next = Math.Clamp(current + (args.Key == "ArrowDown" ? 1 : -1), 0, Flows.Count - 1);
        if (next != current)
        {
            pendingFocus = FlowRef(next);
            SelectFlow(Flows[next].Key);
        }
    }

    private static string FlowRef(int index) => "flow:" + Number(index);

    private static string TabRef(int index) => "tab:" + Number(index);

    private static string StepRef(string step) => "step:" + step;

    private static string NodeRef(string step) => "node:" + step;

    private string CountLine() => PluralText("flow.list.count", Flows?.Count(flow => flow.IsReadable) ?? 0);

    private string StartsLine(FlowData d)
    {
        if (d.Entry.Triggers.Count > 0)
        {
            return d.Entry.Triggers[0] + (d.Entry.Triggers.Count > 1 ? Plain("flow.list.moreTriggers", ("n", Number(d.Entry.Triggers.Count - 1))) : string.Empty);
        }

        return d.Entry.StartedBy.Count > 0
            ? Plain("flow.list.startedBy", ("title", string.Join(Plain("flow.join"), d.Entry.StartedBy.Select(TitleOfId))))
            : Plain("flow.list.startedByHost");
    }

    private string TestsLine(FlowData d)
    {
        var failed = d.Scenarios.Count(line => !line.Passed);
        if (d.Entry.ScenarioCount == 0)
        {
            return Plain("flow.list.noTests");
        }

        return failed == 0
            ? PluralText("flow.list.testsPass", d.Entry.ScenarioCount)
            : PluralText("flow.list.testsFail", failed, ("t", Number(d.Entry.ScenarioCount)));
    }

    private string ReviewLine(WorkbenchFlow flow, FlowData d)
    {
        var work = Session.Work(flow.Key);
        var marked = d.Targets.Count(target => work.Review(target).State != ReviewState.Unreviewed);
        if (marked == 0)
        {
            return Plain("flow.list.review.none");
        }

        return marked == d.Targets.Count
            ? Plain("flow.list.review.done")
            : Plain("flow.list.review.partial", ("n", Number(marked)), ("t", Number(d.Targets.Count)));
    }

    // ----- tabs -------------------------------------------------------------------------------------------------------

    private void SelectTab(FlowWorkbenchTab tab) => Session.SelectedTab = tab;

    private void OnTabKeyDown(KeyboardEventArgs args)
    {
        var current = (int)Session.SelectedTab;
        var count = TabKeys.Length;
        var next = args.Key switch
        {
            "ArrowRight" => (current + 1) % count,
            "ArrowLeft" => (current + count - 1) % count,
            "Home" => 0,
            "End" => count - 1,
            _ => current,
        };
        if (next != current)
        {
            pendingFocus = TabRef(next);
            SelectTab((FlowWorkbenchTab)next);
        }
    }

    /// <summary>Drops what only makes sense for the flow that was open: a question about discarding a try-out, a note about a refusal.</summary>
    private void ResetTransient()
    {
        asking?.Cancel();
        exportDone = false;
        notesHeld = false;
        nameError = false;
        if (SelectedFlow is { } flow)
        {
            Session.Work(flow.Key).ConfirmReset = false;
        }
    }

    // ----- the diagram selection and the links between steps -----------------------------------------------------------

    /// <summary>Moves focus to a step of the flow in the Read tab.</summary>
    private void GoToStep(string step)
    {
        pendingFocus = StepRef(step);
        Session.Touch();
    }

    /// <summary>Opens the card of a step in the diagram and moves focus to its node.</summary>
    private void OpenCard(string step)
    {
        pendingFocus = NodeRef(step);
        Session.SelectedStepId = step;
        Session.Touch();
    }

    private void ToggleCard(string step)
    {
        if (string.Equals(Session.SelectedStepId, step, StringComparison.Ordinal))
        {
            Session.SelectedStepId = null;
        }
        else
        {
            Session.SelectedStepId = step;
        }
    }

    private void OnNodeKeyDown(KeyboardEventArgs args, string step)
    {
        if (args.Key is "Enter" or " " or "Spacebar")
        {
            ToggleCard(step);
        }
    }

    private void FollowRoute(bool inCard, string step)
    {
        if (inCard)
        {
            OpenCard(step);
        }
        else
        {
            GoToStep(step);
        }
    }

    private void ReadAsList()
    {
        Session.SelectedTab = FlowWorkbenchTab.Read;
    }
}

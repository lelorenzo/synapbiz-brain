using Microsoft.AspNetCore.Components;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Blazor;

// The live view: the numbers of open cases over the map, the list of the cases of a process or phase, and one case over its diagram.
// The component only reads through IFlowLiveSource and never acts on a case. It asks the source when the source, the refresh token or the
// view changes, when the reader chooses something and when Refresh is called; it never polls.
public sealed partial class FlowWorkbench
{
    private const int LivePageSize = 25;
    private const int LiveMaxPagesPerClick = 8;

    private IFlowLiveSource? liveBuiltSource;
    private object? liveBuiltToken;
    private bool liveStale = true;

    private FlowLiveCounts? liveCounts;
    private ProcessMapLive? liveOverlay;
    private bool liveLoading;
    private bool liveFailed;
    private int countsGeneration;
    private CancellationTokenSource? countsCts;

    private string? listKey;
    private List<FlowLiveCaseLine> listCases = [];
    private string? listNext;
    private bool listLoading;
    private bool listFailed;
    private int listGeneration;
    private CancellationTokenSource? listCts;

    private string? caseKey;
    private FlowLiveCase? caseData;
    private LiveCasePath? casePath;
    private bool caseLoading;
    private bool caseFailed;
    private bool caseGone;
    private int caseGeneration;
    private CancellationTokenSource? caseCts;

    /// <summary>
    /// The host's source of the cases open now. When it is null the live view is not drawn at all: there is no "Live" button and nothing
    /// asks for cases. The component reads it and never acts on a case.
    /// </summary>
    [Parameter]
    public IFlowLiveSource? LiveSource { get; set; }

    /// <summary>
    /// Raised with the opaque reference of a case when the reader chooses it in the list, so the host can open the case in its own tools.
    /// The reference is never a business key. The component draws the path of the case whether or not the host handles this.
    /// </summary>
    [Parameter]
    public EventCallback<string> OnOpenCase { get; set; }

    /// <summary>
    /// Any value the host changes when it wants the numbers read again (a counter, a time). It is compared with <see cref="object.Equals(object?, object?)"/>.
    /// <see cref="Refresh"/> does the same from code.
    /// </summary>
    [Parameter]
    public object? LiveRefreshToken { get; set; }

    /// <summary>Reads the counts, and the open list and case if there are any, from the live source again. Does nothing without a source.</summary>
    public Task Refresh() => InvokeAsync(async () =>
    {
        if (LiveSource is null)
        {
            return;
        }

        liveStale = false;
        StateHasChanged();
        await LiveSyncAsync(force: true).ConfigureAwait(true);
        StateHasChanged();
    });

    private FlowWorkbenchView EffectiveView => Session.View == FlowWorkbenchView.Live && LiveSource is null ? FlowWorkbenchView.Map : Session.View;

    private bool ShowLive => EffectiveView == FlowWorkbenchView.Live && Flows is { Count: > 0 } && known.Count > 0;

    /// <summary>True when the live view is drawn and what the session asks for (the counts, a list, a case) has not been read yet.</summary>
    private bool NeedsLiveSync => ShowLive && LiveSource is not null
        && (liveStale
            || (liveCounts is null && !liveLoading && !liveFailed)
            || (WantedListKey is { } list && !string.Equals(listKey, list, StringComparison.Ordinal))
            || (WantedCaseKey is { } wanted && !string.Equals(caseKey, wanted, StringComparison.Ordinal)));

    /// <summary>Reads what the session now asks for, when the host (or a test) changed the session from outside.</summary>
    private async Task SyncAfterSessionChangeAsync()
    {
        if (!NeedsLiveSync)
        {
            StateHasChanged();
            return;
        }

        StateHasChanged();
        await LiveSyncAsync(force: false).ConfigureAwait(true);
        StateHasChanged();
    }

    private ProcessMapLive? Overlay => liveOverlay ??= liveCounts is null ? null : ProcessMapLive.From(known, liveCounts);

    private void LiveParametersChanged()
    {
        if (!ReferenceEquals(liveBuiltSource, LiveSource))
        {
            liveBuiltSource = LiveSource;
            ClearLive();
        }
        else if (!Equals(liveBuiltToken, LiveRefreshToken))
        {
            liveStale = true;
        }

        liveBuiltToken = LiveRefreshToken;
    }

    private void ClearLive()
    {
        CancelLive();
        liveStale = true;
        liveCounts = null;
        liveOverlay = null;
        liveLoading = false;
        liveFailed = false;
        listKey = null;
        listCases = [];
        listNext = null;
        listLoading = false;
        listFailed = false;
        caseKey = null;
        caseData = null;
        casePath = null;
        caseLoading = false;
        caseFailed = false;
        caseGone = false;
    }

    private void CancelLive()
    {
        countsGeneration++;
        listGeneration++;
        caseGeneration++;
        Cancel(ref countsCts);
        Cancel(ref listCts);
        Cancel(ref caseCts);
    }

    private static void Cancel(ref CancellationTokenSource? source)
    {
        source?.Cancel();
        source?.Dispose();
        source = null;
    }

    private static CancellationToken Renew(ref CancellationTokenSource? source)
    {
        Cancel(ref source);
        source = new CancellationTokenSource();
        return source.Token;
    }

    // The whole process and the steps with no stage (an empty stage id) are two different lists: the key tells them apart.
    private string? WantedListKey => Session.LiveScopeProcessId is { } id ? id + "|" + ScopeMark(Session.LiveScopeStageId) : null;

    private static string ScopeMark(string? stage) => stage is null ? "*" : "#" + stage;

    private string? WantedCaseKey => Session.LiveCaseRef;

    /// <summary>Brings what is drawn up to date with what the session asks for. With <paramref name="force"/> everything is read again.</summary>
    private async Task LiveSyncAsync(bool force)
    {
        if (LiveSource is not { } source)
        {
            return;
        }

        if (liveStale)
        {
            force = true;
            liveStale = false;
        }

        if (force || (liveCounts is null && !liveLoading))
        {
            await LoadCountsAsync(source).ConfigureAwait(true);
        }

        if (WantedListKey is { } wantedList && (force || !string.Equals(listKey, wantedList, StringComparison.Ordinal)))
        {
            await LoadListAsync(source, append: false).ConfigureAwait(true);
        }

        if (WantedCaseKey is { } wantedCase && (force || !string.Equals(caseKey, wantedCase, StringComparison.Ordinal)))
        {
            await LoadCaseAsync(source, wantedCase).ConfigureAwait(true);
        }
    }

    private async Task LoadCountsAsync(IFlowLiveSource source)
    {
        var generation = ++countsGeneration;
        var token = Renew(ref countsCts);
        liveLoading = true;
        liveFailed = false;
        try
        {
            var counts = await source.GetCountsAsync(token).ConfigureAwait(true);
            if (generation != countsGeneration)
            {
                return;
            }

            liveCounts = counts;
            liveOverlay = null;
        }
        catch (OperationCanceledException)
        {
            // A newer read replaced this one, or the component went away: the newer read says what is true.
        }
#pragma warning disable CA1031 // The source belongs to the host: whatever it throws, the screen says so in words and keeps what it had.
        catch (Exception) when (generation == countsGeneration)
        {
            liveFailed = true;
        }
#pragma warning restore CA1031
        finally
        {
            if (generation == countsGeneration)
            {
                liveLoading = false;
            }
        }
    }

    private async Task LoadListAsync(IFlowLiveSource source, bool append)
    {
        if (Session.LiveScopeProcessId is not { } processId)
        {
            return;
        }

        var key = WantedListKey;
        var generation = ++listGeneration;
        var token = Renew(ref listCts);
        listLoading = true;
        listFailed = false;
        if (!append)
        {
            listKey = key;
            listCases = [];
            listNext = null;
        }

        try
        {
            var wanted = StageSteps(processId);

            // A scope of one step is asked of the source itself, so that a page is not full of cases on other steps.
            var onlyStep = wanted is { Count: 1 } ? wanted.Single() : null;
            var found = new List<FlowLiveCaseLine>();
            var after = append ? listNext : null;
            var next = after;
            for (var pages = 0; pages < LiveMaxPagesPerClick && found.Count < LivePageSize; pages++)
            {
                var page = await source.ListCasesAsync(new FlowLiveCaseQuery(processId, onlyStep, LivePageSize, next), token).ConfigureAwait(true);
                if (generation != listGeneration)
                {
                    return;
                }

                found.AddRange(wanted is null ? page.Cases : page.Cases.Where(line => line.Step is not null && wanted.Contains(line.Step)));
                next = page.Next;
                if (next is null)
                {
                    break;
                }
            }

            listCases = append ? [.. listCases, .. found] : found;
            listNext = next;
        }
        catch (OperationCanceledException)
        {
            return;
        }
#pragma warning disable CA1031 // The source belongs to the host: whatever it throws, the screen says so in words.
        catch (Exception) when (generation == listGeneration)
        {
            listFailed = true;
        }
#pragma warning restore CA1031
        finally
        {
            if (generation == listGeneration)
            {
                listLoading = false;
            }
        }
    }

    private async Task LoadCaseAsync(IFlowLiveSource source, string caseRef)
    {
        var generation = ++caseGeneration;
        var token = Renew(ref caseCts);
        caseKey = caseRef;
        caseLoading = true;
        caseFailed = false;
        caseGone = false;
        caseData = null;
        casePath = null;
        try
        {
            var live = await source.GetCaseAsync(caseRef, token).ConfigureAwait(true);
            if (generation != caseGeneration)
            {
                return;
            }

            caseData = live;
            caseGone = live is null;
            casePath = null;
        }
        catch (OperationCanceledException)
        {
            // A newer read replaced this one, or the component went away: the newer read says what is true.
        }
#pragma warning disable CA1031 // The source belongs to the host: whatever it throws, the screen says so in words.
        catch (Exception) when (generation == caseGeneration)
        {
            caseFailed = true;
        }
#pragma warning restore CA1031
        finally
        {
            if (generation == caseGeneration)
            {
                caseLoading = false;
            }
        }
    }

    private void DisposeLive() => CancelLive();

    /// <summary>The ids of the steps of the phase the list is narrowed to (by the highest version); null for the whole process. An empty scope is the steps with no stage.</summary>
    private HashSet<string>? StageSteps(string processId)
    {
        if (Session.LiveScopeStageId is not { } scope)
        {
            return null;
        }

        var stage = scope.Length == 0 ? null : scope;
        var process = FlowWithId(processId)?.Process;
        if (process is null)
        {
            return [];
        }

        return [.. ProcessOutline.From(process).Steps.Where(step => string.Equals(step.Stage, stage, StringComparison.Ordinal)).Select(step => step.Id)];
    }

    // ----- what the reader chooses -------------------------------------------------------------------------------------

    private async Task ChooseLiveViewAsync()
    {
        Session.View = FlowWorkbenchView.Live;
        await LiveSyncAsync(force: false).ConfigureAwait(true);
    }

    private async Task OpenCasesAsync(string processId, string? stageId, bool phase)
    {
        Session.LiveCaseRef = null;
        Session.LiveScopeProcessId = processId;
        Session.LiveScopeStageId = phase ? stageId ?? string.Empty : null;
        await LiveSyncAsync(force: false).ConfigureAwait(true);
    }

    private void CloseCases()
    {
        Session.LiveCaseRef = null;
        Session.LiveScopeProcessId = null;
        Session.LiveScopeStageId = null;
    }

    private async Task OpenCaseAsync(string caseRef)
    {
        Session.LiveCaseRef = caseRef;
        if (OnOpenCase.HasDelegate)
        {
            await OnOpenCase.InvokeAsync(caseRef).ConfigureAwait(true);
        }

        await LiveSyncAsync(force: false).ConfigureAwait(true);
    }

    private void CloseCase() => Session.LiveCaseRef = null;

    private async Task MoreCasesAsync()
    {
        if (LiveSource is { } source)
        {
            await LoadListAsync(source, append: true).ConfigureAwait(true);
        }
    }

    private async Task RetryAsync()
    {
        await LiveSyncAsync(force: true).ConfigureAwait(true);
    }

    // ----- the facts the markup draws ----------------------------------------------------------------------------------

    private string LiveCountsLine(LiveCount count)
    {
        if (count.IsZero)
        {
            return Plain("flow.cases.count.none");
        }

        var open = PluralText("flow.cases.count.open", count.Open);
        return count.Waiting > 0 ? open + ", " + PluralText("flow.cases.count.waiting", count.Waiting) : open;
    }

    /// <summary>The document a case is drawn over: the exact version it runs on, else the highest version of its process; null when the process is not loaded.</summary>
    private WorkbenchFlow? CaseFlow(FlowLiveCase live)
    {
        var all = (Flows ?? []).Where(flow => flow.IsReadable && string.Equals(flow.Process!.Id, live.ProcessId, StringComparison.Ordinal)).ToList();
        return all.FirstOrDefault(flow => flow.Process!.Version == live.ProcessVersion) ?? all.OrderByDescending(flow => flow.Process!.Version).FirstOrDefault();
    }

    private LiveCasePath? CasePath
    {
        get
        {
            if (casePath is null && caseData is not null && CaseFlow(caseData) is { } flow)
            {
                casePath = LiveCasePath.From(flow.Process!, caseData, Locale, known);
            }

            return casePath;
        }
    }

    /// <summary>The step of a case line in words, as the highest version of its process words it; the id as code for a step the document does not have.</summary>
    private string LineStep(string processId, string step) =>
        FlowWithId(processId) is { } flow && Data(flow) is { } d && d.Outline.Step(step) is not null ? d.Words.Step(step) : step;

    private string LineFacts(FlowLiveCaseLine line, string processId)
    {
        var parts = new List<string>
        {
            line.Step is null ? Plain("flow.cases.line.notStarted") : Plain("flow.cases.line.on", ("step", LineStep(processId, line.Step))),
        };
        if (line.WaitsForPerson)
        {
            parts.Add(Plain("flow.cases.line.waiting"));
        }

        parts.Add(Plain("flow.cases.line.since", ("time", TimeText(line.Since))));
        if (FlowWithId(processId)?.Process is { } latest && latest.Version != line.ProcessVersion)
        {
            parts.Add(Plain("flow.cases.line.version", ("version", Number(line.ProcessVersion))));
        }

        return string.Join(", ", parts);
    }
}

using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Blazor;

// The process map and the view bar: the helpers the markup of FlowWorkbench.razor draws the map with.
public sealed partial class FlowWorkbench
{
    /// <summary>The map of the readable processes: computed once for the processes, the language and the host's labels.</summary>
    private ProcessMap Map => processMap ??= ProcessMap.From(known, Locale, MapLabels, ReportOf);

    /// <summary>True when the map is drawn: the map view is chosen and at least one process can be read (otherwise the list says what is wrong).</summary>
    private bool ShowMap => Session.View == FlowWorkbenchView.Map && Flows is { Count: > 0 } && known.Count > 0;

    private bool ViewBarShown => Flows is { Count: > 0 } && known.Count > 0;

    private void ChooseView(FlowWorkbenchView view) => Session.View = view;

    private static string KindIcon(FlowKind kind) => kind switch
    {
        FlowKind.Main => "main",
        FlowKind.Subprocess => "sub",
        _ => "sch",
    };

    private static string KindTone(FlowKind kind) => kind switch
    {
        FlowKind.Main => "wb-k-main",
        FlowKind.Subprocess => "wb-k-sub",
        _ => "wb-k-sch",
    };

    private static string PieceIcon(MapPieceKind kind) => kind switch
    {
        MapPieceKind.Rule => "rule",
        MapPieceKind.Connector => "con",
        MapPieceKind.PersonTask => "per",
        _ => "ai",
    };

    private static string PieceTone(MapPieceKind kind) => kind switch
    {
        MapPieceKind.Rule => "wb-k-rule",
        MapPieceKind.Connector => "wb-k-con",
        MapPieceKind.PersonTask => "wb-k-per",
        _ => "wb-k-ai",
    };

    private string KindWord(FlowKind kind) => FlowKinds.Word(kind, Locale);

    private string KindsWord(FlowKind kind) => FlowKinds.Plural(kind, Locale);

    private string PieceWord(MapPieceKind kind) => kind switch
    {
        MapPieceKind.Rule => Plain("flow.mapkey.rule"),
        MapPieceKind.Connector => Plain("flow.mapkey.connector"),
        MapPieceKind.PersonTask => Plain("flow.mapkey.person"),
        _ => Plain("flow.mapkey.ai"),
    };

    /// <summary>The legend of the map: one entry for each kind of piece, with its icon, its colour class, its word and what it is.</summary>
    private IEnumerable<(string Tone, string Icon, string Word, string Description)> MapKey()
    {
        yield return (KindTone(FlowKind.Main), KindIcon(FlowKind.Main), KindWord(FlowKind.Main), Plain("flow.mapkey.main.d"));
        yield return (KindTone(FlowKind.Subprocess), KindIcon(FlowKind.Subprocess), KindWord(FlowKind.Subprocess), Plain("flow.mapkey.sub.d"));
        yield return (KindTone(FlowKind.Scheduled), KindIcon(FlowKind.Scheduled), KindWord(FlowKind.Scheduled), Plain("flow.mapkey.scheduled.d"));
        yield return ("wb-k-ev", "ev", Plain("flow.mapkey.event"), Plain("flow.mapkey.event.d"));
        yield return (PieceTone(MapPieceKind.Rule), PieceIcon(MapPieceKind.Rule), PieceWord(MapPieceKind.Rule), Plain("flow.mapkey.rule.d"));
        yield return (PieceTone(MapPieceKind.Connector), PieceIcon(MapPieceKind.Connector), PieceWord(MapPieceKind.Connector), Plain("flow.mapkey.connector.d"));
        yield return (PieceTone(MapPieceKind.PersonTask), PieceIcon(MapPieceKind.PersonTask), PieceWord(MapPieceKind.PersonTask), Plain("flow.mapkey.person.d"));
        yield return (PieceTone(MapPieceKind.Ai), PieceIcon(MapPieceKind.Ai), PieceWord(MapPieceKind.Ai), Plain("flow.mapkey.ai.d"));
    }

    /// <summary>The counts of the map in one line: main processes, subprocesses, scheduled tasks, rules.</summary>
    private string MapCountsLine() => string.Join(
        " · ",
        PluralText("flow.map.count.main", Map.Mains.Count),
        PluralText("flow.map.count.sub", Map.SubprocessCount),
        PluralText("flow.map.count.scheduled", Map.Scheduled.Count),
        PluralText("flow.map.count.rules", Map.RuleCount));

    /// <summary>What a card says about a process: steps, tests, routes no test takes.</summary>
    private string MapFacts(MapProcess process, WorkbenchFlow? flow)
    {
        var parts = new List<string> { PluralText("flow.list.steps", process.Counts.Steps) };
        if (process.Counts.Scenarios == 0)
        {
            parts.Add(Plain("flow.list.noTests"));
        }
        else if (process.Counts.ScenariosPass)
        {
            parts.Add(PluralText("flow.map.tests.pass", process.Counts.Scenarios));
        }
        else
        {
            var failed = flow is not null && Data(flow) is { } d ? d.Scenarios.Count(line => !line.Passed) : 1;
            parts.Add(process.Counts.Scenarios == 1 ? Plain("flow.list.testFails") : PluralText("flow.list.testsFail", failed, ("t", Number(process.Counts.Scenarios))));
        }

        if (process.Counts.RoutesNotTried > 0)
        {
            parts.Add(PluralText("flow.map.routes", process.Counts.RoutesNotTried));
        }

        return string.Join(", ", parts);
    }

    /// <summary>The full name of a card, as a screen reader says it: the kind, the title, the facts.</summary>
    private string CardName(MapProcess process, WorkbenchFlow? flow) => KindWord(process.Kind) + ": " + process.Title + ", " + MapFacts(process, flow);

    private bool IsOpen(string processId) =>
        Session.ProcessOpen && SelectedFlow is { Process: { } current } && string.Equals(current.Id, processId, StringComparison.Ordinal);

    /// <summary>Opens a process beside the map. <paramref name="fromId"/> is the process whose phase it was chosen from, for the breadcrumb.</summary>
    private void OpenProcess(string processId, string? fromId, bool diagram = false)
    {
        if (FlowWithId(processId) is not { } flow)
        {
            return;
        }

        ResetTransient();
        Session.SelectedFlowKey = flow.Key;
        Session.ProcessOpen = true;
        Session.OpenedFromId = fromId;
        if (diagram)
        {
            Session.SelectedTab = FlowWorkbenchTab.Diagram;
        }

        Session.FullDiagram = false;
    }

    private void CloseProcess() => Session.ProcessOpen = false;

    private void OpenFullDiagram()
    {
        Session.SelectedTab = FlowWorkbenchTab.Diagram;
        Session.FullDiagram = true;
    }

    private void CloseFullDiagram() => Session.FullDiagram = false;

    private void OpenRule(string ruleId)
    {
        if (OnOpenRule.HasDelegate)
        {
            _ = OnOpenRule.InvokeAsync(ruleId);
        }
    }

    /// <summary>The host's label for the authority a rule states; null when the rule states none or the host has no label for it.</summary>
    private string? AuthorityBadge(MapUse use)
    {
        if (use.Kind != MapPieceKind.Rule || RuleAuthorities is null || !RuleAuthorities.TryGetValue(use.Name, out var code) || AuthorityLabels is null)
        {
            return null;
        }

        return AuthorityLabels.TryGetValue(code, out var label) && !string.IsNullOrWhiteSpace(label) ? label : null;
    }

    /// <summary>The title of the process the open one was chosen from, when that is another readable process.</summary>
    private (string Id, string Title)? Parent(WorkbenchFlow open)
    {
        var from = Session.OpenedFromId;
        if (from is null || open.Process is null || string.Equals(from, open.Process.Id, StringComparison.Ordinal) || FlowWithId(from) is null)
        {
            return null;
        }

        return (from, TitleOfId(from));
    }
}

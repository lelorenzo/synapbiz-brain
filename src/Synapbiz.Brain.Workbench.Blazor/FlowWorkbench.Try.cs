using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Blazor;

public sealed partial class FlowWorkbench
{
    private const string StartRef = "start";
    private const string SaveNameRef = "save:name";
    private const string KeepResetRef = "reset:keep";
    private const string ZoneInvalid = "zone_invalid";
    private const string TimeCard = "time";

    private static readonly Dictionary<string, string> CodeKeys = new(StringComparer.Ordinal)
    {
        ["process.input.unknown"] = "flow.code.inputUnknown",
        ["process.variable.type_mismatch"] = "flow.code.typeMismatch",
        ["workbench.flow.json_invalid"] = "flow.code.jsonInvalid",
        ["workbench.flow.duration_invalid"] = "flow.code.durationInvalid",
        ["workbench.flow.not_started"] = "flow.code.notStarted",
        ["workbench.flow.answer_source_failed"] = "flow.code.sourceFailed",
        ["process.trigger.stale"] = "flow.code.stale",
        ["process.task.outcome_unknown"] = "flow.code.outcomeUnknown",
        ["process.event.not_accepted"] = "flow.code.eventNotAccepted",
        ["process.window.zone_unknown"] = "flow.code.zoneUnknown",
        ["process.effect.output_malformed"] = "flow.code.outputMalformed",
        ["process.ai.output_invalid"] = "flow.code.aiOutputInvalid",
        [ZoneInvalid] = "flow.code.zoneInvalid",
    };

    private static readonly (string Key, TimeSpan Span)[] Passages =
    [
        ("flow.try.pass.hour", TimeSpan.FromHours(1)),
        ("flow.try.pass.day", TimeSpan.FromDays(1)),
        ("flow.try.pass.week", TimeSpan.FromDays(7)),
    ];

    private readonly Dictionary<BrainProcess, (FlowWords Words, FlowView View)> frameInfo = [];

    private CancellationTokenSource? asking;
    private string? askingEffect;

    private static TryFrame? CurrentFrame(FlowWork work) => work.Frames.Count == 0 ? null : work.Frames[^1];

    private (FlowWords Words, FlowView View) InfoOf(FlowData d, TryFrame frame)
    {
        var process = frame.Simulation.Process;
        if (ReferenceEquals(process, d.Process))
        {
            return (d.Words, d.View);
        }

        if (!frameInfo.TryGetValue(process, out var info))
        {
            info = (FlowWords.For(process, Locale, known), FlowDescriber.Describe(process, Locale, known));
            frameInfo[process] = info;
        }

        return info;
    }

    /// <summary>The words of a code the flow gave, with the code in backticks when the panel has no sentence for it.</summary>
    private string Reason(string? code)
    {
        var key = code is not null && CodeKeys.TryGetValue(code, out var found) ? found : "flow.code.other";
        return Plain(key, ("code", code ?? string.Empty));
    }

    private static string WithoutCode(string sentence) => sentence.Replace("`", string.Empty, StringComparison.Ordinal);

    // ----- starting ---------------------------------------------------------------------------------------------------

    private string StartAtText(FlowWork work)
    {
        if (work.StartAt.Length == 0)
        {
            var now = Now().UtcDateTime;
            work.StartAt = FlowForms.DateTimeText(new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero));
        }

        return work.StartAt;
    }

    private void SetStartValue(FlowWork work, string name, ChangeEventArgs args)
    {
        work.StartValues[name] = args.Value?.ToString() ?? string.Empty;
        work.StartLike = string.Empty;
        Session.Touch();
    }

    private void SetStartAt(FlowWork work, ChangeEventArgs args)
    {
        work.StartAt = args.Value?.ToString() ?? string.Empty;
        work.StartLike = string.Empty;
        Session.Touch();
    }

    private void StartLike(FlowData d, FlowWork work, ChangeEventArgs args)
    {
        var name = args.Value?.ToString() ?? string.Empty;
        work.StartLike = name;
        work.StartValues.Clear();
        work.StartInvalid.Clear();
        work.StartRefusal = null;
        if (name.Length > 0 && FlowForms.FromScenario(d.Process, name) is { } values)
        {
            foreach (var (field, value) in values.Values)
            {
                work.StartValues[field] = value;
            }

            work.StartAt = FlowForms.DateTimeText(values.At);
        }

        Session.Touch();
    }

    private void StartTryOut(FlowData d, FlowWork work)
    {
        var form = FlowForms.ToJson(FlowForms.StartFields(d.Process), work.StartValues);
        var at = FlowForms.ParseInstant(StartAtText(work));
        work.StartInvalid.Clear();
        work.StartInvalid.AddRange(form.Invalid);
        if (at is null)
        {
            work.StartInvalid.Add(StartRef);
        }

        work.StartRefusal = null;
        if (work.StartInvalid.Count > 0)
        {
            pendingFocus = StartFieldRef(work.StartInvalid[0]);
            Session.Touch();
            return;
        }

        FlowSimulation simulation;
        try
        {
            simulation = FlowSimulation.Start(d.Process, new FlowSimulationSetup
            {
                At = at!.Value,
                Today = DateOnly.FromDateTime(at.Value.UtcDateTime),
                InputJson = form.Json,
                Zones = Zones ?? new Dictionary<string, TimeSpan>(),
                AnswerSource = AnswerSource,
                ChildProcesses = known,
            });
        }
        catch (ArgumentException)
        {
            work.StartRefusal = ZoneInvalid;
            Session.Touch();
            return;
        }

        if (!simulation.Started)
        {
            work.StartRefusal = simulation.StartCode ?? string.Empty;
            Session.Touch();
            return;
        }

        work.Frames.Clear();
        work.ReplayName = null;
        var frame = new TryFrame(simulation);
        work.Frames.Add(frame);
        work.Live = LiveText(d, frame, simulation.Log);
        work.SaveError = null;
        Session.Touch();
    }

    private static string StartFieldRef(string name) => "startfield:" + name;

    // ----- commands ---------------------------------------------------------------------------------------------------

    /// <summary>Runs one command on the try-out, remembers whether the flow refused it and says what happened in the live region.</summary>
    private void Do(FlowData d, FlowWork work, TryFrame frame, string card, Func<IReadOnlyList<FlowSimulationEntry>> command)
    {
        var entries = command();
        AfterCommand(d, work, frame, card, entries);
    }

    private void AfterCommand(FlowData d, FlowWork work, TryFrame frame, string card, IReadOnlyList<FlowSimulationEntry> entries)
    {
        var refused = entries.FirstOrDefault(entry => entry.Disposition != AdvanceDisposition.Applied);
        frame.Refusal = refused is null ? null : (card, refused.Code ?? string.Empty);
        work.Live = LiveText(d, frame, entries);
        work.ConfirmReset = false;
        Session.Touch();
    }

    private string LiveText(FlowData d, TryFrame frame, IReadOnlyList<FlowSimulationEntry> entries)
    {
        if (entries.FirstOrDefault(entry => entry.Disposition != AdvanceDisposition.Applied) is { } refused)
        {
            return Plain("flow.live.refused", ("reason", WithoutCode(Reason(refused.Code))));
        }

        var words = InfoOf(d, frame).Words;
        var simulation = frame.Simulation;
        return simulation.Status switch
        {
            ProcessStatus.Completed => Plain("flow.live.ended", ("result", words.Result(simulation.State.Result ?? string.Empty))),
            ProcessStatus.Cancelled => Plain("flow.live.cancelled"),
            _ => simulation.CurrentStep is { } step ? Plain("flow.live.at", ("step", words.Step(step))) : string.Empty,
        };
    }

    private static string AnswerKey(string effectId) => "answer:" + effectId;

    private static string FieldKey(string card, string field) => card + ":" + field;

    private void SetInput(TryFrame frame, string key, ChangeEventArgs args)
    {
        frame.Inputs[key] = args.Value?.ToString() ?? string.Empty;
        Session.Touch();
    }

    private static string InputOf(TryFrame frame, string key) => frame.Inputs.TryGetValue(key, out var value) ? value : string.Empty;

    private void AnswerFromTest(FlowData d, FlowWork work, TryFrame frame, OutstandingEffect effect, FlowAnswer answer) =>
        Do(d, work, frame, effect.Id, () => frame.Simulation.ApplyAnswer(effect.Id, answer, FlowSimulation.ScriptedSource));

    private void AnswerByHand(FlowData d, FlowWork work, TryFrame frame, OutstandingEffect effect)
    {
        var typed = InputOf(frame, AnswerKey(effect.Id)).Trim();
        Do(d, work, frame, effect.Id, () => frame.Simulation.CompleteEffect(effect.Id, typed.Length == 0 ? "{}" : typed));
    }

    private void FailEffect(FlowData d, FlowWork work, TryFrame frame, OutstandingEffect effect, bool permanent) =>
        Do(d, work, frame, effect.Id, () => frame.Simulation.FailEffect(effect.Id, "tryout.failed", permanent));

    private async Task AskAsync(FlowData d, FlowWork work, TryFrame frame, OutstandingEffect effect)
    {
        if (AnswerSource is null || asking is not null)
        {
            return;
        }

        var source = AnswerSource.Name;
        var cancellation = new CancellationTokenSource();
        asking = cancellation;
        askingEffect = effect.Id;
        work.Live = Plain("flow.live.asking", ("source", source));
        Session.Touch();
        try
        {
            var entries = await frame.Simulation.RunAnswersAsync(1, cancellation.Token).ConfigureAwait(true);
            AfterCommand(d, work, frame, effect.Id, entries);
            if (entries.Count == 0)
            {
                work.Live = Plain("flow.live.noAnswer", ("source", source));
            }
        }
        catch (OperationCanceledException)
        {
            frame.Refusal = null;
            work.Live = Plain("flow.live.askCancelled", ("source", source));
        }
        finally
        {
            if (ReferenceEquals(asking, cancellation))
            {
                asking = null;
                askingEffect = null;
            }

            cancellation.Dispose();
        }

        Session.Touch();
    }

    private void CancelAsk() => asking?.Cancel();

    private void CompleteTask(FlowData d, FlowWork work, TryFrame frame, OutstandingTask task, string outcome) =>
        Do(d, work, frame, task.Id, () => frame.Simulation.CompleteTask(task.Id, outcome));

    private void SendEvent(FlowData d, FlowWork work, TryFrame frame, OutstandingWait wait)
    {
        var fields = FlowForms.PayloadFields(frame.Simulation.Process, wait.Step);
        var values = fields.ToDictionary(field => field.Name, field => InputOf(frame, FieldKey(wait.Id, field.Name)), StringComparer.Ordinal);
        var payload = FlowForms.ToJson(fields, values);
        if (payload.Invalid.Count > 0)
        {
            frame.Invalid[wait.Id] = [.. payload.Invalid];
            pendingFocus = FieldRefOf(wait.Id, payload.Invalid[0]);
            Session.Touch();
            return;
        }

        frame.Invalid.Remove(wait.Id);
        Do(d, work, frame, wait.Id, () => frame.Simulation.SendEvent(wait.Event, wait.Correlation, payload.Json));
    }

    private static string FieldRefOf(string card, string field) => "field:" + card + ":" + field;

    private void GoToMoment(FlowData d, FlowWork work, TryFrame frame, string card, DateTimeOffset due) =>
        Do(d, work, frame, card, () => frame.Simulation.AdvanceTo(due));

    private void LetPass(FlowData d, FlowWork work, TryFrame frame, TimeSpan span) =>
        Do(d, work, frame, TimeCard, () => frame.Simulation.AdvanceBy(span));

    private static DateTimeOffset? NextMoment(TryFrame frame) =>
        frame.Simulation.Outstanding.Select(item => item switch
        {
            OutstandingTimer timer => (DateTimeOffset?)timer.Due,
            OutstandingWindow window => window.OpensAt,
            _ => null,
        }).Where(due => due is not null).Min();

    private void ResumeCase(FlowData d, FlowWork work, TryFrame frame) =>
        Do(d, work, frame, TimeCard, () => frame.Simulation.Resume());

    private void FinishChild(FlowData d, FlowWork work, TryFrame frame, OutstandingChild child)
    {
        var result = InputOf(frame, FieldKey(child.Id, "result")).Trim();
        if (result.Length == 0 && ChildOptions(child) is { Count: > 0 } options)
        {
            result = options[0].Id;
        }

        Do(d, work, frame, child.Id, () => frame.Simulation.CompleteChild(child.Id, result));
    }

    private void FailChild(FlowData d, FlowWork work, TryFrame frame, OutstandingChild child) =>
        Do(d, work, frame, child.Id, () => frame.Simulation.FailChild(child.Id, "tryout.child_failed"));

    private void OpenChild(FlowData d, FlowWork work, TryFrame frame, OutstandingChild child)
    {
        var opened = frame.Simulation.OpenChild(child.Id);
        if (opened is null)
        {
            frame.Refusal = (child.Id, "child_unknown");
            work.Live = Plain("flow.live.childUnknown");
            Session.Touch();
            return;
        }

        frame.Refusal = null;
        var childFrame = new TryFrame(opened, child.Id);
        work.Frames.Add(childFrame);
        work.Live = LiveText(d, childFrame, opened.Log);
        Session.Touch();
    }

    private void BackToParent(FlowData d, FlowWork work)
    {
        work.Frames.RemoveAt(work.Frames.Count - 1);
        work.Live = LiveText(d, work.Frames[^1], []);
        Session.Touch();
    }

    private void BringResultBack(FlowData d, FlowWork work)
    {
        var child = work.Frames[^1];
        var parent = work.Frames[^2];
        var result = child.Simulation.State.Result ?? string.Empty;
        var entries = parent.Simulation.CompleteChild(child.ParentChildKey!, result);
        work.Frames.RemoveAt(work.Frames.Count - 1);
        AfterCommand(d, work, parent, child.ParentChildKey!, entries);
    }

    // ----- keeping and starting over ----------------------------------------------------------------------------------

    private void SetSaveName(FlowWork work, ChangeEventArgs args)
    {
        work.SaveName = args.Value?.ToString() ?? string.Empty;
        work.SaveError = null;
        Session.Touch();
    }

    private void SaveAsProposed(FlowData d, FlowWork work)
    {
        var name = work.SaveName.Trim();
        var first = work.Frames[0];
        if (name.Length == 0)
        {
            work.SaveError = "flow.save.nameRequired";
        }
        else if (d.Outline.Scenarios.Contains(name, StringComparer.Ordinal) || work.Proposed.Any(item => item.Name == name))
        {
            work.SaveError = "flow.save.nameTaken";
        }
        else
        {
            try
            {
                work.Proposed.Add(new FlowProposedScenario(name, first.Simulation.ToScenarioJson(name)));
                work.SaveError = null;
                work.SaveName = string.Empty;
                exportDone = false;
                work.Live = Plain("flow.live.saved", ("name", name));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                work.SaveError = "flow.save.invalid";
            }
        }

        if (work.SaveError is not null)
        {
            pendingFocus = SaveNameRef;
        }

        Session.Touch();
    }

    private void RemoveProposed(FlowWork work, string name)
    {
        work.Proposed.RemoveAll(item => item.Name == name);
        exportDone = false;
        Session.Touch();
    }

    private void AskReset(FlowWork work)
    {
        work.ConfirmReset = true;
        pendingFocus = KeepResetRef;
        Session.Touch();
    }

    private void KeepTryOut(FlowWork work)
    {
        work.ConfirmReset = false;
        Session.Touch();
    }

    private void StartOver(FlowWork work)
    {
        asking?.Cancel();
        work.Frames.Clear();
        work.ReplayName = null;
        work.ConfirmReset = false;
        work.Live = string.Empty;
        work.SaveError = null;
        pendingFocus = StartRef;
        Session.Touch();
    }

    private void Replay(FlowData d, FlowWork work, string scenario)
    {
        asking?.Cancel();
        var simulation = FlowSimulation.Replay(d.Process, scenario);
        work.Frames.Clear();
        var frame = new TryFrame(simulation);
        work.Frames.Add(frame);
        work.ReplayName = scenario;
        work.ConfirmReset = false;
        work.Live = Plain("flow.live.replayed", ("name", scenario));
        Session.SelectedTab = FlowWorkbenchTab.Try;
    }

    // ----- what the screen shows of a try-out -------------------------------------------------------------------------

    /// <summary>How many answers the frame has already given to a step, which tells the scenarios' <c>n</c>-th answer to offer.</summary>
    private static int AnsweredCount(TryFrame frame, string step) =>
        frame.Simulation.Log.Count(entry => entry.Step == step
            && entry.Kind is FlowSimulationEntryKind.EffectAnswered or FlowSimulationEntryKind.EffectFailed
            && entry.Disposition == AdvanceDisposition.Applied);

    private string StatusWord(ProcessStatus status) => status switch
    {
        ProcessStatus.Running => Plain("flow.status.running"),
        ProcessStatus.Waiting => Plain("flow.status.waiting"),
        ProcessStatus.Suspended => Plain("flow.status.suspended"),
        ProcessStatus.Completed => Plain("flow.status.completed"),
        _ => Plain("flow.status.cancelled"),
    };

    private string WhoWord(string source) => source switch
    {
        FlowSimulation.ManualSource => Plain("flow.who.manual"),
        FlowSimulation.ScriptedSource => Plain("flow.who.test"),
        _ => Plain("flow.who.source", ("name", source)),
    };

    private string EntryText(FlowSimulationEntry entry, FlowWords words)
    {
        var step = entry.Step is null ? string.Empty : words.Step(entry.Step);
        var key = entry.Kind switch
        {
            FlowSimulationEntryKind.Started => "flow.log.started",
            FlowSimulationEntryKind.EffectAnswered => "flow.log.effectAnswered",
            FlowSimulationEntryKind.EffectFailed => "flow.log.effectFailed",
            FlowSimulationEntryKind.TaskCompleted => "flow.log.taskCompleted",
            FlowSimulationEntryKind.EventSent => "flow.log.eventSent",
            FlowSimulationEntryKind.ClockAdvanced => "flow.log.clockAdvanced",
            FlowSimulationEntryKind.TimerFired => "flow.log.timerFired",
            FlowSimulationEntryKind.WindowOpened => "flow.log.windowOpened",
            FlowSimulationEntryKind.WindowFailed => "flow.log.windowFailed",
            FlowSimulationEntryKind.ChildFinished => "flow.log.childFinished",
            FlowSimulationEntryKind.ChildFailed => "flow.log.childFailed",
            FlowSimulationEntryKind.Cancelled => "flow.log.cancelled",
            FlowSimulationEntryKind.Resumed => "flow.log.resumed",
            _ => "flow.log.answerSourceFailed",
        };
        return Plain(key, ("step", step));
    }

    private string Entered(FlowSimulationEntry entry, FlowWords words)
    {
        var steps = entry.History.Where(item => item.Kind == ProcessHistoryKind.StepEntered && item.Step is not null).Select(item => words.Step(item.Step!)).ToList();
        var parts = new List<string>();
        if (steps.Count > 0)
        {
            parts.Add(Plain("flow.log.then", ("steps", string.Join(Plain("flow.join"), steps))));
        }

        if (entry.History.Any(item => item.Kind == ProcessHistoryKind.Completed))
        {
            parts.Add(Plain("flow.log.ended"));
        }

        return string.Join(' ', parts);
    }

    /// <summary>The values a task shows to the person, as label and text pairs.</summary>
    private static List<(string Name, string Value)> ShownValues(string showJson)
    {
        var shown = new List<(string, string)>();
        try
        {
            using var document = JsonDocument.Parse(showJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    shown.Add((property.Name, property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.GetRawText()));
                }
            }
        }
        catch (JsonException)
        {
            // What the person sees is not an object: nothing to list.
        }

        return shown;
    }

    private static readonly string[] SchemaTypes = ["string", "integer", "number", "boolean", "array", "object", "any"];

    private string TypeWord(string type) => SchemaTypes.Contains(type, StringComparer.Ordinal) ? Plain("flow.schema.type." + type) : type;
}

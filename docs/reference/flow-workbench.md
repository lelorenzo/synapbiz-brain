# Flow workbench

The flow workbench is the part of `Synapbiz.Brain.Workbench` that lets a person who owns a business flow read it in plain language, see it as a diagram, try it with a virtual clock, see which routes the tests cover, and send back a review. It is the headless half of the flows surface: it has no screen, reads no file, opens no connection and keeps no state. Everything it reports comes from `Synapbiz.Brain.Process` (the loader, the interpreter and the scenario format); the screen over it is the component `FlowWorkbench` of [flow-workbench-panels](flow-workbench-panels.md).

Namespace `Synapbiz.Brain.Workbench.Flows`. The workbench now references `Synapbiz.Brain.Contracts`, `.Engine`, `.Process` and `.Spec`, and nothing else. Every sentence, label and message a person reads exists in English and Spanish (English is the default); `WorkbenchLocale` chooses. Every message the workbench produces, problems and findings included, exists in both languages and follows the locale asked for. Every call is deterministic: the same document, the same locale and the same inputs give the same text, byte for byte.

Where the text of a view names a document member, it writes the id in backticks, so a screen can set it in a different type face without parsing the sentence.

The screen that draws all of this is the component `FlowWorkbench`, described in [flow-workbench-panels](flow-workbench-panels.md).

## The outline: what the workbench reads

`ProcessOutline` (in `Synapbiz.Brain.Process`, see [brain-process-v1](brain-process-v1.md#the-outline-a-read-model-of-a-loaded-process)) is the public read model of a loaded process: header, variables, windows, triggers, and every step with its routes. The workbench is built on it and on nothing from the internal model.

## Library: `FlowLibrary`

`FlowLibrary.From(processes, locale)` returns one `FlowEntry` per document of the set, ordered by id, then by version, then by content hash.

| `FlowEntry` member | Meaning |
| --- | --- |
| `Id`, `Version`, `ContentHash` | The identity of the document. |
| `Title`, `Subject`, `StageLabels` | The title (display label, else English, else a readable form of the id), the subject and the stage labels. |
| `Triggers` | One sentence per trigger: "Starts when `case.opened` arrives, one case for each `case_ref`.", "Runs every day at 06:00 America/New_York". |
| `StepCounts` | The number of steps of each kind. |
| `ScenarioCount`, `ScenariosPass` | The embedded scenarios and whether they all pass. |
| `CoveredRoutes`, `TotalRoutes` | Route coverage (see below). |
| `Children` | The child processes the flow starts, as `FlowChildLink` (id, title, `IsMissing`). A child that is not in the given set is listed as missing. |
| `StartedBy` | The ids of the documents of the set that start this flow as a child. |
| `Rules` | The rule references the flow uses. |

## Description: `FlowDescriber` and `FlowView`

`FlowDescriber.Describe(process, locale)` returns a `FlowView`: id, version, title, start step, the stages (`FlowStageView`) and one `FlowStepView` per step: id, label, `Kind` and `KindName` in words, stage and stage label, one `Sentence` saying what the step does, and its `Routes`. A `FlowRouteView` has the route label (`next`, `onError`, `on:<value>`, `interrupt`...), a `Phrase` ("If no answer arrives 2 hours before `visit_at`: go to Call the patient"), the target step id and the label of the target. Sentences are built from templates per kind and per fact, never from free text of the document; labels come from `meta.display`, then English, then a readable form of the id.

An `ai` step also carries a `FlowAiView` in `Ai`: the pinned `Model`, `PromptId` and `PromptVersion`; `MaxAttempts`; the names that go in (`InputNames`) and the canonical JSON of the input and output schemas (`InputSchemaJson`, `OutputSchemaJson`); the variables the answer fills (`OutputNames`); the rule that judges the answer when the next step is a `decide` (`JudgingRule`); where the flow goes when the answer does not fit the schema, when the port fails and when the connection is not available or the plan does not include it (`OnInvalid`, `OnError`, `OnNotAvailable`, `OnNotEntitled`); and `Notes`, plain sentences a reviewer must read, including that the flow does not stop when the AI is unavailable.

`FlowDescriber.DescribeTrigger(trigger, locale)` writes one sentence for a trigger, and `FlowDescriber.KindName(kind, locale)` the name of a kind of step in words.

## Diagram data: `FlowGraph`

`FlowGraph.From(process, locale)` returns `FlowNode`s (step id, label, kind, stage, `StageIndex`, `Depth`, `IsStart`, `IsEnd`) and `FlowEdge`s (from, to, label, phrase, `IsHappyPath`, `IsBackEdge`). `Depth` is the shortest distance from the start step, so a screen can lay out the diagram without a layout engine. `IsHappyPath` is true for `next`, `body` and `on:` routes and false for the error, timeout and `interrupt` routes. `IsBackEdge` is true when the edge closes a cycle: its target can reach its source by following routes, a route from a step to itself included. An edge that only leads to a node of the same or a lower depth, such as an error exit to an end node, is not one. `FlowGraph.From` and `FlowDescriber.Describe` take an optional `knownProcesses` (other documents the caller knows) to word the result of a child process by the child's own labels; with null the result is written as it is in the document.

## Simulation: `FlowSimulation`

`FlowSimulation.Start(process, setup)` starts a case on a virtual clock and drives the interpreter, the same one `BrainProcess.Advance` is. `FlowSimulationSetup` has `At` (the instant), `Today` (the date), `InputJson`, `Zones` (zone id to a fixed UTC offset, as in an embedded scenario), an optional `AnswerSource` and `ChildProcesses` for subprocess steps. `Start` throws `ArgumentException` for input that is not a JSON object or a zone with no id or with an offset of more than 14 hours or not in whole minutes. `At` is truncated to whole seconds, because the scenario format holds nothing finer; a session opened by replay or by `OpenChild` keeps its instant, and `AdvanceTo` rounds the span up to the next whole second so that it still moves. When the interpreter rejects the first entry (an input of the wrong type, for example), `Start` still returns the session: `Started` is false, `StartCode` holds the interpreter's code, and every command is refused with `workbench.flow.not_started` (`FlowSimulation.ErrorNotStarted`).

What a session shows:

| Member | Meaning |
| --- | --- |
| `Process`, `Name` | The document, and the name of the session (the scenario name for a replay). |
| `Started`, `StartCode` | Whether the interpreter accepted the first entry, and the code it gave when it did not. |
| `State`, `Status`, `CurrentStep`, `Variables`, `Now` | The interpreter's state, its status, the step it is at, the variables as canonical JSON text, and the virtual time. |
| `History` | Every interpreter history entry so far. |
| `Log` | One `FlowSimulationEntry` per thing the session did (see below). |
| `AnswerSource` | The answer source of the setup. |
| `Outstanding` | What the case waits for now (see below). |

Commands, each returning the log entries it added:

| Command | Does |
| --- | --- |
| `CompleteEffect(effectId, outputJson)` | Answers a read, action or ai effect with an output object. |
| `FailEffect(effectId, code, isPermanent)`, `FailEffect(effectId, kind, code, retryAfter)` | Answers it with a failure of any `ProcessErrorKind`. |
| `CompleteTask(taskId, outcome, dataJson)` | A person completes a task. |
| `SendEvent(name, correlation, payloadJson)` | An event arrives. |
| `AdvanceBy(duration)`, `AdvanceTo(instant)` | Moves the clock; timers fire in due order, then id order, as the scenario player does. |
| `CompleteChild(childKey, result, outputJson)`, `FailChild(childKey, code)` | A child process ends or fails. |
| `OpenChild(childKey)` | Starts the child as a session of its own, at the present time, with the input the step sent; null when the key is not outstanding or `ChildProcesses` has no such document. |
| `Cancel()`, `Resume()` | Cancels the case; resumes a suspended one. |
| `RunAnswersAsync(maxAnswers, cancellationToken)` | Asks the answer source for every outstanding effect, applies each answer through the commands above, and repeats until nothing is answered or the limit is reached. |
| `ToScenarioJson(name)` | Writes the session as an embedded scenario. |
| `Replay(process, scenarioName)` (static) | Plays an embedded scenario command by command and returns the session. |

A command the interpreter refuses (an unknown id, a stale trigger, a window that is closed) changes nothing and appears in the log with the interpreter's code. A payload that is not a JSON object is refused with `workbench.flow.json_invalid` (`FlowSimulation.ErrorJsonInvalid`); a duration that is not positive, not a whole number of seconds or longer than 3660 days is refused with `workbench.flow.duration_invalid` (`FlowSimulation.ErrorDurationInvalid`), because the scenario format cannot hold it. A delivery window is answered the way the scenario player does: with the next opening in a zone of the setup, or a failure with `process.window.zone_unknown` when the zone is not in the setup.

`FlowSimulationEntry` has the `Sequence`, the virtual time `At`, the `Kind` (`FlowSimulationEntryKind`: `Started`, `EffectAnswered`, `EffectFailed`, `TaskCompleted`, `EventSent`, `ClockAdvanced`, `TimerFired`, `WindowOpened`, `WindowFailed`, `ChildFinished`, `ChildFailed`, `Cancelled`, `Resumed`, `AnswerSourceFailed`), the `Source` that gave the command (`FlowSimulation.ManualSource`, `FlowSimulation.ScriptedSource`, or the name of the answer source), the `Step`, the `Subject` (the id of the effect, task, wait, timer or child), the interpreter's `Disposition` and `Code`, and the `History` entries the command added.

`Outstanding` is a list of `FlowOutstanding` items, each a sealed record: `OutstandingEffect` (id, step, port, kind of step, input JSON, attempt), `OutstandingTask` (queue, outcomes, the JSON of what the person sees), `OutstandingWait` (event, correlation), `OutstandingTimer` (due), `OutstandingWindow` (when the window opens) and `OutstandingChild` (process id, input JSON).

### Answer sources

`IFlowAnswerSource.AnswerAsync(request, cancellationToken)` is asked for one effect, described by a `FlowEffectRequest` (process, step, effect id, port, kind of step, input JSON, attempt and, for an `ai` step, the pinned model, prompt id and version, and the output schema). It returns a `FlowAnswer` (`FlowAnswer.Output(json)`, or `FlowAnswer.Failure(code, isPermanent)` or `FlowAnswer.Failure(kind, code, retryAfter)`) or null to leave the effect for a manual answer. The answer enters as an ordinary effect result, so the interpreter checks the output schema of an `ai` step and the rule that judges it exactly as it does in production: a malformed answer is retried up to the step's attempts and then leaves through `onInvalid`.

Brain holds no model vendor, key or network client. A host that wants a real model in the loop writes an `IFlowAnswerSource` over its own account, and sends nothing outside its own boundary that it did not decide to send; the request carries the input the step built, which may contain what the flow's variables hold, so use synthetic cases to try a flow out.

`ScriptedAnswerSource` answers from lists, by port (`ForPort`) or by step (`ForStep`, used first). An exception from a source does not end the session: it becomes an `AnswerSourceFailed` entry with the code `workbench.flow.answer_source_failed` (`FlowSimulation.ErrorAnswerSourceFailed`) and the effect stays outstanding for a manual answer. Cancelling the token throws `OperationCanceledException` to the caller; the answers applied so far stay.

A source that answers the `ai` steps with the host's own account:

```csharp
/// <summary>The host's own AI account behind a port; the library holds no vendor, key or client.</summary>
internal interface IHostAi
{
    Task<string> CompleteAsync(string model, string promptId, long promptVersion, string inputJson, string outputSchemaJson, CancellationToken cancellationToken);
}

/// <summary>Answers the <c>ai</c> steps of a simulated case with the host's AI account and leaves every other effect to the person.</summary>
internal sealed class HostAiAnswerSource(IHostAi account) : IFlowAnswerSource
{
    public string Name => "HostAi";

    public async ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken)
    {
        if (request.StepKind != OutlineStepKind.Ai)
        {
            return null;
        }

        try
        {
            var text = await account.CompleteAsync(request.Model!, request.PromptId!, request.PromptVersion!.Value, request.InputJson, request.OutputSchemaJson!, cancellationToken);
            return FlowAnswer.Output(text);
        }
        catch (HttpRequestException)
        {
            return FlowAnswer.Failure(ProcessErrorKind.NotAvailable, "host.ai.unreachable");
        }
    }
}
```

Trying a flow out and saying what is left for a person:

```csharp
internal static class FlowTryOut
{
    /// <summary>Starts a case of a flow at the given time, lets the answer source answer, and says what is left for a person.</summary>
    internal static async Task<(FlowSimulation Session, IReadOnlyList<string> WaitingFor)> RunAsync(
        BrainProcess process,
        string inputJson,
        IFlowAnswerSource source,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var session = FlowSimulation.Start(process, new FlowSimulationSetup
        {
            At = now,
            Today = DateOnly.FromDateTime(now.UtcDateTime),
            InputJson = inputJson,
            AnswerSource = source,
            Zones = new Dictionary<string, TimeSpan> { ["America/New_York"] = TimeSpan.FromHours(-5) },
        });
        await session.RunAnswersAsync(cancellationToken: cancellationToken);

        var waiting = session.Outstanding.Select(item => item switch
        {
            OutstandingEffect effect => $"an answer from {effect.Port}",
            OutstandingTask task => $"a person in the queue {task.Queue}",
            OutstandingWait wait => $"the event {wait.Event}",
            OutstandingTimer timer => $"the time {timer.Due:u}",
            OutstandingWindow window => $"the window to open at {window.OpensAt:u}",
            OutstandingChild child => $"the process {child.ProcessId}",
            _ => item.Id,
        }).ToList();
        return (session, waiting);
    }
}
```

## Route coverage: `FlowCoverage`

`FlowCoverage.Of(process, extraRuns, locale)` plays every embedded scenario, reads from the history which route each run took out of each step, and adds the runs given (for example a session a reader made; `Name` names it). It returns a `FlowCoverageResult`: every route as a `FlowRouteCoverage` (step, label, target, times taken, scenario names), `NeverTaken`, `StepsNeverEntered`, the totals (`TotalRoutes`, `CoveredRoutes`, `TotalSteps`, `EnteredSteps`) and `Problems`. The text of a problem follows `locale` (English by default). A run of another process, or of another version or content of this one, is refused with `ArgumentException` (parameter `extraRuns`), and a null run with `ArgumentNullException`. A history the routes cannot explain is reported as `workbench.flow.route_unexplained` (`FlowCoverage.ErrorRouteUnexplained`) for that run and never thrown; the routes that could be read before it still count.

## Review document `brain.process-review@1`

The shape and the idea of `brain.rule-review@1`, for a flow: what a reader sends back. A `FlowReview` has the process id, version and content hash, the reviewer, when it was written (supplied by the caller), the language, `Items` (`ReviewItem`: target, `ReviewState`, note) and `ProposedScenarios` (`FlowProposedScenario`: name and the scenario object as canonical JSON, for example from `ToScenarioJson`). A target is `process`, `stage:<id>`, `step:<id>`, `route:<step>/<label>`, `trigger:<id>` (the id of a schedule, or the event name of an event trigger) or `scenario:<name>`.

```json
{
  "$spec": "brain.process-review@1",
  "process": { "id": "practice.daily_refresh", "version": 1, "contentHash": "..." },
  "reviewer": "Dr. Example",
  "createdAt": "2026-11-22T21:00:00+00:00",
  "language": "en",
  "items": [ { "target": "step:list_cases", "state": "needsFix", "note": "What if there are no cases?" } ],
  "proposedScenarios": [ { "name": "a try-out worth keeping", "start": { }, "events": [ ], "expect": { } } ]
}
```

`FlowReviewJson` writes and reads the document. `FlowReviewJson.Serialize(review)` writes canonical JSON (indented, members in a fixed order, the members of a proposed scenario in ordinal order, `createdAt` to whole seconds). `FlowReviewJson.Parse(json)` returns a `FlowReviewParseResult`: the review, or every defect ordered by path. `FlowReviewCheck` compares a review with the document it is about. `FlowReviewCheck.Against(review, process)` returns a `FlowReviewReport`: `Findings` (a content hash, id or version that is not the document's, a target the document does not have, a proposed scenario that is not valid for the document, with the validator's own code and path) and, for each proposed scenario, a `FlowProposedScenarioResult`: whether it is valid and, if so, whether it runs to its own `expect` (and what went wrong if not). A review never changes a document: applying a proposed scenario is the author's edit in the repository. The messages are in the language of the review.

Writing a review document and reading one that came back:

```csharp
internal static class FlowReviews
{
    /// <summary>The document a reviewer's answers become: one item per target and the scenario of a try-out they want to keep.</summary>
    internal static string Write(BrainProcess process, string reviewer, DateTimeOffset now, FlowSimulation keep)
    {
        var outline = ProcessOutline.From(process);
        var review = new FlowReview
        {
            ProcessId = outline.Id,
            ProcessVersion = outline.Version,
            ProcessContentHash = outline.ContentHash,
            Reviewer = reviewer,
            CreatedAt = now,
            Language = WorkbenchLocale.En,
            Items = [new ReviewItem("process", ReviewState.Confirmed, "The flow reads as the practice works.")],
            ProposedScenarios = [new FlowProposedScenario("a try-out worth keeping", keep.ToScenarioJson("a try-out worth keeping"))],
        };
        return FlowReviewJson.Serialize(review);
    }

    /// <summary>What an author learns from a review that came back: whether it is for this version, and which proposals pass.</summary>
    internal static IReadOnlyList<string> Read(string text, BrainProcess process)
    {
        var parsed = FlowReviewJson.Parse(text);
        if (parsed.Review is not { } review)
        {
            return [.. parsed.Errors.Select(error => error.Code + " at " + error.Path)];
        }

        var report = FlowReviewCheck.Against(review, process);
        return
        [
            .. report.Findings.Select(finding => finding.Code + " at " + finding.Path),
            .. report.Scenarios.Select(result => result.Name + (result.Passed == true ? ": passes" : ": does not pass")),
        ];
    }
}
```

## Support for a screen (added with the panels)

Five small pure additions let a screen draw a flow without holding any knowledge of flows itself. None changes what the library did before.

- `FlowWords` (made with `FlowWords.For(process, locale, knownProcesses)`) names what a document talks about in the reader's language: `Title()`, `Stage(id)`, `Step(id)`, `Result(id)`, `Queue(id)`, `Outcome(id)`, `Event(id)`, `Window(id)` and `ChildResult(processId, result)`. Each is the label of `meta.display` in the asked language, else English, else a readable form of the id, like every other text of the library. `ChildResult` is null when the child is not among the known documents or has no label for the result.
- `FlowForms` writes and reads the forms of a flow. `StartFields(process)` gives one `FlowFormField` (name, type name, `FlowFieldKind`) for each input variable; `PayloadFields(process, stepId)` one for each member a `wait` step keeps (or the `interruptBy` of an `action` keeps), typed by the variable it is kept in. `FlowFieldKind` is `Text`, `Number`, `Date`, `DateTime`, `YesNo` or `Lines` (a `Json` or `Money` variable: a JSON object or array, or one string for each non-empty line). `ToJson(fields, values)` turns the texts of the controls into the canonical JSON object the simulator takes and returns a `FlowFormResult`: the `Json` of the fields that were filled in and usable, and the names of the fields whose text could not be read as their type (`Invalid`). A field with no text is left out. `FromScenario(process, scenarioName)` gives the texts a start form would hold to start a case as an embedded scenario does, with its start instant, as `FlowStartValues`; an input the scenario writes as an empty text is the one thing a form cannot repeat, because an empty control means "not given". `DateTimeText(instant)` writes an instant as the text of a date-and-time control (UTC) and `ParseInstant(text)` reads one (`yyyy-MM-ddTHH:mm`, seconds optional, UTC; or an ISO instant with an offset; null when it is neither).
- `FlowScenarioAnswers` (`For(process, step, occurrence)`) gives, for every embedded scenario that answers the step at least `occurrence + 1` times, the answer it gives the `occurrence`-th time (`complete` or `fail` command, counting from 0) as a `FlowScenarioAnswer` (scenario name and `FlowAnswer`). A screen counts the answers already given to a step in the session's log to offer "answer as this test does".
- `ApplyAnswer(effectId, answer, source)` of `FlowSimulation` answers an outstanding effect with a `FlowAnswer` and writes `source` in the log entry (default `FlowSimulation.ScriptedSource`): the same command as `CompleteEffect` or `FailEffect`, with the source chosen by the caller.
- `FlowSchemaFields` (`From(schemaJson)`) lists the top-level members of an output schema as `FlowSchemaField` (name, JSON type, whether required) in ordinal order, so that a screen says what an `ai` step must answer in a list.

## Codes

| Code | Where | Meaning |
| --- | --- | --- |
| `workbench.flow.json_invalid` | `FlowSimulation` commands | A payload (task data, event payload) is not a JSON object, or the text is not JSON. |
| `workbench.flow.duration_invalid` | `AdvanceBy`, `AdvanceTo` | The duration is not positive, not a whole number of seconds, or longer than 3660 days. |
| `workbench.flow.answer_source_failed` | `RunAnswersAsync` | The answer source threw; the effect stays outstanding. |
| `workbench.flow.route_unexplained` | `FlowCoverage.Of` | The routes of the document cannot explain the history of a run (path: the name of the run). When the history of a decide does not show which route it took, the one route that leads to the step the case went to is taken; two routes to the same step are unexplained. |
| `workbench.flow.not_started` | `FlowSimulation` commands | The session has no case because the interpreter rejected the first entry; `StartCode` says why. |
| `workbench.flow_review.json_invalid` | `FlowReviewJson.Parse` | The text is not JSON, or repeats a member name (path is empty). |
| `workbench.flow_review.unknown_format` | `FlowReviewJson.Parse` | `$spec` names another format (path `/$spec`). |
| `workbench.flow_review.malformed` | `FlowReviewJson.Parse` | A member is missing, of the wrong kind or unreadable; the path is the JSON Pointer of the value. |
| `workbench.flow_review.stale` | `FlowReviewCheck.Against` | The review is for another version of the document (path `/process/contentHash`). |
| `workbench.flow_review.target_unknown` | `FlowReviewCheck.Against` | The document has nothing at a target (path `/items/<n>/target`). |
| `workbench.flow_review.scenario_not_object` | `FlowReviewCheck.Against` (`FlowReviewCheck.ErrorScenarioNotObject`) | The code of the validator error, in the result of a proposed scenario and in the message of its `scenario_invalid` finding, when the scenario is not a JSON object. |
| `workbench.flow_review.scenario_invalid` | `FlowReviewCheck.Against` | A proposed scenario is not valid for the document; the message carries the validator's code and path. |

Codes of the interpreter (`process.*`) reach the log unchanged in `FlowSimulationEntry.Code`.

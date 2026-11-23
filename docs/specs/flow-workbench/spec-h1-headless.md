# H1: the flows surface without a screen

Status: draft, 2026-11-23. Owner chose the order "G first, then H1, H2, H3" on 2026-11-23. Normative once in the repository as `docs/specs/flow-workbench/spec-h1-headless.md`. Source of the need: the design of the SzH autonomous flows (flows visible in the same workbench as the rules; simulate a flow with what the AI would really answer). Package version 0.5.0.

H1 gives a host everything a flows screen needs as plain data and pure functions: what flows exist, what each one does step by step in the reader's language, a simulator that walks one case through a flow on a virtual clock, which routes the embedded scenarios cover, and a review document a reader can send back. H2 draws it; H3 lays a live case over it. Nothing in H1 edits the structure of a flow.

## 1. Rules for the whole package

1. Nothing changes in the format `brain.process@1`, in the interpreter's behavior, in the state text or in the PostgreSQL host. Every existing test, conformance case and golden text stays as it is.
2. The code is pure: no clock, no file, no network, no random source, no environment. The purity scan covers it. Time comes from the caller.
3. Every text a person reads exists in English and Spanish, English by default (`WorkbenchLocale.En`). A test proves that every text key has both languages and that no Spanish text equals its English text unless listed as intentionally equal. Labels of a document come from `ProcessDisplay` (asked language, then `en`); when a document has no label, the surface shows a readable form of the id (`WorkbenchText.Humanize`).
4. Brain holds no model vendor, no key and no network client. An AI answer in a simulation comes from an interface the host implements (section 5.3).
5. No patient data: the fixtures and samples are synthetic.

## 2. Where the code lives

- `Synapbiz.Brain.Process` gains one public, language-free read model of a loaded document: `ProcessOutline` (section 3). It is the only change to that package and it only reads.
- `Synapbiz.Brain.Workbench` gains the namespace `Synapbiz.Brain.Workbench.Flows` and a reference to `Synapbiz.Brain.Process`. No new package: the feed, the release workflow and the consumers' access stay as they are. The architecture tests that pin the references of the workbench are updated to the four libraries, with no new external package beyond what the process library already brings.

## 3. `ProcessOutline` (in `Synapbiz.Brain.Process`)

`ProcessOutline.From(BrainProcess)` returns an immutable description:

- `Id`, `Version`, `ContentHash`, `Subject`, `Stages`, `Start` (step id), `Accepts`, `Uses` (rule references), `Requires`.
- `Variables`: name, type name, `IsInput`.
- `Windows`: name, zone or zone variable, days, from, to.
- `Triggers`: the existing `ProcessTriggerDeclaration` list.
- `Scenarios`: name of each embedded scenario, in order.
- `Steps`, in document order. Each `OutlineStep` has `Id`, `Kind` (public enum `OutlineStepKind`: Read, Action, Ai, Poll, Decide, HumanTask, Wait, Timer, Loop, Subprocess, End), `Stage`, and `Routes`: ordered `OutlineRoute(Label, Target)` where the label is the member name (`next`, `body`, `onError`, `onTimeout`, ...), `on:<value>` for an entry of `on`, and `interrupt` for the `to` of an `interruptBy`. Plus the facts of its kind, null when they do not apply: `Port`, `Delivery`, `IsContact`, `Window`, `Retry` (max attempts, backoff text), `Rule` (reference), `Queue`, `Outcomes`, `Event`, `Correlation`, `Timeout` (either a duration or variable plus offset), `Interrupt` (event, correlation, target), `Timer` (after, or until plus offset), `Loop` (max iterations, for-each variable), `Process` (the child process reference as written), `Result` (of an end), `InputNames` and `OutputNames` (the names a mapping reads and writes), and for an `ai` step `Model`, `PromptId`, `PromptVersion`, `MaxAttempts`, and whether it declares an input schema and an output schema (the schema texts themselves are available as canonical JSON).

The exact member list follows the internal model; the implementer maps every member a step can carry and a test fails when a model record gains a member the outline does not map (reflection over the model records against an explicit allow-list).

Tests: the outline of each of the six reference documents and of the fixtures is pinned as golden text; the set of `(step, label, target)` equals the edge set the validator walks (same enumerator, so a new kind of edge cannot be missed).

## 4. Library and step-by-step description (`Flows`)

### 4.1 `FlowLibrary`

`FlowLibrary.From(IEnumerable<BrainProcess>, WorkbenchLocale)` returns one `FlowEntry` per document, ordered by id, then version, then content hash: id, version, content hash, title, subject, stage labels, trigger summaries in words ("Starts when `case.opened` arrives, one case for each `case_ref`.", "Runs every day at 06:00 America/New_York."), counts of steps by kind, number of scenarios and whether they all pass, route coverage (section 6) as covered and total, the child processes it starts and the processes that start it (resolved inside the given set; a child that is not in the set is listed as missing), and the rules it uses.

### 4.2 `FlowDescriber`

`FlowDescriber.Describe(BrainProcess, WorkbenchLocale, knownProcesses)` (the last parameter is optional) returns a `FlowView`: title, stages with labels, and per step a `FlowStepView`: id, label, kind name in words, stage label, one sentence saying what the step does, and its routes each with a phrase and the label of the target ("If no answer arrives 2 hours before `visit_at`: go to Call the patient"). Sentences are built from templates per kind and per fact, never from free text in the document. An `ai` step view also carries what a reviewer must see: the pinned model and prompt (id and version), what goes in (input names and the input schema), what must come out (the output schema and the variables it fills), how many attempts it gets, and where the flow goes when the answer does not fit the schema (`onInvalid`), when the port fails (`onError`) and when the port is not available or not entitled, so the reader sees that the flow continues without the AI. When the next step is a `decide`, the view names that rule as the one that judges the answer.

`FlowGraph.From(BrainProcess, WorkbenchLocale)` returns nodes and edges for a diagram: node (step id, label, kind, stage, `IsStart`, `IsEnd`), edge (from, to, label key, phrase, `IsHappyPath` for `next`, `body` and `on:` entries; false for the `on...` members and `interrupt`), and a layering: for each node its stage index and its depth from the start by shortest path, so H2 can draw without a layout engine. Back edges are flagged: an edge is a back edge when its target can reach its source by following routes (a route from a step to itself included); an edge that only goes to a node of the same or lower depth, such as an error exit to an end node, is not one.

Tests: golden views in both languages for the six reference documents; template coverage (every kind and every optional fact appears in at least one fixture); no template leaves a placeholder unfilled.

## 5. Simulator

### 5.1 Session

`FlowSimulation.Start(BrainProcess, FlowSimulationSetup)` where the setup has `At` (instant, truncated to whole seconds), `Today` (date), `InputJson`, `Zones` (zone id to fixed UTC offset, as in an embedded scenario), an optional `IFlowAnswerSource` and an optional set of child documents for subprocess steps. The session exposes:

- `State` (the interpreter's `ProcessState`), `Now`, `Status`, `CurrentStep`, `Variables` as name to canonical JSON text.
- `History`: every interpreter history entry so far.
- `Log`: one `FlowSimulationEntry` per thing the session did (started, answered effect, completed task, sent event, advanced clock, fired timer, opened window, child finished, cancelled, resumed), with the virtual time, who answered (`Manual`, `Scripted`, or the name the answer source gives) and the interpreter's disposition and code.
- `Outstanding`: what the case waits for now, as typed items: effect (id, step, port, kind of step, input JSON, attempt), task (id, step, queue, outcomes, show JSON), wait (id, step, event, correlation), timer (id, step, due), window timer (id, step, opens at), child (key, step, process id, input JSON).

Commands, each returning the new `FlowSimulationEntry` items: `CompleteEffect(id, outputJson)`, `FailEffect(id, code, isPermanent)`, `CompleteTask(id, outcome, dataJson)`, `SendEvent(name, correlation, payloadJson)`, `AdvanceBy(duration)`, `AdvanceTo(instant)` (fires every due timer in due order, then id order, exactly like the embedded scenario player), `CompleteChild(key, result, outputJson)`, `FailChild(key, code)`, `Cancel()`, `Resume()`. A command the interpreter rejects changes nothing and returns one entry with the rejection code. A delivery window is answered by the session itself, the way the scenario player does: next opening in the listed zone, or `WindowFailed` for a zone the setup does not list.

The session is deterministic: the same setup and the same commands give the same state text, history and log. It never reads a real clock.

### 5.2 Scenarios in and out

- `FlowSimulation.Replay(BrainProcess, scenarioName)` plays an embedded scenario command by command and returns the session, so a screen can show a scenario step by step. A test proves that for every embedded scenario of every reference document and fixture the replay ends in the state, trace and variables the scenario expects, and that its history equals the history of the independent test player.
- `ToScenarioJson(name)` writes the session so far as an embedded scenario (start, zones, events, and an `expect` taken from the current status, result, trace and variables). A test proves the round trip: the written scenario passes `RunScenarios` when added to the document.

### 5.3 Answer sources

```csharp
public interface IFlowAnswerSource
{
    string Name { get; }
    ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken);
}
```

`FlowEffectRequest`: process id, step id, port, step kind, input JSON, attempt, and for an `ai` step the model, the prompt id and version and the output schema as JSON text. `FlowAnswer` is either an output JSON or a failure (code, permanent or not). Null means "I do not answer this one": it stays outstanding for a manual answer.

`RunAnswersAsync(maxAnswers = 50)` asks the source for every outstanding effect, applies each answer through the same commands, and repeats until nothing outstanding gets an answer or the limit is reached. An exception from the source becomes a log entry with the code `workbench.flow.answer_source_failed` and the effect stays outstanding; the session stays usable.

Built in: `ScriptedAnswerSource` (per port, a queue of answers; optional per step), which is also what `Replay` uses. The host supplies others, for example one that calls the practice's own AI account. Whatever the source, the interpreter applies the same schema check and the same judging rule, since the answer enters as an ordinary effect result; the log records the source's name.

Tests: a scripted source drives a reference flow end to end without a manual command; a source that returns malformed output for an `ai` step is retried up to the step's attempts and then leaves through `onInvalid`; a throwing source; the answer limit; cancellation.

## 6. Route coverage

`FlowCoverage.Of(BrainProcess, IEnumerable<FlowSimulation>? extraRuns = null)` plays every embedded scenario and reads, from the history, which route each run took out of each step (the logic of the test helper `RouteMap`, moved into the library and built on the outline). It returns per route `(step, label, target, times taken, scenario names)`, the routes never taken, the steps never entered and the totals. A history the routes cannot explain is reported as `workbench.flow.route_unexplained` for that run, never thrown.

Tests: for every reference document the result equals what the existing test helper computes; the reference flows keep their present coverage (pinned numbers); a fixture with a dead route reports it.

## 7. Review document `brain.process-review@1`

Same idea and shape as `brain.rule-review@1`, for a flow. `FlowReview`: process id, version, content hash, reviewer, created at (supplied by the caller), language, items `(target, state, note)` reusing `ReviewState`, and proposed scenarios (objects in the embedded scenario format, for example written by `ToScenarioJson`). Targets: `process`, `stage:<id>`, `step:<id>`, `route:<step>/<label>`, `trigger:<id>`, `scenario:<name>`.

`FlowReviewJson.Serialize` writes canonical JSON; `Parse` returns the review or errors with the codes `workbench.flow_review.json_invalid`, `.unknown_format`, `.malformed`. `FlowReviewCheck.Against(review, process)` reports, without failing the parse: a content hash that is not the document's (`.stale`), a target the document does not have (`.target_unknown`), a proposed scenario that is not valid for the document (`.scenario_invalid`, with the validator's own code and path) and, for each valid proposed scenario, whether it runs to its own `expect`.

A review never changes a document. Applying a proposed scenario to a document is the author's edit in the repository.

Tests: round trip, every error code, a stale review, unknown targets of each kind, a proposed scenario that passes and one that fails.

## 8. Documents

`docs/reference/flow-workbench.md` (every type and code above, with a compiled example for the simulator and an answer source), `docs/reference/brain-process-v1.md` (the outline), the guide (how a host shows a flow, simulates it with its own AI provider behind `IFlowAnswerSource`, and collects a review), `docs/architecture/overview.md`, `docs/consuming.md`, `CHANGELOG.md`, `docs/specs/flow-workbench/tasks-h1.md`, version 0.5.0.

## 9. Not in this package

Any screen or component (H2). A live case over the diagram and reading the host's tables (H3). Editing steps, routes, timeouts or triggers. A diff between two versions of a flow. Simulating the PostgreSQL host (leases, retries of the pump): the simulator drives the pure interpreter only. Any model vendor code.

## 10. Amendments after implementation and verification

What the package does differently from, or in addition to, the sections above. Where this section and an earlier one disagree, this section holds.

- Extra public members: `ProcessOutline.ScenarioJson` and `ProcessOutline.CheckScenario` (a scenario that is not in the document, checked with the loader's own validator); `FlowSimulation.Name`, `Started` and `StartCode`; `FlowSimulation.OpenChild`; the `FailEffect(effectId, kind, code, retryAfter)` overload; the cancellation token of `RunAnswersAsync`; the optional `knownProcesses` of `FlowDescriber.Describe` and `FlowGraph.From`; the `locale` parameter of `FlowCoverage.Of` and `FlowCoverage.Compute`.
- Stricter inputs and codes: a review with a repeated JSON member name is refused (`workbench.flow_review.json_invalid`); a zone id or a scenario name that the scenario validator would refuse is refused; a proposed scenario that is not a JSON object is reported with `workbench.flow_review.scenario_not_object` inside a `scenario_invalid` finding; a command on a session whose first entry the interpreter rejected is refused with `workbench.flow.not_started` (`Start` still returns the session, with `Started` false and `StartCode` set); a clock move that would leave the range of an instant is refused with `workbench.flow.duration_invalid`.
- `At` is truncated to whole seconds by `Start`, because the scenario format holds nothing finer. A session opened by `Replay` or `OpenChild` keeps its instant, and `AdvanceTo` rounds its span up to the next whole second so that it still moves.
- A review is stale (`workbench.flow_review.stale`) when its process id, version or content hash differs from the document, not only the hash.
- Route reading: the history names the route when it can. When it cannot (a decide whose output mapping failed leaves the same history as a success), the route is the one declared route that leads to the step the case went to; if two routes lead there, or none does, the run is reported as `workbench.flow.route_unexplained` for that run and never thrown.
- Back edges are defined as in section 4.2: an edge is a back edge when it closes a cycle.
- The internal model keeps `requires`; the format, the interpreter, the state text and the content hash are unchanged.
- The library orders its entries by id, then version, then content hash, and plays the scenarios of each document once.
- `FlowCoverage.Of` and `Compute` take the language of the text of a problem, and refuse (with `ArgumentException`) a run of another process, of another version or of another content hash, and (with `ArgumentNullException`) a null run.
- Every message that a person reads exists in English and Spanish and follows the locale asked for. Values of a route (`on:` entries, result names) are shown as code.

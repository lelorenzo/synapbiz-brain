# Tasks: H1, the flows surface without a screen

Spec: [spec-h1-headless.md](spec-h1-headless.md). The core read model is in `Synapbiz.Brain.Process`; everything else is in `Synapbiz.Brain.Workbench`, namespace `Synapbiz.Brain.Workbench.Flows`.

## Outline (`Synapbiz.Brain.Process`)

- [x] Spec copied unchanged to `docs/specs/flow-workbench/spec-h1-headless.md`
- [x] `ProcessOutline`, `OutlineStep`, `OutlineStepKind` and the small public records, built from the loaded model; routes read from the validator's own enumerator of edges
- [x] The internal model keeps `requires` (no change to the format, the interpreter, the state text or the content hash)
- [x] `ProcessOutline.ScenarioJson` and `ProcessOutline.CheckScenario` (a scenario that is not in the document, checked with the loader's own scenario validator)
- [x] Tests: golden outline text for the six reference documents and the process fixtures; the edge set equals the validator's; labels equal the document; facts of the ai, interrupt, timeout, poll, loop, timer and subprocess steps; kind-specific members null elsewhere; scenario checks; null guards; the reference page names every member
- [x] Reference page: the outline section of `docs/reference/brain-process-v1.md`

## Flows (`Synapbiz.Brain.Workbench`)

- [x] Project reference to `Synapbiz.Brain.Process`; `InternalsVisibleTo` for the workbench tests
- [x] Template tables in English and Spanish (`FlowTemplates`), number, duration, list and time words (`FlowText`), labels from `meta.display` (`FlowLabels`)
- [x] `FlowDescriber`, `FlowView`, `FlowStepView`, `FlowRouteView`, `FlowAiView`, `FlowStageView`
- [x] `FlowGraph`, `FlowNode`, `FlowEdge`
- [x] `FlowLibrary`, `FlowEntry`, `FlowChildLink`
- [x] `FlowSimulation` (start, commands, outstanding items, log, `OpenChild`, `RunAnswersAsync`, `Replay`, `ToScenarioJson`), `FlowSimulationSetup`, `FlowSimulationEntry`, `FlowOutstanding` and its six records
- [x] `IFlowAnswerSource`, `FlowEffectRequest`, `FlowAnswer`, `ScriptedAnswerSource`
- [x] `FlowCoverage`, `FlowCoverageResult`, `FlowRouteCoverage`
- [x] `FlowReview`, `FlowProposedScenario`, `FlowReviewJson`, `FlowReviewCheck`, `FlowReviewReport`, `FlowProposedScenarioResult`
- [x] Fixtures in `tests/fixtures/workbench/flows`: three synthetic documents (`batch`, `child`, `bare`) that exercise what the reference documents do not (a `forEach` loop, a timer tied to an instant, a single-attempt ai step, a fixed poll, a subprocess with `next`, weekday and day-list schedules, a decide with no facts, seconds and minutes in durations), and the golden views and coverage numbers
- [x] Tests: golden views in both languages; template parity, placeholders and use of every template; graph; library; simulator against the independent scenario player for every scenario of every document; `ToScenarioJson` round trip; scripted source, malformed ai output, throwing source, answer limit, cancellation; coverage against the independent route map and pinned numbers; the review document; null guards
- [x] Architecture tests: the pinned reference list of the workbench now holds the four libraries

## Documents and release

- [x] `docs/reference/flow-workbench.md`, with C# examples compiled in `FlowDocExamples.cs` and compared with the page by `FlowDocumentationTests`
- [x] Section 13 of `docs/guides/process-engine.md`
- [x] `docs/architecture/overview.md`, `docs/consuming.md`
- [x] `CHANGELOG.md` 0.5.0
- [x] Version 0.5.0 in `Directory.Build.props`

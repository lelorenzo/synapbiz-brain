# Tasks: H2, the flows panels (read only)

Spec: [spec-h2-panels.md](spec-h2-panels.md). The component is in `Synapbiz.Brain.Workbench.Blazor`; the small pure additions it needed are in `Synapbiz.Brain.Workbench`, namespace `Synapbiz.Brain.Workbench.Flows`.

## Headless support

- [x] `FlowWords`, `FlowForms`, `FlowScenarioAnswers`, `FlowSchemaFields` and `FlowSimulation.ApplyAnswer`, with tests (`FlowPanelSupportTests`) and the section "Support for a screen" of `docs/reference/flow-workbench.md`
- [x] Repair pass after verification: `FlowReplay`, `FlowScenarioReport`, `FlowReviewTargets`, `FlowCodes`, `FlowSimulation.AskAsync`, `NextDue`, `AnsweredCount`, `EnteredSteps`, `FlowNode.Family`, `FlowEdge.ShortLabel`, `FlowRouteView.Stem`, with tests (`FlowPanelLogicTests`) and the section "Replay, report, review targets and the sentences for codes" of `docs/reference/flow-workbench.md`

## Component

- [x] `FlowWorkbench`, `WorkbenchFlow`, `FlowWorkbenchSession`, `FlowWorkbenchTab`, `FlowWorkbenchExport`, `FlowWorkbenchExportFile`
- [x] List, Read, Diagram (deterministic layout, inline SVG), Simulate, Tests and Review tabs
- [x] Strings under `flow.` in English and Spanish; styles from existing `--wb-*` tokens only; no JavaScript
- [x] bUnit tests: mounting, read, diagram and layout (one flow pinned, geometric rules for all), simulate (start, run, source, keep, replay), tests tab, review notes and export, strings, accessibility
- [x] Architecture test: the pinned reference list of the Blazor assembly holds five libraries
- [x] `StringsTests`: the list of public type names includes the new types

## Demo and screenshots

- [x] Demo page `/flows`, shared header, answer source "Demo answers"
- [x] `screenshots.mjs` flow captures in both languages, light and dark, wide and narrow

## Documents and release

- [x] `docs/reference/flow-workbench-panels.md`, link from `docs/reference/flow-workbench.md`
- [x] Section 13 of `docs/guides/process-engine.md`, `docs/architecture/overview.md`, `docs/consuming.md`
- [x] `CHANGELOG.md` 0.6.0
- [x] Version 0.6.0 in `Directory.Build.props`

## Repair pass after verification

- [x] A. Review notes for every item, guarded focus, tests for flow, step, route, stage, scenario and trigger items and for a focus that fails
- [x] B. Ask after a replay, step-by-step replay, typed start forms, notes of other flows hold the export, export marks only what it wrote, version in the list, `ProcessText` removed, scenarios played once, `OnExport` exception contract
- [x] C. Headless additions with tests
- [x] D. Diagram layout rule, geometric tests for every flow and fixture in both languages, one flow pinned
- [x] E. Wording, quotes, code type, times, attribution, no overflow at 480 px
- [x] F. Tests that kill the mutants of the verification
- [x] G. Reference pages and spec amendments (section 9 of the spec)
- [x] H. Screenshots of the flows set, and of the diagram of each flow

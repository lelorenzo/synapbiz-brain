# Tasks: H2, the flows panels (read only)

Spec: [spec-h2-panels.md](spec-h2-panels.md). The component is in `Synapbiz.Brain.Workbench.Blazor`; the small pure additions it needed are in `Synapbiz.Brain.Workbench`, namespace `Synapbiz.Brain.Workbench.Flows`.

## Headless support

- [x] `FlowWords`, `FlowForms`, `FlowScenarioAnswers`, `FlowSchemaFields` and `FlowSimulation.ApplyAnswer`, with tests (`FlowPanelSupportTests`) and the section "Support for a screen" of `docs/reference/flow-workbench.md`

## Component

- [x] `FlowWorkbench`, `WorkbenchFlow`, `FlowWorkbenchSession`, `FlowWorkbenchTab`, `FlowWorkbenchExport`, `FlowWorkbenchExportFile`
- [x] List, Read, Diagram (deterministic layout, inline SVG), Try it, Tests and Review tabs
- [x] Strings under `flow.` in English and Spanish; styles from existing `--wb-*` tokens only; no JavaScript
- [x] bUnit tests: mounting, read, diagram and layout pin, try-out (start, run, source, keep), tests tab, review and export, strings, accessibility
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

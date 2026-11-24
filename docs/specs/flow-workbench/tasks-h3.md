# Tasks: H3, the process map and Live

Spec: [spec-h3-map-live.md](spec-h3-map-live.md). Part A is the wording, the kinds, the map, the diagram vocabulary and the sources of a rule (version 0.8.0). Part B (Live, section 4 of the spec) is done, with the polish list, and the version is 0.8.0.

## 1. Wording

- [x] Every default text says process, in both languages; the demo header and group; the wording tests

## 2. Kinds

- [x] `FlowKind`, `FlowKinds` (main, subprocess, scheduled), tests on every reference document and fixture

## 3. The map

- [x] `ProcessMap` (structure, uses, labels from `meta.display`, the host and the raw name), goldens in both languages, shared pieces, two parents, a cycle
- [x] The map view of `FlowWorkbench` (default view), list by kind, process beside the map, breadcrumb, legend, `MapLabels`, `OnOpenRule`, narrow layout
- [x] Kind tokens `--wb-kind-*`, icons, stylesheet and string tests
- [x] Diagram vocabulary: a symbol on each step, the legend with the same symbols, route labels never cut below 8 characters, the numbered mark and the list under the diagram, geometric tests on every document in both languages, the pinned `previsit.outreach` test updated (labels only)

## 4a. Sources and authority of a rule

- [x] `RuleClaims`, `meta.sources` accepted by the loader, `rule_list` filters, `rule_get`, the differ and `claims_changed`
- [x] `RuleWorkbench`: sources block, authority badge, `SourceLabels`, `AuthorityLabels`; `FlowWorkbench`: `RuleAuthorities`, `AuthorityLabels`

## 4. Live

- [x] Headless: `IFlowLiveSource`, `ProcessMapLive`, `LiveCasePath`, the path of every prefix of every scripted history of the six reference documents in both languages, an unexplained history, no source
- [x] The adapter over the cockpit queries, in the new package `Synapbiz.Brain.Workbench.Postgres` (mapping table in `process-host-postgres.md`), integration tests and an architecture test
- [x] The Live view of `FlowWorkbench`: optional `LiveSource`, `OnOpenCase`, `LiveRefreshToken`, `Refresh()`, counts, list, one case over its diagram, loading, empty and failure states, both languages
- [x] Demo (`/flows`) with a scripted synthetic source; `screenshots.mjs live` writes the `pl-*` pictures

## Polish

- [x] Group heading once when every process has the same group; subprocess cards at the same height; node text clear of the symbol and the folded corner; legend closed while a process is open; `pm-*` pictures regenerated

## Demo and screenshots

- [x] `/flows` with synthetic labels, authorities and kind tokens; `/` with synthetic sources; `screenshots.mjs map` writes the `pm-*` pictures

## Documents and release

- [x] `docs/reference/flow-workbench-panels.md`, `flow-workbench.md`, `rule-workbench-panels.md`, `brain-authoring.md`, `docs/architecture/overview.md`, `docs/consuming.md`, `CHANGELOG.md`
- [x] Version 0.8.0 in `Directory.Build.props`

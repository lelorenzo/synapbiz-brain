# Tasks: H3, the process map and Live

Spec: [spec-h3-map-live.md](spec-h3-map-live.md). Part A is the wording, the kinds, the map, the diagram vocabulary and the sources of a rule (version 0.8.0). Part B (Live, section 4 of the spec) is open.

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

## 4. Live (open)

- [ ] The Live tab: counts, the path of a case replayed from a scripted history, an unexplained history, no source
- [ ] An optional live source parameter of `FlowWorkbench` (the component is ready for it: nothing in part A assumes there is none)

## Demo and screenshots

- [x] `/flows` with synthetic labels, authorities and kind tokens; `/` with synthetic sources; `screenshots.mjs map` writes the `pm-*` pictures

## Documents and release

- [x] `docs/reference/flow-workbench-panels.md`, `flow-workbench.md`, `rule-workbench-panels.md`, `brain-authoring.md`, `docs/architecture/overview.md`, `docs/consuming.md`, `CHANGELOG.md`
- [ ] Version 0.8.0 in `Directory.Build.props` (not bumped here: the release is after part B)

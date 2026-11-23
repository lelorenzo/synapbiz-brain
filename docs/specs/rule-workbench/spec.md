# Spec — Rule workbench: show, simulate, test and review `brain.rule@1` rules

Status: approved by the owner on 2026-11-22 (six decisions, section 2). Package W-A in progress.

## 1. Problem

The person who owns a rule library is a domain expert, not an engineer (first case: a physician who owns clinical rules). They must be able to read each rule in plain language, try it with example facts, run and add test cases, and send back a review, without reading JSON and without any way to activate a rule. Approval stays a separate, human gate owned by the consuming product.

The source repository has an internal single-file rule-pack visualizer (explorer, decisions as sentences, a simulator with a trace, package tests, a client review, live JSON). Its experience is the model for this work. Its code is not reused: it reads another format and evaluates rules with its own JavaScript evaluator. A workbench that simulates with a different evaluator than production can show a result production would not give, which is not acceptable for the rules this is built for.

## 2. Owner decisions (2026-11-22)

1. Build on the real engine; take the visualizer as the experience model; do not port its evaluator.
2. The workbench lives in this repository as two new packages (section 3).
3. First version: view, simulate, run and add test cases, comment on each rule and export the review. Guided editing that produces a draft revision with its diff comes right after. The expert never edits JSON and never activates anything.
4. The first consumer mounts the panels as a "Rules" screen of its local application.
5. Simulation facts are typed by hand or come from synthetic example cases. There is no "simulate with a real record" action.
6. The workbench does not block the first consumer's installer; it ships in the following update.

## 3. Packages

| Package | Content | Depends on |
| --- | --- | --- |
| `Synapbiz.Brain.Workbench` (W-A) | Headless: rule to view model and sentences (English, Spanish), fact input parsing, simulation with a per-row explanation, test runs with extra cases, revision diff and behaviour comparison, review document. | `Synapbiz.Brain.Spec`, `.Engine`, `.Contracts`, base class library |
| `Synapbiz.Brain.Workbench.Blazor` (W-B) | Razor class library: rule list, rule reader, simulator, tests, review and diff panels. No evaluation logic of its own. | `Synapbiz.Brain.Workbench`, ASP.NET Core components |

Every result a panel shows comes from W-A, and every decision W-A reports comes from `BrainRule.Evaluate`, `TableCompiler`, `ConditionMatcher`, `GoldenRunner` and `TableValidator`. W-A adds presentation and comparison only. An architecture test freezes its references.

A host that is not Blazor (the source repository's React back office) can consume W-A behind its own API and keep its views. Its sentence texts are the same templates.

## 4. What W-A provides

- **Labels.** Display names for facts, outcome fields, values and rows, per locale. A rule may carry them under `meta.display.<en|es>`; a host may also supply them. A missing label falls back to a humanised name. Labels never change a decision and a malformed label is ignored.
- **Rule view.** Facts, outcome fields, rows in decision order and the default row, each row with its conditions and one sentence ("If ..., then ..."). Templates are those of the source repository's sentence generator.
- **Fact input.** Text to a typed engine value for a declared fact, with a typed error for a value that does not fit.
- **Simulation.** The engine's decision and trace, plus for every row whether it matched, whether it was selected, and which conditions passed for the given facts. A fact set the engine refuses, or an evaluation the engine refuses, is a typed failure with the engine's code, never a guess.
- **Tests.** The document's golden cases and any extra cases run together on the engine; per case pass or fail, expected and actual; rows no passing case covers.
- **Diff.** Between two loaded revisions: rows added, removed, changed (conditions, outcome) or moved, hit policy and default row changes, golden cases added, removed or changed. And a behaviour comparison: the outcome of both revisions for each example fact set, flagged when it differs.
- **Review document.** `brain.rule-review@1`: who reviewed which rule (id, version and content hash of the exact text), a state and note per rule, row or test case, and proposed test cases. It serialises to JSON and reads back. It is a message from the expert to the rule maintainers; it changes no rule.

## 5. Data rules

- The library reads no file, opens no connection and keeps no state (purity scan).
- Example fact sets, test cases and review notes are typed by a person. The library cannot tell whether a note holds personal data, so the panel that edits a review (W-B) shows a standing notice that notes and case names must not identify a real person, and the export is a file the reviewer hands over themselves.
- Fixtures and tests are synthetic.

## 6. Out of scope

Guided editing and draft revisions (next package, W-C). A flow view for process documents (waits for the process engine). Persistence, activation, permissions and audit (the host's). The React views of the source repository.

## 7. Acceptance for W-A

Build Release with 0 errors and no warning in the new files; all existing tests green and unchanged; new unit tests for every public member including both locales, every operator, every hit policy and every failure code; line coverage of `Synapbiz.Brain.Workbench` at least 95%; architecture tests extended (reference freeze, purity scan); the package packs; reference page and changelog updated.

# Brief W-C3 — Guided editing refinements after the design review

Packages: `Synapbiz.Brain.Workbench` (two small additions) and `Synapbiz.Brain.Workbench.Blazor`. No new package, no new dependency.
Follows: W-C1 (merged), W-C2 (`docs/briefs/W-C2-guided-editing-panels.md`). Design reference: `docs/specs/rule-workbench/guided-editing-design.md`; add this brief's seven changes to it as a final section "13. Review of the guided-editing captures", in the same style.

## A. Headless library (`Synapbiz.Brain.Workbench`)

A1. **An expectation the edit can always express.** `SetCaseExpectation` must be able to say every expectation a test case can hold, so a panel never needs remove plus add:

- a decision by a row or by the default row (today's behaviour);
- no decision ("no row decides");
- under `collect`, zero, one or several results.

Keep the existing constructor source-compatible. Add what is needed (for example a `SetCaseExpectation.NoDecision(string caseName)` factory and a `Results` list for `collect`); choose the smallest shape that round-trips through `BrainRuleLoader.Load` for every hit policy, and report it. The case keeps its position and every other member. Round-trip tests for each form on each hit policy, loading the draft and running the case with `RuleTester`.

A2. **A row whose only change is its name counts as changed.** `RuleChangeDescriber.Describe` already receives the labels of both sides. A row present on both sides whose row label differs between `labelsBefore` and `labelsAfter`, and that `RuleDiffer` does not report, is added to `Rows` as `Changed` with both sentences (they are equal) and is counted in `Summary` as a changed row. Add to `RowChangeView` two members, `string? NameBefore` and `string? NameAfter` (the row labels of each side, null when the row has none), set for every row. Tests: name added, changed, removed, name plus a structural change (counted once), both locales, summary counts.

No other change in the headless library. Purity and coupling tests unchanged.

## B. Panels (`Synapbiz.Brain.Workbench.Blazor`)

B1. **Answers name only what differs** (test case in "Cambia"). Compare the outcome fields of the base result and the draft result.
- One or two fields differ: each button reads the differing fields as "{campo}: {valor}" joined by ", ", then " · ", then the action. Example: "Cola: Cola telefónica · aceptar el nuevo resultado" and "Cola: Cola urgente · revisar mi cambio".
- More than two differ, or one side has no result: the buttons read "Aceptar el nuevo resultado" and "Revisar mi cambio", and above them one line per differing field: "{campo}: antes {a}, ahora {b}" / "{field}: before {a}, now {b}"; a side with no result reads "sin resultado" / "no result" as one line.
- Values use the labelled short form the test table already uses.

B2. **Bold marks the difference only.** In the "Su borrador da" column of a changed case, and in the "Antes" and "Ahora" columns of the Changes tab's case table, only the parts of the result whose value differs are bold (a `<strong>` per differing part); the rest is regular. The Changes tab's case table uses the short result form of the test table ("escalado, Cola telefónica, Avisar: sí, Prioridad: 1"), not full sentences. If the short form must be built from parts, build it in the panel from the same formatter the test table uses; do not parse text.

B3. **One menu per case.** In the draft view each case row has a single "Más" menu button, the same menu component as the row menu, with "Cambiar lo que se espera" and "Quitar el caso" (and "Restaurar" stays a visible button on a removed case). A case in "Cambia" keeps its two answers as the visible actions and also has the menu.

B4. **The list keeps its order.** Every expectation change uses `SetCaseExpectation` (A1). Remove plus add is no longer used for an edit. Only new cases go to the end. Assert the case order in the draft document after: accept, change to another decision, change to "no row decides", and on a `collect` rule change to zero and to two results.

B5. **Counts agree.** The summary sentence, the Changes tab pill, the export card's row-change count, the draft bar count and the discard dialog count all come from `RuleChangeView.Rows` (A2). Remove the panel's own rename detection (`RenamedRows`) and use `NameBefore` / `NameAfter`. Leer's tags and the Changes tab's rows stay equal (keep that test). The name sentences of W-C2 (`changes.renamed`, `changes.named`, `changes.unnamed`) stay.

B6. **Row name in the Changes tab.** A card is titled "Fila {n} · {nombre}" when the row has a name on the side it is numbered by (after for added, changed and moved rows; before for removed rows), else "Fila {n}". The name is the display label, never a key.

B7. **"Más" shows that it opens.** The menu button's text is followed by a caret drawn in CSS (a border triangle or a `::after` with a token colour), not a glyph inside the string, so the string keys do not change and screen readers do not read it. Rows and case table.

## C. Rules

Same as W-C2: English code and docs; strings in Spanish and English as keys; only `--wb-*` tokens; no script, no `IJSRuntime`; no identifier on screen; synthetic data; no change to `Contracts`, `Engine`, `Spec`, `Process`, the hosts, the workflows, `Directory.Packages.props` or the version; never skip, disable or delete a test; tests whose pinned text changes on purpose are updated and listed in the report.

## D. Tests, captures, docs

- bUnit tests for each of B1 to B7 in both locales where text is asserted; headless tests for A1 and A2. Coverage of `Synapbiz.Brain.Workbench` stays at or above 98.5% and `Synapbiz.Brain.Workbench.Blazor` at or above 99.0%.
- Re-run `screenshots.mjs`; look at `wb-edit-cases-light.png` and `wb-edit-changes-light.png` and say what they show.
- `docs/reference/rule-workbench.md` (A1, A2), `docs/reference/rule-workbench-panels.md` (key table if keys change, the menu), the design reference's new section 13, `docs/specs/rule-workbench/tasks.md` (a W-C3 section, "Independent verification" left unticked), `CHANGELOG.md`.

## E. Gates and report

From the repository root with `DOTNET_ROLL_FORWARD=Major`: Release build with 0 errors and only the 2 baseline warnings in `OverlapAnalyzer.cs`; tests green in Architecture, Engine, Spec, Workbench, Process and Workbench.Blazor (baseline 27 + 167 + 226 + 677 + 519 + 512; `Synapbiz.Brain.Host.Postgres.Tests` needs Docker and is not part of this gate where Docker is absent: run the other projects one by one and say so); coverage of both workbench libraries; `dotnet pack` builds the packages; the drift guard reports none.

Report: commits, gate numbers, the shape chosen for A1, string key count, tests whose expectations changed, deviations, anything undone.

# Brief W-C2 — Guided editing panels

Package: `Synapbiz.Brain.Workbench.Blazor` (existing). No new package, no new dependency.
Design to build: `docs/specs/rule-workbench/guided-editing-design.md` (sections 12.1 to 12.10). It is binding for layout, behaviour, copy and accessibility; this brief adds the technical contract and the points where the library deviates from it.
Built on: W-C1 (`RuleEdit`, `RuleDrafter`, `RuleChoiceCatalog`, `RuleChangeDescriber`, `DraftErrorLocator`, `RuleProposalJson`), W-B and W-B2 panels. Read `docs/reference/rule-workbench.md` ("Drafts and proposals") and `docs/reference/rule-workbench-panels.md` first.

## 1. Goal

Inside the single public component `RuleWorkbench`, let the reader start a draft of the selected rule, change it through forms, see what changes, decide on the test cases whose result changes, and export a proposal file. The draft lives in `WorkbenchSession` (memory). Nothing is activated, approved or saved by the library.

## 2. Host contract (public surface; names are binding)

### 2.1 `WorkbenchRule` gains

```csharp
/// Loads a draft text with the host's contracts, exactly as the host loads its rules. Null turns guided editing off for this rule.
public Func<string, BrainRuleLoadResult>? LoadDraft { get; init; }

/// For outcome fields whose values are texts the host knows: field name to the allowed values and the sentence shown for each, in order.
public IReadOnlyDictionary<string, IReadOnlyList<WorkbenchOutcomeText>>? OutcomeTexts { get; init; }
```

```csharp
public sealed record WorkbenchOutcomeText(DecisionValue Value, string Text);
```

Guided editing is offered for a rule only when `Rule`, `RuleText` and `LoadDraft` are all present. Otherwise "Proponer cambios" is not rendered and nothing else changes.

### 2.2 Export

`WorkbenchExportFile` gains a nullable `RuleProposal? Proposal` and its `Review` becomes nullable; exactly one of the two is set. Keep the positional order `(SuggestedName, Json, Review, Proposal = null)` so existing hosts compile. A proposal file's suggested name follows the review's pattern with `proposal` in place of `review`. The same `OnExport` callback receives it; its returned text is the folder shown in "Propuesta guardada en {carpeta}".

Proposal content: `RuleId`, `BaseVersion`, `BaseContentHash = RuleReviewJson.ContentHash(RuleText)`, `Author` = the name field, `CreatedAt = Now()`, `Note` = the reason followed, when there are warnings (12.4 "Avisos"), by a blank line and one line per warning sentence in the current locale, `DraftJson` = the draft text.

### 2.3 `WorkbenchSession` gains

```csharp
public int UnexportedDraftCount { get; }   // drafts never exported or changed after export
```

`Clear()` also drops drafts. `UnexportedRuleCount` keeps its meaning (reviews). The host's leave guard uses both; document this in the panels reference.

### 2.4 Strings and tokens

- Every string of the design is a key in `WorkbenchStrings` with the Spanish and English defaults of the design. Neutral defaults: the draft bar's draft-view sentence defaults to "**Está viendo su borrador.** Nada de esto está en uso: la regla actual sigue igual hasta que se apruebe e instale una versión nueva." / "**You are viewing your draft.** None of this is in use: the current rule stays as it is until a new version is approved and installed." (the product wording of the design is a host override).
- New mandatory markings, in addition to the existing four: `draft.bar` (the bar's draft-view sentence), `pill.draft`, `export.proposal.explain`. An empty override is ignored for them, as for the existing ones, and the tests that pin the mandatory markings are extended, not loosened.
- One new token: `--wb-scrim` (dialog backdrop). Add it to the token list in the reference, to the demo palette and to the stylesheet contract test. No colour, font or fallback in the library stylesheet.
- The identifier guard still holds: no rule id, row key, fact name, field name, operator name or error code reaches the screen.

## 3. Behaviour contract

### 3.1 Draft state (internal, per rule, in the session)

- Base text and base rule; an ordered list of **steps**, each a list of `RuleEdit` applied as one unit with what the step is (row edit, add, remove, move, case decision, case edit); the current draft text and loaded rule; whether exported and whether changed since.
- The draft text is always `RuleDrafter.Apply(baseText, all edits of all steps)`. Undo of a row ("Deshacer", "Restaurar"), of a case decision, and "Deshacer el último cambio" remove steps and re-apply. Removing steps must also leave a draft the loader reads; if it does not, the undo is refused with the 12.4 box.
- **Apply** of a form, a menu action or a case action: build the candidate from the steps plus the new one; `LoadDraft(candidate)`; if the load has errors, or `ValidateTable()` reports `decisions.validation.overlap` and the hit policy is `unique`, the draft is unchanged and the refusal is shown (12.4); otherwise the candidate becomes the draft and the test cases run.
- Row keys of new rows are generated by the panel (`row-` plus a short lowercase suffix, unique in the document, valid as a section id) and never shown.
- Removing a row whose key test cases expect: the refusal lists each such case and the row stays (12.4). The reader changes or removes those cases first.
- "The rule changed underneath" (12.9): when the `RuleText` of the rule with a draft no longer hashes to the draft's base hash, the draft is read-only and can only be exported or discarded.
- Leaving the selected rule or tab keeps the draft. `Session.Clear()` drops it.

### 3.2 What each tab shows

Follow 12.1: the view switch decides whether Leer, Simular and Casos de prueba show the base rule (with the review controls, exactly as today) or the draft (edit controls, no review controls). Simulation and test runs on the draft use the draft's loaded rule through the existing `RuleSimulator` and `RuleTester`. Changes and case results come only from `RuleChangeDescriber.Describe(base, draft, ...)`; the panels add no comparison logic.

### 3.3 The row form (12.3)

- Lines, operators and values come from `RuleChoiceCatalog.For`. The operator `Any` is "cualquier valor" and maps to `ClearCondition` (or to no condition in `AddRow`). The operator names in words are the design's table; an operator with no entry in the table is not offered.
- Value inputs reuse the simulator's inputs (`FactForm` and `FactInput.Parse`): the same parsing and the same error texts. Sets: checkboxes for enum facts, otherwise a list of text chips. Range: two inputs, "desde" and "hasta", with the inclusive flags fixed by the chosen wording (entre: both inclusive; como mínimo / como máximo: inclusive; mayor que / menor que: exclusive).
- Outcome inputs by the field's type; a field present in `OutcomeTexts` is a select of the sentences; an optional field's select starts with "Sin valor".
- The live preview is produced by applying the form's edits to a candidate and describing that row with `RuleDescriber`; when the candidate does not load, the preview keeps the last valid sentence.
- Panel-side checks before applying (12.4, first table) are field errors and do not call the loader.
- Refusal sentences: the design's tables, keyed by error code, with the place from `DraftErrorLocator.Locate`. Codes outside the tables use the internal-problem sentence.
- Warnings (12.3 and 12.4): "se cumple siempre" under `first`, same conditions as another row, `decisions.validation.unreachable`, `decisions.validation.incomplete`. Only findings the base rule did not already have are shown as new.

### 3.4 Cases (12.6)

A case is "Cambia" when it is in `RuleChangeView.CasesThatChange` and passed on the base. "Aceptar el nuevo resultado" adds a `SetCaseExpectation` step with the draft's actual deciding row and outcome. "Revisar mi cambio" navigates as the design says, using `RowKeyAfter` / `RowKeyBefore`. "Cambiar lo que se espera", "Quitar el caso", "Restaurar" and "Agregar un caso" map to `SetCaseExpectation`, `RemoveCase`, step removal and `AddCase`. Export is held while any case is in "Cambia".

### 3.5 Cambios and export (12.8), discard and guard (12.9)

As designed. The discard dialog is a real modal: focus trapped, Escape keeps the draft, default focus on "Seguir editando", backdrop from `--wb-scrim`. No script: focus is set with `ElementReference.FocusAsync`, the trap by rendering the rest of the component `inert`. If something in the design cannot be done without JavaScript, do the closest script-free behaviour and report it; do not add a script file or `IJSRuntime` (the architecture test forbids both).

## 4. Rules

- All code, comments and docs in English. Strings in Spanish and English.
- The panels add no evaluation, validation or comparison logic: every result comes from the headless library and the host's loader.
- No change to `Synapbiz.Brain.Contracts`, `.Engine`, `.Spec`, `.Process`, the workflows, `Directory.Packages.props` or the version. A small addition to `Synapbiz.Brain.Workbench` is allowed only if a panel needs a datum the library computes but does not expose; list each one in the report.
- Existing behaviour without a draft is unchanged: every existing bUnit test passes with at most mechanical edits for the `WorkbenchExportFile` shape. No test is skipped, disabled or deleted.
- Synthetic data only, in tests, demo and screenshots.

## 5. Tests (bUnit, both locales where text is asserted)

Start, switch and discard a draft; the draft bar on every tab; each menu action; the form for every operator in the design's table and every value type; preview; every panel-side check; every refusal code of the design's table with the marked field, produced by real loader errors (not stubs); the validator block and both warnings; undo and restore for each change kind; a case that changes, both answers, the held export; case edit, remove, restore, add; the Cambios tab; the export payload (parse it with `RuleProposalJson.Parse`, load its draft with the loader); done and changed-after-export states; the read-only draft when the base changed; guided editing off without `LoadDraft`; the new mandatory markings; the identifier guard and the stylesheet token contract on the new markup; `UnexportedDraftCount`. Line coverage of `Synapbiz.Brain.Workbench.Blazor` stays at or above 99.0% and `Synapbiz.Brain.Workbench` at or above 98.5%.

## 6. Demo, screenshots, docs

- Demo host: pass `LoadDraft` (and `OutcomeTexts` for one field) on the intake rules; add `--wb-scrim` to both palettes.
- `screenshots.mjs`: add, light and dark, into the same output folder the script already uses: `wb-edit-form`, `wb-edit-refusal`, `wb-edit-draft` (Leer after two changes with the case notice), `wb-edit-cases`, `wb-edit-changes`, `wb-edit-discard`. Keep the existing captures unchanged in content.
- `docs/reference/rule-workbench-panels.md`: a "Guided editing" section (host contract of section 2, the new keys and markings, the token, the leave guard); `docs/specs/rule-workbench/spec.md` and `tasks.md` (a W-C2 section, "Independent verification" left unticked); `CHANGELOG.md`.

## 7. Gates and report

From the repository root with `DOTNET_ROLL_FORWARD=Major`: Release build with 0 errors and only the 2 baseline warnings in `OverlapAnalyzer.cs`; all tests green (baseline 22 + 167 + 226 + 677 + 519 + 297); coverage of both workbench libraries; `dotnet pack` gives six packages; the drift guard reports none.

Report: commits, gate numbers, the string key count, screenshots written and which you looked at, any addition to the headless library, every point where the result differs from the design, anything undone.

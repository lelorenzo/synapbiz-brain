# Rule workbench panels

`Synapbiz.Brain.Workbench.Blazor` is a Razor class library with one public component, `RuleWorkbench`: a rule list on the left and, for the selected rule, four tabs (Read, Simulate, Test cases, Review). It draws what [`Synapbiz.Brain.Workbench`](rule-workbench.md) computes and has no evaluation logic of its own: every result comes from the engine through that library.

Namespace `Synapbiz.Brain.Workbench.Blazor`; references `Synapbiz.Brain.Contracts`, `.Engine`, `.Spec` and `.Workbench`, plus `Microsoft.AspNetCore.Components.Web`. No JavaScript file is shipped. The component runs in Blazor Server or in WebAssembly, interactive mode.

## What the panels do

- **List.** Rules grouped under headings, each with its state, row count, test result and review progress. A rule the host could not load is listed as unreadable. Below 1100 px the list collapses to a select.
- **Read.** One sentence per row, the default row last, and the rule's facts and outcomes.
- **Simulate.** A form of example facts built from the contract, the result with a per-row explanation, and the engine's refusal in words when it does not accept the facts. "Save as a test case" opens the add-case form on the Test cases tab.
- **Test cases.** The rule's golden cases run on the engine plus the cases the reviewer proposes, rows with no passing case, and a form to propose a case. Each case row has a small "Review" button under its status; it opens a full-width line under the row with the confirm / fix / question control and the note field, and once a mark is set the mark's pill replaces the button and toggles the same line (`aria-expanded`, `aria-controls`).
- **Review.** A confirm / fix / question mark per row, per proposed case and for the whole rule, notes, the reviewer's name and the export.

Two markings are always drawn: the simulation band and pill on Simulate and Test cases, and the privacy notice on Review. The host may reword them through `TextOverrides` but cannot empty them.

## Mounting

```razor
<RuleWorkbench Rules="rules"
               Locale="WorkbenchLocale.En"
               Session="session"
               Now="() => DateTimeOffset.UtcNow"
               OnExport="SaveAsync"
               TextOverrides="overrides" />
```

| Parameter | Type | Meaning |
| --- | --- | --- |
| `Rules` | `IReadOnlyList<WorkbenchRule>?` | The rules in list order. `null` shows a loading line; an empty list shows the empty text. Pass the same instances between renders to keep the screen steady. |
| `Locale` | `WorkbenchLocale` | Language of the fixed strings and of the rule sentences. Changing it re-renders in place and keeps the session. |
| `Session` | `WorkbenchSession` (required) | The reviewer's state, owned by the host. |
| `Now` | `Func<DateTimeOffset>` (required) | The host's clock. The library has none: the date of a simulation and the time of an export come from here. |
| `OnExport` | `Func<WorkbenchExport, Task<string?>>?` | Saves an export and returns the place to name in "saved to {place}", or `null` to say it without a place. When the parameter is not set, the export button is not drawn. |
| `TextOverrides` | `IReadOnlyDictionary<string, string>?` | Replaces fixed strings by key for the current locale; see [String keys](#string-keys). |

### `WorkbenchRule`

One record per rule, supplied by the host.

| Member | Meaning |
| --- | --- |
| `Key` (required) | A stable key, unique in the list, never shown. |
| `Group` | The heading the rule is listed under. A heading is drawn each time the group changes. |
| `Rule` | The loaded `BrainRule`; `null` when the rule could not be read. |
| `RuleText` | The exact text of the rule document, used for the content hash of an exported review. A rule with no text counts as unreadable. |
| `FallbackTitle` | The title to show when `Rule` is `null`. |
| `HeadlineFields` | `IReadOnlyList<string>?` | The outcome field names the result heading shows, in order. `null` or empty shows the first field, of the kind's outcome fields, that the decided outcome sets. The definition list under the heading always shows every field. |
| `Labels` | `RuleLabels` for the current locale (build it per locale). |
| `State` | `RuleApprovalState.Draft` or `Approved`, as the host reports it. |
| `StateLine` | A sentence under the title; `null` shows the default line of the state. |
| `SourceLine` | A text after the state line, for example where the rule comes from. |
| `RowTexts` | Row id to a secondary line of the row (for example what a person sees when the row decides). |

### `WorkbenchSession`

In-memory state of one reviewer: selected rule and tab, reviewer name, and for each rule the review marks and notes, proposed cases, simulator fields and last result.

- Register it with the lifetime of one user session (for example `AddScoped` in Blazor Server). One session per reviewer; do not share it.
- Members: `SelectedRuleKey` (`null` selects the first rule), `SelectedTab` (`WorkbenchTab.Read`, `Simulate`, `Tests`, `Review`), `Reviewer`, `UnexportedRuleCount`, `Clear()` and the `Changed` event, raised after any change whoever made it.
- It reads and writes nothing outside memory. Closing the circuit or the page discards it unless the reviewer exported first.

### Export

On "Export review" the component builds one `brain.rule-review@1` document for each rule that has a mark, a note or a proposed case, and calls `OnExport` once with a `WorkbenchExport` (`Files`: `WorkbenchExportFile(SuggestedName, Json, Review)`). The host decides where the files go and returns a short place text. The component then shows "saved" and the session counts nothing as unexported. The export needs the reviewer's name: without it the component marks the name field and moves focus there. Nothing is exported when no rule has a mark, a note or a proposed case. The component does not catch an exception thrown by `OnExport`; handle failures inside it.

## Refinements after the design review (W-B2)

- **No repeated value.** The why-sentence adds the value the person typed only for conditions whose operator is `NotEquals`, `NotIn`, `Range`, `StartsWith` or `IsNotNull`. For `Equals`, `In` and `IsNull` it uses the condition text alone, and in "Row by row" a condition that is met drops the "You entered ..." sentence. A condition that is not met always keeps it, or the "did not enter" sentence.
- **Any-value conditions.** A condition whose operator is `Any`, or a `Range` with no bound, is not listed in "Row by row" and is not named in the why-sentence. A row with no other condition shows one line, `row.always`.
- **Headline and labelled values.** The result heading shows only the headline fields (`WorkbenchRule.HeadlineFields`). In the heading and in the "expected" and "the rule gives" columns, a value of a `Boolean`, `Integer`, `Decimal` or `Money` outcome field is written `<field label>: <value>`; other values stand alone. Parts are joined with `, `, with no final joiner.
- **Money input.** One amount text input and a currency select 88 px wide. Options: `USD` plus every currency that appears in a `Money` operand of a condition on that fact in the rule's table, distinct, `USD` first and the rest in ordinal order; default `USD`. The hint is `hint.moneyUsd` when `USD` is the only option, else `hint.money`. There is no placeholder. An empty amount is an empty fact whatever the select shows.
- **Date input.** The `<input type="date">` carries `lang="es"` or `lang="en"` from the locale, so the browser writes the date in the language of the panel.
- **Long rules.** "Row by row" always shows the deciding rows and every row that matched. When the rule has more than six rows (the default row counts), the rows that do not apply are folded behind the "show the {n} rows that do not apply" button; with six or fewer every row is shown. This replaces the earlier threshold of twelve.
- **A fix or a question needs its note.** In the Review tab an item whose mark is fix or question and whose note is empty or white space shows `review.noteMissing` in the warn tone and a `review.writeNote` button that opens that item's note field and focuses it. While any such item exists in any rule of the session, "Export the review" does not call `OnExport` and shows `review.notesNeeded` with the count. A confirmed item needs no note.

## Style: the `--wb-*` token contract

The stylesheet `_content/Synapbiz.Brain.Workbench.Blazor/workbench.css` holds no palette, no font and no fallback value. It reads only tokens the host defines on an ancestor of the component (for example `.demo { --wb-ink: ...; }`) and a test fails if a colour literal, a font name or a `var()` fallback appears in it. Link it once:

```html
<link rel="stylesheet" href="_content/Synapbiz.Brain.Workbench.Blazor/workbench.css" />
```

Tokens the host must define (light and dark are the host's business):

| Group | Tokens |
| --- | --- |
| Type | `--wb-font`, `--wb-text`, `--wb-text-small`, `--wb-text-label`, `--wb-text-title` |
| Surfaces | `--wb-surface`, `--wb-surface-head`, `--wb-selected`, `--wb-selected-edge`, `--wb-hover` |
| Lines and text | `--wb-line`, `--wb-line-soft`, `--wb-ink`, `--wb-ink-2`, `--wb-muted` |
| Accent and controls | `--wb-accent`, `--wb-accent-ink`, `--wb-accent-hover`, `--wb-control-edge`, `--wb-focus` |
| Shape | `--wb-radius`, `--wb-radius-sm` |
| Status (background, edge, ink) | `--wb-ok-*`, `--wb-warn-*`, `--wb-bad-*`, `--wb-neutral-*`, `--wb-notice-*` |
| Dialog | `--wb-scrim`, the colour behind the discard dialog (added by guided editing; the only new token) |

`samples/Synapbiz.Brain.Workbench.Demo/wwwroot/demo.css` is a complete light and dark example. Every class of the component starts with `wb-`.

## Guided editing

A reviewer can propose changes to a rule inside the same component. Nothing changes the rule, activates it or keeps state outside the session: the reviewer works on a **draft** that lives in the `WorkbenchSession`, in memory, and leaves the panel as a `brain.rule-proposal@1` file. Design: [guided-editing-design](../specs/rule-workbench/guided-editing-design.md). The headless part is [Drafts and proposals](rule-workbench.md#drafts-and-proposals).

### What the host supplies

Guided editing is offered for a rule only when `WorkbenchRule.Rule`, `RuleText` **and** `LoadDraft` are all present; otherwise the rule looks exactly as before.

| Member | Meaning |
| --- | --- |
| `LoadDraft` | `Func<string, BrainRuleLoadResult>`: loads a draft text with the host's contracts, exactly as the host loads its rules. Every candidate draft is read with it, so a draft is judged by the production loader; a candidate it refuses is never kept. |
| `OutcomeTexts` | Optional. For an outcome field whose values are texts the host knows: field name to the allowed values and the sentence shown for each. The field is then picked from a select of the sentences. Without it an enum field is picked from its labelled values, a flag from yes and no, and other fields are typed. |

`WorkbenchSession` gains `UnexportedDraftCount` (rules whose draft has changes that were never exported, or changed after export) and `Clear()` now drops drafts too. `WorkbenchExportFile` is `(SuggestedName, Json, Review, Proposal = null)`: for a proposal `Review` is null, `Proposal` is set and `SuggestedName` is `proposal-<rule id>-v<version>.json`.

### What the reviewer sees

- **Start.** "Propose changes" in the rule header starts a draft (the base text with no edit). A bar under the header says which of the two views is shown and switches between "Current rule" and "Your draft"; a draft with changes asks before it is discarded.
- **Read, in the draft view.** Each row has "Edit" and "More" (move up, move down, add a row below, remove); the button shows a caret drawn by the stylesheet (a `--wb-ink-2` border triangle that points up while the menu is open), not a glyph in the text, so screen readers read "More" only. Each test case in the draft view has the same menu, with "Change what is expected" and "Remove the case"; a removed case keeps a visible "Restore". The row form shows the conditions as a sentence-sized line per fact (an operator and a value) and the outcome fields; "It reads" previews the row, and warnings (a row that always matches, one that can never decide, a gap in coverage) appear before applying. A change the loader or the validator refuses stays in the form with the reason in words and marks the field; the draft does not change. A changed row shows a tag, "Before: ..." and "Undo"; a removed row stays struck through with "Restore".
- **Test cases, in the draft view.** The fourth column is "Your draft gives". A case that passed on the rule and gives another result on the draft is "Changes" (not "Fails") and asks which result is correct: accept the new result (a decision on the case) or review the change (goes to the row that explains it). The line over the cases counts them apart ("7 of 9 cases pass · 2 change"); a case that already failed on the rule keeps "fail" ("6 of 9 cases pass · 2 change · 1 fails"). Read and the tab say how many cases changed. A case whose expectation is accepted or changed (to another decision, to no decision, or under `collect` to zero, one or several results) stays where it is in the list and in the exported document, because every change is a `SetCaseExpectation`; only a new case goes to the end. The two answers of a changed case name only the fields that differ: one or two fields read "Queue: Phone queue · accept the new result" and "Queue: Urgent queue · review my change"; with more than two, or when a side has no result, the buttons read "Accept the new result" and "Review my change" and one line per differing field ("{field}: before {a}, now {b}"), or one line for the whole result, stands over them. In the "Your draft gives" column of a changed case, and in the "Before" and "Now" columns of the Changes tab, the results are in the short form of the test table and only the parts whose value differs are bold. Cases can have what they expect changed, be removed (and restored) and be added; in the draft view the review controls are not shown and nothing is written to the review.
- **Changes.** A fifth tab, shown only while the rule has a draft: the library's summary, each changed row, titled "Row 3 · Invoices on the web" when the row has a name (the draft's; the rule's for a removed row) and "Row 3" when it has none, with "Before:" above "Now:" (a row whose only change is its name is listed too, tagged "Changed", with one line that says what it was named and what it is named now and no sentences). The row count of the summary, the tab, the export card, the draft bar and the discard dialog is the number of rows the library lists, plus the default row, the warnings that remain true, the cases that change result with the decision taken on each, and the card "Send the proposal" (reason, up to 1000 characters, and name, both required). Export is held while any case still changes result. The file is saved through `OnExport`; the draft stays and is marked as exported, and changing it afterwards says so in the bar. A draft whose rule changed underneath (its base text no longer matches) becomes read-only and can still be exported, bound to its base.

### Keys and markings

The new keys are in the table below. `draft.bar`, `pill.draft` and `export.proposal.explain` are markings: they can be reworded, never emptied. No identifier (rule id, row key, fact name, field name, operator, error code) reaches the screen; a fact the labels do not know is written humanised.

### The leave guard

The host's leave guard uses both counts: warn when `Session.UnexportedRuleCount > 0` or `Session.UnexportedDraftCount > 0`. The sentence for drafts is the host's, for example "Tiene un borrador sin exportar de {n} reglas." / "You have an unexported draft of {n} rules."

### What the panels add

Presentation only. Edits are `RuleDrafter` edits, findings are the engine's `ValidateTable`, the difference and the cases that change are `RuleChangeDescriber`, the test cases run on `RuleTester`. The panels add no evaluation, validation or comparison logic.

## String keys

Every fixed string has a key. `WorkbenchStrings.Keys` lists them in ordinal order and `WorkbenchStrings.Default(key, locale)` returns the default. `TextOverrides` replaces a text for the current locale only; keys the host does not name keep their default. In a text a `**bold**` pair renders as `strong`; nothing else is interpreted and every text is HTML-encoded. `{name}` placeholders are filled by the panel; keep the ones the default has. Keys ending in `.one` / `.other` are the singular and plural forms picked by the count `{n}`.

The seven markings (`band.simulate`, `band.tests`, `pill.simulation`, `notice.privacy`, `draft.bar`, `pill.draft`, `export.proposal.explain`) can be reworded but an empty or white-space override is ignored and the default is used.

| Key | Spanish | English |
| --- | --- | --- |
| `band.simulate` | Simulado con datos de ejemplo. No es un caso real y no cambia nada. | Simulated with example data. This is not a real case, and nothing changes. |
| `band.simulateDraft` | Simulando su borrador, no la regla actual. | Simulating your draft, not the current rule. |
| `band.tests` | Los casos de prueba son ejemplos inventados. | Test cases are made-up examples. |
| `band.testsDraft` | Probando su borrador, no la regla actual. | Testing your draft, not the current rule. |
| `case.accept` | {result} · aceptar el nuevo resultado | {result} · accept the new result |
| `case.acceptPlain` | Aceptar el nuevo resultado | Accept the new result |
| `case.apply` | Aplicar a mi borrador | Apply to my draft |
| `case.ask` | ¿Cuál es el resultado correcto para este caso? | What is the correct result for this case? |
| `case.cancel` | Cancelar | Cancel |
| `case.change` | Cambiar lo que se espera | Change what is expected |
| `case.changes` | Cambia | Changes |
| `case.diff` | {field}: antes {a}, ahora {b} | {field}: before {a}, now {b} |
| `case.currentGives` | La regla actual da esto. | The current rule gives this. |
| `case.decidesChanged` | Decide la fila {n}, que usted cambió. | Row {n} decides, which you changed. |
| `case.editTitle` | Cambiar lo que se espera del caso {name} | Change what the case {name} expects |
| `case.expectChanged` | Resultado esperado cambiado | Expected result changed |
| `case.expected` | Resultado que se espera | Expected result |
| `case.expectedRequired` | Elija el resultado que se espera. | Choose the expected result. |
| `case.moreFor` | Más acciones del caso {name} | More actions for the case {name} |
| `case.name` | Nombre del caso | Case name |
| `case.nameRequired` | Escriba un nombre para el caso. | Enter a name for the case. |
| `case.nameTaken` | Ya hay un caso con ese nombre. | A case with that name already exists. |
| `case.noResult` | sin resultado | no result |
| `case.noRowDecides` | Ninguna fila decide. | No row decides. |
| `case.pick` | Elija un resultado… | Choose a result… |
| `case.remove` | Quitar el caso | Remove the case |
| `case.restoreFor` | Restaurar el caso {name} | Restore the case {name} |
| `case.result` | Resultado | Result |
| `case.reviewMine` | {result} · revisar mi cambio | {result} · review my change |
| `case.reviewPlain` | Revisar mi cambio | Review my change |
| `case.save` | Guardar caso | Save case |
| `case.tagNew` | Nuevo | New |
| `case.tagRemoved` | Quitado | Removed |
| `case.undoFor` | Deshacer la decisión sobre el caso {name} | Undo the decision on the case {name} |
| `changes.card` | Enviar la propuesta | Send the proposal |
| `changes.cases` | Casos que cambian de resultado | Test cases that change result |
| `changes.defaultRow` | Fila de respaldo | Fallback row |
| `changes.export` | Exportar la propuesta | Export the proposal |
| `changes.held.one` | Decida qué hacer con {n} caso que cambió de resultado antes de exportar. | Decide what to do with {n} case that changed result before exporting. |
| `changes.held.other` | Decida qué hacer con {n} casos que cambiaron de resultado antes de exportar. | Decide what to do with {n} cases that changed result before exporting. |
| `changes.nameRequired` | Escriba su nombre para firmar la propuesta. | Enter your name to sign the proposal. |
| `changes.named` | Antes no tenía nombre. Ahora se llama «{b}». | It had no name. It is now named "{b}". |
| `changes.noCases` | Ningún caso de prueba cambia de resultado. | No test case changes result. |
| `changes.nothing` | Su borrador no tiene cambios. | Your draft has no changes. |
| `changes.now` | Ahora: | Now: |
| `changes.reason` | Motivo del cambio | Reason for the change |
| `changes.reasonRequired` | Escriba el motivo del cambio. | Write the reason for the change. |
| `changes.renamed` | Antes se llamaba «{a}». Ahora se llama «{b}». | It was named "{a}". It is now named "{b}". |
| `changes.rows` | Filas que cambian | Rows that change |
| `changes.saved` | Propuesta guardada en {place}. Su borrador sigue aquí; si lo cambia, exporte de nuevo. | Proposal saved to {place}. Your draft is still here; if you change it, export again. |
| `changes.savedNoPlace` | Propuesta guardada. Su borrador sigue aquí; si lo cambia, exporte de nuevo. | Proposal saved. Your draft is still here; if you change it, export again. |
| `changes.th.before` | Antes | Before |
| `changes.th.decision` | Su decisión | Your decision |
| `changes.th.now` | Ahora | Now |
| `changes.undecided` | Sin decidir | Not decided |
| `changes.unnamed` | Antes se llamaba «{a}». Ahora no tiene nombre. | It was named "{a}". It now has no name. |
| `changes.warnings` | Avisos | Warnings |
| `cond.met` | Se cumple | Met |
| `cond.notMet` | No se cumple | Not met |
| `discard.body` | Se pierden {rows} y {cases}. La regla actual no cambia. | {rows} and {cases} are lost. The current rule does not change. |
| `discard.cases.one` | {n} decisión sobre casos de prueba | {n} test case decision |
| `discard.cases.other` | {n} decisiones sobre casos de prueba | {n} test case decisions |
| `discard.confirm` | Descartar el borrador | Discard the draft |
| `discard.keep` | Seguir editando | Keep editing |
| `discard.rows.one` | {n} cambio de fila | {n} row change |
| `discard.rows.other` | {n} cambios de filas | {n} row changes |
| `discard.title` | ¿Descartar su borrador de esta regla? | Discard your draft of this rule? |
| `draft.bar` | **Está viendo su borrador.** Nada de esto está en uso: la regla actual sigue igual hasta que se apruebe e instale una versión nueva. | **You are viewing your draft.** None of this is in use: the current rule stays as it is until a new version is approved and installed. |
| `draft.barRule.one` | **Está viendo la regla actual.** Su borrador tiene {n} cambio. | **You are viewing the current rule.** Your draft has {n} change. |
| `draft.barRule.other` | **Está viendo la regla actual.** Su borrador tiene {n} cambios. | **You are viewing the current rule.** Your draft has {n} changes. |
| `draft.changedAfterExport` | Cambió su borrador después de exportarlo. | You changed your draft after exporting it. |
| `draft.discard` | Descartar mi borrador | Discard my draft |
| `draft.inList` | con cambios propuestos | with proposed changes |
| `draft.stale` | **La regla cambió desde que empezó este borrador.** Exporte lo que tiene o descártelo. | **The rule changed since you started this draft.** Export what you have or discard it. |
| `draft.start` | Proponer cambios | Propose changes |
| `draft.switch` | Qué mostrar | What to show |
| `draft.unreadable.fix` | Corrija el borrador para continuar. | Fix the draft to continue. |
| `draft.unreadable.title` | Este borrador no se puede leer | This draft cannot be read |
| `draft.unreadable.undo` | Deshacer el último cambio | Undo the last change |
| `draft.view.draft` | Su borrador | Your draft |
| `draft.view.rule` | Regla actual | Current rule |
| `edit.incomplete` | Con este cambio hay datos para los que ninguna fila decide. | With this change, some data is decided by no row. |
| `edit.noRows` | Esta regla todavía no tiene filas. | This rule has no rows yet. |
| `edit.unreachable` | La fila {n} nunca decidirá: una fila anterior siempre se cumple primero. | Row {n} will never decide: an earlier row always matches first. |
| `err.generic` | Revise este dato. | Check this value. |
| `err.notAllowed` | Elija una de las opciones. | Choose one of the options. |
| `err.notBoolean` | Elija Sí o No. | Choose Yes or No. |
| `err.notDate` | Elija una fecha. | Choose a date. |
| `err.notDecimal` | Escriba un número, por ejemplo 7.5. | Enter a number, for example 7.5. |
| `err.notInteger` | Escriba un número entero, por ejemplo 12. | Enter a whole number, for example 12. |
| `err.notMoney` | Escriba un monto y una moneda de tres letras, por ejemplo 12.50 USD. | Enter an amount and a three-letter currency, for example 12.50 USD. |
| `err.required` | Falta este dato. | This is required. |
| `export.proposal.explain` | Se guarda un archivo en este equipo. Usted lo envía a quien mantiene las reglas. Exportar no cambia, no aprueba y no activa la regla. | A file is saved on this computer. You send it to the rule's maintainer. Exporting does not change, approve or activate the rule. |
| `fail.count.one` | {n} falla | {n} fails |
| `fail.count.other` | {n} fallan | {n} fail |
| `form.and` | y | and |
| `form.apply` | Aplicar a mi borrador | Apply to my draft |
| `form.cancel` | Cancelar | Cancel |
| `form.chipAdd` | Agregar | Add |
| `form.chipLabel` | {fact}: agregar un valor | {fact}: add a value |
| `form.chipRemove` | Quitar {value} | Remove {value} |
| `form.errMissing` | Falta el valor de {fact}. | The value of {fact} is missing. |
| `form.errNeedRow` | La regla necesita al menos una fila. | The rule needs at least one row. |
| `form.errReversed` | El primer valor no puede ser mayor que el segundo. | The first value cannot be greater than the second. |
| `form.from` | desde | from |
| `form.name` | Nombre de la fila | Row name |
| `form.noValue` | Sin valor | No value |
| `form.opLabel` | {fact}: condición | {fact}: condition |
| `form.optional` | Opcional | Optional |
| `form.other` | Aplique o cancele la fila {n} antes de editar otra. | Apply or cancel row {n} before editing another one. |
| `form.otherDefault` | Aplique o cancele la fila de respaldo antes de editar otra. | Apply or cancel the fallback row before editing another one. |
| `form.otherNew` | Aplique o cancele la fila nueva antes de editar otra. | Apply or cancel the new row before editing another one. |
| `form.preview` | Así se lee | It reads |
| `form.then` | Entonces | Then |
| `form.titleDefault` | Editar la fila de respaldo | Edit the fallback row |
| `form.titleEdit` | Editar la fila {n} | Edit row {n} |
| `form.titleNew` | Fila nueva | New row |
| `form.to` | hasta | to |
| `form.unused.one` | Mostrar el dato que esta fila no usa | Show the fact this row does not use |
| `form.unused.other` | Mostrar los {n} datos que esta fila no usa | Show the {n} facts this row does not use |
| `form.valLabel` | {fact}: valor | {fact}: value |
| `form.when` | Cuando | When |
| `hint.code` | Texto. | Text. |
| `hint.decimal` | Número, con punto decimal. | Number, with a decimal point. |
| `hint.integer` | Número entero. | Whole number. |
| `hint.money` | Monto y moneda. | Amount and currency. |
| `hint.moneyUsd` | Monto en dólares. | Amount in dollars. |
| `hint.required` | Obligatorio. | Required. |
| `list.aria` | Lista de reglas | Rule list |
| `list.casesFail.one` | {f} de {t} casos falla | {f} of {t} cases fails |
| `list.casesFail.other` | {f} de {t} casos fallan | {f} of {t} cases fail |
| `list.casesFail.single` | el caso falla | the case fails |
| `list.casesPass.one` | {p} de {t} caso pasa | {p} of {t} case passes |
| `list.casesPass.other` | {p} de {t} casos pasan | {p} of {t} cases pass |
| `list.count.approved.one` | {n} aprobada | {n} approved |
| `list.count.approved.other` | {n} aprobadas | {n} approved |
| `list.count.drafts.one` | {n} en borrador | {n} draft |
| `list.count.drafts.other` | {n} en borrador | {n} drafts |
| `list.count.rules.one` | {n} regla | {n} rule |
| `list.count.rules.other` | {n} reglas | {n} rules |
| `list.empty` | No hay reglas cargadas en este equipo. | No rules are loaded on this computer. |
| `list.noCases` | sin casos de prueba | no test cases |
| `list.review.done` | revisada | reviewed |
| `list.review.fixes.one` | {n} por corregir | {n} to fix |
| `list.review.fixes.other` | {n} por corregir | {n} to fix |
| `list.review.none` | sin revisar | not reviewed |
| `list.review.partial` | revisión en curso | review in progress |
| `list.review.questions.one` | {n} pregunta | {n} question |
| `list.review.questions.other` | {n} preguntas | {n} questions |
| `list.rows.one` | {n} fila | {n} row |
| `list.rows.other` | {n} filas | {n} rows |
| `list.title` | Reglas | Rules |
| `list.unreadable` | No se pudo leer | Could not be read |
| `load.failedBody` | Esta regla no se pudo leer en este equipo. | This rule could not be read on this computer. |
| `load.loading` | Cargando reglas… | Loading rules… |
| `load.untitled` | Regla sin título | Untitled rule |
| `notice.privacy` | **No identifique a personas reales.** No escriba nombres, fechas de nacimiento, números de identificación ni otros datos de una persona real en las notas ni en los nombres de los casos. Use ejemplos inventados. | **Do not identify real people.** Do not write names, dates of birth, identification numbers or any other detail of a real person in notes or case names. Use made-up examples. |
| `notice.privacyReason` | **No identifique a personas reales.** No escriba nombres, fechas de nacimiento, números de identificación ni otros datos de una persona real en el motivo ni en los nombres de los casos. Use ejemplos inventados. | **Do not identify real people.** Do not write names, dates of birth, identification numbers or any other detail of a real person in the reason or in case names. Use made-up examples. |
| `op.any` | cualquier valor | any value |
| `op.between` | está entre | is between |
| `op.eq` | es | is |
| `op.gt` | es mayor que | is more than |
| `op.in` | es uno de | is one of |
| `op.isnull` | no tiene valor | has no value |
| `op.lt` | es menor que | is less than |
| `op.max` | es como máximo | is at most |
| `op.min` | es como mínimo | is at least |
| `op.neq` | no es | is not |
| `op.nin` | no es ninguno de | is none of |
| `op.notnull` | tiene valor | has a value |
| `op.starts` | empieza por | starts with |
| `panel.version` | Versión {n} | Version {n} |
| `pill.draft` | Borrador de cambios | Draft changes |
| `pill.simulation` | Simulación | Simulation |
| `read.casesNotice.one` | **Con sus cambios, {n} caso de prueba da otro resultado.** Decida qué hacer con él en Casos de prueba. | **With your changes, {n} test case gives another result.** Decide what to do with it in Test cases. |
| `read.casesNotice.other` | **Con sus cambios, {n} casos de prueba dan otro resultado.** Decida qué hacer con ellos en Casos de prueba. | **With your changes, {n} test cases give another result.** Decide what to do with them in Test cases. |
| `read.casesSee.one` | Ver el caso | See the case |
| `read.casesSee.other` | Ver los casos | See the cases |
| `read.facts` | Datos que usa | Data it uses |
| `read.lead.collect` | Se aplican todas las filas que se cumplen. | Every row that matches applies. |
| `read.lead.first` | Esta regla decide en este orden. Gana la primera fila que se cumple. | This rule decides in this order. The first row that matches wins. |
| `read.lead.unique` | Solo una fila puede cumplirse a la vez. | Only one row can match at a time. |
| `read.noDefaultRow` | En cualquier otro caso, la regla no decide. | Otherwise, the rule does not decide. |
| `read.optional` | opcional | optional |
| `read.required` | obligatorio | required |
| `read.rowTextPrefix` | Texto que se muestra: | Text shown: |
| `read.type.boolean` | Sí o no | Yes or no |
| `read.type.code` | Texto | Text |
| `read.type.date` | Fecha | Date |
| `read.type.decimal` | Número | Number |
| `read.type.enum` | Una opción | One option |
| `read.type.integer` | Número entero | Whole number |
| `read.type.money` | Monto | Amount |
| `read.type.reference` | Referencia | Reference |
| `refuse.addButton` | Agregar a la revisión | Add to the review |
| `refuse.addToReview` | Anótelo en la revisión de esta regla. | Note it in the review of this rule. |
| `refuse.composition` | Esta regla depende de otras que no se pudieron calcular aquí. | This rule depends on others that could not be computed here. |
| `refuse.edit.caseData` | Los datos del caso «{case}» no sirven para esta regla: revise {fact}. | The data of the case "{case}" does not fit this rule: check {fact}. |
| `refuse.edit.caseOutcome` | Al caso «{case}» le falta el resultado esperado. | The case "{case}" has no expected result. |
| `refuse.edit.caseRow` | El caso «{case}» esperaba una fila que ya no existe. Cambie lo que se espera o quite el caso. | The case "{case}" expected a row that no longer exists. Change what it expects or remove the case. |
| `refuse.edit.cellType` | El valor de {fact} no tiene la forma que la regla espera. | The value of {fact} is not in the form the rule expects. |
| `refuse.edit.currency` | Los montos de {fact} deben estar en la misma moneda. | The amounts of {fact} must be in the same currency. |
| `refuse.edit.enum` | {value} no es una opción de {fact}. | {value} is not an option of {fact}. |
| `refuse.edit.internal` | No se pudo aplicar el cambio por un problema interno, no por lo que usted escribió. Cancele y avise a quien le da soporte. | The change could not be applied because of an internal problem, not because of what you entered. Cancel and tell your support contact. |
| `refuse.edit.operator` | «{operator}» no se puede usar con {fact}. Elija otra condición. | "{operator}" cannot be used with {fact}. Choose another condition. |
| `refuse.edit.outcomeType` | El resultado «{field}» no tiene la forma que la regla espera. | The outcome "{field}" is not in the form the rule expects. |
| `refuse.edit.overlap` | Las filas {n} y {m} pueden cumplirse a la vez y esta regla solo permite una. | Rows {n} and {m} can match at once and this rule allows only one. |
| `refuse.edit.rangeType` | Los dos valores de {fact} deben ser del mismo tipo. | The two values of {fact} must be of the same kind. |
| `refuse.edit.required` | Falta el resultado «{field}» en la fila {n}. | The outcome "{field}" is missing in row {n}. |
| `refuse.edit.stays` | Su borrador sigue como estaba. Corrija el dato marcado o cancele. | Your draft stays as it was. Correct the marked value or cancel. |
| `refuse.edit.title` | Este cambio no se puede aplicar | This change cannot be applied |
| `refuse.fixData` | Corrija el dato marcado y simule de nuevo. | Correct the marked value and simulate again. |
| `refuse.intro` | El motor de reglas no aceptó estos datos, así que no hay resultado. | The rule engine did not accept this data, so there is no result. |
| `refuse.missing` | Falta un dato obligatorio: {fact}. | A required value is missing: {fact}. |
| `refuse.multipleMatches` | Con estos datos se cumplen dos filas a la vez y la regla solo permite una. Esto es un defecto de la regla. | Two rows match this data and the rule allows only one. This is a defect in the rule. |
| `refuse.other` | El motor no pudo evaluar la regla con estos datos. | The engine could not evaluate the rule with this data. |
| `refuse.title` | No se pudo simular | Could not simulate |
| `refuse.tooLarge` | Los datos son demasiado largos para esta regla. | The data is too long for this rule. |
| `refuse.typeMismatch` | El dato {fact} no tiene la forma que la regla espera. | The value {fact} is not in the form the rule expects. |
| `result.decidedRow` | Decidió la fila {n}. | Row {n} decided. |
| `result.default` | Decidió el caso por defecto. | The default case decided. |
| `result.none` | La regla no decide | The rule does not decide |
| `review.addNote` | Agregar una nota | Add a note |
| `review.cardTitle` | Enviar la revisión | Send the review |
| `review.case` | Caso «{name}» | Case «{name}» |
| `review.confirm` | Confirmar | Confirm |
| `review.confirmed` | Confirmada | Confirmed |
| `review.counter` | {n} de {max} | {n} of {max} |
| `review.empty` | Todavía no ha revisado esta regla. Empiece en Leer. | You have not reviewed this rule yet. Start in Read. |
| `review.export` | Exportar la revisión | Export the review |
| `review.exportHelp` | Se guarda un archivo en este equipo. Usted lo envía a quien mantiene las reglas. Exportar no cambia ni aprueba la regla. | A file is saved on this computer. You send it to the rule's maintainer. Exporting does not change or approve the rule. |
| `review.fix` | Corregir | Fix |
| `review.fixState` | Corregir | Fix |
| `review.goRead` | Ir a leer | Go to Read |
| `review.groupCase` | Revisión del caso {name} | Review of the case {name} |
| `review.groupRow` | Revisión de la fila {n} | Review of row {n} |
| `review.groupRule` | Revisión de la regla completa | Review of the whole rule |
| `review.name` | Su nombre | Your name |
| `review.nameRequired` | Escriba su nombre para firmar la revisión. | Enter your name to sign the review. |
| `review.noNote` | Sin nota | No note |
| `review.noteLabel` | Nota para quien mantiene la regla | Note for the rule's maintainer |
| `review.noteMissing` | Falta la nota | Note missing |
| `review.noteShort` | Nota | Note |
| `review.notesNeeded` | Escriba la nota de {n} elementos antes de exportar. | Write the note for {n} items before exporting. |
| `review.nothingToExport` | No hay nada que exportar. | There is nothing to export. |
| `review.proposed` | Caso propuesto | Proposed case |
| `review.proposedNote` | Se espera {expected}. Hoy la regla da {actual}. | Expected: {expected}. Today the rule gives: {actual}. |
| `review.question` | Pregunta | Question |
| `review.questionState` | Pregunta | Question |
| `review.rowsUnreviewed.one` | Fila {rows} | Row {rows} |
| `review.rowsUnreviewed.other` | Filas {rows} | Rows {rows} |
| `review.ruleTarget` | La regla completa | The whole rule |
| `review.saved` | Revisión guardada en {place}. | Review saved to {place}. |
| `review.savedNoPlace` | Revisión guardada. | Review saved. |
| `review.scope.one` | Hay 1 regla con revisión en esta sesión. | 1 rule has a review in this session. |
| `review.scope.other` | Hay {n} reglas con revisión en esta sesión. | {n} rules have a review in this session. |
| `review.summary.cases.one` | {n} caso propuesto | {n} proposed case |
| `review.summary.cases.other` | {n} casos propuestos | {n} proposed cases |
| `review.summary.fixes.one` | {n} por corregir | {n} to fix |
| `review.summary.fixes.other` | {n} por corregir | {n} to fix |
| `review.summary.questions.one` | {n} pregunta | {n} question |
| `review.summary.questions.other` | {n} preguntas | {n} questions |
| `review.summary.rows.one` | {n} de {t} fila revisada | {n} of {t} row reviewed |
| `review.summary.rows.other` | {n} de {t} filas revisadas | {n} of {t} rows reviewed |
| `review.th.about` | Sobre | About |
| `review.th.note` | Nota | Note |
| `review.th.state` | Estado | State |
| `review.title` | Su revisión de esta regla | Your review of this rule |
| `review.unreviewed` | Sin revisar | Not reviewed |
| `review.writeNote` | Escribir la nota | Write the note |
| `row.addBelow` | Agregar una fila debajo | Add a row below |
| `row.addEnd` | Agregar una fila al final | Add a row at the end |
| `row.addFirst` | Agregar la primera fila | Add the first row |
| `row.always` | Se cumple siempre. | Always met. |
| `row.before` | Antes: | Before: |
| `row.decides` | Decide | Decides |
| `row.down` | Bajar | Move down |
| `row.earlier` | Una fila anterior decidió primero. | An earlier row decided first. |
| `row.edit` | Editar | Edit |
| `row.editFor` | Editar la fila {n} | Edit row {n} |
| `row.label` | Fila {n} | Row {n} |
| `row.matchesNoDecide` | Se cumple, no decide | Matches, does not decide |
| `row.more` | Más | More |
| `row.moreFor` | Más acciones de la fila {n} | More actions for row {n} |
| `row.movedNow` | La fila {a} ahora es la fila {b}. | Row {a} is now row {b}. |
| `row.notApply` | No aplica | Does not apply |
| `row.remove` | Quitar la fila | Remove the row |
| `row.restore` | Restaurar | Restore |
| `row.restoreFor` | Restaurar la fila {n} | Restore row {n} |
| `row.undo` | Deshacer | Undo |
| `row.undoFor` | Deshacer los cambios de la fila {n} | Undo the changes of row {n} |
| `row.up` | Subir | Move up |
| `row.wasRow` | Antes era la fila {n}. | It was row {n}. |
| `sim.amount` | Monto | Amount |
| `sim.before` | Escriba los datos de ejemplo y pulse Simular. | Enter the example data and press Simulate. |
| `sim.clear` | Limpiar | Clear |
| `sim.composedNote` | Esta regla usa datos calculados por otras reglas, así que no hay explicación fila por fila. | This rule uses data computed by other rules, so there is no row-by-row explanation. |
| `sim.currency` | Moneda de tres letras | Three-letter currency |
| `sim.loadCase` | O cargue un caso de prueba | Or load a test case |
| `sim.loadCaseNone` | Elija un caso… | Choose a case… |
| `sim.no` | No | No |
| `sim.noDecision` | La regla no decide con estos datos | The rule does not decide with this data |
| `sim.noValue` | Sin dato | No value |
| `sim.notTyped` | Usted no puso este dato. | You did not enter this. |
| `sim.resultTitle` | Resultado: {values} | Result: {values} |
| `sim.rowByRow` | Fila por fila | Row by row |
| `sim.rowIf` | Si {conditions}… | If {conditions}… |
| `sim.run` | Simular | Simulate |
| `sim.saveAsCase` | Guardar como caso de prueba | Save as a test case |
| `sim.showAll` | Mostrar las {n} filas que no aplican | Show the {n} rows that do not apply |
| `sim.stale` | Los datos cambiaron. Simule de nuevo. | The data changed. Simulate again. |
| `sim.title` | Datos de ejemplo | Example data |
| `sim.typed` | Usted puso {value}. | You entered {value}. |
| `sim.yes` | Sí | Yes |
| `state.approved` | Aprobada | Approved |
| `state.approvedLine` | Aprobada. | Approved. |
| `state.draft` | Borrador | Draft |
| `state.draftLine` | Borrador: falta la aprobación. | Draft: approval is pending. |
| `state.elsewhere` | Las reglas no se aprueban ni se activan desde esta pantalla. | Rules are not approved or activated from this screen. |
| `tabs.casesChange.one` | {n} cambia | {n} changes |
| `tabs.casesChange.other` | {n} cambian | {n} change |
| `tabs.changes` | Cambios | Changes |
| `tabs.label` | Secciones de la regla | Rule sections |
| `tabs.read` | Leer | Read |
| `tabs.review` | Revisión | Review |
| `tabs.simulate` | Simular | Simulate |
| `tabs.tests` | Casos de prueba | Test cases |
| `tag.changed` | Cambiada | Changed |
| `tag.moved` | Movida | Moved |
| `tag.new` | Nueva | New |
| `tag.removed` | Quitada | Removed |
| `tests.add` | Agregar un caso | Add a case |
| `tests.allCovered` | Todas las filas tienen al menos un caso que pasa. | Every row has at least one passing case. |
| `tests.createFor` | Crear un caso para esta fila | Create a case for this row |
| `tests.empty` | Esta regla no tiene casos de prueba. Agregue el primero desde el simulador. | This rule has no test cases. Add the first one from the simulator. |
| `tests.fail` | Falla | Fails |
| `tests.failNote` | **Un caso que falla no es un error suyo.** Significa que la regla y lo que usted espera no coinciden. El caso viaja con su revisión para que se corrija la regla o el caso. | **A failing case is not your mistake.** It means the rule and what you expect do not agree. The case travels with your review so the rule or the case gets fixed. |
| `tests.openSim` | Ver en el simulador | Open in the simulator |
| `tests.pass` | Pasa | Passes |
| `tests.pass.one` | {p} de {t} caso pasa | {p} of {t} case passes |
| `tests.pass.other` | {p} de {t} casos pasan | {p} of {t} cases pass |
| `tests.proposed` | Propuesto por usted | Proposed by you |
| `tests.review` | Revisar | Review |
| `tests.runAll` | Probar todos | Run all |
| `tests.running` | Probando… | Running… |
| `tests.th.actual` | La regla da | The rule gives |
| `tests.th.case` | Caso | Case |
| `tests.th.data` | Datos | Data |
| `tests.th.draft` | Su borrador da | Your draft gives |
| `tests.th.expected` | Se espera | Expected |
| `tests.th.status` | Estado | Status |
| `tests.uncovered` | Filas sin caso de prueba | Rows with no test case |
| `warn.always` | Esta fila se cumple siempre. Las filas que están debajo nunca decidirán. | This row always matches. The rows below it will never decide. |
| `warn.same` | La fila {n} tiene las mismas condiciones. | Row {n} has the same conditions. |
| `why.always` | aplica en todos los casos | it applies in every case |
| `why.and` |  y  |  and  |
| `why.default` | Ninguna fila se cumplió, así que aplica el caso por defecto. | No row matched, so the default case applies. |
| `why.row` | Decidió la fila {n} porque {conditions}. | Row {n} decided because {conditions}. |
| `why.rows` | Decidieron las filas {rows}. | Rows {rows} decided. |

## Accessibility

- The tabs are a `tablist` of `tab` buttons with `aria-selected` and `aria-controls`, and the open panel is a `tabpanel`. Left and right arrows move between tabs and Up and Down move between rules in the list; focus is moved with element references, no script.
- Every input has a visible label and, when it has help or an error, `aria-describedby`; an invalid input has `aria-invalid`. Radio sets are `radiogroup`s named by their caption.
- A simulation that the engine refuses is a `role="alert"` block; a result is announced through an `aria-live="polite"` region; loading and the saved line are `role="status"`.
- The mark buttons (confirm, fix, question) are a button group with `aria-pressed`; state is never carried by colour alone (each status pill has text).
- Focus ring: 2 px with a 2 px offset in `--wb-focus`. Under `prefers-reduced-motion: reduce` the transitions of tabs, rules and buttons are off.
- Text size comes from the host's tokens, so zoom and larger text work; the layout has no fixed height.

## What the host must do itself

- **Leave guard.** The session lives in memory. When `Session.UnexportedRuleCount` or `Session.UnexportedDraftCount` is above zero, warn before the page or circuit goes away (for example with a navigation lock) and offer to export.
- **State and approval.** The component only draws `State`, `StateLine` and `SourceLine`. It never approves, activates or edits a rule; where the approval comes from, and what it means, is the host's.
- **Saving the export.** The component hands over the JSON; writing it to a file, a folder or a service is `OnExport`.
- **Loading.** Reading rule files, building `RuleLabels` for each locale, the `RuleText` and the `RowTexts`.
- **Locale and theme.** Switching language is the host passing another `Locale` (and the matching labels); the theme is the host's tokens.
- **Personal data.** The component shows the privacy notice, but it cannot tell whether a note holds personal data.

## Demo and screenshots

`samples/Synapbiz.Brain.Workbench.Demo` is a Blazor Server host with five synthetic rules; the intake rules offer guided editing (`LoadDraft`, and `OutcomeTexts` for the queue). Run it with `dotnet run -c Release --urls http://127.0.0.1:5219` (query parameters `theme=light|dark` and `locale=es|en`) and take the captures with `node screenshots.mjs` (Playwright installed on the machine; the script installs nothing). The demo is not packable.

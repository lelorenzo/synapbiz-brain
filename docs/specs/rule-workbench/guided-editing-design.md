# Design — Guided editing panels (W-C2)

Design input from the first consumer's UX review, 2026-11-23, kept here as the reference the panels are built and tested against. Section numbers (12.x) are those of the original handoff document; "section 2 to 5" and "section 4.x" refer to the panels already built (W-B, W-B2). Spanish copy is the default text of each string key; product words such as "en la clínica" are host overrides over the neutral default given in 12.7.


Scope: full rows, the draft held in memory, exported as a proposal file (owner decision, 2026-11-22).

### 12.1 Where editing starts: an explicit draft, not a pencil on every row

Reading stays the default and has no edit control. The panel header carries one secondary button, "Proponer cambios". Pressing it creates the draft (a copy of the rule as loaded) and switches the panel to the draft view. Reasons: the physician must never change something by accident while reviewing; the review controls and the edit controls would crowd the same row; and one visible mode makes "this is not the live rule" impossible to miss.

While a draft exists:

- **Draft bar**, directly under the panel header and above the tabs, on every tab, never dismissable (notice tone): the dashed pill "Borrador de cambios", the sentence of 12.7, and at the right a two-part switch "Regla actual / Su borrador". "Regla actual" shows the rule exactly as sections 2 to 5, read-only, with the review controls; "Su borrador" shows the draft with the edit controls and without the review controls.
- The header button becomes "Descartar mi borrador".
- The **Cambios** tab appears, with the count of changed, added, removed and moved rows.
- In the rule list, the third fact of the row reads "con cambios propuestos" in bold.
- Simular and Casos de prueba run on what the switch shows. On the draft their band reads "Simulando su borrador, no la regla actual." / "Probando su borrador, no la regla actual."

Mandatory slots (a host may reword, not remove): the draft bar, its pill, and the last sentence of the export explanation.

### 12.2 Row controls in the draft view (Leer)

Each row keeps number, sentence and optional name. At the right, in this order: the change tag when there is one (12.5), "Deshacer" when the row is changed, "Editar", and a menu button "Más" with: "Subir", "Bajar", "Agregar una fila debajo", "Quitar la fila". The default row has "Editar" only. Under the last row: "Agregar una fila al final"; a rule with no rows shows "Agregar la primera fila". Moving is by menu, never by dragging. "Subir" is disabled on row 1 and "Bajar" on the last row. Only one row form is open at a time; opening another asks nothing when the open form is untouched and otherwise shows "Aplique o cancele la fila {n} antes de editar otra." under the open form.

### 12.3 The row form

The form replaces the row in place (2 px accent border), titled "Editar la fila {n}" or "Fila nueva". Focus goes to its first control; on apply or cancel it returns to the row's "Editar".

**Cuando** (conditions): one line per fact, three columns `150px 190px 1fr`: the fact label, an operator select, the value input. The first operator option is always "cualquier valor", which means the row does not look at that fact and hides the value input. Facts the row does not use are folded behind "Mostrar los {n} datos que esta fila no usa" so a rule with eight facts shows only the lines that matter; a new row shows all facts.

| Operator in words (Spanish / English) | Value input |
| --- | --- |
| cualquier valor / any value | none |
| es / is | one input by type (12.3.1) |
| no es / is not | one input by type |
| es uno de / is one of | checkboxes of the fact's values, or a list of text chips for free text |
| no es ninguno de / is none of | same |
| está entre / is between | two inputs joined by "y" |
| es como mínimo / is at least · es mayor que / is more than | one input |
| es como máximo / is at most · es menor que / is less than | one input |
| empieza por / starts with | text input |
| tiene valor / has a value · no tiene valor / has no value | none |

Only the operators the library allows for that fact are listed.

**12.3.1 Inputs by type.** As in the simulator (section 4.3): select for a value list, Sí / No pair, whole number, number, native date, amount with currency select, text. Never a typed `true` or an ISO date.

**Entonces** (outcome): one line per outcome field: label and one input. A field with a value list is a select of the labelled values. A field whose values are texts the host knows (reason texts) is a select showing the full sentence, wide (`grid-column: 2 / 4`), so the physician picks the sentence the screen will show and never a code; writing a new sentence is not offered here, it is asked for in the export note. An optional field has the first option "Sin valor".

**Nombre de la fila**: optional text, 60 characters.

**Así se lee**: a live preview box (selected ground) with the row's sentence built from the form, updated on every change. It is the same sentence Leer will show.

Buttons: "Aplicar a mi borrador" (primary) and "Cancelar". Apply validates, applies the edits as one step, re-reads the draft through the loader and re-runs the test cases.

Warnings shown above the buttons, not blocking: every fact on "cualquier valor" under `first`: "Esta fila se cumple siempre. Las filas que están debajo nunca decidirán."; the same conditions as another row: "La fila {n} tiene las mismas condiciones.".

### 12.4 A change the loader refuses

The panel applies an edit to a candidate copy; the draft changes only if the loader reads the candidate. A refusal keeps the form open, marks the field when the reason names one, and shows a bad-tone box inside the form: title "Este cambio no se puede aplicar", the reason in plain words, then "Su borrador sigue como estaba. Corrija el dato marcado o cancele.". The draft is therefore never left unreadable by an edit.

If a draft is unreadable anyway (defensive state), the draft view shows a bad-tone box in place of the rows: "Este borrador no se puede leer", the reasons as a list, and the buttons "Deshacer el último cambio" and "Descartar mi borrador"; Simular, Casos de prueba and export are disabled with "Corrija el borrador para continuar.".

Reasons are sentences, never codes. Pattern: what is wrong, where. The library gives each refusal a code and its place (row number, fact, outcome field or test case); the form marks that field and shows the sentence. `{dato}`, `{campo}` and `{caso}` are labels, never identifiers.

**Checks the panel makes before applying** (not loader refusals; shown as field errors under the input, the box is not needed):

| Check | Spanish | English |
| --- | --- | --- |
| Range reversed | El primer valor no puede ser mayor que el segundo. | The first value cannot be greater than the second. |
| Operator needs a value and has none | Falta el valor de {dato}. | The value of {fact} is missing. |
| Removing the only row of a rule with no default row | La regla necesita al menos una fila. | The rule needs at least one row. |

Row names are labels and may repeat; there is no duplicate-name error.

**Loader refusals** (box "Este cambio no se puede aplicar"):

| Code | Spanish | English |
| --- | --- | --- |
| `decisions.condition.operator_not_allowed` | «{operador}» no se puede usar con {dato}. Elija otra condición. | "{operator}" cannot be used with {fact}. Choose another condition. |
| `decisions.condition.range_type_mismatch` | Los dos valores de {dato} deben ser del mismo tipo. | The two values of {fact} must be of the same kind. |
| `decisions.validation.cell_type_mismatch` | El valor de {dato} no tiene la forma que la regla espera. | The value of {fact} is not in the form the rule expects. |
| `decisions.validation.money_currency_mismatch` | Los montos de {dato} deben estar en la misma moneda. | The amounts of {fact} must be in the same currency. |
| `brain.fact.enum_not_allowed` | {valor} no es una opción de {dato}. | {value} is not an option of {fact}. |
| `brain.outcome.required_field` | Falta el resultado «{campo}» en la fila {n}. | The outcome "{field}" is missing in row {n}. |
| `brain.outcome.type_mismatch` | El resultado «{campo}» no tiene la forma que la regla espera. | The outcome "{field}" is not in the form the rule expects. |
| `brain.golden.unknown_row` | El caso «{caso}» esperaba una fila que ya no existe. Cambie lo que se espera o quite el caso. | The case "{case}" expected a row that no longer exists. Change what it expects or remove the case. |
| `decisions.facts.missing`, `decisions.facts.type_mismatch` (case) | Los datos del caso «{caso}» no sirven para esta regla: revise {dato}. | The data of the case "{case}" does not fit this rule: check {fact}. |
| `brain.rule.golden_outcome_missing` | Al caso «{caso}» le falta el resultado esperado. | The case "{case}" has no expected result. |
| `decisions.validation.unknown_fact`, `brain.outcome.unknown_field`, `decisions.validation.duplicate_row_key`, and every other code | No se pudo aplicar el cambio por un problema interno, no por lo que usted escribió. Cancele y avise a quien le da soporte. | The change could not be applied because of an internal problem, not because of what you entered. Cancel and tell your support contact. |

The last line groups the codes a form built from the library's own lists cannot cause through the physician's input; they are a defect of the panel or the host, so the sentence says so and does not send the physician looking for a mistake. For those, the way-out line under the box is omitted.

The three case refusals come from "Agregar un caso", "Cambiar lo que se espera" and "Quitar la fila"; when a removed row leaves cases behind, the box lists each case on its own line and the row is not removed until they are dealt with.

**Table validator findings** (agreed with the library thread):

| Finding | Effect | Spanish | English |
| --- | --- | --- | --- |
| `decisions.validation.overlap`, policy `unique` | blocks the apply, in the box | Las filas {n} y {m} pueden cumplirse a la vez y esta regla solo permite una. | Rows {n} and {m} can match at once and this rule allows only one. |
| `decisions.validation.unreachable` | warning above the buttons | La fila {n} nunca decidirá: una fila anterior siempre se cumple primero. | Row {n} will never decide: an earlier row always matches first. |
| `decisions.validation.incomplete` | warning above the buttons | Con este cambio hay datos para los que ninguna fila decide. | With this change, some data is decided by no row. |

Warnings that remain true after the apply stay visible in Leer as a notice-tone line on the row they concern (`unreachable`) or above the rows (`incomplete`), and are listed in Cambios under "Avisos" so they travel with the proposal's reason.

### 12.5 After a change

The row shows its new sentence, a word tag, and under the sentence the old one: "Antes: …" (secondary ink).

| Change | Tag (tone) | Row | Undo |
| --- | --- | --- | --- |
| Conditions, outcome or name changed | Cambiada (warn) | new sentence, "Antes: …" below | "Deshacer" restores the row as in the rule |
| Added | Nueva (ok) | sentence | "Quitar la fila" in "Más" |
| Removed | Quitada (neutral) | stays in place, struck through and dimmed, no number | "Restaurar" |
| Moved | Movida (neutral) | line "Antes era la fila {n}." | "Deshacer" returns it to its place |

A row that is both moved and changed shows both tags. After a move, a polite live region says "La fila {a} ahora es la fila {b}." and focus stays on the moved row's "Más".

When the test run after a change gives a different result for any case, a notice appears at the top of Leer: "**Con sus cambios, {n} casos de prueba dan otro resultado.** Decida qué hacer con ellos en Casos de prueba." with the button "Ver el caso" / "Ver los casos". The Casos de prueba tab pill turns to the bad tone and reads "{n} cambia(n)".

### 12.6 A test case whose result changed

In Casos de prueba on the draft, the fourth column is headed "Su borrador da". A case that passes on the rule and not on the draft is not called a failure: its pill reads "Cambia" (bad tone), "Se espera" carries the line "La regla actual da esto.", and "Su borrador da" shows the new result in bold with "Decide la fila {n}, que usted cambió." (or "Ninguna fila decide."). Under the row, a full-width line asks one question with the two answers as buttons that name the result, so the physician chooses a clinical answer, not a software action:

> **¿Cuál es el resultado correcto para este caso?** [ {nuevo resultado}: aceptar el nuevo resultado ] [ {resultado anterior}: revisar mi cambio ]

- **Aceptar el nuevo resultado** changes what the case expects. The row turns to "Pasa" with the neutral pill "Resultado esperado cambiado" and a small "Deshacer".
- **Revisar mi cambio** goes to Leer and puts focus on the changed row that explains the difference: the row that decides now if it is tagged, otherwise the row that decided before; if neither is tagged (a move), it opens Cambios. The physician undoes or edits there. It does not undo anything by itself, because a case can depend on several changes.

A case that already failed on the rule keeps the "Falla" treatment of section 2.3. In the draft view the review line of a case also offers "Cambiar lo que se espera" and "Quitar el caso"; a removed case stays struck through with "Restaurar". "Agregar un caso" works as in section 2.3 and tags the case "Nuevo".

Export is held while any case is in "Cambia": "Decida qué hacer con {n} casos que cambiaron de resultado antes de exportar.".

### 12.7 Draft markings and copy

| Element | Spanish | English |
| --- | --- | --- |
| Start button | Proponer cambios | Propose changes |
| Draft pill | Borrador de cambios | Draft changes |
| Draft bar, draft view | **Está viendo su borrador.** Nada de esto se usa en la clínica: la regla actual sigue igual hasta que se apruebe e instale una versión nueva. | **You are viewing your draft.** None of this is used in the clinic: the current rule stays as it is until a new version is approved and installed. |
| Draft bar, rule view | **Está viendo la regla actual.** Su borrador tiene {n} cambios. | **You are viewing the current rule.** Your draft has {n} changes. |
| View switch | Regla actual · Su borrador | Current rule · Your draft |
| List | con cambios propuestos | with proposed changes |
| Row controls | Editar · Más · Subir · Bajar · Agregar una fila debajo · Quitar la fila · Deshacer · Restaurar · Agregar una fila al final | Edit · More · Move up · Move down · Add a row below · Remove the row · Undo · Restore · Add a row at the end |
| Form | Editar la fila {n} · Fila nueva · Cuando · Entonces · Nombre de la fila · Opcional · Así se lee · Aplicar a mi borrador · Cancelar | Edit row {n} · New row · When · Then · Row name · Optional · It reads · Apply to my draft · Cancel |
| Tags | Cambiada · Nueva · Quitada · Movida · Antes: · Ahora: · Antes era la fila {n}. | Changed · New · Removed · Moved · Before: · Now: · It was row {n}. |
| Generic host default for "en la clínica" | Nada de esto está en uso | None of this is in use |

The rule's own state pill ("Borrador" / "Aprobada", section 5.1) stays in the header and means the rule's approval state; the physician's draft is always called "Borrador de cambios" or "su borrador" so the two are not confused.

### 12.8 Cambios tab and export

The privacy notice (section 4.1, with "en el motivo" in place of "en las notas") comes first. Left column: the summary sentence from the library ("Este borrador cambia 2 filas. No agrega ni quita ninguna."), then the changed rows in the new order, each with "Antes:" above "Ahora:" (stacked, never side by side), its tag and "Deshacer"; then "Casos que cambian de resultado", a table Caso, Antes, Ahora, Su decisión, or "Ningún caso de prueba cambia de resultado.". Right column (300 px), card "Enviar la propuesta": the counts, "Motivo del cambio" (required, 1000 characters), "Su nombre" (required), "Exportar la propuesta" (primary), and the explanation.

| Element | Spanish | English |
| --- | --- | --- |
| Card | Enviar la propuesta · Motivo del cambio · Su nombre · Exportar la propuesta | Send the proposal · Reason for the change · Your name · Export the proposal |
| Explanation | Se guarda un archivo en este equipo. Usted lo envía a quien mantiene las reglas. Exportar no cambia, no aprueba y no activa la regla. | A file is saved on this computer. You send it to the rule's maintainer. Exporting does not change, approve or activate the rule. |
| Missing reason | Escriba el motivo del cambio. | Write the reason for the change. |
| Nothing to export | Su borrador no tiene cambios. | Your draft has no changes. |
| Done | Propuesta guardada en {carpeta}. Su borrador sigue aquí; si lo cambia, exporte de nuevo. | Proposal saved to {folder}. Your draft is still here; if you change it, export again. |
| Changed after export (draft bar suffix) | Cambió su borrador después de exportarlo. | You changed your draft after exporting it. |

### 12.9 Discard, leaving, and a rule that changed underneath

- **Discard**: "Descartar mi borrador" opens a modal dialog (focus trapped, Escape keeps the draft). Title "¿Descartar su borrador de esta regla?"; body "Se pierden {n} cambios de filas y {m} decisiones sobre casos de prueba. La regla actual no cambia."; buttons "Descartar el borrador" (secondary) and "Seguir editando" (primary, default focus). English: "Discard your draft of this rule?" · "{n} row changes and {m} test case decisions are lost. The current rule does not change." · "Discard the draft" · "Keep editing". A draft with no changes is discarded without the dialog.
- **Leaving the application** with a draft that was never exported, or changed after export, uses the leave guard of section 4.2 with "Tiene un borrador sin exportar de {n} reglas."
- **The rule changed underneath** (the installed rule's content differs from the draft's base, for example after an update): the draft becomes read-only, and the draft bar reads "**La regla cambió desde que empezó este borrador.** Exporte lo que tiene o descártelo." / "**The rule changed since you started this draft.** Export what you have or discard it.". Export stays available because the proposal is bound to its base.

### 12.10 Accessibility and tokens

No new token: the draft bar uses the notice triple, tags use the pill tones, the form border is `--wb-accent`, the dialog scrim is the host's (`--wb-scrim`, the only addition). Each operator select is labelled "{dato}: condición" and each value input "{dato}: valor"; the two inputs of "está entre" are "desde" and "hasta". "Más" is a menu button (`aria-haspopup="menu"`, arrow keys, Escape). Tags and "Antes:" are text, so nothing depends on colour or on the strike-through alone. The preview box is `aria-live="polite"`. The refusal box is `role="alert"`.

### 12.11 Requests to the headless library

1. Edits applied as one step per form with a result that is either the new draft or the refusal reasons (12.4), so the draft is never left unreadable.
2. For each test case whose result differs between rule and draft: the deciding row before and after (12.6).
3. The loader's refusal codes, each with the row and the fact or field it concerns, so the form can mark the field.
4. For outcome fields backed by host texts: the list of allowed values, so the form can offer a select of sentences.

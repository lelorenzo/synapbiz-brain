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

`samples/Synapbiz.Brain.Workbench.Demo/wwwroot/demo.css` is a complete light and dark example. Every class of the component starts with `wb-`.

## String keys

Every fixed string has a key. `WorkbenchStrings.Keys` lists them in ordinal order and `WorkbenchStrings.Default(key, locale)` returns the default. `TextOverrides` replaces a text for the current locale only; keys the host does not name keep their default. In a text a `**bold**` pair renders as `strong`; nothing else is interpreted and every text is HTML-encoded. `{name}` placeholders are filled by the panel; keep the ones the default has. Keys ending in `.one` / `.other` are the singular and plural forms picked by the count `{n}`.

The four markings (`band.simulate`, `band.tests`, `pill.simulation`, `notice.privacy`) can be reworded but an empty or white-space override is ignored and the default is used.

| Key | Spanish | English |
| --- | --- | --- |
| `band.simulate` | Simulado con datos de ejemplo. No es un caso real y no cambia nada. | Simulated with example data. This is not a real case, and nothing changes. |
| `band.tests` | Los casos de prueba son ejemplos inventados. | Test cases are made-up examples. |
| `case.cancel` | Cancelar | Cancel |
| `case.expected` | Resultado que se espera | Expected result |
| `case.expectedRequired` | Elija el resultado que se espera. | Choose the expected result. |
| `case.name` | Nombre del caso | Case name |
| `case.nameRequired` | Escriba un nombre para el caso. | Enter a name for the case. |
| `case.nameTaken` | Ya hay un caso con ese nombre. | A case with that name already exists. |
| `case.pick` | Elija un resultado… | Choose a result… |
| `case.save` | Guardar caso | Save case |
| `cond.met` | Se cumple | Met |
| `cond.notMet` | No se cumple | Not met |
| `err.generic` | Revise este dato. | Check this value. |
| `err.notAllowed` | Elija una de las opciones. | Choose one of the options. |
| `err.notBoolean` | Elija Sí o No. | Choose Yes or No. |
| `err.notDate` | Elija una fecha. | Choose a date. |
| `err.notDecimal` | Escriba un número, por ejemplo 7.5. | Enter a number, for example 7.5. |
| `err.notInteger` | Escriba un número entero, por ejemplo 12. | Enter a whole number, for example 12. |
| `err.notMoney` | Escriba un monto y una moneda de tres letras, por ejemplo 12.50 USD. | Enter an amount and a three-letter currency, for example 12.50 USD. |
| `err.required` | Falta este dato. | This is required. |
| `fail.count.one` | {n} falla | {n} fails |
| `fail.count.other` | {n} fallan | {n} fail |
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
| `panel.version` | Versión {n} | Version {n} |
| `pill.simulation` | Simulación | Simulation |
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
| `row.always` | Se cumple siempre. | Always met. |
| `row.decides` | Decide | Decides |
| `row.earlier` | Una fila anterior decidió primero. | An earlier row decided first. |
| `row.label` | Fila {n} | Row {n} |
| `row.matchesNoDecide` | Se cumple, no decide | Matches, does not decide |
| `row.notApply` | No aplica | Does not apply |
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
| `tabs.label` | Secciones de la regla | Rule sections |
| `tabs.read` | Leer | Read |
| `tabs.review` | Revisión | Review |
| `tabs.simulate` | Simular | Simulate |
| `tabs.tests` | Casos de prueba | Test cases |
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
| `tests.th.expected` | Se espera | Expected |
| `tests.th.status` | Estado | Status |
| `tests.uncovered` | Filas sin caso de prueba | Rows with no test case |
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

- **Leave guard.** The session lives in memory. When `Session.UnexportedRuleCount` is above zero, warn before the page or circuit goes away (for example with a navigation lock) and offer to export.
- **State and approval.** The component only draws `State`, `StateLine` and `SourceLine`. It never approves, activates or edits a rule; where the approval comes from, and what it means, is the host's.
- **Saving the export.** The component hands over the JSON; writing it to a file, a folder or a service is `OnExport`.
- **Loading.** Reading rule files, building `RuleLabels` for each locale, the `RuleText` and the `RowTexts`.
- **Locale and theme.** Switching language is the host passing another `Locale` (and the matching labels); the theme is the host's tokens.
- **Personal data.** The component shows the privacy notice, but it cannot tell whether a note holds personal data.

## Demo and screenshots

`samples/Synapbiz.Brain.Workbench.Demo` is a Blazor Server host with five synthetic rules. Run it with `dotnet run -c Release --urls http://127.0.0.1:5219` (query parameters `theme=light|dark` and `locale=es|en`) and take the captures with `node screenshots.mjs` (Playwright installed on the machine; the script installs nothing). The demo is not packable.

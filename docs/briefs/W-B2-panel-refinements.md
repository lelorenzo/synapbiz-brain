# Brief W-B2 — Refinements of the workbench panels after the design review

Source of the changes: section 11 of `/mnt/project-files/app-local/reglas/reglas-handoff-spec.md` (read-only network mount: read that file by its full path, never search or list above its folder, write only into `/mnt/project-files/app-local/reglas/capturas/`). Context: `docs/briefs/W-B-rule-workbench-panels.md` (every rule of that brief still holds: product-neutral, no evaluation logic in the panels, tokens only, no suppression, no `InternalsVisibleTo`, no script, mandatory markings) and `docs/reference/rule-workbench-panels.md`.

This brief is self-contained and deterministic. You never choose between options and never widen scope. If something cannot be done as written, do the rest and report the point precisely.

## Repository, branch, environment

- `/home/user/synapbiz-brain`, local branch `wb-followup` (already checked out). Commit there. Do not push, do not touch GitHub, do not switch branches.
- Do not edit `.github/workflows/**`, `Synapbiz.Brain.Parity.sln`, `tools/**`, `tests/parity/**`, or anything under `src/Synapbiz.Brain.Contracts`, `.Engine`, `.Spec`, `.Process` or their tests. Never write in `/home/user/nuvora`.
- `export DOTNET_ROLL_FORWARD=Major DOTNET_CLI_TELEMETRY_OPTOUT=1`. Baseline on this branch: build 0 errors, 2 warnings (both in `OverlapAnalyzer.cs`); tests 22 + 167 + 226 + 306 + 519 + 201 passed.

## A. Headless library (`src/Synapbiz.Brain.Workbench`) — sentence grammar

The sentence templates stop being a verbatim copy of the source repository's generator; say so in the XML comment of the template class and in `docs/reference/rule-workbench.md`. No public signature changes.

1. **Lists.** A new internal helper joins items: one item as is; two items with the final joiner; three or more with `, ` between all but the last two and the final joiner before the last. No comma before the final joiner.
   - Conditions of a sentence and outcome parts of a sentence: final joiner ` y ` (Es) / ` and ` (En). Example: `Si A, B y C, entonces Ruta es escalado, Cola es Cola urgente y Avisar es sí.`
   - Values of `In`: final joiner ` o ` / ` or `. Values of `NotIn`: final joiner ` ni ` / ` or `.
2. **Templates changed** (all others unchanged):

   | Key | Spanish | English |
   | --- | --- | --- |
   | `operator.in` | `{fact} es {values}` | `{fact} is {values}` |
   | `operator.notIn` | `{fact} no es {values}` | `{fact} is not {values}` |
   | `operator.isNull` | `{fact} no tiene valor` | `{fact} has no value` |
   | `operator.isNotNull` | `{fact} tiene valor` | `{fact} has a value` |

3. Update every test that pins the old texts to the new texts (this is the one case where existing expectations in `tests/Synapbiz.Brain.Workbench.Tests` and `tests/Synapbiz.Brain.Workbench.Blazor.Tests` change; change only the expected strings, never remove an assertion). The three pinned readiness sentences become: `If score is between 0 and 49, then note is low-score.` (unchanged), `Si score está entre 0 y 49, entonces note es low-score.` (unchanged), `If stage is Review or Complete, then note is late-stage.`. Add tests for one, two, three and four items for each joiner in both locales.

## B. Panels (`src/Synapbiz.Brain.Workbench.Blazor`)

New public member, the only API change: `WorkbenchRule.HeadlineFields` — `IReadOnlyList<string>? { get; init; }`, the outcome field names the result heading shows, in order; null or empty → the first field of the kind's outcome fields that the decided outcome sets.

1. **Review control in the test table** (section 11.1). Each case row shows one small secondary button (key `tests.review`, "Revisar" / "Review") under the status pill. Pressing it opens, in a full-width line under that row, the three-part control and the note field. Once a state is set, the state pill replaces the button and toggles the same line. `aria-expanded` on the button or pill, `aria-controls` to the line.
2. **No repeated value** (11.2). The typed value is added only for conditions whose operator is `NotEquals`, `NotIn`, `Range`, `StartsWith` or `IsNotNull`. For `Equals`, `In` and `IsNull` the why-sentence uses the condition text alone, and in "Row by row" a condition that is met drops the "You entered …" sentence. A condition that is not met always keeps it (or the "did not enter" sentence).
3. **Any-value conditions** (11.3). A condition whose operator is `Any`, or a `Range` with no bound, is not listed in "Row by row" and not named in the why-sentence. A row with no other condition shows one line, key `row.always` ("Se cumple siempre." / "Always met."), and when such a row decides the why-sentence keeps the existing "applies in every case" wording.
4. **Headline and labelled values** (11.4). The result heading shows only the headline fields. In the heading, in the "expected" column and in the "the rule gives" column, a value of a `Boolean`, `Integer`, `Decimal` or `Money` outcome field is written `<field label>: <value>`; other values stand alone. Parts are joined with the list helper rule of A.1 using `, ` only (no final joiner). The definition list under the heading is unchanged.
5. **Money input** (11.6). One amount text input and a currency `<select>` 88 px wide. Options: `USD` plus every currency that appears in a `Money` operand of a condition on that fact in the rule's table, distinct, `USD` first then ordinal order; default `USD`. Hint key `hint.moneyUsd` ("Monto en dólares." / "Amount in dollars.") when `USD` is the only option, else `hint.money` ("Monto y moneda." / "Amount and currency."). No placeholder text. An empty amount is an empty fact whatever the select shows.
6. **Date input** (11.7). The `<input type="date">` carries `lang="es"` or `lang="en"` from the locale.
7. **Long rules** (11.8). "Row by row" always shows the deciding rows and every row that matched. When the rule has more than six rows (the default row counts), the rows that do not apply are folded behind the existing "show the {n} rows that do not apply" button; with six or fewer every row is shown. This replaces the threshold of 12.
8. **A fix or a question needs its note** (11.9). In the Review tab an item whose state is fix or question and whose note is empty or white space shows the warn-tone text key `review.noteMissing` ("Falta la nota" / "Note missing") and a button key `review.writeNote` ("Escribir la nota" / "Write the note") that opens that item's note field and focuses it. While any such item exists in any rule of the session, export does not call `OnExport` and shows key `review.notesNeeded` ("Escriba la nota de {n} elementos antes de exportar." / "Write the note for {n} items before exporting."). Confirmed needs no note.

Every new string is a key in `WorkbenchStrings` with both defaults. Update `docs/reference/rule-workbench-panels.md` (keys, `HeadlineFields`, the note rule, the money and date inputs) and add one entry to `CHANGELOG.md` under `Unreleased`.

## C. Tests

bUnit tests for each item of B, in both locales where text is involved: the review line in the test table (closed, open, state set, `aria-expanded`); the why-sentence and row lines for each operator, met and not met; an any-only row and a row mixing any with another condition; heading with default and with host headline fields; labelled boolean and number in heading and both table columns; money select options and defaults for a rule with one and with two currencies, hint per case, empty amount; `lang` on the date input; folding at exactly six and at seven rows, with matched rows always shown; missing-note marking, the write-note button, export held and the count message, export proceeding once notes exist. Extend the demo fixtures only if a case needs it, with synthetic product-neutral content. The identifier guard, the stylesheet tests and the mandatory-marking tests must still pass unchanged.

## D. Screenshots

Update `samples/Synapbiz.Brain.Workbench.Demo` where needed (give one demo rule `HeadlineFields`), run it and re-capture every picture of `screenshots.mjs` into `/mnt/project-files/app-local/reglas/capturas/` under the same names (overwrite), and add `wb-tests-review-light.png` (a case row with its review line open) and `wb-review-note-missing-light.png`. Look at each with the Read tool and check it against section 11.

## Gates

```bash
export DOTNET_ROLL_FORWARD=Major DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet build Synapbiz.Brain.sln -c Release --no-incremental 2>&1 | tee /tmp/wb2-build.log | tail -5
grep "warning" /tmp/wb2-build.log | grep -v OverlapAnalyzer      # must print nothing
dotnet test Synapbiz.Brain.sln -c Release --no-build
rm -rf /tmp/wb2-cov && dotnet test Synapbiz.Brain.sln -c Release --no-build --collect:"XPlat Code Coverage" --results-directory /tmp/wb2-cov
dotnet pack Synapbiz.Brain.sln -c Release --no-build -o /tmp/wb2-artifacts && ls /tmp/wb2-artifacts
bash tools/parity/check-source-drift.sh /home/user/nuvora | tail -1
git -C /home/user/nuvora status --porcelain | wc -l
```

Expected: 0 errors and only the 2 baseline warnings; every project green, the Process, Engine and Spec counts unchanged (519, 167, 226); line coverage at least 0.95 for `Synapbiz.Brain.Workbench` and 0.85 for `Synapbiz.Brain.Workbench.Blazor` (merge the per-project cobertura files by taking, for each package, the highest line-rate among the files that exercise it, or report covered/valid lines summed across files); six `.nupkg`; the drift guard's usual line; nuvora count 0.

## Commits and report

Small commits in English, each message ending with:

```
Co-Authored-By: Claude <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01VN2ND7Ahy1HAQ2DW9D8MUU
```

Report: commits, the tail of every gate, coverage, the new string keys, the screenshot paths, and every point where you could not follow this brief literally.

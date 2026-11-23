# Brief W-B — `Synapbiz.Brain.Workbench.Blazor`: the rule workbench panels

Design: `docs/specs/rule-workbench/spec.md` (packages), `docs/reference/rule-workbench.md` (the headless API you call), and the UX handoff for the first consumer's screen, which is the visual and behavioural source for this package:

- `/mnt/project-files/app-local/reglas/reglas-handoff-spec.md` (text; "the UX spec" below)
- `/mnt/project-files/app-local/reglas/reglas.html` (static markup and CSS of the five pictures; open the PNGs next to it with the Read tool to see them)

That folder is a read-only network mount: read those files by their full path, never search or list above that folder, never write there except the one screenshots folder named in Step 7.

This brief is self-contained and deterministic. You never choose between options and never widen scope. Where this brief and the UX spec differ, this brief wins (the differences are listed in Step 3). If anything cannot be done as written, do the rest and report the point precisely.

## Repository, branch, environment

- `/home/user/synapbiz-brain`, local branch `wb-panels` (already checked out, on top of the W-A work). Commit there. Do not push. Do not touch GitHub. Do not switch branches.
- Do not edit `.github/workflows/**`, `Synapbiz.Brain.Parity.sln`, `tools/**`, `tests/parity/**`, or anything under `src/Synapbiz.Brain.Contracts`, `src/Synapbiz.Brain.Engine`, `src/Synapbiz.Brain.Spec`. In `src/Synapbiz.Brain.Workbench` only the change of Step 1 is allowed. No `InternalsVisibleTo`. Never write in `/home/user/nuvora`.
- `export DOTNET_ROLL_FORWARD=Major DOTNET_CLI_TELEMETRY_OPTOUT=1` (SDK 10; projects target `net9.0`).
- Baseline on this branch: build 0 errors and 2 warnings (both in `OverlapAnalyzer.cs`); tests 13 + 167 + 226 + 299 passed.
- The library is shared by several products. Code, tests, fixtures, docs and default strings name no product, clinic, vendor or person and say nothing clinical. English for code and docs; the UI strings are Spanish and English.

## Step 1 — One addition to the headless library

`RuleLabels` gets `public Func<DecisionValue, string?>? ValueFormatter { get; init; }` (XML comment: a host formatter for values; return null to use the default text). `WorkbenchText.FormatValue` order: null value → empty; for `CodeValue` / `EnumValue` the value label when there is one; otherwise `labels.ValueFormatter(value)` when the delegate is set and returns non-null; otherwise the existing default. Everything that formats values (condition texts, sentences) already goes through `FormatValue`; verify and keep it so. Tests: a formatter that formats dates changes a sentence with a date range and leaves labelled enum values alone; a formatter returning null falls back. Update `docs/reference/rule-workbench.md`.

## Step 2 — Projects

- `src/Synapbiz.Brain.Workbench.Blazor/Synapbiz.Brain.Workbench.Blazor.csproj`: `Sdk="Microsoft.NET.Sdk.Razor"`, `net9.0`, `RootNamespace` / `AssemblyName` / `PackageId` = `Synapbiz.Brain.Workbench.Blazor`, `IsPackable` true, `Description` "Blazor panels of the rule workbench for brain.rule@1: rule list, reader, simulator, test cases and review, styled only by host-supplied tokens.", one package reference `Microsoft.AspNetCore.Components.Web` version `9.0.0` (add to `Directory.Packages.props` under a comment `<!-- workbench panels -->`), project references to the four libraries. Static asset `wwwroot/workbench.css`. No JavaScript file; never inject `IJSRuntime`.
- `tests/Synapbiz.Brain.Workbench.Blazor.Tests`: xunit, FluentAssertions, coverlet.collector, Microsoft.NET.Test.Sdk, xunit.runner.visualstudio as the other test projects, plus `bunit` `1.38.5` (add to `Directory.Packages.props`; if that exact version does not restore, use the newest 1.x that does and report it).
- `samples/Synapbiz.Brain.Workbench.Demo`: `Microsoft.NET.Sdk.Web`, `net9.0`, `IsPackable` false, a Blazor Server (interactive server) app with one page that mounts the panels (Step 7).
- Add the three projects to `Synapbiz.Brain.sln` (solution folders `src`, `tests`, new `samples`), and a project reference from the architecture tests to the new library.

## Step 3 — What to build

One root component and nothing else public except the types below. Namespace `Synapbiz.Brain.Workbench.Blazor`. XML comments on every public member.

```csharp
public enum RuleApprovalState { Draft = 0, Approved = 1 }
public enum WorkbenchTab { Read = 0, Simulate = 1, Tests = 2, Review = 3 }

/// One rule of the list, as the host supplies it.
public sealed record WorkbenchRule
{
    public required string Key { get; init; }                 // stable, unique, chosen by the host; never rendered
    public string? Group { get; init; }                       // list heading; rules keep the order given, a heading is drawn when Group changes
    public BrainRule? Rule { get; init; }                     // null: the rule could not be read
    public string? RuleText { get; init; }                    // the exact document text, for the review content hash
    public string? FallbackTitle { get; init; }               // title when Rule is null
    public RuleLabels Labels { get; init; } = RuleLabels.Empty;   // for the current locale
    public RuleApprovalState State { get; init; }
    public string? StateLine { get; init; }                   // host sentence under the title; null → the default line of the state
    public string? SourceLine { get; init; }                  // host text; null → not drawn
    public IReadOnlyDictionary<string, string> RowTexts { get; init; }   // row id → secondary line of the row; defaults to empty
}

/// In-memory state of one reviewer's session. The host owns its lifetime (one per user session). It reads and writes nothing outside memory.
public sealed class WorkbenchSession
{
    public string? SelectedRuleKey { get; set; }
    public WorkbenchTab SelectedTab { get; set; }
    public string Reviewer { get; set; }          // defaults to ""
    public int UnexportedRuleCount { get; }       // rules whose review items or proposed cases changed since the last export
    public event Action? Changed;                 // raised after any change
    public void Clear();
}

public sealed record WorkbenchExportFile(string SuggestedName, string Json, RuleReview Review);
public sealed record WorkbenchExport(IReadOnlyList<WorkbenchExportFile> Files);

public static class WorkbenchStrings
{
    public static IReadOnlyList<string> Keys { get; }
    public static string Default(string key, WorkbenchLocale locale);
}
```

`RuleWorkbench.razor` parameters:

| Parameter | Type | Meaning |
| --- | --- | --- |
| `Rules` | `IReadOnlyList<WorkbenchRule>?` | null = loading |
| `Locale` | `WorkbenchLocale` | language of fixed strings and of `RuleDescriber` |
| `Session` | `WorkbenchSession` (`EditorRequired`) | state |
| `Now` | `Func<DateTimeOffset>` (`EditorRequired`) | the host's clock; the library has none. Simulation `asOf` is its date |
| `OnExport` | `Func<WorkbenchExport, Task<string?>>?` | the host saves the files and returns the place to name in "saved to {place}"; null return → the sentence without a place; parameter null → the export button is not drawn |
| `TextOverrides` | `IReadOnlyDictionary<string, string>?` | replaces fixed strings by key for the current locale |

The session keeps, per rule key and internally: review state and note per target, proposed cases, simulator field texts and the last result. All of it survives switching rule or tab and re-rendering.

Build exactly the screen of the UX spec, sections 2 (layout; tabs Leer, Simular, Casos de prueba, Revisión — the "Cambios" tab of section 6 is NOT built), 3 (result explanation), 4 (copy), 5 (states) and 8 (accessibility), calling only the headless API (`RuleDescriber`, `FactInput`, `RuleSimulator`, `RuleTester`, `RuleReviewJson`, `WorkbenchText`). The panels contain no evaluation, matching or comparison logic of their own. Follow the markup structure of `reglas.html`.

Differences from the UX spec (this brief wins):

1. **Classes and tokens.** Every class is prefixed `wb-`; the wrapper element has class `wb-root`. `workbench.css` uses only the `--wb-*` custom properties of UX spec section 7 for every colour, font family, font size role and radius. It contains no colour literal (no `#hex`, `rgb(`, `hsl(`, colour names except `transparent` and `currentColor`), no font name and no fallback value in any `var()`. The library does not render host components.
2. **Neutral default strings.** Every fixed string of UX spec section 4 (and the state sentences of sections 2, 3 and 5) is a key in `WorkbenchStrings` with Spanish and English defaults, and `TextOverrides` replaces it. Name keys `area.name` in lower camel case (`tabs.read`, `sim.run`, ...). The defaults are the spec's texts, except these, whose defaults are neutral and whose keys are fixed:

   | Key | Spanish default | English default |
   | --- | --- | --- |
   | `band.simulate` | Simulado con datos de ejemplo. No es un caso real y no cambia nada. | Simulated with example data. This is not a real case, and nothing changes. |
   | `band.tests` | Los casos de prueba son ejemplos inventados. | Test cases are made-up examples. |
   | `notice.privacy` | **No identifique a personas reales.** No escriba nombres, fechas de nacimiento, números de identificación ni otros datos de una persona real en las notas ni en los nombres de los casos. Use ejemplos inventados. | **Do not identify real people.** Do not write names, dates of birth, identification numbers or any other detail of a real person in notes or case names. Use made-up examples. |
   | `state.draftLine` | Borrador: falta la aprobación. | Draft: approval is pending. |
   | `read.rowTextPrefix` | Texto que se muestra: | Text shown: |
   | `state.approvedLine` | Aprobada. | Approved. |
   | `load.failedBody` | Esta regla no se pudo leer en este equipo. | This rule could not be read on this computer. |

   A `**bold**` pair in a string renders as `<strong>`; nothing else is interpreted and all text is HTML-encoded.
3. **Hit-policy lead, row words, why-sentence.** As UX spec 2.1 and 3. The why-sentence is built from the selected row: for each passed condition that is not `Any`, take `ConditionView.Text`; when it starts with `ConditionView.FactLabel`, insert ` (<typed value>)` right after the label, otherwise append it; join with the locale's " y " / " and "; a selected row with no such condition reads "aplica en todos los casos" / "it applies in every case". Typed values are `WorkbenchText.FormatValue(check.Actual, factName, locale, labels)`.
4. **Inputs by fact type.** `Enum` with allowed values → select of labelled values (plus an empty option when nullable); `Boolean` → Sí/No pair (plus "sin dato" when nullable); `Date` → `<input type="date">`; `Integer`, `Decimal`, `Code` → text; `Money` → amount text plus a three-letter currency text, joined as `FactInput` expects; `Reference` facts and composed facts are not shown. Values go through `FactInput.ParseAll`; its error codes map to the messages of UX spec 4.3; engine codes to 4.4.
5. **Review targets.** `rule` for the whole rule, `row:<row id>` for a row, `golden:<name>` for a case. An unreviewed target with an empty note is not exported.
6. **Export.** One file per rule that has at least one exported item or proposed case, across all rules of the session, in list order: `SuggestedName` = `review-<rule id>-v<version>.json`, `Json` = `RuleReviewJson.Serialize`, content hash from `RuleText`, `CreatedAt` = `Now()`, reviewer = the name field (blank → the field error, no export). After `OnExport` completes, `UnexportedRuleCount` is 0. A rule with a non-null `Rule` and a null `RuleText` is treated as a rule that could not be read. The leave-guard dialog of UX spec 4.2 is the host's; it is not built here.
7. **No identifiers on screen.** Rule ids, row keys, row ids, kind keys, fact names without a label go through `Humanize`; error and failure codes are never rendered.

8. **Mandatory markings.** The simulation band (`band.simulate`, `band.tests`), the simulation pill (fixed key `pill.simulation`, defaults "Simulación" / "Simulation") and the privacy notice (`notice.privacy`) are always rendered. A host may reword them; an override of any of these four keys that is null, empty or white space is ignored and the default is used. No parameter hides them. Tests cover this.
9. **Buttons.** Primary: rest on `--wb-accent` with `--wb-accent-ink`; hover on `--wb-accent-hover` with the same ink. Secondary: hover on `--wb-hover` with `--wb-ink`. Focus ring 2 px `--wb-focus` at 2 px offset on every interactive element. The stylesheet has no generic `button:hover` (or any element-level hover) rule that a variant must out-rank: each variant class states its own rest, hover, pressed and disabled colours, and a stylesheet test asserts that no selector ending in `button:hover` or `.wb-btn:hover` without a variant class sets `background` or `color`.

## Step 4 — Tests (bUnit, `tests/Synapbiz.Brain.Workbench.Blazor.Tests`)

Use the fixtures of `tests/fixtures/workbench` and `tests/fixtures/brain-spec/readiness.*`, with labels from `meta.display` plus labels added in the tests so every value has one. At least:

- list: groups, counts line, state pills, failing-tests summary in bold, selection with `aria-current`, Up/Down keys, loading (null), empty, a rule that could not be read (and one with `RuleText` null);
- tabs: roles `tablist` / `tab` / `tabpanel`, `aria-selected`, the tab is kept when the rule changes and the rule when the tab changes, count pills;
- Read: hit-policy lead for the three policies, every sentence equals `RuleDescriber`'s, default row block, `RowTexts` line with its prefix, "Datos que usa" list, review control (`aria-pressed`, toggle back to unreviewed, note opens for fix and question), state kept in the session;
- Simulate: each input kind; each `workbench.fact.*` message with `aria-describedby` and no result drawn; a decided result with heading, why-sentence (exact strings in both locales), outcome list and the three row words; `collect` with two deciding rows; default-row win; no decision with no default; each engine refusal text with `role="alert"` and no row list; composed-facts note; stale result after a field change; load a case into the fields; more than 12 rows collapses the rows that do not apply;
- Tests tab: summary line, table with a failing case and a refused case, "proposed by you" pill, uncovered rows and the all-covered sentence, add-case form (from scratch, from the simulator, for an uncovered row), run all;
- Review: notice always present, whole-rule control, table of reviewed items, unreviewed rows collapsed in one line, export disabled with nothing reviewed, name error, export payload (every `Json` parses with `RuleReviewJson.Parse`, the hash equals `RuleReviewJson.ContentHash(RuleText)`, targets and proposed cases are right), "saved to" sentence with and without a place, `UnexportedRuleCount` before and after;
- strings: every key has both defaults, an override replaces the text, the seven fixed keys exist with exactly the defaults above, `**bold**` renders `<strong>` and markup in a note or name is encoded;
- no-identifier guard: for every fixture and every tab, in both locales, the rendered markup contains no rule id, row key, row id, kind key, `workbench.` or `decisions.` text;
- stylesheet: no colour literal, no font name, no `var(` with a fallback, and every `--wb-*` it reads is in the list of UX spec section 7;
- Step 1 tests in `tests/Synapbiz.Brain.Workbench.Tests`.

Never weaken, skip or delete an existing test.

## Step 5 — Architecture tests

- New `WorkbenchBlazorAssemblyCouplingTests`: the assembly references only the base class library, the four Brain libraries and assemblies whose name starts with `Microsoft.AspNetCore.Components`, `Microsoft.JSInterop` or `Microsoft.Extensions.`; the project declares exactly one `PackageReference` (`Microsoft.AspNetCore.Components.Web`); no type of the assembly has a field, property or constructor parameter of type `IJSRuntime`; the package ships no `.js` file (scan `wwwroot`).
- Add the new assembly to the purity scan of `LibraryPurityArchTests` (no clock, no IO, no network, no randomness). No exemption may be added. If generated Razor code is flagged and cannot be changed, stop and report.

## Step 6 — Docs

`docs/reference/rule-workbench-panels.md`: how a host mounts `RuleWorkbench` (parameters, session lifetime, export, clock), the `--wb-*` token contract, the list of string keys with their defaults, accessibility notes, and what the host must do itself (leave guard, where the state and approval of a rule come from, saving the export). Update `docs/architecture/overview.md`, `README.md` (five packages), `CHANGELOG.md` (`Unreleased`), `docs/consuming.md`, `docs/specs/rule-workbench/tasks.md` (W-B items) and `plan.md`. Product-neutral, synthetic examples.

## Step 7 — Demo host and screenshots

`samples/Synapbiz.Brain.Workbench.Demo`: loads the fixture rules (two approved, the rest draft, grouped under two headings, one entry that could not be read), full Spanish and English labels, a scoped `WorkbenchSession`, an `OnExport` that returns a fixed place text without writing anything, and a stylesheet that sets the `--wb-*` properties from the `:root` (light) and dark token values found in `reglas.html`. Query string: `?theme=light|dark&locale=es|en`. The demo holds its palette; the library holds none.

Run it (`dotnet run -c Release --urls http://127.0.0.1:5219` from the sample folder, in the background) and capture with the pre-installed Playwright (`require('playwright')`, `chromium.launch()` with no options, viewport 1408 wide, `deviceScaleFactor: 2`), a script kept at `samples/Synapbiz.Brain.Workbench.Demo/screenshots.mjs`: for light and dark, Spanish: Read (with one row marked "fix" and a note typed), Simulate with a decided result, Simulate with an input error and an engine refusal, Tests with one failing proposed case, Review with items; plus Simulate in English, light. Save as `/mnt/project-files/app-local/reglas/capturas/wb-<state>-<theme>[-en].png` (create that folder; write nothing else on the mount). Look at each picture with the Read tool and compare it with the matching `reglas-*-claro.png` / `-oscuro.png`; fix what differs in layout or wording, and list in the report what still differs and why. Stop the demo when done.

## Gates (run all; paste the tail of each in the report)

```bash
export DOTNET_ROLL_FORWARD=Major DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet build Synapbiz.Brain.sln -c Release --no-incremental 2>&1 | tee /tmp/wb-build.log | tail -5
grep "warning" /tmp/wb-build.log | grep -v OverlapAnalyzer      # must print nothing
dotnet test Synapbiz.Brain.sln -c Release --no-build
rm -rf /tmp/wb-cov && dotnet test Synapbiz.Brain.sln -c Release --no-build --collect:"XPlat Code Coverage" --results-directory /tmp/wb-cov
rm -rf /tmp/wb-artifacts && dotnet pack Synapbiz.Brain.sln -c Release --no-build -o /tmp/wb-artifacts && ls /tmp/wb-artifacts
bash tools/parity/check-source-drift.sh /home/user/nuvora | tail -1
git -C /home/user/nuvora status --porcelain | wc -l
```

Expected: 0 errors and only the 2 baseline warnings (never `NoWarn`, never an analyzer switched off, never a suppression attribute or pragma in new code; if an analyzer rule cannot be satisfied in Razor-generated code, stop and report rather than suppress); every test project green with the existing counts unchanged or grown; cobertura `line-rate` at least `0.95` for `Synapbiz.Brain.Workbench` and at least `0.85` for `Synapbiz.Brain.Workbench.Blazor`; five `.nupkg` files; the drift guard's usual line; nuvora count 0.

## Commits and report

Small commits in English, each message ending with:

```
Co-Authored-By: Claude <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01VN2ND7Ahy1HAQ2DW9D8MUU
```

Report: the commit list, the tail of every gate, both coverage numbers, the public API as built, the list of string keys, the screenshot paths, and every place where you could not follow this brief literally.

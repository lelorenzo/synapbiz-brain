# H3: the process map, kinds of process, and a live case over the diagram

Status: draft, 2026-11-23. Direction approved by the owner on the mockup (`/mnt/project-files/app-local/procesos/mapa-procesos-claro.png`, source `mapa-procesos.html`): "mejor", 6:43 PM ET. Normative once in the repository as `docs/specs/flow-workbench/spec-h3-map-live.md`. Package version 0.8.0, after Brain MCP (0.7.0).

Owner's words this package answers (2026-11-23): "seria bueno que existiera una diferenciacion entre los distintos tipos de flujos, al menos saber cuales son tareas programadas ... poder representar de alguna manera la estructura logica de la clinica y sus procesos"; "Flujos es el termino correcto aqui?" (decided: **Procesos**); "quiero que esto conviva con la funcionalidad que visualiza los diagramas de flujo".

H3 has four parts. Parts 1 to 3 are computed from the documents only. Part 4 needs the host.

## 1. Wording: "process", never "flow", in what a person reads

- Every fixed string of the flows panels and every sentence template says "process"/"processes" in English and "proceso"/"procesos" in Spanish. Kinds: "Main process"/"Proceso principal", "Subprocess"/"Subproceso", "Scheduled task"/"Tarea programada".
- Code names stay (`FlowWorkbench`, `Synapbiz.Brain.Workbench.Flows`, string keys with the prefix `flow.`), so hosts do not break. Only default texts change; `TextOverrides` keeps working by key.
- A test fails when a default text in either language contains "flow", "flows", "flujo" or "flujos" as a word.
- The demo header becomes "Rules and processes workbench" / "Banco de pruebas de reglas y procesos".

## 2. Kinds of process (headless, `Synapbiz.Brain.Workbench.Flows`)

`FlowKind` for every flow of a library, derived from triggers and from who starts whom, with no host input:

| Kind | Rule |
| --- | --- |
| `Scheduled` | has a schedule trigger |
| `Main` | has an event trigger (or a manual start) and no other process starts it |
| `Subprocess` | another process in the library starts it through a subprocess step |

A document that is both started by another and has its own trigger is `Main` and is also listed as started by its parent. A document nobody starts and with no trigger is `Main` with the mark "no start defined", and the panel says so in words. The rule is stated in the reference page and pinned on every reference document and fixture.

The list groups by kind (Main processes, Subprocesses, Scheduled tasks), each line with its kind icon and word. The host's `Group` heading stays as a second level when present.

## 3. The process map

### 3.1 Model (headless): `ProcessMap.From(library, locale)`

- **Starts**: each event or manual trigger of a main process, with its sentence ("one case per appointment").
- **Main processes**, each with its **phases** in order (the stages of the document, labels from `meta.display`), each phase with the sentence of what ends it when it can be derived (the routes leaving the stage), otherwise none.
- Under each phase, the **subprocesses** that phase starts, in document order. A subprocess started from steps with no stage goes under "No phase".
- For every process, what it **uses**, deduplicated and in a fixed order: rules (`decide` steps), connectors (ports of read, action and poll steps), queues of person tasks, AI steps. Each with a label: from `meta.display` when the document has one, else from the host's `MapLabels` (a dictionary by kind and name the host passes), else the raw name shown as code.
- **Ends** of each main process (its end steps' labels).
- **Scheduled tasks** in their own lane, each with its schedule sentence.
- **Shared pieces**: every rule, connector or queue used by more than one process, with the count.
- Counts per process: steps, scenarios and their result, routes not tried (from `FlowCoverage`).
- A subprocess started by two parents appears under each, marked "also started by <title>". Cycles between processes are reported, not drawn twice.
- Every process of the library appears somewhere on the map: in a lane, nested in the card of the subprocess that starts it, under the scheduled task that starts it, or in the "nothing reaches" lane. A subprocess card lists the subprocesses it starts (nested, in document order) and a scheduled task lists the subprocesses it starts; a process already on the way down is a cycle and is not drawn again.

Everything is deterministic and ordered by document order; golden tests in both languages on the six reference documents.

### 3.2 The view (`FlowWorkbench`)

- The component opens on **Map**; the three views are tabs: **Map**, **List** (the H2 list, now grouped by kind), **Live** (part 4; not drawn when the host gives no live source).
- Layout as in the mockup: a compact legend strip (one icon, colour token and word per kind of piece), the starts, each main process as a frame with its phase strip and one column per phase holding its subprocess cards with "uses" chips, the ends, the scheduled lane, shared pieces.
- Selecting a process opens it beside the map with the existing tabs (Read, Diagram, Simulate, Tests, Review) and a breadcrumb "Map › parent › process"; "Open the full diagram" gives the diagram the whole width. Selecting a rule chip raises `OnOpenRule(ruleId)` so the host can switch to `RuleWorkbench`; without the callback the chip is not a link.
- Below 1100 px the map stacks phases vertically; the side panel replaces the map with a back control.
- Icons: one inline SVG symbol per kind of piece (main process, subprocess, scheduled task, event, rule, connector, person task, AI step), drawn by the component, stroke in `currentColor`, no icon font, no JavaScript. Kind is never by colour alone: icon and word always go together. New `--wb-*` tokens per kind (tone and soft background), listed in the reference page with light and dark values in the demo.
- Accessibility: the map is a list structure (main process › phases › subprocesses) with headings, every card a button with a full name ("Subprocess: Pre-visit outreach, 9 steps, 16 tests pass"), chips are list items, the legend is a description list.

### 3.3 The same vocabulary inside the diagram

- A step node carries the small icon of what it uses (rule, connector, person, AI, subprocess); the diagram legend uses the same symbols as the map.
- Route labels (the known limit of 0.6.0): a label is never cut below 8 characters; when it does not fit beside its port it moves onto the first free horizontal segment of its line; when nothing fits it becomes a numbered mark whose text is listed under the diagram ("1: No answer"). A line never crosses label text (geometric test on every reference document and fixture, both languages).

## 4. Live: cases over the map and one case over its diagram

The host supplies an optional `IFlowLiveSource` (read only): counts of open cases per process and per current step, and for one case its history. Brain's Postgres host already answers these (the cockpit queries of 0.3.0); the brief maps each member to an existing query, and adds none unless one is missing.

- **Map, Live tab**: each phase and each subprocess card shows how many cases are on it now, and how many wait for a person. A count opens the list of case references (opaque references only, never personal data) through `OnOpenCase`.
- **One case**: its path drawn over the diagram of the version it runs on (steps visited, the current step, routes taken, what it waits for and until when), and a side column: current step, why it is here (the rule and row or the event that decided), what comes next, process version. Built from history with the same explanation code as `FlowCoverage`; a history that cannot be explained says so.
- Nothing in Live acts on a case. Refresh is the host's call (`Refresh()` or a new parameter value); the component does not poll.
- No patient data: the component shows only what the source returns, and the contract says case references are opaque.

## 4a. Sources and authority of a rule (asked by the clinical library thread, item CL4, 2026-11-23; the owner wants it pulled forward)

Brain renders what a rule document states and hard-codes no clinical source.

- **Sources block** on the Read tab of `RuleWorkbench`, and in the answer of `rule_get`: one line per entry of `meta.sources[]` (`source`, `version`, `relation` = `implements` | `adapts` | `informed_by`, `differences[]`, each an object with `en` and `es` texts, agreed with the clinical library thread). The source label comes from the host's dictionary (`SourceLabels`: id to label per language); an id with no label is shown as code. The relation is a fixed phrase in both languages ("implements", "adapts", "informed by"); differences are shown as the list the document gives. An entry with an unknown relation or a wrong shape is shown as "source entry not readable" and never breaks the panel. `meta` stays an open object: no format change.
- **Authority badge** from `meta.authority` (a code such as `payment_condition`, `payer_measured`, `recommendation`, `local_preference`): shown with a host-supplied label on the rule's list line, its Read tab and its chip on the process map; no label, no badge.
- **`rule_list` filter** in the authoring tools: `source` (matches `meta.sources[].source`) and `authority`; the answer says in words how many rules cite it.
- The rule differ reports a change in `meta.sources` or `meta.authority` in plain language, and such a change makes a rule proposal `needsReview` true (it changes what the rule claims to follow).

## 5. Tests

Kinds on every reference document and fixture. `ProcessMap` goldens in both languages; shared pieces; two parents; a cycle; labels from the three sources. bunit: map structure equals the headless model, selection and breadcrumb, rule chip with and without callback, narrow layout, legend, accessibility names. Diagram: icons per step kind, the three label placements, no line through a label. Live: counts, a case path on each reference document replayed from a scripted history, an unexplained history, no source. The wording test of part 1. Stylesheet and string parity tests cover the additions. Mutation pass by the verifier, and the demo run in a browser.

## 6. Documents

`docs/reference/flow-workbench-panels.md` (map, kinds rule, icons and tokens, live source), `docs/reference/flow-workbench.md` (`FlowKind`, `ProcessMap`), architecture overview, consuming guide (the wording change and `MapLabels`), CHANGELOG, tasks file, screenshots of Map, List by kind, Map with a process open, Live, in both languages and themes.

## 7. Not in this package

Editing from the map. Drag, zoom or a layout engine. Acting on a live case. Organizational structure beyond what the documents state (locations, roles, schedules of people).

## 8. Amendments after verification (0.8.0)

An independent verifier found the points below; each is fixed and pinned by a test that failed before.

- **Every process is on the map.** Section 3.1 said a subprocess appears under the phase that starts it, which left a subprocess started only by a scheduled task, or only by another subprocess, in no lane. `MapChild.Nested` and `MapScheduled.Children` now carry them, the view draws them as nested cards (same component, same names), and the Live counts sit on them like on any card. Invariant test: every process of every fixture library appears on the map.
- **The map answers to its own width.** The phase strip stacks by a container query on the map panel, not by the window width, and the grid, columns and cards have `min-width: 0`.
- **Diagram text.** Text breaks only at spaces; a third line and a taller step (66) are allowed; a single long word is cut with an ellipsis, never split; the marker of the current step sits on the corner of the step; the text of an end step is 19 characters wide so it clears the start dot and the number of a visited step. Geometry tests on every document and fixture in both languages.
- **Labels.** The 8-character floor is pinned with the literal; a cut label is cut at a space when that keeps 8 characters, never leaving a space before the ellipsis; fixtures force a cut and a numbered mark.
- **Live list.** A phase of one step is asked of the source by that step; "None among the cases read so far" is said while more pages remain; the whole process and the steps with no stage are different lists (they shared a key); one case has a Refresh control; stale answers for counts, list and case are ignored (tested with a source that ignores its cancellation token).
- **Sources.** The differ compares parsed values, so the order of the members of a `meta.sources` entry is no change; `rule_get` and `rule_list` say in words when a source entry cannot be read.
- **Raw route values.** A route value of a decision (for example `yes` of `case.lifecycle`) has no label in `meta.display`, whose groups are stages, steps, results, queues, outcomes, events and windows; the format is unchanged and the value is shown as written.

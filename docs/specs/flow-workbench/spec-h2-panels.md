# H2: the flows panels (read only)

Status: draft, 2026-11-23. Owner chose the order "G first, then H1, H2, H3" on 2026-11-23. Normative once in the repository as `docs/specs/flow-workbench/spec-h2-panels.md`. Package version 0.6.0.

H2 draws what H1 computes. It adds one public component, `FlowWorkbench`, to `Synapbiz.Brain.Workbench.Blazor`, next to `RuleWorkbench` and built the same way: a list on the left, tabs on the right, a session the host owns, fixed strings in English and Spanish that the host can reword, styles from `--wb-*` tokens only, no JavaScript. A reader can see every flow, read it in plain language, see it as a diagram, simulate it with a made-up case (answering AI steps by hand, from an embedded scenario, or through the host's AI provider), see what the embedded scenarios cover, and send back a review. Nothing in H2 edits a flow. A live case over the diagram is H3.

## 1. Rules for the whole package

1. The component has no logic of its own about flows: every sentence, graph, simulation step, coverage figure and review document comes from `Synapbiz.Brain.Workbench.Flows`. Nothing changes in `Synapbiz.Brain.Process`, in the headless library's behavior or in `RuleWorkbench` (its tests stay as they are).
2. No JavaScript file and no `IJSRuntime` (the existing architecture tests cover it). The diagram is SVG written by the component.
3. Every fixed string exists in English and Spanish, English by default; the keys join `WorkbenchStrings` under the prefix `flow.` and follow its rules (override by key, `**bold**`, placeholders, mandatory markings). The existing parity test covers them. The host owns the language selector and passes `Locale`; changing it re-renders in place and keeps the session.
4. The stylesheet keeps its contract: only `wb-*` classes, only `--wb-*` tokens, no colour, no font, no `var()` fallback (the existing stylesheet test covers the additions). New tokens are listed in the reference page with their role.
5. Accessibility as in the rule panels: the tabs pattern, visible focus, every control reachable by keyboard, state never by colour alone (an icon or a word goes with it), `aria-live` for a result that appears after an action, and the diagram has a text equivalent (section 4.2).
6. Simulation is always marked as such: the simulation band and pill are mandatory markings on the Simulate tab and wherever a simulated result is shown. The review tab carries the privacy notice (no patient-identifying text in notes). An override may reword them, never empty them.
7. No patient data in fixtures, the demo or screenshots: synthetic values only.
8. The assembly now references `Synapbiz.Brain.Process` as well; the architecture test that pins its references is updated to the five libraries and nothing else.

## 2. Mounting

```razor
<FlowWorkbench Flows="flows"
               Locale="WorkbenchLocale.En"
               Session="session"
               Now="() => DateTimeOffset.UtcNow"
               Zones="zones"
               AnswerSource="practiceAi"
               OnExport="SaveAsync"
               TextOverrides="overrides" />
```

| Parameter | Type | Meaning |
| --- | --- | --- |
| `Flows` | `IReadOnlyList<WorkbenchFlow>?` | The flows in list order. `null` shows a loading line; empty shows the empty text. |
| `Locale` | `WorkbenchLocale` | Language of fixed strings, sentences and labels. |
| `Session` | `FlowWorkbenchSession` (required) | The reader's state, owned by the host, in memory only. |
| `Now` | `Func<DateTimeOffset>` (required) | The host's clock: the default start of a try-out and the time of an export. |
| `Zones` | `IReadOnlyDictionary<string, TimeSpan>?` | Fixed UTC offsets for the zones a try-out may meet. A zone not listed makes its delivery window fail in the try-out, and the panel says so in words. |
| `AnswerSource` | `IFlowAnswerSource?` | The host's source of effect answers (for example the practice's own AI account). When null, the "ask" buttons are not drawn. |
| `OnExport` | `Func<FlowWorkbenchExport, Task<string?>>?` | Saves the review files; same contract as in `RuleWorkbench`. Not set: no export button. |
| `TextOverrides` | `IReadOnlyDictionary<string, string>?` | Replaces fixed strings by key. |

`WorkbenchFlow`: `Key` (required, stable, unique), `Group` (heading in the list), `Process` (`BrainProcess?`, null when the host could not load it), `FallbackTitle`, `StateLine`, `SourceLine`. Child flows for subprocess steps are resolved inside `Flows`.

`FlowWorkbenchSession`: selected flow, tab and step; per flow the review marks and notes, the proposed scenarios, the current try-out (a `FlowSimulation`) and the scenario being replayed; `Reviewer`; `UnexportedFlowCount`; `Clear()`; `Changed`. One per reader; register it per user session.

## 3. The list

Flows under their group headings. Each line: title, a line saying how it starts (the trigger sentences of `FlowLibrary`), counts (steps, scenarios), scenario result (all pass, n fail, none), route coverage as "n of m routes tried", review progress. An unreadable flow is listed as such with its fallback title. A flow started by another flow shows "started by <title>". Below 1100 px the list collapses to a select, as in the rule panels.

## 4. Tabs for the selected flow

### 4.1 Read

Title, state and source lines, how it starts, the rules it uses and the flows it starts (each a link that selects that flow when it is in the list). Then the stages in order; under each stage its steps in document order: label, kind in words, the step's sentence, and its routes as a list of "phrase: target label" where the target is a link that moves focus to that step. A step with a route never taken by any scenario carries the mark "not tried by a test" on that route. An `ai` step shows the reviewer's block of `FlowAiView` (pinned model and prompt, what goes in, what must come out, attempts, where the flow goes without the AI) under a fixed heading. Steps with no stage come under a last heading "No stage".

### 4.2 Diagram

An SVG drawn from `FlowGraph`: stage bands stacked vertically in stage order (a last band for steps with no stage), nodes placed by rank and column inside their band, a shape for each family of step (`FlowFamily`: action, decision, person, AI, wait, flow, end; the start step carries a dot) with the label inside; lines with an arrow, the happy path solid and the other routes dashed. One line is drawn for each ordered pair of steps that has a route, with a count mark when it stands for several routes and an SVG `title` that lists their phrases. Each node is focusable and selectable (click, Enter or Space) and opens the step card beside the diagram: the same content as the step in Read. Edge phrases are shown in the card, not on the lines, except the short label of a decision route (`FlowEdge.ShortLabel`). Routes never taken by a test are drawn with the "not tried" sign and counted in the legend. The legend explains every shape and line style in words.

The layout is a deterministic rule of `DiagramLayout`, stated in full in the reference page (bands, rank by the longest path over the routes that do not return, rows, spine and columns, one line for each ordered pair, orthogonal routing in the gutters and in lanes at the right for returning routes, marks 14 units below the port). It is pinned to the unit for one flow (`previsit.outreach`: boxes, line paths, label points and marks) and tested as geometric rules for every reference flow and every fixture in both languages (no overlap, orthogonal lines, no line through a box, no shared stretch, one line for each pair, `case.lifecycle` at most 1100 wide). The SVG has `role="group"` with a label; the text equivalent is the Read tab, linked from a visible "read as a list" control. Wide diagrams scroll inside their box; the card goes beside the diagram only when the box holds the diagram at 640 px and the card at 320 px. Nothing depends on zoom.

### 4.3 Simulate (simulation)

The mandatory band says the case is made up and nothing is sent anywhere.

- **Start.** A form built from the flow's input variables with a control per type (text, number, date, date and time, yes/no, list as lines), the start time (default `Now()` truncated to the minute) and "Start". When the flow has embedded scenarios, a select "start like the test <name>" fills the form from that scenario. A start the interpreter rejects shows its reason in words and keeps the form.
- **Where the case is.** Current step (label, stage, sentence), the virtual time, the status in words, the variables as a definition list (label, value), and the diagram's node highlighted when the Diagram tab is opened.
- **What it waits for.** One card per outstanding item with the actions that fit:
  - an effect (read, action, AI, poll): "Answer from the test <name>" for each embedded scenario that answers this step (taken from the scenario, no typing); "Ask <source name>" when `AnswerSource` is set (runs `RunAnswersAsync` for that effect, shows a busy state, can be cancelled); "It fails" with the two choices temporary or permanent; and, folded under "Write the answer", a JSON text area for a hand-written output. For an AI step the card shows the output schema's fields in words. A refused answer shows the reason and changes nothing.
  - a task for a person: one button per outcome (label from the document), with the task's shown values.
  - a wait for an event: "The event arrives" with the payload fields the step reads as a small form.
  - a timer or a window: "Go to that moment" (advances the clock to it).
  - a child flow: "Finish the child with result" (a select of the child's results when the child is in `Flows`), or "Open the child" to try the child itself and bring its result back.
  - always: "Let time pass" with choices (1 hour, 1 day, 1 week, to the next timer).
- **What happened.** The session log as a timeline, newest last: virtual time, what was done and by whom (by hand, from a test, or the source's name), what the flow did next (steps entered, in words). A refused command is listed with its reason.
- **Keep it.** "Save as a proposed test" names the try-out and adds it to the review of this flow as a proposed scenario (`ToScenarioJson`). "Start over" discards the try-out after a confirmation drawn in the panel.

### 4.4 Tests and coverage

The embedded scenarios and the proposed ones: name, result (pass, or the failure in words), the steps it went through. "Replay" plays a scenario step by step in the Simulate tab's timeline (read only until its end; then the reader may continue by hand). Coverage of `FlowCoverage`: n of m routes tried, the routes never tried as "step label: route phrase", the steps never entered, and any run whose history could not be explained. Each scenario line has the small "Review" control of the rule panels (confirm, fix, question, note).

### 4.5 Review

Marks (confirm, fix, question) with a note for the whole flow, per stage, per step, per route and per scenario, set here or from the Read tab and the step card through the same small control. A fix or a question needs its note before export, as in the rule panels. Reviewer name, the privacy notice, progress (n of m items marked), and "Export the review": one `brain.process-review@1` document per flow that has a mark, a note or a proposed scenario, handed to `OnExport` as `FlowWorkbenchExport` (`Files`: suggested name, JSON, the `FlowReview`). After a successful export the session counts nothing as unexported.

## 5. Demo and screenshots

`samples/Synapbiz.Brain.Workbench.Demo` gains a page `/flows` that mounts `FlowWorkbench` with the six reference documents, fixed zones and a scripted answer source named "Demo answers". The screenshot script captures list, Read, Diagram, Simulate (mid-run) and Review in both languages, desktop and narrow; the images go where the rule panels' images go and the reference page shows them.

## 6. Tests (bunit, as the rule panels)

- Mounting: null, empty, unreadable flow, one flow, groups, the narrow select; locale switch keeps the session; `TextOverrides` applied; the mandatory markings cannot be emptied.
- Read: every reference flow renders every step sentence of `FlowDescriber` in both languages (compared with the headless view, not with a copy of the text); route links move the selection; the AI block; the "not tried" mark equals `FlowCoverage`.
- Diagram: one node per step and one line per route for every reference flow; coordinates of `previsit.outreach` pinned; keyboard selection; legend; no overlap of two nodes (checked on every reference flow and fixture).
- Simulate: start form per variable type; rejected start; each kind of outstanding item and each action; answer from a scenario; ask the source (a scripted one), busy state, cancel, a source that throws; refused JSON; let time pass; a whole reference flow walked to its end by buttons only; save as proposed test, which then passes on the Tests tab; start over.
- Tests and coverage: results equal the headless results; replay ends where the scenario ends.
- Review: marks from each place meet in one state; note required; export content equals `FlowReviewJson.Serialize` of the session's review and parses back; export without name focuses the name field; nothing to export.
- Accessibility assertions as in the rule panels (roles, `aria-selected`, `aria-expanded`/`aria-controls`, labels on every control, live region).
- Strings: every `flow.` key is used, has both languages, and no panel text is written outside the table. Stylesheet test passes with the additions.
- Architecture tests updated only for the fifth library.

## 7. Documents

`docs/reference/flow-workbench-panels.md` (mounting, types, tabs, string keys, tokens, the layout rule, screenshots), `docs/reference/flow-workbench.md` (link), the guide, `docs/architecture/overview.md`, `docs/consuming.md`, `CHANGELOG.md`, `docs/specs/flow-workbench/tasks-h2.md`, version 0.6.0.

## 8. Not in this package

A live case over the diagram (H3). Editing a flow, its timeouts, texts or triggers. Comparing two versions. Drag, zoom or a layout engine. Printing. Storing anything outside memory.

## 9. Amendments after implementation and verification

The first implementation was verified by an independent reader and repaired. Every change of a contract of this specification is recorded here; where this section and an earlier one differ, this section wins.

1. **Name.** The tab shown as "Try it" is "Simulate" (`FlowWorkbenchTab.Try` keeps its name). "Try-out" stays as the word for the case on the screen.
2. **`WorkbenchFlow.ProcessText` is removed.** The content hash of a review comes from the loaded process, so the host supplies no text; a flow is unreadable only when `Process` is null.
3. **Review tab.** Every item the tab lists has its own note editor, so "Write the note" never depends on another tab. Focus is given only to an element drawn in the current render, and a `JSException` or `InvalidOperationException` of the focus call is ignored. Review targets include `trigger:<id>` (`FlowReviewTargets`).
4. **Export.** A missing note in any flow of the list holds the export of all flows, and the message names the flows (with the version when two listed flows share a title). Only the flows written are marked as exported; unlisted flows with unexported work stay unexported and the panel says so. Editing only a note after an export makes the flow unexported again. An exception of `OnExport` is not caught, as in `RuleWorkbench`; nothing is marked as exported then. The list line says "needs a note" for a flow with such an item and "{n} of {t} tests fail(s)" when tests fail.
5. **Simulate.** The answers of the tests are offered in one select with the button "Use this answer" (the options are the scenario names in quotes, "(fails)" appended when the answer fails); the second visit of a step offers the second answer of each test (`FlowScenarioAnswers.For(simulation, step)`). Start forms have typed controls (number, date, date and time labelled UTC, yes or no). A replay of a test is step by step ("Next step", "Play to the end"), the cards are read only until the last command, and afterwards the reader goes on by hand with the answer source and the child documents of the screen; "Ask" is drawn only when the simulation has a source and a failure of the source is a visible refusal (`FlowSimulation.AskAsync(effectId, ...)` asks one effect). Proposed tests get the same line as the tests of the document (trace, Replay, review control). Reasons are written in words (`FlowCodes`), with the code under "Technical detail:" when the table has no sentence. Times are written `MM/dd/yyyy h:mm tt` (English) or `dd/MM/yyyy HH:mm` (Spanish) followed by UTC; automatic entries are attributed to "automatic".
6. **Scenarios are played once.** The component plays the scenarios of a flow once for each flow instance and language (`FlowScenarioReport`) and keeps the report; the list, the coverage, the Tests tab and the Diagram all read it. No static state holds it.
7. **Diagram.** The layout of 4.2 replaces the layout by stage column and depth. Its rank uses the retreating edges of a depth-first walk from the first step, not `FlowEdge.IsBackEdge`, which the headless library sets by reachability and which the diagram no longer uses. Lines are orthogonal, one for each ordered pair, with count marks; the width is computed from the content.
8. **Headless additions** (no change to existing behaviour or goldens): `FlowNode.Family`, `FlowEdge.ShortLabel`, `FlowRouteView.Stem`, `FlowSimulation.NextDue`, `AnsweredCount`, `EnteredSteps` and `AskAsync`, `FlowSimulationEntry.EnteredSteps`, `IsAutomatic` and `EndedCase`, `FlowSimulationSetup.TodayOf`, `FlowReplay`, `FlowScenarioReport` with `FlowScenarioResult`, `FlowLibrary.From(..., reports)` and `ResultsOf`, `FlowReviewTargets`, `FlowCodes`, and the `code.*` sentences of the template table.
9. **Layout of the page.** Cards wrap, the review control of a step wraps under its title, nothing overflows at 480 px, and the diagram sits in a container whose width decides where the card goes.


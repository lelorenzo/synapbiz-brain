# Flow workbench panels

`Synapbiz.Brain.Workbench.Blazor` has two public components: `RuleWorkbench` (see [rule-workbench-panels](rule-workbench-panels.md)) and, since 0.6.0, `FlowWorkbench`. It draws what [`Synapbiz.Brain.Workbench`](flow-workbench.md) computes about business flows: a list on the left and, for the selected flow, five tabs (Read, Diagram, Simulate, Tests, Review). It is read only: nothing in it edits, activates or sends a flow. It holds no flow logic; every sentence, form, answer and check comes from the headless library.

Namespace `Synapbiz.Brain.Workbench.Blazor`; the assembly references `Synapbiz.Brain.Contracts`, `.Engine`, `.Process`, `.Spec` and `.Workbench`, plus `Microsoft.AspNetCore.Components.Web`. No JavaScript is shipped; the only use of `IJSRuntime` is the focus call that `ElementReference.FocusAsync` makes. It runs in Blazor Server or in WebAssembly, interactive mode.

## What the panels do

- **List.** Flows under group headings. Each line has the title, how the flow starts, the counts of steps and tests, whether the tests pass ("all 16 tests pass", or "1 of 16 tests fails"), "n of m routes tried" and review progress. A flow that has a fix or a question with no note says "needs a note". When two listed flows have the same title each line also says "version n". A flow the host could not load is listed as unreadable. A flow started by another flow says so. Below 1100 px the list collapses to a select.
- **Read.** The flow in plain language, stage by stage, one card per step with its sentence and one phrase per route (the name of the target step is a link). An `ai` step also shows the pinned model and prompt, the schemas, the attempts and where the flow goes when the answer does not fit.
- **Diagram.** An inline SVG drawn by the rule of the next section. The happy path is a solid line, the other routes are dashed, a route no test takes carries the "not tried" sign. Selecting a node shows the same card as Read beside the diagram when the room allows it, else below it.
- **Simulate.** A made-up case on a virtual clock. The reader fills the case data (a typed control for each declared input: text, number, date, date and time in UTC, yes or no, one value for each line), sees where the case is and what it waits for, and answers: a port result, an answer to an `ai` step (one select of the answers the tests give, each named by the test in quotes and marked "(fails)" when it fails, with the button "Use this answer"; or written by hand; or asked of the host's `AnswerSource`), a person's task, an event, or the passing of time (one hour, one day, one week, to the next timer). Every command is written to the log of what happened, with the virtual time (`MM/dd/yyyy h:mm tt` in English, `dd/MM/yyyy HH:mm` in Spanish, always followed by "UTC") and who did it (by hand, from a test, the source's name, or "automatic" for a timer or a window). A refused command is listed with its reason in words; the code of an unusual failure is shown under "Technical detail:". A try-out can be saved as a proposed test. A test of the Tests tab can be replayed in this tab one command at a time ("Next step") or all at once ("Play to the end"); the cards are read only until the last command; after it the reader goes on by hand, and the "Ask" button is drawn for any effect that is waiting when the host gave an `AnswerSource`.
- **Tests.** The tests the document carries and the ones the reviewer proposes, each with its result, the steps it goes through, a "Replay" button and the small review control; the routes no test takes and the steps none enters; the proposed tests are checked with `FlowReviewCheck`.
- **Review.** A confirm, fix or question mark per flow, trigger, stage, step, route and test; a note editor for every item the tab lists (a fix or a question needs its note); the reviewer's name; the export of one `brain.process-review@1` file per flow with something to say.

Two markings are always drawn: the simulation band and pill on Simulate and Tests (nothing is sent anywhere) and the privacy notice on Review. The host may reword them through `TextOverrides` but cannot empty them.

## Mounting

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
| `Flows` | `IReadOnlyList<WorkbenchFlow>?` | The flows in list order. `null` shows a loading line; empty shows the empty text. Child flows of subprocess steps are resolved inside this list. Pass the same instances between renders to keep the screen steady: the scenarios of a flow are played once for each flow instance and language and kept by the component. |
| `Locale` | `WorkbenchLocale` | Language of fixed strings, sentences and labels. Changing it re-renders in place and keeps the session. |
| `Session` | `FlowWorkbenchSession` (required) | The reader's state, owned by the host, in memory only. Register one per user session. |
| `Now` | `Func<DateTimeOffset>` (required) | The host's clock: the default start of a try-out and the time of an export. |
| `Zones` | `IReadOnlyDictionary<string, TimeSpan>?` | Fixed UTC offsets for the zones a try-out may meet. A zone not listed makes its delivery window fail in the try-out, and the log says "the time zone is not in the list of this screen". A zone that cannot be used at all (an offset over 14 hours) is refused at the start, in words. |
| `AnswerSource` | `IFlowAnswerSource?` | The host's source of effect answers. When null, the "Ask" button is not drawn, in a try-out or after a replay. Its failures are shown as a refusal, never as an exception. |
| `OnExport` | `Func<FlowWorkbenchExport, Task<string?>>?` | Saves the review files and returns a place to show, or null to say "saved" without a place. Not set: no export button. See "Export" below for what happens when it throws. |
| `TextOverrides` | `IReadOnlyDictionary<string, string>?` | Replaces fixed strings by key for the current locale. |

## Types

- `WorkbenchFlow`: `Key` (required, stable, unique, never shown), `Group`, `Process` (`BrainProcess?`; null makes the flow unreadable), `FallbackTitle`, `StateLine`, `SourceLine`. There is no text of the document: the content hash of a review comes from the loaded process.
- `FlowWorkbenchSession`: selected flow, tab and step (the step is cleared when the flow changes); per flow the marks and notes, the proposed tests, the current try-out and the replay; `Reviewer`; `UnexportedFlowCount`; `Clear()`; `Changed`.
- `FlowWorkbenchTab`: `Read`, `Diagram`, `Try` (shown as "Simulate"), `Tests`, `Review`.
- `FlowWorkbenchExport` and `FlowWorkbenchExportFile` (`SuggestedName`, `Json`, `Review`): one file for each flow with a mark, a note or a proposed test, named `review-<flow id>-v<version>.json`.

## Review targets

A review item is one of these targets (the same list `FlowReviewTargets.Of` gives the headless library, and the one `FlowReviewCheck` accepts): `process` (the flow), `trigger:<id>` (a schedule by its id, an event trigger by its event name), `stage:<id>`, `step:<id>`, `route:<step>/<label>` and `scenario:<name>` (a test of the document, or one proposed in this review). A mark can be set where the thing is shown: Read, the step card of the diagram, the line of a test and the Review tab lists everything that has a mark, a note or is proposed. Each listed item has its own note editor in the Review tab; "Write the note" opens the editor of an item that needs one and moves focus there. Focus is only given to an element drawn in the render that is being made, and a refusal of the browser to focus (`JSException`, `InvalidOperationException`) is ignored, so a click can never end the session.

## Export

"Export the review" needs the reviewer's name (without it the name field is marked and focused). It is held while any item of any flow of the list is a fix or a question with no note; then no file is built and the message names the flows, for example "2 items to fix or with a question need their notes, in "Front desk follow-up", "Visit note". Write them before exporting." (a flow is named with its version when another listed flow has the same title). Otherwise one `brain.process-review@1` document is built for each readable flow that has a mark, a note or a proposed test, and `OnExport` is called once with all of them.

- The flows written are marked as exported; a flow edited afterwards (a mark, or only a note) is unexported again. A flow that has unexported work but is no longer in `Flows` is not written and not marked; the panel says "n flows with unexported work are no longer in the list. That work was not exported and stays unexported." The "saved" line is shown only while no listed flow has unexported work.
- The returned place is shown in "saved to {place}"; null or white space gives the line without a place.
- As in `RuleWorkbench`, an exception thrown by `OnExport` is not caught by the component. It goes to the host (Blazor's error handling of the event); nothing is marked as exported, so the work stays unexported and the reader can export again. Handle failures inside `OnExport` if you want to say something to the reader.

## Rules the panels follow

- Answers typed by the reader are checked by the interpreter, not by the panel; a refusal is shown in words, and the code of a refusal the table has no sentence for is shown in code type under "Technical detail:".
- Names of scenarios and of answer sources are written in typographic quotes; variable names and ports are written in code type, except a variable the document gives a label.
- Controls have text names, state is never shown by colour alone, and each route of the diagram carries a text title with its phrase, and Read lists every route in words.
- Nothing is stored outside the session's memory.

## The diagram layout

The layout is a rule with no search and no randomness; it is computed from the graph alone and written in the summary of `DiagramLayout` in the code. In SVG units (one unit is one pixel at 100 %), a node is 184 by 50, the labels have at most two lines of 22 characters, and a short label has at most 14 characters.

1. The stage bands are stacked vertically in stage order, with a last band for the steps that have no stage. Each is a full-width strip with its label at the top left; a stage with no step has no band.
2. The rank of a step is the longest path to it from the first step over the routes that do not return. A route returns when, walking the routes depth first from the first step in document order, its target is a step that is still being walked (a retreating edge of that walk). This is not the `IsBackEdge` flag of `FlowEdge`, which the headless library sets from reachability; the layout uses its own walk so that it is the same for every document.
3. In a band, the rows are the ranks of its steps squeezed in order (equal ranks share a row). An end step goes in the last row of its band, one row lower when a route of the band leads to it from that row.
4. Column 0 of a band is its spine: from its first step by rank, the first happy-path route that stays in the band, again and again. The other steps take columns 1, 2, ... in document order, row by row, so that no two steps of a band share a row and a column. The columns are the same in every band.
5. One line is drawn for each ordered pair of steps that has a route; when it stands for several routes it carries their count, and its title (an SVG `title`) lists their phrases. A line runs between boxes only: it leaves the bottom edge of its step at a port of its own, runs in the gutters between rows and columns, one lane for each line that uses a stretch of a gutter, and enters the top edge of the other step; two steps in the same column and adjacent rows are joined by a straight vertical line. A route that returns, or leads to an earlier band, runs to a lane at the right of the bands (one lane for each route) and enters the other step from its right side.
6. The short label of a decision route and the "not tried" sign sit 14 units below the port of the line, on its first segment; when a line has both, the count is stacked 18 units below. The leftmost labelled line of a step that has several lines writes its label to the left of its port (right-aligned), the others to the right; a label is cut with an ellipsis (at least one character) so that it ends before the next port of the same step, and the full text stays in the title of the line.
7. The width is computed from the content (the right edge of the last box, lane or label, plus 8) and never more than 1100 for the reference flows; the diagram scrolls inside its box when the box is narrower, and the card of the selected step goes beside it only when the box holds the diagram at 640 px and the card at 320 px, else below it.

What is tested: the coordinates of every box, the path of every line, the label points and the marks of `previsit.outreach` are pinned to the unit; for every document (the six reference flows and every fixture) in both languages the rules are tested as geometry: bands do not overlap, no two boxes overlap, every line is orthogonal, no line crosses a box that is not at its ends, no two lines share a stretch, there is one line for each ordered pair, marks and labels sit where rule 6 says, `case.lifecycle` is at most 1100 wide, and the same input gives the same layout. The reader can always read the same facts in Read, so nothing depends on the lines alone.

## Strings and tokens

Fixed strings live in `WorkbenchStrings` under keys that start with `flow.` (311 keys in `WorkbenchStrings.Flows.cs`, each in English and Spanish, with the same placeholders, bold markers and plural pairs in both). List them with `WorkbenchStrings.Keys.Where(key => key.StartsWith("flow."))` and read a default with `WorkbenchStrings.Default(key, locale)`. The key families are `flow.list.*`, `flow.read.*`, `flow.diagram.*`, `flow.legend.*`, `flow.family.*`, `flow.try.*`, `flow.log.*`, `flow.live.*`, `flow.status.*`, `flow.save.*`, `flow.tests.*`, `flow.coverage.*`, `flow.review.*`, `flow.ai.*`, `flow.schema.*`, `flow.tabs.*`, `flow.field.*`, `flow.who.*`, `flow.load.*`, `flow.band.*` and the keys `flow.join`, `flow.join.label`, `flow.panel.version`, `flow.state.elsewhere` and `flow.time`. The sentences for the codes of the interpreter are not here: they are the `code.*` templates of the headless `FlowTemplates` (`FlowCodes.Describe`).

Three keys of the rule panels are used as they are: `pill.simulation` and `notice.privacy` (both are mandatory markings: an override may reword them, never empty them) and `fail.count.one` / `fail.count.other` (the number of failing tests on the label of the Tests tab).

The styles are `wb-*` classes in `wwwroot/workbench.css` and read only `--wb-*` tokens the rule panels already use; no token was added, and the stylesheet holds no colour, font or `var()` fallback. The tokens the flow rules read are `--wb-accent`, `--wb-accent-ink`, `--wb-focus`, `--wb-font`, `--wb-hover`, `--wb-ink`, `--wb-ink-2`, `--wb-line`, `--wb-line-soft`, `--wb-muted`, `--wb-radius-sm`, `--wb-selected`, `--wb-selected-edge`, `--wb-surface`, `--wb-surface-head`, `--wb-text`, `--wb-text-label`, `--wb-text-small`, `--wb-warn-bg`, `--wb-warn-edge` and `--wb-warn-ink`. At 480 px nothing overflows: the cards wrap, the review control of a step wraps under its title and the diagram scrolls in its own box.

## Demo and screenshots

`samples/Synapbiz.Brain.Workbench.Demo` serves the page `/flows` with the six reference flows (query parameters `theme=light|dark` and `locale=es|en`). Its answer source is called "Demo answers": it takes the first non-failing answer of the embedded tests. Start it as described in [rule-workbench-panels](rule-workbench-panels.md#demo-and-screenshots) and run `node screenshots.mjs <url> <folder> flows` (the fourth argument is `all`, `rules` or `flows`). It writes `wf-list`, `wf-read`, `wf-diagram`, `wf-try` (mid-run, the Simulate tab), `wf-replay` (a test replayed one command) and `wf-review` in Spanish light, `-dark`, `-narrow` and `-dark-narrow`, and in English `-light-en` and `-light-en-narrow`, and `wf-diagram-<flow id>-light` for the Diagram tab of each of the six flows (Spanish, light, wide), as PNG files. The demo is not packable.

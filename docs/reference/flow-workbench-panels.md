# Flow workbench panels

`Synapbiz.Brain.Workbench.Blazor` has two public components: `RuleWorkbench` (see [rule-workbench-panels](rule-workbench-panels.md)) and, since 0.6.0, `FlowWorkbench`. It draws what [`Synapbiz.Brain.Workbench`](flow-workbench.md) computes about business flows: a list on the left and, for the selected flow, five tabs (Read, Diagram, Try it, Tests, Review). It is read only: nothing in it edits, activates or sends a flow. It holds no flow logic; every sentence, form, answer and check comes from the headless library.

Namespace `Synapbiz.Brain.Workbench.Blazor`; the assembly references `Synapbiz.Brain.Contracts`, `.Engine`, `.Process`, `.Spec` and `.Workbench`, plus `Microsoft.AspNetCore.Components.Web`. No JavaScript is shipped and no `IJSRuntime` is used. It runs in Blazor Server or in WebAssembly, interactive mode.

## What the panels do

- **List.** Flows under group headings. Each line has the title, how the flow starts, the counts of steps and scenarios, whether the scenarios pass, "n of m routes tried" and review progress. A flow the host could not load is listed as unreadable. A flow started by another flow says so. Below 1100 px the list collapses to a select.
- **Read.** The flow in plain language, stage by stage, one card per step with its sentence and one phrase per route. An `ai` step also shows the pinned model and prompt, the schemas, the attempts and where the flow goes when the answer does not fit.
- **Diagram.** An inline SVG: one column per stage, one node per step, straight lines for forward routes and curves for routes that go back. The happy path is marked. Selecting a node shows the same card as Read.
- **Try it.** A made-up case on a virtual clock. The reader fills the case data (a form built from the flow's declared input), sees where the case is and what it waits for, and answers: a port result, an answer to an `ai` step (written by hand, taken from an embedded scenario, or asked of the host's `AnswerSource`), a person's task, an event, or the passing of time. Every command is written to the log of what happened. A try-out can be saved as a proposed scenario.
- **Tests.** The scenarios the document carries and the ones the reviewer proposes, each with its result; the routes no scenario takes and the steps none enters; the proposed scenarios are checked with `FlowReviewCheck`.
- **Review.** A confirm, fix or question mark per step, route, stage, trigger, scenario and for the whole flow; notes (required for a fix or a question); the reviewer's name; the export of one `brain.process-review@1` file per flow with something to say.

Two markings are always drawn: the simulation band on Try it and Tests (nothing is sent anywhere) and the privacy notice on Review. The host may reword them through `TextOverrides` but cannot empty them.

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
| `Flows` | `IReadOnlyList<WorkbenchFlow>?` | The flows in list order. `null` shows a loading line; empty shows the empty text. Child flows of subprocess steps are resolved inside this list. |
| `Locale` | `WorkbenchLocale` | Language of fixed strings, sentences and labels. |
| `Session` | `FlowWorkbenchSession` | The reader's state, owned by the host, in memory only. Register one per user session. |
| `Now` | `Func<DateTimeOffset>` | The host's clock: the default start of a try-out and the time of an export. |
| `Zones` | `IReadOnlyDictionary<string, TimeSpan>?` | Fixed UTC offsets for the zones a try-out may meet. A zone not listed makes its delivery window fail in the try-out, and the panel says so in words. |
| `AnswerSource` | `IFlowAnswerSource?` | The host's source of effect answers. When null, the "ask" button is not drawn. |
| `OnExport` | `Func<FlowWorkbenchExport, Task<string?>>?` | Saves the review files and returns a place to show; same contract as in `RuleWorkbench`. Not set: no export button. |
| `TextOverrides` | `IReadOnlyDictionary<string, string>?` | Replaces fixed strings by key. |

## Types

- `WorkbenchFlow`: `Key` (required, stable, unique, never shown), `Group`, `Process` (`BrainProcess?`), `ProcessText` (the exact document text, for the content hash of a review; none counts as unreadable), `FallbackTitle`, `StateLine`, `SourceLine`.
- `FlowWorkbenchSession`: selected flow, tab and step; per flow the marks and notes, the proposed scenarios, the current try-out and the scenario being replayed; `Reviewer`; `UnexportedFlowCount`; `Clear()`; `Changed`.
- `FlowWorkbenchTab`: `Read`, `Diagram`, `Try`, `Tests`, `Review`.
- `FlowWorkbenchExport` and `FlowWorkbenchExportFile` (`SuggestedName`, `Json`, `Review`): one file for each flow with a mark, a note or a proposed scenario, named `review-<flow id>-v<version>.json`.

## Rules the panels follow

- A fix or a question needs its note; the export is held, and says which, until every such note is written. The reviewer's name is required.
- Answers typed by the reader are checked by the interpreter, not by the panel; a refusal is shown in words with its code.
- Controls have text names, state is never shown by colour alone, and each route of the diagram carries a text title with its phrase, and Read lists every route in words.
- Nothing is stored outside the session's memory.

## The diagram layout

The layout is deterministic and computed from the graph alone: the column of a node is the index of its stage, its row is its depth from the start step (steps of the same stage and depth share a column and stack in document order). A route is drawn as a curve only when it is flagged as a back edge and leads to a smaller depth or to itself; every other route is a straight line. Known limitation: there is no routing engine, so a straight line between two distant nodes can cross a node between them. Read lists every route in words, so nothing depends on the lines alone. The layout is pinned by a test against the reference flows.

## Strings and tokens

Fixed strings live in `WorkbenchStrings` under keys that start with `flow.` (309 keys, each in English and Spanish, with the same placeholders, bold markers and plural pairs in both). The styles are `wb-*` classes in `wwwroot/workbench.css` and use only the existing `--wb-*` tokens; no token was added.

## Demo and screenshots

`samples/Synapbiz.Brain.Workbench.Demo` serves the page `/flows` with the six reference flows (query parameters `theme=light|dark` and `locale=es|en`). Its answer source is called "Demo answers": it takes the first non-failing answer of the embedded scenarios. Start it as described in [rule-workbench-panels](rule-workbench-panels.md#demo-and-screenshots) and run `node screenshots.mjs <url> <folder> flows` (the fourth argument is `all`, `rules` or `flows`). It writes `wf-list`, `wf-read`, `wf-diagram`, `wf-try` (mid-run) and `wf-review` in Spanish light, `-dark`, `-narrow` and `-dark-narrow`, and in English `-light-en` and `-light-en-narrow`, as PNG files. The demo is not packable.

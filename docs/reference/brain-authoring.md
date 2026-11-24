# Brain authoring: the tools an assistant uses to model processes and rules

`Synapbiz.Brain.Authoring` is the headless half of Brain MCP (package version 0.7.0, specification [docs/specs/brain-mcp/spec.md](../specs/brain-mcp/spec.md), tasks in [tasks.md](../specs/brain-mcp/tasks.md)). It holds seventeen tools as plain functions (JSON in, JSON out, one JSON Schema each) over a workspace of approved documents, drafts and proposals. It has no model, no vendor, no network and no MCP SDK: the MCP adapter and the stdio program are the second package (M2) and call these functions.

Wording: every text a person reads says "process" (`proceso`), never "flow" (`flujo`). Names in code, the tool names and the codes keep "flow".

## Three rules that do not bend

1. The assistant proposes; it never activates. No tool changes an approved document, activates a process, starts a real case or calls a port. Every result is a draft in the workspace or a proposal file.
2. The engine is the judge. A draft is ready to propose only when the loader accepts it, every embedded test passes and the checks below pass. The tools return facts computed by Brain; nothing the assistant writes can mark a draft valid.
3. Definitions and made-up cases only. The tools never read the host's tables, a live case or patient data. Their descriptions say so, and every free-text input carries the warning against text that identifies a patient.

## Using it

```csharp
var store = new FileAuthoringStore("/path/to/workspace");          // or InMemoryAuthoringStore in tests
var tools = new AuthoringTools(store, new AuthoringOptions { Clock = TimeProvider.System });
foreach (var tool in tools.All) { /* tool.Name, tool.Description, tool.InputSchema */ }
JsonObject answer = tools.Invoke("flow_get", JsonNode.Parse("""{ "id": "visit.note_wait", "language": "es" }"""));
```

`Invoke` never throws: an input the schema refuses, an unknown id or a draft that is not ready is a coded answer. `AuthoringOptions` has the clock (session expiry and the time a proposal carries; the library never reads the machine clock), `SimulationLifetime` (30 minutes without use), `MaxSimulations` (20) and `MaxDocumentLength` (1,000,000 characters).

### The workspace

`FileAuthoringStore` reads this layout and writes only under `drafts` and `proposals`:

```text
catalog.json                         brain.catalog@1, what the host offers (optional: without it nothing is restricted)
flows/<id>.json                      approved brain.process@1 documents (read only)
rules/<id>.json                      approved brain.rule@1 documents (read only)
drafts/flows/<id>.json               a draft document, with <id>.draft.json beside it (base, known gaps, edits)
drafts/rules/<id>.json               the same for rules
proposals/<name>.json                proposals
```

Every path is built from a validated id (letters, digits, `_`, `.`, `-`; not ending in `.draft` or `.tmp`), never from free text. A path that, once links are followed, leaves the workspace (for a read) or `drafts` and `proposals` (for a write) is refused, and so is a link. `InMemoryAuthoringStore` has the same contract for hosts that keep their documents elsewhere. A draft stores the approved document it came from (id, version, content hash); a flow hash is bare hex SHA-256 and a rule hash is `sha256:` plus hex, as in the existing files.

### The catalog (`brain.catalog@1`)

```json
{ "$spec": "brain.catalog@1",
  "ports":  [ { "name": "records.note_status", "kind": "read", "delivery": "atLeastOnce", "contactsPerson": false, "description": "..." } ],
  "events": [ { "name": "practice.opened" } ],
  "queues": [ { "name": "note_followup" } ],
  "stages": [ "visit" ],
  "contracts": [ { "module": "visit", "schemaVersion": 1, "facts": [], "kinds": [] } ] }
```

A member that is left out is not restricted. A draft that names a port of the wrong kind, a delivery the port does not offer, or a port, event, queue or stage outside the catalog is refused as "not available here", with the list of what is (codes `authoring.catalog.port_unavailable`, `port_kind`, `delivery_unsupported`, `event_unavailable`, `queue_unavailable`, `stage_unavailable`). A catalog that does not parse is refused with `authoring.catalog.invalid` and the code of the defect (`json_invalid`, `unknown_format`, `malformed`, `duplicate`, `contract_invalid`).

## The answer

Every answer is an object. A success is `{ "ok": true, "say": "...", ...data }`; a refusal is `{ "ok": false, "error": { "code": "authoring.<area>.<code>", "hint": "..." }, "say": "..." }`. `say` is a sentence in the asked language (`language`: `en` or `es`, default `en`) that the assistant can relay as it is, so a person never has to read JSON, a pointer or a code. `hint` is English, for the assistant.

## The tools

| Tool | What it does |
| --- | --- |
| `brain_guide` | The format reference for assistants in sections (overview, steps, routes, timeouts, loops, triggers, scenarios, labels, good, rules, method, examples); `section` picks one. The examples are the reference documents of the repository. |
| `brain_catalog` | Ports, events, queues, stages and approved rules available; a null list is not restricted. |
| `flow_list`, `rule_list` | Approved documents and drafts in progress, with a summary each. |
| `flow_get`, `rule_get` | One document with its plain-language description (and, for a process, the diagram as nodes and edges, tests and coverage; for a rule, every row as a sentence). `draft` true reads the draft. |
| `flow_draft_save` | Stores a whole document as the draft and answers in one result: loader errors (code, pointer, message, hint), catalog problems, description, what changed against the base, test results, route coverage and the readiness verdict with its reasons. `basedOn` names the approved process; without it an approved process of the same id is the base. `knownGaps` lists what the author accepts knowingly (a route no test takes, a wait without a timeout), each with a reason, kept beside the draft. |
| `flow_draft_patch` | The same result after RFC 6902 JSON Patch operations on the stored draft (`add`, `remove`, `replace`, `move`, `copy`, `test`); written for this package, applied to a copy, all or nothing. |
| `flow_draft_simulate` | `start` a made-up case on a draft, then send `command`s with the `session`: `complete`, `fail`, `task`, `event`, `advance`, `child`, `childFail`, `cancel`, `resume`. Sessions live in memory, expire by the injected clock and are bounded. `close` frees one. |
| `flow_draft_add_scenario` | Adds a test from a simulation (`fromSimulation`) or as written (`scenario`). |
| `rule_draft_edit` | The guided edits of the rule workbench (conditions, outcomes, rows, order, test cases, row names), cumulative on the approved rule; `reset` starts again. The answer has errors, sentences, what changed, how the test cases now decide, the tests and whether a proposal would need review. |
| `rule_simulate`, `rule_test` | Evaluate a rule for made-up facts, or run all its test cases; `draft` selects the draft. |
| `flow_propose` | Only when the draft is ready. Writes `brain.process-proposal@1`; `needsReview` is always true. |
| `rule_propose` | Writes `brain.rule-proposal@1` for a rule draft whose tests pass; the file also says whether it needs review. |
| `proposal_list`, `proposal_get` | The proposals written, and one with the text a person reads. |

## Readiness ("ready to propose")

`ready` is true when every check passes. Each finding has a code, a sentence in both languages and, where it applies, a step and a route.

| Code | The check |
| --- | --- |
| `authoring.ready.loader_errors` | The loader accepts the document. A process loads only against the contracts of the catalog (`brain.catalog@1`, member `contracts`) that its rules need; without them the loader refuses it. |
| `authoring.ready.scenario_failing` | Every embedded test passes. |
| `authoring.ready.no_scenarios` | The process has at least one test. Zero tests is never ready, whatever else is accepted. |
| `authoring.ready.scenario_names_duplicated` | Every test has its own name. |
| `authoring.ready.label_missing` | Every step, stage, result, task queue and choice a person is shown has a name in English and Spanish. |
| `authoring.ready.route_untested` | Every route is taken by a test, or listed as a known gap with a reason. |
| `authoring.ready.contact_unsafe` | A step that contacts a person is at most once per delivery, sits behind a delivery window and has routes for failure (`onError`) and for an unknown outcome (`onUnknown`). |
| `authoring.ready.catalog_missing` | A process with steps that read, act, poll or ask an AI is checked against a catalog that has ports. Without one the answer is "no catalog was given, so connectors and contact safety cannot be checked" and the draft is not ready. |
| `authoring.ready.ai_without_fallback` | Every failure route of an `ai` step reaches an end without AI. A failure kind without its own route goes to `onError`, as the engine does, so `onError` must exist and be safe. |
| `authoring.ready.wait_without_timeout` | Every wait has a timeout, or a known gap with a reason. |
| `authoring.ready.outside_catalog` | Nothing the process names is outside the catalog. |
| `authoring.ready.version_not_newer` | When the draft comes from an approved process (`basedOn`), its version is greater than the version of that process. |
| `authoring.ready.base_stale` | The approved process did not change after the draft was based on it. To start again on the current version, read the process with `flow_get`, make the changes on it, and save with `basedOn` set to its id, which records the current hash. |
| `authoring.ready.base_mismatch` | The draft has the id of the approved process it comes from. |

A known gap is `{check, step, route, reason}`; the reason is at least ten characters (`authoring.gaps.reason_short` refuses a shorter one and saves nothing). When a ready answer relies on known gaps it names each one with its reason, and the proposal file carries them, so the reviewer sees what was accepted knowingly.

## Words a person hears

No `say` carries a JSON pointer, a code, a backtick, a schema message or text of the platform. Every code that can reach a person has a sentence in both languages in the string table (`err.<code>` for the codes of the loaders, validators and checks; `refuse.<code>` for the refusals of the tools), written with the labels of the document (`meta.display`, else the words of the id), the natural name of each field, and what to do next. A code with no sentence gets one generic sentence; the raw detail stays in data members (`detail`, `hint`, `problems`) for the assistant. A call whose input does not fit the schema names the wrong or the missing member in words: "“by” is not a valid member; use “duration”". A test walks every refusal and every failure path in both languages and checks the sentences against a list of raw fragments.

Moments are said as a person says them with their zone ("January 4, 2027 at 14:00 (UTC)"), and the word is always "process", never "flow".

## What changed

`FlowDiffer` and `FlowChangeDescriber` compare two process documents and say, as data and in both languages: steps added, removed or renamed; routes that go elsewhere; timeouts, windows, retries, loop limits and triggers that changed with old and new value in words; labels; tests added, removed or changed; the id, title, description and subject of the process; the description of what an AI step is sent and may return ("what the AI may return changed: “reason” was added"); and the behaviour change: every test of the earlier version is replayed on the new document, and those that now end differently, or can no longer be played, are named. For rules, the existing `RuleChangeDescriber` is used.

When two documents are not equal (leaving the version aside) but nothing the differ names changed, the list says "something else changed" and where, in words, with the JSON Pointers in data (`pointers`). The differ never says nothing changed when the documents differ.

A rule proposal whose edit sets a row's outcome to a value the rule already gives, or changes a threshold, has `needsReview` false: that is the owner's decision (the rule stays within the facts it reads and the outcomes it gives). Editing the expected result of a test shows in the change list as "A test’s expected result changed: …" (`changes.testsChanged`), so a person sees when a test was bent to fit.

## Proposals

A proposal changes nothing. Applying it (a commit, an activation in the host) is a person's step outside these tools.

- `brain.process-proposal@1` (`FlowProposalJson`, `FlowProposalCheck`): process id and version, base version and hash (or none for a new process), author, time (host clock), language, summary, `needsReview` (always true), the whole document and its hash, the change list in English and Spanish, test results, route coverage, known gaps. `FlowProposalCheck.Against` re-checks a stored proposal: hash, loader, identity, tests, a newer version than the base, and that the approved base did not move.
- `brain.rule-proposal@1` is written by the existing `RuleProposalJson`; `RuleProposalFiles` adds the members `needsReview`, `needsReviewReasons` and `changes` after it, so the older parser reads the file unchanged. `needsReview` is false when the draft stays within the facts the rule reads and the outcomes it gives, and true otherwise, with the reasons `kind_changed`, `hit_policy_changed`, `new_fact_read`, `new_outcome_field`, `new_outcome_value` (`RuleReviewNeed`). A file without the member counts as needing review.
- Proposal files are named `process.<id>.v<version>` and `rule.<id>.v<version>`.

## Fix hints

`FixHints.For(code)` gives one sentence for each code of the loaders, validators and guided edits saying what to do about it. A test finds the codes by reflection over the constants of the libraries, so a new code without a hint fails the build.

## Safety of the workspace and concurrency

`FileAuthoringStore` writes only to `<root>/drafts` and `<root>/proposals`. No component of the path, from the root down, may be a link (a link is refused, and so is an existing link at the target); temp files get random names created with create-new semantics; ids that are Windows reserved names (`con`, `nul`, `com1`, …) and ids that differ only by case from an existing id are refused. Tool calls and store operations are serialized per tool set, and a read sees what the call before it wrote. The token of a simulation comes from `AuthoringOptions.SessionIds`; without it the library numbers them (`sim-1`), which suits a test or one trusted person. `BrainMcp.Create` supplies 24 random bytes unless the host set its own source; the MCP SDK registers one tool set for all connections, so a per-connection source is not possible.

## Errors of the tools

`authoring.tool.unknown`, `input.invalid`, `workspace.id_invalid`, `workspace.store_failed`, `catalog.invalid`, `flow.not_found`, `rule.not_found`, `rule.not_loading`, `proposal.not_found`, `proposal.unreadable`, `proposal.not_ready`, `proposal.base_stale`, `proposal.inconsistent`, and by area: `draft.` (`not_found`, `id_mismatch`, `base_not_found`, `not_json`, `too_large`), `patch.failed`, `rule.` (`edit_invalid`, `edit_failed`, `facts_invalid`), `scenario.` (`source`, `draft_not_loaded`, `name_taken`, `invalid`, `not_writable`), `simulation.` (`target`, `not_found`, `start_invalid`, `draft_not_loaded`, `draft_changed`, `command_failed`, `limit`). Each has a sentence in both languages (`refuse.<code>` in the string table).

## Not here

The MCP adapter, the `brain-mcp` stdio program and the end-to-end test (M2); packs (section 4.1 of the specification, undecided); any model call; activating or deploying a proposal; reading live cases or the host's database; authorization.

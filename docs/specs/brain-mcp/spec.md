# M: Brain MCP, modeling and refining flows and rules in conversation

Status: scope settled with the owner on 2026-11-23 (José is the first author; rules apply on his approval, flows wait for our review; packs are a direction, not scope). Owner asked for it on 2026-11-23 ("Brain needs its own MCP so we can model and refine processes conversationally with AI") and placed it after H2 and before H3. Normative once in the repository as `docs/specs/brain-mcp/spec.md`. Package version 0.7.0.

## 1. What it is for

A person says what they want in plain words ("if the patient has not answered after 48 hours, the front desk calls; never send a message at night"). An assistant, through this server, reads the current flow, writes a changed draft, has Brain check it and run it on made-up cases, corrects itself until Brain accepts it, and hands back a proposal a person can read: what changed in plain language, the diagram, the tests that pass. A person approves it elsewhere. The same for rules.

The server is the assistant's hands and eyes on Brain. It holds no model, no vendor and no key: the assistant is whatever client connects (a desktop assistant, a coding assistant, later the SzH app's own).

## 2. Three rules that do not bend

1. **The assistant proposes; it never activates.** No tool changes an approved document, activates a process, starts a real case or calls a port. Every result is a draft in a workspace or a proposal file.
2. **The engine is the judge.** A draft is "ready to propose" only when the loader accepts it, every embedded scenario passes, and the checks of section 6 pass. Tools return facts computed by Brain; nothing the assistant writes can mark a draft valid.
3. **Definitions and made-up cases only.** The server never reads the host's tables, a live case or patient data. Simulation input is typed by the person or invented by the assistant. Tool descriptions say so, and every text field that could carry free text (names of scenarios, notes) carries the standing warning against patient-identifying text.

## 3. Shape

- `Synapbiz.Brain.Authoring` (new library, pure, no network): the workspace model, the tools as plain functions with JSON in and JSON out and a JSON Schema each, the flow differ, the proposal documents, the checks. Everything testable without a transport. English and Spanish for every person-facing text; tool descriptions and error hints for the assistant are English.
- `Synapbiz.Brain.Mcp` (new library): adapts those functions to the MCP protocol with the official C# SDK (the same one the SzH local app uses), so a host can add Brain's tools to its own MCP server with one call and its own authorization.
- `brain-mcp` (new small program, a .NET tool): the same tools over stdio for an author's machine, pointed at a folder of documents. This is what a desktop or coding assistant connects to today.

The pure libraries of today gain nothing but small additions the tools need (section 5.4).

**Two authors, one set of tools (owner, 2026-11-23).** José, the pilot physician, models and debugs flows and rules himself: the SzH local app mounts `Synapbiz.Brain.Mcp` in the MCP server it already has, with the practice's real catalog, and he talks to his own assistant. Our team uses `brain-mcp` on a checkout. Because the reader may be a physician, every tool answer carries a `say` text in the asked language (English or Spanish) that the assistant can relay as is, next to the data; no answer requires the person to read JSON, a pointer or a code. A proposal carries `needsReview`: false for a rule proposal within the rule's existing facts and outcomes, true for every flow proposal and for a rule proposal that changes facts or outcomes. The host applies an approved rule proposal with `needsReview` false, and holds the others as waiting for our team. The server itself activates nothing.

**Delivery.** M1: `Synapbiz.Brain.Authoring` with everything headless (workspace, catalog, tools as functions, differ, proposals, checks, guide). M2: `Synapbiz.Brain.Mcp`, the `brain-mcp` program and the end-to-end test. One version, 0.7.0.

## 4. Workspace

A workspace is a folder the host names: approved documents (read only for the server) and a `drafts` area the server may write. A draft is a full document (`brain.process@1` or `brain.rule@1`) with a pointer to the approved version it came from (id, version, content hash), or none for a new flow. The server never writes outside `drafts` and `proposals`. The host supplies a **catalog** the drafts are checked against: the ports that exist (name, kind, delivery, input and output schema, whether it contacts a person), the events, the task queues, the stage names in use, the fact contracts of the rules. A draft that names a port, event or queue outside the catalog is refused as "not available here", with the list of what is.

### 4.1 Packs (owner's idea, 2026-11-23; NOT decided, not in 0.7.0 until he confirms)

The owner's intent: flows and rules are not meant to be changed often by each clinic. They are prepared by us as **packs** per specialty or type of clinic or health business, improved centrally, and offered as part of a support service. So the first author is our team, and what a clinic touches is small.

- A **pack** is a versioned folder with a manifest `brain.pack@1`: id, version, title and description in English and Spanish, what it is for (specialty, type of practice), the flows and rules it contains with their content hashes, the catalog it needs (ports, events, queues, fact contracts), and the **settings** it exposes.
- A **setting** is a named value a clinic may adjust without touching the structure: a wait in hours, a delivery window, a number of attempts, a queue, a message template key, a rule row value. Each has a type, a default, allowed limits and a label in both languages. Documents refer to settings by name; a clinic's values live in a small `brain.pack-settings@1` document that is checked against the limits. Everything outside the settings is the pack's and changes only with a new pack version.
- A workspace is a pack under authoring (or several). Drafts and proposals belong to a pack. Readiness adds: the pack's manifest is consistent, every setting is used, and every scenario passes at the default, the lowest and the highest allowed values of the settings it touches.
- **What changed between two pack versions**, in plain language in both languages: the flow and rule change lists of section 5.4 gathered per pack, plus settings added, removed or with new limits, and whether a clinic's current settings are still valid. This text is what support sends with an upgrade.
- Tools: `pack_list`, `pack_get`, `pack_diff(from, to)`, `pack_settings_check(pack, settings)`, and `pack_propose` (a `brain.pack-proposal@1` that gathers the flow and rule proposals of one change).
- Not here: distributing a pack to clinics, licensing, and installing an upgrade. Those belong to the host and the support service; this package gives them the manifest, the settings check and the change text.

## 5. Tools

### 5.1 Read and learn
- `brain_guide`: the format reference for assistants, in sections (step kinds, routes, timeouts, windows, loops, triggers, scenarios, labels, what a good flow has), plus two worked examples. Also exposed as MCP resources and as a prompt "model a flow with me" that sets the working method of section 7.
- `brain_catalog`: ports, events, queues, rules and stages available.
- `flow_list`, `flow_get` (document, outline, plain-language description in the asked language, diagram as nodes and edges), `rule_list`, `rule_get` (document, sentences per row).

### 5.2 Draft and check
- `flow_draft_save(id, document, basedOn?)`: stores the draft and returns, in one answer: loader errors (code, JSON Pointer, message, and a hint saying how to fix that code), catalog problems, the plain-language description, **what changed** against the base in plain language (section 5.4), scenario results, route coverage, and the readiness verdict with the reasons it is not ready.
- `flow_draft_patch(id, operations)`: the same result after applying JSON Patch operations to the stored draft, for a small change to a large document.
- `flow_draft_simulate`: start a made-up case and send commands (answer an effect, complete a task, send an event, let time pass); returns where the case is, what it waits for and what happened, in words and as data. Sessions live in memory and expire.
- `flow_draft_add_scenario(id, fromSimulation | scenario)`: adds a test to the draft from a simulation or as written.
- `rule_draft_edit(id, edits)`: the guided edits the rule workbench already has (rows, values, order), with the same full answer (errors, sentences, what changed, behavior change on the golden cases, tests).
- `rule_simulate(id, facts)` and `rule_test(id)`.

### 5.3 Propose
- `flow_propose(id, summary, language)`: only when the draft is ready. Writes `brain.process-proposal@1` to `proposals`: base (id, version, hash) or "new", the full new document and its hash, the change list in plain language, the scenario results and coverage at the time, who asked (a name the person gives) and when (host clock). Returns the path and the text a person will read.
- `rule_propose(id, summary, language)`: writes the existing `brain.rule-proposal@1`.
- `proposal_list`, `proposal_get`.

A proposal changes nothing. Applying it (a commit in the repository, an activation in the host) stays a person's step outside this server.

### 5.4 New headless pieces these tools need
- `FlowDiffer` and `FlowChangeDescriber`: what changed between two flow documents, as data and in plain language in English and Spanish: steps added, removed, renamed; a route that now goes elsewhere; a timeout, window, retry, loop limit or trigger that changed, with old and new value in words; labels changed; scenarios added or removed; and behavior change: every base scenario replayed on the new document, with those that now end differently named.
- `brain.process-proposal@1` with its serializer, parser and check, like the rule proposal.
- Fix hints: one sentence per loader and validator code saying what to do about it.
- The catalog model and its check.

## 6. Checks beyond the loader ("ready to propose")

1. The loader accepts the document and every scenario passes.
2. Every step and stage has a label in English and Spanish.
3. Every route is taken by at least one scenario, or the draft lists it as knowingly untested with a reason.
4. Every step that contacts a person is at most once per delivery, sits behind a delivery window, and has a route for a failed or unknown outcome.
5. Every `ai` step has a route that needs no AI.
6. Every loop is bounded by the document (the loader already demands it) and every wait has a timeout or the draft says why not.
7. Nothing outside the catalog.

Each failed check is returned as a code and a sentence; the host may add its own checks.

## 7. Working method the server teaches the assistant

Read before writing; restate the request in one sentence and ask the person what is ambiguous; change the smallest thing; save the draft and read Brain's answer; fix every error before anything else; add or adjust a scenario for each new behavior; simulate the case the person described and tell them, in their language, what the flow did; propose only when ready; never claim a change is live.

## 8. Tests

Every tool: schema, success, every refusal. The differ on pairs of documents covering every kind of change, golden texts in both languages. Proposal round trip. The checks, one failing fixture each. The stdio program driven by a scripted MCP client end to end: list, get, save a broken draft, read hints, fix, simulate, add scenario, propose. A test that no tool can write outside `drafts` and `proposals` (path traversal, symlink, absolute path) and that approved documents are byte-identical after any sequence of calls. Architecture tests: the authoring library is pure; only the MCP adapter references the SDK.

## 9. Not in this package

Any model call or vendor code. Activating or deploying a proposal. Reading live cases or the host's database. A screen for proposals (the flows panels can show a proposal later). Authorization (the host's). Generating port implementations.

## 10. Decisions for the owner

1. **Who uses it first.** Owner, 2026-11-23 4:53 PM ET: the pack model is NOT decided yet; for now José (the pilot physician) must be able to model and debug flows and rules himself with the assistant. So the first version serves two authors with the same tools: José, through the SzH local app's MCP server and his own assistant account, in Spanish or English, with no file, JSON or repository in sight; and our team on a checkout. Consequences for the design: the SzH host mounts `Synapbiz.Brain.Mcp` with its real catalog; every answer a person reads is plain language first; proposals are stored by the host and shown in the flows and rules panels; section 4.1 (packs and settings) stays a direction to confirm, not scope of 0.7.0.
1a. **What happens to José's proposal.** DECIDED by the owner (card, 2026-11-23 5:00 PM ET): a rule change José approves takes effect at his clinic; a flow change is reviewed by our team before it is activated. The server still activates nothing: the host applies an approved rule proposal, and holds a flow proposal as "waiting for review" until our team accepts it.
2. **How a flow is changed.** Recommended: the assistant writes the whole changed document as a draft and Brain reports what changed in plain language, rather than a fixed menu of small edits. It allows any change the format allows; the plain-language change list and the replay of every existing test are what keep it reviewable.
3. **Who may approve.** Recommended: a proposal for a flow is approved by the owner's team (structure, ports, contact with patients); a proposal for a rule within its existing facts and outcomes can be approved by the clinical partner who signs the rules. The server enforces neither; the host and the repository do.
5. **What a clinic may change.** Recommended: only the settings a pack exposes, within its limits, with no structure editing; a request beyond that is a support request that we turn into a new pack version (or, exceptionally, a clinic-specific variant we maintain).
6. **How a clinic receives an improved pack.** Recommended: never by itself. Support offers the new version with the plain-language change text; someone at the clinic (or we, under the support contract) accepts it; cases already running finish on the version they started with.
4. **What the assistant may see.** Recommended: the definitions (flows, rules, catalog) go to the assistant the person chose, under that person's own account; never case data. Flow and rule texts are business knowledge of the practice and of the product, so the person connects only an account they trust with it.

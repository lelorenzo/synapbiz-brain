# Brain W1: durable process engine (design for owner review)

Status: APPROVED by the owner on 2026-11-22 (Tampa), decisions 1 to 9 of section 12 as recommended. Target repository: `lelorenzo/synapbiz-brain`.
Normative spec of the first PR: [spec.md](spec.md). Plan: [plan.md](plan.md).

## 1. What exists today (verified in the checkouts)

- `synapbiz-brain` 0.1.0 ships only the Decision Engine: `Synapbiz.Brain.Contracts`, `.Engine` (byte-for-byte copies of nuvora, guarded by `tools/parity`) and `.Spec` (`brain.rule@1`). There is no workflow code in it.
- nuvora's Workflow Engine (`src/Shared/Nuvora.Workflows*`, about 3,900 lines) is an **approval/stage overlay on aggregate state machines** (`OverlayPlanner`, `AdvancePlan`, `IOverlayParticipant`). It has no timers, schedules, loops, durable waits, retries or AI steps.
- The name `brain.flow@1` is **already taken** in nuvora: `src/Platform/Spec/Synapbiz.Spec.Brain/Schemas/brain.flow.v1.json` defines it as that overlay (module, aggregateType, slot, states of kind Stage/Approval), with conformance fixtures. The strategy brief S2 proposed the same name for the new long-running format; that would collide at the nuvora swap.

Conclusion: what SzH needs is a new, additive capability. Keeping nuvora's signatures compatible is achieved by not touching the copied assemblies at all.

## 2. Scope

A deterministic, data-defined, durable process engine: one long-running instance per case (per appointment), from pre-visit to collected money, able to express steps, bounded loops, timers, schedules, durable waits for external events with timeout, human tasks, retries and AI steps under contract.

Not in scope: a general BPMN engine, Temporal-style code replay, a visual designer, parallel branches (v1), an expression language, the `Case` aggregate itself (owned by the health-mcp spec S2), any connector (messaging, EHR, clearinghouse live behind ports in the consumer).

## 3. Packages

| Package | Content | Depends on |
| --- | --- | --- |
| `Synapbiz.Brain.Process` | `brain.process@1` schema, loader and validator, the pure interpreter, scenario (golden) runner | `Synapbiz.Brain.Spec` |
| `Synapbiz.Brain.Host.Postgres` | durable store, worker, timers, inbox, outbox, schedules, migrations | `Synapbiz.Brain.Process`, Npgsql / EF Core |

`Synapbiz.Brain.Process` obeys the existing purity rule: no clock, GUID, randomness, file or network. Time and identities arrive as parameters. The host owns every side effect.

## 4. Execution model

The interpreter is a pure function:

```
Advance(definition, instanceState, trigger, now) -> (newState, effects[], timers[], waits[], history[])
```

`trigger` is one of: start, effect result, timer fired, external event, human task completed, operator command. The host loads the instance, calls `Advance` until the instance blocks, and commits state, history, timers, waits and outbox rows in **one transaction**. Effects run after the commit; their results come back as triggers.

Consequences: state changes are exactly-once; replaying history never re-executes a read, a send or a model call (the recorded result is reused); a scenario test drives the same function with a virtual clock and fake ports.

## 5. Format `brain.process@1` (proposed name, see decision 1)

Same envelope as `brain.rule@1` (`$spec`, `id`, `version`, `uses`, `requires`, `meta`, `body`). Body:

| Member | Meaning |
| --- | --- |
| `subject` | the case type this process runs for (opaque to Brain) |
| `triggers[]` | how instances start: `event` (name), `schedule` (cron + IANA time zone + misfire policy), `manual`, `parent` |
| `variables` | typed declarations (same value types as rule facts); the only state a step may read or write |
| `stages[]` | ordered labels (for example previsit, visit, postvisit, collected); each step belongs to one; shown by the cockpit |
| `start`, `steps{}` | the step graph |
| `scenarios[]` | the process's own tests: timeline of events and clock advances, expected step trace |

Step kinds:

| Kind | Does | Blocks until |
| --- | --- | --- |
| `read` | asks a named fact port for named facts, stores them in variables | effect result |
| `decide` | evaluates a `brain.rule@1` reference; stores outcome and provenance (rule id, version, facts hash) | never (pure) |
| `action` | calls a named action port (send SMS, send email, create task in EHR) with an idempotency key | effect result |
| `humanTask` | puts a task in a named queue with allowed outcomes | task completed or timeout |
| `ai` | calls a model through a port under an input/output contract (section 8) | effect result |
| `wait` | waits for a named external event matched by a correlation key, with `timeout` | event or timeout |
| `timer` | `after` (ISO 8601 duration) or `until` (a Date variable, optional offset) | due time |
| `loop` | bounded repetition: `maxIterations` is required; `forEach` over a list variable or `while` a decide outcome holds | body |
| `poll` | sugar for read + decide + timer: `every`, `backoff`, `maxDuration`, then `onExpired` | condition or expiry |
| `subprocess` | starts a child instance (own id and version) and optionally waits for it | child end |
| `end` | closes the instance with a declared result | |

Routing: each step declares `next` or `on: { <outcome value>: <step> }`, plus `onTimeout`, `onError`, `onNotAvailable`, `onNotEntitled` where they apply. There is **no expression language**: any branch that needs logic goes through a `decide` step, so the rule engine remains the only place that decides.

Validator rules (load-time, fail loudly): every referenced step, rule, port, event and variable is declared; every enum outcome is routed; a cycle must pass through a `loop`, `timer`, `wait` or `humanTask`; `maxIterations` present; contact actions declare a delivery window; no unreachable step.

Synthetic example (pre-visit outreach):

```json
{ "$spec": "brain.process@1", "id": "previsit.outreach", "version": 1,
  "uses": [{ "$ref": "rule/previsit.missing_items" }], "requires": ["previsit@1"], "meta": {},
  "body": {
    "subject": "case", "triggers": [{ "event": "case.opened" }],
    "stages": ["previsit"], "start": "check",
    "steps": {
      "check":   { "kind": "read", "port": "ehr.previsit_facts", "next": "missing" },
      "missing": { "kind": "decide", "rule": "previsit.missing_items",
                   "on": { "none": "done", "some": "remind" } },
      "remind":  { "kind": "loop", "maxIterations": 2, "body": "send", "onExhausted": "escalate" },
      "send":    { "kind": "action", "port": "messaging.notice", "window": "contact_hours",
                   "retry": { "maxAttempts": 3, "backoff": "exponential", "initial": "PT1M" },
                   "next": "reply" },
      "reply":   { "kind": "wait", "event": "patient.form_submitted", "timeout": "PT24H",
                   "on": { "received": "check" }, "onTimeout": "continue" },
      "escalate":{ "kind": "humanTask", "queue": "front_desk", "outcomes": ["resolved", "unreachable"],
                   "on": { "resolved": "check", "unreachable": "done" } },
      "done":    { "kind": "end", "result": "previsit_closed" }
    } } }
```

## 6. State model (PostgreSQL, schema owned by the host package)

| Table | Purpose |
| --- | --- |
| `process_instance` | id, tenant, definition id + version + content hash (pinned), `business_key` unique per tenant and definition, status (running, waiting, suspended, completed, failed, cancelled), stage, variables (jsonb), revision, parent, lease |
| `process_history` | append-only: sequence, step, iteration, attempt, kind, status, input hash, output, provenance, error code, times. This is the audit trail and the cockpit's activity feed |
| `process_timer` | due time, instance, step, fired flag |
| `process_wait` | instance, step, event name, correlation key, expiry |
| `process_inbox` | external events, unique event id (dedupe), consumed flag |
| `process_outbox` | effects to run: deterministic effect id, port, payload, status, attempts, next attempt, lease |
| `process_schedule` | schedule id, cron, time zone, next fire, unique (schedule, fire time) |

One writer per instance (row lease plus optimistic `revision`). Tenant id on every table and every query. Times stored in UTC; cron and windows evaluated in the declared IANA zone (a skipped local time fires at the next valid instant, a repeated one fires once).

Data protection: the engine stores references where possible and treats variables and payloads as opaque. Payload columns go through an `IPayloadProtector` hook (encryption owned by the consumer). Logs and metrics carry only ids, step names and error codes, never payloads. All fixtures and scenarios are synthetic.

## 7. Scheduler and worker

One `BackgroundService` inside the consumer process, no broker. Each tick (default 5 s) it claims, with `FOR UPDATE SKIP LOCKED` and a lease: due timers, expired waits, unconsumed inbox rows, due outbox rows, due schedules. Safe with several workers. Clock through `TimeProvider`. A crashed worker's lease expires and another takes over. Timer precision is the tick; that is enough for hours-to-weeks waits.

Schedules start instances or signal them (daily schedule refresh = a schedule-triggered process per practice that opens or signals the day's cases). Misfire policy per schedule: `skip` or `fireOnce`.

## 8. AI step contract

Declared in the step: input JSON schema, output JSON schema, pinned model id, prompt id + version, `validate` (a `brain.rule@1` that checks the output), `onInvalid` and `onLowConfidence` (normally a human task), `maxAttempts`. The host records model, prompt version, input hash and output in history; a replay reuses the record. Control flow can depend on an AI output only through the validating rule and declared enum fields. The engine never calls a model itself: the consumer supplies `IAiStepExecutor`. Each AI step ships a synthetic evaluation set.

## 9. Idempotency and retries

- External events: deduplicated by event id in the inbox; an event with no waiting step is buffered if the definition lists it under `accepts`, otherwise rejected with a typed error.
- Effects: at-least-once delivery with a deterministic idempotency key (`instance/step/iteration`) handed to the port. Ports that support keys dedupe on their side.
- Typed errors: `transient`, `rate_limited` (with retry-after), `not_available`, `not_entitled`, `permanent`. Only `transient` and `rate_limited` are retried, per the step's `retry` policy (fixed or exponential, capped). When attempts run out the step takes `onError`; with no `onError` the instance is suspended and an operator task is raised. Nothing fails silently.
- Unknown outcome of a contact action (crash between send and record): see decision 6.
- Guards: per-instance step budget and per-loop `maxIterations` stop runaway definitions.

## 10. Versioning

An instance is pinned to the definition version and content hash it started with. A new version applies to new instances only. Moving a running instance is an explicit operator command with a declared step mapping (later work).

## 11. The case lifecycle on top of it (reference flows, synthetic)

One root instance per case with `business_key` = case key, and one subprocess per stage, so each stage is versioned on its own and a stage the practice is not entitled to never starts:

1. Pre-visit: read, decide what is missing, contact, wait for reply, remind, escalate to front desk.
2. Visit: `poll` until the note exists and is signed (EHR port), expiry raises a task.
3. Post-visit coding: decide opportunities, optional AI step under contract, **physician review as a human task** (straight to review, never straight to bill).
4. Collection: `wait` / `poll` for claim submitted, claim status, denial, payment. These are observed through connector ports; SzH does not submit or replace eCW billing. A practice with no such connector gets `not_available`, routed to a human confirmation task. The case ends on paid, written off or manual close.

Brain stays generic: it knows instances, stages, variables and ports, nothing about patients or eCW.

## 12. Owner decisions (all approved as written, 2026-11-22)

1. Format name `brain.process@1`, leaving nuvora's `brain.flow@1` untouched.
2. Two new packages; do not extract nuvora's overlay engine now.
3. Interpreted, step-persisted state machine; one instance per case; stages as subprocesses.
4. PostgreSQL only now, store behind a port.
5. Running instances stay on their version.
6. Contact actions: no automatic resend when the outcome is unknown; raise a task.
7. v1 without parallel branches and without an expression language.
8. Delivery window as data on every contact step.
9. Delivery order and who codes.

## 13. Delivery plan

| PR | Content | Gate |
| --- | --- | --- |
| W1-A | spec trio, schema, loader/validator, pure interpreter (all step kinds), scenario runner, unit + architecture tests | build, tests, coverage, purity |
| W1-B | Postgres host: store, migrations, worker, timers, waits, inbox, outbox, retries; integration tests on a containerized Postgres | same + integration |
| W1-C | schedules (cron, time zones, misfire), delivery windows, `poll` | same |
| W1-D | four synthetic reference flows with scenarios, consumer guide, CHANGELOG, version 0.2.0 ready for Luis's tag | same |

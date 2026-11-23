# W1-G: timeouts tied to an instant, interruptible delivery windows, display labels

Status: normative for package G (2026-11-23). The owner chose the order "G first, then H1, H2, H3" on 2026-11-23. Source of the need: the design of the SzH autonomous flows (follow-up "G", items G1 to G3).

All three items are additions to `brain.process@1`. A document that uses none of them loads, runs and serializes its state exactly as before (byte-identical state text and history for every existing scenario). A document that uses G1 or G2 is refused by a loader older than this package (unknown member or wrong type), which is the intended signal. Package version 0.4.0.

## G1. A timeout tied to an instant

### Format

`timeout` on a `wait` and on a `humanTask` is either a duration text (as today) or an object:

```json
"timeout": { "until": "visit_at", "offset": "-PT2H" }
```

- `until` names a declared variable of type `Instant`; another type is `process.variable.type_mismatch`, an undeclared name is the existing unknown-variable error. `offset` is optional, a signed duration with the same rules as the `offset` of a `timer` step.
- `onTimeout` stays required with `timeout` and forbidden without, in both forms.
- An object with another member, or with both a duration text and an object, is the existing error for a malformed member at that path.

### Run time

On entering the step, in this order:

1. The existing inbox rule runs first for a `wait`: a matching buffered event is consumed and the step leaves through `next`; no timeout is computed.
2. The due instant is the value of the variable plus the offset. A variable with no value suspends the instance with the new code `process.timeout.instant_missing` (detail: the variable name). Resuming enters the step again.
3. A due instant at or before the clock: the step does not request a wait or a task; it records `TimedOut` and goes to `onTimeout` at once.
4. Otherwise the step requests its wait or task and a `TimerRequest` with the timer id of a timeout today (`I/S/E/timeout`) due at that instant. Everything after that (the event or the task wins and cancels the timer; the timer wins and cancels the wait or task) is the existing behavior.

The due instant is fixed when the step is entered. A later change of the variable does not move a pending timeout; the flow re-enters the step if it wants a new deadline.

The scenario player needs no new member: `advance` already fires timers by their due time.

## G2. A delivery-window wait that an event can interrupt

### Format

An `action` that names a `window` may add:

```json
"interruptBy": { "event": "case.reply_received", "correlation": "case_ref", "output": { "reply_kind": "kind" }, "to": "reply_arrived" }
```

- `event`, `correlation?` and `output?` follow the rules of a `wait` step (same validation, same codes). `to` is a step id (unknown step: the existing error).
- `interruptBy` without `window` is the new error `process.action.interrupt_requires_window`.

### Run time

At every gate of the action (before the first attempt and before each retry, where a `WindowRequest` is emitted today):

1. If a matching event is already in the inbox, it is consumed, no `WindowRequest` is emitted, the history records `WindowInterrupted`, `output` is assigned and the step goes to `to`.
2. Otherwise the interpreter emits the `WindowRequest` as today and also a `WaitRequest` for the event. The pending kind stays `Window`; the state gains the wait id as an additive member written only in this case.
3. `EventReceived` that matches, while the gate is pending: the interpreter emits `CancelTimerRequest` for the window timer, records `EventConsumed` and `WindowInterrupted`, assigns `output` and goes to `to`. The effect is not requested.
4. `TimerFired` for the window: the interpreter emits `CancelWaitRequest`, records `WindowOpened` and requests the effect as today. From then on the event does not interrupt: an event that arrives while the effect is outstanding is buffered by the existing inbox rule.
5. `WindowFailed`, cancel and suspend also cancel the wait.
6. An event whose payload does not fit `output` follows the existing rule for an awaited event with a bad payload.

A retry delay still runs before the gate and is not interruptible; only the gate is.

### Host

`Synapbiz.Brain.Host.Postgres` stores the wait row and the window timer in the same transaction as today's requests and routes the event through the existing path. No schema change is expected; if one is needed it is migration `0005`. Tests on PostgreSQL: interrupted before the window opens (no port call, timer row gone), event after the opening (port called once, event buffered), event and timer racing (exactly one outcome, many rounds, no deadlock), tenant isolation of the wait.

## G3. Display labels (no format change)

`meta` is already a free object. This package defines a convention inside it and a reader; it does not make any member of `meta` an error.

```json
"meta": {
  "title": "Pre-visit outreach",
  "display": {
    "en": { "title": "Pre-visit outreach", "stages": { "previsit": "Before the visit" }, "steps": { "send": "Send reminder" }, "results": { "ready": "Ready" }, "queues": { "front_desk": "Front desk" }, "outcomes": { "approved": "Approved" } },
    "es": { "title": "Contacto previo a la visita", "steps": { "send": "Enviar recordatorio" } }
  }
}
```

- Language keys are lower-case two-letter codes; each group (`stages`, `steps`, `results`, `queues`, `outcomes`, `events`, `windows`) is an object of id to non-empty text of at most 120 characters. Every group and the title are optional.
- `ProcessDisplay.From(BrainProcess)` returns a typed reader: `Title(language)`, `Stage(language, id)`, `Step(language, id)`, `Result(...)`, `Queue(...)`, `Outcome(...)`, `Event(...)`, `Window(...)`. An event id is an event name the document uses in a `wait`, an `interruptBy` or an event trigger; a window id is a name under `windows`. Fallback order: the asked language, then `en`, then null (the caller shows its own text or the id). The reader never throws on a `meta` of any shape.
- Diagnostics are warnings only, never errors, and never prevent loading: `process.display.invalid` (a `display` member of the wrong shape, at its path) and `process.display.unknown_id` (a label for a stage, step, result, queue, outcome, event or window the document does not have). The loader exposes them through the existing diagnostics list with a warning severity; if the loader has no severity today, the warnings are returned by `ProcessDisplay.Validate(BrainProcess)` instead and the loader is unchanged. The implementer reports which.
- The six reference documents in `samples/process-reference/` gain `meta.display` in `en` and `es`, synthetic wording, and a test that every stage and step of each has a label in both.

## Tests common to the package

- Conformance cases for every new validation rule, valid and invalid.
- Scenarios for each run-time rule above, including the orderings (buffered event first; due instant in the past; missing value; resume).
- Every existing scenario, state text and history is unchanged (the existing golden tests are the proof; none is edited).
- The independent scenario player agrees on the new scenarios.
- The reference flows use G1 where the SzH design asks for it only if that needs no change to an existing scenario; otherwise a new sample document shows G1 and G2 together.

## Documents

`docs/reference/brain-process-v1.md` (members, run-time rules, codes, history kinds, the state member), `docs/reference/process-host-postgres.md` (the interruptible gate), the guide (labels in a cockpit, with a compiled example), overview, CHANGELOG, `tasks-g.md`, version 0.4.0.

## Not in this package

A timeout tied to an instant on `poll` (`maxDuration`) or on `subprocess`; interrupting a retry delay or an outstanding effect; per-language text for task `show` values; editing labels in the workbench; any model call.

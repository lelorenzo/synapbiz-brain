# Tasks — W1-G, timeouts tied to an instant, interruptible delivery windows, display labels

Spec: [spec-g-format-followups.md](spec-g-format-followups.md). Additions to `brain.process@1` and to `Synapbiz.Brain.Process`; `Synapbiz.Brain.Host.Postgres` is verified and needs no change.

## Code

- [x] G1: model, reader, schema and validation of `timeout` as `{ "until", "offset" }` on `wait` and `humanTask` (`process.variable.type_mismatch` for a variable that is not an `Instant`, the existing unknown-variable error for an undeclared name, the existing malformed-member error for another shape)
- [x] G1: run time in the order of the spec (buffered event first, due instant, past instant to `onTimeout` with `TimedOut`, otherwise the request plus a `TimerRequest` with the timer id of a timeout); `process.timeout.instant_missing` suspends and a resume enters the step again
- [x] G2: model, reader, schema and validation of `interruptBy` (the rules and codes of a `wait`, `to` a step id, `process.action.interrupt_requires_window` without a window)
- [x] G2: the gate (buffered event, `WindowRequest` plus `WaitRequest`, event interrupts and cancels the window timer, timer cancels the wait, `WindowFailed`, cancel and suspend cancel the wait, bad payload follows the rule of an awaited event); `ProcessPending.InterruptWaitId` written only in this case; history kind `WindowInterrupted`
- [x] G3: `ProcessDisplay` (`From`, `Validate`, the typed readers) with the fallback asked language, `en`, null, and the warnings `process.display.invalid` and `process.display.unknown_id`
- [x] G3: `meta.display` in the six reference documents, `en` and `es`
- [x] Version 0.4.0 in `Directory.Build.props`

## Tests

- [x] Conformance cases (23, appended) for every new validation rule, valid and invalid, with codes and paths
- [x] `TimeoutAtTests` and `InterruptGateTests`: every run-time rule and ordering, state round trip, byte-identical state for documents that do not use the additions
- [x] `DisplayTests`, `ReferenceLabelTests` (every stage and step of the six documents labelled in both languages), `ReminderExampleTests` (the new fixture is on the reference page verbatim, loads, its scenarios pass, the independent player agrees)
- [x] PostgreSQL: `GateTests` (interrupted before the window opens, event after the opening, event and timer racing for 200 rounds, tenant isolation, cancel) and `TimeoutAtHostTests` (fires at the instant, past instant with no task row and no wait row, missing value)
- [x] `GuideTests`: the labels of the cockpit example
- [x] No existing test, scenario, fixture or golden file is edited; the existing ones are the proof that nothing changed

## Documents

- [x] `docs/reference/brain-process-v1.md`, `docs/reference/process-host-postgres.md`, `docs/guides/process-engine.md` (labels in a cockpit, compiled example), `docs/architecture/overview.md`, `samples/process-reference/README.md`
- [x] `CHANGELOG.md`: a W1-G bullet under `## 0.4.0 (2026-11-23)`, an empty `## Unreleased` above; `docs/consuming.md` and the guide name 0.4.0

## Open

- [ ] Independent verification

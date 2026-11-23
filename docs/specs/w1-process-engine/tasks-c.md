# Tasks — W1-C, triggers, schedules, delivery windows and `poll`

Spec: [spec-c-triggers.md](spec-c-triggers.md). The core is the pure library `Synapbiz.Brain.Process`; the host is `Synapbiz.Brain.Host.Postgres`.

## Core (`Synapbiz.Brain.Process`)

- [x] Spec copied unchanged to `docs/specs/w1-process-engine/spec-c-triggers.md`
- [x] Format: `triggers` (event and schedule declarations), `windows`, action `contact` and `window`, step kind `poll`, scenario `zones`; schema `brain.process.v1.json` extended
- [x] `LocalTimes` (gap and fold rules on the wall clock of a `TimeZoneInfo`) and `CronSchedule` (`TryParse`, `Text`, `Next`)
- [x] `DeliveryWindows.NextOpen`
- [x] Loader: `ModelReader`, `ProcessValidator` and the new codes (`process.action.window_required`, `process.window.invalid`, `process.window.unknown`, `process.trigger.*`, `process.poll.too_many_rounds`)
- [x] `BrainProcess.Triggers`, `ProcessTriggerDeclaration`, `EventTriggerDeclaration`, `ScheduleTriggerDeclaration`, `ProcessMisfire`
- [x] Interpreter: `WindowRequest`, `WindowFailed`, `ProcessPendingKind.Window`, history kinds `WindowWaiting`, `WindowOpened`, `PollExpired`; the window gate before every attempt; `process.window.zone_missing`
- [x] Interpreter: the `poll` step (rounds, fixed and exponential intervals, `max`, expiry, ids, resume) and the `poll` member of the state JSON (written only while a poll runs)
- [x] Scenario runner: `zones`, window answers, `complete` and `fail` on a poll
- [x] Tests: cron table, `Next` across both transitions and a zone without daylight saving, strictly increasing iteration
- [x] Tests: delivery windows (open now, before, after, weekend, `24:00`, gap, fold, argument checks) and the window flow (zone missing, `WindowFailed` with and without `onError`, retry passes the gate again, cancel while waiting)
- [x] Tests: poll (first round, later round, expiry, exponential with `max`, port failure routes, rule error, state round trip mid-poll, ids)
- [x] Tests: validator codes, schema error locations of the new constructs, old state text still loads, malformed `poll` member
- [x] Fixtures `followup.contract.json`, `followup.rule.json`, `followup.process.json` (synthetic) with five scenarios, shown verbatim on the reference page and checked by a documentation test
- [x] Docs: `docs/reference/brain-process-v1.md`
- [x] Existing fixtures unchanged; line coverage of the library not lower than before

## Host (`Synapbiz.Brain.Host.Postgres`)

- [x] Migration `0002_triggers.sql`: `process_activation`, `process_event_trigger`, `process_schedule`
- [x] `IProcessEngine.ActivateAsync` and `DeactivateAsync`; `ProcessActivation`, `ActivationResult`; zone resolution behind one internal seam
- [x] Event triggers: `PublishEventAsync` starts one instance per business key inside the existing de-duplication
- [x] Schedules: first step of the pump pass, `skip` and `fireOnce`, `ScheduleMisfireGrace`, `next_fire_at`, business key `@schedule/{scheduleId}/{occurrence}`
- [x] Windows: `WindowRequest` stored as a timer at `DeliveryWindows.NextOpen`; `WindowFailed` when the zone cannot be resolved
- [x] Applying `WindowRequest` and the new history kinds in the request switch
- [x] Tests on PostgreSQL with a fake clock: activation, schedule concurrency (4 pumps), misfire policies, grace boundary, both daylight saving transitions, input mappings, deactivation, event triggers, tenant isolation, protected input, contact action at the opening, unknown zone, poll end to end, logging hygiene
- [x] Architecture guards unchanged except for what spec B allows
- [x] Docs: host reference page, architecture overview, CHANGELOG under Unreleased, `plan.md`
- [x] Gates of spec B section 9, the three concurrency tests 20 times in a row each

## Repair after independent verification (spec section 10)

- [x] R1 (10.1): total reading of the business key of an event (trimmed, at most 200 characters, no control character, no unpaired surrogate, not starting with `@`); a bad key or an unreadable payload never throws, never stops the event from being recorded and routed to waiting instances, and is logged as `process.trigger.no_business_key` with ids only; a mapped value with U+0000 suspends a step that would wait on it as a correlation (`process.wait.correlation_invalid`); tests per bad shape
- [x] R2 (10.4): `BrainProcessLoader.Load` is total; a number a schema cannot compare by value (`1e400`, `1e308`, thirty digits) in `until`, `stages`, `requires` or `uses` is a `spec.structure.schema` violation at its path; the same number in an `ai` schema evaluation at run time never throws; a leaf-replacement fuzz over both reference documents
- [x] R3: two tenants with the same process id and schedule id are isolated in the activation lookup, the schedule lock and firing; a schedule row locked by another connection is skipped without waiting and fires on a later pass; both checked by mutation (the `tenant_id` predicate dropped from the lookup and from the lock query, `skip locked` removed)
- [x] R4 (10.2): the business key of a schedule start is `@schedule/{scheduleId}/{yyyyMMddTHHmmZ}`; code, tests and docs
- [x] R5 (10.3): the activation `Zone` and the value of a window zone variable must have the shape `UTC` or `Area/Location` before the resolver is asked (`ArgumentException` at activation; `WindowFailed` with `process.window.zone_unknown` for a window); `DeliveryWindows.IsZoneIdShape`
- [ ] R6 (10.5): `DeliveryWindows.NextOpen` for a window whose opening or closing time falls in a repeated local hour; stopped on a question (the wall-clock reading and the first-occurrence interval reading of the specification give different answers); the brute-force oracle test covers every minute of a fortnight around both transitions of three custom zones (one with a change of thirty minutes) for the windows that do not touch the repeated hour
- [x] R7 (10.7): `BrainProcess.Triggers`, the `Input` maps of the triggers and `WindowRequest.Days` are read-only copies
- [x] R8: coverage margin; tests for the reachable uncovered lines, the unreachable ones are named in the report
- [x] R9 (10.6): reference pages updated; the three documented behaviours of cron, the window timer that is not re-checked and the contact action that may be repeated after the window closed

## Independent verification

- [ ] Independent verification of every gate

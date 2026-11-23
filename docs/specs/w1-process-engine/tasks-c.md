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

- [ ] Migration `0002_triggers.sql`: `process_activation`, `process_event_trigger`, `process_schedule`
- [ ] `IProcessEngine.ActivateAsync` and `DeactivateAsync`; `ProcessActivation`, `ActivationResult`; zone resolution behind one internal seam
- [ ] Event triggers: `PublishEventAsync` starts one instance per business key inside the existing de-duplication
- [ ] Schedules: first step of the pump pass, `skip` and `fireOnce`, `ScheduleMisfireGrace`, `next_fire_at`, business key `{scheduleId}/{occurrence}`
- [ ] Windows: `WindowRequest` stored as a timer at `DeliveryWindows.NextOpen`; `WindowFailed` when the zone cannot be resolved
- [ ] Applying `WindowRequest` and the new history kinds in the request switch
- [ ] Tests on PostgreSQL with a fake clock: activation, schedule concurrency (4 pumps), misfire policies, grace boundary, both daylight saving transitions, input mappings, deactivation, event triggers, tenant isolation, protected input, contact action at the opening, unknown zone, poll end to end, logging hygiene
- [ ] Architecture guards unchanged except for what spec B allows
- [ ] Docs: host reference page, architecture overview, CHANGELOG under Unreleased, `plan.md`
- [ ] Gates of spec B section 9, the three concurrency tests 20 times in a row each

## Independent verification

- [ ] Independent verification of every gate

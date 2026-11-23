# Tasks — W1-D, reference flows, consumer guide and version 0.2.0

Brief: `docs/briefs/` (W1-D). Plan: [plan.md](plan.md). No engine feature is added and nothing under `src/` changes; a flow the format cannot express is a finding, not a patch.

## Reference documents (`samples/process-reference`)

- [x] `previsit.outreach`: read what is missing, decide with a rule, a contact action (`atMostOnce`, `contact: true`, delivery window, `onUnknown` to a person), wait for the reply, bounded loop of three reminders, front desk tasks
- [x] `visit.note_wait`: poll of the note status with growing intervals, a rule that says when it is signed, a person after two days or when there is no connector
- [x] `postvisit.coding_review`: a collect rule for the opportunities, an optional `ai` step under contract (model, prompt and version, schemas), a rule that checks the suggestion, always a physician review
- [x] `collection.followup`: wait for the claim, bounded loop, poll of the claim status, a rule that routes paid, denied and pending; it only observes
- [x] `case.lifecycle`: the root, started by the event `case.opened`, one child process per stage, an operations task when a child fails
- [x] `practice.daily_refresh`: a schedule at 06:00 in the zone of the activation that reads the day's case list
- [x] Rules, contracts and goldens for the seven decisions
- [x] 77 scenarios between the six documents; every route of every branching step is taken
- [x] `README.md`: layout, ports, events, triggers and inputs, human task queues and outcomes, rules, scenarios
- [x] Synthetic data only: case references such as `c-1001`, codes such as `C-100`, a placeholder model id; no personal, clinic, vendor, payer, product or model names

## Tests

- [x] `ReferenceFlowTests` (Process.Tests): load with zero diagnostics, rule goldens, every scenario, an independent scenario player, route coverage per branching step (checked by mutation), every step entered, contact actions are `atMostOnce` with `onUnknown` to a task, the collection process never acts, nothing reaches `coding_approved` without the physician review, the README names every port, event, queue, outcome, result and process
- [x] `LifecycleFlowTests` (Host.Postgres.Tests): the case from `case.opened` to `collected` with the four children, the root stage at each point, the contact held outside its window, history compared with the pure interpreter's
- [x] One path per other root result (`closed_not_billed`, `closed_no_visit`, `written_off`, `manual_close`, and a cancelled child)
- [x] `practice.daily_refresh` activated for a tenant and fired once with the right local date (two zones, one a day ahead of UTC)
- [x] Two tenants with the same case reference do not interfere
- [x] Logging hygiene across the whole case
- [x] No sleeps and no timing assumptions: a fake clock and `RunOnceAsync` only
- [x] `GuideTests`: the guide shows every region of `GuideExamples.cs` and no other C#; the examples that can run, run
- [x] The new host test class run 10 times in a row

## Consumer guide

- [x] `docs/guides/process-engine.md`: what the engine is and is not; packages; writing a process; registering the host and the migrator; the four ports with compiling examples; activation; starting by event; completing tasks from a UI; a cockpit; delivery guarantees and `Unknown`; data protection; operations; versioning

## Release

- [x] `Directory.Build.props` `<Version>` 0.1.0 to 0.2.0 (the only change to that file)
- [x] `CHANGELOG.md`: `Unreleased` content under `## 0.2.0 (2026-11-23)`, a W1-D bullet, an empty `## Unreleased` above
- [x] `README.md`, `docs/architecture/overview.md`, `docs/consuming.md`, `plan.md`
- [x] Release build ends with the two base warnings; the whole solution tests; `dotnet pack` gives seven packages at 0.2.0
- [x] No change under `src/`, `.github/`, `tools/`, `Directory.Packages.props`, the solution files or the architecture tests; no tag created

## Independent verification

- [ ] Independent verification

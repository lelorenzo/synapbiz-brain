# Tasks — W1-F, purging cases from the PostgreSQL host

Spec: [spec-f-purge.md](spec-f-purge.md). Delete commands added to `Synapbiz.Brain.Host.Postgres`; `Synapbiz.Brain.Process` and every other package are unchanged in content.

## Code

- [x] Public enums `PurgeMode` and `PurgeStatus` (ordinals from 1) and records `PurgeCounts`, `PurgeResult`, `PurgeBatchResult`, `PurgeEventsResult` in `Hosting/IProcessEngine.cs`
- [x] `IProcessEngine`: `PurgeInstanceAsync`, `PurgeFinishedBeforeAsync`, `PurgeEventsBeforeAsync`, implemented in `ProcessEngine` with the guard of the newest methods (`Guard.Id`, `Guard.NoControlCharacters`, `Guard.InRange`)
- [x] Internal port: `IProcessStore.ScanFinishedRootsAsync` and `PurgeEventsAsync`; `IProcessSession.LockTreeAsync` and `DeleteTreeRowsAsync`, both guarded by the instance lock
- [x] `PostgresProcessStore.Purge.cs` (scans and event delete) and the session operations of the purge: `ReadTreeAsync` (unlocked walk without a depth bound, `union` so a loop in the parent links ends, tenant at every level), `TryLockInstancesAsync` (all ids of the tree in one `for update skip locked` statement) and `DeleteTreeRowsAsync` (refuses an id the session does not hold)
- [x] One transaction per tree: history, timers, waits, tasks, effects, inbox, then the instances; the counts are the affected-row counts of the statements
- [x] `PurgeFinishedBeforeAsync`: scan oldest first, one attempt per tree (no retry, no wait), everything re-checked under the locks, `More` from a final scan
- [x] Pump: the result of a port call for a purged instance is dropped and `process.effect.orphaned` is logged (`HostLog`, EventId 21); `process.instance.purged` is EventId 20
- [x] `Session.DisposeAsync` no longer lets the rollback of a transaction that Npgsql already ended throw `ObjectDisposedException` over the real error
- [x] Migration `0004_purge.sql`: the finished-roots index and the event receipt index, additive

## Tests (PostgreSQL, the existing rig)

- [x] `PurgeTests`: a tree with children and a grandchild and exact counts per table, another tree and another tenant untouched; `NotFound`, `not_root`, `not_finished` for each status, other tenant's id; a tree 40 levels deep; a child committed while the purge waited; business key free again; atomicity with a failure injected into each of the seven tables; the log line; argument checks; `PurgeFinishedBeforeAsync` boundary, limit, `More`, oldest first, byte order of ids, locked trees skipped and found later, unfinished roots, tenants, `@schedule/` roots, a root that stopped qualifying; `PurgeEventsBeforeAsync` boundary, limit, `More`, tenants, a purged id accepted again; the tree lock
- [x] `PurgeFlowTests`: an item of every kind (timer, inbox, effect claim, expired lease, child notice to a purged parent) read by a pump before the purge is dropped without error and nothing is recreated; a schedule keeps firing; a stray inbox row is left alone; an effect in flight is dropped with `process.effect.orphaned` and the case restarted under the same key is untouched; a live tree with every kind of outstanding work; a purge against three pumps for 80 rounds with no deadlock and no half tree at any observation; logging hygiene
- [x] `GuidePurgeTests`: the retention example of the guide runs
- [x] The guide example `Retention` compiles and is checked against the guide by the existing `GuideTests`

## Documents

- [x] `docs/reference/process-host-postgres.md`: "Purging cases", "What remains after a purge", the log lines; the sentence that retention and purging are not part of the release is gone
- [x] `docs/guides/process-engine.md`: the operations section and `GuideExamples.cs`; the three statements that retention is outside the release
- [x] `docs/architecture/overview.md`
- [x] `CHANGELOG.md`: `Unreleased` content under `## 0.3.0 (2026-11-23)`, a W1-F bullet, an empty `## Unreleased` above
- [x] `Directory.Build.props` `<Version>` 0.3.0; `docs/consuming.md` and the guide name 0.3.0

## Open

- [x] The tests that pinned the applied migrations to `1, 2, 3` now expect `1, 2, 3, 4` (`MigratorTests`, and the upgrade test of `CockpitQueryTests`, whose setup also drops what `0004` creates)
- [ ] Independent verification (second round after the repair pass below)

## Repair pass (after the first verification)

- [x] R1: a purge never waits for a row lock while it holds one. One attempt = unlocked walk, one `skip locked` statement for the whole tree, fewer rows than ids rolls back, second walk under the locks, decision under the locks, delete, commit. `PurgeInstanceAsync` retries with a randomized pause on the `TimeProvider` until the new `ProcessHostOptions.PurgeLockTimeout` (default 10 s, greater than zero and at most 5 minutes), then `Refused` / `process.purge.busy`; `PurgeFinishedBeforeAsync` makes one attempt per tree. Spec section 3 rule 3, the reference page, the CHANGELOG, the guide and the XML docs say so; the "publish can deadlock with purge" caveat is removed. Tests: two deadlock scenarios against a publish held open, a stress test of publishes, pumps and both commands (60 rounds each), busy, retry, cancellation, grown tree, partly locked batch tree, status and boundary under the locks, lock tenant scope
- [x] R2: the event delete is `delete ... where tenant_id = @t and event_id = any(array(select ... limit @n))` (index scan on the key, no scan of the tenant); the inbox delete of a tree got an index (`ix_process_inbox_instance` in `0004`) because the existing inbox index is partial
- [x] R3: tests for the tenant scope of the walk, of the event select and of the lock statement, and for the strict boundary under the locks
- [x] R4: reference page corrections (transaction shape, clear columns by table, purged schedule occurrence, `PurgeLockTimeout`, migrations `0003` and `0004` take their locks inside the migration transaction), CHANGELOG sentence on the new `IProcessEngine` members
- [x] R5: `Session.DisposeAsync` rolls back through a call whose catch is limited to `ObjectDisposedException`, which Npgsql raises only after a critical failure and exposes no state to test; a failed commit no longer triggers a rollback and surfaces as its own exception; disposing twice is harmless

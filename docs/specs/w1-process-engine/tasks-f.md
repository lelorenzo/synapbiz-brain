# Tasks — W1-F, purging cases from the PostgreSQL host

Spec: [spec-f-purge.md](spec-f-purge.md). Delete commands added to `Synapbiz.Brain.Host.Postgres`; `Synapbiz.Brain.Process` and every other package are unchanged in content.

## Code

- [x] Public enums `PurgeMode` and `PurgeStatus` (ordinals from 1) and records `PurgeCounts`, `PurgeResult`, `PurgeBatchResult`, `PurgeEventsResult` in `Hosting/IProcessEngine.cs`
- [x] `IProcessEngine`: `PurgeInstanceAsync`, `PurgeFinishedBeforeAsync`, `PurgeEventsBeforeAsync`, implemented in `ProcessEngine` with the guard of the newest methods (`Guard.Id`, `Guard.NoControlCharacters`, `Guard.InRange`)
- [x] Internal port: `IProcessStore.ScanFinishedRootsAsync` and `PurgeEventsAsync`; `IProcessSession.LockTreeAsync` and `DeleteTreeRowsAsync`, both guarded by the instance lock
- [x] `PostgresProcessStore.Purge.cs` (scans and event delete) and the two session operations: the whole tree is walked without a depth bound (`union`, so a loop in the parent links ends), locked root first and then in `(created_at, instance_id collate "C")` order, and walked again after each round of locks
- [x] One transaction per tree: history, timers, waits, tasks, effects, inbox, then the instances; the counts are the affected-row counts of the statements
- [x] `PurgeFinishedBeforeAsync`: scan oldest first, root locked with `skip locked` and re-checked under the lock, `More` from a final scan
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
- [ ] Independent verification

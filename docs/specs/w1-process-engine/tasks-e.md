# Tasks — W1-E, cockpit queries on the PostgreSQL host

Spec: [spec-e-cockpit-queries.md](spec-e-cockpit-queries.md). Read-only queries added to `Synapbiz.Brain.Host.Postgres`; `Synapbiz.Brain.Process` and every other package are unchanged.

## Code

- [x] Public records `ProcessInstanceSummary`, `ProcessInstanceQuery`, `ProcessInstancePage`, `ProcessInstanceCount`, `ProcessCaseView` in `Hosting/IProcessEngine.cs`
- [x] `IProcessEngine`: `ListInstancesAsync`, `CountInstancesAsync`, `GetCaseAsync`, `ListChildrenAsync`, implemented in `ProcessEngine` with the guard of the existing methods
- [x] Internal port `IProcessStore` gains the four read methods; records for the rows in `StoreModel.cs`
- [x] `PostgresProcessStore.Cockpit.cs`: tenant-first statements with parameters only, recursive walk bounded by depth 16, no `state` column, one repeatable-read read-only transaction for the case
- [x] Opaque keyset cursor (`Hosting/InstanceCursors.cs`): creation instant as exact UTC ticks plus the instance id; a bad cursor is an `ArgumentException`
- [x] Migration `0003_cockpit.sql`: the list-order index and the `(tenant_id, status)` index, additive

## Tests (`CockpitQueryTests`, PostgreSQL, the existing rig)

- [x] Empty database; argument checks
- [x] Two tenants that share process ids and business keys, including by instance id of the other tenant
- [x] Every filter alone and combined; `RootsOnly` on and off; `NeedsAttention` for each reason and for none, applied before the limit
- [x] Paging over more rows than the limit with equal creation instants (including microseconds), every row once and in order; limit bounds; bad cursors
- [x] A `Created` instance counts and filters as `Running`
- [x] Suspended grandchild and a task on a grandchild; the depth bound of 16
- [x] `GetCaseAsync` on the reference `case.lifecycle` at each point; a task with a timeout; unknown id
- [x] No decryption of the state; the show text of the open tasks is the only protector use; no row lock
- [x] The migration on a database at `0002` that holds instances
- [x] The guide example `Cockpit` compiles and is run by the existing `GuideTests`

## Documents

- [x] `docs/reference/process-host-postgres.md`: the cockpit queries
- [x] `docs/guides/process-engine.md` section 8 and `GuideExamples.cs`
- [x] `docs/architecture/overview.md`
- [x] `CHANGELOG.md` under Unreleased

## Open

- [x] The `MigratorTests` that pinned the applied versions to `1, 2` now expect `1, 2, 3` (commit b0e3fba changed the pinned list and the setup of the upgrade test)

## Repair pass

- [x] R1: a cursor longer than `InstanceCursors.MaxLength` (4 x the longest id + 64) or whose id holds a control character is an `ArgumentException`; the cockpit queries reject a control character (NUL included) in the tenant id, process id, stage or instance id, on their own paths only; tests for `MXwA`, an over-long cursor and the longest real cursor
- [x] R2: tenant isolation tests with the same instance and parent ids in both tenants, and the shape that catches a dropped tenant filter in a recursion; each of the eight tenant filters proven by mutation
- [x] R3: `GetCaseAsync` is proven to be one read-only repeatable-read snapshot (interleaved commit through the `AfterCaseSummaries` test seam, and the transaction settings read inside the transaction), both proven by mutation
- [x] R4: depth boundary tests, a suspended instance and an open task at depth 17 (not counted) and at depth 16 (counted), for the root summary, the list, the case descendants and the case tasks
- [x] R5: every ordering and comparison on an instance id or task id uses `collate "C"`; the list index declares `instance_id collate "C" desc`; reference page and spec sections 4, 6 and 7 say so
- [x] R6: the limit error names the member `Limit` (`Guard.InRange`) and a test asserts it
- [x] R7: this file, and the spec section 7 sentence on the status index


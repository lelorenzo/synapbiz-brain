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

- [ ] The five `MigratorTests` that pin the applied versions to `1, 2` must expect `1, 2, 3` once the owner allows an existing test to change

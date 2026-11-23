# W1-E: cockpit queries on the PostgreSQL host

Status: approved by the owner on 2026-11-23. Normative for PR E. It adds read-only queries to `Synapbiz.Brain.Host.Postgres`; it changes no behavior of the interpreter, the pump, a command or the format. `Synapbiz.Brain.Process` is not touched.

## 1. Why

A monitoring screen shows many cases at once: which are in progress, at which stage, which need a person, what each one waits for. Version 0.2.0 answers only for one instance at a time (`GetInstanceAsync`, `FindInstanceAsync`, `GetHistoryAsync`) and lists open tasks only per queue. A case is a root instance with one child instance per stage, so a consumer today has to walk the history of the root to find its children, and it cannot see that a child is suspended without opening it.

## 2. Rules for every query here

1. Read-only: one statement (or one read-only transaction), no row lock, no write.
2. Tenant first: every statement filters by `tenant_id`; no query can return a row of another tenant, and an id of another tenant behaves as an unknown id.
3. No state: these queries never read, decrypt or return the `state` column, a history `data` column, an effect input or a task `show` value, except where section 6 says a task carries `ShowJson` exactly as `ListOpenTasksAsync` does today. They never call `IPayloadProtector` for an instance row.
4. Status mapping: an instance row in the internal `Created` status is reported as `Running`, and a filter on `Running` matches both. No other status is remapped.
5. Arguments are validated with the same guard as the existing methods (`ArgumentException` for a null, empty or over-long id); an unknown id yields an empty result or null, never an exception.
6. Descendants are the instances reachable from an instance through `parent_instance_id`, at any depth, in the same tenant. The walk is a recursive query bounded by depth 16; deeper rows are not returned.
7. Nothing is logged by a query.

## 3. `ProcessInstanceSummary`

A new public record, the row of every list here. It is `ProcessInstanceInfo` without `State`, plus two numbers:

`TenantId`, `InstanceId`, `ProcessId`, `ProcessVersion`, `BusinessKey`, `Status`, `Stage`, `CurrentStep`, `Result`, `ProblemCode` (string, the stored code; null when there is none), `ParentInstanceId`, `CreatedAt`, `UpdatedAt`, `OpenTasks` (int: open tasks of the instance itself), `SuspendedDescendants` (int: descendants whose status is `Suspended`; the instance itself is not counted).

## 4. `ListInstancesAsync(ProcessInstanceQuery query, CancellationToken)`

`ProcessInstanceQuery(TenantId, ProcessId = null, Statuses = null, Stage = null, RootsOnly = true, NeedsAttention = false, Limit = 100, After = null)`.

- `ProcessId` null means every process. `Statuses` null or empty means every status. `Stage` filters on the instance's own stage, exact and case-sensitive.
- `RootsOnly` keeps instances with no parent.
- `NeedsAttention` keeps instances that are suspended, or have at least one suspended descendant, or have at least one open task of their own or of a descendant.
- `Limit` is 1 to 500; outside that range is an `ArgumentOutOfRangeException`.
- Order: `created_at` descending, then `instance_id` descending. Paging is by keyset: `After` is the `Next` value of the previous page.
- Returns `ProcessInstancePage(Items, Next)`. `Next` is null on the last page. The cursor is an opaque string; a cursor that does not parse is an `ArgumentException`. A cursor carries only the creation instant and the instance id of the last row.
- Rows inserted or changed between pages may be missed or seen under their new values; no row is returned twice in one walk.

## 5. `CountInstancesAsync(string tenantId, string? processId, bool rootsOnly, CancellationToken)`

Returns `IReadOnlyList<ProcessInstanceCount>` with `ProcessInstanceCount(ProcessId, Status, Stage, Count)`: one entry per distinct process, reported status and stage (null stage is its own group), ordered by process id, status, stage with null first. `Created` and `Running` rows are one group. Groups with zero rows are absent.

## 6. `GetCaseAsync(string tenantId, string instanceId, CancellationToken)`

Returns `ProcessCaseView?` (null when the tenant has no such instance):

- `Instance`: the summary of the instance.
- `Descendants`: the summaries of its descendants, ordered by `created_at` then `instance_id` ascending. `ParentInstanceId` lets the consumer rebuild the tree.
- `OpenTasks`: the open tasks of the instance and of its descendants as `ProcessTaskInfo`, oldest first, then by task id.
- `NextTimerDue`: the earliest `due_at` among the timers of the instance and of its descendants; null when there is none.
- `WaitingForEvents`: the distinct event names the instance and its descendants wait for, ordered; names only, never a correlation value.

The reads of one call run in one read-only transaction at repeatable read, so the parts agree with each other.

`ListChildrenAsync(string tenantId, string instanceId, CancellationToken)` returns the direct children only, as summaries, in the same order as `Descendants`.

## 7. Storage

Migration `0003_cockpit.sql`: an index for the list order, `(tenant_id, created_at desc, instance_id desc)` on `process_instance`, and an index on `process_instance (tenant_id, status)`. No table or column changes. The migration is additive and applies on a database at `0002` with data.

`IProcessStore` (internal) gains the read methods; `PostgresProcessStore` implements them. No new public type outside `Synapbiz.Brain.Process.Hosting`.

## 8. Not in this PR

Retention, reopening a case under the same business key, a public zone resolver, the business key in the suspended log event, display names in the format, counts over time. `ListOpenTasksAsync(tenant, queue)` is unchanged.

## 9. Tests (PostgreSQL, the existing rig)

1. Each query: empty database, one tenant, two tenants that share process ids, business keys and (where the store allows) nothing else; a query of tenant A never returns a row of tenant B, including by instance id of B.
2. `ListInstancesAsync`: every filter alone and combined; `RootsOnly` on and off; `NeedsAttention` for each of its three reasons and for none; paging over more rows than the limit with equal creation instants, every row once, in order; limit bounds; bad cursor.
3. A `Created` child counts and filters as `Running`.
4. `SuspendedDescendants` and `OpenTasks` with a suspended grandchild and with a task on a grandchild.
5. `GetCaseAsync` on the reference root `case.lifecycle` at a point where a stage child waits for a task and a timer: descendants, tasks, next timer and event names are the expected literals; unknown id is null.
6. No protector call: the queries of sections 4 to 6 run with a protector that throws on every call (a task's `ShowJson` is read the way `ListOpenTasksAsync` reads it today; if that path uses the protector, the test allows that one use and names it).
7. The migration applies on a database migrated to `0002` that holds instances.
8. No existing test or fixture is edited, except additions to shared test helpers.

## 10. Documents

`docs/reference/process-host-postgres.md` (the queries), section 8 of `docs/guides/process-engine.md` (replace the walk through the parent's history by `GetCaseAsync`, keep the examples compiled through `GuideExamples.cs`; the list of known gaps loses the entries this PR closes), `docs/architecture/overview.md`, `CHANGELOG.md` under `## Unreleased`, `tasks-e.md`. The version line is not changed by this PR.

# W1-F: purging cases from the PostgreSQL host

Status: normative for PR F (2026-11-23). It adds delete commands to `Synapbiz.Brain.Host.Postgres`. It changes nothing in `Synapbiz.Brain.Process`, the format or the behavior of an instance that is not purged.

## 1. Why

Version 0.2.0 never deletes a row. A consumer with a retention rule (for example "nothing about a visit 35 days after it") cannot meet it in the host's tables, and cannot serve a request to erase everything now. The consumer owns the rule and the dates; the host supplies commands that remove a case completely and report what they removed.

## 2. Vocabulary

- A tree is a root instance (no parent) and its descendants at any depth, in one tenant.
- The rows of a tree are, for every instance of the tree: its `process_instance` row and its rows in `process_history`, `process_timer`, `process_wait`, `process_task`, `process_effect` and `process_inbox`.
- Finished means status `Completed` or `Cancelled`.

## 3. Rules for every command here

1. Tenant first, as everywhere: an id of another tenant behaves as an unknown id.
2. All or nothing per tree: one transaction removes every row of a tree or none. No command can leave part of a tree.
3. Lock order: the root instance row is locked first, then every descendant row in `(created_at, instance_id)` order, then the rows are deleted. This is the order the pump uses for one instance (instance row first), extended to the tree, so a purge cannot deadlock with the pump; a concurrent pump transaction on an instance of the tree finishes first and the purge then sees its result.
4. Deletion is a plain SQL `DELETE`. The host keeps no tombstone, no copy and no archive. Reclaiming the space of dead rows is PostgreSQL's (auto)vacuum; the reference page says so and names what a dead row can still hold until then (section 8).
5. A purge logs one line per tree with the tenant id, the root instance id and the counts; never a business key, a correlation or a payload.
6. A purge never calls a port and never notifies anyone.

## 4. `PurgeInstanceAsync(string tenantId, string instanceId, PurgeMode mode, CancellationToken)`

`PurgeMode.FinishedOnly` or `PurgeMode.Force`. Returns `PurgeResult(PurgeStatus Status, PurgeCounts Counts)`.

- Unknown instance, in either mode: `NotFound` with zero counts, never an exception, so a retry after a crash is safe. Every result of every command here carries its counts, also when they are all zero.
- The instance has a parent: `Refused` with code `process.purge.not_root`. A child is purged with its root.
- `FinishedOnly` and the root is not finished: `Refused` with code `process.purge.not_finished`, nothing changes. Only the root's status is looked at; descendants of a finished root go with it whatever their status.
- `Force`: the tree is removed whatever its statuses. Nothing is cancelled through the interpreter first and no history entry is written, since the history goes too. This is the "erase now" path.
- Otherwise: `Purged`, with the counts.

Effects in flight. A port call that a worker already started cannot be recalled: the outside world may still receive it. When its worker comes back, the effect row and the instance row are gone; the worker drops the result and logs `process.effect.orphaned` with ids only. Likewise a timer, an inbox row or a wait that a pump read before the purge and handles after it finds no instance and is dropped without error. The tests prove each of these.

`PurgeCounts(Instances, History, Timers, Waits, Tasks, Effects, Inbox)`: the number of rows deleted per table, so one audit line can carry them.

After a purge the business key is free: `StartAsync` with the same process id and business key creates a new instance.

## 5. `PurgeFinishedBeforeAsync(string tenantId, DateTimeOffset before, int limit, CancellationToken)`

A safety net, not the main path. Removes up to `limit` (1 to 500) trees of the tenant whose root is finished and whose root `updated_at` is earlier than `before`, oldest first, one transaction per tree. Returns `PurgeBatchResult(int Trees, PurgeCounts Counts, bool More)`; `More` is true when at least one further tree qualified when the call ended. A tree that another transaction holds is skipped in this call (`skip locked` on the root) and found by the next one. `@schedule/...` instances qualify like any other.

## 6. `PurgeEventsBeforeAsync(string tenantId, DateTimeOffset before, int limit, CancellationToken)`

Removes up to `limit` (1 to 5000) rows of `process_event` (the de-duplication ids) of the tenant received before `before`. Returns the count and `More`. After it, an event id that was purged is unknown again: publishing it is treated as a new event. The reference page states that the de-duplication window is therefore what the consumer keeps.

## 7. What is not purged, and not in this PR

`process_activation`, `process_event_trigger` and `process_schedule` are configuration, removed by `DeactivateAsync`. No scheduled purge inside the host: the consumer calls the commands (from its own job) with its own dates. No purge by business key (use `FindInstanceAsync`, then the instance id). No reopening of a finished case. No retention policy in the format.

## 8. Reference page: "What remains after a purge"

State plainly: rows are deleted, not marked; until vacuum reuses the pages, and in the write-ahead log and in backups taken before the purge, the old row images still exist; in those images the protected columns (state, history data, effect input, task show, inbox payload) are ciphertext produced by the consumer's `IPayloadProtector`, and the clear columns hold what the consumer chose as tenant id, business key, correlation, event id and failure codes, which is why those must be opaque references. Backups and log retention are the consumer's to bound.

## 9. Storage

Migration `0004_purge.sql` only if an index is needed for section 5 (`process_instance (tenant_id, updated_at)` restricted to finished roots) or section 6 (`process_event (tenant_id, received_at)`); additive.

## 10. Tests (PostgreSQL)

1. Purge of a finished tree with children and grandchildren that have history, tasks, timers, waits, effects and inbox rows: every table is empty for the tree afterwards, counts equal the rows that existed, another tenant's identical tree and another tree of the same tenant are untouched.
2. `NotFound`, `not_root`, `not_finished`; other tenant's id is `NotFound`.
3. `Force` on a tree with a waiting child, an open task, a due timer, a pending effect and a pending inbox row; then a pump pass runs without error and does nothing for that tree.
4. Effect in flight: a port blocked inside its call while the tree is purged; the port's return is dropped, `process.effect.orphaned` logged with ids only, no exception, the pass continues with other work.
5. Concurrency: purge against a running pump on the same tree, many rounds, no deadlock (`40P01`), tree fully gone or fully present at every observation.
6. Atomicity: a failure injected midway through the deletes leaves every row.
7. Start again with the same business key after a purge.
8. `PurgeFinishedBeforeAsync`: boundary instant, limit and `More`, oldest first, a locked tree skipped, unfinished roots never touched, tenant isolation.
9. `PurgeEventsBeforeAsync`: boundary, limit, tenant isolation, a purged id is accepted again.
10. Logging hygiene for the purge lines.
11. No existing test or fixture is edited beyond the pinned migration list, if a migration is added.

## 11. Documents

`docs/reference/process-host-postgres.md` (commands, "What remains after a purge", the removal of the sentence that retention is not part of the release), the guide's operations section with a compiled example of a consumer purge job, `docs/architecture/overview.md`, `CHANGELOG.md` under `## Unreleased`, `tasks-f.md`.

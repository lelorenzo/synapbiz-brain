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
3. No waiting while holding a lock. A purge never waits for a row lock while it holds one, so it cannot be part of a deadlock cycle. One attempt is one transaction: (a) it reads the ids of the tree without locking (a recursive walk from the root, tenant-filtered at every level), then locks all of them, root included, in one statement `select ... where tenant_id = @t and instance_id = any(@ids) order by instance_id collate "C" for update skip locked`; a waiting `for update` on an instance row is never issued by purge code. If the statement returns fewer rows than ids, the attempt failed: it rolls back, which releases everything. (b) With every row locked it reads the root row and walks the tree again, and decides here, under the locks, whether the instance exists, is a root and is finished (`NotFound`, `process.purge.not_root`, `process.purge.not_finished`); if the walk finds an id that is not locked (a child committed in between) the attempt fails the same way. Otherwise it deletes all rows of the tree and commits. (c) A failed attempt is tried again by `PurgeInstanceAsync` after a short randomized pause (about 20 ms, growing to at most about 250 ms, on the `TimeProvider` of the host), until `ProcessHostOptions.PurgeLockTimeout` (default 10 seconds, greater than zero and at most 5 minutes) has passed since the call began; then the result is `Refused` with code `process.purge.busy`, zero counts, nothing changed. Cancelling the token ends the wait with the usual cancellation. An unknown id is answered `NotFound` at once, without a retry; every other decision is made under the locks. (d) `PurgeFinishedBeforeAsync` makes exactly one attempt per candidate root, no retry and no wait: a tree that is not wholly acquired, whose second walk found a new row, or whose root no longer qualifies under the locks is skipped in that call and counted toward `More`. (e) Why this excludes deadlocks: a deadlock needs a transaction that waits for a lock while holding another. A purge transaction holds locks only after its single `skip locked` statement has returned, and from then on it waits for no row lock (it only deletes rows it holds), so no cycle can contain it. The transactions that do hold several instance locks while waiting (publishing an event locks its targets in instance id order) therefore never wait for a purge that waits for them. The price is that a purge can be refused as busy, and a batch can skip a tree, when the tree stays locked; both are safe to repeat.
4. Deletion is a plain SQL `DELETE`. The host keeps no tombstone, no copy and no archive. Reclaiming the space of dead rows is PostgreSQL's (auto)vacuum; the reference page says so and names what a dead row can still hold until then (section 8).
5. A purge logs one line per tree with the tenant id, the root instance id and the counts; never a business key, a correlation or a payload.
6. A purge never calls a port and never notifies anyone.

## 4. `PurgeInstanceAsync(string tenantId, string instanceId, PurgeMode mode, CancellationToken)`

`PurgeMode.FinishedOnly` or `PurgeMode.Force`. Returns `PurgeResult(PurgeStatus Status, PurgeCounts Counts)`.

- Unknown instance, in either mode: `NotFound` with zero counts, never an exception, so a retry after a crash is safe. Every result of every command here carries its counts, also when they are all zero.
- The instance has a parent: `Refused` with code `process.purge.not_root`. A child is purged with its root.
- `FinishedOnly` and the root is not finished: `Refused` with code `process.purge.not_finished`, nothing changes. Only the root's status is looked at; descendants of a finished root go with it whatever their status.
- `Force`: the tree is removed whatever its statuses. Nothing is cancelled through the interpreter first and no history entry is written, since the history goes too. This is the "erase now" path.
- Another transaction held a row of the tree for the whole `PurgeLockTimeout`: `Refused` with code `process.purge.busy`, zero counts, nothing changed; the call may be repeated.
- Otherwise: `Purged`, with the counts.

Effects in flight. A port call that a worker already started cannot be recalled: the outside world may still receive it. When its worker comes back, the effect row and the instance row are gone; the worker drops the result and logs `process.effect.orphaned` with ids only. Likewise a timer, an inbox row or a wait that a pump read before the purge and handles after it finds no instance and is dropped without error. The tests prove each of these.

`PurgeCounts(Instances, History, Timers, Waits, Tasks, Effects, Inbox)`: the number of rows deleted per table, so one audit line can carry them.

After a purge the business key is free: `StartAsync` with the same process id and business key creates a new instance.

## 5. `PurgeFinishedBeforeAsync(string tenantId, DateTimeOffset before, int limit, CancellationToken)`

A safety net, not the main path. Removes up to `limit` (1 to 500) trees of the tenant whose root is finished and whose root `updated_at` is earlier than `before`, oldest first, one transaction per tree. Returns `PurgeBatchResult(int Trees, PurgeCounts Counts, bool More)`; `More` is true when at least one further tree qualified when the call ended. A tree that another transaction holds, wholly or in part, is skipped in this call (one attempt, rule 3 (d)) and found by the next one. `@schedule/...` instances qualify like any other.

## 6. `PurgeEventsBeforeAsync(string tenantId, DateTimeOffset before, int limit, CancellationToken)`

Removes up to `limit` (1 to 5000) rows of `process_event` (the de-duplication ids) of the tenant received before `before`. Returns the count and `More`. After it, an event id that was purged is unknown again: publishing it is treated as a new event. The reference page states that the de-duplication window is therefore what the consumer keeps.

## 7. What is not purged, and not in this PR

`process_activation`, `process_event_trigger` and `process_schedule` are configuration, removed by `DeactivateAsync`. No scheduled purge inside the host: the consumer calls the commands (from its own job) with its own dates. No purge by business key (use `FindInstanceAsync`, then the instance id). No reopening of a finished case. No retention policy in the format.

## 8. Reference page: "What remains after a purge"

State plainly: rows are deleted, not marked; until vacuum reuses the pages, and in the write-ahead log and in backups taken before the purge, the old row images still exist; in those images the protected columns (state, history data, effect input, task show, inbox payload) are ciphertext produced by the consumer's `IPayloadProtector`, and every column not listed as protected is clear text, and the reference page names them by table: instance (process id, business key, stage, current step, result, problem code, parent child key), effect (idempotency key, port, model, prompt id), task (queue), inbox (code), history (step, code), wait (event, correlation), event (event id, name). The consumer or its documents choose the business key, result, stage names, queue, correlation, event id and name; the process id, step names, port and codes come from the process definition and the host. That is why what the consumer chooses must be opaque references. Backups and log retention are the consumer's to bound.

## 9. Storage

Migration `0004_purge.sql` only if an index is needed for section 5 (`process_instance (tenant_id, updated_at)` restricted to finished roots), section 6 (`process_event (tenant_id, received_at)`) or the delete of inbox rows in every status (`process_inbox (tenant_id, instance_id)`, since the existing inbox index is partial); additive.

## 10. Tests (PostgreSQL)

1. Purge of a finished tree with children and grandchildren that have history, tasks, timers, waits, effects and inbox rows: every table is empty for the tree afterwards, counts equal the rows that existed, another tenant's identical tree and another tree of the same tenant are untouched.
2. `NotFound`, `not_root`, `not_finished`; other tenant's id is `NotFound`.
3. `Force` on a tree with a waiting child, an open task, a due timer, a pending effect and a pending inbox row; then a pump pass runs without error and does nothing for that tree.
4. Effect in flight: a port blocked inside its call while the tree is purged; the port's return is dropped, `process.effect.orphaned` logged with ids only, no exception, the pass continues with other work.
5. Concurrency: purge against running pumps and against concurrent publishes that wait on several instances of the same tree (ids ordered differently from creation order, the root sorting last), many rounds, no deadlock (`40P01`), tree fully gone or fully present at every observation; a tree held for longer than `PurgeLockTimeout` gives `process.purge.busy` and changes nothing; a tree that grows between the walk and the locks is purged whole by the next attempt.
6. Atomicity: a failure injected midway through the deletes leaves every row.
7. Start again with the same business key after a purge.
8. `PurgeFinishedBeforeAsync`: boundary instant, limit and `More`, oldest first, a locked tree skipped, unfinished roots never touched, tenant isolation.
9. `PurgeEventsBeforeAsync`: boundary, limit, tenant isolation, a purged id is accepted again.
10. Logging hygiene for the purge lines.
11. No existing test or fixture is edited beyond the pinned migration list, if a migration is added.

## 11. Documents

`docs/reference/process-host-postgres.md` (commands, "What remains after a purge", the removal of the sentence that retention is not part of the release), the guide's operations section with a compiled example of a consumer purge job, `docs/architecture/overview.md`, `CHANGELOG.md` under `## Unreleased`, `tasks-f.md`.

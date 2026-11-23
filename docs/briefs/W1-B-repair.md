# Brief W1-B repair — `Synapbiz.Brain.Host.Postgres` (one repair pass)

Repository: this checkout, branch `claude/project-thread-6zzgr7`. Commit on it; do **not** push, do not open a pull request, do not use any GitHub tool. Everything in `docs/briefs/W1-B-postgres-host.md` (hard rules, toolchain, stop rule) still applies. Read it and `docs/specs/w1-process-engine/spec-b-postgres-host.md` (section 11 is new and is the normative text of this repair) before you start.

An independent verifier found no blocker and the following defects. Fix every one; each fix comes with the tests named.

## R1. One failing item must not stop the pass (spec 11.1)

`ProcessPump` has no per-item catch: an exception while handling one item (protector throwing for one tenant, corrupt state, malformed inbox payload) aborts `RunOnceAsync`, the same row is first again next pass, and later work kinds never run. Catch per item (not `OperationCanceledException` from the pass token), log a warning with the item kind, the tenant, the ids and the exception **type only**, leave the item untouched, continue with the next item and the next kind. Test: a tenant whose protector throws does not prevent another tenant's timers, inbox rows and effects from being handled in the same pass; no protected value in the log.

## R2. Items left untouched must not starve the others (spec 11.2)

Scans are `order by ... limit BatchSize`, so `BatchSize` stuck items (definition unavailable, failing item, locked instance) hide every other item of that kind for ever. Page each scan with a keyset cursor on its existing ordering key inside one pass: keep reading the next page while the scan is not exhausted and fewer than `BatchSize` items of that kind have been handled in this pass. Test: `BatchSize = 1`, one stuck instance and one healthy one, the healthy one is served in one pass, for timers, inbox rows and effects.

## R3. Correlation candidates are matched without regard to case (spec 11.3)

`brain-process-v1` says a Reference correlation compares equal whatever the case of the input; the host's `correlation = @c` is case-sensitive, so an upper-case Reference correlation is answered `NoTarget` and the event id is consumed. Select candidate waits case-insensitively (`lower(correlation) = lower(@c)`, with the supporting index changed to match, in the same `0001_initial.sql`: nothing is released yet), hand the event to the interpreter for each candidate as today and let it decide. If the interpreter ignores the event for every candidate because the value does not match exactly (a String correlation differing in case), the result must still be `NoTarget`: report how the code achieves that, and if the interpreter's public API cannot tell you, use the stop rule for this item only. Tests: upper-case Reference correlation reaches the wait and completes the instance; a String correlation differing only in case does not.

## R4. Claim an effect only when it is about to be invoked (spec 11.4)

The pump claims the whole batch up front and runs `MaxConcurrentEffects` at a time, so a claimed effect burns its lease while it waits for a slot; another pump can then requeue it (double invocation) or declare an at-most-once effect `Unknown` while the first pump still invokes it later; and a graceful stop mid-batch turns never-invoked at-most-once effects into `Unknown`. Change to: acquire a slot first, then claim one effect (lease starts then), then invoke. An effect that was never claimed stays `Pending`. When the pass token is cancelled no new effect is claimed; a port call already in flight whose result arrives is recorded with `CancellationToken.None` (the port did its work; dropping the result would create an `Unknown`). Tests: 3 at-most-once effects, one slot, the pass cancelled after the first call: the other two are still `Pending`, are never `Unknown`, and a later pass invokes each exactly once; a result that completes after the stop token fired is applied.

## R5. Tests the verifier found missing (no production change expected)

1. Protection on the child and cancel paths: run a parent-and-child flow and a cancel with the reversing protector and assert that `process_inbox.payload` rows of kinds Start, ChildCompleted, ChildFailed and Cancel hold no plaintext.
2. Ruling 5: hold the instance row lock from a second transaction while a port result is being applied; assert the result is applied once the lock is released (not dropped, not deferred to lease expiry). No sleeps: coordinate with task completion sources or by observing `pg_locks`/`pg_stat_activity` in a loop that yields.
3. Re-validation under the lock: (a) an inbox row scanned out of order is not applied before its predecessor; (b) a lease that was re-claimed after the expiry scan is not expired by the stale scan result; (c) a timer row already consumed by another session is not fired again. If a store seam is needed to inject the stale scan result, use the internal `IProcessStore` with a decorating test double; do not add public API.
4. Tenant collision: the same `instance_id` inserted for two tenants (by SQL in the test) must not let an event wait lookup or a task lookup of tenant A reach tenant B.

## R6. Docs

- `docs/specs/w1-process-engine/plan.md`: W1-A is `merged`.
- `CHANGELOG.md` W1-B bullet: seven packages, not six.
- `docs/reference/process-host-postgres.md`: `process.inbox.refused` is logged for rejected rows only; add the behaviour of spec section 10 rulings 1, 2, 5 and 6 and of section 11 (per-item isolation, paging, case-insensitive candidates, lease starts at claim immediately before the call, what a graceful stop leaves behind); say inbox rows are applied "in id order per instance", not "strictly in order"; say that an event id is consumed by any answer including `NoTarget`; state under data protection that `process_wait.correlation` and failure codes supplied by ports are stored in clear and therefore must be opaque references and codes, never personal data.
- `docs/specs/w1-process-engine/tasks-b.md`: add the repair tasks, ticked as you finish.

## Gates and report

Same gates as the original brief on the whole solution (expected base now: Architecture 27, Engine 167, Spec 226, Process 519, Host.Postgres 117 plus your new tests; the two Workbench projects must stay green at whatever count they have, unchanged by you). 2 base warnings only. Host line coverage ≥ 85 %. Run each of the two concurrency tests 20 times in a row and the whole host test project 3 times. Report: commit hashes, files changed, counts, coverage, loop results, how each of R1 to R6 was done, every deviation and every open question.

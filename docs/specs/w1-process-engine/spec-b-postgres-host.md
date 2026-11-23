# W1-B: `Synapbiz.Brain.Host.Postgres`, the durable process host

Status: normative specification of PR W1-B. Design and owner decisions: [design.md](design.md). The interpreter it hosts: [spec.md](spec.md) (W1-A, merged) and [brain-process-v1](../../reference/brain-process-v1.md).

Every test, fixture and example is synthetic.

## 1. Deliverable

One new library, `Synapbiz.Brain.Host.Postgres` (assembly and package id), that runs `brain.process@1` instances durably on PostgreSQL 14 or later: it stores instance state and history, executes the requests the interpreter returns (effects, timers, waits, tasks, child starts), feeds the results back as triggers, and survives crashes and concurrent workers.

It references `Synapbiz.Brain.Process` (project), `Npgsql`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options` — nothing else (no EF Core, no Dapper, no JSON library beyond `System.Text.Json`). It changes no file under the five existing `src` projects.

Two namespaces in the one assembly, so that a second database provider can later be split out by moving files:

- `Synapbiz.Brain.Process.Hosting`: provider-neutral public types (engine, ports, records, options, worker).
- `Synapbiz.Brain.Host.Postgres`: PostgreSQL-specific public types (registration, migrator, PostgreSQL options).

All SQL lives behind an **internal** interface `IProcessStore` in `Synapbiz.Brain.Process.Hosting`; nothing outside the PostgreSQL store class may reference `Npgsql`. The shape of `IProcessStore` is the implementer's, constrained by the behaviour below; it is not public in this PR.

Out of scope: schedules and cron, delivery windows, `poll`, retention and purging, metrics, an HTTP API, a UI, SQL Server, migrating running instances to a new definition version.

## 2. Public API

```csharp
namespace Synapbiz.Brain.Process.Hosting;

public interface IProcessEngine
{
    Task<StartResult> StartAsync(StartRequest request, CancellationToken cancellationToken = default);
    Task<PublishResult> PublishEventAsync(ProcessEvent processEvent, CancellationToken cancellationToken = default);
    Task<CommandResult> CompleteTaskAsync(string tenantId, string taskId, string outcome, string dataJson, CancellationToken cancellationToken = default);
    Task<CommandResult> CancelAsync(string tenantId, string instanceId, string reason, CancellationToken cancellationToken = default);
    Task<CommandResult> ResumeAsync(string tenantId, string instanceId, CancellationToken cancellationToken = default);

    Task<ProcessInstanceInfo?> GetInstanceAsync(string tenantId, string instanceId, CancellationToken cancellationToken = default);
    Task<ProcessInstanceInfo?> FindInstanceAsync(string tenantId, string processId, string businessKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProcessHistoryEntry>> GetHistoryAsync(string tenantId, string instanceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProcessTaskInfo>> ListOpenTasksAsync(string tenantId, string queue, CancellationToken cancellationToken = default);
}

public sealed record StartRequest(string TenantId, string ProcessId, string BusinessKey, string InputJson);
public sealed record StartResult(string InstanceId, bool Created);          // Created false: an instance with that business key already existed
public sealed record ProcessEvent(string TenantId, string EventId, string Name, string? Correlation, string PayloadJson, string? InstanceId = null);
public enum PublishStatus { Queued = 1, Duplicate = 2, NoTarget = 3 }
public sealed record PublishResult(PublishStatus Status, int Instances);
public sealed record CommandResult(AdvanceDisposition Disposition, string? Code);
public sealed record ProcessInstanceInfo(
    string TenantId, string InstanceId, string ProcessId, long ProcessVersion, string BusinessKey,
    ProcessStatus Status, string? Stage, string? CurrentStep, string? Result, ProcessProblem? Problem,
    string? ParentInstanceId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, ProcessState? State);  // State null until the instance has started
public sealed record ProcessTaskInfo(string TenantId, string TaskId, string InstanceId, string Step, string Queue,
    IReadOnlyList<string> Outcomes, string ShowJson, DateTimeOffset CreatedAt);

// Ports the consumer supplies
public interface IProcessCatalog
{
    BrainProcess? Latest(string processId);                               // used by StartAsync and child starts
    BrainProcess? Find(string processId, long version, string contentHash);   // used for every later trigger
}
public interface IProcessPort
{
    string Name { get; }                                                   // the `port` of read, action and ai steps
    Task<PortResult> InvokeAsync(PortCall call, CancellationToken cancellationToken);
}
public sealed record PortCall(string TenantId, string InstanceId, EffectRequest Request);
public abstract record PortResult;                                         // S1694 suppression as on ProcessRequest
public sealed record PortCompleted(string OutputJson) : PortResult;
public sealed record PortFailed(ProcessErrorKind Kind, string Code, TimeSpan? RetryAfter = null) : PortResult;
public interface IProcessCalendar { DateOnly Today(string tenantId, DateTimeOffset now); }   // default: the UTC date
public interface IPayloadProtector { string Protect(string tenantId, string plaintext); string Unprotect(string tenantId, string protectedText); }

public sealed class ProcessHostOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 50;                 // 1..1000
    public int MaxConcurrentEffects { get; set; } = 8;       // 1..256
    public TimeSpan PortTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan EffectLease { get; set; } = TimeSpan.FromSeconds(120);   // must be > PortTimeout
}

public interface IProcessPump { Task<int> RunOnceAsync(CancellationToken cancellationToken = default); }   // one pass; returns the number of work items handled
```

```csharp
namespace Synapbiz.Brain.Host.Postgres;

public sealed class PostgresProcessStoreOptions
{
    public string ConnectionString { get; set; } = "";       // required
    public string Schema { get; set; } = "brain";            // ^[a-z_][a-z0-9_]{0,62}$
}

public static class ServiceCollectionExtensions
{
    // Registers IProcessEngine, IProcessPump, the hosted worker, the store and the default IProcessCalendar.
    // The consumer registers IProcessCatalog, its IProcessPort implementations and an IPayloadProtector.
    public static IServiceCollection AddBrainProcessHostPostgres(
        this IServiceCollection services,
        Action<PostgresProcessStoreOptions> configureStore,
        Action<ProcessHostOptions>? configureHost = null);

    public static IServiceCollection AddPlaintextProcessPayloads(this IServiceCollection services);   // registers a pass-through IPayloadProtector
}

public interface IProcessStoreMigrator { Task MigrateAsync(CancellationToken cancellationToken = default); }
```

Rules of the surface:

- Options are validated when first resolved; an invalid value throws `OptionsValidationException` naming the member.
- No `IPayloadProtector` registered → resolving the engine or the pump throws `InvalidOperationException` whose message tells the consumer to register one or call `AddPlaintextProcessPayloads`. There is no silent default.
- Time comes only from the registered `TimeProvider` (default `TimeProvider.System`); the SQL never calls `now()`. Identifiers the host generates use `Guid.NewGuid().ToString("N")`.
- `tenantId`, `processId`, `businessKey`, `eventId`, `taskId`: non-empty, at most 200 characters, else `ArgumentException`. `InputJson`, `PayloadJson`, `dataJson`: must be a JSON object text, else `ArgumentException`.
- The hosted worker (`BackgroundService`) calls the pump in a loop: after a pass that handled work it runs again at once, otherwise it waits `PollInterval`. An exception in a pass is logged and the loop continues after `PollInterval`.

## 3. Schema

`IProcessStoreMigrator.MigrateAsync` creates the schema and tables if missing, under a transaction-scoped advisory lock, and records applied scripts in `process_schema_version (version int primary key, applied_at timestamptz)`. Scripts are embedded resources `Migrations/0001_initial.sql`, numbered, each applied once, in order. Running it twice changes nothing. The worker does not migrate; the consumer calls the migrator at start-up.

Every table has `tenant_id text not null` as the first column of its primary key, and every statement filters on it. `timestamptz` everywhere; values come from `TimeProvider`.

| Table | Key | Other columns |
| --- | --- | --- |
| `process_instance` | (`tenant_id`, `instance_id`) | `process_id`, `process_version` bigint, `process_hash`, `business_key`, `status` smallint (0 = Created, then `ProcessStatus` ordinals), `stage`, `current_step`, `result`, `problem_code`, `state` text (protected `ProcessState.ToJson()`, null while Created), `revision` bigint, `parent_instance_id`, `parent_child_key`, `created_at`, `updated_at`. Unique (`tenant_id`, `process_id`, `business_key`) |
| `process_history` | (`tenant_id`, `instance_id`, `sequence`) | `at`, `step`, `entry` bigint, `attempt` int, `kind` smallint, `code`, `data` text (protected, nullable) |
| `process_timer` | (`tenant_id`, `timer_id`) | `instance_id`, `due_at`. Index on `due_at` |
| `process_wait` | (`tenant_id`, `wait_id`) | `instance_id`, `event`, `correlation` (nullable). Index on (`tenant_id`, `event`, `correlation`) |
| `process_task` | (`tenant_id`, `task_id`) | `instance_id`, `step`, `queue`, `outcomes` text (JSON array), `show` text (protected), `status` smallint (1 Open, 2 Completed, 3 Cancelled), `created_at`, `closed_at`. Index on (`tenant_id`, `queue`, `status`) |
| `process_effect` | (`tenant_id`, `effect_id`) | `instance_id`, `step`, `idempotency_key`, `kind` smallint, `port`, `input` text (protected), `attempt` int, `delivery` smallint, `model`, `prompt_id`, `prompt_version` bigint, `status` smallint (1 Pending, 2 Running, 3 Done, 4 Discarded), `due_at`, `lease_until`, `lease_owner`. Index on (`status`, `due_at`) |
| `process_event` | (`tenant_id`, `event_id`) | `name`, `received_at` (de-duplication record only) |
| `process_inbox` | (`tenant_id`, `inbox_id` bigint identity) | `instance_id`, `kind` smallint (1 Start, 2 Event, 3 ChildCompleted, 4 ChildFailed, 5 Cancel), `payload` text (protected JSON of the trigger's members), `created_at`, `status` smallint (1 Pending, 2 Done, 3 Rejected), `code`. Index on (`status`, `inbox_id`) |

No foreign keys across tables are required; no table is shared with any other library; the schema name is the configured one, quoted.

Protected columns hold `IPayloadProtector.Protect(tenantId, text)`; they are read only through `Unprotect`. Logs never contain a protected value, an input, an output, a payload or a variable: only tenant id, instance id, step, ids of timers/effects/tasks, kinds and codes.

## 4. The one transaction shape

Every change to an instance happens in one transaction of this shape, and in no other way:

1. `select … from process_instance where tenant_id = … and instance_id = … for update` (the worker adds `skip locked`; an instance locked by someone else is left for the next pass; API commands wait for the lock).
2. Re-validate the work item **under that lock** (the timer row still exists, the inbox row is still Pending, the effect row is still Running with this owner…). If not, do nothing for the item.
3. Resolve the definition with `IProcessCatalog.Find(process_id, process_version, process_hash)`. Null → the item is left untouched, a warning `process.definition.unavailable` is logged, the pass continues (the item is tried again on later passes).
4. `ProcessState.FromJson(Unprotect(state))`, `clock = new ProcessClock(timeProvider.GetUtcNow(), calendar.Today(tenantId, now))`, `result = process.Advance(state, trigger, clock)`.
5. If `Applied`: update the instance row (`state`, `status`, `stage`, `current_step`, `result`, `problem_code`, `revision + 1`, `updated_at`), insert `result.History`, and apply `result.Requests` in order (section 5). If `Ignored` or `Rejected`: the instance is not written.
6. Mark the work item consumed (delete the timer, set the inbox or effect row's final status and code).
7. Commit.

The instance row is always locked first; timer, wait, task, effect and inbox rows of an instance are only written while holding its instance lock. This ordering is what excludes deadlocks; an architecture or unit test must show that every store method that writes those tables takes the instance lock first (for example by a store-level test double or by asserting the SQL order in an integration test with two competing transactions).

## 5. Applying requests

| Request | Effect on the store |
| --- | --- |
| `EffectRequest` | insert `process_effect` Pending with `due_at = now` |
| `TimerRequest` | insert `process_timer` |
| `CancelTimerRequest` | delete the timer row if present |
| `WaitRequest` | insert `process_wait` |
| `CancelWaitRequest` | delete the wait row if present |
| `TaskRequest` | insert `process_task` Open |
| `CancelTaskRequest` | set the task Cancelled, `closed_at = now` |
| `ChildStartRequest` | resolve `IProcessCatalog.Latest(ProcessId)`; insert a `process_instance` row in status Created with a new instance id, `business_key = ChildKey`, the parent ids and the definition's id, version and hash; insert a Start inbox row for it carrying `InputJson`. `Latest` null → instead insert a ChildFailed inbox row for the parent with code `process.definition.unavailable` |

When a transaction leaves the instance `Completed` or `Cancelled`:

- every remaining `process_wait` and `process_timer` row of the instance is deleted, open tasks are set Cancelled, Pending effects are set Discarded;
- if it has a parent: `Completed` inserts a ChildCompleted inbox row for the parent (`ChildKey`, `Result`, `OutputJson` = the JSON object of all the child's variables); `Cancelled` inserts ChildFailed with code `process.child.cancelled`;
- if it was `Cancelled`: every child instance that is not Completed or Cancelled gets a Cancel inbox row (reason `process.parent.cancelled`).

A `Suspended` instance notifies nobody; its Pending effects are set Discarded (the interpreter has already cancelled waits, tasks and timers through requests).

## 6. Engine commands

- `StartAsync`: `IProcessCatalog.Latest(processId)` null → `InvalidOperationException` (`process.definition.unavailable`). Otherwise, in one transaction: insert the instance in status Created (`on conflict (tenant_id, process_id, business_key) do nothing`); if it already existed return its id with `Created = false` and do nothing else; else lock it, run `process.Start(instanceId, input, clock)` and persist exactly as steps 5 and 7 of section 4. A `Rejected` start (bad input) deletes the just-inserted row and throws `ArgumentException` carrying the code. Two concurrent starts with the same key create one instance and both return its id.
- `PublishEventAsync`: in one transaction, insert `process_event` (`on conflict do nothing`); a conflict returns `Duplicate` with 0 instances and writes nothing. Targets: when `InstanceId` is given, that instance (missing → `NoTarget`); otherwise every instance with a `process_wait` row for (`tenant`, `Name`) whose `correlation` is null or equals `Correlation`. No target → `NoTarget` (the event record is still kept, so a retry of the same id is `Duplicate`). Otherwise insert one Event inbox row per target and return `Queued` with the count. The event is applied later by the pump.
- `CompleteTaskAsync`: section 4 with trigger `TaskCompleted`, waiting for the lock; the task row must be Open, else `Ignored` with `process.trigger.stale`. On `Applied` the task becomes Completed. The interpreter's `Rejected` (unknown outcome, bad data) leaves the task Open.
- `CancelAsync` / `ResumeAsync`: section 4 with `CancelRequested` / `ResumeRequested`. Unknown instance → `Rejected`, `process.instance.not_found` (also for `CompleteTaskAsync` with an unknown task).
- Queries read committed data and unprotect what they return. A query for one tenant never returns another tenant's row.

## 7. The pump

One pass does, in this order, up to `BatchSize` items of each kind:

1. **Expired effect leases**: effects Running with `lease_until <= now`. Under the instance lock: delivery `AtLeastOnce` → back to Pending with `due_at = now` (the same `effect_id` and idempotency key will be invoked again); `AtMostOnce` → apply `EffectFailed(EffectId, Unknown, "process.effect.outcome_unknown")` and set the row Done. The effect is **not** invoked again.
2. **Inbox**: Pending rows in `inbox_id` order; for one instance, in id order (section 11.5; a later row of an instance is not applied while an earlier one is still Pending). Start → `process.Start` (section 6 persistence; a rejected start sets the instance `Cancelled` with `problem_code` and, when it has a parent, inserts ChildFailed with that code). Event → `EventReceived`; ChildCompleted / ChildFailed / Cancel → the matching trigger. `Applied` → Done; `Ignored` → Done with the code; `Rejected` → Rejected with the code.
3. **Due timers**: `due_at <= now`, in `due_at` order; under the lock, delete the row and apply `TimerFired`. A timer row that is gone is skipped.
4. **Pending effects**: `status = Pending and due_at <= now`. Claim under the instance lock: if the instance's `Pending` is not this effect (`Kind == Effect` and `Id == effect_id`), set the row Discarded; else set Running with `lease_until = now + EffectLease` and a `lease_owner` unique to this pump instance; commit. Then, outside any transaction and at most `MaxConcurrentEffects` at a time, invoke the port registered under the effect's `port` name with a cancellation token that fires after `PortTimeout`. Then, in a section 4 transaction that re-validates the row is Running with this owner, apply the trigger and set the row Done:

| What happened | Trigger |
| --- | --- |
| `PortCompleted(output)` | `EffectCompleted(EffectId, output)` |
| `PortFailed(kind, code, retryAfter)` | `EffectFailed(EffectId, kind, code, retryAfter)` |
| no port registered under that name | `EffectFailed(EffectId, NotAvailable, "process.port.not_registered")` (decided at claim time, nothing is invoked) |
| the port threw, or timed out, delivery `AtLeastOnce` | `EffectFailed(EffectId, Transient, "process.port.exception")` or `"process.port.timeout"` |
| the port threw, or timed out, delivery `AtMostOnce` | `EffectFailed(EffectId, Unknown, "process.effect.outcome_unknown")` |

If the pump is cancelled or crashes between the claim and the result, the row stays Running and step 1 of a later pass handles it.

`RunOnceAsync` returns the number of items it handled (applied, discarded or skipped-as-gone all count; items left because an instance was locked or a definition was unavailable do not). Several pumps may run at once, in one process or many.

Guarantees to prove with tests: an instance's state changes exactly once per trigger; a trigger is never lost once its transaction committed; an `AtLeastOnce` effect is invoked at least once per attempt and always with the same idempotency key for the same step entry; an `AtMostOnce` effect is invoked at most once per attempt.

## 8. Tests

Project `tests/Synapbiz.Brain.Host.Postgres.Tests` (xunit, FluentAssertions, `Microsoft.Extensions.TimeProvider.Testing` for `FakeTimeProvider`, `Testcontainers.PostgreSql`). They run against a real PostgreSQL:

- When the environment variable `BRAIN_TEST_POSTGRES` holds a connection string, use it. Otherwise start one `postgres:16-alpine` container for the test run with Testcontainers. If neither is possible the tests **fail** with a message that says how to provide a database; they never skip.
- Each test class uses its own schema name (random suffix) and drops it at the end, so classes can run in parallel and re-runs start clean.
- Tests drive `IProcessPump.RunOnceAsync` and a `FakeTimeProvider`; no test sleeps or depends on wall-clock time, except the two concurrency tests, which may use real parallel tasks but not timing assumptions.

Required cases:

1. Migrator: creates everything; second run is a no-op; two concurrent runs both succeed.
2. The reference outreach process of W1-A (`tests/fixtures/brain-process/outreach.*`) end to end with fake ports, in each of its five scenario paths, asserting final status, result, the history kinds in order, and that no timer, wait, open task or Pending effect is left at the end.
3. Start idempotency: same business key twice; 20 concurrent starts create one instance.
4. Events: duplicate event id; routing by wait and correlation; direct targeting with buffering through `accepts`; `NoTarget`; a rejected event marks the inbox row Rejected with the code.
5. Effects: retry delay honoured with the fake clock (not invoked before `due`, invoked after); expired lease `AtLeastOnce` is invoked again with the same idempotency key; expired lease `AtMostOnce` is not invoked again and the flow takes `onUnknown`; thrown exception and timeout for both delivery modes; unregistered port; an effect of a cancelled instance is Discarded and never invoked.
6. Tasks: listed per queue and tenant; completed; unknown outcome leaves it Open; timeout cancels it.
7. Child processes: parent waits for the child and receives its variables; missing child definition; cancelling the parent cancels the child.
8. Cancel and resume through the engine; resume of a suspended instance re-runs its step.
9. Concurrency A: 4 pumps running `RunOnceAsync` in parallel loops over 50 instances of a process with a read, a timer and an action until all complete: every instance Completed, every effect invoked exactly once per attempt (the fake port counts by `EffectId`), history sequences contiguous from 1 with no duplicates.
10. Concurrency B (lock order): for 200 iterations, one task completes a human task through the engine while a pump fires that task's timeout timer; the instance always ends in exactly one of the two outcomes and no PostgreSQL deadlock error (`40P01`) occurs.
11. Tenant isolation: the same business key and the same event id in two tenants do not collide; no query or command with tenant A's id can read or change tenant B's instance, task or history.
12. Payload protection: with a protector that prefixes a marker and reverses the text, no protected column contains a known plaintext variable value, and everything round-trips.
13. Definition unavailable: the item is left, a warning is logged, and after the catalog gains the definition the next pass applies it.
14. Options and argument validation, and the missing-protector error.
15. Logging hygiene: with a capturing logger, across the end-to-end test no log message or argument contains any variable value, input or output used in the test.

Architecture tests (in `tests/Synapbiz.Brain.ArchitectureTests`): the new assembly references only the base class library, `Synapbiz.Brain.Process` and its Brain dependencies, `Npgsql` and the four `Microsoft.Extensions.*` abstractions packages above (and their transitive assemblies, listed explicitly as the restore graph holds them); its project file declares exactly those packages and the one project reference; no type outside the PostgreSQL store class and the migrator references the `Npgsql` namespace; the pure libraries (six after the merge of the workbench panels) stay in the purity scan and the host is **not** added to it.

## 9. Gates

`dotnet build Synapbiz.Brain.sln -c Release`: 0 errors, the same 2 base warnings and none in a new or touched file; no `NoWarn`, no `#pragma warning disable`, no analyzer switched off, no skipped test; `SuppressMessage` only with a written justification and only for a rule the code base already suppresses for the same reason. `dotnet test Synapbiz.Brain.sln -c Release --no-build --collect:"XPlat Code Coverage"`: all green, the existing tests unchanged (1228 at the base, 1436 after the merge of the workbench panels). Line coverage of `Synapbiz.Brain.Host.Postgres` ≥ 85 %. `dotnet pack` produces one more package than the base (seven after the merge of the workbench panels). No change to `.github/`, `Directory.Build.props`, `tools/` or the parity solution; `Directory.Packages.props` gains only the package versions this PR needs.

## 10. Rulings on the implementer's interpretations (2026-11-23)

These settle points the sections above left open. They are normative.

1. An instance whose start has been accepted but not yet applied by a pump is reported by `GetInstanceAsync` and `FindInstanceAsync` with status `Running` and a null state. The interpreter has no "created" status and the host does not invent one.
2. `CancelAsync` and `ResumeAsync` on such an instance return `Ignored` with `process.trigger.stale`. The caller retries after the first pump pass. Nothing is queued on its behalf.
3. Parameter names of the port interface are not part of the contract; `portCall` stands.
4. Appending an inbox row is the one write allowed outside the transaction shape of section 4: it is an insert guarded by the unique event key, it touches no instance row, and an event may precede its instance. Consuming an inbox row always happens inside the shape.
5. An effect result is applied under the instance lock taken with a wait, not `SKIP LOCKED`: the port call has already happened and its result must not be dropped or deferred to a lease expiry.
6. When the catalog cannot supply the pinned definition, a pending effect stays `Pending` and no port is invoked (same treatment as test 13).
7. A rejected start leaves no row: the transaction is rolled back.
8. The pump scans across tenants and every row it claims carries its `tenant_id`, which scopes every later statement of that unit of work. The engine's commands and queries always take the tenant from the caller.

## 11. Amendments after independent verification (2026-11-23)

Normative; where they differ from sections 5 to 7 they win.

1. **Item isolation.** An exception while the pump handles one item (other than cancellation of the pass) is caught for that item: a warning is logged with the item kind, the tenant, the ids and the exception type, never its message; the item is left as it was; the pass continues with the next item and the next kind.
2. **Paging.** Within one pass each scan is paged by keyset on its ordering key until the scan is exhausted or `BatchSize` items of that kind have been handled, so items that are left untouched cannot hide the ones behind them.
3. **Correlation candidates.** Waits are selected for an event by event name and by correlation compared without regard to case; the interpreter decides whether each candidate accepts the event. An event no candidate accepts is `NoTarget`.
4. **Effect lease.** A pump takes a concurrency slot first, then claims one pending effect, then invokes the port; the lease runs from that claim. An effect that was not claimed stays `Pending`. After the pass token is cancelled no further effect is claimed, and the result of a call already in flight is recorded regardless of the token.
5. **Inbox order.** Rows are applied in id order per instance. Two triggers appended concurrently have no defined order between them.
6. **Values stored in clear.** `process_wait.correlation` and failure codes supplied by a port are not protected. A consumer must use opaque references as correlation values and plain codes as failure codes, never personal data. An event id is consumed by any answer to its publication, including `NoTarget`.
7. **Candidate whose definition is unavailable.** When a wait matched an event only without regard to case and the catalog cannot supply its definition at publication, the instance is kept as a target and the pump applies the event once the definition is back; the interpreter then decides.

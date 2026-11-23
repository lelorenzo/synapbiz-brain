# The PostgreSQL process host

`Synapbiz.Brain.Host.Postgres` runs `brain.process@1` instances durably. It stores the state and the history of every instance, executes what the interpreter asks for (port calls, timers, waits, human tasks, child instances), feeds the results back as triggers and survives crashes and concurrent workers. The interpreter itself is described in [brain-process-v1](brain-process-v1.md); this page is about the host. The normative specification is [spec-b-postgres-host](../specs/w1-process-engine/spec-b-postgres-host.md).

It needs PostgreSQL 14 or later and references `Synapbiz.Brain.Process`, `Npgsql` and four `Microsoft.Extensions.*` abstractions packages, nothing else. All examples below are synthetic.

## What the consumer supplies

The host owns the database and the loop. The consumer owns everything that touches the outside world:

| Port | What it is | Notes |
| --- | --- | --- |
| `IProcessCatalog` | Finds `BrainProcess` objects | `Latest(processId)` is used when an instance starts (and for child starts); `Find(processId, version, contentHash)` is used for every later trigger, so a running instance always meets the definition it was pinned to. Return null for a definition you no longer have; the host leaves the work item alone and retries it on later passes. |
| `IProcessPort` | One per port name used by `read`, `action` and `ai` steps | `Name` is the `port` of the step. `InvokeAsync(PortCall, CancellationToken)` returns `PortCompleted(outputJson)` or `PortFailed(kind, code, retryAfter)`. |
| `IPayloadProtector` | Encrypts what the host stores | Required. There is no silent default; call `AddPlaintextProcessPayloads()` in development and tests. |
| `IProcessCalendar` | The local date of a tenant | Optional; the default is the UTC date. |
| `TimeProvider` | The clock | Optional; the default is `TimeProvider.System`. The host never reads another clock and the SQL never calls `now()`. |

## Registering the host

```csharp
services.AddSingleton<IProcessCatalog, MyCatalog>();
services.AddSingleton<IProcessPort, IntakeStatusPort>();     // Name = "intake.status"
services.AddSingleton<IProcessPort, MessagingNoticePort>();  // Name = "messaging.notice"
services.AddSingleton<IPayloadProtector, MyProtector>();     // or services.AddPlaintextProcessPayloads();

services.AddBrainProcessHostPostgres(
    store =>
    {
        store.ConnectionString = configuration.GetConnectionString("Brain")!;
        store.Schema = "brain";                               // ^[a-z_][a-z0-9_]{0,62}$
    },
    host =>
    {
        host.PollInterval = TimeSpan.FromSeconds(5);          // wait after a pass that handled nothing
        host.BatchSize = 50;                                  // items of each kind per pass, 1..1000
        host.MaxConcurrentEffects = 8;                        // port calls at once per pump, 1..256
        host.PortTimeout = TimeSpan.FromSeconds(60);
        host.EffectLease = TimeSpan.FromSeconds(120);         // must be longer than PortTimeout
    });
```

Options are validated when first resolved; an invalid value throws `OptionsValidationException` naming the member. Resolving the engine or the pump without an `IPayloadProtector` throws `InvalidOperationException`.

The registration adds `IProcessEngine`, `IProcessPump`, `IProcessStoreMigrator` and a hosted worker (a `BackgroundService`). The worker calls the pump in a loop: after a pass that handled work it runs again at once, otherwise it waits `PollInterval`. A pass that throws is logged (the exception type only) and the loop goes on after `PollInterval`. Several workers, in one process or many, may run against the same schema.

### Writing a port

```csharp
public sealed class MessagingNoticePort : IProcessPort
{
    public string Name => "messaging.notice";

    public async Task<PortResult> InvokeAsync(PortCall portCall, CancellationToken cancellationToken)
    {
        // portCall.TenantId, portCall.InstanceId
        // portCall.Request.IdempotencyKey: the same value on every delivery of the same step entry
        // portCall.Request.InputJson, Attempt, Delivery, Model/PromptId/PromptVersion for ai steps
        var sent = await gateway.SendAsync(portCall.Request.IdempotencyKey, portCall.Request.InputJson, cancellationToken);
        return sent.Accepted
            ? new PortCompleted("{\"receipt\":\"" + sent.Receipt + "\"}")
            : new PortFailed(ProcessErrorKind.Transient, "messaging.busy", TimeSpan.FromMinutes(1));
    }
}
```

Return `PortFailed` for failures you understand (`Transient`, `RateLimited`, `NotAvailable`, `NotEntitled`, `Permanent`); the step's retry policy and its `onError` routes decide what happens next. Honour the cancellation token: it fires after `PortTimeout`. A port that throws, or does not answer in time, is a failure the host classifies for you (see the delivery guarantees).

### Running the migrator

The worker never migrates. Call the migrator once at start-up, before the host starts:

```csharp
await app.Services.GetRequiredService<IProcessStoreMigrator>().MigrateAsync();
```

It creates the schema and the tables if missing, under a transaction-scoped advisory lock (two processes may call it at once), applies each embedded script `Migrations/NNNN_*.sql` once in order, and records it in `process_schema_version`. A second run changes nothing.

## Starting, publishing, completing

```csharp
var engine = provider.GetRequiredService<IProcessEngine>();

// One instance per business key and process. A second call returns the same instance with Created = false.
var started = await engine.StartAsync(new StartRequest("t-1", "previsit.outreach", "case-1", "{\"case_ref\":\"c-1\"}"));

// An event is de-duplicated by its id. It reaches every instance that waits for the name (and the correlation),
// or the one named in InstanceId, which buffers it when its definition lists the event under `accepts`.
var published = await engine.PublishEventAsync(new ProcessEvent("t-1", "e-1", "form_submitted", "c-1", "{}"));
// published.Status: Queued, Duplicate or NoTarget

// A human task is completed by its id with one of its outcomes and the data it declares.
var task = (await engine.ListOpenTasksAsync("t-1", "front_desk")).First();
var done = await engine.CompleteTaskAsync("t-1", task.TaskId, "resolved", "{}");
// done.Disposition: Applied, Ignored (stale, for example completed twice) or Rejected (unknown outcome, bad data)

await engine.CancelAsync("t-1", started.InstanceId, "operator.cancelled");
await engine.ResumeAsync("t-1", started.InstanceId);   // a suspended instance re-runs the step that stopped it
```

`StartAsync` returns once the first run of the instance is committed. Everything else happens in the pump: events and child notifications are applied as inbox rows, timers when they fall due, port calls when they are claimed. Queries (`GetInstanceAsync`, `FindInstanceAsync`, `GetHistoryAsync`, `ListOpenTasksAsync`) read committed data, unprotect what they return and never return another tenant's row.

Identifiers (`tenantId`, `processId`, `businessKey`, `eventId`, `taskId`) must be non-empty and at most 200 characters; `InputJson`, `PayloadJson` and `dataJson` must be the text of a JSON object. Otherwise the call throws `ArgumentException` before touching the database.

## The one transaction shape

Every change to an instance happens in one transaction of this shape:

1. Lock the instance row (`select ... for update`; the pump adds `skip locked` and leaves a locked instance for the next pass, the engine commands wait).
2. Re-validate the work item under that lock: the timer row still exists, the inbox row is still Pending, the effect row is still Running for this owner.
3. Find the definition through `IProcessCatalog.Find`. Null leaves the item untouched and logs `process.definition.unavailable`.
4. Unprotect and load the state, run `BrainProcess.Advance` with the trigger and the clock.
5. If the interpreter applied the trigger: update the instance, append the history and apply the requests (insert effects, timers, waits and tasks, start children). Otherwise write nothing.
6. Consume the work item.
7. Commit.

The instance row is always locked first, and the rows of an instance's timers, waits, tasks, effects and inbox are only written while holding that lock. That uniform order is what excludes deadlocks; the concurrency tests run a task completion against a timeout timer 200 times and require exactly one outcome and no `40P01`.

## Delivery guarantees and what at-most-once means after a crash

State changes are exactly-once per trigger: a trigger is applied in one transaction that also consumes the work item that carried it, and a trigger that has been committed is never lost. Effects (port calls) cannot be part of that transaction, so their guarantee is the step's `delivery` mode:

| Mode | Normal run | Lease expired (the pump died between the claim and the result) | Port threw or timed out |
| --- | --- | --- | --- |
| `atLeastOnce` | invoked once per attempt | invoked again with the same effect id and idempotency key | `EffectFailed` `Transient`, code `process.port.exception` or `process.port.timeout`; the retry policy decides |
| `atMostOnce` | invoked once per attempt | **not** invoked again: the host applies `EffectFailed` with kind `Unknown` and code `process.effect.outcome_unknown` | the same `Unknown` failure |

So for an `atMostOnce` effect, after a crash the system does not know whether the message went out. It never sends it a second time; the flow takes the step's `onUnknown` route (typically a human task, per owner decision 6 of the [design](../specs/w1-process-engine/design.md)). For an `atLeastOnce` effect the port must dedupe on the idempotency key, which is identical on every delivery of the same step entry. An effect whose instance was cancelled, completed or suspended before the claim is Discarded and never invoked. A port name nobody registered fails the effect with `NotAvailable`, code `process.port.not_registered`, without invoking anything.

The claim sets the row Running with a lease of `EffectLease` and an owner unique to the pump; the port is called outside any transaction; the result is applied in a new transaction that checks the row is still Running for that owner. If the pump is cancelled or crashes in between, the row stays Running and the lease-expiry step of a later pass handles it. Delivery order across effects is not guaranteed; the order of triggers for one instance is (inbox rows of an instance are applied strictly in order).

## What a pass does

`IProcessPump.RunOnceAsync` handles, in this order and up to `BatchSize` of each: expired effect leases, inbox rows, due timers, pending effects (claim, invoke, apply the result). It returns the number of items handled (applied, discarded or found gone; items left because an instance was locked or a definition was unavailable do not count). Tests and hosts without the worker can drive it directly.

## Tables

All tables live in the configured schema, carry `tenant_id` as the first column of their primary key, and every statement filters on it. Times are `timestamptz` and come from the `TimeProvider`.

| Table | Holds |
| --- | --- |
| `process_instance` | One row per instance: pinned process id, version and content hash, business key (unique per tenant and process), status, stage, current step, result, problem code, protected state, revision, parent and child key |
| `process_history` | Append-only audit trail per instance: sequence, step, entry, attempt, kind, code, protected data |
| `process_timer` | Timers still to fire |
| `process_wait` | Waits for an event name and an optional correlation |
| `process_task` | Human tasks: queue, outcomes, protected `show`, status Open, Completed or Cancelled |
| `process_effect` | Port calls: id, idempotency key, port, protected input, attempt, delivery, status Pending, Running, Done or Discarded, due time, lease and owner |
| `process_event` | De-duplication record of published event ids |
| `process_inbox` | Triggers waiting to be applied: Start, Event, ChildCompleted, ChildFailed, Cancel, with status Pending, Done or Rejected and the code |
| `process_schema_version` | Applied migration scripts |

Protected columns (instance state, history data, effect input, task `show`, inbox payload) go through `IPayloadProtector.Protect(tenantId, text)` and are read only through `Unprotect`. The business key, the wait correlation, ids, step names, kinds and codes are stored as plain text because the host has to query them. Retention and purging of finished instances are not part of this release.

## What is logged

Log messages carry tenant ids, instance ids, step names, ids of timers, effects and tasks, kinds and codes; never a variable, an input, an output, a payload or the message of an exception (a port that throws is logged by exception type). They use source-generated `LoggerMessage` methods with stable names:

| Message | Level | When |
| --- | --- | --- |
| `process.definition.unavailable` | Warning | The catalog has no definition for a work item; it is retried |
| `process.child.definition_unavailable` | Warning | A child start named a process the catalog does not know |
| `process.port.exception`, `process.port.timeout`, `process.port.not_registered` | Warning | A port threw, timed out or is not registered |
| `process.effect.lease_expired` | Warning | A Running effect's lease ran out |
| `process.instance.finished`, `process.instance.suspended` | Information, Warning | An instance ended, or stopped on a problem |
| `process.inbox.refused` | Information | An inbox row was ignored or rejected, with its code |
| `process.worker.started`, `process.worker.pass_failed` | Information, Error | The worker loop |

## Testing against PostgreSQL

The tests of the host run against a real database. Set `BRAIN_TEST_POSTGRES` to a connection string, or let the test run start a `postgres:16-alpine` container with Testcontainers; with neither the tests fail with a message saying so. Each test class uses its own schema and drops it at the end. Tests drive `IProcessPump.RunOnceAsync` with a `FakeTimeProvider`.

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

Return `PortFailed` for failures you understand (`Transient`, `RateLimited`, `NotAvailable`, `NotEntitled`, `Permanent`); the step's retry policy and its `onError` routes decide what happens next. Honour the cancellation token: it fires after `PortTimeout` and when the pump is stopped. A port that throws, or does not answer in time, is a failure the host classifies for you (see the delivery guarantees).

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
// published.Status: Queued, Duplicate or NoTarget. The event id is consumed by any answer, NoTarget included:
// publishing the same id again is a Duplicate, so a caller that wants to retry a NoTarget uses a new id.

// A human task is completed by its id with one of its outcomes and the data it declares.
var task = (await engine.ListOpenTasksAsync("t-1", "front_desk")).First();
var done = await engine.CompleteTaskAsync("t-1", task.TaskId, "resolved", "{}");
// done.Disposition: Applied, Ignored (stale, for example completed twice) or Rejected (unknown outcome, bad data)

await engine.CancelAsync("t-1", started.InstanceId, "operator.cancelled");
await engine.ResumeAsync("t-1", started.InstanceId);   // a suspended instance re-runs the step that stopped it
```

`StartAsync` returns once the first run of the instance is committed. Everything else happens in the pump: events and child notifications are applied as inbox rows, timers when they fall due, port calls when they are claimed. Queries (`GetInstanceAsync`, `FindInstanceAsync`, `GetHistoryAsync`, `ListOpenTasksAsync`) read committed data, unprotect what they return and never return another tenant's row.

A child instance is created in the transaction of its parent but started by the pump. Until a pump pass has applied its start, `GetInstanceAsync` and `FindInstanceAsync` report it with status `Running` and a null `State` (the interpreter has no "created" status and the host does not invent one), and `CancelAsync` and `ResumeAsync` on it return `Ignored` with `process.trigger.stale`: nothing is queued on the caller's behalf, so the caller retries after the first pass.

Routing an event by correlation compares the correlation without regard to case, so that a `Reference` correlation (which the interpreter stores lower-case and matches whatever the case of the input) finds its wait. The host hands the event to the interpreter for the waits that matched only by case and keeps those the interpreter takes, so a `Code` correlation that differs from the stored one in case is `NoTarget`, as before. An event whose instance cannot be asked because its definition is not in the catalog stays a target and the pump decides when the definition is back.

Identifiers (`tenantId`, `processId`, `businessKey`, `eventId`, `taskId`) must be non-empty and at most 200 characters; `InputJson`, `PayloadJson` and `dataJson` must be the text of a JSON object. Otherwise the call throws `ArgumentException` before touching the database.

## Activation, event triggers, schedules and delivery windows

The `triggers` of a definition (see [brain-process-v1](brain-process-v1.md)) do nothing until the consumer activates the process for a tenant:

```csharp
// The latest version in the catalog is pinned. Zone is the zone of every schedule that declares none; InputJson is the
// input every instance started by a trigger begins with (it must satisfy the `input: true` variables).
var activation = await engine.ActivateAsync(new ProcessActivation("t-1", "previsit.followup", Zone: "America/New_York", InputJson: "{\"channel\":\"sms\"}"));
// activation.ProcessVersion is the pinned version; activation.Changed is false when nothing was different.

await engine.DeactivateAsync("t-1", "previsit.followup");   // true when there was an activation; running instances are not touched
```

`ActivateAsync` validates the input with a dry run of the interpreter's `Start`, resolves every zone a schedule needs (the zone of the trigger, else `ProcessActivation.Zone`; a schedule with neither, or a zone id the host cannot resolve, is an `ArgumentException` naming the schedule id; `ProcessActivation.Zone`, when given, must have the shape `UTC` or `Area/Location` before the resolver is asked, otherwise the `ArgumentException` names the parameter and never the input) and writes, in one transaction taken under an advisory lock per tenant and process, one activation row (pinned version and hash, zone, protected input), one row per event trigger and one row per schedule. It is idempotent: the same activation again returns `Changed = false` and keeps every `next_fire_at`. A new version, zone or input replaces the rows; a schedule whose cron expression and zone did not change keeps its `next_fire_at`, so changing the input does not skip or repeat an occurrence. The catalog is asked once, at activation: later passes use the pinned version, so a newer version of the definition changes nothing until the process is activated again.

### Event triggers

`PublishEventAsync` first de-duplicates the event id, as before. It then looks at the event triggers of the tenant for the event name. For each one it reads the business key member of the payload. The key must be a JSON string that, after trimming, is not empty, has at most 200 characters, holds no control character (U+0000 to U+001F, U+007F to U+009F) and no unpaired surrogate, and does not start with `@` (that prefix is reserved for the keys the host makes, see below). A member that is missing, not a string, or whose string breaks one of those rules, or a member name the payload writes twice, means no start for that trigger, logged as `process.trigger.no_business_key` with ids only, never the payload. Reading the key never throws, and a bad key never prevents the event id from being recorded or the instances that wait for the event from receiving it: the event is routed as it always was. Otherwise it queues a start through the same code path as `StartAsync`, so the unique business key guarantees one instance per key: publishing the same event again (a new event id with the same key), or publishing concurrently, never creates a second instance, and an instance that already has the key is left alone. The input is the activation input with the mapped payload members laid over it; a mapped member the payload does not carry leaves the variable as the activation has it. `PublishResult.Instances` counts the started instances as well as the targeted ones, and the status is `Queued` when at least one instance was targeted or started. The event routing to waiting instances is unchanged.

The rest of the payload follows the same rule of never throwing, and each case ends as follows:

- A payload that holds an unpaired surrogate anywhere (in a value, in a member name, deep inside, mapped or not; written as the JSON escape `\ud800` on its own) cannot be read as text by anything: no trigger starts an instance (`process.trigger.no_business_key`), the event id is recorded, and an instance that waits for the event still receives it (the interpreter then judges the payload as it does any malformed one). The caller's own text, when it holds such a surrogate as a real character, is refused before anything is written with an `ArgumentException` (it cannot be encoded) and the event id is not consumed.
- A mapped value that carries `\u0000` is valid data: the instance starts and keeps it. The database cannot store that character in a text column, so a step that would use it as a wait correlation suspends the instance with `process.wait.correlation_invalid` instead of failing on every pass; the start row is finished.
- A mapped value the interpreter does not accept for the variable (wrong type) is a rejected start: the start row ends as `Rejected` with the interpreter's code, the instance row it created is cancelled, and nothing is retried.

### Schedules

Due schedule rows (`next_fire_at <= now`) are the first step of every pump pass, before expired leases. Each one is handled in one transaction that locks the schedule row with `for update skip locked` and reads it again under the lock, so several pumps never fire the same occurrence twice; a row another pump holds is left for the next pass. The occurrences `o1 < ... < ok <= now` since `next_fire_at` are computed in the zone of the schedule:

- when there is exactly one and it is on time, it fires; an occurrence is on time when `now - o` is at most `ProcessHostOptions.ScheduleMisfireGrace` (default 5 minutes, zero allowed, negative invalid). Exactly the grace is on time.
- otherwise the schedule misfired (the host was down or late). With `skip`, every occurrence that is not on time is dropped and the last one fires only if it is on time. With `fireOnce`, the earlier ones are dropped and the last one fires whether or not it is on time. A misfire is logged as `process.schedule.misfired`. The host never starts one instance per missed occurrence.
- `next_fire_at` becomes the first occurrence after `now`.

A fired occurrence queues a start with the business key `@schedule/{scheduleId}/{yyyyMMddTHHmmZ}` (the occurrence in UTC; the reserved `@` means that a key taken from an event payload can never collide with it) and the input of the activation with `scheduledFor` (an instant, `yyyy-MM-ddTHH:mm:ssZ`) and `scheduledDate` (the date of the occurrence in the zone of the schedule) laid over it as the declaration maps them. The unique business key means a retry after a crash, or any second pump, cannot start a second instance for the occurrence. When the catalog cannot supply the pinned version the row is left untouched and `process.definition.unavailable` is logged. Schedule items follow the same item isolation and paging rules as every other work kind.

A cron day field counts as restricted unless its text is exactly `*`, so `*/1` or `1-31` restricts the day (when both day fields are restricted a day matches when either does). Two behaviours of the wall clock are documented and unchanged: in the repeated hour of a daylight saving change an expression fires on the first pass only, so an every-minute or hourly schedule is silent for that one hour; and on a day a zone skips entirely (a date-line change) nothing fires for that day.

Occurrences are computed on the wall clock of the zone, not by adding 24 hours. A local time that does not exist on a day of the spring transition fires once, at the first valid instant after the gap (a `30 2` schedule fires at 03:00 local); a local time that occurs twice on a day of the autumn transition fires at its first occurrence only.

### Delivery windows

When the interpreter asks for a window (`WindowRequest`, a `contact` action outside its days and hours), the host stores a timer whose due time is `DeliveryWindows.NextOpen` for the zone the request names, so the contact is made at the opening and never before, and the retry of a failed contact passes the same gate again. When the zone id cannot be resolved nothing is stored and a `WindowFailed` trigger with the code `process.window.zone_unknown` is queued through the inbox of the instance: the step routes through its `onError` or the instance is suspended, as the interpreter decides. The zone id, whether it is the zone of the window or the value of its zone variable, must first have the shape `UTC` or `Area/Location` (`^[A-Za-z_]+(/[A-Za-z0-9_+\-]+){1,2}$`): any other text (`localtime`, `EST5EDT`, `Factory`, a path, an empty text) is `process.window.zone_unknown` without the time zone database being asked.

A window timer is not re-checked when it fires: the due time was inside the window by construction and timer precision is the pump tick, so a contact can leave a pump pass that runs after the window closed, and a clock that changed the zone rules since the timer was stored is not noticed. For the same reason an `atLeastOnce` contact action whose lease expired may be requested again after the window closed. Declare a contact action `atMostOnce` when a repeat outside the window is worse than a missed contact.

### Time zones

Zone ids are resolved through one internal seam, `IZoneResolver`. The default implementation asks the machine's time zone database (the only place in the host that does) and returns no zone for an unknown id. The shape check (`DeliveryWindows.IsZoneIdShape`, public in the process library) runs before the resolver is asked, so the system database never sees an id such as `localtime` or a path. The interface is internal and not part of the public surface: the tests of the host (which see it through `InternalsVisibleTo`) register their own resolver and inject zones built with `TimeZoneInfo.CreateCustomTimeZone`, so none of them depends on the system database.

## The one transaction shape

Every change to an instance happens in one transaction of this shape (a schedule is handled in the same spirit: the schedule row is locked first, with `skip locked`, re-read under the lock, and the start it queues is written in the same transaction; an activation is written under a transaction-scoped advisory lock per tenant and process):

1. Lock the instance row (`select ... for update`; the pump adds `skip locked` and leaves a locked instance for the next pass, the engine commands wait). The one exception to the waiting rule is the pump applying the result of a port call it has already made: it waits for the lock too, because the call happened and its result must not be dropped or deferred to a lease expiry.
2. Re-validate the work item under that lock: the timer row still exists, the inbox row is still Pending, the effect row is still Running for this owner.
3. Find the definition through `IProcessCatalog.Find`. Null leaves the item untouched and logs `process.definition.unavailable`.
4. Unprotect and load the state, run `BrainProcess.Advance` with the trigger and the clock.
5. If the interpreter applied the trigger: update the instance, append the history and apply the requests (insert effects, timers, waits and tasks, start children). Otherwise write nothing.
6. Consume the work item.
7. Commit.

The instance row is always locked first, and the rows of an instance's timers, waits, tasks, effects and inbox are only written while holding that lock. The one write outside the shape is appending an inbox row (publishing an event, notifying a parent or a child): it is a plain insert guarded by the unique event key, it touches no instance row, and an event may be published before its instance exists; consuming an inbox row always happens inside the shape. That uniform order is what excludes deadlocks; the concurrency tests run a task completion against a timeout timer 200 times and require exactly one outcome and no `40P01`.

## Delivery guarantees and what at-most-once means after a crash

State changes are exactly-once per trigger: a trigger is applied in one transaction that also consumes the work item that carried it, and a trigger that has been committed is never lost. Effects (port calls) cannot be part of that transaction, so their guarantee is the step's `delivery` mode:

| Mode | Normal run | Lease expired (the pump died between the claim and the result) | Port threw or timed out |
| --- | --- | --- | --- |
| `atLeastOnce` | invoked once per attempt | invoked again with the same effect id and idempotency key | `EffectFailed` `Transient`, code `process.port.exception` or `process.port.timeout`; the retry policy decides |
| `atMostOnce` | invoked once per attempt | **not** invoked again: the host applies `EffectFailed` with kind `Unknown` and code `process.effect.outcome_unknown` | the same `Unknown` failure |

So for an `atMostOnce` effect, after a crash the system does not know whether the message went out. It never sends it a second time; the flow takes the step's `onUnknown` route (typically a human task, per owner decision 6 of the [design](../specs/w1-process-engine/design.md)). For an `atLeastOnce` effect the port must dedupe on the idempotency key, which is identical on every delivery of the same step entry. An effect whose instance was cancelled, completed or suspended before the claim is Discarded and never invoked. A port name nobody registered fails the effect with `NotAvailable`, code `process.port.not_registered`, without invoking anything. When the catalog cannot supply the definition the instance is pinned to, the effect stays Pending and no port is invoked; a later pass handles it once the definition is back.

For every due effect the pump takes a concurrency slot (`MaxConcurrentEffects`) first, then claims that one effect, then invokes the port at once. The claim sets the row Running with a lease of `EffectLease` and an owner unique to the pump, so the lease starts immediately before the call and cannot run out while the effect waits for a slot; an effect that has not been claimed stays Pending and is untouched by lease expiry on any pump. The port is called outside any transaction; the result is applied in a new transaction that waits for the instance lock and checks the row is still Running for that owner. If the pump crashes between the claim and the result, the row stays Running and the lease-expiry step of a later pass handles it.

What a graceful stop leaves behind: when the pass token is cancelled (the worker stops) no further effect is claimed, so effects that were never claimed stay Pending and are invoked by a later pass, including `atMostOnce` effects, which therefore never become `Unknown` because of a stop. A port call already in flight is awaited; if its result arrives it is recorded whatever the state of the token (the port did its work, and dropping the result would create an `Unknown`). If the port gives way to the token (it throws the cancellation) there is no result, the row stays Running, and its lease decides: `atLeastOnce` is delivered again, `atMostOnce` becomes `Unknown`. `RunOnceAsync` ends with `OperationCanceledException` once the calls in flight are recorded. A port that ignores its token delays the stop until it answers or `PortTimeout` passes.

Delivery order across effects is not guaranteed. Inbox rows are applied in id order per instance: a later row of an instance is not applied while an earlier one is still Pending. Two triggers appended concurrently for the same instance have no defined order between them.

## What a pass does

`IProcessPump.RunOnceAsync` handles, in this order and up to `BatchSize` of each: due schedules, expired effect leases, inbox rows, due timers, pending effects (claim, invoke, apply the result). It returns the number of items handled (applied, discarded or found gone; items left because an instance was locked, a definition was unavailable or the item failed do not count). Tests and hosts without the worker can drive it directly.

Two properties keep a stuck item from harming the others:

- **Per-item isolation.** An exception while one item is handled (a protector that throws for one tenant, a corrupt state, a malformed inbox payload) does not abort the pass. It is logged as `process.item.failed` with the item kind, the tenant, the ids and the exception type (never its message), the item is left as it was, and the pass goes on with the next item and the next kind. Only the cancellation of the pass itself ends it. A failing item is tried again on every later pass.
- **Paging.** Each scan reads its items page by page with a keyset cursor on its ordering key (time, then tenant, then id; the inbox by id) until the scan is exhausted or `BatchSize` items of that kind have been handled in the pass. Items that are left untouched (an instance locked by another pump, a definition not in the catalog, a failing item) therefore cannot hide the items behind them, however small `BatchSize` is.

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
| `process_inbox` | Triggers waiting to be applied: Start, Event, ChildCompleted, ChildFailed, Cancel, WindowFailed, with status Pending, Done or Rejected and the code |
| `process_activation` | One row per tenant and process: pinned version and hash, the zone the consumer supplied, the protected input every triggered instance begins with, the activation time |
| `process_event_trigger` | One row per event trigger of an activated process (tenant, event, process): the business key member and the map of payload members to input variables, both copied from the pinned definition (names, never values) |
| `process_schedule` | One row per schedule trigger of an activated process (tenant, process, schedule id): cron text, the zone it is evaluated in, the misfire policy and `next_fire_at`, the first occurrence not handled yet; indexed by due time |
| `process_schema_version` | Applied migration scripts |

Protected columns (instance state, history data, effect input, task `show`, inbox payload, activation input) go through `IPayloadProtector.Protect(tenantId, text)` and are read only through `Unprotect`. The business key, the wait correlation, ids, step names, kinds and codes are stored as plain text because the host has to query them. In particular `process_wait.correlation` and the failure codes that a port returns (`PortFailed.Code`, which end up in the history) are stored in clear: a consumer must use opaque references as correlation values and plain codes as failure codes, never personal data. Retention and purging of finished instances are not part of this release.

## What is logged

Log messages carry tenant ids, instance ids, step names, ids of timers, effects and tasks, kinds and codes; never a variable, an input, an output, a payload or the message of an exception (a port that throws is logged by exception type). They use source-generated `LoggerMessage` methods with stable names:

| Message | Level | When |
| --- | --- | --- |
| `process.definition.unavailable` | Warning | The catalog has no definition for a work item; it is retried |
| `process.child.definition_unavailable` | Warning | A child start named a process the catalog does not know |
| `process.port.exception`, `process.port.timeout`, `process.port.not_registered` | Warning | A port threw, timed out or is not registered |
| `process.effect.lease_expired` | Warning | A Running effect's lease ran out |
| `process.instance.finished`, `process.instance.suspended` | Information, Warning | An instance ended, or stopped on a problem |
| `process.inbox.refused` | Information | An inbox row was rejected, with its code (rows that were applied or ignored are not logged) |
| `process.item.failed` | Warning | One work item threw while the pump handled it (kind, tenant, ids, exception type; for a schedule the process id and schedule id); it is left as it was |
| `process.trigger.no_business_key` | Warning | An event trigger found no usable business key in the payload (tenant, event id, event name, process id; no payload) |
| `process.schedule.fired` | Information | A schedule occurrence queued its start (schedule id, the occurrence, whether the instance was new) |
| `process.schedule.misfired` | Warning | Occurrences were missed: the policy, the last missed occurrence and whether it fired |
| `process.definition.unavailable` | Warning | (also for a schedule whose pinned version the catalog cannot supply) |
| `process.window.zone_unknown` | Warning | A delivery window names a zone the host cannot resolve; `WindowFailed` is queued |
| `process.activation.saved`, `process.activation.removed` | Information | An activation was written (with whether it changed anything) or removed |
| `process.worker.started`, `process.worker.pass_failed` | Information, Error | The worker loop |

## Testing against PostgreSQL

The tests of the host run against a real database. Set `BRAIN_TEST_POSTGRES` to a connection string, or let the test run start a `postgres:16-alpine` container with Testcontainers; with neither the tests fail with a message saying so. Each test class uses its own schema and drops it at the end. Tests drive `IProcessPump.RunOnceAsync` with a `FakeTimeProvider`.

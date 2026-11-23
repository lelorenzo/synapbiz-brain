# Spec W1-C — triggers, schedules, delivery windows and `poll`

Status: draft for implementation, 2026-11-23. Design and owner decisions: [design.md](design.md) (decisions 7 and 8 apply). Builds on [spec.md](spec.md) (W1-A, the interpreter) and [spec-b-postgres-host.md](spec-b-postgres-host.md) (W1-B, the host).

The work is in two parts that ship in one pull request: **C-core** changes only `Synapbiz.Brain.Process` (sections 1 to 5), **C-host** changes only `Synapbiz.Brain.Host.Postgres` (sections 6 to 8). Nothing under `Contracts`, `Engine`, `Spec` or the two workbench libraries changes. The format stays `brain.process@1`: every addition is optional, so every W1-A document stays valid and keeps its content hash.

The pure library still never reads a clock, a time zone database, a file or the network. Wherever a time zone is needed, the library takes a `TimeZoneInfo` as a parameter and the host resolves the id.

## 1. Format additions (body)

| Member | Required | Meaning |
| --- | --- | --- |
| `triggers` | no | Array of trigger declarations (section 2). Absent or empty: the process is started only through the engine's `StartAsync` or as a child, as today. |
| `windows` | no | Object: window name to delivery window (section 3). |

Step additions:

| `kind` | New members |
| --- | --- |
| `action` | `contact?` (boolean, default false) and `window?` (the name of a declared window). `contact: true` requires `window` (`process.action.window_required`). `window` on a non-contact action is allowed. |
| `poll` | new kind, section 4 |

## 2. Triggers

```json
"triggers": [
  { "kind": "event", "event": "case.opened", "businessKey": "case_ref", "input": { "case_ref": "case_ref", "visit_date": "visit_date" } },
  { "kind": "schedule", "id": "daily", "cron": "0 6 * * *", "zone": "America/New_York", "misfire": "fireOnce",
    "input": { "run_for": "scheduledDate", "fired_at": "scheduledFor" } }
]
```

**Event trigger.** `event`: non-empty name, unique among the event triggers of the document. `businessKey`: the name of a payload member whose value (a JSON string, non-empty) becomes the business key of the instance. `input?`: mapping of an `input: true` variable to a payload member. The interpreter does nothing with it at run time; it exposes it (section 5) and validates it: every variable exists and is `input: true`; `process.trigger.event_duplicate`, `process.trigger.variable_unknown`, `process.trigger.variable_not_input`.

**Schedule trigger.** `id`: `[a-z][a-z0-9_]{0,31}`, unique among the schedule triggers. `cron`: section 2.1. `zone?`: an IANA id (checked for shape only: `^[A-Za-z_]+(/[A-Za-z0-9_+\-]+){1,2}$` or `UTC`); when absent the zone comes from the activation (section 6). `misfire`: `skip` or `fireOnce`, required. `input?`: mapping of an `input: true` variable to one of the two names `scheduledFor` (the variable must be `Instant`) or `scheduledDate` (the variable must be `Date`: the local calendar date of the fire time in the schedule's zone). Codes: `process.trigger.schedule_duplicate`, `process.trigger.cron_invalid`, `process.trigger.zone_invalid`, `process.trigger.input_source_unknown`, `process.variable.type_mismatch`.

### 2.1 Cron

Five fields separated by single spaces: minute (0 to 59), hour (0 to 23), day of month (1 to 31), month (1 to 12), day of week (0 to 7; 0 and 7 are Sunday). Each field is `*`, a number, a range `a-b` (a ≤ b), a step `*/n` or `a-b/n` (n ≥ 1), or a comma list of those. No names, no seconds, no `L`, `W`, `#`, `?`, no macros. When both day fields are restricted (neither is `*`), a day matches when **either** matches; otherwise both must match. An expression that can never fire (for example `0 0 31 2 *`) is invalid.

Public pure API in `Synapbiz.Brain.Process`:

```csharp
public sealed class CronSchedule
{
    public static bool TryParse(string text, [NotNullWhen(true)] out CronSchedule? schedule);
    public string Text { get; }
    /// The first occurrence strictly after `after`, as a UTC instant, evaluated on the wall clock of `zone`.
    public DateTimeOffset Next(DateTimeOffset after, TimeZoneInfo zone);
}
```

Daylight saving rules, normative: a local time that does not exist on a day (the gap) fires at the first valid instant after the gap, once; a local time that occurs twice fires at its first occurrence only. `Next` never returns the same instant for two different local occurrences and is strictly increasing when iterated. The search looks at most 8 years ahead (enough for 29 February); the parser's "can never fire" check uses the same bound in UTC.

## 3. Delivery windows

```json
"windows": {
  "contact_hours": { "zone": { "variable": "practice_zone" }, "days": ["mon", "tue", "wed", "thu", "fri"], "from": "09:00", "to": "18:00" }
}
```

`zone`: either an IANA id as a string (same shape check) or `{ "variable": "<Code variable>" }`. `days`: non-empty, unique, from `mon tue wed thu fri sat sun`. `from`, `to`: `HH:mm`, `from` < `to` (a window does not cross midnight; `to` may be `24:00`). The window is open on a listed local day from `from` inclusive to `to` exclusive. Codes: `process.window.invalid`, `process.window.unknown` (a step names an undeclared window), `process.window.unused` is **not** an error.

**Semantics.** Before every attempt of an `action` step that names a window (the first attempt and every retry), the interpreter does not request the effect. It emits

```csharp
public sealed record WindowRequest(string Step, string TimerId, string Zone, IReadOnlyList<DayOfWeek> Days, TimeOnly From, TimeOnly To /* 00:00 means 24:00 when From is later */) : ProcessRequest(Step);
```

with the timer id `{InstanceId}/{step}/{Entry}/{Attempt}/window`, records a `WindowWaiting` history entry and waits (pending kind `Window`). The host answers with `TimerFired(TimerId)` at an instant inside the window (at once when the window is open now); the interpreter then records `WindowOpened` and requests the effect exactly as today. A retry delay runs first, then the window gate. If the zone variable has no value the instance suspends with `process.window.zone_missing`. If the host cannot resolve the zone it delivers the new trigger

```csharp
public sealed record WindowFailed(string TimerId, string Code) : ProcessTrigger;   // code process.window.zone_unknown
```

and the step goes to `onError`, or the instance suspends with that code. Cancelling or suspending the instance cancels a pending window with `CancelTimerRequest`.

Pure helper used by the host and by the scenario runner:

```csharp
public static class DeliveryWindows
{
    /// `now` when the window is open at `now`, else the next opening instant (UTC). Gap and repeated-hour rules as for cron:
    /// an opening time inside a gap opens at the end of the gap; the window is evaluated on the local wall clock.
    public static DateTimeOffset NextOpen(DateTimeOffset now, TimeZoneInfo zone, IReadOnlyList<DayOfWeek> days, TimeOnly from, TimeOnly to);
}
```

## 4. `poll`

```json
"note_signed": { "kind": "poll", "port": "ehr.visit_note", "input": { "case": "case_ref" }, "output": { "status": "note_status" },
  "rule": "visit.note_ready", "facts": { "status": "note_status" }, "field": "ready", "until": ["yes"],
  "every": "PT15M", "backoff": "exponential", "max": "PT4H", "maxDuration": "P7D",
  "next": "coding", "onExpired": "chase_note", "onError": "chase_note" }
```

Members: `port`, `input?`, `output?`, `retry?`, `onError?`, `onNotAvailable?`, `onNotEntitled?` as for `read`; `rule` and `facts` as for `decide` (hit policy `unique` or `first`); `field`: an outcome field of type `Enum` or `Boolean`; `until`: non-empty list of values of that field that end the poll (checked against the values the rule's rows can produce, as `decide` routes are; a `Boolean` as `"true"`/`"false"`); `every`: duration; `backoff?`: `fixed` (default) or `exponential` (the interval doubles each round); `max?`: the largest interval, only with `exponential`; `maxDuration`: duration, required; `next`; `onExpired`. Load-time bound: the number of rounds that fit in `maxDuration` must be at most 10000 (`process.poll.too_many_rounds`).

Semantics. Entering the step records the start instant and begins round 1. A round requests the read (effect id `{InstanceId}/{step}/{Entry}/r{Round}/{Attempt}`; retries inside a round as for `read`), assigns `output`, evaluates the rule and records a `Decided` entry with the usual provenance. If the field's value is in `until`, the step goes to `next`. If the rule does not decide, or the value is not in `until`: when `now + interval` is later than `start + maxDuration` the step records `PollExpired` and goes to `onExpired`; otherwise it requests a timer (`{InstanceId}/{step}/{Entry}/r{Round}/poll`) for `now + interval` and, when it fires, begins the next round. Port failures after retries follow `onError` and the other error routes, or suspend, exactly as `read`; a rule error follows `onError` or suspends. Rounds do not count against `limits.maxSteps` (the step was entered once). A `poll` step is not a `loop` for the cycle rule and needs none.

## 5. Public surface of the interpreter (C-core)

- `BrainProcess.Triggers`: `IReadOnlyList<ProcessTriggerDeclaration>` with `EventTriggerDeclaration(Event, BusinessKeyMember, Input)` and `ScheduleTriggerDeclaration(Id, Cron, Zone, Misfire, Input)`; `ProcessMisfire { Skip = 1, FireOnce = 2 }`.
- `CronSchedule`, `DeliveryWindows`, `WindowRequest`, `WindowFailed`, `ProcessPendingKind.Window`, history kinds `WindowWaiting`, `WindowOpened`, `PollExpired` appended to the enum (existing numeric values unchanged).
- State JSON: new members only where a new pending kind or a poll in progress needs them; a state written by W1-A/W1-B code must still load.
- JSON schema `brain.process.v1.json` extended; the reference page `docs/reference/brain-process-v1.md` updated.
- Scenarios: optional `zones`, an object of IANA id to a fixed UTC offset (`"-05:00"`), used to answer `WindowRequest` with `DeliveryWindows.NextOpen` at the virtual clock (an id not listed answers `WindowFailed`). `advance` fires window timers like other timers. `complete`/`fail` on a `poll` step answer the outstanding read of the current round. Triggers are not executed by scenarios.

Tests (C-core): cron parser table (valid, invalid, never fires); `Next` across both transitions of a zone with daylight saving and one without, built with `TimeZoneInfo.CreateCustomTimeZone` so no system database is needed, plus iteration is strictly increasing; windows: open now, before opening, after closing, weekend skip, `24:00`, opening inside a gap, zone missing, `WindowFailed` with and without `onError`, retry passes the gate again, cancel while waiting; poll: ends on first round, ends on a later round, expires, exponential with `max`, port failure routes, rule error, state round-trip mid-poll, ids; validator codes above; every existing fixture unchanged and green; line coverage of the library not lower than before.

## 6. Activation (C-host)

A definition's triggers do nothing until the consumer activates the process for a tenant:

```csharp
public interface IProcessEngine   // additions
{
    Task<ActivationResult> ActivateAsync(ProcessActivation activation, CancellationToken cancellationToken = default);
    Task<bool> DeactivateAsync(string tenantId, string processId, CancellationToken cancellationToken = default);
}
public sealed record ProcessActivation(string TenantId, string ProcessId, string? Zone = null, string InputJson = "{}");
public sealed record ActivationResult(long ProcessVersion, bool Changed);
```

`ActivateAsync` takes `IProcessCatalog.Latest(processId)`, validates `InputJson` against the definition's `input: true` variables, resolves every zone it needs (the trigger's own `zone`, else `Zone`; a schedule with neither, or an id the host's `TimeZoneInfo.FindSystemTimeZoneById` does not know, is an `ArgumentException` naming the schedule id) and writes, in one transaction, one activation per (tenant, process id) with the pinned version and hash, the protected input, its event trigger rows and its schedule rows with `next_fire_at = cron.Next(now, zone)`. It is idempotent: the same activation again changes nothing (`Changed` false) and keeps `next_fire_at`; a new version, zone or input replaces the rows, and a schedule whose cron and zone did not change keeps its `next_fire_at`. `DeactivateAsync` removes them; running instances are not touched.

Tables (migration `0002_triggers.sql`, `tenant_id` first in every key): `process_activation`, `process_event_trigger` (tenant, event, process id), `process_schedule` (tenant, process id, schedule id, cron, zone, misfire, next_fire_at).

## 7. Starting instances from triggers (C-host)

**Events.** `PublishEventAsync`, after its existing routing and inside the same event-id de-duplication, also looks up the event triggers of the tenant for that name. For each one it reads the business key member from the payload (missing or not a non-empty string: no start for that trigger, logged with ids only) and queues a start exactly as `StartAsync` does, with the input built from the activation's input overlaid by the mapped payload members. An existing instance with that business key is left alone. `PublishResult.Instances` counts started instances too, and the status is `Queued` when at least one instance was targeted or started.

**Schedules.** New first step of the pump pass, before expired leases: due schedule rows (`next_fire_at <= now`), each handled in one transaction that locks the schedule row (`FOR UPDATE SKIP LOCKED`), re-reads it, and:

- computes the occurrences `o1 < o2 < ... < ok <= now` starting at `next_fire_at` (k ≥ 1). An occurrence is *on time* when `now - o` is at most `ProcessHostOptions.ScheduleMisfireGrace` (default 5 minutes);
- when `k = 1` and `o1` is on time: fires `o1`;
- otherwise the schedule misfired (the host was down or late). `skip` drops every occurrence that is not on time and fires `ok` only if it is on time; `fireOnce` drops `o1 .. ok-1` and fires `ok` whether or not it is on time;
- sets `next_fire_at` to the first occurrence after `now`.

Firing queues a start with business key `{scheduleId}/{occurrence as yyyyMMddTHHmmZ}` and the input from the activation overlaid by the `scheduledFor` / `scheduledDate` mappings, so two pumps can never start two instances for one occurrence. A definition the catalog cannot supply leaves the row untouched (same treatment as the other work kinds, including item isolation and paging of spec B section 11).

**Windows.** A `WindowRequest` is stored as a timer row whose `due_at` is `DeliveryWindows.NextOpen(now, zone, ...)`; when the zone id cannot be resolved the host queues `WindowFailed` for the instance instead. Firing re-checks nothing: the due time was inside the window by construction and timer precision is the pump tick.

Zone resolution lives behind one internal seam in the host so tests can inject zones without the system database.

## 8. Tests and gates (C-host)

Integration tests on PostgreSQL in the style of W1-B, with a fake clock: activation idempotency and replacement; a schedule fires once per occurrence with 4 pumps in parallel; `skip` and `fireOnce` after the clock jumps three occurrences; the grace boundary; both daylight saving transitions (injected zone); `scheduledFor` and `scheduledDate` reach the variables; deactivation stops firing; an event trigger starts one instance per business key, twice-published and concurrently published; payload without the key member; tenant isolation of activations, schedules and event triggers; protected activation input; a contact action outside its window is sent at the opening and never before, across a retry too; unknown zone suspends or routes; a `poll` flow end to end against the pure interpreter's oracle; logging hygiene. Architecture guards unchanged except that the host still references only what spec B allows.

Gates as spec B section 9: Release build with the 2 base warnings only, every test green and none skipped, host line coverage ≥ 85 %, interpreter coverage not lower than the base, seven packages, the two concurrency tests of W1-B and the new schedule concurrency test 20 times in a row each.

Docs: reference pages of the format and of the host, `docs/architecture/overview.md`, CHANGELOG under Unreleased, `plan.md` (W1-B merged, W1-C in progress), a `tasks-c.md`.

## 9. Out of scope

Calendars of holidays, windows that cross midnight, cron seconds or names, per-instance schedule changes, catch-up of every missed occurrence, moving running instances to a new version, HTTP endpoints, metrics, the four reference flows and the version bump (W1-D).

## 10. Amendments after independent verification (2026-11-23)

Normative; where they differ from the sections above they win.

1. **Business key from an event.** The key member must be a JSON string that, after trimming, is non-empty, is at most 200 characters, contains no control character and no unpaired surrogate, and does not start with `@`. Anything else means no start for that trigger (`process.trigger.no_business_key`, ids only in the log). Reading the key never throws, and a bad key never prevents the event from being recorded and routed to waiting instances.
2. **Business key of a schedule start.** `@schedule/{scheduleId}/{occurrence as yyyyMMddTHHmmZ}`. The `@` prefix is reserved for the host, so a key taken from a payload cannot collide with it.
3. **Zone ids.** The activation's `Zone` and the value of a window's zone variable must have the shape of section 2 (`UTC` or `Area/Location`); any other text is rejected (`ArgumentException` at activation; `WindowFailed` with `process.window.zone_unknown` for a window) without asking the system database.
4. **Loader totality.** No input text makes `BrainProcessLoader.Load` throw; a number the schema evaluation cannot represent is a schema violation at its path.
5. **Window as an interval.** On each listed local day the window is the interval from its opening instant (inclusive) to its closing instant (exclusive). The instant of a local time that occurs twice is its first occurrence; the instant of a local time inside a gap is the end of the gap; a window whose two instants coincide is empty that day. `DeliveryWindows.NextOpen` returns `now` when `now` is inside such an interval and otherwise the earliest opening instant after `now`. So a window that spans a repeated hour stays open through both passes, and one that closes inside it closes on the first pass.
6. **Documented, unchanged behaviour.** A cron day field counts as restricted unless its text is exactly `*`. In the repeated hour of a daylight saving change a cron expression fires on the first pass only, so an every-minute or hourly schedule is silent for that one hour. On a day a zone skips entirely (a date-line change) nothing fires for that day.
7. **Exposed collections.** `BrainProcess.Triggers`, their `Input` maps and `WindowRequest.Days` are read-only copies; a caller cannot change the definition through them.

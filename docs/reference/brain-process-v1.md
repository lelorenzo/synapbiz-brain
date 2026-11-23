# `brain.process@1` and the process interpreter

How to write a process document for the Brain process library and how a host runs it. The library is `Synapbiz.Brain.Process`; design in [the W1 design](../specs/w1-process-engine/design.md) and [the W1-A spec](../specs/w1-process-engine/spec.md). Every example here is synthetic.

A process is a graph of steps over a set of typed variables. The library is **pure**: it never reads a clock, never generates an identifier, never does I/O and never calls a port. It takes the state of an instance, one trigger (a result, a timer, an event, ...) and the current time, and returns the new state, the requests the host must carry out (call a port, start a timer, create a task, ...) and the history entries to store. Persisting the state, calling ports and delivering triggers is the host's job.

Rules are not part of a process document. A `decide` step names a rule of the [rule library](brain-rule-v1.md); the rule is given to the loader in the load context.

## The envelope

All members are required and no other member is allowed.

| Member | Meaning |
| --- | --- |
| `$spec` | `brain.process@1` |
| `id` | Stable identifier: letters, digits, `_`, `.`, `-`, not starting with `.` or `-`. |
| `version` | Integer from 1 to 9007199254740991. Changing a process means a new version. |
| `uses` | Array of unique `{"$ref": "<kind>/<id>"}`. It must hold `rule/<id>` for every rule a `decide` step names and `process/<id>` for every process a `subprocess` step names, and nothing else. |
| `requires` | Array of unique `<module>@<n>` contract addresses. |
| `meta` | Free object (title, labels). The interpreter does not read it and no member of it is an error; `BrainProcess.Meta` returns a deep copy. The convention `meta.display` holds the labels people read, see [Display labels](#display-labels). |
| `body` | The process, below. |

## The body

| Member | Required | Meaning |
| --- | --- | --- |
| `subject` | yes | Non-empty string: the case type. Opaque to Brain. |
| `stages` | yes | Unique non-empty strings, for example `["previsit", "visit"]`. May be empty. A step may name one with `stage`. |
| `variables` | yes | Object: variable name to declaration (below). |
| `accepts` | no | Unique event names that may be buffered while no step waits for them. |
| `limits` | no | `{ "maxSteps": 1..1000000 }`. Default 10000. |
| `start` | yes | Id of the first step. |
| `steps` | yes | Object: step id to step. At least one. Every step must be reachable from `start`. |
| `triggers` | no | What starts an instance: event and schedule declarations, see [Triggers, cron and delivery windows](#triggers-cron-and-delivery-windows). The interpreter only validates and exposes them. |
| `windows` | no | Delivery windows by name, see [Triggers, cron and delivery windows](#triggers-cron-and-delivery-windows). |
| `scenarios` | no | Executable examples, see [Scenarios](#scenarios). |

## Variables

A declaration is `{ "type": ..., "allowedValues": [...]?, "input": true?, "initial": ...? }`. `input: true` marks a variable that `Start` may receive. `allowedValues` is for `Enum` only and is required there. `null` or absence means "no value" for every type.

| `type` | JSON form of a value |
| --- | --- |
| `Code` | string |
| `Enum` | string, one of `allowedValues` |
| `Boolean` | `true` or `false` |
| `Integer` | JSON integer within the safe-integer range, both bounds inclusive (-9007199254740991 to 9007199254740991; one beyond either bound is a mismatch) |
| `Decimal` | string such as `"12.50"` (`-?(0\|[1-9][0-9]*)(\.[0-9]{1,28})?`) |
| `Money` | `{ "amount": "<Decimal string>", "currency": "USD" }` (three upper-case letters) |
| `Date` | string `YYYY-MM-DD`, a real calendar date |
| `Reference` | string GUID in the `D` format; stored lower-case, so correlation compares equal whatever the case of the input |
| `Instant` | ISO 8601 date-time with `Z` or an offset; stored as UTC `yyyy-MM-ddTHH:mm:ssZ`, fractions truncated |
| `Json` | any JSON value, opaque |

A JSON document with a duplicate member name, at any depth, is invalid wherever Brain reads one (a process, `ProcessState.FromJson`, an effect output, task data, an event payload, a child output, an input). A value that does not fit its type is `process.variable.type_mismatch`. Values are stored in the state as canonical JSON text (object members in ordinal order).

## Steps

Every step has a `kind` and may have a `stage`. "Mapping" means an object of strings to strings. A step reference is the id of a step.

| `kind` | Members |
| --- | --- |
| `read` | `port`; `input?` (port parameter to variable); `output?` (result member to variable); `retry?`; `next`; `onError?`, `onNotAvailable?`, `onNotEntitled?` |
| `action` | as `read`, plus `delivery` (`atLeastOnce` or `atMostOnce`) and `onUnknown`, required with `atMostOnce` and forbidden otherwise; `contact?` (boolean, `true` needs a `window`), `window?` (the name of a delivery window) and `interruptBy?` (an event that ends the wait for the window, needs a `window`; see [An interruptible delivery window](#an-interruptible-delivery-window)) |
| `poll` | `port`, `input?`, `output?`, `retry?`, `onError?`, `onNotAvailable?`, `onNotEntitled?` as for `read`; `rule`, `facts` as for `decide` (hit policy `unique` or `first`); `field` (an `Enum` or `Boolean` outcome of the rule), `until` (non-empty, unique values of that field that end the poll); `every` (duration); `backoff?` (`fixed`, the default, or `exponential`); `max?` (the largest interval, only with `exponential`); `maxDuration` (duration); `next`; `onExpired` |
| `ai` | `port`, `model`, `prompt` `{ "id", "version" }`, `input?`, `inputSchema`, `outputSchema`, `output?`, `maxAttempts?` (1 to 5, default 1), `retry?`, `next` (a `decide` step), `onInvalid`, `onError?`, `onNotAvailable?`, `onNotEntitled?` |
| `decide` | `rule`, `facts` (fact name to variable); either `output?` and one of `next` or `route` with `on` (rule of hit policy `unique` or `first`), or `collectInto` and `next` (hit policy `collect`); `onNoDecision`; `onError?` |
| `humanTask` | `queue`, `outcomes`, `show?` (variables to show), `output?` (data member to variable), `on` (outcome to step), `timeout?` (a duration, or `{ "until", "offset?" }` for a [timeout tied to an instant](#timeouts-tied-to-an-instant)), `onTimeout` (required with `timeout`, forbidden without) |
| `wait` | `event`, `correlation?` (a `Code` or `Reference` variable), `output?` (payload member to variable), `next`, `timeout?`, `onTimeout` (as for `humanTask`) |
| `timer` | exactly one of `after` (duration) or `until` (an `Instant` variable); `offset?` (signed duration, only with `until`); `next` |
| `loop` | `maxIterations` (1 to 10000), `body`, `onExhausted`; optionally `forEach` (a `Json` variable holding an array), `item` (a `Json` variable) and `next`, which come together or not at all |
| `subprocess` | `process`, `input?`, `output?` (child output member to variable), one of `next` or `on` (child result to step), `onError?` |
| `end` | `result` |

A **duration** is an ISO 8601 `P...D`/`T...H...M...S` text with at least one component, greater than zero and at most 3660 days (`PT30S`, `PT24H`, `P3D`). A signed duration (the `offset`) may start with `-`.

`retry` is `{ "maxAttempts": 1..10, "backoff": "fixed" | "exponential", "initial": <duration>, "max": <duration>? }`. Without it a step has one attempt.

### Effects: `read`, `action`, `ai`

The step asks the host to call a port. The request carries the input object built from `input`, an idempotency key and the attempt number; the host answers with `EffectCompleted` (an output object) or `EffectFailed` (a kind and a code).

- On success each `output` entry is assigned to its variable; a missing or null member clears the variable. A member that does not fit the variable's type, or an output that is not an object, counts as a permanent failure (`process.variable.type_mismatch`, `process.effect.output_malformed`).
- `Transient` and `RateLimited` failures are retried while `attempt < retry.maxAttempts`: the delay is `initial` (fixed) or `initial x 2^(attempt-1)` (exponential), capped by `max`; for `RateLimited` a `RetryAfter` larger than that wins. The host starts the retry timer the library asks for and delivers `TimerFired` when it is due.
- `NotAvailable` goes to `onNotAvailable`, `NotEntitled` to `onNotEntitled`, `Unknown` to `onUnknown` (an `Unknown` outcome is never retried: the host cannot tell whether the effect happened).
- Everything else (retries exhausted, `Permanent`, a kind without its own route) goes to `onError`; without `onError` the instance is **suspended** with the failure's code.

An `action` with `atMostOnce` delivery tells the host never to repeat the call; use it for effects that must not be duplicated, and route `Unknown` to a person.

`inputSchema` and `outputSchema` must be self-contained: Brain builds each one and evaluates it once against `{}` at load time; a schema that cannot be built, uses an unknown keyword, or has a `$ref` that does not resolve inside the same schema object is `process.ai.schema_invalid` at the schema's path. An `ai` step validates its input object against `inputSchema` before asking (a failure is `process.ai.input_invalid`) and the answer against `outputSchema`. An invalid answer is retried at once, up to `maxAttempts`, then goes to `onInvalid`. The history entry of a valid answer records the model, the prompt id and version, a hash of the input and the output. `next` must be a `decide` step: an AI output never moves a case by itself, a rule decides.

### Delivery windows on an `action`

An `action` that names a `window` does not request the effect straight away. Before every attempt (the first and each retry) the interpreter emits a `WindowRequest` and waits; when the host answers with `TimerFired` for the window timer, the effect is requested exactly as for an action without a window. See [Triggers, cron and delivery windows](#triggers-cron-and-delivery-windows). `contact: true` marks an action that reaches a person and requires a `window` (`process.action.window_required`); the library gives it no other meaning. A non-contact action may name a window too.

### `poll`

A `poll` reads a port, evaluates a rule on the result and repeats until the rule's `field` takes one of the `until` values or the time is up.

- Entering the step records the start instant and begins round 1. A round requests the read (effect id `I/S/E/rR/A`, see [Identifiers](#identifiers); retries inside a round work as for `read`), assigns `output`, evaluates the rule and records a `Decided` entry with the usual provenance.
- When the field's value is in `until` the step goes to `next`. When the rule decides nothing, or the value is not in `until`, the wait after round `R` is `every` (`fixed`) or `every x 2^(R-1)` (`exponential`), never above `max`. If `now` plus that wait is later than the start plus `maxDuration`, the step records `PollExpired` and goes to `onExpired`; otherwise it requests the timer `I/S/E/rR/poll` for that instant and, when it fires, begins round `R+1`.
- A port failure after its retries follows `onError` and the other error routes, or suspends, exactly as for `read`; a rule error follows `onError` or suspends. A suspended poll keeps no progress: `ResumeRequested` enters the step again with a fresh entry number, so the poll starts over with round 1 and a new start instant. While it runs, the round and the start instant are the `poll` member of the state.
- The number of rounds that fit in `maxDuration` must be at most 10000 (`process.poll.too_many_rounds`). Rounds do not count against `limits.maxSteps`: the step is entered once. A `poll` is not a `loop` for the cycle rule and needs no bound of its own; a route back to it from its own `onExpired` path is an unbounded cycle like any other.

### `decide`

The facts are built from the mapped variables (a variable without a value leaves the fact out) and the rule is evaluated for the clock's `Today`. The `Decided` history entry carries the provenance: rule id and version, whether it decided, the matched row keys, the outcome, the facts and a hash of the facts.

- No decision goes to `onNoDecision`. A rule error is recorded in a `Decided` entry (code = the error code, data `{"rule":..,"version":..,"error":..}`) and goes to `onError`, or suspends with that code.
- Outcome values are assigned to variables of the same type. A `Decimal` outcome field (a JSON number in the rule library) becomes the number's raw text as the variable's string, and a `Money` outcome `{amount: number, currency}` becomes `{amount: "<raw text>", currency}`; the number is never converted to a binary floating type. A number that does not match the `Decimal` form (exponent notation, for example) is a `process.variable.type_mismatch`, routed to `onError` or suspension.
- With `unique` or `first` rules, `output` assigns outcome fields to variables, then the step goes to `next` or follows `route`: the value of that outcome field (a `Boolean` as `"true"` or `"false"`) selects the entry of `on`. A value without an entry goes to `onError`, or suspends with `process.route.unmatched`.
- With a `collect` rule the whole outcome array is stored in the `Json` variable `collectInto`.

### `humanTask`

Asks the host to create a task in `queue` with the listed `outcomes` and the values of the `show` variables. `TaskCompleted` names one outcome and carries a data object that is assigned through `output`; the step goes to `on[outcome]`. An unknown outcome, or data that does not fit, is **rejected** and leaves the task open. With `timeout` the host also receives a timer; when it fires the task is cancelled and the step goes to `onTimeout`. The timeout may be tied to an instant instead of a length of time, see [Timeouts tied to an instant](#timeouts-tied-to-an-instant).

### `timer`

`after` starts a timer at now plus the duration; `until` uses the instant in a variable plus the optional `offset` (a missing instant suspends with `process.timer.until_missing`). The timer is always requested, even when it is already due.

### `wait` and events

The step waits for an event of the given name; with `correlation` only an event with the same correlation value matches. If the oldest matching event is already in the inbox (see below) it is consumed on entering and nothing is requested. `timeout` and `onTimeout` work as for tasks, in both forms (a duration or an instant).

An `EventReceived` trigger is handled in this order: a finished instance ignores it; the step that waits for it consumes it; otherwise, if the name is in `accepts`, it is appended to the inbox (even while the instance is suspended); otherwise it is **rejected** with `process.event.not_accepted`.

### Timeouts tied to an instant

`timeout` of a `wait` and of a `humanTask` is a duration text, as above, or an object that names the instant the step must be left by:

```json
"timeout": { "until": "visit_at", "offset": "-PT2H" }
```

`until` names a declared variable of type `Instant` (another type is `process.variable.type`, an undeclared name is `process.variable.unknown`, both at `/timeout/until`). `offset` is optional and is a signed duration with the rules of the `offset` of a `timer` step (a bad one is `process.duration.invalid` at `/timeout/offset`). `onTimeout` is required with the object form and forbidden without a `timeout`, as for a duration. An object with another member, without `until`, or any other shape is `spec.structure.schema` at the offending value.

On entering the step, in this order:

1. A `wait` first applies the inbox rule: a matching buffered event is consumed and the step leaves through `next`; no timeout is computed (so the variable need not have a value).
2. The due instant is the value of the variable plus the offset. A variable with no value suspends the instance with `process.timeout.instant_missing` (the problem detail and the history data hold the variable name); `ResumeRequested` enters the step again.
3. A due instant at or before the clock: the step requests no wait and no task, records `TimedOut` (data `{"dueAt": ...}`) and goes to `onTimeout` at once. This differs from a `timer` step, which always requests its timer even when it is already due: a timeout is a deadline, and a deadline that has passed is a timeout.
4. Otherwise the step requests its wait or task and a `TimerRequest` with the usual timeout timer id (`I/S/E/timeout`) due at that instant. The event or the task winning (the timer is cancelled) and the timer winning (the wait or task is cancelled) are as for a duration.

The due instant is fixed when the step is entered: a later change of the variable does not move a pending timeout, and the state keeps only the timer id. A flow that wants a new deadline enters the step again. A scenario needs no new member: `advance` fires timers by their due time.

### An interruptible delivery window

An `action` that names a `window` may also name an event that ends the wait for the window:

```json
"interruptBy": { "event": "case.reply_received", "correlation": "case_ref", "output": { "reply_kind": "kind" }, "to": "reply_arrived" }
```

`event`, `correlation?` and `output?` follow the rules of a `wait` step (the same validation and codes: `correlation` is a `Code` or `Reference` variable, `output` maps payload members to variables); `to` is a step id (`process.step.unknown` otherwise, and the edge counts for reachability and for the cycle rule). `interruptBy` without `window` is `process.action.interrupt_requires_window`, at `/interruptBy`.

At every gate of the action (before the first attempt and before each retry, where a `WindowRequest` is emitted for an action without `interruptBy`):

1. If a matching event is already in the inbox it is consumed: no `WindowRequest` and no `WaitRequest` are emitted, the history records `EventConsumed` and `WindowInterrupted`, `output` is assigned and the step goes to `to`. The check comes before the zone is read, so a buffered event needs no zone; the correlation is read first, as for a `wait` (`process.wait.correlation_missing`, `process.wait.correlation_invalid`).
2. Otherwise the interpreter emits the `WindowRequest` and then a `WaitRequest` (wait id `I/S/E/A/wait`, one per attempt) for the event, and records `WindowWaiting` and `WaitStarted`. The pending kind stays `Window`; its state member gains `interruptWaitId`, written only in this case.
3. A matching `EventReceived` while the gate is pending: the interpreter emits `CancelTimerRequest` for the window timer, records `EventConsumed` and `WindowInterrupted`, assigns `output` and goes to `to`; the effect is not requested. The wait row is consumed by the event itself.
4. `TimerFired` for the window: the interpreter emits `CancelWaitRequest`, records `WindowOpened` and requests the effect as before. From then on the event does not interrupt: an event that arrives while the effect is outstanding, or during a retry delay, follows the inbox rule (buffered when the name is in `accepts`, otherwise rejected). A retry delay runs before the gate and is not interruptible; only the gate is.
5. `WindowFailed`, cancelling and suspending cancel the wait together with the window timer.
6. An event whose payload is not an object, or does not fit `output`, follows the rule of an awaited event with a bad payload: the instance is suspended with `process.variable.type_mismatch` (the event is consumed, as for a `wait`) and both requests are cancelled; a resume enters the gate again.

Whichever of the event and the window timer reaches the instance first wins; the other finds nothing outstanding and is ignored (`Ignored`, `process.trigger.stale`) or, for the event, buffered. The `interruptBy` event does not have to be in `accepts` to interrupt, but an event of that name that arrives when it cannot interrupt is rejected unless it is.

### Display labels

`meta` stays a free object. By convention the labels people read live in `meta.display`:

```json
"meta": {
  "title": "Pre-visit outreach",
  "display": {
    "en": { "title": "Pre-visit outreach", "stages": { "previsit": "Before the visit" }, "steps": { "send": "Send reminder" }, "results": { "ready": "Ready" }, "queues": { "front_desk": "Front desk" }, "outcomes": { "approved": "Approved" } },
    "es": { "title": "Contacto previo a la visita", "steps": { "send": "Enviar recordatorio" } }
  }
}
```

A language key is a lower-case two-letter code. Each language object has an optional `title` and any of the groups `stages`, `steps`, `results`, `queues`, `outcomes`, `events` and `windows`, each an object of id to a non-empty text of at most 120 characters. The ids are the stage names, step ids, the `result` of an `end` step, the `queue` of a `humanTask`, its `outcomes`, the event names a `wait`, an `interruptBy` or an event trigger names, and the names under `windows`.

`ProcessDisplay.From(process)` returns a reader with `Title(language)`, `Stage(language, id)`, `Step(...)`, `Result(...)`, `Queue(...)`, `Outcome(...)`, `Event(...)` and `Window(...)`. Each answers in the language asked for, then in `en`, then with `null` (the caller shows its own text or the id). The language is compared without regard to case and a region suffix is dropped (`ES` and `es-MX` ask for `es`). The reader never throws on a `meta` of any shape: a member that does not have the shape of the convention is ignored.

The diagnostics are warnings, never errors, and never prevent loading. `BrainSpecError` has no severity and the loader reports errors only, so the warnings come from `ProcessDisplay.Validate(process)` (the loader is unchanged), ordered by path then code:

| Code | Condition |
| --- | --- |
| `process.display.invalid` | a `display` member of the wrong shape, at its path: `display` not an object, a language key that is not two lower-case letters or whose value is not an object, a member of a language other than `title` and the groups, a group that is not an object, a label that is not a text of 1 to 120 characters |
| `process.display.unknown_id` | a label for a stage, step, result, queue, outcome, event or window the document does not have, at the path of the label |

The six documents of [samples/process-reference](../../samples/process-reference/README.md) label every stage, step, result, queue, outcome, event and window they have, in `en` and `es`.

### `loop`

A bounded gate. Its `body` edge is the only edge that may close a cycle in the step graph, and it is taken at most `maxIterations` times; the `onExhausted` and `next` paths of a loop must not lead back to the loop or into any cycle. Each arrival counts: with `forEach` the item is the n-th element of the list and the step goes to `next` when the list is done; otherwise, after `maxIterations` arrivals it goes to `onExhausted`. The list check comes first. Counters never reset within an instance. The graph without the `body` edges of loop steps must be acyclic (a cycle is the load error `process.cycle.unbounded`, reported once per cycle at its first step), so an instance always enters a bounded number of steps; `limits.maxSteps` is a second guard.

### `subprocess`

Asks the host to start the process `process` as a child with the mapped input. `ChildCompleted` carries a result name and an output object; the step goes to `next` or `on[result]`. `ChildFailed`, an unlisted result or output that does not fit goes to `onError`, or suspends.

### `end`

Completes the instance with `result`.

## Triggers, cron and delivery windows

### Triggers

```json
"triggers": [
  { "kind": "event", "event": "case.opened", "businessKey": "case_ref", "input": { "case_ref": "case_ref" } },
  { "kind": "schedule", "id": "daily", "cron": "0 6 * * *", "zone": "America/New_York", "misfire": "fireOnce",
    "input": { "run_for": "scheduledDate", "fired_at": "scheduledFor" } }
]
```

The library never starts an instance by itself: a host reads `BrainProcess.Triggers` (an ordered list of `EventTriggerDeclaration` and `ScheduleTriggerDeclaration`, empty when the document declares none) and does the starting. The loader validates them. The collections it returns (`Triggers`, the `Input` map of each trigger) and `WindowRequest.Days` are read-only copies: a caller cannot change the definition through them.

- An **event trigger** has a non-empty `event` name, unique among the event triggers (`process.trigger.event_duplicate`), and `businessKey`, the name of the payload member whose string value becomes the business key. A host accepts that value only when, after trimming, it is not empty, has at most 200 characters, holds no control character and no unpaired surrogate, and does not start with `@`; anything else means no start for that trigger (a bad key never stops the event from being recorded and routed to waiting instances). `input?` maps an `input: true` variable to a payload member; the variable must exist (`process.trigger.variable_unknown`) and be an input (`process.trigger.variable_not_input`).
- A **schedule trigger** has an `id` (`[a-z][a-z0-9_]{0,31}`, unique, `process.trigger.schedule_duplicate`), a `cron` expression (`process.trigger.cron_invalid` when it does not parse or never fires), an optional `zone` (an IANA id, checked for shape only: `process.trigger.zone_invalid`; without it the zone comes from the activation in the host), a required `misfire` (`skip` or `fireOnce`) and an optional `input` that maps an `input: true` variable to `scheduledFor` (the variable must be `Instant`) or `scheduledDate` (the variable must be `Date`: the local calendar date of the fire time in the schedule's zone). Another name is `process.trigger.input_source_unknown`, a variable of the wrong type is `process.variable.type_mismatch`. A host starts an instance for a schedule with the business key `@schedule/{scheduleId}/{yyyyMMddTHHmmZ}` (the occurrence in UTC); the reserved `@` keeps it apart from any key taken from an event.

### Cron

`CronSchedule.TryParse(text, out schedule)` and `schedule.Next(after, zone)` are pure. A cron expression has five fields separated by single spaces: minute (0 to 59), hour (0 to 23), day of month (1 to 31), month (1 to 12) and day of week (0 to 7, where 0 and 7 are Sunday). A field is `*`, a number, a range `a-b`, a step `*/n` or `a-b/n` (n at least 1), or a comma list of those. There are no names, seconds, `L`, `W`, `#`, `?` or macros. A day field counts as restricted unless its text is exactly `*` (so `*/1` and `1-31` restrict it). When both day fields are restricted a day matches when either matches; otherwise both must match. An expression that can never fire (`0 0 31 2 *`) is invalid.

`Next` returns the first occurrence strictly after `after`, as a UTC instant, evaluated on the local wall clock of the given `TimeZoneInfo` (never by adding 24 hours). A local time that does not exist on a day (the gap of a spring transition) fires once, at the first valid instant after the gap; a local time that occurs twice (the fold of an autumn transition) fires at its first occurrence only, so in the repeated hour a cron expression fires on the first pass only and an every-minute or hourly schedule is silent for that one hour. On a day a zone skips entirely (a date-line change) nothing fires for that day. Iterating `Next` is strictly increasing. The search looks at most 8 years ahead.

### Delivery windows

```json
"windows": {
  "contact_hours": { "zone": { "variable": "practice_zone" }, "days": ["mon", "tue", "wed", "thu", "fri"], "from": "09:00", "to": "18:00" }
}
```

`zone` is an IANA id (shape check as for schedules: `UTC`, or `Area/Location` matching `^[A-Za-z_]+(/[A-Za-z0-9_+\-]+){1,2}$`) or `{ "variable": "<Code variable>" }`; `days` is a non-empty list of unique `mon` to `sun`; `from` and `to` are `HH:mm` with `from` earlier than `to` (a window does not cross midnight; `to` may be `24:00`). The window is open on a listed local day from `from` inclusive to `to` exclusive. Errors are `process.window.invalid` (at the offending member) and `process.window.unknown` (a step names an undeclared window); a window that no step uses is not an error.

At run time the interpreter emits `WindowRequest(Step, TimerId, Zone, Days, From, To)` (`To` is `00:00` for a window that closes at the end of the day), records `WindowWaiting` and waits with the pending kind `Window`. The host answers with `TimerFired(TimerId)` at an instant inside the window, at once when it is open now; the interpreter records `WindowOpened` and requests the effect. A retry delay runs first, then the gate again. If the zone variable has no value the instance suspends with `process.window.zone_missing`. If the host cannot resolve the zone, or the value of the zone variable does not have the shape above (`localtime`, `EST5EDT`, `Factory`, a path, an empty text are refused without the system time zone database being asked; `DeliveryWindows.IsZoneIdShape` is the check), it delivers `WindowFailed(TimerId, "process.window.zone_unknown")`: the step goes to `onError`, or the instance suspends with that code. Cancelling or suspending the instance cancels a pending window with `CancelTimerRequest`. An action may also name an event that ends the wait, see [An interruptible delivery window](#an-interruptible-delivery-window).

`DeliveryWindows.NextOpen(now, zone, days, from, to)` is the pure helper hosts and scenarios use. On each listed local day the window is the interval from its opening instant (inclusive) to its closing instant (exclusive). The instant of a local time that occurs twice is its first occurrence; the instant of a local time inside a gap is the end of the gap; a window whose two instants coincide (a gap swallows it) is empty that day. `NextOpen` returns `now` when `now` is inside such an interval, otherwise the earliest opening instant after `now` (as UTC). So a window that spans the repeated hour of an autumn change stays open through both passes, and one that closes inside it closes on the first pass. A window timer is not re-checked when it fires, and an `atLeastOnce` contact action whose lease expired may be requested again after the window closed: declare a contact action `atMostOnce` when a repeat outside the window is worse than a missed contact.

## Routing at a glance

| Situation | Goes to |
| --- | --- |
| a step finishes | `next`, or `on[value]` |
| effect failed and not retried | `onNotAvailable`, `onNotEntitled`, `onUnknown`, else `onError`, else suspension |
| ai answer invalid after all attempts | `onInvalid` |
| rule decided nothing | `onNoDecision` |
| rule error, route value without entry, child failure | `onError`, else suspension |
| task or wait timed out | `onTimeout` |
| loop used up | `onExhausted` |

Suspension is not an end: the instance keeps its step and the host (or an operator) sends `ResumeRequested` after fixing the cause.

## Loading and the static errors

```csharp
var rule = BrainRuleLoader.Load(ruleJson, [contract]).Rule!;
var loaded = BrainProcessLoader.Load(processJson, new ProcessLoadContext([rule], knownProcessIds));
if (!loaded.IsValid) { /* loaded.Errors: code, path, English and Spanish message, hint */ }
BrainProcess process = loaded.Process!;
```

`ProcessLoadContext.KnownProcessIds` is optional; when given, `subprocess` steps must name one of them. Errors are `BrainSpecError` values ordered by path then code. JSON, envelope and schema failures are reported alone (`spec.json.invalid`, `spec.structure.envelope`, `spec.structure.unknown_format`, `spec.structure.schema`); only a document that passes them is checked against the rules below, and all of those are reported together. `BrainProcessLoader.Load` never throws for any text: invalid JSON (including a string with an unpaired surrogate or a repeated member name) is `spec.json.invalid`, and a number that a schema evaluation cannot compare by value (`1e400`, thirty digits) where a list of unique items is expected is a `spec.structure.schema` violation at the path of that number. The same holds while an instance runs: an `ai` schema that meets such a number never makes the interpreter throw.

| Code | Condition |
| --- | --- |
| `process.step.unknown` | a step reference names no step |
| `process.step.unreachable` | a step cannot be reached from `start` |
| `process.stage.unknown` | `stage` is not listed in `stages` |
| `process.variable.unknown` | a mapping value or variable member names no declared variable |
| `process.variable.type` | a variable is used where its type is not allowed (collect, loop, `until` of a timer or of a `timeout`, `correlation` of a `wait` or an `interruptBy`; a fact or output mapping of a different type) |
| `process.variable.initial` | `initial` is not a valid value, or `allowedValues` is misused |
| `process.rule.unavailable` | the rule is not in the load context |
| `process.rule.fact_unknown` | a `facts` key is not a fact of the rule's kind |
| `process.rule.outcome_unknown` | an `output` key or the `route` is not an outcome field of the kind (also when the route field is not an `Enum`, `Boolean` or `Code`) |
| `process.rule.hit_policy` | members for a `collect` rule on a `unique` or `first` rule, or the reverse |
| `process.route.incomplete` | at `/on`: a `Boolean` route lacks `true` or `false`; an `Enum` or `Code` route lacks a value that some row or the default row of the rule produces for the route field; a `humanTask` `on` lacks one of its `outcomes` |
| `process.route.unknown_value` | at `/on/<key>`: a `Boolean` route has a key other than `true` and `false`; an `Enum` or `Code` route has a key that no row and no default row of the rule produces for the route field; a `humanTask` `on` has a key that is not one of its `outcomes` |
| `process.uses.missing` | a rule or process named by a step is not in `uses` |
| `process.uses.unused` | `uses` lists something no step names |
| `process.subprocess.unavailable` | the child process is not among the known process ids |
| `process.ai.next_not_decide` | the `next` of an `ai` step is not a `decide` step |
| `process.ai.schema_invalid` | `inputSchema` or `outputSchema` cannot be built as a JSON Schema |
| `process.cycle.unbounded` | the step graph without the `body` edges of `loop` steps has a cycle (a step that routes back to a step before it, or a loop whose `onExhausted` or `next` path leads back into a cycle) |
| `process.duration.invalid` | a duration that matches the pattern but is zero, longer than 3660 days or has no component |
| `process.action.window_required` | an `action` with `contact: true` names no `window` |
| `process.action.interrupt_requires_window` | an `action` has `interruptBy` and names no `window` |
| `process.window.invalid` | a window member is out of range, malformed or contradictory (zone shape, days, times, `from` not before `to`) |
| `process.window.unknown` | a step names a window the document does not declare |
| `process.trigger.event_duplicate` | two event triggers share one event name |
| `process.trigger.schedule_duplicate` | two schedule triggers share one id |
| `process.trigger.cron_invalid` | a cron expression does not parse or can never fire |
| `process.trigger.zone_invalid` | a schedule's zone is not shaped like an IANA id |
| `process.trigger.input_source_unknown` | a schedule `input` source is neither `scheduledFor` nor `scheduledDate` |
| `process.trigger.variable_unknown` | a trigger `input` names no declared variable |
| `process.trigger.variable_not_input` | a trigger `input` names a variable that is not `input: true` |
| `process.poll.too_many_rounds` | more than 10000 rounds fit in a poll's `maxDuration` |
| `process.scenario.invalid` | a scenario names an unknown step, variable or event, or is otherwise malformed |

The domain of a `decide` route is read from the rule: the values that its rows and its default row set for the route field. A row that leaves the route field empty produces no value; if such a row matches at run time the step goes to `onError`, or suspends with `process.route.unmatched`.

## Running an instance

```csharp
var clock = new ProcessClock(now, DateOnly.FromDateTime(now.UtcDateTime));
AdvanceResult started = process.Start("i-1", input, clock);
// store started.State.ToJson() and started.History, carry out started.Requests ...
ProcessState state = ProcessState.FromJson(storedJson);
AdvanceResult next = process.Advance(state, new EffectCompleted(effectId, outputJson), clock);
```

`Start` accepts only variables declared with `input: true`; anything else is `Rejected` (`process.input.unknown`, `process.variable.type_mismatch`) with a `Cancelled` state and nothing to store. `Advance` returns a disposition:

- `Applied`: store the new state, carry out the requests, store the history.
- `Ignored` (`process.trigger.stale`): the trigger matches nothing outstanding, for example a result delivered twice. State, requests and history are empty or unchanged. Delivering twice is harmless.
- `Rejected`: the trigger was understood but refused (`process.definition.mismatch`, `process.task.outcome_unknown`, `process.event.not_accepted`, `process.variable.type_mismatch`, and `process.limits.max_steps` for a resume at the step limit). Nothing changed.

### Triggers

| Trigger | When |
| --- | --- |
| `EffectCompleted(effectId, outputJson)` | a port call succeeded |
| `EffectFailed(effectId, kind, code, retryAfter?)` | a port call failed; kinds `Transient`, `RateLimited`, `NotAvailable`, `NotEntitled`, `Permanent`, `Unknown` |
| `TimerFired(timerId)` | a requested timer is due (timers of retry delays, `timer` steps, timeouts, poll rounds and delivery windows) |
| `WindowFailed(timerId, code)` | the host cannot resolve the zone of a pending delivery window (`process.window.zone_unknown`) |
| `EventReceived(name, correlation, payloadJson)` | an external event arrived (consumed by a `wait`, by the gate of an action with `interruptBy`, or buffered) |
| `TaskCompleted(taskId, outcome, dataJson)` | a person finished a task |
| `ChildCompleted(childKey, result, outputJson)` / `ChildFailed(childKey, code)` | the child process ended |
| `CancelRequested(reason)` | stop the instance: outstanding timers, waits and tasks are cancelled |
| `ResumeRequested` | run the current step again after a suspension; refused with `process.limits.max_steps` when the instance has already used its step limit |

### Requests

`EffectRequest` (step, effect id, idempotency key, kind `Read`/`Action`/`Ai`, port, input JSON, attempt, delivery, and for `ai` the model and prompt), `TimerRequest` (id, due time), `CancelTimerRequest`, `WaitRequest` (event, correlation), `CancelWaitRequest`, `TaskRequest` (queue, outcomes, JSON to show), `CancelTaskRequest`, `ChildStartRequest` (process, input JSON), `WindowRequest` (step, timer id, zone, days, from, to). An action with `interruptBy` emits its `WindowRequest` and a `WaitRequest` in one result.

### Identifiers

Identifiers are derived, never generated. With the instance id `I`, the step `S`, the step's entry number `E` (the running count of steps when it was entered) and the attempt `A`:

| What | Value |
| --- | --- |
| idempotency key | `I/S/E` |
| effect id | `I/S/E/A` |
| timer id | `I/S/E/timer`; retry delay `I/S/E/retry/A`; timeout `I/S/E/timeout` |
| wait id, task id, child key | `I/S/E/wait`, `I/S/E/task`, `I/S/E/child` |
| delivery window | `I/S/E/A/window`; the wait of its `interruptBy`, `I/S/E/A/wait` |
| `poll` round `R` | idempotency key `I/S/E/rR`; effect id `I/S/E/rR/A`; retry delay `I/S/E/rR/retry/A`; next round timer `I/S/E/rR/poll` |

A resume gives the step a fresh entry number, so the host never confuses a re-run with the earlier attempt.

### State and history

`ProcessState.ToJson()` is the serialized state (stable text: object keys in a fixed order, variables and counters in ordinal order) and `ProcessState.FromJson` reads it back (a `null` element of a `forEach` array clears `item`, and such a state round-trips); text that is not a state throws `DecisionEngineException` with the code `process.state.malformed`. The state holds the status (`Waiting`, `Suspended`, `Completed`, `Cancelled`), current step, stage, result, variables, loop counters, the outstanding work (`Pending`; its kind is one of `Effect`, `RetryDelay`, `Timer`, `Wait`, `Task`, `Child` and `Window`), the event inbox and, when suspended, the `Problem` (code, step, detail). A poll in progress adds the member `poll` (`{"round":R,"startedAt":"..."}`); it is written only then, so a state written without it still loads and a state of any other step has the same text as before. While an action with `interruptBy` waits for its window and its event, `pending` gains the member `interruptWaitId` (the wait id of the event), written only then: every other state, including a window gate without `interruptBy`, has the text it always had.

Every `DateTimeOffset` in a request or a history entry is UTC, whatever offset the host clock has. Suspending an instance cancels its outstanding work like `CancelRequested` does: `CancelWaitRequest`, `CancelTaskRequest` and `CancelTimerRequest` (timeouts and retry delays included). A payload that is malformed JSON or has a duplicate member name never throws: a malformed effect output is a permanent failure (`process.effect.output_malformed`); malformed task data, event payload or trigger input is rejected with `process.variable.type_mismatch` (an awaited event suspends the instance with that code); a malformed child output is a child failure.

Every history entry has a sequence number (continuing across advances), the time, the step, the step's entry number, the attempt, a kind (`Started`, `StepEntered`, `EffectRequested`, `EffectCompleted`, `EffectFailed`, `RetryScheduled`, `Decided`, `TaskRequested`, `TaskCompleted`, `WaitStarted`, `EventConsumed`, `EventBuffered`, `TimerStarted`, `TimerFired`, `TimedOut`, `LoopIteration`, `LoopExhausted`, `ChildStarted`, `ChildCompleted`, `ChildFailed`, `Suspended`, `Resumed`, `Cancelled`, `Completed`, `WindowWaiting`, `WindowOpened`, `PollExpired`, `WindowInterrupted`), an optional code and an optional JSON object. History never contains a value that the state or the trigger did not contain.

### Run-time codes

| Code | Meaning |
| --- | --- |
| `process.input.unknown` | `Start` received a variable that is not declared as an input |
| `process.variable.type_mismatch` | a value does not fit the variable's type |
| `process.definition.mismatch` | the state belongs to another process id, version or content hash |
| `process.trigger.stale` | the trigger matches no outstanding work |
| `process.event.not_accepted` | no step waits for the event and `accepts` does not list it |
| `process.task.outcome_unknown` | the task outcome is not one of the step's `outcomes` |
| `process.effect.output_malformed` | an effect answered with something that is not a JSON object |
| `process.ai.input_invalid` / `process.ai.output_invalid` | the ai input or answer does not satisfy its schema |
| `process.route.unmatched` | a route value or child result has no entry and the step has no `onError` |
| `process.timer.until_missing` | an `until` timer has no instant |
| `process.timeout.instant_missing` | the `Instant` variable of a `timeout` tied to an instant has no value when the step is entered; the detail is the variable name |
| `process.wait.correlation_missing` | a `wait` declares a correlation variable that has no value |
| `process.wait.correlation_invalid` | the correlation of a `wait` holds the character U+0000, which a store cannot keep as text and no event could match; the instance is suspended instead of waiting |
| `process.loop.not_a_list` | a `forEach` variable holds no array |
| `process.limits.max_steps` | the instance entered more steps than `limits.maxSteps`; it is suspended on the step before; a later `ResumeRequested` is rejected with the same code while `StepCount` is at or above the limit |
| `process.window.zone_missing` | the zone variable of a delivery window has no value; the instance is suspended |
| `process.window.zone_unknown` | the host could not resolve the zone of a delivery window (`WindowFailed`) |
| `process.state.malformed` | `ProcessState.FromJson` could not read the text (not JSON, wrong shape, or a duplicate member name) |

## Scenarios

A scenario is an executable example stored in the document. `BrainProcess.RunScenarios()` plays each one against the interpreter with a virtual clock and returns one result per scenario.

```json
{ "name": "two reminders then escalate",
  "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { } },
  "events": [
    { "complete": "check", "output": { "pending": 2 } },
    { "fail": "send", "error": "Transient", "code": "x", "retryAfter": "PT30S" },
    { "advance": "PT24H" },
    { "event": "form_submitted", "correlation": "c-1", "payload": { } },
    { "task": "escalate", "outcome": "resolved", "data": { } },
    { "child": "stage", "result": "done", "output": { } },
    { "childFailed": "stage", "code": "x" },
    { "cancel": "reason" },
    { "resume": true } ],
  "expect": { "status": "Completed", "result": "closed",
              "trace": ["check", "missing", "remind", "send"],
              "variables": { "pending": 0 } } }
```

`complete` and `fail` answer the outstanding effect of the named step; `task`, `child` and `childFailed` the outstanding task or child. `advance` moves the clock forward and fires every timer that falls due, each at its own time and in time order (ties by timer id), including timers started by those firings. On a `poll` step, `complete` and `fail` answer the read of the current round. A scenario may carry `zones`, an object of IANA id to a fixed UTC offset such as `"-05:00"` (at most 14 hours): a `WindowRequest` is answered with `DeliveryWindows.NextOpen` at the virtual clock, at once when the window is open and otherwise by a timer at the next opening, and a zone that is not listed is answered with `WindowFailed` (`process.window.zone_unknown`). Triggers are not executed by scenarios. An event that matches nothing outstanding, or any rejected trigger, fails the scenario. `expect.status` is required; `result`, `trace` (the exact ordered list of entered steps) and `variables` (a subset, compared as JSON) are optional.

## Complete example

A pre-visit outreach: read how many items are missing, decide with a rule, send at most two reminders, each at most once, wait for the reply with a timeout, and hand the case to a person when the reminders are used up or the delivery is uncertain. The contract and the rule it uses:

```json
{
  "module": "outreach",
  "schemaVersion": 1,
  "facts": [
    {
      "kind": "outreach.missing_items",
      "name": "missing",
      "type": "Integer",
      "nullable": false,
      "allowedValues": [],
      "labelSource": null,
      "catalogFactId": null
    }
  ],
  "kinds": [
    {
      "id": "outreach.missing_items",
      "schemaVersion": 1,
      "evaluationMode": "OnDemand",
      "permissions": {
        "read": "outreach.decisions.read",
        "author": "outreach.decisions.author",
        "approve": "outreach.decisions.approve",
        "readTrace": "outreach.decisions.read_trace"
      },
      "outcomes": [
        {
          "name": "action",
          "type": "Enum",
          "nullable": false,
          "labelSource": null,
          "allowedValues": ["none", "remind"]
        }
      ]
    }
  ]
}
```

```json
{
  "$spec": "brain.rule@1",
  "id": "previsit.missing_items",
  "version": 2,
  "uses": [],
  "requires": ["outreach@1"],
  "meta": { "title": "Missing items decide the next outreach action" },
  "body": {
    "kind": "outreach.missing_items",
    "hitPolicy": "unique",
    "rows": [
      {
        "id": "nothing-missing",
        "version": 1,
        "legacyKey": "nothing-missing",
        "conditions": [
          {
            "fact": "missing",
            "op": "equals",
            "value": { "type": "Integer", "value": "0" }
          }
        ],
        "outcome": { "action": "none" }
      },
      {
        "id": "something-missing",
        "version": 1,
        "legacyKey": "something-missing",
        "conditions": [
          {
            "fact": "missing",
            "op": "range",
            "value": { "type": "Integer", "value": "1" },
            "lowerInclusive": true,
            "upperInclusive": true
          }
        ],
        "outcome": { "action": "remind" }
      }
    ],
    "goldens": [
      {
        "name": "Nothing missing needs no reminder",
        "facts": { "missing": { "type": "Integer", "value": "0" } },
        "expect": { "row": "nothing-missing", "outcome": { "action": "none" } }
      },
      {
        "name": "Two missing items need a reminder",
        "facts": { "missing": { "type": "Integer", "value": "2" } },
        "expect": { "row": "something-missing", "outcome": { "action": "remind" } }
      },
      {
        "name": "A negative count decides nothing",
        "facts": { "missing": { "type": "Integer", "value": "-1" } },
        "expect": { "noDecision": true }
      }
    ]
  }
}
```

The process, with five scenarios that `RunScenarios()` plays:

```json
{
  "$spec": "brain.process@1",
  "id": "previsit.outreach",
  "version": 1,
  "uses": [{ "$ref": "rule/previsit.missing_items" }],
  "requires": ["outreach@1"],
  "meta": { "title": "Pre-visit outreach" },
  "body": {
    "subject": "case",
    "stages": ["previsit"],
    "variables": {
      "case_ref": { "type": "Code", "input": true },
      "missing": { "type": "Integer" }
    },
    "start": "cycle",
    "steps": {
      "cycle": {
        "kind": "loop",
        "stage": "previsit",
        "maxIterations": 2,
        "body": "check",
        "onExhausted": "escalate_final"
      },
      "check": {
        "kind": "read",
        "port": "intake.status",
        "input": { "case": "case_ref" },
        "output": { "missing_count": "missing" },
        "next": "assess",
        "onError": "escalate"
      },
      "assess": {
        "kind": "decide",
        "rule": "previsit.missing_items",
        "facts": { "missing": "missing" },
        "route": "action",
        "on": { "none": "done", "remind": "send" },
        "onNoDecision": "escalate",
        "onError": "escalate"
      },
      "send": {
        "kind": "action",
        "port": "messaging.notice",
        "delivery": "atMostOnce",
        "input": { "case": "case_ref", "missing": "missing" },
        "retry": { "maxAttempts": 3, "backoff": "exponential", "initial": "PT1M", "max": "PT10M" },
        "next": "reply",
        "onUnknown": "escalate",
        "onError": "escalate"
      },
      "reply": {
        "kind": "wait",
        "event": "form_submitted",
        "correlation": "case_ref",
        "timeout": "PT24H",
        "next": "cycle",
        "onTimeout": "cycle"
      },
      "escalate": {
        "kind": "humanTask",
        "queue": "front_desk",
        "outcomes": ["resolved", "unreachable"],
        "show": ["case_ref", "missing"],
        "timeout": "P3D",
        "on": { "resolved": "cycle", "unreachable": "closed" },
        "onTimeout": "closed"
      },
      "escalate_final": {
        "kind": "humanTask",
        "queue": "front_desk",
        "outcomes": ["resolved", "unreachable"],
        "show": ["case_ref", "missing"],
        "timeout": "P3D",
        "on": { "resolved": "done", "unreachable": "closed" },
        "onTimeout": "closed"
      },
      "done": { "kind": "end", "result": "ready" },
      "closed": { "kind": "end", "result": "unreachable" }
    },
    "scenarios": [
      {
        "name": "nothing missing",
        "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } },
        "events": [
          { "complete": "check", "output": { "missing_count": 0 } }
        ],
        "expect": {
          "status": "Completed",
          "result": "ready",
          "trace": ["cycle", "check", "assess", "done"],
          "variables": { "missing": 0 }
        }
      },
      {
        "name": "reply after the first reminder",
        "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } },
        "events": [
          { "complete": "check", "output": { "missing_count": 2 } },
          { "complete": "send" },
          { "event": "form_submitted", "correlation": "c-1", "payload": { } },
          { "complete": "check", "output": { "missing_count": 0 } }
        ],
        "expect": {
          "status": "Completed",
          "result": "ready",
          "trace": ["cycle", "check", "assess", "send", "reply", "cycle", "check", "assess", "done"],
          "variables": { "missing": 0 }
        }
      },
      {
        "name": "two reminders then escalate",
        "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } },
        "events": [
          { "complete": "check", "output": { "missing_count": 2 } },
          { "complete": "send" },
          { "advance": "PT24H" },
          { "complete": "check", "output": { "missing_count": 2 } },
          { "complete": "send" },
          { "advance": "PT24H" },
          { "task": "escalate_final", "outcome": "unreachable" }
        ],
        "expect": {
          "status": "Completed",
          "result": "unreachable",
          "trace": ["cycle", "check", "assess", "send", "reply", "cycle", "check", "assess", "send", "reply", "cycle", "escalate_final", "closed"],
          "variables": { "missing": 2 }
        }
      },
      {
        "name": "unknown delivery goes to a task",
        "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } },
        "events": [
          { "complete": "check", "output": { "missing_count": 3 } },
          { "fail": "send", "error": "Unknown", "code": "send.lost" },
          { "task": "escalate", "outcome": "resolved" },
          { "complete": "check", "output": { "missing_count": 0 } }
        ],
        "expect": {
          "status": "Completed",
          "result": "ready",
          "trace": ["cycle", "check", "assess", "send", "escalate", "cycle", "check", "assess", "done"]
        }
      },
      {
        "name": "transient failure is retried",
        "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } },
        "events": [
          { "complete": "check", "output": { "missing_count": 1 } },
          { "fail": "send", "error": "Transient", "code": "send.busy", "retryAfter": "PT30S" },
          { "advance": "PT1M" },
          { "complete": "send" },
          { "event": "form_submitted", "correlation": "c-1", "payload": { } },
          { "complete": "check", "output": { "missing_count": 0 } }
        ],
        "expect": {
          "status": "Completed",
          "result": "ready",
          "trace": ["cycle", "check", "assess", "send", "reply", "cycle", "check", "assess", "done"]
        }
      }
    ]
  }
}
```

Reading it: the `cycle` loop bounds the number of reminders to two. `check` reads the count of missing items into `missing`; `assess` asks the rule and routes on its `action` outcome (`none` ends the process as `ready`, `remind` sends a notice). `send` is `atMostOnce`: if the host cannot tell whether the notice went out (`Unknown`), the case goes to the `escalate` task instead of being retried, and a person who resolves it sends the case back to `cycle`. When the reminders are used up (`onExhausted`) the case goes to a separate task, `escalate_final`: a path out of a loop must not lead back into the loop. `reply` waits for the `form_submitted` event of this case for 24 hours and then returns to `cycle`. The documentation tests load these files and run the scenarios, so the page and the fixtures cannot drift apart.

## Follow-up example

A second synthetic process shows the additions together: it is started by an event or a schedule, sends a notice only inside contact hours (waiting for the window to open, and again before any retry) and then polls for a reply, reading at growing intervals until a rule says the notice is answered or six hours have passed. Its scenarios use `zones` to answer the window. The contract, the rule and the process:

```json
{
  "module": "followup",
  "schemaVersion": 1,
  "facts": [
    {
      "kind": "followup.reply_state",
      "name": "replies",
      "type": "Integer",
      "nullable": false,
      "allowedValues": [],
      "labelSource": null,
      "catalogFactId": null
    }
  ],
  "kinds": [
    {
      "id": "followup.reply_state",
      "schemaVersion": 1,
      "evaluationMode": "OnDemand",
      "permissions": {
        "read": "followup.decisions.read",
        "author": "followup.decisions.author",
        "approve": "followup.decisions.approve",
        "readTrace": "followup.decisions.read_trace"
      },
      "outcomes": [
        {
          "name": "state",
          "type": "Enum",
          "nullable": false,
          "labelSource": null,
          "allowedValues": ["waiting", "answered"]
        }
      ]
    }
  ]
}
```

```json
{
  "$spec": "brain.rule@1",
  "id": "notice.reply_state",
  "version": 1,
  "uses": [],
  "requires": ["followup@1"],
  "meta": { "title": "Replies received decide whether a notice is answered" },
  "body": {
    "kind": "followup.reply_state",
    "hitPolicy": "unique",
    "rows": [
      {
        "id": "no-reply",
        "version": 1,
        "legacyKey": "no-reply",
        "conditions": [
          { "fact": "replies", "op": "equals", "value": { "type": "Integer", "value": "0" } }
        ],
        "outcome": { "state": "waiting" }
      },
      {
        "id": "replied",
        "version": 1,
        "legacyKey": "replied",
        "conditions": [
          { "fact": "replies", "op": "range", "value": { "type": "Integer", "value": "1" } }
        ],
        "outcome": { "state": "answered" }
      }
    ],
    "goldens": [
      {
        "name": "No reply yet keeps waiting",
        "facts": { "replies": { "type": "Integer", "value": "0" } },
        "expect": { "row": "no-reply", "outcome": { "state": "waiting" } }
      },
      {
        "name": "One reply answers the notice",
        "facts": { "replies": { "type": "Integer", "value": "1" } },
        "expect": { "row": "replied", "outcome": { "state": "answered" } }
      },
      {
        "name": "A negative count decides nothing",
        "facts": { "replies": { "type": "Integer", "value": "-1" } },
        "expect": { "noDecision": true }
      }
    ]
  }
}
```

```json
{
  "$spec": "brain.process@1",
  "id": "notice.followup",
  "version": 1,
  "uses": [{ "$ref": "rule/notice.reply_state" }],
  "requires": ["followup@1"],
  "meta": { "title": "Notice sent in contact hours, then polled for a reply" },
  "body": {
    "subject": "case",
    "stages": ["notice"],
    "variables": {
      "case_ref": { "type": "Code", "input": true },
      "replies": { "type": "Integer" }
    },
    "triggers": [
      { "kind": "event", "event": "case.opened", "businessKey": "case_ref", "input": { "case_ref": "case_ref" } },
      { "kind": "schedule", "id": "morning", "cron": "0 8 * * 1-5", "zone": "America/New_York", "misfire": "skip" }
    ],
    "windows": {
      "contact_hours": { "zone": "America/New_York", "days": ["mon", "tue", "wed", "thu", "fri"], "from": "09:00", "to": "18:00" }
    },
    "start": "send",
    "steps": {
      "send": {
        "kind": "action",
        "stage": "notice",
        "port": "messaging.notice",
        "delivery": "atLeastOnce",
        "contact": true,
        "window": "contact_hours",
        "input": { "case": "case_ref" },
        "retry": { "maxAttempts": 2, "backoff": "fixed", "initial": "PT5M" },
        "next": "await_reply",
        "onError": "failed"
      },
      "await_reply": {
        "kind": "poll",
        "port": "intake.replies",
        "input": { "case": "case_ref" },
        "output": { "reply_count": "replies" },
        "rule": "notice.reply_state",
        "facts": { "replies": "replies" },
        "field": "state",
        "until": ["answered"],
        "every": "PT1H",
        "backoff": "exponential",
        "max": "PT4H",
        "maxDuration": "PT6H",
        "next": "done",
        "onExpired": "expired",
        "onError": "failed"
      },
      "done": { "kind": "end", "result": "answered" },
      "expired": { "kind": "end", "result": "expired" },
      "failed": { "kind": "end", "result": "failed" }
    },
    "scenarios": [
      {
        "name": "open window sends at once and the second round finds a reply",
        "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } },
        "zones": { "America/New_York": "-05:00" },
        "events": [
          { "complete": "send" },
          { "complete": "await_reply", "output": { "reply_count": 0 } },
          { "advance": "PT1H" },
          { "complete": "await_reply", "output": { "reply_count": 2 } }
        ],
        "expect": {
          "status": "Completed",
          "result": "answered",
          "trace": ["send", "await_reply", "done"],
          "variables": { "replies": 2 }
        }
      },
      {
        "name": "a closed window holds the notice until the next opening",
        "start": { "at": "2027-01-04T23:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } },
        "zones": { "America/New_York": "-05:00" },
        "events": [
          { "advance": "PT15H" },
          { "complete": "send" },
          { "complete": "await_reply", "output": { "reply_count": 1 } }
        ],
        "expect": {
          "status": "Completed",
          "result": "answered",
          "trace": ["send", "await_reply", "done"]
        }
      },
      {
        "name": "the poll expires after the longest allowed time",
        "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } },
        "zones": { "America/New_York": "-05:00" },
        "events": [
          { "complete": "send" },
          { "complete": "await_reply", "output": { "reply_count": 0 } },
          { "advance": "PT1H" },
          { "complete": "await_reply", "output": { "reply_count": 0 } },
          { "advance": "PT2H" },
          { "complete": "await_reply", "output": { "reply_count": 0 } }
        ],
        "expect": {
          "status": "Completed",
          "result": "expired",
          "trace": ["send", "await_reply", "expired"]
        }
      },
      {
        "name": "a zone the host cannot resolve follows onError",
        "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } },
        "events": [],
        "expect": {
          "status": "Completed",
          "result": "failed",
          "trace": ["send", "failed"]
        }
      },
      {
        "name": "a failed read after its retries follows onError",
        "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1" } },
        "zones": { "America/New_York": "-05:00" },
        "events": [
          { "complete": "send" },
          { "fail": "await_reply", "error": "Permanent", "code": "intake.gone" }
        ],
        "expect": {
          "status": "Completed",
          "result": "failed",
          "trace": ["send", "await_reply", "failed"]
        }
      }
    ]
  }
}
```

Reading it: `send` is a contact action tied to `contact_hours` (Monday to Friday, 09:00 to 18:00 in New York), so at 23:00 UTC in January (18:00 local) the notice waits for the next morning. `await_reply` reads `intake.replies` after the notice, asks the rule for `state`, and ends on `answered`; otherwise it waits one hour, then two, then four (the `max`), and expires once the next read would fall after the sixth hour. A read that fails for good goes to `failed`, as does a window zone the host cannot resolve. The documentation tests load these files and run the scenarios.

## Deadline example

A third synthetic process shows the two additions together, with the labels. A reminder is sent only inside contact hours, and the wait for the contact hours can be interrupted by the person's reply (`interruptBy`). A confirmation is awaited until two hours before the visit, and a call to the person is open until the visit starts (`timeout` tied to the instant of the visit). It uses no rule, so there is no contract or rule file. Its scenarios use `zones` to answer the window.

```json
{
  "$spec": "brain.process@1",
  "id": "visit.reminder",
  "version": 1,
  "uses": [],
  "requires": ["reminder@1"],
  "meta": {
    "title": "Visit reminder with a deadline",
    "display": {
      "en": {
        "title": "Visit reminder",
        "stages": { "reminder": "Reminder" },
        "steps": {
          "remind": "Send the reminder",
          "await_confirmation": "Wait for the confirmation",
          "handle_reply": "Handle the reply",
          "call_patient": "Call the patient",
          "confirmed": "Confirmed",
          "unreachable": "Unreachable",
          "lapsed": "Lapsed"
        },
        "results": { "confirmed": "Confirmed", "unreachable": "Unreachable", "lapsed": "Lapsed" },
        "queues": { "front_desk": "Front desk" },
        "outcomes": { "handled": "Handled", "reached": "Reached", "unreachable": "Unreachable" },
        "events": { "case.reply_received": "Reply received", "visit.confirmed": "Visit confirmed" },
        "windows": { "contact_hours": "Contact hours" }
      },
      "es": {
        "title": "Recordatorio de visita",
        "stages": { "reminder": "Recordatorio" },
        "steps": {
          "remind": "Enviar el recordatorio",
          "await_confirmation": "Esperar la confirmación",
          "handle_reply": "Atender la respuesta",
          "call_patient": "Llamar al paciente",
          "confirmed": "Confirmada",
          "unreachable": "Sin contacto",
          "lapsed": "Vencida"
        },
        "results": { "confirmed": "Confirmada", "unreachable": "Sin contacto", "lapsed": "Vencida" },
        "queues": { "front_desk": "Recepción" },
        "outcomes": { "handled": "Atendida", "reached": "Localizado", "unreachable": "Sin contacto" },
        "events": { "case.reply_received": "Respuesta recibida", "visit.confirmed": "Visita confirmada" },
        "windows": { "contact_hours": "Horario de contacto" }
      }
    }
  },
  "body": {
    "subject": "case",
    "stages": ["reminder"],
    "variables": {
      "case_ref": { "type": "Code", "input": true },
      "visit_at": { "type": "Instant", "input": true },
      "reply_kind": { "type": "Code" }
    },
    "accepts": ["case.reply_received", "visit.confirmed"],
    "windows": {
      "contact_hours": { "zone": "America/New_York", "days": ["mon", "tue", "wed", "thu", "fri"], "from": "09:00", "to": "18:00" }
    },
    "start": "remind",
    "steps": {
      "remind": {
        "kind": "action",
        "stage": "reminder",
        "port": "messaging.reminder",
        "delivery": "atMostOnce",
        "contact": true,
        "window": "contact_hours",
        "input": { "case": "case_ref" },
        "interruptBy": {
          "event": "case.reply_received",
          "correlation": "case_ref",
          "output": { "reply_kind": "reply_kind" },
          "to": "handle_reply"
        },
        "next": "await_confirmation",
        "onUnknown": "call_patient",
        "onError": "call_patient"
      },
      "await_confirmation": {
        "kind": "wait",
        "stage": "reminder",
        "event": "visit.confirmed",
        "correlation": "case_ref",
        "timeout": { "until": "visit_at", "offset": "-PT2H" },
        "next": "confirmed",
        "onTimeout": "call_patient"
      },
      "handle_reply": {
        "kind": "humanTask",
        "stage": "reminder",
        "queue": "front_desk",
        "outcomes": ["handled"],
        "show": ["case_ref", "reply_kind"],
        "timeout": { "until": "visit_at", "offset": "-PT1H" },
        "on": { "handled": "confirmed" },
        "onTimeout": "lapsed"
      },
      "call_patient": {
        "kind": "humanTask",
        "stage": "reminder",
        "queue": "front_desk",
        "outcomes": ["reached", "unreachable"],
        "show": ["case_ref"],
        "timeout": { "until": "visit_at" },
        "on": { "reached": "confirmed", "unreachable": "unreachable" },
        "onTimeout": "lapsed"
      },
      "confirmed": { "kind": "end", "stage": "reminder", "result": "confirmed" },
      "unreachable": { "kind": "end", "stage": "reminder", "result": "unreachable" },
      "lapsed": { "kind": "end", "stage": "reminder", "result": "lapsed" }
    },
    "scenarios": [
      {
        "name": "a reply interrupts the wait for the contact hours",
        "start": { "at": "2027-01-04T23:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1", "visit_at": "2027-01-05T20:00:00Z" } },
        "zones": { "America/New_York": "-05:00" },
        "events": [
          { "event": "case.reply_received", "correlation": "c-1", "payload": { "reply_kind": "reschedule" } },
          { "task": "handle_reply", "outcome": "handled" }
        ],
        "expect": {
          "status": "Completed",
          "result": "confirmed",
          "trace": ["remind", "handle_reply", "confirmed"],
          "variables": { "reply_kind": "reschedule" }
        }
      },
      {
        "name": "the window opens first and the reminder goes out",
        "start": { "at": "2027-01-04T23:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1", "visit_at": "2027-01-05T20:00:00Z" } },
        "zones": { "America/New_York": "-05:00" },
        "events": [
          { "advance": "PT15H" },
          { "complete": "remind" },
          { "event": "visit.confirmed", "correlation": "c-1" }
        ],
        "expect": {
          "status": "Completed",
          "result": "confirmed",
          "trace": ["remind", "await_confirmation", "confirmed"]
        }
      },
      {
        "name": "a reply while the reminder is being sent is kept and does not interrupt",
        "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1", "visit_at": "2027-01-04T20:00:00Z" } },
        "zones": { "America/New_York": "-05:00" },
        "events": [
          { "event": "case.reply_received", "correlation": "c-1", "payload": { "reply_kind": "question" } },
          { "complete": "remind" },
          { "event": "visit.confirmed", "correlation": "c-1" }
        ],
        "expect": {
          "status": "Completed",
          "result": "confirmed",
          "trace": ["remind", "await_confirmation", "confirmed"]
        }
      },
      {
        "name": "no confirmation two hours before the visit goes to a person",
        "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1", "visit_at": "2027-01-04T20:00:00Z" } },
        "zones": { "America/New_York": "-05:00" },
        "events": [
          { "complete": "remind" },
          { "advance": "PT4H" },
          { "task": "call_patient", "outcome": "unreachable" }
        ],
        "expect": {
          "status": "Completed",
          "result": "unreachable",
          "trace": ["remind", "await_confirmation", "call_patient", "unreachable"]
        }
      },
      {
        "name": "a visit that is too close for the wait goes to a person at once",
        "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1", "visit_at": "2027-01-04T15:30:00Z" } },
        "zones": { "America/New_York": "-05:00" },
        "events": [
          { "complete": "remind" },
          { "task": "call_patient", "outcome": "reached" }
        ],
        "expect": {
          "status": "Completed",
          "result": "confirmed",
          "trace": ["remind", "await_confirmation", "call_patient", "confirmed"]
        }
      },
      {
        "name": "a call still open when the visit starts lapses",
        "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04", "input": { "case_ref": "c-1", "visit_at": "2027-01-04T16:00:00Z" } },
        "zones": { "America/New_York": "-05:00" },
        "events": [
          { "complete": "remind" },
          { "advance": "PT2H" }
        ],
        "expect": {
          "status": "Completed",
          "result": "lapsed",
          "trace": ["remind", "await_confirmation", "call_patient", "lapsed"]
        }
      }
    ]
  }
}
```

Reading it: at 23:00 UTC in January (18:00 in New York) the window is closed, so `remind` emits the window request and a wait for `case.reply_received` together; the reply arrives first, the step goes to `handle_reply` with `reply_kind` assigned, and no reminder is sent. When the window opens first, the reminder goes out and a reply that arrives while it is being sent is kept in the inbox (the name is in `accepts`) without interrupting. `await_confirmation` is left through `onTimeout` at two hours before `visit_at`, and at once when the visit is closer than that. `call_patient` is open until the visit starts and then lapses. The documentation tests load this file, run its scenarios, check that the page shows it as it is and that `ProcessDisplay.Validate` has nothing to say about its labels.

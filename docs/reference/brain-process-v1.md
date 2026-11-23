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
| `meta` | Free object (title, labels). Not interpreted; `BrainProcess.Meta` returns a deep copy. |
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
| `scenarios` | no | Executable examples, see [Scenarios](#scenarios). |

## Variables

A declaration is `{ "type": ..., "allowedValues": [...]?, "input": true?, "initial": ...? }`. `input: true` marks a variable that `Start` may receive. `allowedValues` is for `Enum` only and is required there. `null` or absence means "no value" for every type.

| `type` | JSON form of a value |
| --- | --- |
| `Code` | string |
| `Enum` | string, one of `allowedValues` |
| `Boolean` | `true` or `false` |
| `Integer` | JSON integer within the safe-integer range (plus or minus 9007199254740991) |
| `Decimal` | string such as `"12.50"` (`-?(0\|[1-9][0-9]*)(\.[0-9]{1,28})?`) |
| `Money` | `{ "amount": "<Decimal string>", "currency": "USD" }` (three upper-case letters) |
| `Date` | string `YYYY-MM-DD`, a real calendar date |
| `Reference` | string GUID in the `D` format |
| `Instant` | ISO 8601 date-time with `Z` or an offset; stored as UTC `yyyy-MM-ddTHH:mm:ssZ`, fractions truncated |
| `Json` | any JSON value, opaque |

A value that does not fit its type is `process.variable.type_mismatch`. Values are stored in the state as canonical JSON text (object members in ordinal order).

## Steps

Every step has a `kind` and may have a `stage`. "Mapping" means an object of strings to strings. A step reference is the id of a step.

| `kind` | Members |
| --- | --- |
| `read` | `port`; `input?` (port parameter to variable); `output?` (result member to variable); `retry?`; `next`; `onError?`, `onNotAvailable?`, `onNotEntitled?` |
| `action` | as `read`, plus `delivery` (`atLeastOnce` or `atMostOnce`) and `onUnknown`, required with `atMostOnce` and forbidden otherwise |
| `ai` | `port`, `model`, `prompt` `{ "id", "version" }`, `input?`, `inputSchema`, `outputSchema`, `output?`, `maxAttempts?` (1 to 5, default 1), `retry?`, `next` (a `decide` step), `onInvalid`, `onError?`, `onNotAvailable?`, `onNotEntitled?` |
| `decide` | `rule`, `facts` (fact name to variable); either `output?` and one of `next` or `route` with `on` (rule of hit policy `unique` or `first`), or `collectInto` and `next` (hit policy `collect`); `onNoDecision`; `onError?` |
| `humanTask` | `queue`, `outcomes`, `show?` (variables to show), `output?` (data member to variable), `on` (outcome to step), `timeout?`, `onTimeout` (required with `timeout`, forbidden without) |
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

An `ai` step validates its input object against `inputSchema` before asking (a failure is `process.ai.input_invalid`) and the answer against `outputSchema`. An invalid answer is retried at once, up to `maxAttempts`, then goes to `onInvalid`. The history entry of a valid answer records the model, the prompt id and version, a hash of the input and the output. `next` must be a `decide` step: an AI output never moves a case by itself, a rule decides.

### `decide`

The facts are built from the mapped variables (a variable without a value leaves the fact out) and the rule is evaluated for the clock's `Today`. The `Decided` history entry carries the provenance: rule id and version, whether it decided, the matched row keys, the outcome, the facts and a hash of the facts.

- No decision goes to `onNoDecision`. A rule error is recorded in a `Decided` entry (code = the error code, data `{"rule":..,"version":..,"error":..}`) and goes to `onError`, or suspends with that code.
- Outcome values are assigned to variables of the same type. A `Decimal` outcome field (a JSON number in the rule library) becomes the number's raw text as the variable's string, and a `Money` outcome `{amount: number, currency}` becomes `{amount: "<raw text>", currency}`; the number is never converted to a binary floating type. A number that does not match the `Decimal` form (exponent notation, for example) is a `process.variable.type_mismatch`, routed to `onError` or suspension.
- With `unique` or `first` rules, `output` assigns outcome fields to variables, then the step goes to `next` or follows `route`: the value of that outcome field (a `Boolean` as `"true"` or `"false"`) selects the entry of `on`. A value without an entry goes to `onError`, or suspends with `process.route.unmatched`.
- With a `collect` rule the whole outcome array is stored in the `Json` variable `collectInto`.

### `humanTask`

Asks the host to create a task in `queue` with the listed `outcomes` and the values of the `show` variables. `TaskCompleted` names one outcome and carries a data object that is assigned through `output`; the step goes to `on[outcome]`. An unknown outcome, or data that does not fit, is **rejected** and leaves the task open. With `timeout` the host also receives a timer; when it fires the task is cancelled and the step goes to `onTimeout`.

### `timer`

`after` starts a timer at now plus the duration; `until` uses the instant in a variable plus the optional `offset` (a missing instant suspends with `process.timer.until_missing`). The timer is always requested, even when it is already due.

### `wait` and events

The step waits for an event of the given name; with `correlation` only an event with the same correlation value matches. If the oldest matching event is already in the inbox (see below) it is consumed on entering and nothing is requested. `timeout` and `onTimeout` work as for tasks.

An `EventReceived` trigger is handled in this order: a finished instance ignores it; the step that waits for it consumes it; otherwise, if the name is in `accepts`, it is appended to the inbox (even while the instance is suspended); otherwise it is **rejected** with `process.event.not_accepted`.

### `loop`

A bounded gate. Its `body` edge is the only edge that may close a cycle in the step graph, and it is taken at most `maxIterations` times; the `onExhausted` and `next` paths of a loop must not lead back to the loop or into any cycle. Each arrival counts: with `forEach` the item is the n-th element of the list and the step goes to `next` when the list is done; otherwise, after `maxIterations` arrivals it goes to `onExhausted`. The list check comes first. Counters never reset within an instance. The graph without the `body` edges of loop steps must be acyclic (a cycle is the load error `process.cycle.unbounded`, reported once per cycle at its first step), so an instance always enters a bounded number of steps; `limits.maxSteps` is a second guard.

### `subprocess`

Asks the host to start the process `process` as a child with the mapped input. `ChildCompleted` carries a result name and an output object; the step goes to `next` or `on[result]`. `ChildFailed`, an unlisted result or output that does not fit goes to `onError`, or suspends.

### `end`

Completes the instance with `result`.

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

`ProcessLoadContext.KnownProcessIds` is optional; when given, `subprocess` steps must name one of them. Errors are `BrainSpecError` values ordered by path then code. JSON, envelope and schema failures are reported alone (`spec.json.invalid`, `spec.structure.envelope`, `spec.structure.unknown_format`, `spec.structure.schema`); only a document that passes them is checked against the rules below, and all of those are reported together.

| Code | Condition |
| --- | --- |
| `process.step.unknown` | a step reference names no step |
| `process.step.unreachable` | a step cannot be reached from `start` |
| `process.stage.unknown` | `stage` is not listed in `stages` |
| `process.variable.unknown` | a mapping value or variable member names no declared variable |
| `process.variable.type` | a variable is used where its type is not allowed (collect, loop, `until`, `correlation`; a fact or output mapping of a different type) |
| `process.variable.initial` | `initial` is not a valid value, or `allowedValues` is misused |
| `process.rule.unavailable` | the rule is not in the load context |
| `process.rule.fact_unknown` | a `facts` key is not a fact of the rule's kind |
| `process.rule.outcome_unknown` | an `output` key or the `route` is not an outcome field of the kind (also when the route field is not an `Enum`, `Boolean` or `Code`) |
| `process.rule.hit_policy` | members for a `collect` rule on a `unique` or `first` rule, or the reverse |
| `process.route.incomplete` | at `/on`: a `Boolean` route lacks `true` or `false`; an `Enum` or `Code` route lacks a value that some row or the default row of the rule produces for the route field; a `humanTask` `on` lacks one of its `outcomes` |
| `process.route.unknown_value` | at `/on/<key>`: an `Enum` or `Code` route has a key that no row and no default row of the rule produces for the route field; a `humanTask` `on` has a key that is not one of its `outcomes` |
| `process.uses.missing` | a rule or process named by a step is not in `uses` |
| `process.uses.unused` | `uses` lists something no step names |
| `process.subprocess.unavailable` | the child process is not among the known process ids |
| `process.ai.next_not_decide` | the `next` of an `ai` step is not a `decide` step |
| `process.ai.schema_invalid` | `inputSchema` or `outputSchema` cannot be built as a JSON Schema |
| `process.cycle.unbounded` | the step graph without the `body` edges of `loop` steps has a cycle (a step that routes back to a step before it, or a loop whose `onExhausted` or `next` path leads back into a cycle) |
| `process.duration.invalid` | a duration that matches the pattern but is zero, longer than 3660 days or has no component |
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
| `TimerFired(timerId)` | a requested timer is due (timers of retry delays, `timer` steps and timeouts) |
| `EventReceived(name, correlation, payloadJson)` | an external event arrived |
| `TaskCompleted(taskId, outcome, dataJson)` | a person finished a task |
| `ChildCompleted(childKey, result, outputJson)` / `ChildFailed(childKey, code)` | the child process ended |
| `CancelRequested(reason)` | stop the instance: outstanding timers, waits and tasks are cancelled |
| `ResumeRequested` | run the current step again after a suspension; refused with `process.limits.max_steps` when the instance has already used its step limit |

### Requests

`EffectRequest` (step, effect id, idempotency key, kind `Read`/`Action`/`Ai`, port, input JSON, attempt, delivery, and for `ai` the model and prompt), `TimerRequest` (id, due time), `CancelTimerRequest`, `WaitRequest` (event, correlation), `CancelWaitRequest`, `TaskRequest` (queue, outcomes, JSON to show), `CancelTaskRequest`, `ChildStartRequest` (process, input JSON).

### Identifiers

Identifiers are derived, never generated. With the instance id `I`, the step `S`, the step's entry number `E` (the running count of steps when it was entered) and the attempt `A`:

| What | Value |
| --- | --- |
| idempotency key | `I/S/E` |
| effect id | `I/S/E/A` |
| timer id | `I/S/E/timer`; retry delay `I/S/E/retry/A`; timeout `I/S/E/timeout` |
| wait id, task id, child key | `I/S/E/wait`, `I/S/E/task`, `I/S/E/child` |

A resume gives the step a fresh entry number, so the host never confuses a re-run with the earlier attempt.

### State and history

`ProcessState.ToJson()` is the serialized state (stable text: object keys in a fixed order, variables and counters in ordinal order) and `ProcessState.FromJson` reads it back; text that is not a state throws `DecisionEngineException` with the code `process.state.malformed`. The state holds the status (`Waiting`, `Suspended`, `Completed`, `Cancelled`), current step, stage, result, variables, loop counters, the outstanding work (`Pending`), the event inbox and, when suspended, the `Problem` (code, step, detail).

Every history entry has a sequence number (continuing across advances), the time, the step, the step's entry number, the attempt, a kind (`Started`, `StepEntered`, `EffectRequested`, `EffectCompleted`, `EffectFailed`, `RetryScheduled`, `Decided`, `TaskRequested`, `TaskCompleted`, `WaitStarted`, `EventConsumed`, `EventBuffered`, `TimerStarted`, `TimerFired`, `TimedOut`, `LoopIteration`, `LoopExhausted`, `ChildStarted`, `ChildCompleted`, `ChildFailed`, `Suspended`, `Resumed`, `Cancelled`, `Completed`), an optional code and an optional JSON object. History never contains a value that the state or the trigger did not contain.

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
| `process.wait.correlation_missing` | a `wait` declares a correlation variable that has no value |
| `process.loop.not_a_list` | a `forEach` variable holds no array |
| `process.limits.max_steps` | the instance entered more steps than `limits.maxSteps`; it is suspended on the step before; a later `ResumeRequested` is rejected with the same code while `StepCount` is at or above the limit |
| `process.state.malformed` | `ProcessState.FromJson` could not read the text |

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

`complete` and `fail` answer the outstanding effect of the named step; `task`, `child` and `childFailed` the outstanding task or child. `advance` moves the clock forward and fires every timer that falls due, each at its own time and in time order (ties by timer id), including timers started by those firings. An event that matches nothing outstanding, or any rejected trigger, fails the scenario. `expect.status` is required; `result`, `trace` (the exact ordered list of entered steps) and `variables` (a subset, compared as JSON) are optional.

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

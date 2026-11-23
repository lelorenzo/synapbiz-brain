# W1-A: `brain.process@1` and the pure process interpreter

Status: approved by the owner on 2026-11-22 (decisions 1 to 9 of [design.md](design.md)). This document is the normative specification of PR W1-A. PR W1-B (PostgreSQL host), W1-C (schedules, delivery windows, `poll`) and W1-D (reference flows, 0.2.0) have their own specs later.

Every example and fixture is synthetic. No patient data, no clinic names, no real identifiers anywhere.

## 1. Deliverable

A new library `Synapbiz.Brain.Process` (assembly, package id and root namespace of the same name) that:

1. validates and loads a `brain.process@1` document into a `BrainProcess`;
2. advances a process instance as a pure function of (definition, state, trigger, clock);
3. serializes instance state to and from JSON;
4. runs the scenarios embedded in a document.

It references `Synapbiz.Brain.Spec` (project reference) and `JsonSchema.Net` (package reference), nothing else. It reads no clock, GUID generator, randomness, file or network. It does not touch `Synapbiz.Brain.Contracts`, `Synapbiz.Brain.Engine` or `Synapbiz.Brain.Spec` (no file in `src/` outside the new project changes).

Out of scope for W1-A: persistence, workers, real timers, cron schedules, `triggers`, delivery windows, `poll`, parallel branches, expressions, migration of running instances.

## 2. Document

Envelope, all members required, no other member allowed: `$spec` = `brain.process@1`; `id` (pattern `^[A-Za-z0-9_][A-Za-z0-9_.-]*$`); `version` (integer 1..9007199254740991); `uses` (array of `{"$ref": "<kind>/<id>"}`, unique); `requires` (array of `<module>@<n>`, unique); `meta` (free object, returned as a deep clone); `body`.

`uses` must contain `rule/<id>` for every rule a `decide` step names and `process/<id>` for every process a `subprocess` step names, and nothing else.

### 2.1 Body

| Member | Required | Meaning |
| --- | --- | --- |
| `subject` | yes | non-empty string; the case type, opaque to Brain |
| `stages` | yes | array of unique non-empty strings (may be empty) |
| `variables` | yes | object: variable name (id pattern) to declaration |
| `accepts` | no | array of unique event names that may be buffered while no step waits for them |
| `limits` | no | `{ "maxSteps": <integer 1..1000000> }`; default 10000 |
| `start` | yes | id of the first step |
| `steps` | yes | object: step id (id pattern) to step; at least one |
| `scenarios` | no | array, see section 8 |

### 2.2 Variables

Declaration: `{ "type": <type>, "allowedValues": [..]?, "input": <bool>?, "initial": <value>? }`.

| `type` | JSON form of a value |
| --- | --- |
| `Code` | string |
| `Enum` | string; `allowedValues` is required and non-empty, the value must be a member |
| `Boolean` | `true` / `false` |
| `Integer` | JSON integer within the safe-integer range |
| `Decimal` | string matching `^-?(0|[1-9][0-9]*)(\.[0-9]{1,28})?$` |
| `Money` | `{ "amount": <Decimal string>, "currency": <three upper-case letters> }` |
| `Date` | string `YYYY-MM-DD` (a real calendar date) |
| `Reference` | string GUID in the `D` format, lower or upper case |
| `Instant` | string, ISO 8601 date-time with `Z` or an offset; stored normalized to UTC as `yyyy-MM-ddTHH:mm:ssZ` (seconds precision; fractions are truncated) |
| `Json` | any JSON value; opaque |

`null` (or absence) means "no value" for every type. `allowedValues` is allowed only on `Enum`. `input: true` marks a variable that `Start` may receive. `initial` must be a valid value of the type. A value that does not fit its declared type is the error `process.variable.type_mismatch` (section 6.9).

### 2.3 Steps

Common optional member on every step: `stage` (must be listed in `stages`).

"Mapping" below means a JSON object whose keys and values are strings. A **step reference** is the id of a step in `steps`.

| `kind` | Members (required unless marked `?`) |
| --- | --- |
| `read` | `port`, `input?` (mapping: port parameter → variable), `output?` (mapping: result member → variable), `retry?`, `next`, `onError?`, `onNotAvailable?`, `onNotEntitled?` |
| `action` | as `read`, plus `delivery` (`atLeastOnce` or `atMostOnce`) and `onUnknown` (required when `delivery` is `atMostOnce`, forbidden otherwise) |
| `ai` | `port`, `model` (non-empty string), `prompt` `{ "id": string, "version": integer ≥ 1 }`, `input?` (mapping), `inputSchema` (JSON Schema object), `outputSchema` (JSON Schema object), `output?` (mapping), `maxAttempts?` (1..5, default 1), `retry?`, `next` (must be a `decide` step), `onInvalid`, `onError?`, `onNotAvailable?`, `onNotEntitled?` |
| `decide` | `rule` (rule id), `facts` (mapping: fact name → variable), then either (a) for a `unique`/`first` rule: `output?` (mapping: outcome field → variable), and exactly one of `next` or (`route` + `on`), where `route` is an outcome field of type `Enum` or `Boolean` or `Code` and `on` is an object value → step reference; or (b) for a `collect` rule: `collectInto` (a `Json` variable) and `next`. Always: `onNoDecision` (required), `onError?` |
| `humanTask` | `queue` (non-empty string), `outcomes` (non-empty array of unique strings), `show?` (array of variable names), `output?` (mapping: data member → variable), `on` (object outcome → step reference, exactly the members of `outcomes`), `timeout?` (duration), `onTimeout` (required with `timeout`, forbidden without) |
| `wait` | `event` (non-empty string), `correlation?` (variable of type `Code` or `Reference`), `output?` (mapping: payload member → variable), `next`, `timeout?` (duration), `onTimeout` (required with `timeout`, forbidden without) |
| `timer` | exactly one of `after` (duration) or `until` (variable of type `Instant`); `offset?` (signed duration, only with `until`); `next` |
| `loop` | `maxIterations` (integer 1..10000), `body` (step reference), `onExhausted` (step reference), and optionally `forEach` (a `Json` variable holding an array) with `item` (a `Json` variable) and `next` (step reference); `forEach`, `item` and `next` come together or not at all |
| `subprocess` | `process` (process id), `input?` (mapping: child input variable → variable), `output?` (mapping: child output member → variable), exactly one of `next` or `on` (object child result → step reference), `onError?` |
| `end` | `result` (non-empty string) |

`retry`: `{ "maxAttempts": 1..10, "backoff": "fixed" | "exponential", "initial": <duration>, "max": <duration>? }`. Absent means one attempt.

Duration: `^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$` with at least one component, total > 0 and ≤ 3660 days. Signed duration: the same with an optional leading `-`; zero is not allowed.

### 2.4 Static rules (after the schema)

All are errors; the loader reports every error of the first group that has any (groups in this order: JSON, envelope and schema; then all of the following together).

| Code | Condition |
| --- | --- |
| `process.step.unknown` | a step reference (`start`, `next`, `on` values, `onX`, `body`) names no step |
| `process.step.unreachable` | a step cannot be reached from `start` |
| `process.stage.unknown` | `stage` not listed in `stages` |
| `process.variable.unknown` | a mapping value or a variable-valued member names no declared variable |
| `process.variable.type` | a variable is used where its type is not allowed (`collectInto`, `forEach`, `item` need `Json`; `until` needs `Instant`; `correlation` needs `Code` or `Reference`; a `decide` fact mapping needs the variable type equal to the fact type; a `decide` output mapping needs the variable type equal to the outcome field type) |
| `process.variable.initial` | `initial` is not a valid value of the type, or `allowedValues` misused |
| `process.rule.unavailable` | `rule` names no rule in the load context |
| `process.rule.fact_unknown` | a `facts` key is not a fact of the rule's kind |
| `process.rule.outcome_unknown` | an `output` key or `route` is not an outcome field of the rule's kind, or `route` names a field whose type is not `Enum`, `Boolean` or `Code` |
| `process.rule.hit_policy` | members of form (a) used with a `collect` rule, or form (b) with a `unique`/`first` rule |
| `process.route.incomplete` | at `/on`: for a `decide` route of type `Enum` or `Code`, `on` lacks a value that some row or the default row of the rule produces for the `route` field; for a `Boolean` route, `on` lacks `true` or `false` (keys are the strings `"true"` and `"false"`); for a `humanTask`, `on` lacks a member of `outcomes` |
| `process.route.unknown_value` | at `/on/<key>`: for a `decide` route of type `Enum` or `Code`, a key that no row and no default row of the rule produces for the `route` field; for a `humanTask`, a key that is not a member of `outcomes` |
| `process.uses.missing` | a rule or process named by a step is not in `uses` |
| `process.uses.unused` | `uses` lists a reference no step names |
| `process.subprocess.unavailable` | `process` is not in the load context's known process ids (only checked when the context supplies them) |
| `process.ai.next_not_decide` | an `ai` step's `next` is not a `decide` step |
| `process.ai.schema_invalid` | `inputSchema` or `outputSchema` cannot be built as a JSON Schema |
| `process.cycle.unbounded` | the step graph has a cycle that contains no `loop` step. Edges: every step reference of a step; for a `loop`, `body`, `onExhausted` and `next` |
| `process.duration.invalid` | a duration that matches the schema pattern but violates the range rules |
| `process.scenario.invalid` | a scenario refers to an unknown step, variable or event, or is otherwise malformed beyond the schema |

Every error is a `Synapbiz.Brain.Spec.BrainSpecError` (code, RFC 6901 path of the offending value, English message, Spanish message, fix hint). JSON, envelope and schema failures reuse the existing codes `spec.json.invalid`, `spec.structure.envelope`, `spec.structure.unknown_format`, `spec.structure.schema` with the same texts as `Synapbiz.Brain.Spec` uses. Errors are ordered by path, then code, ordinal, without duplicates.

The schema is a JSON Schema 2020-12 file embedded as `Schemas/brain.process.v1.json`, with `additionalProperties: false` everywhere except `meta`, `inputSchema`, `outputSchema`, and scenario payload objects.

## 3. Public API

```csharp
namespace Synapbiz.Brain.Process;

public sealed record ProcessLoadContext(
    IReadOnlyCollection<BrainRule> Rules,
    IReadOnlyCollection<string>? KnownProcessIds = null);

public static class BrainProcessLoader
{
    public static BrainProcessLoadResult Load(string json, ProcessLoadContext context);
}

public sealed record BrainProcessLoadResult(BrainProcess? Process, IReadOnlyList<BrainSpecError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class BrainProcess
{
    public string Id { get; }
    public long Version { get; }
    public string ContentHash { get; }          // ContentHash.FromBytes(UTF-8 bytes of the json argument).Value
    public string Subject { get; }
    public IReadOnlyList<string> Stages { get; }
    public IReadOnlyList<string> StepIds { get; }   // document order
    public JsonObject Meta { get; }                 // deep clone on every get

    public AdvanceResult Start(string instanceId, JsonObject? input, ProcessClock clock);
    public AdvanceResult Advance(ProcessState state, ProcessTrigger trigger, ProcessClock clock);
    public ScenarioRunResult RunScenarios();
}

public readonly record struct ProcessClock(DateTimeOffset Now, DateOnly Today);

public enum AdvanceDisposition { Applied = 1, Ignored = 2, Rejected = 3 }

public sealed record AdvanceResult(
    ProcessState State,
    AdvanceDisposition Disposition,
    string? Code,                               // null when Applied
    IReadOnlyList<ProcessRequest> Requests,
    IReadOnlyList<ProcessHistoryEntry> History);
```

`ProcessState` is an immutable class with `ToJson()` (deterministic text: members in a fixed order, dictionaries ordered by ordinal key) and `static ProcessState FromJson(string)` (throws `DecisionEngineException`, a `BrainException`, with code `process.state.malformed` on bad input). `FromJson(ToJson(s))` equals `s` (value equality). Its public read-only members:

| Member | Type |
| --- | --- |
| `InstanceId`, `DefinitionId`, `DefinitionHash` | string |
| `DefinitionVersion` | long |
| `Status` | `ProcessStatus { Running = 1, Waiting = 2, Suspended = 3, Completed = 4, Cancelled = 5 }` (`Running` never appears in a returned state) |
| `CurrentStep` | string? (null when completed or cancelled) |
| `Stage`, `Result` | string? |
| `Variables` | `IReadOnlyDictionary<string, string>`: variable name → JSON text of its value; a variable with no value is absent |
| `LoopCounters` | `IReadOnlyDictionary<string, int>` |
| `Attempt` | int (attempt of the current step, from 1) |
| `Entry` | long (value of `StepCount` when the current step was entered) |
| `StepCount`, `Sequence` | long (steps entered so far; last history sequence number) |
| `Pending` | `ProcessPending?`: `Kind` (`Effect`, `RetryDelay`, `Timer`, `Wait`, `Task`, `Child`), `Id`, `TimeoutTimerId?` |
| `Inbox` | `IReadOnlyList<BufferedEvent>` (`Name`, `Correlation?`, `PayloadJson`) |
| `Problem` | `ProcessProblem?` (`Code`, `Step`, `Detail?`) set while `Suspended` |

Triggers (`abstract record ProcessTrigger` and its sealed cases):

| Trigger | Members |
| --- | --- |
| `EffectCompleted` | `EffectId`, `OutputJson` (a JSON object text, or `"{}"`) |
| `EffectFailed` | `EffectId`, `Kind` (`ProcessErrorKind { Transient = 1, RateLimited, NotAvailable, NotEntitled, Permanent, Unknown }`), `Code`, `RetryAfter?` (`TimeSpan`) |
| `TimerFired` | `TimerId` |
| `EventReceived` | `Name`, `Correlation?`, `PayloadJson` |
| `TaskCompleted` | `TaskId`, `Outcome`, `DataJson` |
| `ChildCompleted` | `ChildKey`, `Result`, `OutputJson` |
| `ChildFailed` | `ChildKey`, `Code` |
| `CancelRequested` | `Reason` |
| `ResumeRequested` | (none) |

Requests (`abstract record ProcessRequest` and its sealed cases), all carrying `Step`:

| Request | Members |
| --- | --- |
| `EffectRequest` | `EffectId`, `IdempotencyKey`, `EffectKind` (`Read`, `Action`, `Ai`), `Port`, `InputJson`, `Attempt`, `Delivery` (`AtLeastOnce`, `AtMostOnce`; `AtLeastOnce` for read and ai), and for ai `Model`, `PromptId`, `PromptVersion` (null otherwise) |
| `TimerRequest` | `TimerId`, `DueAt` (`DateTimeOffset`, UTC) |
| `CancelTimerRequest` | `TimerId` |
| `WaitRequest` | `WaitId`, `Event`, `Correlation?` |
| `CancelWaitRequest` | `WaitId` |
| `TaskRequest` | `TaskId`, `Queue`, `Outcomes`, `ShowJson` (object of the `show` variables that have a value) |
| `CancelTaskRequest` | `TaskId` |
| `ChildStartRequest` | `ChildKey`, `ProcessId`, `InputJson` |

`ProcessHistoryEntry`: `Sequence` (long), `At` (`DateTimeOffset`), `Step` (string?), `Entry` (long), `Attempt` (int), `Kind` (`ProcessHistoryKind`), `Code` (string?), `DataJson` (string?). Kinds: `Started`, `StepEntered`, `EffectRequested`, `EffectCompleted`, `EffectFailed`, `RetryScheduled`, `Decided`, `TaskRequested`, `TaskCompleted`, `WaitStarted`, `EventConsumed`, `EventBuffered`, `TimerStarted`, `TimerFired`, `TimedOut`, `LoopIteration`, `LoopExhausted`, `ChildStarted`, `ChildCompleted`, `ChildFailed`, `Suspended`, `Resumed`, `Cancelled`, `Completed`.

For `Decided`, `DataJson` is the provenance object `{"rule":<id>,"version":<n>,"decided":<bool>,"matchedRowKeys":[..],"outcome":<outcome JSON or null>,"factsHash":<hex>,"facts":<facts JSON>}`. For `EffectCompleted` of an `ai` step it is `{"model":..,"promptId":..,"promptVersion":..,"inputHash":<ContentHash of InputJson UTF-8>,"output":<output JSON>}`. Other kinds: the smallest object that explains the entry, or null. History never contains a value the state or the trigger did not contain.

### 3.1 Identifiers (deterministic)

With `E` = `Entry` of the current step and `A` = `Attempt`:

- `IdempotencyKey` = `{InstanceId}/{step}/{E}`
- `EffectId` = `{InstanceId}/{step}/{E}/{A}`
- `TimerId` = `{InstanceId}/{step}/{E}/timer`, retry delay `{InstanceId}/{step}/{E}/retry/{A}`, timeout `{InstanceId}/{step}/{E}/timeout`
- `WaitId` = `{InstanceId}/{step}/{E}/wait`; `TaskId` = `{InstanceId}/{step}/{E}/task`; `ChildKey` = `{InstanceId}/{step}/{E}/child`

## 4. Start

`Start(instanceId, input, clock)`: `instanceId` non-empty and without `/` (else `ArgumentException`). `input` members must be declared variables with `input: true` and valid values, otherwise the result is `Rejected` with code `process.input.unknown` or `process.variable.type_mismatch` and a state with `Status = Cancelled`, no requests, no history. Otherwise variables are set from `initial` then `input`, a `Started` entry is written, and the instance enters the `start` step and runs (section 5).

## 5. The run loop

Entering a step: increment `StepCount`; if it exceeds `limits.maxSteps` the instance is suspended with `process.limits.max_steps`. Set `CurrentStep`, `Entry = StepCount`, `Attempt = 1`, `Stage` (when the step declares one; otherwise unchanged), write `StepEntered`. Then execute the step (section 6). A step either continues to another step immediately or leaves a `Pending` item and stops with `Status = Waiting`. The loop is iterative, never recursive.

`Advance` checks, in order:

1. `state.DefinitionId`, `DefinitionVersion`, `DefinitionHash` must equal the process's: otherwise `Rejected`, `process.definition.mismatch`, state unchanged.
2. `CancelRequested`: if `Completed` or `Cancelled` → `Ignored`, `process.trigger.stale`. Otherwise emit the cancel request for the pending timer, wait or task and for a timeout timer (nothing for an effect or a child), write `Cancelled`, `Status = Cancelled`, `CurrentStep = null`, `Pending = null`.
3. `ResumeRequested`: only when `Suspended` (otherwise `Ignored`, `process.trigger.stale`). When `StepCount` is already at or above `limits.maxSteps` the result is `Rejected` with `process.limits.max_steps` and the state is unchanged, so a host that resumes blindly cannot loop. Otherwise: clear `Problem`, write `Resumed`, increment `StepCount`, set `Entry = StepCount` (so every identifier is fresh) and `Attempt = 1`, do **not** write `StepEntered`, and execute the current step again. Loop counters are not changed by the resume itself (a resumed `loop` step counts one more arrival).
4. `EventReceived`: section 6.6.
5. Any other trigger must match `Pending` (kind and id, or the `TimeoutTimerId`): otherwise `Ignored`, `process.trigger.stale`, state unchanged, no requests, no history. This makes redelivery of a result harmless.
6. If `Status` is not `Waiting` → `Ignored`, `process.trigger.stale`.

`Ignored` and `Rejected` results return the input state unchanged with empty requests and history.

## 6. Step semantics

### 6.1 `read`, `action`

Build `InputJson`: an object with one member per `input` entry whose variable has a value. Emit `EffectRequest`, write `EffectRequested`, `Pending = Effect(EffectId)`.

- `EffectCompleted`: parse `OutputJson` (must be an object, else treat as `EffectFailed(Permanent, "process.effect.output_malformed")`). For each `output` entry: a present, non-null member is assigned to the variable after type validation; a missing or null member clears the variable. A type mismatch is treated as `EffectFailed(Permanent, "process.variable.type_mismatch")` and assigns nothing. Write `EffectCompleted`, go to `next`.
- `EffectFailed`, write `EffectFailed` with the kind and code, then:
  - `Transient` or `RateLimited`, and `Attempt < retry.maxAttempts`: delay = `initial` (fixed) or `initial × 2^(Attempt−1)` (exponential), capped by `max` when present; for `RateLimited` with `RetryAfter`, the larger of the two. Emit `TimerRequest` (retry delay id, `DueAt = Now + delay`), write `RetryScheduled`, `Pending = RetryDelay(id)`. When that timer fires: `Attempt += 1`, emit a new `EffectRequest`.
  - `NotAvailable` → `onNotAvailable` when present; `NotEntitled` → `onNotEntitled` when present; `Unknown` → `onUnknown` when present.
  - every remaining case (retries exhausted, `Permanent`, a kind with no dedicated route) → `onError` when present, otherwise suspend with the failure's code.

`Unknown` is never retried, for any delivery mode.

### 6.2 `ai`

Build `InputJson` as in 6.1 and validate it against `inputSchema`; invalid → handled as `EffectFailed(Permanent, "process.ai.input_invalid")` without emitting a request. Otherwise emit `EffectRequest` (kind `Ai`, with model and prompt).

- `EffectCompleted`: validate the output object against `outputSchema`. Valid: assign `output` as in 6.1, write `EffectCompleted` with the ai provenance, go to `next`. Invalid (or a type mismatch on assignment): write `EffectFailed` with code `process.ai.output_invalid`; if `Attempt < maxAttempts`, `Attempt += 1` and emit a new request immediately; otherwise go to `onInvalid`.
- `EffectFailed`: as 6.1 (`retry` governs transport failures; `maxAttempts` governs invalid outputs; both share `Attempt`).

### 6.3 `decide`

Build the facts: for each `facts` entry whose variable has a value, convert it to the `DecisionValue` of the fact's type (`Instant` and `Json` variables can never be mapped; the static rule `process.variable.type` already forbids it). Call `BrainRule.Evaluate(facts, clock.Today)`. Write `Decided` with the provenance.

- A `DecisionEngineException` (or any `BrainException`) → write `Decided` with `Code` = the exception's `ErrorCode` and `DataJson` = `{"rule":<id>,"version":<n>,"error":<code>}`, then `onError` when present, else suspend with that code.
- `NoDecision` → `onNoDecision`.
- Form (a): assign `output` from the outcome object (missing or null member clears the variable); then `next`, or look up the `route` field's value in `on` (Boolean as `"true"`/`"false"`); a value with no entry → `onError` when present, else suspend with `process.route.unmatched`.
- Form (b): store the outcome JSON array in `collectInto`, go to `next`.

### 6.4 `humanTask`

Emit `TaskRequest`; with `timeout` also `TimerRequest` (timeout id, `DueAt = Now + timeout`). `Pending = Task(TaskId, TimeoutTimerId)`.

- `TaskCompleted`: `Outcome` must be one of `outcomes`, else `Rejected` with `process.task.outcome_unknown` (state unchanged). Assign `output` from `DataJson` as in 6.1 (a type mismatch → `Rejected`, `process.variable.type_mismatch`, state unchanged, so the task can be completed again with valid data). Emit `CancelTimerRequest` for the timeout timer, write `TaskCompleted`, go to `on[Outcome]`.
- Timeout timer fired: emit `CancelTaskRequest`, write `TimedOut`, go to `onTimeout`.

### 6.5 `timer`

`DueAt` = `Now + after`, or the `until` variable's instant plus `offset`. If the `until` variable has no value: `onError` does not exist on timers, so suspend with `process.timer.until_missing`. Always emit `TimerRequest` (even when `DueAt` is in the past), write `TimerStarted`, `Pending = Timer(id)`. On `TimerFired`: write `TimerFired`, go to `next`.

### 6.6 `wait` and events

Entering a `wait` step: compute the correlation value (the `correlation` variable's value as a string, or null; a declared correlation variable with no value suspends with `process.wait.correlation_missing`). If `Inbox` holds an event with the same name and an equal correlation (when the step declares one), remove the **oldest** such event and consume it at once (below). Otherwise emit `WaitRequest`, and with `timeout` a `TimerRequest`; `Pending = Wait(WaitId, TimeoutTimerId)`.

Consuming an event: assign `output` from the payload as in 6.1; a type mismatch goes to suspension with `process.variable.type_mismatch`. Emit `CancelTimerRequest` for a timeout timer and write `EventConsumed`, go to `next`.

`EventReceived` trigger:

1. Instance `Completed` or `Cancelled` → `Ignored`, `process.trigger.stale`.
2. Instance waiting on a `wait` step with this event name, and the step declares no correlation or the trigger's `Correlation` equals the computed value → consume it.
3. Otherwise, if `accepts` lists the name → append to `Inbox`, write `EventBuffered`, `Applied`, nothing else changes (also while `Suspended`).
4. Otherwise → `Rejected`, `process.event.not_accepted`.

Timeout timer fired: emit `CancelWaitRequest`, write `TimedOut`, go to `onTimeout`.

De-duplication of redelivered external events is the host's job (W1-B, by event id); the interpreter consumes what it is given.

### 6.7 `loop`

A `loop` step is a bounded gate. Each time control arrives: `n = LoopCounters[step] + 1` (starting from 0), stored.

- With `forEach`: read the variable; no value or not an array → suspend with `process.loop.not_a_list`. If `n` > array length → write nothing more, go to `next`. 
- If `n > maxIterations` → write `LoopExhausted`, go to `onExhausted`.
- Otherwise, with `forEach` set `item` to element `n−1`; write `LoopIteration` (`{"iteration": n}`), go to `body`.

Order of the two checks: the `forEach` completion check first, then `maxIterations`. Counters never reset within an instance. Because every cycle contains a `loop` step, an instance always terminates in a bounded number of steps.

### 6.8 `subprocess`

Emit `ChildStartRequest` (`InputJson` built as in 6.1), write `ChildStarted`, `Pending = Child(ChildKey)`.

- `ChildCompleted`: assign `output` from `OutputJson` as in 6.1 (type mismatch → `onError` or suspend); write `ChildCompleted`; go to `next`, or `on[Result]`; a result with no entry → `onError` when present, else suspend with `process.route.unmatched`.
- `ChildFailed`: write `ChildFailed`; `onError` when present, else suspend with the code.

### 6.9 `end`, suspension

`end`: `Status = Completed`, `Result`, `CurrentStep = null`, write `Completed`.

A payload that is not parseable JSON or not an object is treated as a type mismatch of its step: read and action output → `EffectFailed(Permanent, "process.effect.output_malformed")`; ai output → `process.ai.output_invalid`; task data → `Rejected`, `process.variable.type_mismatch`; wait payload → suspend with `process.variable.type_mismatch` (a buffered event with a bad payload is `Rejected` with the same code when it arrives); child output → `ChildFailed` then `onError` or suspend. Cancelling during a retry delay emits `CancelTimerRequest` for the retry timer.

Suspension: `Status = Suspended`, `Problem = (code, step, detail)`, `Pending = null`, write `Suspended`. Outstanding timeout timers are cancelled with a `CancelTimerRequest`. The instance stays on its step; `ResumeRequested` runs it again.

## 7. Value conversions

Variable JSON to `DecisionValue`: `Code` → `DecisionValue.Code`, `Enum` → `Enum`, `Boolean` → `Boolean`, `Integer` → `Integer`, `Decimal` → `Decimal` (invariant culture), `Money` → `Money(amount, currency)`, `Date` → `Date`, `Reference` → `Reference(Guid)`.

Outcome JSON to a variable: the outcome object member is a plain JSON value in the forms of section 2.2 for the outcome field's type, with two exceptions that `Synapbiz.Brain.Spec` writes as JSON numbers: a `Decimal` outcome is a JSON number and a `Money` outcome is `{ "amount": <number>, "currency": .. }`. For these the variable receives the number's **raw JSON lexeme** as its string (no parsing to a binary floating type); a lexeme that does not match the `Decimal` pattern of section 2.2 (for example exponent notation) is a type mismatch.

## 8. Scenarios

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

The runner keeps a virtual clock (starting at `start.at`, with `Today = start.today + floor((Now − start.at) / 24h)` days) and the set of outstanding timers from `TimerRequest` / `CancelTimerRequest`. `advance` moves the clock forward by the duration, firing each due timer in `DueAt` order (ties by `TimerId` ordinal) at its own `DueAt`, including timers created by those firings that fall inside the window. `complete` and `fail` address the pending effect of the named step; `task` the pending task; `child`/`childFailed` the pending child. An event that does not match the pending work, or a `Rejected` result, fails the scenario. `expect.trace` is the exact ordered list of steps of all `StepEntered` entries; `expect.variables` is a subset compared by JSON equality; `expect.status` is required, `result`, `trace` and `variables` are optional.

`ScenarioRunResult`: `IsOk`, `Results` (`Name`, `Passed`, `Failure?` text). A process with no scenarios returns `IsOk = true` and no results.

## 9. Tests (all required)

Project `tests/Synapbiz.Brain.Process.Tests` (xunit, FluentAssertions), fixtures under `tests/fixtures/brain-process/`:

1. A conformance file `conformance.json`: at least one valid document per step kind and at least one invalid document per code of section 2.4 and per structure code, each with the expected `(code, path)` list. A theory runs them all and a test asserts that every code of section 2.4 is covered.
2. Interpreter unit tests for every branch of sections 4 to 6, including: stale and duplicate triggers ignored with unchanged state; retry delays for fixed and exponential backoff with cap and with `RetryAfter`; `Unknown` never retried and routed to `onUnknown`; ai invalid output retried then `onInvalid`; buffered event consumed on entering a wait; event rejected when not accepted; loop exhaustion; `forEach` completion; task outcome rejected; cancel while each pending kind is outstanding; resume after suspension; `maxSteps`.
3. State round trip: for the state after every step of the reference scenarios, `FromJson(ToJson(s))` equals `s` and `ToJson` is stable.
4. Determinism: running the same trigger sequence twice gives identical states, requests and history (compare `ToJson` and serialized requests).
5. A reference process `outreach.process.json` (synthetic: read missing items, decide with a synthetic rule and contract, bounded reminder loop, at-most-once action, wait with timeout, human task) whose embedded scenarios cover: nothing missing; reply after the first reminder; two reminders then escalation; unknown delivery routed to a task; transient failure retried. `RunScenarios().IsOk` is asserted, plus a negative test where an altered expectation fails.
6. Provenance: a `Decided` entry carries rule id, version, matched row keys, facts hash and facts.

Architecture tests (in `tests/Synapbiz.Brain.ArchitectureTests`): add `Process` to the purity scan's library list; a new coupling test that `Synapbiz.Brain.Process` references only the base class library, the three Brain libraries and the JsonSchema.Net assemblies, and that its project file declares exactly the `JsonSchema.Net` package and the `Synapbiz.Brain.Spec` project reference.

## 10. Gates

`dotnet build Synapbiz.Brain.sln -c Release`: 0 errors and **no warning in any new or touched file** (the base has 2 warnings; the count must stay 2). No `NoWarn`, no disabled analyzers, no `#pragma warning disable`; a `SuppressMessage` needs a written justification and is allowed only where the existing code base already suppresses the same rule for the same reason. `dotnet test Synapbiz.Brain.sln -c Release --no-build --collect:"XPlat Code Coverage"`: all green, the 402 existing tests unchanged and passing. Line coverage of `Synapbiz.Brain.Process` ≥ 92 %; coverage of the three existing libraries unchanged. `dotnet pack` produces four packages. `bash tools/parity/check-source-drift.sh` is unaffected (no file under the three existing `src` projects changes).

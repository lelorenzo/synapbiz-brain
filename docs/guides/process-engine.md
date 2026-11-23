# Running processes: the consumer guide

How an application uses the process engine end to end: write the documents, register the PostgreSQL host, implement the ports, start cases, let people complete their tasks, show the state in a cockpit and operate the whole. The pages it points to are the references; this one is the path through them. Every C# block below is a region of `tests/Synapbiz.Brain.Host.Postgres.Tests/GuideExamples.cs`, which compiles with the solution, and a test (`GuideTests`) fails when the guide and that file drift apart. Everything in the examples is synthetic.

## 1. What the engine is, and what it is not

It is a library that runs long, durable cases: a document in the `brain.process@1` format says what happens (read something, decide with a rule, contact somebody inside their contact hours, wait for an event or a person, start a child process), the interpreter turns each trigger into a state transition, and the PostgreSQL host stores the state and the history, calls the consumer's ports, keeps the timers and survives crashes and several workers.

It is not:

- a user interface. It stores human tasks and completes them when asked; the screens are the consumer's.
- a set of connectors. Every contact with the outside world is a port the consumer implements.
- an HTTP service or a message broker. Events are published by a method call; the host has no endpoint of its own.
- a place that decides. A process observes through read ports and hands every judgement to a rule (`brain.rule@1`) or to a human task. The reference flows never submit or change a claim and never write to a record.
- a retention policy. It deletes a case when asked and reports what it removed (section 11), but the rule and the dates are the consumer's, and nothing is deleted unless the consumer calls the commands.

## 2. Packages

| Package | What for | Reference it when |
| --- | --- | --- |
| `Synapbiz.Brain.Process` | The `brain.process@1` loader, the pure interpreter and the scenario runner | You load documents (every consumer) |
| `Synapbiz.Brain.Host.Postgres` | The durable host: store, pump, worker, migrator, `IProcessEngine` | You run instances (every consumer) |
| `Synapbiz.Brain.Spec` | `brain.rule@1` rules and module contracts | You load a rule a process names in `uses` (it comes with the two above) |
| `Synapbiz.Brain.Contracts`, `Synapbiz.Brain.Engine` | The decision tables and the evaluator under the rules | Transitively; nothing to call |
| `Synapbiz.Brain.Workbench`, `Synapbiz.Brain.Workbench.Blazor` | Reading, simulating and reviewing rules | You give people a rule workbench; the process engine does not need them |

The host needs PostgreSQL 14 or later. How to get the packages from the feed, and how a release is cut, is in [consuming.md](../consuming.md). All seven packages carry the same version.

## 3. Writing a process

The format is in [brain-process-v1](../reference/brain-process-v1.md): variables, the eleven step kinds, routes for every failure kind, triggers, delivery windows and scenarios. The documents to start from are the six in [samples/process-reference](../../samples/process-reference/README.md): four stages of one case (`previsit.outreach`, `visit.note_wait`, `postvisit.coding_review`, `collection.followup`), the root that runs them (`case.lifecycle`) and a schedule (`practice.daily_refresh`). Copy a folder and change the names, the thresholds and the queues. The README of that folder lists every port, event, human task queue, rule and result.

A process folder holds `process.json`, the module contract of its rules and the rules themselves. Loading is the consumer's job, and a document that does not load is reported with its codes and paths, never half-loaded:

```csharp
internal static class Documents
{
    /// <summary>Loads one process from its folder: the rules next to it (with the contract) and the ids of the processes it may start.</summary>
    internal static BrainProcess Load(string folder, IReadOnlyCollection<string> knownProcessIds)
    {
        var contractPath = Path.Combine(folder, "contract.json");
        var contracts = File.Exists(contractPath) ? new[] { BrainContract.Parse(File.ReadAllText(contractPath)) } : [];
        var rules = Directory.GetFiles(folder, "*.rule.json")
            .Select(path => BrainRuleLoader.Load(File.ReadAllText(path), contracts).Rule ?? throw new InvalidOperationException(path + " does not load."))
            .ToList();
        var loaded = BrainProcessLoader.Load(File.ReadAllText(Path.Combine(folder, "process.json")), new ProcessLoadContext(rules, knownProcessIds));
        return loaded.Process ?? throw new InvalidOperationException(folder + ": " + string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path)));
    }
}
```

Three habits the reference flows follow, worth keeping:

- Put the scenarios in the document (`RunScenarios()` runs them on the pure interpreter without a database) and make a test fail when a route of a branching step is never taken. The tests of the reference documents do that.
- Name every route of every failing step. A step with no `onError` suspends the instance when its port fails; with one, the failure goes to a person or another step.
- A contact action is `atMostOnce`, carries `contact: true` and a delivery window, and routes `onUnknown` to a human task (section 9).

## 4. Registering the host

The registration adds the engine, the pump, the migrator, the store and a hosted worker. The consumer registers a catalog, its ports, a payload protector and, if the day of a tenant is not the UTC day, a calendar. The options are validated when first resolved, and resolving the engine without a protector throws.

```csharp
internal static class CaseProcessesRegistration
{
    internal static IServiceCollection AddCaseProcesses(this IServiceCollection services, string connectionString, string documentsFolder)
    {
        services.AddSingleton<IProcessCatalog>(_ => new ListCatalog(Directory.GetDirectories(documentsFolder).Select(folder => Documents.Load(folder, ["previsit.outreach", "visit.note_wait", "postvisit.coding_review", "collection.followup"]))));
        services.AddSingleton<IProcessPort, RequirementsPort>();
        services.AddSingleton<IProcessPort, ReminderPort>();
        services.AddSingleton<IProcessPort, SuggestPort>();
        services.AddSingleton<IProcessCalendar, PracticeCalendar>();
        services.AddSingleton<IPayloadProtector, TenantKeyProtector>();
        return services.AddBrainProcessHostPostgres(
            store =>
            {
                store.ConnectionString = connectionString;
                store.Schema = "brain";
            },
            host =>
            {
                host.PollInterval = TimeSpan.FromSeconds(5);
                host.BatchSize = 50;
                host.MaxConcurrentEffects = 8;
                host.PortTimeout = TimeSpan.FromSeconds(60);
                host.EffectLease = TimeSpan.FromSeconds(120);
                host.ScheduleMisfireGrace = TimeSpan.FromMinutes(5);
                host.PurgeLockTimeout = TimeSpan.FromSeconds(10);
            });
    }
}
```

| Option | Default | Meaning |
| --- | --- | --- |
| `PollInterval` | 5 s | How long the worker waits after a pass that handled nothing; after a pass that did, it runs again at once |
| `BatchSize` | 50 | Items of each kind one pass handles (1 to 1000) |
| `MaxConcurrentEffects` | 8 | Port calls running at once in one pump (1 to 256) |
| `PortTimeout` | 60 s | How long a port call may take before the host gives up on it |
| `EffectLease` | 120 s | How long a claimed effect stays claimed; it must be longer than `PortTimeout` |
| `ScheduleMisfireGrace` | 5 min | How late a schedule occurrence may be handled and still count as on time |
| `PurgeLockTimeout` | 10 s | How long `PurgeInstanceAsync` keeps trying to lock a tree that another transaction holds (greater than zero, at most 5 minutes); then the result is `Refused` with `process.purge.busy` |

`PostgresProcessStoreOptions.Schema` is the schema that holds the tables (`^[a-z_][a-z0-9_]{0,62}$`, default `brain`). Time comes from the registered `TimeProvider` (the system clock by default); the host never reads another clock.

**The migrator.** The worker never migrates. Run the migrator once at start-up, before the host starts working (`await Startup.MigrateAsync(app.Services)` in a generic host, before `RunAsync`). Two processes may call it at once and a second run changes nothing:

```csharp
internal static class Startup
{
    /// <summary>Called once, before the worker starts handling work: creates the schema and the tables, or does nothing when they are there.</summary>
    internal static Task MigrateAsync(IServiceProvider services) =>
        services.GetRequiredService<IProcessStoreMigrator>().MigrateAsync();
}
```

**Time zones.** Delivery windows and schedules name IANA zones (`America/New_York`). The host resolves them through the time zone database of the machine, so a container image needs its tzdata package; an id the machine does not know is a failure the process routes (`process.window.zone_unknown`), not a crash. The resolver is internal: a consumer cannot replace it, and the tests of the host inject zones through `InternalsVisibleTo`.

## 5. The ports to implement

| Port | One per | What it does |
| --- | --- | --- |
| `IProcessCatalog` | host | Finds the `BrainProcess` objects the host runs |
| `IProcessPort` | port name | Runs the `read`, `action`, `ai` and `poll` steps that name it |
| `IProcessCalendar` | host (optional) | Tells the day of a tenant, for the rules |
| `IPayloadProtector` | host | Encrypts what the host stores |

### The catalog

`Latest` is asked when an instance starts and when a child is started. `Find` is asked for every later trigger of a running instance, with the version and the content hash the instance is pinned to, so an instance always meets the definition it began with. Return null for a definition you no longer have: the host leaves the item alone, logs `process.definition.unavailable` and tries again on a later pass. Keep every version that still has a running instance.

```csharp
internal sealed class ListCatalog(IEnumerable<BrainProcess> processes) : IProcessCatalog
{
    private readonly List<BrainProcess> all = [.. processes];

    public BrainProcess? Latest(string processId) =>
        all.Where(process => process.Id == processId).MaxBy(process => process.Version);

    public BrainProcess? Find(string processId, long version, string contentHash) =>
        all.FirstOrDefault(process => process.Id == processId && process.Version == version && process.ContentHash == contentHash);
}
```

### A read port

A `read` or `poll` step builds its input from its `input` mapping and maps the members of the output it names. A practice that has no connector answers `NotAvailable`, one whose plan does not include the port answers `NotEntitled`; both are routes of the step, not errors.

```csharp
internal sealed class RequirementsPort(IRequirementsGateway gateway) : IProcessPort
{
    public string Name => "intake.requirements";

    public async Task<PortResult> InvokeAsync(PortCall portCall, CancellationToken cancellationToken)
    {
        var caseRef = JsonNode.Parse(portCall.Request.InputJson)!["case"]!.GetValue<string>();
        var missing = await gateway.CountMissingAsync(portCall.TenantId, caseRef, cancellationToken);
        return missing is null
            ? new PortFailed(ProcessErrorKind.NotAvailable, "intake.no_connector")
            : new PortCompleted(new JsonObject { ["missing_count"] = missing }.ToJsonString());
    }
}
```

### An action port

The idempotency key is the same on every delivery of the same step entry. Pass it to the system that sends, so that a second delivery after a crash is recognised there. A port returns `PortCompleted` with an output object, or `PortFailed` with one of `Transient`, `RateLimited` (with `RetryAfter`), `NotAvailable`, `NotEntitled` or `Permanent`; the retry policy of the step and its `onError` routes decide what happens next. Honour the cancellation token: it fires after `PortTimeout` and when the pump stops.

```csharp
internal sealed class ReminderPort(IMessagingGateway gateway) : IProcessPort
{
    public string Name => "messaging.reminder";

    public async Task<PortResult> InvokeAsync(PortCall portCall, CancellationToken cancellationToken)
    {
        var input = JsonNode.Parse(portCall.Request.InputJson)!;

        // The idempotency key is the same on every delivery of this step entry: hand it to the system that sends.
        var answer = await gateway.SendAsync(portCall.Request.IdempotencyKey, portCall.TenantId, input["case"]!.GetValue<string>(), cancellationToken);
        return answer switch
        {
            SendAnswer.Sent => new PortCompleted(new JsonObject { ["receipt"] = portCall.Request.IdempotencyKey }.ToJsonString()),
            SendAnswer.Busy => new PortFailed(ProcessErrorKind.RateLimited, "messaging.busy", TimeSpan.FromMinutes(1)),
            SendAnswer.NoChannel => new PortFailed(ProcessErrorKind.NotAvailable, "messaging.no_channel"),
            _ => new PortFailed(ProcessErrorKind.Permanent, "messaging.refused"),
        };
    }
}
```

### An ai port

An `ai` step names a model, a prompt id and a prompt version, and declares the schemas of its input and output. The host passes them in the request; what a model id means is the consumer's business. An answer that does not satisfy the output schema is not an error of the port: the step routes `onInvalid`, and `maxAttempts` bounds the retries. The reference flow keeps the suggestion apart from the decision: it only ever proposes a code, and a physician reviews it.

```csharp
internal sealed class SuggestPort(IAssistantGateway assistant) : IProcessPort
{
    public string Name => "assist.suggest";

    public async Task<PortResult> InvokeAsync(PortCall portCall, CancellationToken cancellationToken)
    {
        // The step names the model and the prompt; the consumer maps them to its own deployment.
        var request = portCall.Request;
        var suggestion = await assistant.SuggestAsync(request.Model!, request.PromptId!, request.PromptVersion!.Value, request.InputJson, cancellationToken);
        return suggestion is null
            ? new PortFailed(ProcessErrorKind.Transient, "assist.no_answer")
            : new PortCompleted(new JsonObject { ["code"] = suggestion.Code, ["confidence"] = suggestion.Confidence }.ToJsonString());
    }
}
```

### The calendar

Rules are evaluated for a day. The default calendar is the UTC date; a consumer whose tenants live in other zones gives the date of the tenant.

```csharp
internal sealed class PracticeCalendar(IPracticeDirectory directory) : IProcessCalendar
{
    public DateOnly Today(string tenantId, DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, directory.ZoneOf(tenantId)).DateTime);
}
```

### The protector

The instance state, the history data, the input of effects, the values a task shows, the payloads of the inbox and the activation input pass through `Protect` before they are written and `Unprotect` after they are read. There is no silent default: register a real protector, or call `AddPlaintextProcessPayloads()` in development and tests only. The example binds each text to its tenant, so a value never opens under another tenant's key.

```csharp
internal sealed class TenantKeyProtector(IKeyStore keys) : IPayloadProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string Protect(string tenantId, string plaintext)
    {
        var clear = Encoding.UTF8.GetBytes(plaintext);
        var packed = new byte[NonceSize + TagSize + clear.Length];
        var nonce = packed.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(keys.KeyOf(tenantId), TagSize);

        // The tenant id is authenticated with the text: a value written for one tenant never opens for another.
        aes.Encrypt(nonce, clear, packed.AsSpan(NonceSize + TagSize), packed.AsSpan(NonceSize, TagSize), Encoding.UTF8.GetBytes(tenantId));
        return Convert.ToBase64String(packed);
    }

    public string Unprotect(string tenantId, string protectedText)
    {
        var packed = Convert.FromBase64String(protectedText);
        var clear = new byte[packed.Length - NonceSize - TagSize];
        using var aes = new AesGcm(keys.KeyOf(tenantId), TagSize);
        aes.Decrypt(packed.AsSpan(0, NonceSize), packed.AsSpan(NonceSize + TagSize), packed.AsSpan(NonceSize, TagSize), clear, Encoding.UTF8.GetBytes(tenantId));
        return Encoding.UTF8.GetString(clear);
    }
}
```

## 6. Activating processes per tenant, and starting cases by event

The `triggers` of a document do nothing until the consumer activates the process for a tenant. `ActivateAsync` pins the latest version in the catalog, resolves the zone, validates the input against the `input: true` variables and writes the rows of its event triggers and schedules in one transaction. It is idempotent: the same version, zone and input again changes nothing and keeps every fire time. A new version, zone or input replaces the rows; instances that are running are not touched.

`case.lifecycle` starts on the event `case.opened`: the consumer publishes one event per case, with the case reference as the business key, and the engine starts one instance per key, so telling it twice never opens a case twice. The event id de-duplicates the event itself: use the id of the fact in your own records, and a new id only for a new fact. A publish answers `Queued`, `Duplicate` or `NoTarget`, and an id is consumed by any answer.

```csharp
internal sealed class CaseOpening(IProcessEngine engine)
{
    /// <summary>Once per tenant, at onboarding: pins the latest version of the root and gives every case the same input.</summary>
    internal Task<ActivationResult> ActivateAsync(string tenantId, string zone, bool assistantEnabled) =>
        engine.ActivateAsync(new ProcessActivation(
            tenantId,
            "case.lifecycle",
            null,
            new JsonObject { ["practice_zone"] = zone, ["assist_enabled"] = assistantEnabled }.ToJsonString()));

    /// <summary>
    /// Opens a case. The event id is the id of the fact in the consumer's own records, so that telling the engine twice is a
    /// duplicate, and the case reference is an opaque id.
    /// </summary>
    internal Task<PublishResult> OpenCaseAsync(string tenantId, string openedEventId, string caseRef) =>
        engine.PublishEventAsync(new ProcessEvent(tenantId, openedEventId, "case.opened", null, new JsonObject { ["case_ref"] = caseRef }.ToJsonString()));
}
```

**The zone is mandatory.** The delivery window of the reference pre-visit stage reads the zone of the practice from the input `practice_zone`, so every case needs it: put it in the activation input (`practice_zone`, an IANA id such as `America/New_York`, the same for every case of the tenant) or in the payload of `case.opened` (one case). A case without it never starts its reminders. The reference `case.lifecycle` checks this first, with the rule `lifecycle.zone_ready`: a missing or empty zone goes to the task `ops_zone` of the queue `operations`, before any child exists, and a person chooses `continue` (skip the reminders, go on to the visit) or `close` (end the case as `manual_close`). Watch that queue. A zone that is present but is not one the machine knows passes the check; the first reminder then fails the window with `process.window.zone_unknown` and goes to the front desk task. What the document cannot do is repair the input afterwards: an input is fixed when the instance starts, and the business key of a case is taken for good, so a case cannot be opened a second time with its zone. A consumer that copies `previsit.outreach` and starts it without the zone gets the harder case, described in section 11 (a suspended instance that nobody is told about).

Two things the reference flows leave to the consumer, because the format has no "publish event" step:

- **Opening the cases of the day.** `practice.daily_refresh` runs on its schedule (06:00 in the zone of the activation, `fireOnce`), reads the day's list of cases and ends with it in its variable `cases`. If the read fails for good it does not end silently: it raises the task `refresh_attention` in the queue `operations`, waits for a person to choose `handled`, and only then ends as `refresh_failed`, so the open task is the alert and the result alone is not. The consumer reads the finished instance (`FindInstanceAsync` with the business key of the occurrence, then `State.Variables["cases"]`; or its own read port does it as a side effect) and publishes one `case.opened` per case.
- **Telling a process that something happened.** `case.reply_received` and `claim.submitted` reach an instance only if it is waiting for them when they are published. An event that arrives while the instance is somewhere else is lost, and the timeout of the wait covers it (the next reminder, the `overdue` task). Publish them with the case reference as the correlation.

## 7. Completing human tasks from a UI

A `humanTask` step puts a task in a queue with the values it asks to `show`. The consumer lists the open tasks of a queue (oldest first), draws the values, and completes the task with one of its outcomes and the data the step declares. The queues and outcomes of the reference flows are tabulated in their README.

```csharp
internal sealed class TaskInbox(IProcessEngine engine)
{
    /// <summary>The cards of one queue, oldest first. The shown values are the only data of the case the person sees.</summary>
    internal async Task<IReadOnlyList<TaskCard>> QueueAsync(string tenantId, string queue) =>
        [.. (await engine.ListOpenTasksAsync(tenantId, queue)).Select(task => new TaskCard(task.TaskId, task.InstanceId, task.Step, task.Outcomes, JsonNode.Parse(task.ShowJson)!.AsObject(), task.CreatedAt))];

    /// <summary>What the person did, in words for the person: the engine answers Applied, Ignored (somebody else was first) or Rejected.</summary>
    internal async Task<string> CompleteAsync(string tenantId, string taskId, string outcome, string dataJson)
    {
        var result = await engine.CompleteTaskAsync(tenantId, taskId, outcome, dataJson);
        return result.Disposition switch
        {
            AdvanceDisposition.Applied => "done",
            AdvanceDisposition.Ignored => "already handled",
            _ => "refused: " + result.Code,
        };
    }
}

internal sealed record TaskCard(string TaskId, string InstanceId, string Step, IReadOnlyList<string> Outcomes, JsonObject Show, DateTimeOffset CreatedAt);
```

The answer of `CompleteTaskAsync` is `Applied`, `Ignored` (the task was already completed or cancelled: somebody else was first, or a timeout fired) or `Rejected` (an unknown outcome, data the step does not accept, or a task of another tenant, which is reported as not found). A screen shows "already handled" for `Ignored` and refreshes the list; it does not retry. The shown values are protected like every other payload and are the only data of the case the host stores for the task, so a task shows opaque references and the values a person needs to decide, not more.

## 8. What to show in a cockpit

The engine answers these questions per tenant. About one instance: by id or by process and business key (`GetInstanceAsync`, `FindInstanceAsync`), its history (`GetHistoryAsync`). About many: a page of instances newest first with filters (`ListInstancesAsync`), the number of instances per process, status and stage (`CountInstancesAsync`). About a case: the instance with all its descendants, open tasks, next timer and awaited events in one consistent read (`GetCaseAsync`), or only the direct children (`ListChildrenAsync`). And the open tasks of a queue (`ListOpenTasksAsync`). An instance reports its status (`Running`, `Waiting`, `Suspended`, `Completed`, `Cancelled`), its stage, the step it is on, its result, the problem that suspended it, its parent, its times and its full state. The history is the audit trail: every step entered, effect requested and completed or failed, decision (with the rule version and the matching rows), task, wait, timer and window, each with a sequence number and the instant.

The list, count, case and children queries return a `ProcessInstanceSummary` and never the state: they do not decrypt anything of the instance, so they are cheap enough for a screen that refreshes. A summary carries the status, stage, current step, result, problem code and times of the instance, the number of its own open tasks and the number of suspended descendants, so a row can say that a case needs a person without opening it. A created child that the pump has not started yet is reported, and filtered on, as `Running`. `ListInstancesAsync` keeps only root instances by default (`RootsOnly`), so a case is one row; `NeedsAttention` keeps the instances that are suspended, have a suspended descendant, or have an open task of their own or of a descendant. Pages are keyset pages over creation time and instance id: pass the `Next` of a page as `After` of the next call, and a page whose `Next` is null is the last. A walk sees each row at most once, but a row inserted or changed meanwhile may be missed or seen under its new values.

A cockpit row for a case is the root's stage and status, plus what the current stage is doing, and `GetCaseAsync` returns all of it: the descendants with their `ParentInstanceId` (so the tree can be rebuilt), the open tasks of the whole tree oldest first, the earliest timer due and the names of the events the tree waits for (names only, never a correlation value). The walk goes down to 16 levels.

```csharp
internal sealed class Cockpit(IProcessEngine engine)
{
    private static readonly string[] StageTrack = ["previsit", "visit", "postvisit", "collection"];

    /// <summary>One case for a cockpit row: the root, the business keys of its children in the order of the stage track and what the case waits for, read in one call.</summary>
    internal async Task<CaseView?> ViewAsync(string tenantId, string caseRef)
    {
        var root = await engine.FindInstanceAsync(tenantId, "case.lifecycle", caseRef);
        var view = root is null ? null : await engine.GetCaseAsync(tenantId, root.InstanceId);
        if (view is null)
        {
            return null;
        }

        var instance = view.Instance;
        return new CaseView(instance.Stage, instance.Status, instance.CurrentStep, instance.Result, instance.ProblemCode, view.OpenTasks.Count, view.NextTimerDue, [.. view.Descendants.OrderBy(child => Array.IndexOf(StageTrack, child.Stage)).Select(child => child.BusinessKey)]);
    }

    /// <summary>The cases that need a person: suspended, with a suspended child, or with an open task, newest first, page by page.</summary>
    internal async Task<IReadOnlyList<ProcessInstanceSummary>> NeedsAttentionAsync(string tenantId)
    {
        var rows = new List<ProcessInstanceSummary>();
        string? after = null;
        do
        {
            var page = await engine.ListInstancesAsync(new ProcessInstanceQuery(tenantId, ProcessId: "case.lifecycle", NeedsAttention: true, Limit: 100, After: after));
            rows.AddRange(page.Items);
            after = page.Next;
        }
        while (after is not null);
        return rows;
    }

    /// <summary>How many open cases are in each stage.</summary>
    internal async Task<IReadOnlyDictionary<string, int>> OpenCasesPerStageAsync(string tenantId)
    {
        var counts = await engine.CountInstancesAsync(tenantId, "case.lifecycle", rootsOnly: true);
        return counts
            .Where(count => count.Status is not (ProcessStatus.Completed or ProcessStatus.Cancelled))
            .GroupBy(count => count.Stage ?? "(none)")
            .ToDictionary(group => group.Key, group => group.Sum(count => count.Count));
    }

    /// <summary>For an operator: says why an instance is suspended and, once the cause is fixed, runs the step that stopped it again.</summary>
    internal async Task<string?> ResumeAsync(string tenantId, string instanceId)
    {
        var instance = await engine.GetInstanceAsync(tenantId, instanceId);
        if (instance is not { Status: ProcessStatus.Suspended })
        {
            return null;
        }

        var problem = instance.Problem!.Code + " at " + instance.Problem.Step;
        await engine.ResumeAsync(tenantId, instanceId);
        return problem;
    }
}

internal sealed record CaseView(string? Stage, ProcessStatus Status, string? CurrentStep, string? Result, string? ProblemCode, int OpenTasks, DateTimeOffset? NextTimerDue, IReadOnlyList<string> ChildKeys);
```

Useful things to draw: a stage track from the `stages` of the document and `Stage`; a timeline from the history; the open task and its queue; the problem code of a suspended instance; the counts per stage as a header. What the engine still does not give you, because it is outside this release, is reopening a finished case, and counts over time (a purged case leaves no trace in the tables): for those keep your own index (the `process.instance.finished` and `process.instance.suspended` log events, or a row you write when you open a case).

## 9. Delivery guarantees, and what to do with `Unknown`

A state change is exactly-once per trigger: it is applied in one transaction that also consumes the item that carried it. A port call cannot be part of that transaction, so its guarantee is the `delivery` mode of the step:

| Mode | After a crash between the claim and the result |
| --- | --- |
| `atLeastOnce` | The port is called again with the same effect id and idempotency key. The port must dedupe on the key. |
| `atMostOnce` | The port is not called again. The host records the failure kind `Unknown`, code `process.effect.outcome_unknown`, and the step takes its `onUnknown` route. |

For `atMostOnce` the system does not know whether the message went out, and it never sends it a second time. A contact is the case for it: a second reminder to a person is worse than a missed one. The reference flows route `onUnknown` to a human task whose person checks the channel's own record and either goes on or closes the case. A port that throws or does not answer in time is a `Transient` failure for `atLeastOnce` and an `Unknown` for `atMostOnce`. Do not turn `Unknown` into a retry in the port: that is the one thing the mode exists to prevent.

A delivery window is applied when its timer is stored and is not checked again when the timer fires: a pump pass that runs late can send after the window closed. Choose `atMostOnce` when that is worse than a missed contact. The details are in the [host reference](../reference/process-host-postgres.md).

## 10. Data protection

- Everything the host stores that a person could be identified from goes through `IPayloadProtector`: the instance state, the history data, the input of effects, the values a task shows, the inbox payloads and the activation input.
- Some columns are plain text because the host queries them: the business key, the correlation value of a wait, the ids, the step names and the codes, and the code a port returns in `PortFailed`. Use opaque references (`c-1001`, never a name or a number a person owns) as business keys and correlation values, and plain codes as failure codes.
- The log never carries a variable, an input, an output, a payload, a business key or the message of an exception: a port that throws is logged by the exception type. It does carry tenant ids, instance ids, step names, ids of effects, timers and tasks, kinds and codes (section 11).
- Tests use synthetic data. The reference flows use `c-1001` and `C-100` and a placeholder model id, and the tests check that nothing a sentinel value went through shows up in a log record or in a protected column.
- Retention is the consumer's rule: finished instances, their history and their tasks stay until the consumer calls a purge command (section 11), and what a deleted row still leaves in vacuum, the write-ahead log and backups is described in the host reference ("What remains after a purge"). The clear columns (business key, correlation, ids, codes) are why those must be opaque references.

## 11. Operations

**The pump.** The hosted worker calls `IProcessPump.RunOnceAsync` in a loop. A pass handles, in this order and up to `BatchSize` of each: due schedules, expired effect leases, the inbox, due timers, pending effects. Several workers, in one process or in many, may run against one schema; they take instance rows with `for update skip locked` and cannot start the same schedule occurrence twice. A pass that throws is logged (the exception type only) and the loop goes on after `PollInterval`. The latency of a timer or an event is therefore the poll interval plus the time of the pass.

**Leases.** The pump claims an effect with a lease of `EffectLease` before it calls the port. If it dies in between, a later pass finds the lease expired and applies the delivery mode (section 9). Keep `PortTimeout` shorter than `EffectLease`, and shorter than the slowest port you can tolerate.

**Misfire.** A schedule occurrence handled within `ScheduleMisfireGrace` of its time is on time. Later (the host was down), the schedule's policy decides: `skip` drops it, `fireOnce` fires the last missed occurrence once. The host never starts one instance per missed occurrence.

**Log events.** Stable messages, with ids, kinds and codes only:

| Message | Level | Meaning and what to do |
| --- | --- | --- |
| `process.definition.unavailable` | Warning | The catalog lacks a definition an instance or schedule is pinned to. Put it back; the item is retried. |
| `process.child.definition_unavailable` | Warning | A child step named a process the catalog does not know. |
| `process.port.exception`, `process.port.timeout`, `process.port.not_registered` | Warning | A port threw, was too slow, or no port has that name. |
| `process.effect.lease_expired` | Warning | A pump died or hung with an effect claimed; the delivery mode was applied. |
| `process.instance.finished` | Information | An instance ended. |
| `process.instance.suspended` | Warning | An instance stopped on a problem (step and code). Fix the cause, then resume. |
| `process.inbox.refused` | Information | A trigger was rejected with its code. |
| `process.item.failed` | Warning | One item threw; it is left as it was and tried again next pass. |
| `process.trigger.no_business_key` | Warning | An event trigger found no usable business key in the payload. |
| `process.schedule.fired`, `process.schedule.misfired` | Information, Warning | A schedule occurrence started, or occurrences were missed. |
| `process.window.zone_unknown` | Warning | A delivery window names a zone the machine cannot resolve. |
| `process.activation.saved`, `process.activation.removed` | Information | An activation was written or removed. |
| `process.instance.purged` | Information | A case was purged: tenant, root instance and the number of rows deleted per table. |
| `process.effect.orphaned` | Warning | A port call finished after its case was purged; its result was dropped. Nothing to fix: the call could not be recalled. |
| `process.worker.started`, `process.worker.pass_failed` | Information, Error | The worker loop. |

**Retention and erasure.** Nothing is deleted until the consumer says so. `PurgeFinishedBeforeAsync` removes the finished cases whose root was last written before a date you choose, oldest first, a tree per transaction; `PurgeEventsBeforeAsync` removes the event ids that de-duplicate `PublishEventAsync`; `PurgeInstanceAsync` removes one case now, `PurgeMode.FinishedOnly` refusing a case that is still going and `PurgeMode.Force` erasing it whatever its state (no history is written, no port is called, nobody is told). Each purge is all or nothing per case, and none of them waits for a row lock while it holds another, so a purge cannot deadlock with a pump, a publish or a command and returns the number of rows it deleted per table, which is the line to keep in your own audit log (the host logs `process.instance.purged` with the same numbers and never a business key). Run the job from your own scheduler; a loop stops when a call purged nothing, because a case that another transaction holds is skipped and found by a later run. `PurgeInstanceAsync` waits at most `PurgeLockTimeout` for a case that is held and then answers `Refused` with the code `process.purge.busy`, changing nothing: treat it as "try again on the next run", as the erase example does. Purging event ids shortens the de-duplication window to what you keep: an id older than the cut-off is a new event if it is published again. A port call that was already running when its case was purged cannot be recalled: its result is dropped and `process.effect.orphaned` is logged.

```csharp
internal sealed class Retention(IProcessEngine engine, TimeProvider time)
{
    // The consumer's rules, not the host's: finished cases are kept 35 days; an event id is remembered for 7 days.
    private static readonly TimeSpan CaseRetention = TimeSpan.FromDays(35);
    private static readonly TimeSpan EventWindow = TimeSpan.FromDays(7);

    /// <summary>The job of one tenant, run once a night: purges the cases that finished longer ago than the retention, then the event ids older than the window.</summary>
    internal async Task<RetentionReport> RunAsync(string tenantId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var trees = 0;
        var instances = 0;
        PurgeBatchResult batch;
        do
        {
            batch = await engine.PurgeFinishedBeforeAsync(tenantId, now - CaseRetention, 100, cancellationToken);
            trees += batch.Trees;
            instances += batch.Counts.Instances;
        }
        while (batch.More && batch.Trees > 0);

        var events = 0;
        PurgeEventsResult page;
        do
        {
            page = await engine.PurgeEventsBeforeAsync(tenantId, now - EventWindow, 5000, cancellationToken);
            events += page.Events;
        }
        while (page.More);

        return new RetentionReport(trees, instances, events);
    }

    /// <summary>A request to erase a case now, whatever its state: finds the root by its business key and removes the whole tree. The report holds numbers only, so it can go to an audit log. A refusal with the code <c>process.purge.busy</c> means that another transaction held the case for the whole <c>PurgeLockTimeout</c> and nothing changed: record the request as open and try again on the next run.</summary>
    internal async Task<PurgeResult> EraseAsync(string tenantId, string caseRef, CancellationToken cancellationToken)
    {
        var root = await engine.FindInstanceAsync(tenantId, "case.lifecycle", caseRef, cancellationToken);
        return root is null
            ? new PurgeResult(PurgeStatus.NotFound, PurgeCounts.None)
            : await engine.PurgeInstanceAsync(tenantId, root.InstanceId, PurgeMode.Force, cancellationToken);
    }
}

internal sealed record RetentionReport(int Trees, int Instances, int Events);

```

**Suspended instances.** An instance whose step failed with no route for the failure (or whose rule or data was invalid) is `Suspended` with a problem code: the instance stays as it is, its outstanding work is cancelled, and nothing happens until someone acts. A suspension is not a failure: the parent of a suspended child is told nothing and keeps waiting, so the routes of a parent (`onError`) never see it, and no task is raised. Three ways to learn of it: the log event `process.instance.suspended` (tenant, instance, step and code, no business key), the status and `Problem` of an instance you look at (`GetInstanceAsync`, or `FindInstanceAsync` with the business key), and the cockpit queries of section 8: `ListInstancesAsync` with `Statuses` set to `Suspended`, or with `NeedsAttention` to include the cases whose child is suspended (`SuspendedDescendants` on the row says how many).

`ResumeAsync` runs the step that stopped it again. Resume after the cause is fixed on the outside (a port registered, a document back in the catalog); resuming without fixing it suspends the instance again. Some causes cannot be fixed on the outside, because they are inputs: the reference pre-visit stage started without `practice_zone` is suspended at `send` with `process.window.zone_missing`, resuming it suspends it at once again, and the instance has no way to receive the zone. The way out that the engine allows is `CancelAsync` on the suspended child. A cancelled child answers its parent with `process.child.cancelled`, the parent takes the `onError` route of its stage (`ops_previsit` in `case.lifecycle`), and a person continues or closes the case there. Cancelling a parent cancels its children. This path was verified on PostgreSQL with a copy of `case.lifecycle` whose zone check lets the case through (`LifecycleFlowTests`); the reference document itself does not reach it, because its first step stops a case without a zone.

## 12. Versioning

An instance is pinned to the version and the content hash of the document it started with; every later trigger finds that exact definition through the catalog. Publishing a new version of a process therefore changes nothing for the instances that are running. A new instance starts on the latest version: an event trigger or schedule on the version pinned by the last activation, a child on the latest version in the catalog at the moment its parent reaches the subprocess step. To move new cases to a new version, add it to the catalog and call `ActivateAsync` again; keep the old version in the catalog until its last instance has finished. There is no way to move a running instance to a new version.

A change to the package is a change of the library, not of the documents: `brain.process@1` documents keep loading, and a new version of the library says in the changelog when the format gains something. This guide describes version 0.3.0.

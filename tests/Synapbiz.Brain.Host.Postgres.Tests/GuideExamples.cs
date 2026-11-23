using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;
using Synapbiz.Brain.Spec;

// The C# blocks of docs/guides/process-engine.md. Every block of the guide is a region of this file, between the two markers
// "guide:begin <name>" and "guide:end", and GuideTests checks that the guide shows each region verbatim and shows no other C#. So
// every example the guide gives compiles, and the ones that can run are run by the tests. Everything here is synthetic: the gateways
// below stand for the consumer's own systems.
namespace Synapbiz.Brain.Host.Postgres.Tests;

// guide:begin loading
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

// guide:end

// guide:begin catalog
internal sealed class ListCatalog(IEnumerable<BrainProcess> processes) : IProcessCatalog
{
    private readonly List<BrainProcess> all = [.. processes];

    public BrainProcess? Latest(string processId) =>
        all.Where(process => process.Id == processId).MaxBy(process => process.Version);

    public BrainProcess? Find(string processId, long version, string contentHash) =>
        all.FirstOrDefault(process => process.Id == processId && process.Version == version && process.ContentHash == contentHash);
}

// guide:end

// guide:begin read-port
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

// guide:end

// guide:begin action-port
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

// guide:end

// guide:begin ai-port
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

// guide:end

// guide:begin calendar
internal sealed class PracticeCalendar(IPracticeDirectory directory) : IProcessCalendar
{
    public DateOnly Today(string tenantId, DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, directory.ZoneOf(tenantId)).DateTime);
}

// guide:end

// guide:begin protector
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

// guide:end

// guide:begin registration
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

// guide:end

// guide:begin startup
internal static class Startup
{
    /// <summary>Called once, before the worker starts handling work: creates the schema and the tables, or does nothing when they are there.</summary>
    internal static Task MigrateAsync(IServiceProvider services) =>
        services.GetRequiredService<IProcessStoreMigrator>().MigrateAsync();
}

// guide:end

// guide:begin opening
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

// guide:end

// guide:begin tasks
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

// guide:end

// guide:begin cockpit
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

// guide:end

// guide:begin retention
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

// guide:end

// The consumer's own systems, which the examples above call. They are not part of the engine.
internal interface IRequirementsGateway
{
    Task<int?> CountMissingAsync(string tenantId, string caseRef, CancellationToken cancellationToken);
}

internal enum SendAnswer
{
    Sent = 1,
    Busy = 2,
    NoChannel = 3,
    Refused = 4,
}

internal interface IMessagingGateway
{
    Task<SendAnswer> SendAsync(string idempotencyKey, string tenantId, string caseRef, CancellationToken cancellationToken);
}

internal sealed record Suggestion(string Code, int Confidence);

internal interface IAssistantGateway
{
    Task<Suggestion?> SuggestAsync(string model, string promptId, long promptVersion, string inputJson, CancellationToken cancellationToken);
}

internal interface IPracticeDirectory
{
    TimeZoneInfo ZoneOf(string tenantId);
}

internal interface IKeyStore
{
    byte[] KeyOf(string tenantId);
}

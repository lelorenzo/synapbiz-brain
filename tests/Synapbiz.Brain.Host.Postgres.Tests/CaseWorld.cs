using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>One contact the reminder port was asked to send.</summary>
/// <param name="Tenant">The tenant of the instance.</param>
/// <param name="InstanceId">The instance that asked.</param>
/// <param name="At">The instant of the fake clock when the port was invoked.</param>
internal sealed record Contact(string Tenant, string InstanceId, DateTimeOffset At);

/// <summary>
/// A practice for the tests of the reference flows: a rig with the six reference processes in its catalog and a fake port for every
/// port they name. The ports answer from a few knobs (how many items are missing, how many reads a note stays a draft, what the
/// assistant suggests, what status the claim ends with) so that a test steers a case without scripting every call, and the
/// reminder port records the instant of every contact it is asked to make.
/// </summary>
internal sealed partial class CaseWorld : IAsyncDisposable
{
    /// <summary>The zone of the contact hours: a zone of the United States pattern, UTC-5 in January.</summary>
    internal const string Zone = "America/New_York";

    private readonly ConcurrentQueue<Contact> contacts = new();

    private CaseWorld()
    {
    }

    private void Wire()
    {
        var rig = Rig;
        Requirements = rig.Port("intake.requirements");
        Reminder = rig.Port("messaging.reminder");
        NoteStatus = rig.Port("records.note_status");
        NoteFacts = rig.Port("records.note_facts");
        Suggest = rig.Port("assist.suggest");
        ClaimStatus = rig.Port("claims.status");
        CaseList = rig.Port("schedule.cases_for_day");
        Requirements.Handler = (call, _) => Done("{\"missing_count\":" + (Entry(call) <= 2 ? MissingFirst(call) : 0).ToString(CultureInfo.InvariantCulture) + Noisy() + "}");
        Reminder.Handler = (call, _) =>
        {
            contacts.Enqueue(new Contact(call.TenantId, call.InstanceId, rig.Time.GetUtcNow()));
            return Done("{\"receipt\":\"" + (Noise ?? "r-1") + "\"}");
        };
        NoteStatus.Handler = (call, _) => Done("{\"status\":\"" + (Round(call) <= DraftReads ? "draft" : "signed") + "\"" + Noisy() + "}");
        NoteFacts.Handler = (_, _) => Done("{\"items\":6,\"flagged\":2" + Noisy() + "}");
        Suggest.Handler = (_, _) => Done("{\"code\":\"" + SuggestedCode + "\",\"confidence\":" + Confidence.ToString(CultureInfo.InvariantCulture) + "}");
        ClaimStatus.Handler = (call, _) => Done("{\"status\":\"" + (Round(call) <= ReviewReads ? "in_review" : FinalClaimStatus) + "\"" + Noisy() + "}");
        CaseList.Handler = (call, _) =>
        {
            CaseListInputs.Enqueue(call.Request.InputJson);
            return Done("{\"cases\":[{\"case_ref\":\"c-1001\"},{\"case_ref\":\"c-1002\"}]}");
        };
    }

    internal Rig Rig { get; private set; } = null!;

    internal FakePort Requirements { get; private set; } = null!;

    internal FakePort Reminder { get; private set; } = null!;

    internal FakePort NoteStatus { get; private set; } = null!;

    internal FakePort NoteFacts { get; private set; } = null!;

    internal FakePort Suggest { get; private set; } = null!;

    internal FakePort ClaimStatus { get; private set; } = null!;

    internal FakePort CaseList { get; private set; } = null!;

    /// <summary>The inputs the case-list port was called with, in order.</summary>
    internal ConcurrentQueue<string> CaseListInputs { get; } = new();

    /// <summary>The contacts made so far.</summary>
    internal IReadOnlyList<Contact> Contacts => [.. contacts];

    /// <summary>How many items the first read of a case finds missing.</summary>
    internal Func<PortCall, int> MissingFirst { get; set; } = _ => 2;

    /// <summary>How many reads of the note answer <c>draft</c> before it is signed.</summary>
    internal int DraftReads { get; set; } = 1;

    /// <summary>How many reads of the claim answer <c>in_review</c> before it answers <see cref="FinalClaimStatus"/>.</summary>
    internal int ReviewReads { get; set; } = 1;

    internal string FinalClaimStatus { get; set; } = "paid";

    internal string SuggestedCode { get; set; } = "C-100";

    internal int Confidence { get; set; } = 85;

    /// <summary>A text added to the output of the ports as an extra member the processes do not read; null for none.</summary>
    internal string? Noise { get; set; }

    internal static async Task<CaseWorld> CreateAsync(IPayloadProtector? protector = null)
    {
        var processes = ReferenceFlows.All();
        var world = new CaseWorld { Rig = await Rig.CreateAsync(protector) };
        world.Rig.Catalog.Add(processes);
        world.Wire();
        return world;
    }

    internal static Task<PortResult> Done(string outputJson) => Task.FromResult<PortResult>(new PortCompleted(outputJson));

    internal static Task<PortResult> Failed(ProcessErrorKind kind, string code) => Task.FromResult<PortResult>(new PortFailed(kind, code));

    /// <summary>Activates <c>case.lifecycle</c> for a tenant with the zone of the contact hours and whether the assistant is enabled.</summary>
    internal Task<ActivationResult> ActivateLifecycleAsync(string tenant = "t-1", bool assist = true) =>
        Rig.Engine.ActivateAsync(new ProcessActivation(tenant, "case.lifecycle", null, "{\"practice_zone\":\"" + Zone + "\",\"assist_enabled\":" + (assist ? "true" : "false") + "}"));

    /// <summary>Publishes <c>case.opened</c> for a case; <paramref name="note"/> is an extra payload member nothing reads.</summary>
    internal Task<PublishResult> OpenAsync(string tenant, string caseRef, string? note = null) =>
        Rig.Engine.PublishEventAsync(new ProcessEvent(
            tenant,
            "open-" + tenant + "-" + caseRef,
            "case.opened",
            null,
            new JsonObject { ["case_ref"] = caseRef, ["note"] = note }.ToJsonString()));

    internal Task<PublishResult> PublishAsync(string tenant, string eventId, string name, string caseRef, string payloadJson = "{}") =>
        Rig.Engine.PublishEventAsync(new ProcessEvent(tenant, eventId, name, caseRef, payloadJson));

    internal async Task<ProcessInstanceInfo> RootAsync(string tenant, string caseRef) =>
        (await Rig.Engine.FindInstanceAsync(tenant, "case.lifecycle", caseRef))!;

    /// <summary>The stage of the root instance of a case.</summary>
    internal async Task<string?> StageAsync(string tenant, string caseRef) => (await RootAsync(tenant, caseRef)).Stage;

    /// <summary>The child instances of a root, by process id.</summary>
    internal async Task<Dictionary<string, ProcessInstanceInfo>> ChildrenAsync(string tenant, string rootId)
    {
        var ids = await Rig.ColumnAsync<string>(
            "select instance_id from {schema}.process_instance where tenant_id = @t and parent_instance_id = @p order by process_id",
            ("t", tenant),
            ("p", rootId));
        var children = new Dictionary<string, ProcessInstanceInfo>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var info = (await Rig.Engine.GetInstanceAsync(tenant, id))!;
            children[info.ProcessId] = info;
        }

        return children;
    }

    /// <summary>The single open task of a queue.</summary>
    internal async Task<ProcessTaskInfo> TaskAsync(string tenant, string queue) =>
        (await Rig.Engine.ListOpenTasksAsync(tenant, queue)).Should().ContainSingle().Subject;

    /// <summary>Completes a task, lets the host carry the answer through, and checks that the interpreter applied it.</summary>
    internal async Task CompleteAsync(ProcessTaskInfo task, string outcome, string data = "{}")
    {
        (await Rig.Engine.CompleteTaskAsync(task.TenantId, task.TaskId, outcome, data)).Disposition.Should().Be(AdvanceDisposition.Applied);
        await Rig.DrainAsync();
    }

    public ValueTask DisposeAsync() => Rig.DisposeAsync();

    /// <summary>The entry number of the step an effect belongs to (the third part of its id).</summary>
    private static int Entry(PortCall call) => int.Parse(call.Request.EffectId.Split('/')[^2], CultureInfo.InvariantCulture);

    /// <summary>The round of a poll an effect belongs to (the <c>rN</c> part of its id).</summary>
    private static int Round(PortCall call) => int.Parse(RoundPattern().Match(call.Request.EffectId).Groups[1].Value, CultureInfo.InvariantCulture);

    private string Noisy() => Noise is null ? "" : ",\"note\":\"" + Noise + "\"";

    [GeneratedRegex("/r([0-9]+)/", RegexOptions.None, 1000)]
    private static partial Regex RoundPattern();
}

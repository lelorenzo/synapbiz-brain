using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// The consumer guide shows the C# of <c>GuideExamples.cs</c> and nothing else; the examples that can run, run. A change to either
/// side that the other does not follow fails here.
/// </summary>
public sealed partial class GuideTests
{
    private const string Guide = "docs/guides/process-engine.md";
    private const string Examples = "tests/Synapbiz.Brain.Host.Postgres.Tests/GuideExamples.cs";
    private const string Tenant = "t-1";
    private const string Folder = "samples/process-reference/";

    [Fact]
    public void TheGuideShowsEveryRegionOfTheExamplesVerbatimAndNoOtherCSharp()
    {
        var regions = Regions();
        var guide = Normalize(Fixtures.Read(Guide));
        var blocks = FencePattern().Matches(guide).Select(match => match.Groups[1].Value.TrimEnd('\n')).ToList();

        regions.Should().NotBeEmpty();
        foreach (var (name, text) in regions)
        {
            blocks.Should().Contain(text, "the region " + name + " of GuideExamples.cs must appear in the guide as it is");
        }

        blocks.Should().OnlyContain(block => regions.Values.Contains(block), "every C# block of the guide must be a region of GuideExamples.cs, so that it compiles");
        blocks.Should().HaveCount(regions.Count, "each region appears once");
    }

    [Fact]
    public void TheGuideNamesEveryLogMessageTheHostWrites()
    {
        var guide = Fixtures.Read(Guide);
        var source = Fixtures.Read("src/Synapbiz.Brain.Host.Postgres/Hosting/HostLog.cs");
        var messages = MessagePattern().Matches(source).Select(match => match.Groups[1].Value).Distinct().ToList();

        messages.Should().NotBeEmpty();
        foreach (var message in messages)
        {
            guide.Should().Contain("`" + message + "`");
        }
    }

    [Fact]
    public void TheGuideNamesEveryOptionOfTheHost()
    {
        var guide = Fixtures.Read(Guide);

        foreach (var option in typeof(ProcessHostOptions).GetProperties().Select(property => property.Name))
        {
            guide.Should().Contain("`" + option + "`");
        }
    }

    [Fact]
    public void TheCatalogFindsTheLatestVersionAndTheExactOne()
    {
        var processes = ReferenceFlows.All();
        var catalog = new ListCatalog(processes);
        var outreach = processes.Single(process => process.Id == "previsit.outreach");

        catalog.Latest("previsit.outreach").Should().BeSameAs(outreach);
        catalog.Latest("nobody.knows").Should().BeNull();
        catalog.Find("previsit.outreach", outreach.Version, outreach.ContentHash).Should().BeSameAs(outreach);
        catalog.Find("previsit.outreach", outreach.Version, "another-hash").Should().BeNull();
    }

    [Fact]
    public void TheLoadingExampleLoadsAFolderAndNamesAFolderThatDoesNotLoad()
    {
        var folder = Path.GetDirectoryName(Fixtures.Locate(Folder + "postvisit.coding_review/process.json"))!;

        Documents.Load(folder, ["previsit.outreach"]).Id.Should().Be("postvisit.coding_review");

        var broken = Directory.CreateTempSubdirectory("guide-broken-");
        try
        {
            File.WriteAllText(Path.Combine(broken.FullName, "process.json"), "{ \"$spec\": \"brain.process@1\" }");
            var load = () => Documents.Load(broken.FullName, []);
            load.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(broken.FullName);
        }
        finally
        {
            broken.Delete(true);
        }
    }

    [Fact]
    public async Task TheReadPortAnswersWithTheCountOrWithNotAvailable()
    {
        var port = new RequirementsPort(new Requirements(3));
        var call = new PortCall(Tenant, "i-1", Request("{\"case\":\"c-1\"}"));

        port.Name.Should().Be("intake.requirements");
        (await port.InvokeAsync(call, CancellationToken.None)).Should().Be(new PortCompleted("{\"missing_count\":3}"));
        (await new RequirementsPort(new Requirements(null)).InvokeAsync(call, CancellationToken.None)).Should().Be(new PortFailed(ProcessErrorKind.NotAvailable, "intake.no_connector"));
    }

    [Fact]
    public async Task TheActionPortHandsTheIdempotencyKeyOnAndMapsEveryAnswer()
    {
        var call = new PortCall(Tenant, "i-1", Request("{\"case\":\"c-1\",\"missing\":2}"));
        var messaging = new Messaging(SendAnswer.Sent);

        (await new ReminderPort(messaging).InvokeAsync(call, CancellationToken.None)).Should().Be(new PortCompleted("{\"receipt\":\"key-1\"}"));
        messaging.Keys.Should().Equal("key-1");
        (await new ReminderPort(new Messaging(SendAnswer.Busy)).InvokeAsync(call, CancellationToken.None)).Should().Be(new PortFailed(ProcessErrorKind.RateLimited, "messaging.busy", TimeSpan.FromMinutes(1)));
        (await new ReminderPort(new Messaging(SendAnswer.NoChannel)).InvokeAsync(call, CancellationToken.None)).Should().Be(new PortFailed(ProcessErrorKind.NotAvailable, "messaging.no_channel"));
        (await new ReminderPort(new Messaging(SendAnswer.Refused)).InvokeAsync(call, CancellationToken.None)).Should().Be(new PortFailed(ProcessErrorKind.Permanent, "messaging.refused"));
    }

    [Fact]
    public async Task TheAiPortPassesTheModelAndThePromptAndAnswersWithOnlyTheCodeAndTheConfidence()
    {
        var assistant = new Assistant(new Suggestion("C-100", 85));
        var request = Request("{\"case\":\"c-1\"}") with { Model = "model-a", PromptId = "coding-suggest", PromptVersion = 1 };
        var call = new PortCall(Tenant, "i-1", request);

        (await new SuggestPort(assistant).InvokeAsync(call, CancellationToken.None)).Should().Be(new PortCompleted("{\"code\":\"C-100\",\"confidence\":85}"));
        assistant.Seen.Should().Equal("model-a/coding-suggest/1");
        (await new SuggestPort(new Assistant(null)).InvokeAsync(call, CancellationToken.None)).Should().Be(new PortFailed(ProcessErrorKind.Transient, "assist.no_answer"));
    }

    [Fact]
    public void TheCalendarGivesTheLocalDayOfTheTenant()
    {
        var calendar = new PracticeCalendar(new Practices(TestZoneResolver.Eastern));

        calendar.Today(Tenant, new DateTimeOffset(2027, 1, 5, 3, 0, 0, TimeSpan.Zero)).Should().Be(new DateOnly(2027, 1, 4));
        calendar.Today(Tenant, new DateTimeOffset(2027, 1, 5, 5, 0, 0, TimeSpan.Zero)).Should().Be(new DateOnly(2027, 1, 5));
    }

    [Fact]
    public void TheProtectorRoundTripsAndNeverOpensForAnotherTenant()
    {
        var protector = new TenantKeyProtector(new Keys());
        const string Text = "{\"case_ref\":\"c-1\"}";

        var sealedText = protector.Protect(Tenant, Text);

        sealedText.Should().NotContain("c-1");
        protector.Protect(Tenant, Text).Should().NotBe(sealedText, "every text has its own nonce");
        protector.Unprotect(Tenant, sealedText).Should().Be(Text);
        var other = () => protector.Unprotect("t-2", sealedText);
        other.Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task TheRegistrationBuildsTheHostWithItsEngineItsPumpItsMigratorAndItsWorker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRequirementsGateway>(new Requirements(0));
        services.AddSingleton<IMessagingGateway>(new Messaging(SendAnswer.Sent));
        services.AddSingleton<IAssistantGateway>(new Assistant(null));
        services.AddSingleton<IPracticeDirectory>(new Practices(TimeZoneInfo.Utc));
        services.AddSingleton<IKeyStore>(new Keys());
        services.AddCaseProcesses("Host=localhost;Database=never_opened", Path.GetDirectoryName(Path.GetDirectoryName(Fixtures.Locate(Folder + "case.lifecycle/process.json")))!);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IProcessEngine>().Should().NotBeNull();
        provider.GetRequiredService<IProcessPump>().Should().NotBeNull();
        provider.GetRequiredService<IProcessStoreMigrator>().Should().NotBeNull();
        provider.GetServices<IHostedService>().Should().ContainSingle();
        provider.GetServices<IProcessPort>().Select(port => port.Name).Should().BeEquivalentTo("intake.requirements", "messaging.reminder", "assist.suggest");
        provider.GetRequiredService<IProcessCatalog>().Latest("case.lifecycle").Should().NotBeNull();
    }

    [Fact]
    public async Task TheOperationsOfTheGuideOpenACaseListItsTaskCompleteItAndShowItInTheCockpit()
    {
        await using var world = await CaseWorld.CreateAsync();
        world.MissingFirst = _ => 0;
        world.DraftReads = 0;
        var opening = new CaseOpening(world.Rig.Engine);
        var inbox = new TaskInbox(world.Rig.Engine);
        var cockpit = new Cockpit(world.Rig.Engine);

        (await opening.ActivateAsync(Tenant, CaseWorld.Zone, true)).Changed.Should().BeTrue();
        (await opening.OpenCaseAsync(Tenant, "opened-1", "c-1001")).Should().Be(new PublishResult(PublishStatus.Queued, 1));
        (await opening.OpenCaseAsync(Tenant, "opened-1", "c-1001")).Status.Should().Be(PublishStatus.Duplicate);
        await world.Rig.DrainAsync();

        var view = (await cockpit.ViewAsync(Tenant, "c-1001"))!;
        view.Stage.Should().Be("postvisit");
        view.Status.Should().Be(ProcessStatus.Waiting);
        view.ChildKeys.Should().HaveCount(3);
        (await cockpit.ViewAsync(Tenant, "c-9999")).Should().BeNull();
        var child = await world.Rig.Engine.FindInstanceAsync(Tenant, "postvisit.coding_review", view.ChildKeys[^1]);
        child!.Status.Should().Be(ProcessStatus.Waiting);

        var card = (await inbox.QueueAsync(Tenant, "physician_review")).Should().ContainSingle().Subject;
        card.Outcomes.Should().BeEquivalentTo("approved", "changed", "rejected");
        card.Show["suggested_code"]!.GetValue<string>().Should().Be("C-100");
        (await inbox.CompleteAsync(Tenant, card.TaskId, "maybe", "{}")).Should().StartWith("refused: ");
        (await inbox.CompleteAsync(Tenant, card.TaskId, "approved", "{}")).Should().Be("done");
        await world.Rig.DrainAsync();
        (await inbox.CompleteAsync(Tenant, card.TaskId, "approved", "{}")).Should().Be("already handled");
        (await cockpit.ViewAsync(Tenant, "c-1001"))!.Stage.Should().Be("collection");
        (await cockpit.ResumeAsync(Tenant, (await world.RootAsync(Tenant, "c-1001")).InstanceId)).Should().BeNull("nothing is suspended");
    }

    [Fact]
    public async Task TheCockpitSaysWhySuspendedInstanceStoppedAndResumesIt()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Catalog.Add(Fixtures.Host("suspend"));
        var calls = 0;
        rig.Port("t.read").Handler = (_, _) => Task.FromResult<PortResult>(Interlocked.Increment(ref calls) == 1 ? new PortFailed(ProcessErrorKind.Permanent, "x.broken") : new PortCompleted("{}"));
        var id = await rig.StartAsync("host.suspend", "k-1");
        await rig.DrainAsync();
        var cockpit = new Cockpit(rig.Engine);

        var problem = await cockpit.ResumeAsync(rig.Tenant, id);
        await rig.DrainAsync();

        problem.Should().StartWith("x.broken at ");
        (await rig.Engine.GetInstanceAsync(rig.Tenant, id))!.Status.Should().Be(ProcessStatus.Completed);
    }

    private static EffectRequest Request(string inputJson) =>
        new("step", "i-1/step/1", "key-1", ProcessEffectKind.Read, "port", inputJson, 1, ProcessDelivery.AtLeastOnce);

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>The regions of the examples file, by name, without the markers and without the indentation they have in the file.</summary>
    private static Dictionary<string, string> Regions()
    {
        var source = Normalize(Fixtures.Read(Examples));
        var regions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in RegionPattern().Matches(source))
        {
            var lines = match.Groups[2].Value.Split('\n');
            var indent = lines.Where(line => line.Trim().Length > 0).Min(line => line.Length - line.TrimStart().Length);
            regions[match.Groups[1].Value] = string.Join("\n", lines.Select(line => line.Length >= indent ? line[indent..] : line.TrimStart())).Trim('\n');
        }

        return regions;
    }

    [GeneratedRegex("// guide:begin (\\S+)\\n(.*?)\\n// guide:end\\n", RegexOptions.Singleline, 1000)]
    private static partial Regex RegionPattern();

    [GeneratedRegex("```csharp\\n(.*?)```", RegexOptions.Singleline, 1000)]
    private static partial Regex FencePattern();

    [GeneratedRegex("Message = \"(process\\.[a-z_.]+):", RegexOptions.None, 1000)]
    private static partial Regex MessagePattern();

    private sealed class Requirements(int? missing) : IRequirementsGateway
    {
        public Task<int?> CountMissingAsync(string tenantId, string caseRef, CancellationToken cancellationToken) => Task.FromResult(missing);
    }

    private sealed class Messaging(SendAnswer answer) : IMessagingGateway
    {
        public List<string> Keys { get; } = [];

        public Task<SendAnswer> SendAsync(string idempotencyKey, string tenantId, string caseRef, CancellationToken cancellationToken)
        {
            Keys.Add(idempotencyKey);
            return Task.FromResult(answer);
        }
    }

    private sealed class Assistant(Suggestion? suggestion) : IAssistantGateway
    {
        public List<string> Seen { get; } = [];

        public Task<Suggestion?> SuggestAsync(string model, string promptId, long promptVersion, string inputJson, CancellationToken cancellationToken)
        {
            Seen.Add(model + "/" + promptId + "/" + promptVersion.ToString(CultureInfo.InvariantCulture));
            return Task.FromResult(suggestion);
        }
    }

    private sealed class Practices(TimeZoneInfo zone) : IPracticeDirectory
    {
        public TimeZoneInfo ZoneOf(string tenantId) => zone;
    }

    private sealed class Keys : IKeyStore
    {
        public byte[] KeyOf(string tenantId) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("synthetic-key-" + tenantId));
    }
}

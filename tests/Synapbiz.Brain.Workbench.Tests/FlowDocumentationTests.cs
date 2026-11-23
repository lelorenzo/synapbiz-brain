using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The reference page shows the C# of <c>FlowDocExamples.cs</c> and nothing else, and names every public type and code of the flow workbench.</summary>
public sealed partial class FlowDocumentationTests
{
    private const string Page = "docs/reference/flow-workbench.md";
    private const string Examples = "tests/Synapbiz.Brain.Workbench.Tests/FlowDocExamples.cs";

    [GeneratedRegex("// doc:begin (\\S+)\\n(.*?)\\n// doc:end\\n", RegexOptions.Singleline, 1000)]
    private static partial Regex RegionPattern();

    [GeneratedRegex("```csharp\\n(.*?)```", RegexOptions.Singleline, 1000)]
    private static partial Regex FencePattern();

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void The_page_shows_every_region_verbatim_and_no_other_csharp()
    {
        var regions = RegionPattern().Matches(Normalize(TestFiles.Read(Examples))).ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value.TrimEnd('\n'));
        var blocks = FencePattern().Matches(Normalize(TestFiles.Read(Page))).Select(match => match.Groups[1].Value.TrimEnd('\n')).ToList();
        regions.Should().NotBeEmpty();
        foreach (var (name, text) in regions)
        {
            blocks.Should().Contain(text, "the region " + name + " must appear on the page as it is");
        }

        blocks.Should().OnlyContain(block => regions.Values.Contains(block));
        blocks.Should().HaveCount(regions.Count);
    }

    [Fact]
    public void The_page_names_every_public_type_of_the_flow_workbench_and_of_the_outline()
    {
        var page = TestFiles.Read(Page);
        var types = typeof(FlowSimulation).Assembly.GetExportedTypes().Where(type => type.Namespace == "Synapbiz.Brain.Workbench.Flows" && !typeof(Delegate).IsAssignableFrom(type));
        string.Join(", ", types.Select(type => type.Name).Where(name => !page.Contains("`" + name + "`", StringComparison.Ordinal))).Should().BeEmpty();
    }

    [Fact]
    public void The_page_names_every_code_the_flow_workbench_can_report()
    {
        var page = TestFiles.Read(Page);
        var codes = typeof(FlowSimulation).Assembly.GetExportedTypes().Where(type => type.Namespace == "Synapbiz.Brain.Workbench.Flows")
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.IsLiteral && field.FieldType == typeof(string)))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(value => value.StartsWith("workbench.", StringComparison.Ordinal))
            .Distinct()
            .ToList();
        codes.Should().HaveCountGreaterThanOrEqualTo(8);
        string.Join(", ", codes.Where(code => !page.Contains("`" + code + "`", StringComparison.Ordinal))).Should().BeEmpty();
    }

    [Fact]
    public void The_page_names_every_public_member_of_the_simulation()
    {
        var page = TestFiles.Read(Page);
        var members = typeof(FlowSimulation).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member is MethodInfo { IsSpecialName: false } or PropertyInfo)
            .Select(member => member.Name).Distinct();
        string.Join(", ", members.Where(member => !page.Contains("`" + member, StringComparison.Ordinal))).Should().BeEmpty();
    }

    [Fact]
    public async Task The_host_answer_source_of_the_page_answers_an_ai_step_and_leaves_the_rest()
    {
        var ai = new FakeAi();
        var process = FlowDocs.Process("flow.batch");
        var session = FlowSimulation.Start(process, new FlowSimulationSetup
        {
            At = DateTimeOffset.Parse("2027-01-04T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Today = new DateOnly(2027, 1, 4),
            InputJson = """{"batch_ref":"b-1","items":[],"visit_at":"2027-01-10T15:00:00Z"}""",
            AnswerSource = new HostAiAnswerSource(ai),
        });
        session.AdvanceBy(TimeSpan.FromDays(7));
        var entries = await session.RunAnswersAsync();
        entries.Should().ContainSingle().Which.Source.Should().Be("HostAi");
        ai.Seen.Should().ContainSingle().Which.Should().Be(("model-a", "pick", 2L));
        session.State.Result.Should().Be("broken");

        var failing = FlowSimulation.Start(process, new FlowSimulationSetup
        {
            At = DateTimeOffset.Parse("2027-01-04T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Today = new DateOnly(2027, 1, 4),
            InputJson = """{"batch_ref":"b-1","items":[],"visit_at":"2027-01-10T15:00:00Z"}""",
            AnswerSource = new HostAiAnswerSource(new FakeAi { Down = true }),
        });
        failing.AdvanceBy(TimeSpan.FromDays(7));
        await failing.RunAnswersAsync();
        failing.State.Result.Should().Be("broken");
        failing.Log.Should().Contain(entry => entry.Kind == FlowSimulationEntryKind.EffectFailed);
    }

    [Fact]
    public async Task The_try_out_of_the_page_runs_and_says_what_is_left_for_a_person()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2027-01-04T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var source = new ScriptedAnswerSource().ForPort("schedule.cases_for_day", FlowAnswer.Failure("schedule.gone", isPermanent: true));
        var (session, waiting) = await FlowTryOut.RunAsync(FlowDocs.Process("practice.daily_refresh"), """{"run_for":"2027-01-04"}""", source, clock, CancellationToken.None);
        session.CurrentStep.Should().Be("refresh_attention");
        waiting.Should().Equal("a person in the queue operations");
    }

    [Fact]
    public void The_review_of_the_page_writes_and_reads_back_with_a_passing_proposal()
    {
        var process = FlowDocs.Process("practice.daily_refresh");
        var session = FlowSimulation.Start(process, new FlowSimulationSetup
        {
            At = DateTimeOffset.Parse("2027-01-05T11:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Today = new DateOnly(2027, 1, 5),
            InputJson = """{"run_for":"2027-01-05"}""",
        });
        session.CompleteEffect(session.Outstanding.OfType<OutstandingEffect>().Single().Id, """{"cases":[]}""");
        var text = FlowReviews.Write(process, "Dr. Example", DateTimeOffset.Parse("2026-11-22T21:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture), session);
        FlowReviews.Read(text, process).Should().Equal("a try-out worth keeping: passes");
        FlowReviews.Read(text, FlowDocs.Process("flow.bare")).Should().Contain(line => line.StartsWith("workbench.flow_review.stale", StringComparison.Ordinal));
        FlowReviews.Read("not json", process).Should().ContainSingle().Which.Should().StartWith("workbench.flow_review.json_invalid");
    }

    private sealed class FakeAi : IHostAi
    {
        internal List<(string Model, string Prompt, long Version)> Seen { get; } = [];

        internal bool Down { get; init; }

        public Task<string> CompleteAsync(string model, string promptId, long promptVersion, string inputJson, string outputSchemaJson, CancellationToken cancellationToken)
        {
            Seen.Add((model, promptId, promptVersion));
            return Down ? throw new HttpRequestException("down") : Task.FromResult("""{"pick":"p","size":3}""");
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

using System.Text.Json.Nodes;
using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>
/// The sample document that shows a timeout tied to an instant and an interruptible delivery window together: the reference page
/// includes it verbatim, it loads with no diagnostic, its scenarios pass, the independent player agrees and its labels are sound.
/// </summary>
public sealed class ReminderExampleTests
{
    private const string Fixture = "tests/fixtures/brain-process/reminder.process.json";

    private static BrainProcess Load()
    {
        var loaded = BrainProcessLoader.Load(TestFiles.Read(Fixture), new ProcessLoadContext([]));
        return loaded.Process ?? throw new InvalidOperationException("The reminder example does not load: " + string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path)));
    }

    private static IEnumerable<JsonObject> Scenarios() =>
        TestFiles.ReadJson(Fixture)["body"]!["scenarios"]!.AsArray().Select(node => node!.AsObject());

    [Fact]
    public void TheReferencePageIncludesTheExampleVerbatim()
    {
        var page = TestFiles.Read("docs/reference/brain-process-v1.md").Replace("\r\n", "\n", StringComparison.Ordinal);
        var example = TestFiles.Read(Fixture).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

        page.Should().Contain(example);
    }

    [Fact]
    public void TheExampleLoadsAndEveryScenarioPasses()
    {
        var result = Load().RunScenarios();

        result.Results.Should().HaveCount(6);
        result.Results.Where(item => !item.Passed).Select(item => item.Name + ": " + item.Failure).Should().BeEmpty();
    }

    [Fact]
    public void TheIndependentPlayerEndsEveryScenarioWhereTheDocumentSaysItDoes()
    {
        var process = Load();
        foreach (var scenario in Scenarios())
        {
            var run = ScenarioTracer.Play(process, scenario);
            var expect = scenario["expect"]!.AsObject();
            var name = scenario["name"]!.GetValue<string>();

            run.Final.Status.ToString().Should().Be(expect["status"]!.GetValue<string>(), because: name);
            run.Final.Result.Should().Be(expect["result"]?.GetValue<string>(), because: name);
            run.Trace.Should().Equal(expect["trace"]!.AsArray().Select(node => node!.GetValue<string>()), because: name);
        }
    }

    [Fact]
    public void TheExampleUsesBothAdditions()
    {
        var text = TestFiles.Read(Fixture);

        text.Should().Contain("\"until\"").And.Contain("\"interruptBy\"");
        Scenarios().SelectMany(scenario => ScenarioTracer.Play(Load(), scenario).History.Select(entry => entry.Kind))
            .Should().Contain([ProcessHistoryKind.WindowInterrupted, ProcessHistoryKind.TimedOut]);
    }

    [Fact]
    public void TheLabelsOfTheExampleAreSoundAndComplete()
    {
        var process = Load();
        var display = ProcessDisplay.From(process);

        ProcessDisplay.Validate(process).Should().BeEmpty();
        foreach (var language in new[] { "en", "es" })
        {
            display.Title(language).Should().NotBeNullOrWhiteSpace();
            foreach (var step in process.StepIds)
            {
                display.Step(language, step).Should().NotBeNullOrWhiteSpace(because: language + " " + step);
            }
        }
    }
}

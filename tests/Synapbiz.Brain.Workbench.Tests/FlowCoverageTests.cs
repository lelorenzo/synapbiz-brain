using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;
using ProcessTests = Synapbiz.Brain.Process.Tests;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed class FlowCoverageTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2027-01-04T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    public static TheoryData<string> Documents()
    {
        var data = new TheoryData<string>();
        foreach (var (name, _) in FlowDocs.All.Where(item => item.Name is not ("flow.batch" or "fixture.reminder")))
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void The_routes_taken_are_those_of_the_independent_route_map(string name)
    {
        var process = FlowDocs.Process(name);
        var steps = JsonNode.Parse(FlowDocs.SourceOf(name).Text)!["body"]!["steps"]!.AsObject();
        var expected = new HashSet<(string, string)>();
        foreach (var scenario in ProcessOutline.From(process).Scenarios)
        {
            var run = ProcessTests.ScenarioTracer.Play(process, JsonNode.Parse(ProcessOutline.From(process).ScenarioJson(scenario)!)!.AsObject());
            expected.UnionWith(ProcessTests.RouteMap.Taken(steps, run.History));
        }

        var coverage = FlowCoverage.Of(process);
        coverage.Problems.Should().BeEmpty(name);
        coverage.Routes.Where(route => route.Times > 0).Select(route => (route.Step, route.Label)).Should().BeEquivalentTo(expected, name);
        var declared = steps.SelectMany(step => ProcessTests.RouteMap.Declared(step.Value!.AsObject()).Select(label => (step.Key, label))).ToList();
        coverage.Routes.Select(route => (route.Step, route.Label)).Where(route => route.Label != "interrupt").Should().BeEquivalentTo(declared, name);
    }

    [Fact]
    public void The_numbers_of_every_document_are_pinned()
    {
        var text = new StringBuilder();
        foreach (var (name, process) in FlowDocs.All)
        {
            var coverage = FlowCoverage.Of(process);
            text.Append(name).Append(": routes ").Append(coverage.CoveredRoutes).Append('/').Append(coverage.TotalRoutes)
                .Append(", steps ").Append(coverage.EnteredSteps).Append('/').Append(coverage.TotalSteps).Append('\n');
            foreach (var route in coverage.NeverTaken)
            {
                text.Append("  never: ").Append(route.Step).Append(' ').Append(route.Label).Append(" -> ").Append(route.Target).Append('\n');
            }

            foreach (var step in coverage.StepsNeverEntered)
            {
                text.Append("  never entered: ").Append(step).Append('\n');
            }
        }

        FlowDocs.Golden("coverage.txt", text.ToString()).Should().Be(text.ToString());
    }

    [Fact]
    public void An_interrupt_is_a_route_of_its_own()
    {
        var coverage = FlowCoverage.Of(FlowDocs.Process("fixture.reminder"));
        coverage.Problems.Should().BeEmpty();
        var interrupt = coverage.Routes.Single(route => route.Step == "remind" && route.Label == "interrupt");
        interrupt.Target.Should().Be("handle_reply");
        interrupt.Times.Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_route_no_scenario_takes_is_listed()
    {
        var coverage = FlowCoverage.Of(FlowDocs.Process("flow.batch"));
        coverage.Problems.Should().BeEmpty();
        coverage.NeverTaken.Select(route => (route.Step, route.Label)).Should().Contain([("check", "onExpired"), ("check", "onError"), ("judge", "on:low")]);
        coverage.Routes.Single(route => route.Step == "verify" && route.Label == "on:true").Times.Should().Be(1);
        coverage.Routes.Single(route => route.Step == "each" && route.Label == "body").Times.Should().Be(6);
        coverage.Routes.Single(route => route.Step == "each" && route.Label == "body").Scenarios.Should().HaveCount(3);
        coverage.CoveredRoutes.Should().Be(coverage.TotalRoutes - coverage.NeverTaken.Count);
        coverage.StepsNeverEntered.Should().BeEmpty();
        coverage.EnteredSteps.Should().Be(coverage.TotalSteps);
    }

    [Fact]
    public void A_run_of_a_reader_adds_its_routes_under_its_name()
    {
        var process = FlowDocs.Process("flow.batch");
        var session = FlowSimulation.Start(process, new FlowSimulationSetup
        {
            At = At,
            Today = new DateOnly(2027, 1, 4),
            InputJson = """{"batch_ref":"b-1","items":[],"visit_at":"2027-01-10T15:00:00Z"}""",
            ChildProcesses = [FlowDocs.Process("flow.child")],
        });
        session.AdvanceBy(TimeSpan.FromDays(7));
        session.FailEffect(session.Outstanding.OfType<OutstandingEffect>().Single().Id, "assist.down", isPermanent: true);
        var coverage = FlowCoverage.Of(process, [session]);
        coverage.Problems.Should().BeEmpty();
        var route = coverage.Routes.Single(item => item.Step == "ask" && item.Label == "onError");
        route.Times.Should().Be(1);
        route.Scenarios.Should().Equal("simulation");
        coverage.CoveredRoutes.Should().Be(FlowCoverage.Of(process).CoveredRoutes + 1);
    }

    [Fact]
    public void A_history_the_routes_cannot_explain_is_reported_and_not_thrown()
    {
        var foreign = FlowSimulation.Start(FlowDocs.Process("flow.bare"), new FlowSimulationSetup { At = At, Today = new DateOnly(2027, 1, 4), InputJson = """{"ref":"r"}""" });
        foreign.AdvanceBy(TimeSpan.FromSeconds(31));
        var batch = FlowDocs.Process("flow.batch");
        FluentActions.Invoking(() => FlowCoverage.Of(batch, [foreign])).Should().Throw<ArgumentException>();
        var outline = ProcessOutline.From(batch);
        var coverage = FlowCoverage.Compute(outline, [.. FlowCoverage.PlayScenarios(batch, outline).Select(run => run.Session), foreign], WorkbenchLocale.En);
        var problem = coverage.Problems.Should().ContainSingle().Subject;
        problem.Code.Should().Be(FlowCoverage.ErrorRouteUnexplained);
        problem.Path.Should().Be("simulation");
        problem.Message.Should().Contain("blink");
        FlowCoverage.Of(FlowDocs.Process("flow.batch")).CoveredRoutes.Should().Be(coverage.CoveredRoutes);
    }

    [Fact]
    public void A_session_that_has_not_left_its_first_step_adds_nothing()
    {
        var process = FlowDocs.Process("flow.bare");
        var fresh = FlowSimulation.Start(process, new FlowSimulationSetup { At = At, Today = new DateOnly(2027, 1, 4), InputJson = """{"ref":"r"}""" });
        var coverage = FlowCoverage.Of(process, [fresh]);
        coverage.Problems.Should().BeEmpty();
        coverage.CoveredRoutes.Should().Be(FlowCoverage.Of(process).CoveredRoutes);
    }

    [Fact]
    public void A_null_process_is_rejected()
    {
        FluentActions.Invoking(() => FlowCoverage.Of(null!)).Should().Throw<ArgumentNullException>();
    }
}

using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The repair pass of route coverage: how a run is read, the fallback rule, the language and the runs it accepts.</summary>
public sealed class FlowRepairCoverageTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2027-01-04T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static long sequence;

    private static ProcessHistoryEntry Entry(string step, ProcessHistoryKind kind, string? code = null, string? data = null) =>
        new(++sequence, At, step, 1, 1, kind, code, data);

    private static OutlineStep Step(string id, OutlineStepKind kind, params (string Label, string Target)[] routes) =>
        new() { Id = id, Kind = kind, Routes = [.. routes.Select(route => new OutlineRoute(route.Label, route.Target))] };

    private static ProcessOutline Outline(params OutlineStep[] steps) => ProcessOutline.From(FlowDocs.Process("flow.bare")) with { Steps = steps };

    private const string DecidedWithNote = """{"rule":"r","version":1,"decided":true,"matchedRowKeys":["small"],"outcome":{"note":null,"level":"low"},"factsHash":"h","facts":{}}""";

    [Fact]
    public void A_decide_whose_routed_field_is_null_leaves_through_on_error_and_is_explained()
    {
        var process = FlowDocs.Conformance("valid decide routed by a code");
        var session = FlowSimulation.Start(process, new FlowSimulationSetup { At = At, Today = new DateOnly(2027, 1, 4) });
        session.Status.Should().Be(ProcessStatus.Completed);
        var coverage = FlowCoverage.Of(process, [session]);
        coverage.Problems.Should().BeEmpty();
        coverage.Routes.Single(route => route.Step == "s" && route.Label == "onError").Times.Should().Be(1);
        coverage.Routes.Where(route => route.Times > 0).Should().ContainSingle();
    }

    [Fact]
    public void The_decide_label_is_read_without_a_throw_for_every_shape_of_outcome()
    {
        var step = Step("s", OutlineStepKind.Decide, ("on:big", "e"), ("onError", "e"), ("onNoDecision", "e")) with { RouteField = "note" };
        string? Label(string? data, string? code = null) => RouteReader.DecideLabel(step, [Entry("s", ProcessHistoryKind.Decided, code, data)]);

        Label(DecidedWithNote).Should().Be("onError");
        Label("""{"decided":true,"outcome":{}}""").Should().Be("onError");
        Label("""{"decided":true,"outcome":null}""").Should().Be("onError");
        Label("""{"decided":true,"outcome":{"note":7}}""").Should().Be("onError");
        Label("""{"decided":true,"outcome":{"note":"big"}}""").Should().Be("on:big");
        Label("""{"decided":true,"outcome":{"note":"small"}}""").Should().Be("onError");
        Label("""{"decided":false,"outcome":null}""").Should().Be("onNoDecision");
        Label("""{"decided":true,"outcome":{"note":"big"}}""", "process.rule.failed").Should().Be("onError");
        Label(null).Should().BeNull();
        Label("""{"outcome":{}}""").Should().BeNull();
        RouteReader.DecideLabel(step, []).Should().BeNull();
        RouteReader.DecideLabel(step with { RouteField = null }, [Entry("s", ProcessHistoryKind.Decided, null, """{"decided":true,"outcome":{}}""")]).Should().Be("next");
        var flag = Step("s", OutlineStepKind.Decide, ("on:true", "e"), ("on:false", "e"), ("onError", "e")) with { RouteField = "flag" };
        RouteReader.DecideLabel(flag, [Entry("s", ProcessHistoryKind.Decided, null, """{"decided":true,"outcome":{"flag":true}}""")]).Should().Be("on:true");
        RouteReader.DecideLabel(flag, [Entry("s", ProcessHistoryKind.Decided, null, """{"decided":true,"outcome":{"flag":false}}""")]).Should().Be("on:false");
    }

    [Fact]
    public void A_route_only_the_fallback_can_resolve_is_resolved_by_it()
    {
        // The history of a decide whose output mapping failed reads as a success, but the case went to onError.
        var step = Step("s", OutlineStepKind.Decide, ("next", "a"), ("onError", "b"));
        var segment = new[] { Entry("s", ProcessHistoryKind.Decided, null, """{"decided":true,"outcome":{"x":1}}""") };
        RouteReader.Label(step, segment, "b").Should().Be("next", "the history alone points at the happy path");
        RouteReader.Resolve(step, segment, "b").Should().Be("onError");
        RouteReader.Resolve(step, segment, "a").Should().Be("next");
        RouteReader.Resolve(step, segment, "nowhere").Should().BeNull();
    }

    [Fact]
    public void Two_labels_that_lead_to_the_target_where_the_history_fails_are_unexplained()
    {
        var step = Step("s", OutlineStepKind.Decide, ("next", "a"), ("onError", "b"), ("onNoDecision", "b"));
        var segment = new[] { Entry("s", ProcessHistoryKind.Decided, null, """{"decided":true,"outcome":{"x":1}}""") };
        RouteReader.Resolve(step, segment, "b").Should().BeNull();

        var outline = Outline(step, Step("a", OutlineStepKind.End), Step("b", OutlineStepKind.End));
        var history = new[] { Entry("s", ProcessHistoryKind.StepEntered), segment[0], Entry("b", ProcessHistoryKind.StepEntered) };
        var reading = RouteReader.Taken(outline, history);
        reading.Routes.Should().BeEmpty();
        reading.Problem.Should().Be(new RouteReader.Unexplained("s", "b"));
    }

    [Fact]
    public void An_interrupt_and_the_loop_labels_are_inferred_from_the_history_alone()
    {
        var action = Step("send", OutlineStepKind.Action, ("next", "wait"), ("interrupt", "handle"));
        RouteReader.Label(action, [Entry("send", ProcessHistoryKind.WindowInterrupted)], "handle").Should().Be("interrupt");
        RouteReader.Label(action, [Entry("send", ProcessHistoryKind.EffectCompleted)], "wait").Should().Be("next");

        var loop = Step("cycle", OutlineStepKind.Loop, ("body", "work"), ("next", "after"), ("onExhausted", "after"));
        RouteReader.Label(loop, [Entry("cycle", ProcessHistoryKind.LoopIteration)], "work").Should().Be("body");
        RouteReader.Label(loop, [Entry("cycle", ProcessHistoryKind.LoopIteration)], "after").Should().Be("next");
        RouteReader.Label(loop, [Entry("cycle", ProcessHistoryKind.LoopExhausted)], "after").Should().Be("onExhausted");
        RouteReader.Label(Step("end", OutlineStepKind.End), [], "x").Should().BeNull();
    }

    [Fact]
    public void A_resumed_or_suspended_step_that_comes_back_to_itself_is_not_a_route()
    {
        var wait = Step("w", OutlineStepKind.Wait, ("next", "e"), ("onTimeout", "w"));
        var outline = Outline(wait, Step("e", OutlineStepKind.End));
        foreach (var between in new[] { ProcessHistoryKind.Resumed, ProcessHistoryKind.Suspended })
        {
            var history = new[] { Entry("w", ProcessHistoryKind.StepEntered), Entry("w", between), Entry("w", ProcessHistoryKind.StepEntered) };
            var reading = RouteReader.Taken(outline, history);
            reading.Problem.Should().BeNull(between.ToString());
            reading.Routes.Should().BeEmpty(between.ToString());
        }

        var timedOut = new[] { Entry("w", ProcessHistoryKind.StepEntered), Entry("w", ProcessHistoryKind.TimedOut), Entry("w", ProcessHistoryKind.StepEntered) };
        RouteReader.Taken(outline, timedOut).Routes.Should().Equal(("w", "onTimeout"));
    }

    [Fact]
    public void A_history_that_names_a_step_the_document_does_not_have_is_unexplained()
    {
        var outline = Outline(Step("a", OutlineStepKind.End));
        var reading = RouteReader.Taken(outline, [Entry("ghost", ProcessHistoryKind.StepEntered), Entry("a", ProcessHistoryKind.StepEntered)]);
        reading.Problem.Should().Be(new RouteReader.Unexplained("ghost", "a"));
    }

    [Fact]
    public void A_run_of_another_process_is_refused()
    {
        var foreign = FlowSimulation.Start(FlowDocs.Process("flow.bare"), new FlowSimulationSetup { At = At, Today = new DateOnly(2027, 1, 4), InputJson = """{"ref":"r"}""" });
        FluentActions.Invoking(() => FlowCoverage.Of(FlowDocs.Process("flow.batch"), [foreign])).Should().Throw<ArgumentException>().Which.ParamName.Should().Be("extraRuns");

        var sameIdOtherHash = FlowDocs.Edited("flow.bare", document => document["meta"]!["title"] = "Bare steps, edited");
        ProcessOutline.From(sameIdOtherHash).Id.Should().Be("flow.bare");
        FluentActions.Invoking(() => FlowCoverage.Of(FlowDocs.Process("flow.bare"), [FlowSimulation.Start(sameIdOtherHash, new FlowSimulationSetup { At = At, Today = new DateOnly(2027, 1, 4), InputJson = """{"ref":"r"}""" })]))
            .Should().Throw<ArgumentException>();
        FlowCoverage.Of(FlowDocs.Process("flow.bare"), [foreign]).Problems.Should().BeEmpty();
        FluentActions.Invoking(() => FlowCoverage.Of(FlowDocs.Process("flow.bare"), [null!])).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void The_text_of_a_problem_follows_the_language_asked_for()
    {
        var foreign = FlowSimulation.Start(FlowDocs.Process("flow.bare"), new FlowSimulationSetup { At = At, Today = new DateOnly(2027, 1, 4), InputJson = """{"ref":"r"}""" });
        foreign.AdvanceBy(TimeSpan.FromSeconds(31));
        var batch = FlowDocs.Process("flow.batch");

        // A history of another document read against this one is the way to make a run no route can explain.
        var outline = ProcessOutline.From(batch);
        var reading = RouteReader.Taken(outline, foreign.History);
        reading.Problem.Should().NotBeNull();
        var en = FlowCoverage.Compute(outline, [foreign], WorkbenchLocale.En).Problems.Single().Message;
        var es = FlowCoverage.Compute(outline, [foreign], WorkbenchLocale.Es).Problems.Single().Message;
        en.Should().Be("The history of step `blink` does not say which way the case left it: it went to `pause`.");
        es.Should().Be("El historial del paso `blink` no dice por dónde salió el caso: fue a `pause`.");
        FlowCoverage.Compute(outline, [foreign], WorkbenchLocale.Es).Problems.Single().Code.Should().Be(FlowCoverage.ErrorRouteUnexplained);
    }

    [Fact]
    public void The_scenarios_are_played_once_by_the_library_and_the_entry_agrees_with_the_scenario_player()
    {
        var source = File.ReadAllText(TestFiles.Locate("src/Synapbiz.Brain.Workbench/Flows/FlowLibrary.cs"));
        source.Should().NotContain("RunScenarios(").And.NotContain("FlowCoverage.Of(");
        foreach (var (name, process) in FlowDocs.All)
        {
            FlowLibrary.From([process], WorkbenchLocale.En).Single().ScenariosPass.Should().Be(process.RunScenarios().IsOk, name);
        }
    }
}

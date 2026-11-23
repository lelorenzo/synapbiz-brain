using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit.Abstractions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>
/// The six reference documents of <c>samples/process-reference</c>: they load, their scenarios pass, and the scenarios between them
/// take every route out of every step that can branch. A route nobody takes is a route nobody has tested.
/// </summary>
public sealed class ReferenceFlowTests(ITestOutputHelper output)
{
    public static TheoryData<string> Processes => [.. ReferenceDocs.Ids];

    [Fact]
    public void TheReferenceFoldersAreExactlyTheSixListedProcesses()
    {
        var root = Path.GetDirectoryName(TestFiles.Locate(ReferenceDocs.Root + "README.md"))!;
        var folders = Directory.GetDirectories(root).Select(Path.GetFileName);

        folders.Should().BeEquivalentTo(ReferenceDocs.Ids);
    }

    [Theory]
    [MemberData(nameof(Processes))]
    public void EveryDocumentLoadsWithZeroDiagnostics(string id)
    {
        foreach (var (file, result) in ReferenceDocs.RuleResults(id))
        {
            result.Errors.Should().BeEmpty(because: id + "/" + file);
            result.Rule.Should().NotBeNull();
        }

        var loaded = ReferenceDocs.Load(id);

        loaded.Errors.Should().BeEmpty(because: string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path)));
        loaded.Process!.Id.Should().Be(id);
        loaded.Process.Meta["title"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(Processes))]
    public void EveryRuleRunsItsOwnGoldensOnAValidTable(string id)
    {
        foreach (var rule in ReferenceDocs.Rules(id))
        {
            var goldens = rule.RunGoldens();
            goldens.IsOk.Should().BeTrue(because: id + "/" + rule.Id + ": " + string.Join(", ", goldens.Failed.Select(item => item.Name)) + " " + string.Join(",", goldens.UncoveredRowKeys));
            rule.ValidateTable().Errors.Should().BeEmpty(because: id + "/" + rule.Id);
        }
    }

    [Theory]
    [MemberData(nameof(Processes))]
    public void EveryScenarioPasses(string id)
    {
        var result = ReferenceDocs.Process(id).RunScenarios();

        result.Results.Should().NotBeEmpty();
        result.Results.Where(item => !item.Passed).Select(item => item.Name + ": " + item.Failure).Should().BeEmpty();
        result.Results.Select(item => item.Name).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [MemberData(nameof(Processes))]
    public void TheIndependentPlayerEndsEveryScenarioWhereTheDocumentSaysItDoes(string id)
    {
        var process = ReferenceDocs.Process(id);
        foreach (var scenario in Scenarios(id))
        {
            var run = ScenarioTracer.Play(process, scenario);
            var expect = scenario["expect"]!.AsObject();

            run.Final.Status.ToString().Should().Be(expect["status"]!.GetValue<string>(), because: scenario["name"]!.GetValue<string>());
            run.Final.Result.Should().Be(expect["result"]?.GetValue<string>(), because: scenario["name"]!.GetValue<string>());
            run.Trace.Should().Equal(expect["trace"]!.AsArray().Select(node => node!.GetValue<string>()), because: scenario["name"]!.GetValue<string>());
        }
    }

    [Theory]
    [MemberData(nameof(Processes))]
    public void EveryRouteOutOfEveryBranchingStepIsTakenByAScenario(string id)
    {
        var process = ReferenceDocs.Process(id);
        var steps = ReferenceDocs.ProcessJson(id)["body"]!["steps"]!.AsObject();
        var taken = new HashSet<(string Step, string Label)>();
        foreach (var scenario in Scenarios(id))
        {
            var run = ScenarioTracer.Play(process, scenario);
            foreach (var route in RouteMap.Taken(steps, run.History))
            {
                taken.Add(route);
            }
        }

        var branching = steps.Where(entry => RouteMap.Declared(entry.Value!.AsObject()).Count > 1).ToList();
        var required = branching.SelectMany(entry => RouteMap.Declared(entry.Value!.AsObject()).Select(label => (Step: entry.Key, Label: label))).ToList();
        var missed = required.Where(route => !taken.Contains(route)).Select(route => route.Step + " " + route.Label).ToList();

        output.WriteLine($"{id}: {branching.Count} branching steps, {required.Count} routes, {required.Count - missed.Count} taken, {steps.Count} steps, {Scenarios(id).Count()} scenarios");
        missed.Should().BeEmpty(because: "every route of a step with more than one route needs a scenario");
        required.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Processes))]
    public void EveryStepIsEnteredByAScenario(string id)
    {
        var process = ReferenceDocs.Process(id);
        var entered = Scenarios(id).SelectMany(scenario => ScenarioTracer.Play(process, scenario).Trace).ToHashSet(StringComparer.Ordinal);

        process.StepIds.Where(step => !entered.Contains(step)).Should().BeEmpty();
    }

    [Fact]
    public void EveryActionIsAContactActionThatNeverRepeatsAndSendsAnUnknownOutcomeToAPerson()
    {
        var actions = 0;
        foreach (var id in ReferenceDocs.Ids)
        {
            var steps = ReferenceDocs.ProcessJson(id)["body"]!["steps"]!.AsObject();
            foreach (var (name, node) in steps.Where(entry => entry.Value!["kind"]!.GetValue<string>() == "action"))
            {
                var step = node!.AsObject();
                actions++;
                step["contact"]!.GetValue<bool>().Should().BeTrue(because: id + "/" + name);
                step["delivery"]!.GetValue<string>().Should().Be("atMostOnce", because: id + "/" + name);
                step["window"]!.GetValue<string>().Should().NotBeNullOrEmpty();
                steps[step["onUnknown"]!.GetValue<string>()]!["kind"]!.GetValue<string>().Should().Be("humanTask", because: id + "/" + name);
            }
        }

        actions.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TheCollectionProcessOnlyObserves()
    {
        var steps = ReferenceDocs.ProcessJson("collection.followup")["body"]!["steps"]!.AsObject();

        steps.Select(entry => entry.Value!["kind"]!.GetValue<string>()).Should().NotContain("action", because: "a claim is never submitted or changed by the process");
        steps.Where(entry => entry.Value!["kind"]!.GetValue<string>() == "read" || entry.Value["kind"]!.GetValue<string>() == "poll")
            .Select(entry => entry.Value!["port"]!.GetValue<string>())
            .Should().OnlyContain(port => port == "claims.status");
        steps.Where(entry => entry.Value!["kind"]!.GetValue<string>() == "wait").Select(entry => entry.Value!["event"]!.GetValue<string>())
            .Should().Equal("claim.submitted");
    }

    [Fact]
    public void NothingReachesTheEndOfTheCodingReviewWithoutThePhysicianReview()
    {
        var steps = ReferenceDocs.ProcessJson("postvisit.coding_review")["body"]!["steps"]!.AsObject();
        var review = steps.Single(entry => entry.Value!["kind"]!.GetValue<string>() == "humanTask" && entry.Value["queue"]!.GetValue<string>() == "physician_review").Key;
        var approvedEnds = steps.Where(entry => entry.Value!["kind"]!.GetValue<string>() == "end" && entry.Value["result"]!.GetValue<string>() == "coding_approved").Select(entry => entry.Key).ToList();

        approvedEnds.Should().NotBeEmpty();
        Reachable(steps, ReferenceDocs.ProcessJson("postvisit.coding_review")["body"]!["start"]!.GetValue<string>(), review).Should().NotContain(approvedEnds);
        steps[review]!["outcomes"]!.AsArray().Select(node => node!.GetValue<string>()).Should().Equal("approved", "changed", "rejected");
        steps[review]!["timeout"].Should().BeNull(because: "an unanswered review waits; it never expires into an approval");
    }

    [Fact]
    public void TheAiStepIsUnderContractAndItsOutputIsJudgedByARule()
    {
        var steps = ReferenceDocs.ProcessJson("postvisit.coding_review")["body"]!["steps"]!.AsObject();
        var ai = steps.Single(entry => entry.Value!["kind"]!.GetValue<string>() == "ai");
        var step = ai.Value!.AsObject();

        step["model"]!.GetValue<string>().Should().Be("model-a");
        step["prompt"]!["id"]!.GetValue<string>().Should().NotBeNullOrEmpty();
        step["prompt"]!["version"]!.GetValue<int>().Should().BePositive();
        step["inputSchema"].Should().NotBeNull();
        step["outputSchema"].Should().NotBeNull();
        steps[step["next"]!.GetValue<string>()]!["kind"]!.GetValue<string>().Should().Be("decide");
        steps[step["onInvalid"]!.GetValue<string>()]!["queue"]!.GetValue<string>().Should().Be("physician_review");
    }

    [Fact]
    public void TheLifecycleStartsFromTheOpeningOfACaseAndRunsOneChildPerStage()
    {
        var process = ReferenceDocs.Process("case.lifecycle");
        var steps = ReferenceDocs.ProcessJson("case.lifecycle")["body"]!["steps"]!.AsObject();

        var trigger = process.Triggers.Should().ContainSingle().Subject.Should().BeOfType<EventTriggerDeclaration>().Subject;
        trigger.Event.Should().Be("case.opened");
        trigger.BusinessKeyMember.Should().Be("case_ref");
        trigger.Input.Should().ContainKey("case_ref");
        process.Stages.Should().Equal("previsit", "visit", "postvisit", "collection");
        var children = steps.Where(entry => entry.Value!["kind"]!.GetValue<string>() == "subprocess").ToList();
        children.Select(entry => entry.Value!["process"]!.GetValue<string>()).Should().Equal("previsit.outreach", "visit.note_wait", "postvisit.coding_review", "collection.followup");
        children.Select(entry => entry.Value!["stage"]!.GetValue<string>()).Should().Equal(process.Stages);
        children.Should().OnlyContain(entry => entry.Value!["onError"] != null);
    }

    [Fact]
    public void TheCollectionStageStartsOnlyFromAnApprovedCodingReview()
    {
        var steps = ReferenceDocs.ProcessJson("case.lifecycle")["body"]!["steps"]!.AsObject();

        var into = steps
            .SelectMany(entry => RouteMap.Declared(entry.Value!.AsObject()).Select(label => (Step: entry.Key, Label: label, Target: RouteMap.Target(entry.Value.AsObject(), label))))
            .Where(route => route.Target == "collection")
            .Select(route => (route.Step, route.Label))
            .ToList();

        into.Should().ContainSingle().Which.Should().Be(("postvisit", "on:coding_approved"));
    }

    [Fact]
    public void TheDailyRefreshIsAScheduleOnTheLocalDateOfTheActivationZone()
    {
        var process = ReferenceDocs.Process("practice.daily_refresh");

        var schedule = process.Triggers.Should().ContainSingle().Subject.Should().BeOfType<ScheduleTriggerDeclaration>().Subject;
        schedule.Id.Should().Be("daily");
        schedule.Cron.Should().Be("0 6 * * *");
        schedule.Zone.Should().BeNull(because: "the zone comes from the activation");
        schedule.Misfire.Should().Be(ProcessMisfire.FireOnce);
        schedule.Input.Should().Equal(new Dictionary<string, string> { ["run_for"] = "scheduledDate" });
        ReferenceDocs.ProcessJson("practice.daily_refresh")["body"]!["variables"]!["run_for"]!["type"]!.GetValue<string>().Should().Be("Date");
        ReferenceDocs.ProcessJson("practice.daily_refresh")["body"]!["variables"]!["cases"]!["type"]!.GetValue<string>().Should().Be("Json");
    }

    [Theory]
    [MemberData(nameof(Processes))]
    public void TheDocumentsAreAsciiWithoutEmailAddressesOrLinks(string id)
    {
        var texts = new List<string> { ReferenceDocs.ProcessText(id) };
        texts.AddRange(ReferenceDocs.RuleFiles(id).Select(file => File.ReadAllText(Path.Combine(ReferenceDocs.Folder(id), file))));
        foreach (var text in texts)
        {
            text.Should().MatchRegex("^[\\x09\\x0A\\x0D\\x20-\\x7E]*$");
            text.Should().NotMatchRegex("[A-Za-z0-9._-]+@[A-Za-z]").And.NotContain("http").And.NotContain("www.");
        }
    }

    [Fact]
    public void TheReadmeNamesEveryPortEventQueueOutcomeAndResultOfEveryDocument()
    {
        var readme = TestFiles.Read(ReferenceDocs.Root + "README.md");
        foreach (var id in ReferenceDocs.Ids)
        {
            var body = ReferenceDocs.ProcessJson(id)["body"]!;
            var names = new HashSet<string>(StringComparer.Ordinal) { id };
            foreach (var step in body["steps"]!.AsObject().Select(entry => entry.Value!.AsObject()))
            {
                foreach (var member in new[] { "port", "event", "queue", "result", "process" })
                {
                    if (step[member] is JsonValue value)
                    {
                        names.Add(value.GetValue<string>());
                    }
                }

                foreach (var outcome in step["outcomes"]?.AsArray() ?? [])
                {
                    names.Add(outcome!.GetValue<string>());
                }
            }

            foreach (var name in names)
            {
                readme.Should().Contain("`" + name + "`", because: id + " uses " + name);
            }
        }
    }

    private static IEnumerable<JsonObject> Scenarios(string id) => ReferenceDocs.ProcessJson(id)["body"]!["scenarios"]!.AsArray().Select(node => node!.AsObject());

    /// <summary>Every step reachable from <paramref name="start"/> without passing through <paramref name="without"/>.</summary>
    private static HashSet<string> Reachable(JsonObject steps, string start, string without)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([start]);
        while (queue.TryDequeue(out var current))
        {
            if (current == without || !seen.Add(current))
            {
                continue;
            }

            var step = steps[current]!.AsObject();
            foreach (var label in RouteMap.Declared(step))
            {
                queue.Enqueue(RouteMap.Target(step, label));
            }
        }

        return seen;
    }
}

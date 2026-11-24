using System.Text;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>The differ on pairs of documents covering every kind of change, with golden texts in both languages.</summary>
public sealed class FlowDifferTests
{
    internal sealed record Pair(string Name, string Doc, string Patch, FlowChangeKind Kind, FlowAspect? Aspect = null, bool KeepTests = false);

    private const string NW = "visit.note_wait";
    private const string PV = "previsit.outreach";
    private const string CF = "collection.followup";
    private const string PC = "postvisit.coding_review";
    private const string CL = "case.lifecycle";
    private const string DR = "practice.daily_refresh";

    internal static readonly Pair[] Pairs =
    [
        new("step added and start changed", NW, """
            [{"op":"add","path":"/body/steps/pause","value":{"kind":"timer","stage":"visit","after":"PT1H","next":"await_note"}},
             {"op":"replace","path":"/body/start","value":"pause"}]
            """, FlowChangeKind.StepAdded),
        new("start changed", NW, """
            [{"op":"add","path":"/body/steps/pause","value":{"kind":"timer","stage":"visit","after":"PT1H","next":"await_note"}},
             {"op":"replace","path":"/body/start","value":"pause"}]
            """, FlowChangeKind.StartChanged),
        new("step removed", NW, """
            [{"op":"replace","path":"/body/steps/await_note/onNotAvailable","value":"chase_note"},
             {"op":"replace","path":"/body/steps/await_note/onNotEntitled","value":"chase_note"},
             {"op":"remove","path":"/body/steps/confirm_note"}]
            """, FlowChangeKind.StepRemoved),
        new("step renamed", NW, """
            [{"op":"move","from":"/body/steps/chase_note","path":"/body/steps/call_front_desk"},
             {"op":"replace","path":"/body/steps/await_note/onExpired","value":"call_front_desk"},
             {"op":"replace","path":"/body/steps/await_note/onError","value":"call_front_desk"}]
            """, FlowChangeKind.StepRenamed),
        new("route changed", NW, """[{"op":"replace","path":"/body/steps/await_note/onNotAvailable","value":"chase_note"}]""", FlowChangeKind.RouteChanged),
        new("route added", NW, """
            [{"op":"add","path":"/body/steps/chase_note/timeout","value":"PT24H"},
             {"op":"add","path":"/body/steps/chase_note/onTimeout","value":"cancelled"}]
            """, FlowChangeKind.RouteAdded),
        new("route removed", NW, """[{"op":"remove","path":"/body/steps/await_note/onNotEntitled"}]""", FlowChangeKind.RouteRemoved),
        new("window added", PV, """[{"op":"add","path":"/body/windows/late","value":{"zone":{"variable":"practice_zone"},"days":["sat"],"from":"09:00","to":"12:00"}}]""", FlowChangeKind.WindowAdded),
        new("window removed", PV, """
            [{"op":"remove","path":"/body/steps/send/window"},{"op":"replace","path":"/body/steps/send/contact","value":false},{"op":"remove","path":"/body/windows/contact_hours"}]
            """, FlowChangeKind.WindowRemoved),
        new("window changed", PV, """[{"op":"replace","path":"/body/windows/contact_hours/to","value":"17:00"}]""", FlowChangeKind.WindowChanged),
        new("trigger added", DR, """[{"op":"add","path":"/body/triggers/-","value":{"kind":"event","event":"practice.opened","businessKey":"practice_id"}}]""", FlowChangeKind.TriggerAdded),
        new("trigger removed", DR, """[{"op":"remove","path":"/body/triggers/0"}]""", FlowChangeKind.TriggerRemoved),
        new("trigger changed", DR, """[{"op":"replace","path":"/body/triggers/0/cron","value":"30 7 * * *"}]""", FlowChangeKind.TriggerChanged),
        new("variable added", NW, """[{"op":"add","path":"/body/variables/spare","value":{"type":"Code"}}]""", FlowChangeKind.VariableAdded),
        new("show list shortened", NW, """[{"op":"remove","path":"/body/steps/chase_note/show/1"}]""", FlowChangeKind.StepChanged),
        new("variable dropped", PC, """[{"op":"remove","path":"/body/variables/final_code"},{"op":"remove","path":"/body/steps/physician_review/output"}]""", FlowChangeKind.VariableRemoved),
        new("variable changed", NW, """[{"op":"replace","path":"/body/variables/case_ref/input","value":false},{"op":"add","path":"/body/variables/case_ref/initial","value":"c-0"}]""", FlowChangeKind.VariableChanged),
        new("process id changed", NW, """[{"op":"replace","path":"/id","value":"visit.note_wait_two"}]""", FlowChangeKind.ProcessIdChanged),
        new("title changed", NW, """[{"op":"replace","path":"/meta/title","value":"Visit note follow-up"}]""", FlowChangeKind.TitleChanged),
        new("description changed", NW, """[{"op":"replace","path":"/meta/description","value":"Waits for the note of the visit."}]""", FlowChangeKind.DescriptionChanged),
        new("subject changed", NW, """[{"op":"replace","path":"/body/subject","value":"visit"}]""", FlowChangeKind.SubjectChanged),
        new("something else changed", NW, """[{"op":"add","path":"/body/stages/-","value":"extra"}]""", FlowChangeKind.SomethingElseChanged),
        new("label changed", NW, """[{"op":"replace","path":"/meta/display/en/title","value":"Wait for the visit note"}]""", FlowChangeKind.LabelChanged),
        new("label added", NW, """[{"op":"add","path":"/meta/display/en/steps/signed","value":"Signed"}]""", FlowChangeKind.LabelChanged),
        new("label removed", NW, """[{"op":"remove","path":"/meta/display/es/steps/cancelled"}]""", FlowChangeKind.LabelChanged),
        new("test added", NW, """
            [{"op":"add","path":"/body/scenarios/-","value":{"name":"the note is signed on the second read","start":{"at":"2027-01-04T14:00:00Z","today":"2027-01-04","input":{"case_ref":"c-1002"}},"events":[{"complete":"await_note","output":{"status":"draft"}},{"advance":"PT1H"},{"complete":"await_note","output":{"status":"signed"}}],"expect":{"status":"Completed","result":"note_signed","trace":["await_note","signed"]}}}]
            """, FlowChangeKind.ScenarioAdded, KeepTests: true),
        new("test removed", NW, """[{"op":"remove","path":"/body/scenarios/3"}]""", FlowChangeKind.ScenarioRemoved, KeepTests: true),
        new("test changed", NW, """[{"op":"replace","path":"/body/scenarios/0/events/0/output/status","value":"signed"},{"op":"replace","path":"/body/scenarios/0/start/input/case_ref","value":"c-1002"}]""", FlowChangeKind.ScenarioChanged, KeepTests: true),
        new("stage", CL, """[{"op":"replace","path":"/body/steps/ops_visit/stage","value":"previsit"}]""", FlowChangeKind.StepChanged, FlowAspect.Stage),
        new("port", NW, """[{"op":"replace","path":"/body/steps/await_note/port","value":"records.note_state"}]""", FlowChangeKind.StepChanged, FlowAspect.Port),
        new("delivery", PV, """[{"op":"replace","path":"/body/steps/send/delivery","value":"atLeastOnce"},{"op":"remove","path":"/body/steps/send/onUnknown"}]""", FlowChangeKind.StepChanged, FlowAspect.Delivery),
        new("contact", PV, """[{"op":"replace","path":"/body/steps/send/contact","value":false}]""", FlowChangeKind.StepChanged, FlowAspect.Contact),
        new("window of a step", PV, """[{"op":"add","path":"/body/windows/late","value":{"zone":{"variable":"practice_zone"},"days":["sat"],"from":"09:00","to":"12:00"}},{"op":"replace","path":"/body/steps/send/window","value":"late"}]""", FlowChangeKind.StepChanged, FlowAspect.Window),
        new("interrupt", PV, """[{"op":"add","path":"/body/steps/send/interruptBy","value":{"event":"case.reply_received","correlation":"case_ref","to":"done"}}]""", FlowChangeKind.StepChanged, FlowAspect.Interrupt),
        new("retry", NW, """[{"op":"replace","path":"/body/steps/await_note/retry/maxAttempts","value":5}]""", FlowChangeKind.StepChanged, FlowAspect.Retry),
        new("collect into", PC, """[{"op":"add","path":"/body/variables/opportunities2","value":{"type":"Json"}},{"op":"replace","path":"/body/steps/find_opportunities/collectInto","value":"opportunities2"}]""", FlowChangeKind.StepChanged, FlowAspect.CollectInto),
        new("queue", NW, """[{"op":"replace","path":"/body/steps/chase_note/queue","value":"note_confirmation"}]""", FlowChangeKind.StepChanged, FlowAspect.Queue),
        new("outcomes", NW, """[{"op":"add","path":"/body/steps/chase_note/outcomes/-","value":"postponed"},{"op":"add","path":"/body/steps/chase_note/on/postponed","value":"cancelled"}]""", FlowChangeKind.StepChanged, FlowAspect.Outcomes),
        new("show", NW, """[{"op":"add","path":"/body/steps/chase_note/show/-","value":"note_status"}]""", FlowChangeKind.StepChanged, FlowAspect.Show),
        new("event", PV, """[{"op":"replace","path":"/body/steps/reply/event","value":"case.answer_received"}]""", FlowChangeKind.StepChanged, FlowAspect.Event),
        new("correlation", PV, """[{"op":"replace","path":"/body/steps/reply/correlation","value":"practice_zone"}]""", FlowChangeKind.StepChanged, FlowAspect.Correlation),
        new("timeout", PV, """[{"op":"replace","path":"/body/steps/reply/timeout","value":"PT48H"}]""", FlowChangeKind.StepChanged, FlowAspect.Timeout),
        new("timer", CF, """[{"op":"replace","path":"/body/steps/balance_wait/after","value":"P5D"}]""", FlowChangeKind.StepChanged, FlowAspect.Timer),
        new("loop", PV, """[{"op":"replace","path":"/body/steps/cycle/maxIterations","value":5}]""", FlowChangeKind.StepChanged, FlowAspect.Loop),
        new("poll", CF, """[{"op":"replace","path":"/body/steps/poll_claim/every","value":"PT12H"}]""", FlowChangeKind.StepChanged, FlowAspect.Poll),
        new("result", NW, """[{"op":"replace","path":"/body/steps/signed/result","value":"note_is_signed"}]""", FlowChangeKind.StepChanged, FlowAspect.Result),
        new("ai", PC, """[{"op":"replace","path":"/body/steps/suggest/model","value":"model-b"}]""", FlowChangeKind.StepChanged, FlowAspect.Ai),
        new("ai input schema", PC, """[{"op":"add","path":"/body/steps/suggest/inputSchema/properties/note","value":{"type":"string"}}]""", FlowChangeKind.StepChanged, FlowAspect.AiInputSchema),
        new("ai output schema", PC, """[{"op":"add","path":"/body/steps/suggest/outputSchema/properties/reason","value":{"type":"string"}}]""", FlowChangeKind.StepChanged, FlowAspect.AiOutputSchema),
        new("inputs", CL, """[{"op":"remove","path":"/body/steps/visit/input/case_ref"},{"op":"add","path":"/body/steps/visit/input/case_ref","value":"case_ref"},{"op":"replace","path":"/body/steps/previsit/input/practice_zone","value":"case_ref"}]""", FlowChangeKind.StepChanged, FlowAspect.Inputs),
        new("facts", PV, """[{"op":"add","path":"/body/variables/missing2","value":{"type":"Integer"}},{"op":"replace","path":"/body/steps/assess/facts/missing","value":"missing2"}]""", FlowChangeKind.StepChanged, FlowAspect.Facts),
        new("outputs", PV, """[{"op":"add","path":"/body/variables/missing2","value":{"type":"Integer"}},{"op":"replace","path":"/body/steps/check/output/missing_count","value":"missing2"}]""", FlowChangeKind.StepChanged, FlowAspect.Outputs),
        new("child", CL, """[{"op":"replace","path":"/body/steps/visit/process","value":"practice.daily_refresh"},{"op":"replace","path":"/body/steps/visit/on","value":{"refreshed":"postvisit","failed":"closed_no_visit"}},{"op":"remove","path":"/body/steps/visit/input"},{"op":"replace","path":"/uses/2","value":{"$ref":"process/practice.daily_refresh"}}]""", FlowChangeKind.StepChanged, FlowAspect.Child),
        new("kind", NW, """
            [{"op":"replace","path":"/body/steps/chase_note","value":{"kind":"timer","stage":"visit","after":"P1D","next":"signed"}}]
            """, FlowChangeKind.StepChanged, FlowAspect.Kind),
        new("rule", PC, """[{"op":"replace","path":"/body/steps/check_suggestion/rule","value":"postvisit.assist_gate"},{"op":"replace","path":"/body/steps/check_suggestion/facts","value":{"assist_enabled":"assist_enabled","items":"items"}},{"op":"remove","path":"/body/steps/check_suggestion/output"},{"op":"add","path":"/body/steps/check_suggestion/route","value":"use"},{"op":"add","path":"/body/steps/check_suggestion/on","value":{"yes":"physician_review","no":"physician_review"}},{"op":"remove","path":"/body/steps/check_suggestion/next"},{"op":"remove","path":"/uses/2"}]""", FlowChangeKind.StepChanged, FlowAspect.Rule),
    ];

    public static IEnumerable<object[]> Names => Pairs.Select(pair => new object[] { pair.Name });

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_pair_loads_and_shows_the_change_it_is_named_for(string name)
    {
        var pair = Pairs.Single(item => item.Name == name);
        var (before, after) = Load(pair);
        var diff = FlowDiffer.Diff(before, after, Document(pair, null), Document(pair, pair.Patch));
        diff.Changes.Should().Contain(change => change.Kind == pair.Kind, "the pair {0} is meant to show {1}; it shows {2}", name, pair.Kind, string.Join(", ", diff.Changes.Select(change => change.Kind + "/" + change.Aspect)));
        if (pair.Aspect is { } aspect)
        {
            diff.Changes.Should().Contain(change => change.Kind == pair.Kind && change.Aspect == aspect);
        }
    }

    [Fact]
    public void Together_the_pairs_show_every_kind_of_change_and_every_aspect()
    {
        var kinds = new HashSet<FlowChangeKind>();
        var aspects = new HashSet<FlowAspect>();
        foreach (var pair in Pairs)
        {
            var (before, after) = Load(pair);
            foreach (var change in FlowDiffer.Diff(before, after, Document(pair, null), Document(pair, pair.Patch)).Changes)
            {
                _ = kinds.Add(change.Kind);
                if (change.Aspect is { } aspect)
                {
                    _ = aspects.Add(aspect);
                }
            }
        }

        kinds.Should().BeEquivalentTo(Enum.GetValues<FlowChangeKind>());
        aspects.Should().BeEquivalentTo(Enum.GetValues<FlowAspect>());
    }

    [Fact]
    public void A_document_compared_with_itself_has_no_changes()
    {
        var process = ReferenceDocs.Load(NW);
        var diff = FlowDiffer.Diff(process, ReferenceDocs.Load(NW));
        diff.Changes.Should().BeEmpty();
        FlowChangeDescriber.Describe(process, process, WorkbenchLocale.En).Items.Should().BeEmpty();
    }

    [Fact]
    public void The_texts_are_golden_in_both_languages()
    {
        foreach (var locale in new[] { WorkbenchLocale.En, WorkbenchLocale.Es })
        {
            var text = new StringBuilder();
            foreach (var pair in Pairs)
            {
                var (before, after) = Load(pair);
                var view = FlowChangeDescriber.Describe(before, after, locale, Document(pair, null), Document(pair, pair.Patch));
                _ = text.Append("### ").Append(pair.Name).Append('\n').Append(view.Summary).Append('\n');
                foreach (var item in view.Items)
                {
                    _ = text.Append("- ").Append(item.Text).Append('\n');
                }

                foreach (var item in view.Behaviour)
                {
                    _ = text.Append("* ").Append(item.Text).Append('\n');
                }

                _ = text.Append('\n');
            }

            Golden.Check("differ." + AuthoringJson.Tag(locale) + ".txt", text.ToString());
        }
    }

    [Fact]
    public void Replaying_the_tests_names_the_ones_that_now_end_differently()
    {
        var before = ReferenceDocs.Load(NW);
        var after = ReferenceDocs.Load(NW, """[{"op":"replace","path":"/body/steps/chase_note/on/signed","value":"cancelled"}]""", withoutTests: false);
        var view = FlowChangeDescriber.Describe(before, after, WorkbenchLocale.En);
        view.Behaviour.Should().NotBeEmpty();
        view.Behaviour.Should().Contain(item => item.Kind == FlowBehaviourKind.DifferentEnding && item.Scenario.Contains("front desk learns", StringComparison.Ordinal));
        view.TestsUnchanged.Should().BeLessThan(view.TestsReplayed);
        FlowChangeDescriber.Describe(before, after, WorkbenchLocale.Es).Behaviour.Should().HaveCount(view.Behaviour.Count);
    }

    [Fact]
    public void A_step_that_no_longer_exists_makes_the_test_that_used_it_not_apply()
    {
        var before = ReferenceDocs.Load(NW);
        var withoutBrokenTests = ReferenceDocs.Load(NW, """
            [{"op":"replace","path":"/body/steps/await_note/onNotAvailable","value":"chase_note"},
             {"op":"replace","path":"/body/steps/await_note/onNotEntitled","value":"chase_note"},
             {"op":"remove","path":"/body/steps/confirm_note"}]
            """, withoutTests: true);
        var view = FlowChangeDescriber.Describe(before, withoutBrokenTests, WorkbenchLocale.En);
        view.Behaviour.Should().Contain(item => item.Kind == FlowBehaviourKind.NoLongerApplies && item.Scenario.Contains("confirms the note by hand", StringComparison.Ordinal));
        view.TestsReplayed.Should().Be(before.RunScenarios().Results.Count);
    }

    private static System.Text.Json.Nodes.JsonObject? Document(Pair pair, string? patch) =>
        AuthoringJson.TryObject(ReferenceDocs.Changed(pair.Doc, patch, !pair.KeepTests));

    private static (BrainProcess Before, BrainProcess After) Load(Pair pair) =>
        (ReferenceDocs.Load(pair.Doc, null, !pair.KeepTests), ReferenceDocs.Load(pair.Doc, pair.Patch, !pair.KeepTests));
}

/// <summary>Golden texts under <c>Golden/</c>; set <c>BRAIN_UPDATE_GOLDEN=1</c> to write them again after a deliberate change.</summary>
internal static class Golden
{
    internal static void Check(string file, string actual)
    {
        var path = Path.Combine(Fixtures.Locate("tests/Synapbiz.Brain.Authoring.Tests/Synapbiz.Brain.Authoring.Tests.csproj") is { } project ? Path.GetDirectoryName(project)! : string.Empty, "Golden", file);
        if (Environment.GetEnvironmentVariable("BRAIN_UPDATE_GOLDEN") == "1")
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual, new UTF8Encoding(false));
        }

        File.Exists(path).Should().BeTrue("the golden file " + file + " must exist; run with BRAIN_UPDATE_GOLDEN=1 once to write it");
        File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Should().Be(actual);
    }
}

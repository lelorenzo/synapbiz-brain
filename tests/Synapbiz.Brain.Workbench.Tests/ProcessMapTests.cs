using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

/// <summary>The headless process map: goldens in both languages on the six reference documents and on the synthetic fixtures, and the properties each fixture exists for.</summary>
public sealed class ProcessMapTests
{
    private static ProcessMap Reference(WorkbenchLocale locale) => ProcessMap.From(FlowDocs.Reference.Select(item => item.Process), locale);

    private static ProcessMap Fixtures(WorkbenchLocale locale) => ProcessMap.From(MapDocs.All, locale, MapDocs.HostLabels(locale));

    [Theory]
    [InlineData(WorkbenchLocale.En, "en")]
    [InlineData(WorkbenchLocale.Es, "es")]
    public void TheMapOfTheSixReferenceDocumentsIsTheGoldenText(WorkbenchLocale locale, string language)
    {
        var actual = MapDocs.Render(Reference(locale));
        MapDocs.Golden("map.reference." + language + ".txt", actual).Should().Be(actual);
    }

    [Theory]
    [InlineData(WorkbenchLocale.En, "en")]
    [InlineData(WorkbenchLocale.Es, "es")]
    public void TheMapOfTheSyntheticFixturesIsTheGoldenText(WorkbenchLocale locale, string language)
    {
        var actual = MapDocs.Render(Fixtures(locale));
        MapDocs.Golden("map.fixtures." + language + ".txt", actual).Should().Be(actual);
    }

    [Fact]
    public void TheReferenceMapHasTheLifecycleAsItsOnlyMainProcessWithFourPhasesAndOneChildEach()
    {
        var map = Reference(WorkbenchLocale.En);
        var main = map.Mains.Should().ContainSingle().Subject;
        main.Process.Id.Should().Be("case.lifecycle");
        main.Phases.Select(phase => phase.Label).Should().Equal("Before the visit", "Visit", "After the visit", "Collection");
        main.Phases.Select(phase => phase.Subprocesses.Single().Process.Id).Should().Equal("previsit.outreach", "visit.note_wait", "postvisit.coding_review", "collection.followup");
        main.Phases[0].EndsWhen.Should().Be("Ends when the process moves on to Visit and Collection.");
        main.Starts.Should().ContainSingle().Which.Should().Contain("case.opened");
        map.Scheduled.Should().ContainSingle().Which.Process.Id.Should().Be("practice.daily_refresh");
        map.SubprocessCount.Should().Be(4);
        map.Cycles.Should().BeEmpty();
        map.Detached.Should().BeEmpty();
    }

    [Fact]
    public void ASubprocessStartedByTwoParentsAppearsUnderEachAndSaysWhoElseStartsIt()
    {
        var map = Fixtures(WorkbenchLocale.En);
        var front = map.Mains.Single(main => main.Process.Id == "map.front");
        var second = map.Mains.Single(main => main.Process.Id == "map.second");
        var underFront = front.Phases.SelectMany(phase => phase.Subprocesses).Single(child => child.Process.Id == "map.serve");
        var underSecond = second.Phases.SelectMany(phase => phase.Subprocesses).Single(child => child.Process.Id == "map.serve");
        underFront.AlsoStartedBy.Should().Equal("Second main process");
        underSecond.AlsoStartedBy.Should().Equal("Front process");
        var notify = front.Phases.SelectMany(phase => phase.Subprocesses).Single(child => child.Process.Id == "map.notify");
        notify.AlsoStartedBy.Should().Equal("Morning sweep");
        map.Process("map.serve")!.StartedBy.Should().Equal("map.front", "map.second");
    }

    [Fact]
    public void AStepWithNoStageGoesUnderNoPhaseAndAStageWithNoSubprocessIsStillAPhase()
    {
        var map = Fixtures(WorkbenchLocale.En);
        var front = map.Mains.Single(main => main.Process.Id == "map.front");
        front.Phases.Select(phase => phase.StageId).Should().Equal("intake", "service", null);
        front.Phases[2].IsLoose.Should().BeTrue();
        front.Phases[2].Subprocesses.Single().Process.Id.Should().Be("map.loose");
        map.Mains.Single(main => main.Process.Id == "map.second").Phases.Should().ContainSingle().Which.IsLoose.Should().BeTrue();
        map.Mains.Single(main => main.Process.Id == "map.nostart").Phases.Should().BeEmpty();
    }

    [Fact]
    public void ACycleBetweenProcessesIsReportedOnceAndItsMembersAreDetachedNotDrawnTwice()
    {
        var map = Fixtures(WorkbenchLocale.En);
        map.Cycles.Should().ContainSingle().Which.Should().Equal("map.loop_a", "map.loop_b");
        map.Detached.Select(process => process.Id).Should().Equal("map.loop_a", "map.loop_b");
        map.Mains.SelectMany(main => main.Phases).SelectMany(phase => phase.Subprocesses).Select(child => child.Process.Id).Should().NotContain(["map.loop_a", "map.loop_b"]);
    }

    internal static void SelfStart(System.Text.Json.Nodes.JsonObject document)
    {
        document["body"]!["steps"]!["go"]!["process"] = "map.loop_a";
        document["uses"]![0]!["$ref"] = "process/map.loop_a";
    }

    [Fact]
    public void AProcessThatStartsItselfIsACycleOfOne()
    {
        var documents = MapDocs.With("map.loop_a", SelfStart);
        var map = ProcessMap.From(documents, WorkbenchLocale.En);
        map.Cycles.Should().Contain(cycle => cycle.Count == 1 && cycle[0] == "map.loop_a");
    }

    [Fact]
    public void AMainProcessWithNoStartIsMarkedAndHasNoStartSentence()
    {
        var main = Fixtures(WorkbenchLocale.En).Mains.Single(item => item.Process.Id == "map.nostart");
        main.Process.NoStartDefined.Should().BeTrue();
        main.Starts.Should().BeEmpty();
        main.Process.Triggers.Should().BeEmpty();
        Fixtures(WorkbenchLocale.En).Mains.Single(item => item.Process.Id == "map.front").Process.NoStartDefined.Should().BeFalse();
    }

    [Fact]
    public void ALabelComesFromTheDocumentThenTheHostThenTheRawName()
    {
        var check = Fixtures(WorkbenchLocale.En).Process("map.check")!;
        var lab = check.Uses.Single(use => use.Kind == MapPieceKind.PersonTask);
        (lab.Label, lab.LabelIsName).Should().Be(("Lab desk", false));
        var records = check.Uses.Single(use => use.Name == "map.records");
        (records.Label, records.LabelIsName).Should().Be(("Records system", false));
        var mailer = check.Uses.Single(use => use.Name == "map.mailer");
        (mailer.Label, mailer.LabelIsName).Should().Be(("map.mailer", true));
        var rule = check.Uses.Single(use => use.Kind == MapPieceKind.Rule);
        (rule.Label, rule.LabelIsName).Should().Be(("conf.flag_rule", true));
        Fixtures(WorkbenchLocale.En).Process("map.serve")!.Uses.Single(use => use.Kind == MapPieceKind.Rule).Label.Should().Be("Level by count");
        Fixtures(WorkbenchLocale.Es).Process("map.check")!.Uses.Single(use => use.Kind == MapPieceKind.PersonTask).Label.Should().Be("Mesa de laboratorio");
        var withoutHost = ProcessMap.From(MapDocs.All, WorkbenchLocale.En).Process("map.check")!;
        withoutHost.Uses.Single(use => use.Name == "map.records").LabelIsName.Should().BeTrue();
        // The document's own label wins over the host's.
        var overruled = ProcessMap.From(MapDocs.All, WorkbenchLocale.En, new Dictionary<string, string> { [ProcessMap.LabelKey(MapPieceKind.PersonTask, "lab")] = "Host lab" }).Process("map.check")!;
        overruled.Uses.Single(use => use.Kind == MapPieceKind.PersonTask).Label.Should().Be("Lab desk");
    }

    [Fact]
    public void UsesAreDeduplicatedAndInTheFixedOrderRulesConnectorsQueuesAndAiSteps()
    {
        var map = Reference(WorkbenchLocale.En);
        foreach (var process in map.Processes)
        {
            process.Uses.Select(use => use.Kind).Should().BeInAscendingOrder(process.Id);
            process.Uses.Select(use => (use.Kind, use.Name)).Should().OnlyHaveUniqueItems(process.Id);
        }

        map.Process("postvisit.coding_review")!.Uses.Should().Contain(use => use.Kind == MapPieceKind.Ai);
        map.Process("collection.followup")!.Uses.Should().Contain(use => use.Kind == MapPieceKind.Rule && use.Name == "collection.claim_progress");
    }

    [Fact]
    public void SharedPiecesAreTheOnesMoreThanOneProcessUsesWithTheirCount()
    {
        var shared = Fixtures(WorkbenchLocale.En).Shared;
        shared.Select(item => (item.Use.Kind, item.Use.Name, item.Count)).Should().Equal(
            (MapPieceKind.Rule, "conf.default_rule", 2),
            (MapPieceKind.Connector, "map.records", 2),
            (MapPieceKind.Connector, "map.mailer", 2),
            (MapPieceKind.PersonTask, "desk", 2));
        shared.Single(item => item.Use.Name == "map.mailer").UsedBy.Should().Equal("map.check", "map.notify");
        var reference = Reference(WorkbenchLocale.En).Shared.Should().ContainSingle().Subject;
        reference.Use.Name.Should().Be("operations");
        reference.UsedBy.Should().Equal("case.lifecycle", "practice.daily_refresh");
    }

    [Fact]
    public void ACountIsWhatTheListSays()
    {
        var map = Reference(WorkbenchLocale.En);
        var entry = FlowLibrary.From(FlowDocs.Reference.Select(item => item.Process), WorkbenchLocale.En).Single(item => item.Id == "collection.followup");
        var process = map.Process("collection.followup")!;
        process.Counts.Should().Be(new MapCounts(ProcessOutline.From(FlowDocs.Process("collection.followup")).Steps.Count, entry.ScenarioCount, true, entry.TotalRoutes - entry.CoveredRoutes));
        process.Counts.RoutesNotTried.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TheMapIsTheSameWhateverTheOrderOfTheInputAndWhenTwoVersionsShareAnIdTheHighestOneCounts()
    {
        var forward = MapDocs.Render(ProcessMap.From(MapDocs.All, WorkbenchLocale.En, MapDocs.HostLabels(WorkbenchLocale.En)));
        var backward = MapDocs.Render(ProcessMap.From(MapDocs.All.Reverse(), WorkbenchLocale.En, MapDocs.HostLabels(WorkbenchLocale.En)));
        backward.Should().Be(forward);

        var v2 = MapDocs.With("map.loose", document =>
        {
            document["version"] = 2;
            document["meta"]!["display"]!["en"]!["title"] = "Loose end two";
        });
        var both = ProcessMap.From([.. MapDocs.All, v2.Single(process => process.Id == "map.loose")], WorkbenchLocale.En);
        both.Process("map.loose")!.Version.Should().Be(2);
        both.Process("map.loose")!.Title.Should().Be("Loose end two");
        both.Processes.Select(process => process.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AChildThatIsNotLoadedIsACardMarkedMissing()
    {
        var map = ProcessMap.From([MapDocs.Process("map.front")], WorkbenchLocale.En);
        var children = map.Mains.Single().Phases.SelectMany(phase => phase.Subprocesses).ToList();
        children.Should().HaveCount(4).And.OnlyContain(child => child.IsMissing && child.Process.Title == child.Process.Id);
        map.Detached.Should().BeEmpty();
    }

    private static IEnumerable<(string Name, IReadOnlyList<BrainProcess> Library)> Libraries()
    {
        yield return ("reference", [.. FlowDocs.Reference.Select(item => item.Process)]);
        yield return ("all documents", [.. FlowDocs.All.Select(item => item.Process)]);
        yield return ("map fixtures", MapDocs.All);
        yield return ("map fixtures without the front process", [.. MapDocs.All.Where(process => process.Id != "map.front")]);
        yield return ("map fixtures without the front process and the sweep", [.. MapDocs.All.Where(process => process.Id is not ("map.front" or "map.cron"))]);
        yield return ("map fixtures without the third process", [.. MapDocs.All.Where(process => process.Id != "map.third")]);
        yield return ("map fixtures with a process that starts itself", MapDocs.With("map.loop_a", SelfStart));
        yield return ("map fixtures with a cycle below a main process", MapDocs.With("map.inner", StartWrap));
        
    }

    private static readonly string[] InnerAndWrap = ["map.inner", "map.wrap"];

    internal static void StartWrap(System.Text.Json.Nodes.JsonObject document)
    {
        document["body"]!["start"] = "go";
        document["body"]!["steps"]!["go"] = System.Text.Json.Nodes.JsonNode.Parse("""{"kind":"subprocess","process":"map.wrap","input":{"ref":"ref"},"next":"done","onError":"done"}""");
        document["uses"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"$ref":"process/map.wrap"}]""");
    }

    [Theory]
    [InlineData(WorkbenchLocale.En)]
    [InlineData(WorkbenchLocale.Es)]
    public void EveryProcessOfEveryLibraryAppearsSomewhereOnTheMap(WorkbenchLocale locale)
    {
        foreach (var (name, library) in Libraries())
        {
            var map = ProcessMap.From(library, locale);
            MapDocs.Shown(map).Should().BeEquivalentTo(map.Processes.Select(process => process.Id), name);
        }

        foreach (var process in MapDocs.All)
        {
            var map = ProcessMap.From([process], locale);
            MapDocs.Shown(map).Should().BeEquivalentTo(map.Processes.Select(item => item.Id), process.Id);
        }
    }

    [Fact]
    public void ASubprocessOnlyAScheduledTaskStartsIsDrawnUnderThatTask()
    {
        var map = ProcessMap.From(MapDocs.All.Where(process => process.Id != "map.front"), WorkbenchLocale.En);
        var cron = map.Scheduled.Single(task => task.Process.Id == "map.cron");
        cron.Children.Select(child => child.Process.Id).Should().Equal("map.notify");
        map.Detached.Should().NotContain(process => process.Id == "map.notify");
        var nightly = map.Scheduled.Single(task => task.Process.Id == "map.nightly");
        nightly.Children.Select(child => child.Process.Id).Should().Equal("map.nightly_child");
        nightly.Children[0].AlsoStartedBy.Should().BeEmpty();
        nightly.Children[0].Nested.Should().BeEmpty();
        map.Scheduled.Single(task => task.Process.Id == "map.cron").Children[0].AlsoStartedBy.Should().BeEmpty();
    }

    [Fact]
    public void ASubprocessStartedOnlyByAnotherSubprocessIsNestedUnderItInDocumentOrder()
    {
        var map = Fixtures(WorkbenchLocale.En);
        var third = map.Mains.Single(main => main.Process.Id == "map.third");
        var wrap = third.Phases.SelectMany(phase => phase.Subprocesses).Should().ContainSingle().Subject;
        wrap.Process.Id.Should().Be("map.wrap");
        var inner = wrap.Nested.Should().ContainSingle().Subject;
        inner.Process.Id.Should().Be("map.inner");
        inner.Nested.Should().BeEmpty();
        map.Detached.Select(process => process.Id).Should().NotContain(["map.wrap", "map.inner"]);
        map.Process("map.inner")!.StartedBy.Should().Equal("map.wrap");
    }

    [Fact]
    public void ACycleBelowAMainProcessIsReportedAndTheWayBackIsNotDrawn()
    {
        var map = ProcessMap.From(MapDocs.With("map.inner", StartWrap), WorkbenchLocale.En);
        map.Cycles.Should().Contain(cycle => cycle.SequenceEqual(InnerAndWrap));
        var wrap = map.Mains.Single(main => main.Process.Id == "map.third").Phases.SelectMany(phase => phase.Subprocesses).Single();
        wrap.Process.Id.Should().Be("map.wrap");
        var inner = wrap.Nested.Should().ContainSingle().Subject;
        inner.Process.Id.Should().Be("map.inner");
        inner.Nested.Should().BeEmpty("map.wrap is already on the way down");
    }

    [Fact]
    public void ASubprocessThatIsNotLoadedBelowASubprocessIsACardMarkedMissing()
    {
        var map = ProcessMap.From(MapDocs.All.Where(process => process.Id != "map.inner"), WorkbenchLocale.En);
        var inner = map.Mains.Single(main => main.Process.Id == "map.third").Phases.SelectMany(phase => phase.Subprocesses).Single().Nested.Should().ContainSingle().Subject;
        (inner.Process.Id, inner.IsMissing).Should().Be(("map.inner", true));
    }

    [Fact]
    public void ANullSetIsRejectedAndAnEmptyOneGivesAnEmptyMap()
    {
        FluentActions.Invoking(() => ProcessMap.From(null!, WorkbenchLocale.En)).Should().Throw<ArgumentNullException>();
        var map = ProcessMap.From([], WorkbenchLocale.En);
        map.Mains.Should().BeEmpty();
        map.Scheduled.Should().BeEmpty();
        map.Processes.Should().BeEmpty();
        map.Shared.Should().BeEmpty();
        map.Cycles.Should().BeEmpty();
        map.Detached.Should().BeEmpty();
        map.RuleCount.Should().Be(0);
    }

    [Fact]
    public void TheLabelKeyNamesTheKindAndTheNameAndRefusesAnUnknownKind()
    {
        ProcessMap.LabelKey(MapPieceKind.Rule, "a.b").Should().Be("rule:a.b");
        ProcessMap.LabelKey(MapPieceKind.Connector, "p.q").Should().Be("connector:p.q");
        ProcessMap.LabelKey(MapPieceKind.PersonTask, "desk").Should().Be("queue:desk");
        ProcessMap.LabelKey(MapPieceKind.Ai, "ask").Should().Be("ai:ask");
        new MapUse(MapPieceKind.Rule, "a.b", "A", false).LabelKey.Should().Be("rule:a.b");
        FluentActions.Invoking(() => ProcessMap.LabelKey((MapPieceKind)9, "x")).Should().Throw<ArgumentOutOfRangeException>();
    }
}

using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Sections 1 to 3 of the W1-C specification: the format additions and their static checks.</summary>
public sealed class TriggerValidationTests
{
    private const string Variables = """
        {
          "case_ref": { "type": "Code", "input": true },
          "visit_date": { "type": "Date", "input": true },
          "run_for": { "type": "Date", "input": true },
          "fired_at": { "type": "Instant", "input": true },
          "internal": { "type": "Code" },
          "zone": { "type": "Code", "input": true },
          "count": { "type": "Integer", "input": true },
          "text": { "type": "Json" }
        }
        """;

    private const string End = """{ "s": { "kind": "end", "result": "ok" } }""";

    private static List<string> Errors(string extra, string steps = End, string variables = Variables, string uses = "[]", params Synapbiz.Brain.Spec.BrainRule[] rules)
    {
        var loaded = BrainProcessLoader.Load(Docs.Text(variables, steps, extra, uses), new ProcessLoadContext(rules));
        return [.. loaded.Errors.Select(error => error.Code + "@" + error.Path)];
    }

    private static BrainProcess Loaded(string extra, string steps = End)
    {
        var loaded = BrainProcessLoader.Load(Docs.Text(Variables, steps, extra), new ProcessLoadContext([]));
        loaded.IsValid.Should().BeTrue(because: string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path)));
        return loaded.Process!;
    }

    private static string Triggers(params string[] triggers) => "\"triggers\": [" + string.Join(", ", triggers) + "]";

    private static string Event(string name = "case.opened", string key = "case_ref", string input = "") =>
        $$"""{ "kind": "event", "event": "{{name}}", "businessKey": "{{key}}"{{(input.Length == 0 ? "" : ", \"input\": " + input)}} }""";

    private static string Schedule(string id = "daily", string cron = "0 6 * * *", string zone = "America/New_York", string input = "", string misfire = "fireOnce") =>
        $$"""{ "kind": "schedule", "id": "{{id}}", "cron": "{{cron}}", {{(zone.Length == 0 ? "" : "\"zone\": \"" + zone + "\", ")}}"misfire": "{{misfire}}"{{(input.Length == 0 ? "" : ", \"input\": " + input)}} }""";

    [Fact]
    public void ADocumentWithoutTriggersHasNone()
    {
        Loaded("").Triggers.Should().BeEmpty();
        Loaded(Triggers()).Triggers.Should().BeEmpty();
    }

    [Fact]
    public void TheDeclaredTriggersAreExposedInDocumentOrder()
    {
        var process = Loaded(Triggers(
            Event(input: """{ "case_ref": "case_ref", "visit_date": "visit_date" }"""),
            Schedule(input: """{ "run_for": "scheduledDate", "fired_at": "scheduledFor" }"""),
            Schedule(id: "weekly", cron: "30 7 * * 1", zone: "", misfire: "skip"),
            Event(name: "case.updated", key: "ref")));

        process.Triggers.Should().HaveCount(4);
        var first = process.Triggers[0].Should().BeOfType<EventTriggerDeclaration>().Subject;
        first.Event.Should().Be("case.opened");
        first.BusinessKeyMember.Should().Be("case_ref");
        first.Input.Should().Equal(new Dictionary<string, string> { ["case_ref"] = "case_ref", ["visit_date"] = "visit_date" });
        first.Kind.Should().Be("event");
        var second = process.Triggers[1].Should().BeOfType<ScheduleTriggerDeclaration>().Subject;
        second.Id.Should().Be("daily");
        second.Cron.Should().Be("0 6 * * *");
        second.Zone.Should().Be("America/New_York");
        second.Misfire.Should().Be(ProcessMisfire.FireOnce);
        second.Input.Should().Equal(new Dictionary<string, string> { ["fired_at"] = "scheduledFor", ["run_for"] = "scheduledDate" });
        second.Kind.Should().Be("schedule");
        var third = process.Triggers[2].Should().BeOfType<ScheduleTriggerDeclaration>().Subject;
        third.Zone.Should().BeNull();
        third.Misfire.Should().Be(ProcessMisfire.Skip);
        third.Input.Should().BeEmpty();
        process.Triggers[3].Input.Should().BeEmpty();
        ((int)ProcessMisfire.Skip, (int)ProcessMisfire.FireOnce).Should().Be((1, 2));
    }

    [Fact]
    public void TriggersDoNotChangeHowTheInstanceRuns()
    {
        var process = Loaded(Triggers(Event(), Schedule()));

        var started = process.Start("i-1", Docs.Input("""{ "case_ref": "c-1" }"""), Docs.Clock());

        started.State.Result.Should().Be("ok");
        started.Requests.Should().BeEmpty();
    }

    [Fact]
    public void AnEventNameIsUsedByOneEventTriggerOnly()
    {
        Errors(Triggers(Event(), Event(name: "other"), Event())).Should().Equal("process.trigger.event_duplicate@/body/triggers/2/event");
    }

    [Fact]
    public void AScheduleIdIsUsedByOneScheduleTriggerOnly()
    {
        Errors(Triggers(Schedule(), Schedule(id: "other"), Schedule(), Schedule())).Should().Equal(
            "process.trigger.schedule_duplicate@/body/triggers/2/id",
            "process.trigger.schedule_duplicate@/body/triggers/3/id");
    }

    [Fact]
    public void AnEventAndAScheduleMayShareAName()
    {
        Errors(Triggers(Event(name: "daily"), Schedule())).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("* * * *")]
    [InlineData("0 0 31 2 *")]
    [InlineData("0 0 * * mon")]
    [InlineData("@daily")]
    public void ACronThatIsNotValidOrNeverFiresIsReported(string cron)
    {
        Errors(Triggers(Schedule(cron: cron))).Should().Equal("process.trigger.cron_invalid@/body/triggers/0/cron");
    }

    [Theory]
    [InlineData("New York")]
    [InlineData("America/")]
    [InlineData("/New_York")]
    [InlineData("America/Argentina/Buenos_Aires/Extra")]
    [InlineData("Local")]
    [InlineData("America/New York")]
    [InlineData("GMT")]
    public void AZoneThatIsNotOfTheShapeOfAnIanaIdIsReported(string zone)
    {
        Errors(Triggers(Schedule(zone: zone))).Should().Equal("process.trigger.zone_invalid@/body/triggers/0/zone");
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("America/New_York")]
    [InlineData("America/Argentina/Buenos_Aires")]
    [InlineData("Etc/GMT+5")]
    [InlineData("Etc/GMT-14")]
    public void AZoneOfTheShapeOfAnIanaIdIsNotLookedUp(string zone)
    {
        Errors(Triggers(Schedule(zone: zone))).Should().BeEmpty();
    }

    [Fact]
    public void ATriggerMapsDeclaredInputVariablesOnly()
    {
        var errors = Errors(Triggers(
            Event(input: """{ "nope": "x", "internal": "y", "case_ref": "z" }"""),
            Schedule(input: """{ "nope": "scheduledFor", "internal": "scheduledFor" }""")));

        errors.Should().Equal(
            "process.trigger.variable_not_input@/body/triggers/0/input/internal",
            "process.trigger.variable_unknown@/body/triggers/0/input/nope",
            "process.trigger.variable_not_input@/body/triggers/1/input/internal",
            "process.trigger.variable_unknown@/body/triggers/1/input/nope");
    }

    [Fact]
    public void AScheduleMapsOnlyItsTwoSources()
    {
        Errors(Triggers(Schedule(input: """{ "run_for": "yesterday" }"""))).Should().Equal("process.trigger.input_source_unknown@/body/triggers/0/input/run_for");
    }

    [Fact]
    public void ScheduledForNeedsAnInstantAndScheduledDateNeedsADate()
    {
        var errors = Errors(Triggers(Schedule(input: """{ "run_for": "scheduledFor", "fired_at": "scheduledDate", "case_ref": "scheduledDate", "count": "scheduledFor" }""")));

        errors.Should().Equal(
            "process.variable.type_mismatch@/body/triggers/0/input/case_ref",
            "process.variable.type_mismatch@/body/triggers/0/input/count",
            "process.variable.type_mismatch@/body/triggers/0/input/fired_at",
            "process.variable.type_mismatch@/body/triggers/0/input/run_for");
    }

    [Theory]
    [InlineData("""{ "kind": "event", "event": "e" }""")]
    [InlineData("""{ "kind": "event", "businessKey": "k" }""")]
    [InlineData("""{ "kind": "event", "event": "", "businessKey": "k" }""")]
    [InlineData("""{ "kind": "event", "event": "e", "businessKey": "k", "zone": "UTC" }""")]
    [InlineData("""{ "kind": "schedule", "id": "Daily", "cron": "0 6 * * *", "misfire": "skip" }""")]
    [InlineData("""{ "kind": "schedule", "id": "1daily", "cron": "0 6 * * *", "misfire": "skip" }""")]
    [InlineData("""{ "kind": "schedule", "id": "d", "cron": "0 6 * * *" }""")]
    [InlineData("""{ "kind": "schedule", "id": "d", "cron": "0 6 * * *", "misfire": "never" }""")]
    [InlineData("""{ "kind": "schedule", "id": "d", "misfire": "skip" }""")]
    [InlineData("""{ "kind": "schedule", "id": "d", "cron": 5, "misfire": "skip" }""")]
    [InlineData("""{ "kind": "timer" }""")]
    [InlineData("""{ "event": "e", "businessKey": "k" }""")]
    public void AMalformedTriggerIsASchemaError(string trigger)
    {
        Errors(Triggers(trigger)).Should().OnlyContain(error => error.StartsWith("spec.structure.schema@/body/triggers/0", StringComparison.Ordinal));
    }

    [Fact]
    public void ATheIdOfAScheduleMayHaveThirtyTwoCharactersNotMore()
    {
        Errors(Triggers(Schedule(id: "a" + new string('b', 31)))).Should().BeEmpty();
        Errors(Triggers(Schedule(id: "a" + new string('b', 32)))).Should().Equal("spec.structure.schema@/body/triggers/0/id");
    }

    [Fact]
    public void AnUnknownMemberInATriggerIsReportedAtTheMember()
    {
        Errors(Triggers("""{ "kind": "event", "event": "e", "businessKey": "k", "zzz": 1 }""")).Should().Equal("spec.structure.schema@/body/triggers/0/zzz");
        Errors(Triggers("""{ "kind": "schedule", "id": "d", "cron": "0 6 * * *", "misfire": "skip", "zzz": 1 }""")).Should().Equal("spec.structure.schema@/body/triggers/0/zzz");
    }

    // Delivery windows.
    private const string Hours = """{ "zone": "America/New_York", "days": ["mon", "fri"], "from": "09:00", "to": "18:00" }""";

    private static string Windows(string window) => "\"windows\": { \"w\": " + window + " }";

    private static string Action(string members) =>
        "{ \"s\": { \"kind\": \"action\", \"port\": \"p\", \"delivery\": \"atLeastOnce\", " + members + "\"next\": \"e\" }, \"e\": { \"kind\": \"end\", \"result\": \"ok\" } }";

    [Fact]
    public void AValidWindowLoadsAndAnUnusedWindowIsNotAnError()
    {
        Errors(Windows(Hours), Action("")).Should().BeEmpty();
        Errors(Windows(Hours.Replace("\"to\": \"18:00\"", "\"to\": \"24:00\"", StringComparison.Ordinal)), Action("\"contact\": true, \"window\": \"w\", ")).Should().BeEmpty();
        Errors(Windows("""{ "zone": { "variable": "zone" }, "days": ["sat"], "from": "00:00", "to": "00:01" }"""), Action("\"window\": \"w\", ")).Should().BeEmpty();
        Errors("\"windows\": {}", Action("")).Should().BeEmpty();
    }

    [Fact]
    public void AContactActionNeedsAWindow()
    {
        Errors("", Action("\"contact\": true, ")).Should().Equal("process.action.window_required@/body/steps/s/contact");
        Errors("", Action("\"contact\": false, ")).Should().BeEmpty();
        Errors(Windows(Hours), Action("\"contact\": true, \"window\": \"w\", ")).Should().BeEmpty();
    }

    [Fact]
    public void AStepMustNameADeclaredWindow()
    {
        Errors(Windows(Hours), Action("\"contact\": true, \"window\": \"other\", ")).Should().Equal("process.window.unknown@/body/steps/s/window");
        Errors("", Action("\"window\": \"w\", ")).Should().Equal("process.window.unknown@/body/steps/s/window");
    }

    [Theory]
    [InlineData("""{ "zone": "New York", "days": ["mon"], "from": "09:00", "to": "18:00" }""", "/body/windows/w/zone")]
    [InlineData("""{ "zone": "", "days": ["mon"], "from": "09:00", "to": "18:00" }""", "/body/windows/w/zone")]
    [InlineData("""{ "zone": "UTC", "days": [], "from": "09:00", "to": "18:00" }""", "/body/windows/w/days")]
    [InlineData("""{ "zone": "UTC", "days": ["mon", "mon"], "from": "09:00", "to": "18:00" }""", "/body/windows/w/days")]
    [InlineData("""{ "zone": "UTC", "days": ["monday"], "from": "09:00", "to": "18:00" }""", "/body/windows/w/days")]
    [InlineData("""{ "zone": "UTC", "days": ["Mon"], "from": "09:00", "to": "18:00" }""", "/body/windows/w/days")]
    [InlineData("""{ "zone": "UTC", "days": ["mon"], "from": "9:00", "to": "18:00" }""", "/body/windows/w/from")]
    [InlineData("""{ "zone": "UTC", "days": ["mon"], "from": "09:60", "to": "18:00" }""", "/body/windows/w/from")]
    [InlineData("""{ "zone": "UTC", "days": ["mon"], "from": "24:00", "to": "24:00" }""", "/body/windows/w/from")]
    [InlineData("""{ "zone": "UTC", "days": ["mon"], "from": "09:00", "to": "25:00" }""", "/body/windows/w/to")]
    [InlineData("""{ "zone": "UTC", "days": ["mon"], "from": "09:00", "to": "24:30" }""", "/body/windows/w/to")]
    [InlineData("""{ "zone": "UTC", "days": ["mon"], "from": "09:00", "to": "09:00" }""", "/body/windows/w/to")]
    [InlineData("""{ "zone": "UTC", "days": ["mon"], "from": "09:00", "to": "08:00" }""", "/body/windows/w/to")]
    [InlineData("""{ "zone": "UTC", "days": ["mon"], "from": "09:00", "to": "00:00" }""", "/body/windows/w/to")]
    public void AnInvalidWindowIsReportedAtTheMember(string window, string path)
    {
        Errors(Windows(window), Action("\"window\": \"w\", ")).Should().Equal("process.window.invalid@" + path);
    }

    [Fact]
    public void AZoneVariableMustBeDeclaredAndBeACode()
    {
        Errors(Windows("""{ "zone": { "variable": "nope" }, "days": ["mon"], "from": "09:00", "to": "18:00" }""")).Should().Equal("process.variable.unknown@/body/windows/w/zone/variable");
        Errors(Windows("""{ "zone": { "variable": "count" }, "days": ["mon"], "from": "09:00", "to": "18:00" }""")).Should().Equal("process.variable.type@/body/windows/w/zone/variable");
        Errors(Windows("""{ "zone": { "variable": "zone" }, "days": ["mon"], "from": "09:00", "to": "18:00" }""")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{ "zone": "UTC", "days": ["mon"], "from": "09:00" }""")]
    [InlineData("""{ "zone": 5, "days": ["mon"], "from": "09:00", "to": "18:00" }""")]
    [InlineData("""{ "zone": { "variable": "zone", "zzz": 1 }, "days": ["mon"], "from": "09:00", "to": "18:00" }""")]
    [InlineData("""{ "zone": {}, "days": ["mon"], "from": "09:00", "to": "18:00" }""")]
    [InlineData("""{ "zone": "UTC", "days": "mon", "from": "09:00", "to": "18:00" }""")]
    [InlineData("""{ "zone": "UTC", "days": [1], "from": "09:00", "to": "18:00" }""")]
    [InlineData("""{ "zone": "UTC", "days": ["mon"], "from": 9, "to": "18:00" }""")]
    [InlineData("""{ "zone": "UTC", "days": ["mon"], "from": "09:00", "to": "18:00", "zzz": 1 }""")]
    public void AMalformedWindowIsASchemaError(string window)
    {
        Errors(Windows(window)).Should().OnlyContain(error => error.StartsWith("spec.structure.schema@/body/windows/w", StringComparison.Ordinal));
    }

    [Fact]
    public void AWindowNameMustBeAnId()
    {
        Errors("\"windows\": { \"has space\": " + Hours + " }").Should().Equal("spec.structure.schema@/body/windows/has space");
    }

    [Fact]
    public void AnActionMemberOfTheWrongTypeIsASchemaError()
    {
        Errors("", Action("\"contact\": \"yes\", ")).Should().Equal("spec.structure.schema@/body/steps/s/contact");
        Errors("", Action("\"window\": 5, ")).Should().Equal("spec.structure.schema@/body/steps/s/window");
    }

    // The poll step.
    private const string Uses = """[{ "$ref": "rule/conf.flag_rule" }]""";

    private static string PollStep(string changes = "")
    {
        var members = System.Text.Json.Nodes.JsonNode.Parse(
            "{ \"kind\": \"poll\", \"port\": \"p\", \"rule\": \"conf.flag_rule\", \"facts\": { \"count\": \"count\" }, \"field\": \"flag\", \"until\": [\"true\"], "
            + "\"every\": \"PT15M\", \"maxDuration\": \"P1D\", \"next\": \"e\", \"onExpired\": \"e\" }")!.AsObject();
        if (changes.Length > 0)
        {
            foreach (var change in System.Text.Json.Nodes.JsonNode.Parse("{" + changes.TrimStart(',', ' ') + "}")!.AsObject().ToList())
            {
                members.Remove(change.Key);
                members.Add(change.Key, change.Value?.DeepClone());
            }
        }

        return "{ \"s\": " + members.ToJsonString() + ", \"e\": { \"kind\": \"end\", \"result\": \"ok\" } }";
    }

    private static List<string> PollErrors(string changes = "", string uses = Uses, params Synapbiz.Brain.Spec.BrainRule[] rules) =>
        Errors("", PollStep(changes), Variables, uses, rules.Length == 0 ? [Docs.FlagRule] : rules);

    [Fact]
    public void AValidPollLoads()
    {
        PollErrors().Should().BeEmpty();
        PollErrors(", \"backoff\": \"exponential\", \"max\": \"PT1H\", \"onError\": \"e\", \"onNotAvailable\": \"e\", \"onNotEntitled\": \"e\", \"retry\": { \"maxAttempts\": 2, \"backoff\": \"fixed\", \"initial\": \"PT1S\" }, \"input\": { \"c\": \"case_ref\" }, \"output\": { \"n\": \"count\" }, \"stage\": \"one\"")
            .Should().BeEmpty();
        PollErrors(", \"field\": \"level\", \"until\": [\"high\", \"low\"]").Should().BeEmpty();
        PollErrors(", \"until\": [\"true\", \"false\"]").Should().BeEmpty();
    }

    [Fact]
    public void AnUnknownStepOfAPollIsReported()
    {
        PollErrors(", \"onExpired\": \"nope\"").Should().Equal("process.step.unknown@/body/steps/s/onExpired");
        PollErrors(", \"onError\": \"nope\"").Should().Equal("process.step.unknown@/body/steps/s/onError");
    }

    [Fact]
    public void ThePollRuleMustBeInTheContextAndInUses()
    {
        Errors("", PollStep(), uses: Uses).Should().Equal("process.rule.unavailable@/body/steps/s/rule");
        PollErrors(uses: "[]").Should().Equal("process.uses.missing@/body/steps/s/rule");
        PollErrors(uses: """[{ "$ref": "rule/conf.flag_rule" }, { "$ref": "rule/other" }]""").Should().Equal("process.uses.unused@/uses/1");
    }

    [Fact]
    public void ThePollFactsAreCheckedLikeThoseOfADecide()
    {
        PollErrors(", \"facts\": { \"nope\": \"count\" }").Should().Equal("process.rule.fact_unknown@/body/steps/s/facts/nope");
        PollErrors(", \"facts\": { \"count\": \"case_ref\" }").Should().Equal("process.variable.type@/body/steps/s/facts/count");
        PollErrors(", \"facts\": { \"count\": \"missing\" }").Should().Equal("process.variable.unknown@/body/steps/s/facts/count");
    }

    [Fact]
    public void ThePollMappingsNameDeclaredVariables()
    {
        PollErrors(", \"input\": { \"c\": \"missing\" }, \"output\": { \"n\": \"gone\" }").Should().Equal(
            "process.variable.unknown@/body/steps/s/input/c",
            "process.variable.unknown@/body/steps/s/output/n");
    }

    [Fact]
    public void ThePollRuleMustNotCollect()
    {
        Errors("", PollStep().Replace("conf.flag_rule", "conf.list_rule", StringComparison.Ordinal), uses: """[{ "$ref": "rule/conf.list_rule" }]""", rules: Docs.ListRule).Should().Contain("process.rule.hit_policy@/body/steps/s");
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("total")]
    [InlineData("note")]
    public void ThePollFieldMustBeAnEnumOrBooleanOutcome(string field)
    {
        PollErrors(", \"field\": \"" + field + "\"").Should().Equal("process.rule.outcome_unknown@/body/steps/s/field");
    }

    [Fact]
    public void TheUntilValuesMustBeValuesTheRuleCanProduce()
    {
        PollErrors(", \"until\": [\"true\", \"yes\"]").Should().Equal("process.route.unknown_value@/body/steps/s/until/1");
        PollErrors(", \"field\": \"level\", \"until\": [\"medium\", \"high\", \"x\"]").Should().Equal(
            "process.route.unknown_value@/body/steps/s/until/0",
            "process.route.unknown_value@/body/steps/s/until/2");
    }

    [Fact]
    public void TheDefaultRowOfTheRuleCountsForTheUntilValues()
    {
        var withDefault = new[] { Docs.DefaultRule };
        var text = PollStep(", \"field\": \"level\", \"until\": [\"low\"]").Replace("conf.flag_rule", "conf.default_rule", StringComparison.Ordinal);
        Errors("", text, uses: """[{ "$ref": "rule/conf.default_rule" }]""", rules: withDefault).Should().BeEmpty();
    }

    [Theory]
    [InlineData(", \"every\": \"PT1S\", \"maxDuration\": \"P1D\"", true)]
    [InlineData(", \"every\": \"PT1M\", \"maxDuration\": \"PT10000M\"", true)]
    [InlineData(", \"every\": \"PT1M\", \"maxDuration\": \"PT9999M\"", false)]
    [InlineData(", \"every\": \"PT1M\", \"maxDuration\": \"PT9998M\"", false)]
    [InlineData(", \"every\": \"PT1M\", \"maxDuration\": \"PT10001M\"", true)]
    [InlineData(", \"every\": \"PT1S\", \"maxDuration\": \"PT9999S\"", false)]
    [InlineData(", \"every\": \"PT1S\", \"maxDuration\": \"PT10000S\"", true)]
    [InlineData(", \"every\": \"PT1S\", \"maxDuration\": \"P3660D\", \"backoff\": \"exponential\"", false)]
    [InlineData(", \"every\": \"PT1S\", \"maxDuration\": \"P3660D\", \"backoff\": \"exponential\", \"max\": \"PT1S\"", true)]
    public void AtMostTenThousandRoundsMustFitInMaxDuration(string changes, bool tooMany)
    {
        var errors = PollErrors(changes);

        if (tooMany)
        {
            errors.Should().Equal("process.poll.too_many_rounds@/body/steps/s/maxDuration");
        }
        else
        {
            errors.Should().BeEmpty();
        }
    }

    [Fact]
    public void ADurationOfAPollThatIsNotValidIsReportedOnceAndNotCounted()
    {
        PollErrors(", \"every\": \"PT0S\"").Should().Equal("process.duration.invalid@/body/steps/s/every");
        PollErrors(", \"maxDuration\": \"P4000D\"").Should().Equal("process.duration.invalid@/body/steps/s/maxDuration");
        PollErrors(", \"backoff\": \"exponential\", \"max\": \"P0D\"").Should().Equal("process.duration.invalid@/body/steps/s/max");
    }

    [Theory]
    [InlineData(", \"max\": \"PT1H\"")]
    [InlineData(", \"backoff\": \"fixed\", \"max\": \"PT1H\"")]
    public void MaxIsOnlyForExponentialBackoff(string changes)
    {
        PollErrors(changes).Should().Equal("spec.structure.schema@/body/steps/s/max");
    }

    [Theory]
    [InlineData(", \"backoff\": \"linear\"", "/body/steps/s/backoff")]
    [InlineData(", \"until\": []", "/body/steps/s/until")]
    [InlineData(", \"until\": [\"true\", \"true\"]", "/body/steps/s/until")]
    [InlineData(", \"until\": [3]", "/body/steps/s/until/0")]
    [InlineData(", \"every\": 5", "/body/steps/s/every")]
    [InlineData(", \"zzz\": 1", "/body/steps/s/zzz")]
    public void AMalformedPollIsASchemaError(string changes, string path)
    {
        PollErrors(changes).Should().Equal("spec.structure.schema@" + path);
    }

    [Theory]
    [InlineData("rule")]
    [InlineData("facts")]
    [InlineData("field")]
    [InlineData("until")]
    [InlineData("every")]
    [InlineData("maxDuration")]
    [InlineData("onExpired")]
    [InlineData("port")]
    [InlineData("next")]
    public void EveryRequiredMemberOfAPollIsRequired(string member)
    {
        var steps = PollStep();
        var document = System.Text.Json.Nodes.JsonNode.Parse(steps)!.AsObject();
        document["s"]!.AsObject().Remove(member);

        var errors = Errors("", document.ToJsonString(), uses: Uses, rules: Docs.FlagRule);

        errors.Should().Equal("spec.structure.schema@/body/steps/s");
    }

    [Fact]
    public void APollIsNotALoopSoARouteBackToItIsAnUnboundedCycle()
    {
        Errors("", PollStep(", \"onExpired\": \"s\""), uses: Uses, rules: Docs.FlagRule).Should().Equal("process.cycle.unbounded@/body/steps/s");
    }

    // Scenario zones.
    private static string ScenarioWith(string zones) => $$"""
        "scenarios": [ { "name": "n", "start": { "at": "2027-01-04T14:00:00Z", "today": "2027-01-04" }, "zones": {{zones}}, "events": [], "expect": { "status": "Completed" } } ]
        """;

    [Theory]
    [InlineData("""{ "America/New_York": "-05:00", "UTC": "+00:00", "Etc/GMT-14": "+14:00", "Asia/Kolkata": "+05:30" }""")]
    [InlineData("{}")]
    public void ScenarioZonesAreIanaIdsWithFixedOffsets(string zones)
    {
        Errors(ScenarioWith(zones)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{ "America/New_York": "-15:00" }""", "America/New_York")]
    [InlineData("""{ "America/New_York": "+14:30" }""", "America/New_York")]
    [InlineData("""{ "America/New_York": "-5:00" }""", "America/New_York")]
    [InlineData("""{ "America/New_York": "-05:75" }""", "America/New_York")]
    [InlineData("""{ "America/New_York": "Z" }""", "America/New_York")]
    [InlineData("""{ "America/New_York": "" }""", "America/New_York")]
    [InlineData("""{ "New York": "-05:00" }""", "New York")]
    public void AScenarioZoneNeedsAnIdOfTheRightShapeAndAnOffsetOfAtMostFourteenHours(string zones, string id)
    {
        Errors(ScenarioWith(zones)).Should().Equal("process.scenario.invalid@/body/scenarios/0/zones/" + id.Replace("/", "~1", StringComparison.Ordinal));
    }

    [Fact]
    public void AScenarioZoneOfTheWrongTypeIsASchemaError()
    {
        Errors(ScenarioWith("""{ "America/New_York": -5 }""")).Should().Equal("spec.structure.schema@/body/scenarios/0/zones/America~1New_York");
    }
}

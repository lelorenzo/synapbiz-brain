using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>The language-free read model of a loaded document: golden text, the edges of the validator, and what a step can carry.</summary>
public sealed class OutlineTests
{
    private const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

    public static TheoryData<string> Names => [.. OutlineDocs.All().Select(item => item.Name)];

    private static BrainProcess Process(string name) => OutlineDocs.All().Single(item => item.Name == name).Process;

    [Theory]
    [MemberData(nameof(Names))]
    public void TheOutlineOfEveryDocumentIsPinnedAsGoldenText(string name)
    {
        var rendered = OutlineDocs.Render(ProcessOutline.From(Process(name)));

        rendered.Should().Be(OutlineDocs.Golden(name, rendered));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void TheRoutesOfEveryStepAreTheEdgesTheValidatorWalks(string name)
    {
        var process = Process(name);
        var outline = ProcessOutline.From(process);

        var definition = typeof(BrainProcess).GetProperty("Definition", Internal)!.GetValue(process)!;
        var steps = ((IEnumerable)definition.GetType().GetProperty("Steps", Internal)!.GetValue(definition)!).Cast<object>().ToList();
        var walked = steps.SelectMany(step =>
        {
            var id = (string)step.GetType().GetProperty("Id", Internal)!.GetValue(step)!;
            var references = (IEnumerable)step.GetType().GetMethod("References", Internal)!.Invoke(step, null)!;
            return references.Cast<object>().Select(reference => (Step: id, Target: (string)reference.GetType().GetProperty("Target", Internal)!.GetValue(reference)!));
        }).ToList();

        outline.Steps.SelectMany(step => step.Routes.Select(route => (Step: step.Id, route.Target))).Should().Equal(walked);
        outline.Steps.Select(step => step.Id).Should().Equal(steps.Select(step => (string)step.GetType().GetProperty("Id", Internal)!.GetValue(step)!));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void TheLabelOfEveryRouteIsAMemberOfTheStepInTheDocument(string name)
    {
        var steps = Text(name)["body"]!["steps"]!.AsObject();
        var members = new[] { "next", "body", "onError", "onNotAvailable", "onNotEntitled", "onUnknown", "onTimeout", "onExhausted", "onNoDecision", "onInvalid", "onExpired" };
        foreach (var step in ProcessOutline.From(Process(name)).Steps)
        {
            var node = steps[step.Id]!.AsObject();
            foreach (var route in step.Routes)
            {
                var written = route.Label switch
                {
                    "interrupt" => node["interruptBy"]!["to"]!.GetValue<string>(),
                    var on when on.StartsWith("on:", StringComparison.Ordinal) => node["on"]![on[3..]]!.GetValue<string>(),
                    var member => node[member]!.GetValue<string>(),
                };
                written.Should().Be(route.Target, because: step.Id + " " + route.Label);
            }

            var declared = members.Count(member => node[member] is JsonValue) + ((node["on"] as JsonObject)?.Count ?? 0) + (node["interruptBy"] is null ? 0 : 1);
            step.Routes.Count.Should().Be(declared, because: step.Id);
        }
    }

    [Fact]
    public void TheDocumentLevelFactsAreRead()
    {
        var outline = ProcessOutline.From(Process("postvisit.coding_review"));

        outline.Id.Should().Be("postvisit.coding_review");
        outline.Version.Should().Be(1);
        outline.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");
        outline.Subject.Should().Be("case");
        outline.Stages.Should().Equal("postvisit");
        outline.Start.Should().Be("read_note");
        outline.Uses.Should().Equal("rule/postvisit.opportunities", "rule/postvisit.assist_gate", "rule/postvisit.suggestion_check");
        outline.Requires.Should().Equal("coding@1");
        outline.Variables.Should().Contain(new OutlineVariable("case_ref", "Code", true)).And.Contain(new OutlineVariable("items", "Integer", false));
        outline.Scenarios.Should().HaveCount(18).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void AnAiStepCarriesWhatAReviewerMustSee()
    {
        var step = ProcessOutline.From(Process("postvisit.coding_review")).Step("suggest")!;

        step.Kind.Should().Be(OutlineStepKind.Ai);
        step.Port.Should().Be("assist.suggest");
        step.Model.Should().Be("model-a");
        step.PromptId.Should().Be("coding-suggest");
        step.PromptVersion.Should().Be(1);
        step.MaxAttempts.Should().Be(2);
        step.HasInputSchema.Should().BeTrue();
        step.HasOutputSchema.Should().BeTrue();
        step.InputSchemaJson.Should().Be("{\"properties\":{\"case\":{\"type\":\"string\"},\"opportunities\":{\"type\":\"array\"}},\"required\":[\"case\",\"opportunities\"],\"type\":\"object\"}");
        step.OutputSchemaJson.Should().Contain("\"additionalProperties\":false");
        step.InputNames.Should().Equal("case_ref", "opportunities");
        step.OutputNames.Should().Equal("suggested_code", "confidence");
        step.Retry.Should().Be(new OutlineRetry(2, "fixed", TimeSpan.FromMinutes(1), null));
        step.Routes.Select(route => route.Label).Should().Equal("next", "onError", "onNotAvailable", "onInvalid");
    }

    [Fact]
    public void AnActionWithAnInterruptedWindowCarriesTheInterruptAndItsRoute()
    {
        var step = ProcessOutline.From(Process("fixture.reminder")).Steps.Single(item => item.Interrupt is not null);

        step.Kind.Should().Be(OutlineStepKind.Action);
        step.Window.Should().NotBeNull();
        step.IsContact.Should().BeTrue();
        step.Delivery.Should().Be(ProcessDelivery.AtMostOnce);
        step.Routes.Should().Contain(route => route.Label == "interrupt" && route.Target == step.Interrupt!.Target);
        step.OutputNames.Should().Contain(step.Interrupt!.Output.Select(binding => binding.Variable));
    }

    [Fact]
    public void ATimeoutTiedToAnInstantIsReportedWithItsVariableAndOffset()
    {
        var timeouts = ProcessOutline.From(Process("fixture.reminder")).Steps.Select(step => step.Timeout).Where(timeout => timeout is not null).ToList();

        timeouts.Should().NotBeEmpty();
        timeouts.Should().OnlyContain(timeout => timeout!.Variable != null && timeout.Duration == null && timeout.Offset != null);
        ProcessOutline.From(Process("previsit.outreach")).Steps.Select(step => step.Timeout).Where(timeout => timeout is not null)
            .Should().NotBeEmpty().And.OnlyContain(timeout => timeout!.Variable == null && timeout.Duration != null && timeout.Offset == null);
    }

    [Fact]
    public void ThePollLoopTimerAndSubprocessFactsAreRead()
    {
        var outreach = ProcessOutline.From(Process("previsit.outreach"));
        var note = ProcessOutline.From(Process("visit.note_wait"));
        var lifecycle = ProcessOutline.From(Process("case.lifecycle"));

        note.Steps.Single(step => step.Kind == OutlineStepKind.Poll).Poll.Should().NotBeNull();
        note.Steps.Single(step => step.Kind == OutlineStepKind.Poll).Rule.Should().NotBeNullOrEmpty();
        outreach.Steps.Single(step => step.Kind == OutlineStepKind.Loop).Loop!.MaxIterations.Should().BeGreaterThan(0);
        outreach.Windows.Should().ContainSingle().Which.Days.Should().NotBeEmpty();
        lifecycle.Steps.Where(step => step.Kind == OutlineStepKind.Subprocess).Select(step => step.Process).Should().OnlyContain(id => id != null);
        lifecycle.Triggers.Should().ContainSingle();
        ProcessOutline.From(Process("collection.followup")).Steps.Single(step => step.Kind == OutlineStepKind.Timer).Timer!.After.Should().NotBeNull();
        lifecycle.Steps.Where(step => step.Kind == OutlineStepKind.End).Select(step => step.Result).Should().OnlyContain(result => result != null);
    }

    [Fact]
    public void AStepCarriesNothingOfAKindItIsNot()
    {
        foreach (var step in OutlineDocs.All().SelectMany(item => ProcessOutline.From(item.Process).Steps))
        {
            var effect = step.Kind is OutlineStepKind.Read or OutlineStepKind.Action or OutlineStepKind.Ai or OutlineStepKind.Poll;
            (step.Model is not null).Should().Be(step.Kind == OutlineStepKind.Ai, because: step.Id);
            (step.Queue is not null).Should().Be(step.Kind == OutlineStepKind.HumanTask, because: step.Id);
            (step.Result is not null).Should().Be(step.Kind == OutlineStepKind.End, because: step.Id);
            (step.Process is not null).Should().Be(step.Kind == OutlineStepKind.Subprocess, because: step.Id);
            (step.Port is not null).Should().Be(effect, because: step.Id);
            (step.Retry is not null).Should().Be(effect, because: step.Id);
        }
    }

    [Fact]
    public void AScenarioIsAvailableAsCanonicalJson()
    {
        var outline = ProcessOutline.From(Process("fixture.outreach"));

        foreach (var name in outline.Scenarios)
        {
            var text = outline.ScenarioJson(name)!;
            JsonNode.Parse(text)!["name"]!.GetValue<string>().Should().Be(name);
            text.Should().NotContain("\n").And.NotContain("\": ");
        }

        outline.ScenarioJson("no such scenario").Should().BeNull();
    }

    [Fact]
    public void EveryEmbeddedScenarioIsValidForItsOwnDocument()
    {
        foreach (var (name, process) in OutlineDocs.All())
        {
            var outline = ProcessOutline.From(process);
            foreach (var scenario in outline.Scenarios)
            {
                ProcessOutline.CheckScenario(process, JsonNode.Parse(outline.ScenarioJson(scenario)!)!.AsObject()).Should().BeEmpty(because: name + "/" + scenario);
            }
        }
    }

    [Fact]
    public void AScenarioThatBreaksTheFormatOrTheDocumentIsReportedWithTheValidatorsOwnCodeAndPath()
    {
        var process = Process("fixture.outreach");
        var outline = ProcessOutline.From(process);
        var good = JsonNode.Parse(outline.ScenarioJson(outline.Scenarios[0])!)!.AsObject();

        var unknownStep = (JsonObject)good.DeepClone();
        unknownStep["events"] = new JsonArray(new JsonObject { ["complete"] = "no_such_step" });
        var unknownVariable = (JsonObject)good.DeepClone();
        unknownVariable["expect"]!["variables"] = new JsonObject { ["no_such_variable"] = 1 };
        var shape = (JsonObject)good.DeepClone();
        shape.Remove("expect");
        var badTime = (JsonObject)good.DeepClone();
        badTime["start"]!["at"] = "tomorrow";

        ProcessOutline.CheckScenario(process, unknownStep).Select(error => error.Path).Should().Equal("/events/0/complete");
        ProcessOutline.CheckScenario(process, unknownVariable).Select(error => error.Path).Should().Equal("/expect/variables/no_such_variable");
        ProcessOutline.CheckScenario(process, shape).Should().ContainSingle().Which.Path.Should().BeEmpty();
        ProcessOutline.CheckScenario(process, badTime).Should().NotBeEmpty();
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        var process = Process("fixture.outreach");

        ((Action)(() => ProcessOutline.From(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => ProcessOutline.CheckScenario(null!, []))).Should().Throw<ArgumentNullException>();
        ((Action)(() => ProcessOutline.CheckScenario(process, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => ProcessOutline.From(process).ScenarioJson(null!))).Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// What each member of the internal step model becomes in the outline. A model record that gains a member fails the next test
    /// until the member is listed here with the outline member that carries it, so that a screen never silently loses a fact.
    /// </summary>
    private static readonly Dictionary<string, string[]> Mapped = new(StringComparer.Ordinal)
    {
        ["Id"] = ["Id"], ["Stage"] = ["Stage"], ["Port"] = ["Port"], ["Input"] = ["Inputs", "InputNames"], ["Output"] = ["Outputs", "OutputNames"],
        ["Retry"] = ["Retry"], ["Next"] = ["Routes"], ["OnError"] = ["Routes"], ["OnNotAvailable"] = ["Routes"], ["OnNotEntitled"] = ["Routes"],
        ["OnUnknown"] = ["Routes"], ["OnInvalid"] = ["Routes"], ["OnExpired"] = ["Routes"], ["OnTimeout"] = ["Routes"], ["OnNoDecision"] = ["Routes"],
        ["OnExhausted"] = ["Routes"], ["Body"] = ["Routes", "Loop"], ["On"] = ["Routes"], ["AtMostOnce"] = ["Delivery"], ["Contact"] = ["IsContact"],
        ["Window"] = ["Window"], ["Interrupt"] = ["Interrupt", "Routes"], ["Model"] = ["Model"], ["PromptId"] = ["PromptId"],
        ["PromptVersion"] = ["PromptVersion"], ["InputSchema"] = ["HasInputSchema", "InputSchemaJson"], ["OutputSchema"] = ["HasOutputSchema", "OutputSchemaJson"],
        ["MaxAttempts"] = ["MaxAttempts"], ["Rule"] = ["Rule"], ["Facts"] = ["Facts"], ["Route"] = ["RouteField"], ["CollectInto"] = ["CollectInto"],
        ["Queue"] = ["Queue"], ["Outcomes"] = ["Outcomes"], ["Show"] = ["Show"], ["Timeout"] = ["Timeout"], ["TimeoutAt"] = ["Timeout"],
        ["Event"] = ["Event"], ["Correlation"] = ["Correlation"], ["After"] = ["Timer"], ["Until"] = ["Timer", "Poll"], ["Offset"] = ["Timer"],
        ["MaxIterations"] = ["Loop"], ["ForEach"] = ["Loop"], ["Item"] = ["Loop"], ["Process"] = ["Process"], ["Result"] = ["Result"],
        ["Field"] = ["Poll"], ["Every"] = ["Poll"], ["Exponential"] = ["Poll"], ["Max"] = ["Poll"], ["MaxDuration"] = ["Poll"],
    };

    /// <summary>
    /// Every member of every record of the internal model, by record: a record that gains a member fails the next test until the member
    /// is listed here, so that a screen never silently loses a fact. The outline member that carries it is found in <see cref="Mapped"/>
    /// (step records) or <see cref="Carried"/> (the nested records and the document); a member the outline leaves out on purpose says so there.
    /// </summary>
    private static readonly Dictionary<string, string> Declared = new(StringComparer.Ordinal)
    {
        ["ReadStep"] = "Id Stage Port Input Output Retry Next OnError OnNotAvailable OnNotEntitled OnUnknown",
        ["ActionStep"] = "Id Stage Port Input Output Retry Next OnError OnNotAvailable OnNotEntitled OnUnknown AtMostOnce Contact Window Interrupt",
        ["AiStep"] = "Id Stage Port Input Output Retry Next OnError OnNotAvailable OnNotEntitled OnUnknown Model PromptId PromptVersion InputSchema OutputSchema MaxAttempts OnInvalid",
        ["PollStep"] = "Id Stage Port Input Output Retry Next OnError OnNotAvailable OnNotEntitled OnUnknown Rule Facts Field Until Every Exponential Max MaxDuration OnExpired",
        ["DecideStep"] = "Id Stage Rule Facts Output Route On Next CollectInto OnNoDecision OnError",
        ["HumanTaskStep"] = "Id Stage Queue Outcomes Show Output On Timeout OnTimeout TimeoutAt",
        ["WaitStep"] = "Id Stage Event Correlation Output Next Timeout OnTimeout TimeoutAt",
        ["TimerStep"] = "Id Stage After Until Offset Next",
        ["LoopStep"] = "Id Stage MaxIterations Body OnExhausted ForEach Item Next",
        ["SubprocessStep"] = "Id Stage Process Input Output Next On OnError",
        ["EndStep"] = "Id Stage Result",
        ["TimeoutAt"] = "Variable Offset",
        ["InterruptDef"] = "Event Correlation Output To",
        ["WindowDef"] = "Name Zone ZoneVariable Days From To",
        ["RetryPolicy"] = "MaxAttempts Exponential Initial Max",
        ["Mapping"] = "Entries IsEmpty",
        ["VariableDef"] = "Name Type AllowedValues Input Initial",
        ["ProcessDefinition"] = "Id Version Hash Subject Stages Variables Accepts MaxSteps Start Steps StepsById Uses Meta Requires Triggers Windows Scenarios Rules",
    };

    /// <summary>Where the members of the nested records and of the document go in the outline, as <c>OutlineType.Member</c>; a dash is a member the outline does not carry, on purpose.</summary>
    private static readonly Dictionary<string, string[]> Carried = new(StringComparer.Ordinal)
    {
        ["TimeoutAt.Variable"] = ["OutlineTimeout.Variable"], ["TimeoutAt.Offset"] = ["OutlineTimeout.Offset"],
        ["InterruptDef.Event"] = ["OutlineInterrupt.Event"], ["InterruptDef.Correlation"] = ["OutlineInterrupt.Correlation"],
        ["InterruptDef.Output"] = ["OutlineInterrupt.Output"], ["InterruptDef.To"] = ["OutlineInterrupt.Target", "OutlineStep.Routes"],
        ["WindowDef.Name"] = ["OutlineWindow.Name"], ["WindowDef.Zone"] = ["OutlineWindow.Zone"], ["WindowDef.ZoneVariable"] = ["OutlineWindow.ZoneVariable"],
        ["WindowDef.Days"] = ["OutlineWindow.Days"], ["WindowDef.From"] = ["OutlineWindow.From"], ["WindowDef.To"] = ["OutlineWindow.To"],
        ["RetryPolicy.MaxAttempts"] = ["OutlineRetry.MaxAttempts"], ["RetryPolicy.Exponential"] = ["OutlineRetry.Backoff"],
        ["RetryPolicy.Initial"] = ["OutlineRetry.Initial"], ["RetryPolicy.Max"] = ["OutlineRetry.Max"],
        ["Mapping.Entries"] = ["OutlineBinding.Name", "OutlineBinding.Variable"], ["Mapping.IsEmpty"] = ["-"],
        ["VariableDef.Name"] = ["OutlineVariable.Name"], ["VariableDef.Type"] = ["OutlineVariable.Type"], ["VariableDef.Input"] = ["OutlineVariable.IsInput"],
        ["VariableDef.AllowedValues"] = ["-"], ["VariableDef.Initial"] = ["-"],
        ["ProcessDefinition.Id"] = ["ProcessOutline.Id"], ["ProcessDefinition.Version"] = ["ProcessOutline.Version"], ["ProcessDefinition.Hash"] = ["ProcessOutline.ContentHash"],
        ["ProcessDefinition.Subject"] = ["ProcessOutline.Subject"], ["ProcessDefinition.Stages"] = ["ProcessOutline.Stages"], ["ProcessDefinition.Variables"] = ["ProcessOutline.Variables"],
        ["ProcessDefinition.Accepts"] = ["ProcessOutline.Accepts"], ["ProcessDefinition.MaxSteps"] = ["-"], ["ProcessDefinition.Start"] = ["ProcessOutline.Start"],
        ["ProcessDefinition.Steps"] = ["ProcessOutline.Steps"], ["ProcessDefinition.StepsById"] = ["ProcessOutline.Step"], ["ProcessDefinition.Uses"] = ["ProcessOutline.Uses"],
        ["ProcessDefinition.Meta"] = ["-"], ["ProcessDefinition.Requires"] = ["ProcessOutline.Requires"], ["ProcessDefinition.Triggers"] = ["ProcessOutline.Triggers"],
        ["ProcessDefinition.Windows"] = ["ProcessOutline.Windows"], ["ProcessDefinition.Scenarios"] = ["ProcessOutline.Scenarios"], ["ProcessDefinition.Rules"] = ["-"],
    };

    private static readonly string[] StepRecords = ["ReadStep", "ActionStep", "AiStep", "PollStep", "DecideStep", "HumanTaskStep", "WaitStep", "TimerStep", "LoopStep", "SubprocessStep", "EndStep"];

    private static IEnumerable<string> ModelMembers(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(property => property.Name is not ("EqualityContract" or "Kind") && property.GetIndexParameters().Length == 0)
            .Select(property => property.Name);

    [Fact]
    public void EveryMemberOfEveryRecordOfTheModelIsMappedToAnOutlineMember()
    {
        var assembly = typeof(BrainProcess).Assembly;
        var root = assembly.GetType("Synapbiz.Brain.Process.StepDef")!;
        var models = assembly.GetTypes().Where(type => root.IsAssignableFrom(type) && !type.IsAbstract).ToList();
        models.Select(type => type.Name).Should().BeEquivalentTo(StepRecords);

        var types = models.Concat(Declared.Keys.Except(StepRecords).Select(name => assembly.GetType("Synapbiz.Brain.Process." + name)!)).ToList();
        types.Should().NotContainNulls();
        types.Select(type => type.Name).Should().BeEquivalentTo(Declared.Keys);
        foreach (var type in types)
        {
            ModelMembers(type).Order(StringComparer.Ordinal).Should().Equal(Declared[type.Name].Split(' ').Order(StringComparer.Ordinal), because: "a new member of " + type.Name + " needs a place in ProcessOutline, or a line in the list of members left out");
        }

        var outlineMembers = new[] { typeof(ProcessOutline), typeof(OutlineStep), typeof(OutlineTimeout), typeof(OutlineInterrupt), typeof(OutlineWindow), typeof(OutlineRetry), typeof(OutlineVariable), typeof(OutlineBinding) }
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance).Where(member => member is PropertyInfo or MethodInfo { IsSpecialName: false }).Select(member => type.Name + "." + member.Name))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var member in Declared.Where(item => StepRecords.Contains(item.Key)).SelectMany(item => item.Value.Split(' ').Select(name => item.Key + "." + name)))
        {
            Mapped.Should().ContainKey(member[(member.IndexOf('.', StringComparison.Ordinal) + 1)..], because: member);
        }

        Mapped.Values.SelectMany(names => names).Select(name => "OutlineStep." + name).Where(name => !outlineMembers.Contains(name)).Should().BeEmpty();
        foreach (var (key, targets) in Carried)
        {
            var record = key[..key.IndexOf('.', StringComparison.Ordinal)];
            Declared[record].Split(' ').Should().Contain(key[(record.Length + 1)..], because: key);
            targets.Where(target => target != "-" && !outlineMembers.Contains(target)).Should().BeEmpty(because: key);
        }

        var nested = Declared.Where(item => !StepRecords.Contains(item.Key)).SelectMany(item => item.Value.Split(' ').Select(name => item.Key + "." + name)).ToList();
        nested.Should().BeEquivalentTo(Carried.Keys, because: "every member of a nested record is mapped or left out on purpose");
    }

    private static JsonNode Text(string name) => name.StartsWith("fixture.", StringComparison.Ordinal)
        ? TestFiles.ReadJson(TestFiles.Fixtures + name["fixture.".Length..] + ".process.json")
        : ReferenceDocs.ProcessJson(name);


    private const string TaskDocument =
        """
        {"$spec":"brain.process@1","id":"outline.escape","version":1,"uses":[],"requires":[],"meta":{},
         "body":{"subject":"case","stages":[],"variables":{},"start":"t",
          "steps":{"t":{"kind":"humanTask","queue":"q","outcomes":["a/b","x~y","a~1b"],"show":[],"on":{"a/b":"e","x~y":"e","a~1b":"e"}},
                   "e":{"kind":"end","result":"done"}},"scenarios":[]}}
        """;

    [Fact]
    public void AnOnLabelUnescapesTheSlashAndTheTildeOfItsKeyInTheRightOrder()
    {
        var loaded = BrainProcessLoader.Load(TaskDocument, new ProcessLoadContext([]));
        var outline = ProcessOutline.From(loaded.Process ?? throw new InvalidOperationException(string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path))));

        outline.Step("t")!.Routes.Select(route => route.Label).Should().Equal("on:a/b", "on:a~1b", "on:x~y");
    }

    [Fact]
    public void AnAiStepSaysItDeclaresBothSchemasAndCarriesTheirText()
    {
        // The format requires both schemas on an ai step, so the one-sided case cannot be loaded; both flags and both texts are read.
        var step = ProcessOutline.From(ReferenceDocs.Process("postvisit.coding_review")).Step("suggest")!;
        step.HasInputSchema.Should().BeTrue();
        step.HasOutputSchema.Should().BeTrue();
        step.InputSchemaJson.Should().NotBeNullOrEmpty();
        step.OutputSchemaJson.Should().NotBeNullOrEmpty();
        step.InputSchemaJson.Should().NotBe(step.OutputSchemaJson);
    }

    [Fact]
    public void NoListOfTheOutlineCanBeChangedThroughItsProperty()
    {
        var outlines = OutlineDocs.All().Select(item => ProcessOutline.From(item.Process)).ToList();
        var objects = outlines.Cast<object>()
            .Concat(outlines.SelectMany(outline => outline.Steps))
            .Concat(outlines.SelectMany(outline => outline.Windows))
            .Concat(outlines.SelectMany(outline => outline.Steps.Select(step => step.Interrupt)).OfType<OutlineInterrupt>())
            .Concat(outlines.SelectMany(outline => outline.Steps.Select(step => step.Poll)).OfType<OutlinePoll>())
            .ToList();
        var seen = 0;
        foreach (var item in objects)
        {
            foreach (var property in item.GetType().GetProperties().Where(property => property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)))
            {
                var value = property.GetValue(item);
                if (value is null)
                {
                    continue;
                }

                seen++;
                var because = item.GetType().Name + "." + property.Name;
                value.GetType().IsArray.Should().BeFalse(because);
                value.GetType().IsGenericType.Should().BeTrue(because);
                value.GetType().GetGenericTypeDefinition().Should().NotBe(typeof(List<>), because);
                ((ICollection)value).Should().NotBeNull(because);
                var collection = value.GetType().GetInterfaces().Single(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICollection<>));
                ((bool)collection.GetProperty("IsReadOnly")!.GetValue(value)!).Should().BeTrue(because);
            }
        }

        seen.Should().BeGreaterThan(500);
        outlines.Select(outline => outline.Steps.GetType()).Distinct().Should().ContainSingle().Which.Should().NotBe<List<OutlineStep>>();
    }

    [Fact]
    public void The_reference_page_names_every_public_member_of_the_outline()
    {
        var page = TestFiles.Read("docs/reference/brain-process-v1.md");
        var members = new[] { typeof(ProcessOutline), typeof(OutlineStep) }
            .SelectMany(type => type.GetProperties().Select(property => property.Name))
            .Concat(Enum.GetNames<OutlineStepKind>())
            .Concat([nameof(OutlineRoute), nameof(OutlineVariable), nameof(OutlineWindow), nameof(OutlineBinding), nameof(OutlineRetry), nameof(OutlineTimeout), nameof(OutlineInterrupt), nameof(OutlineTimer), nameof(OutlineLoop), nameof(OutlinePoll)])
            .Distinct();
        string.Join(", ", members.Where(member => !page.Contains("`" + member, StringComparison.Ordinal))).Should().BeEmpty();
        foreach (var method in new[] { nameof(ProcessOutline.From), nameof(ProcessOutline.Step), nameof(ProcessOutline.ScenarioJson), nameof(ProcessOutline.CheckScenario) })
        {
            page.Should().Contain("ProcessOutline." + method + "(");
        }
    }
}

using System.Text.Json.Nodes;
using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>W1-G, G3: the reader of <c>meta.display</c> and its warnings.</summary>
public sealed class DisplayTests
{
    private const string Variables = """{ "case_ref": { "type": "Code", "input": true } }""";

    private const string Steps = """
        {
          "s": { "kind": "wait", "event": "case.ready", "correlation": "case_ref", "next": "d" },
          "d": { "kind": "end", "result": "done" }
        }
        """;

    private const string Extra = "\"accepts\": [\"case.ready\"]";

    private static BrainProcess WithMeta(string meta)
    {
        var text = Docs.Text(Variables, Steps, Extra).Replace("\"meta\": {}", "\"meta\": " + meta, StringComparison.Ordinal);
        var loaded = BrainProcessLoader.Load(text, new ProcessLoadContext([]));
        return loaded.Process ?? throw new InvalidOperationException("The test document does not load: " + string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path)));
    }

    private const string Good = """
        {
          "title": "Plain",
          "display": {
            "en": { "title": "Case", "stages": { "one": "First" }, "steps": { "s": "Wait for it", "d": "Done" }, "results": { "done": "Finished" }, "events": { "case.ready": "Ready" } },
            "es": { "title": "Caso", "steps": { "s": "Esperar" } }
          }
        }
        """;

    [Fact]
    public void AnAskedLanguageWinsAndEnglishIsTheFallback()
    {
        var display = ProcessDisplay.From(WithMeta(Good));

        display.Title("es").Should().Be("Caso");
        display.Step("es", "s").Should().Be("Esperar");
        display.Step("es", "d").Should().Be("Done");
        display.Stage("es", "one").Should().Be("First");
        display.Result("es", "done").Should().Be("Finished");
        display.Event("en", "case.ready").Should().Be("Ready");
    }

    [Theory]
    [InlineData("fr")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AnUnknownOrMissingLanguageAsksForEnglish(string? language)
    {
        var display = ProcessDisplay.From(WithMeta(Good));

        display.Title(language).Should().Be("Case");
        display.Step(language, "s").Should().Be("Wait for it");
    }

    [Theory]
    [InlineData("ES")]
    [InlineData("es-MX")]
    [InlineData("es_419")]
    public void TheCaseAndARegionSuffixOfTheLanguageAreIgnored(string language) =>
        ProcessDisplay.From(WithMeta(Good)).Title(language).Should().Be("Caso");

    [Fact]
    public void AnIdThatNoLanguageLabelsIsNull()
    {
        var display = ProcessDisplay.From(WithMeta(Good));

        display.Step("es", "nothing").Should().BeNull();
        display.Queue("en", "front_desk").Should().BeNull();
        display.Outcome("en", "x").Should().BeNull();
        display.Window("en", "w").Should().BeNull();
        display.Step("en", null!).Should().BeNull();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "display": null }""")]
    [InlineData("""{ "display": [] }""")]
    [InlineData("""{ "display": "x" }""")]
    [InlineData("""{ "display": { "en": "x" } }""")]
    [InlineData("""{ "display": { "english": { "title": "x" } } }""")]
    [InlineData("""{ "display": { "en": { "title": 4, "steps": [], "stages": { "one": 3 }, "other": {} } } }""")]
    [InlineData("""{ "display": { "en": { "steps": { "s": "" } } } }""")]
    public void TheReaderNeverThrowsWhateverTheShapeOfMeta(string meta)
    {
        var process = WithMeta(meta);
        var display = ProcessDisplay.From(process);

        display.Title("en").Should().BeNull();
        display.Step("en", "s").Should().BeNull();
        display.Invoking(_ => ProcessDisplay.Validate(process)).Should().NotThrow();
    }

    [Fact]
    public void ALabelOfMoreThanOneHundredTwentyCharactersIsRefusedAndOneOfExactlyThatIsKept()
    {
        var long121 = new string('a', 121);
        var long120 = new string('b', 120);
        var process = WithMeta($$"""{ "display": { "en": { "steps": { "s": "{{long121}}", "d": "{{long120}}" } } } }""");

        var display = ProcessDisplay.From(process);

        display.Step("en", "s").Should().BeNull();
        display.Step("en", "d").Should().Be(long120);
        ProcessDisplay.Validate(process).Select(warning => warning.Code + "@" + warning.Path)
            .Should().Equal("process.display.invalid@/meta/display/en/steps/s");
    }

    [Fact]
    public void ADocumentWithGoodLabelsHasNoWarning() =>
        ProcessDisplay.Validate(WithMeta(Good)).Should().BeEmpty();

    [Fact]
    public void ADocumentWithoutDisplayHasNoWarningAndAnEmptyReader()
    {
        var process = WithMeta("""{ "title": "x" }""");

        ProcessDisplay.Validate(process).Should().BeEmpty();
        ProcessDisplay.From(process).Title("en").Should().BeNull();
    }

    [Fact]
    public void ALabelForAnIdTheDocumentDoesNotHaveIsAWarningAtItsPath()
    {
        var process = WithMeta("""
            { "display": { "en": {
                "stages": { "nine": "x" }, "steps": { "ghost": "x" }, "results": { "lost": "x" }, "queues": { "q": "x" },
                "outcomes": { "o": "x" }, "events": { "other.event": "x" }, "windows": { "w": "x" } } } }
            """);

        ProcessDisplay.Validate(process).Select(warning => warning.Code + "@" + warning.Path).Should().Equal(
            "process.display.unknown_id@/meta/display/en/events/other.event",
            "process.display.unknown_id@/meta/display/en/outcomes/o",
            "process.display.unknown_id@/meta/display/en/queues/q",
            "process.display.unknown_id@/meta/display/en/results/lost",
            "process.display.unknown_id@/meta/display/en/stages/nine",
            "process.display.unknown_id@/meta/display/en/steps/ghost",
            "process.display.unknown_id@/meta/display/en/windows/w");
    }

    [Fact]
    public void AMemberOfTheWrongShapeIsAnInvalidWarningAtItsPath()
    {
        var process = WithMeta("""
            { "display": { "en": { "title": 4, "steps": [], "colour": {}, "stages": { "one": 3 } }, "Spanish": {}, "es": "x" } }
            """);

        ProcessDisplay.Validate(process).Select(warning => warning.Code + "@" + warning.Path).Should().Equal(
            "process.display.invalid@/meta/display/Spanish",
            "process.display.invalid@/meta/display/en/colour",
            "process.display.invalid@/meta/display/en/stages/one",
            "process.display.invalid@/meta/display/en/steps",
            "process.display.invalid@/meta/display/en/title",
            "process.display.invalid@/meta/display/es");
    }

    [Fact]
    public void ADisplayThatIsNotAnObjectIsOneWarningAtTheMember() =>
        ProcessDisplay.Validate(WithMeta("""{ "display": [1] }""")).Select(warning => warning.Code + "@" + warning.Path)
            .Should().Equal("process.display.invalid@/meta/display");

    [Fact]
    public void EveryEventOfAnInterruptByAndAWaitCountsAsKnown()
    {
        var variables = """{ "case_ref": { "type": "Code", "input": true }, "kind": { "type": "Code" } }""";
        var steps = """
            {
              "s": { "kind": "action", "port": "p.send", "delivery": "atMostOnce", "contact": true, "window": "hours", "input": { "case": "case_ref" },
                     "interruptBy": { "event": "case.reply", "correlation": "case_ref", "output": { "kind": "kind" }, "to": "d" }, "next": "d", "onUnknown": "d", "onError": "d" },
              "d": { "kind": "end", "result": "done" }
            }
            """;
        var text = Docs.Text(variables, steps, "\"windows\": { \"hours\": { \"zone\": \"UTC\", \"days\": [\"mon\"], \"from\": \"09:00\", \"to\": \"17:00\" } }, \"accepts\": [\"case.reply\"]")
            .Replace("\"meta\": {}", "\"meta\": " + """{ "display": { "en": { "events": { "case.reply": "Reply" }, "windows": { "hours": "Hours" } } } }""", StringComparison.Ordinal);
        var loaded = BrainProcessLoader.Load(text, new ProcessLoadContext([]));

        loaded.Errors.Select(error => error.Code + "@" + error.Path).Should().BeEmpty();
        ProcessDisplay.Validate(loaded.Process!).Should().BeEmpty();
        ProcessDisplay.From(loaded.Process!).Window("en", "hours").Should().Be("Hours");
        ProcessDisplay.From(loaded.Process!).Event("en", "case.reply").Should().Be("Reply");
    }

    [Fact]
    public void TheWarningsNeverPreventLoadingAndTheLoaderReportsNone()
    {
        var text = Docs.Text(Variables, Steps, Extra).Replace("\"meta\": {}", "\"meta\": " + """{ "display": { "en": { "steps": { "ghost": "x" } }, "xx": 1 } }""", StringComparison.Ordinal);

        var loaded = BrainProcessLoader.Load(text, new ProcessLoadContext([]));

        loaded.IsValid.Should().BeTrue();
        loaded.Errors.Should().BeEmpty();
        ProcessDisplay.Validate(loaded.Process!).Should().HaveCount(2);
    }

    [Fact]
    public void ADisplayDoesNotChangeTheHashOrTheBehaviourBeyondTheMeta()
    {
        var plain = WithMeta("{}");
        var labelled = WithMeta(Good);

        var first = new Driver(plain);
        var second = new Driver(labelled);
        first.Start("""{ "case_ref": "c-1" }""");
        second.Start("""{ "case_ref": "c-1" }""");

        second.Last.Requests.Should().Equal(first.Last.Requests);
        second.Kinds.Should().Equal(first.Kinds);
        JsonNode.Parse(second.State.ToJson())!["pending"]!.ToJsonString().Should().Be(JsonNode.Parse(first.State.ToJson())!["pending"]!.ToJsonString());
    }
}

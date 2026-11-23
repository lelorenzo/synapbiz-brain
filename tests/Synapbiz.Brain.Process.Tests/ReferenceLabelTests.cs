using System.Text.Json.Nodes;
using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>The six reference documents carry display labels in English and Spanish, with nothing missing and nothing unknown.</summary>
public sealed class ReferenceLabelTests
{
    private static readonly string[] Languages = ["en", "es"];

    public static TheoryData<string> Processes => [.. ReferenceDocs.Ids];

    [Theory]
    [MemberData(nameof(Processes))]
    public void EveryStageAndStepHasALabelInBothLanguages(string id)
    {
        var display = ProcessDisplay.From(ReferenceDocs.Process(id));
        var body = ReferenceDocs.ProcessJson(id)["body"]!.AsObject();

        foreach (var language in Languages)
        {
            display.Title(language).Should().NotBeNullOrWhiteSpace(because: id + " " + language);
            foreach (var stage in body["stages"]!.AsArray().Select(node => StageName(node!)))
            {
                display.Stage(language, stage).Should().NotBeNullOrWhiteSpace(because: id + " stage " + stage + " " + language);
            }

            foreach (var step in body["steps"]!.AsObject().Select(entry => entry.Key))
            {
                display.Step(language, step).Should().NotBeNullOrWhiteSpace(because: id + " step " + step + " " + language);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Processes))]
    public void TheLabelsHaveNoWarning(string id) =>
        ProcessDisplay.Validate(ReferenceDocs.Process(id)).Should().BeEmpty(because: id);

    [Theory]
    [MemberData(nameof(Processes))]
    public void TheSpanishLabelsDifferFromTheEnglishOnes(string id)
    {
        var display = ProcessDisplay.From(ReferenceDocs.Process(id));
        var steps = ReferenceDocs.ProcessJson(id)["body"]!["steps"]!.AsObject().Select(entry => entry.Key).ToList();

        steps.Count(step => display.Step("es", step) != display.Step("en", step)).Should().BeGreaterThan(0, because: id);
    }

    [Theory]
    [MemberData(nameof(Processes))]
    public void TheDocumentStaysAsciiOnly(string id) =>
        ReferenceDocs.ProcessText(id).All(character => character < 128).Should().BeTrue(because: id);

    private static string StageName(JsonNode node) =>
        node is JsonValue ? node.GetValue<string>() : node["name"]?.GetValue<string>() ?? node["id"]!.GetValue<string>();
}

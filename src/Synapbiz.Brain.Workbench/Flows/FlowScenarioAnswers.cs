using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>What an embedded scenario answers to one step: the name of the scenario and the answer it gives.</summary>
/// <param name="Scenario">The name of the scenario.</param>
/// <param name="Answer">The answer: an output or a failure, as the scenario's <c>complete</c> or <c>fail</c> command writes it.</param>
public sealed record FlowScenarioAnswer(string Scenario, FlowAnswer Answer);

/// <summary>Looks up the answers the embedded scenarios of a document give to a step, so that a screen can offer "answer as this test does".</summary>
public static class FlowScenarioAnswers
{
    /// <summary>
    /// For every scenario of the document, in document order, the answer it gives to a step the given number of times before: the
    /// <c>occurrence</c>-th <c>complete</c> or <c>fail</c> command of that step, counting from 0. A scenario that does not answer the
    /// step that many times is left out. The answers are taken from the scenario as written; nothing is run.
    /// </summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="step">The id of the step.</param>
    /// <param name="occurrence">How many answers to the step were given before, from 0.</param>
    public static IReadOnlyList<FlowScenarioAnswer> For(BrainProcess process, string step, int occurrence)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentOutOfRangeException.ThrowIfNegative(occurrence);
        var outline = ProcessOutline.From(process);
        var answers = new List<FlowScenarioAnswer>();
        foreach (var name in outline.Scenarios)
        {
            if (FlowJson.TryObject(outline.ScenarioJson(name)) is not { } scenario || scenario["events"] is not JsonArray events)
            {
                continue;
            }

            var given = events.OfType<JsonObject>()
                .Where(item => StepOf(item) == step)
                .Select(AnswerOf)
                .OfType<FlowAnswer>()
                .ToList();
            if (occurrence < given.Count)
            {
                answers.Add(new FlowScenarioAnswer(name, given[occurrence]));
            }
        }

        return new ReadOnlyCollection<FlowScenarioAnswer>(answers);
    }

    private static string? StepOf(JsonObject item) =>
        (item["complete"] ?? item["fail"]) is JsonValue value && value.TryGetValue<string>(out var step) ? step : null;

    private static FlowAnswer? AnswerOf(JsonObject item)
    {
        if (item["complete"] is not null)
        {
            return FlowAnswer.Output(FlowJson.Canonical(item["output"] ?? new JsonObject()));
        }

        if (item["fail"] is null || item["code"] is not JsonValue code || !code.TryGetValue<string>(out var text))
        {
            return null;
        }

        var kind = item["error"] is JsonValue error && error.TryGetValue<string>(out var name) && Enum.TryParse<ProcessErrorKind>(name, out var parsed) ? parsed : ProcessErrorKind.Permanent;
        TimeSpan? retryAfter = item["retryAfter"] is JsonValue hint && hint.TryGetValue<string>(out var span) ? System.Xml.XmlConvert.ToTimeSpan(span) : null;
        return FlowAnswer.Failure(kind, text, retryAfter);
    }
}

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process;

/// <summary>What a process document is loaded against.</summary>
/// <param name="Rules">The rules a <c>decide</c> step may name, found by their id.</param>
/// <param name="KnownProcessIds">The ids of the processes a <c>subprocess</c> step may name; null when the caller does not check them.</param>
public sealed record ProcessLoadContext(
    IReadOnlyCollection<BrainRule> Rules,
    IReadOnlyCollection<string>? KnownProcessIds = null);

/// <summary>The outcome of loading a <c>brain.process@1</c> document.</summary>
/// <param name="Process">The loaded process; null when <paramref name="Errors"/> is not empty.</param>
/// <param name="Errors">Every error of the first validation step that found any, ordered by path then code.</param>
public sealed record BrainProcessLoadResult(BrainProcess? Process, IReadOnlyList<BrainSpecError> Errors)
{
    /// <summary>True when the document loaded without errors.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Loads <c>brain.process@1</c> documents: parses and validates them and turns them into a <see cref="BrainProcess"/>.</summary>
public static partial class BrainProcessLoader
{
    private const long MaximumVersion = 9_007_199_254_740_991;

    /// <summary>
    /// Loads a process document. The steps run in order and stop at the first one that reports errors, which are all
    /// returned: (1) JSON, envelope and schema; (2) the static rules of the specification (references, variables, rules,
    /// routing, uses, cycles, durations and scenarios).
    /// </summary>
    /// <param name="json">The process document.</param>
    /// <param name="context">The rules and process ids the document is checked against.</param>
    public static BrainProcessLoadResult Load(string json, ProcessLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(context);

        if (!JsonText.TryParse(json, out var document))
        {
            return Failed([Diagnostics.JsonNotValid()]);
        }

        var structure = Ordered(Structure(document));
        if (structure.Count != 0)
        {
            return Failed(structure);
        }

        var errors = new List<BrainSpecError>();
        var definition = new ModelReader(errors).Read(document!.AsObject(), json);
        new ProcessValidator(definition, context, errors).Validate();
        if (errors.Count != 0)
        {
            return Failed(Ordered(errors));
        }

        var available = ProcessValidator.IndexRules(context.Rules);
        definition.Rules = definition.Steps.OfType<DecideStep>().Select(step => step.Rule)
            .Concat(definition.Steps.OfType<PollStep>().Select(step => step.Rule))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, id => available[id], StringComparer.Ordinal);
        return new BrainProcessLoadResult(new BrainProcess(definition), []);
    }

    private static IEnumerable<BrainSpecError> Structure(JsonNode? document)
    {
        if (!HasEnvelope(document))
        {
            return [Diagnostics.Envelope()];
        }

        if (JsonText.Text(document!["$spec"]) != ProcessSchema.Format)
        {
            return [Diagnostics.UnknownFormat()];
        }

        return ProcessSchema.Violations(document).Select(Diagnostics.Schema);
    }

    private static bool HasEnvelope(JsonNode? document) =>
        document is JsonObject root
        && JsonText.Text(root["id"]) is { } id && IdPattern().IsMatch(id)
        && JsonText.Text(root["$spec"]) is { } format
        && FormatPattern().Match(format) is { Success: true } match
        && long.TryParse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture, out var major)
        && major <= MaximumVersion;

    private static BrainProcessLoadResult Failed(IReadOnlyList<BrainSpecError> errors) => new(null, errors);

    private static List<BrainSpecError> Ordered(IEnumerable<BrainSpecError> errors) =>
        [.. errors.Distinct()
            .OrderBy(error => error.Path, StringComparer.Ordinal)
            .ThenBy(error => error.Code, StringComparer.Ordinal)];

    [GeneratedRegex(@"\A([a-z][a-z0-9_]*\.[a-z][a-z0-9_]*)@([1-9][0-9]*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex FormatPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}

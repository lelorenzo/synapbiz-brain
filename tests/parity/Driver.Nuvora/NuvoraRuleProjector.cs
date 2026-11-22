using System.Text.Json.Nodes;
using Nuvora.Decisions.Application.Serialization;
using Synapbiz.Brain.Spec;
using Synapbiz.Spec.Brain;

namespace Synapbiz.Brain.Parity;

/// <summary>
/// The source repository's own path from a rule document to an engine table: its validator, its bridge to the v0
/// definition and goldens, and its definition codec. The kind descriptor comes from the repository's contract reader
/// compiled against the source repository's decision contracts.
/// </summary>
internal sealed class NuvoraRuleProjector : IRuleProjector
{
    public ProjectedRule? Project(string documentJson, IReadOnlyList<string> contractJsons, out IReadOnlyList<(string Code, string Path)> errors)
    {
        var document = JsonNode.Parse(documentJson)!.AsObject();
        var validator = new BrainValidator(contractJsons.Select(json => JsonNode.Parse(json)!.AsObject()));
        var validation = validator.Validate(document);
        errors = [.. validation.Errors.Select(error => (error.Code, error.Path))];
        if (!validation.IsValid)
        {
            return null;
        }

        var legacy = LegacyBrainBridge.FromRule(document);
        var table = DecisionDefinitionCodec.ParseDefinition(legacy.DefinitionJson);
        var goldens = DecisionDefinitionCodec.ParseGoldens(legacy.GoldensJson, table.HitPolicy);
        var kindId = document["body"]!["kind"]!.GetValue<string>();
        foreach (var contractJson in contractJsons)
        {
            if (BrainContract.Parse(contractJson).TryGetKind(kindId, out var kind))
            {
                return new ProjectedRule(table, goldens, kind);
            }
        }

        throw new InvalidOperationException($"No contract declares the kind '{kindId}'.");
    }
}

internal static partial class RuleProjectorFactory
{
    internal static partial IRuleProjector Create() => new NuvoraRuleProjector();
}

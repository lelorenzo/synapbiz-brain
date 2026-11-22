using System.Text.Json;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Kinds;
using static Synapbiz.Brain.Spec.Json;

namespace Synapbiz.Brain.Spec;

/// <summary>Loads <c>brain.rule@1</c> documents: parses and validates them against their module contracts and projects them into decision tables.</summary>
public static class BrainRuleLoader
{
    private static readonly string[] Deferred = ["overlap", "coverage", "goldens", "outcome-policy"];

    /// <summary>
    /// Loads a rule document. The steps run in order and stop at the first one that reports errors, which are all
    /// returned: (1) JSON, envelope and schema, with the golden-outcome check; (2) ids, versions and references of
    /// every node; (3) the module contracts: owning contract, kind, facts, operators, typed values, outcomes and
    /// goldens; (4) the projection into a decision table and golden cases.
    /// </summary>
    /// <param name="json">The rule document.</param>
    /// <param name="contracts">The module contracts available, looked up by their address (<c>module@version</c>).</param>
    /// <param name="knownSectionRefs">The addresses (<c>kind/id</c>) of other sections that <c>uses</c> may refer to.</param>
    public static BrainRuleLoadResult Load(
        string json, IReadOnlyCollection<BrainContract> contracts, IReadOnlyCollection<string>? knownSectionRefs = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(contracts);

        JsonNode? document;
        try
        {
            document = JsonNode.Parse(json);
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return Failed([Diagnostics.JsonNotValid()]);
        }

        var structure = Ordered(StructureValidation.Validate(document));
        if (structure.Count != 0)
        {
            return Failed(structure);
        }

        var root = document!.AsObject();
        var byAddress = new Dictionary<string, BrainContract>(StringComparer.Ordinal);
        foreach (var contract in contracts)
        {
            byAddress[contract.Address] = contract;
        }

        var semantics = Ordered(NodeValidation.Validate(root, knownSectionRefs).Concat(RuleSemantics.Validate(root, byAddress)));
        if (semantics.Count != 0)
        {
            return Failed(semantics);
        }

        var body = root["body"]!.AsObject();
        ProjectedRule projected;
        try
        {
            projected = RuleProjection.Project(body);
        }
        catch (ProjectionException error)
        {
            return Failed([Diagnostics.ProjectionMalformed(error.Path, error.Message)]);
        }

        var kindId = Text(body["kind"])!;
        var kind = FindKind(root, byAddress, kindId);
        var meta = (JsonObject)root["meta"]!.AsObject().DeepClone();
        var rule = new BrainRule(
            Text(root["id"])!,
            SectionIdentity.Version(root["version"])!.Value,
            kind,
            projected.Table,
            projected.Goldens,
            projected.RowIdsByKey,
            meta);
        return new BrainRuleLoadResult(rule, [], Deferred);
    }

    private static DecisionKindDescriptor FindKind(JsonObject root, Dictionary<string, BrainContract> byAddress, string kindId)
    {
        var module = kindId.Split('.', 2)[0];
        foreach (var (requirement, _) in Items(root["requires"], "/requires"))
        {
            var address = Text(requirement)!;
            if (address.StartsWith(module + "@", StringComparison.Ordinal)
                && byAddress.TryGetValue(address, out var contract)
                && contract.TryGetKind(kindId, out var kind))
            {
                return kind;
            }
        }

        throw new InvalidOperationException($"The kind '{kindId}' passed validation but is declared by no required contract.");
    }

    private static BrainRuleLoadResult Failed(IReadOnlyList<BrainSpecError> errors) => new(null, errors, []);

    private static List<BrainSpecError> Ordered(IEnumerable<BrainSpecError> errors) =>
        [.. errors.Distinct()
            .OrderBy(error => error.Path, StringComparer.Ordinal)
            .ThenBy(error => error.Code, StringComparer.Ordinal)];
}

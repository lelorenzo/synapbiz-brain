using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Parity;

/// <summary>The Brain library's projection: its contract reader and rule loader.</summary>
internal sealed class LibraryRuleProjector : IRuleProjector
{
    public ProjectedRule? Project(string documentJson, IReadOnlyList<string> contractJsons, out IReadOnlyList<(string Code, string Path)> errors)
    {
        var contracts = contractJsons.Select(BrainContract.Parse).ToList();
        var result = BrainRuleLoader.Load(documentJson, contracts);
        errors = [.. result.Errors.Select(error => (error.Code, error.Path))];
        return result.Rule is { } rule ? new ProjectedRule(rule.Table, rule.Goldens, rule.Kind) : null;
    }
}

internal static partial class RuleProjectorFactory
{
    internal static partial IRuleProjector Create() => new LibraryRuleProjector();
}

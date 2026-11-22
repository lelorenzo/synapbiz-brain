using System.Text.RegularExpressions;

namespace Nuvora.Decisions.Contracts.Kinds;

/// <summary>The shared catalogue identity is three lowercase snake-case segments, separate from persisted local names.</summary>
public static partial class CatalogFactIdGrammar
{
    /// <summary>Checks module.entity.fact without culture-dependent casing or normalization.</summary>
    public static bool IsValid(string value) => Pattern().IsMatch(value);

    [GeneratedRegex(@"\A[a-z][a-z0-9]*(?:_[a-z0-9]+)*(?:\.[a-z][a-z0-9]*(?:_[a-z0-9]+)*){2}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Pattern();
}

using System.Text.RegularExpressions;

namespace Synapbiz.Brain.Authoring;

/// <summary>The one pattern every id and name of the workspace must match before it becomes part of a path.</summary>
public static partial class StoreIds
{
    /// <summary>The longest id or name.</summary>
    public const int MaximumLength = 120;

    /// <summary>True when the text is a letter, digit or underscore followed by letters, digits, <c>_</c>, <c>.</c> and <c>-</c>, at most 120 characters, with no <c>..</c>, no trailing dot and no ending <c>.draft</c> or <c>.tmp</c>, which the store keeps for itself.</summary>
    /// <param name="id">The candidate id.</param>
    public static bool IsValid(string? id) =>
        id is { Length: > 0 and <= MaximumLength }
        && Pattern().IsMatch(id)
        && !id.Contains("..", StringComparison.Ordinal)
        && !id.EndsWith('.')
        && !id.EndsWith(".draft", StringComparison.OrdinalIgnoreCase)
        && !id.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
        && !IsReserved(id);

    /// <summary>True when Windows would not let a file have this name (<c>con</c>, <c>nul</c>, <c>com1</c> and the others, with or without an extension, in any case).</summary>
    /// <param name="id">The candidate id.</param>
    public static bool IsReserved(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var dot = id.IndexOf('.', StringComparison.Ordinal);
        var stem = dot < 0 ? id : id[..dot];
        return Reserved.Contains(stem.TrimEnd(' '));
    }

    /// <summary>True when the two ids are not the same but differ only in capital letters, which a file system that ignores case would take for one file.</summary>
    /// <param name="first">An id.</param>
    /// <param name="second">Another id.</param>
    public static bool DiffersOnlyByCase(string first, string second) =>
        !string.Equals(first, second, StringComparison.Ordinal) && string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> Reserved = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(0, 10).SelectMany(digit => new[] { "COM" + digit, "LPT" + digit })],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Throws <see cref="ArgumentException"/> unless <see cref="IsValid"/>.</summary>
    /// <param name="id">The candidate id.</param>
    /// <param name="parameter">The name of the parameter, for the exception.</param>
    public static void Require(string? id, string parameter)
    {
        if (!IsValid(id))
        {
            throw new ArgumentException("An id is a letter, digit or underscore followed by letters, digits, '_', '.' and '-' (at most 120 characters, no '..', not a reserved name such as con or nul).", parameter);
        }
    }

    [GeneratedRegex(@"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

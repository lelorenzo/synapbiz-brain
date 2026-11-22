using System.Text.RegularExpressions;

namespace Nuvora.Decisions.Contracts.Kinds;

/// <summary>
/// Stable identifier of a decision kind: <c>{owning_module}.{kind_name}</c>,
/// exactly one dot, lowercase snake_case segments on both sides (D-04/D-05).
/// The first segment is the module that owns the kind's fact schema and
/// outcome type; <see cref="Module"/> exposes it so a registry can refuse a
/// kind whose module segment does not match the registering module
/// (AT-DEC-03 covers the assembly-reference half of that rule; the key-shape
/// half is this type).
/// </summary>
/// <remarks>Introduced in P2-20.2 (paquete 0.A369).</remarks>
public readonly partial record struct DecisionKindKey
{
    /// <summary>Stable error code: the key does not match the required shape.</summary>
    public const string ErrorInvalidFormat = "decisions.kind.key_invalid";

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();

    /// <summary>The canonical <c>{module}.{kind}</c> string. Empty for a default-constructed (uninitialized) key.</summary>
    public string Value { get; } = string.Empty;

    private DecisionKindKey(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Parses <paramref name="value"/> into a <see cref="DecisionKindKey"/>.
    /// Throws <see cref="DecisionEngineException"/> with
    /// <see cref="ErrorInvalidFormat"/> when it is null/empty/whitespace or
    /// does not match <c>^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$</c> — exactly one
    /// dot, a lowercase-snake-case segment on each side.
    /// </summary>
    public static DecisionKindKey From(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !KeyRegex().IsMatch(value))
        {
            throw new DecisionEngineException(
                ErrorInvalidFormat,
                $"'{value}' is not a valid decision kind key (expected '{{module}}.{{kind}}', lowercase snake_case, exactly one dot).",
                new Dictionary<string, object?> { ["value"] = value });
        }

        return new DecisionKindKey(value);
    }

    /// <summary>The first segment (before the dot) — the owning module id.</summary>
    public string Module => Value[..Value.IndexOf('.', StringComparison.Ordinal)];

    /// <inheritdoc />
    public override string ToString() => Value;
}

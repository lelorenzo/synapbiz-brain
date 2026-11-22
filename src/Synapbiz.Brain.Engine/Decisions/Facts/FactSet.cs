using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Conditions;
using Synapbiz.Brain;

namespace Nuvora.Decisions.Engine.Facts;

/// <summary>
/// A validated, immutable set of fact values for one decision kind (D-04).
/// <see cref="Build"/> checks the caller's values against the kind's
/// descriptor — every unknown (undeclared) fact and every missing
/// non-nullable, non-composed fact is refused with
/// <see cref="ErrorMissing"/> — and against the kind's byte cap
/// (<see cref="CanonicalFacts.ErrorTooLarge"/>). A fact declared with
/// <see cref="FactDescriptor.ComposedFromKind"/> is never required here: its
/// value arrives later, from composition (D-14, wired by the evaluator).
/// </summary>
/// <remarks>Introduced in P2-20.2 (paquete 0.A369).</remarks>
public sealed class FactSet
{
    /// <summary>Stable error code: the fact set has an undeclared fact or is missing a required one.</summary>
    public const string ErrorMissing = "decisions.facts.missing";

    /// <summary>Stable error code: a caller-supplied value's runtime DecisionValue case does not match its fact's declared FactType.</summary>
    public const string ErrorTypeMismatch = "decisions.facts.type_mismatch";

    private readonly Dictionary<string, DecisionValue> _values;

    private FactSet(Dictionary<string, DecisionValue> values)
    {
        _values = values;
    }

    /// <summary>The validated fact values, keyed by fact name.</summary>
    public IReadOnlyDictionary<string, DecisionValue> Values => _values;

    /// <summary>
    /// Builds a <see cref="FactSet"/> for <paramref name="descriptor"/> from
    /// <paramref name="callerValues"/>. Throws <see cref="DecisionEngineException"/>
    /// (<see cref="ErrorMissing"/>) when <paramref name="callerValues"/> carries
    /// a fact name the descriptor does not declare, or omits a fact that is
    /// both non-nullable and not <see cref="FactDescriptor.ComposedFromKind"/>;
    /// throws (<see cref="ErrorTypeMismatch"/>) when a value's case is not the one its
    /// fact's declared <see cref="FactType"/> carries; throws (<see cref="CanonicalFacts.ErrorTooLarge"/>) when the resulting
    /// canonical JSON exceeds <see cref="DecisionKindDescriptor.MaxFactsBytes"/>.
    /// </summary>
    public static FactSet Build(DecisionKindDescriptor descriptor, IReadOnlyDictionary<string, DecisionValue> callerValues)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(callerValues);

        var declared = descriptor.Facts.ToDictionary(f => f.Name, StringComparer.Ordinal);

        var unknown = callerValues.Keys
            .Where(name => !declared.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var missing = descriptor.Facts
            .Where(f => !f.Nullable && f.ComposedFromKind is null && !callerValues.ContainsKey(f.Name))
            .Select(f => f.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (unknown.Count > 0 || missing.Count > 0)
        {
            throw new DecisionEngineException(
                ErrorMissing,
                $"Fact set for kind '{descriptor.Key}' has unknown facts [{string.Join(", ", unknown)}] "
                + $"and/or is missing required facts [{string.Join(", ", missing)}].",
                new Dictionary<string, object?>
                {
                    ["kind"] = descriptor.Key.Value,
                    ["unknown"] = unknown,
                    ["missing"] = missing,
                });
        }

        var mistyped = callerValues
            .Where(entry => !ValueTypes.Matches(declared[entry.Key].Type, entry.Value))
            .Select(entry => entry.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (mistyped.Count > 0)
        {
            var first = mistyped[0];
            throw new DecisionEngineException(
                ErrorTypeMismatch,
                $"Fact '{first}' of kind '{descriptor.Key}' must be a {declared[first].Type} value but is a {callerValues[first].GetType().Name}"
                + (mistyped.Count > 1 ? $" (and {mistyped.Count - 1} more mistyped)." : "."),
                new Dictionary<string, object?>
                {
                    ["kind"] = descriptor.Key.Value,
                    ["mistyped"] = mistyped,
                });
        }

        var values = new Dictionary<string, DecisionValue>(callerValues, StringComparer.Ordinal);
        var canonicalBytes = CanonicalFacts.ToCanonicalJsonBytes(values);
        CanonicalFacts.EnsureWithinCap(canonicalBytes, descriptor.MaxFactsBytes, descriptor.Key.Value);

        return new FactSet(values);
    }

    /// <summary>Canonical JSON of <see cref="Values"/> (CanonicalFacts).</summary>
    public string ToCanonicalJson() => CanonicalFacts.ToCanonicalJson(_values);

    /// <summary>SHA-256 of the canonical JSON's UTF-8 bytes.</summary>
    public ContentHash Hash() => CanonicalFacts.Hash(_values);

    /// <summary>
    /// Returns a new <see cref="FactSet"/> with fact <paramref name="name"/>
    /// set to <paramref name="value"/> (overwriting any existing value) —
    /// used by the evaluator to fold a resolved composed fact into the
    /// working set without re-validating against the descriptor (the
    /// composed fact, by definition, is not one <see cref="Build"/> required
    /// from the caller).
    /// </summary>
    internal FactSet With(string name, DecisionValue value)
    {
        var merged = new Dictionary<string, DecisionValue>(_values, StringComparer.Ordinal) { [name] = value };
        return new FactSet(merged);
    }
}

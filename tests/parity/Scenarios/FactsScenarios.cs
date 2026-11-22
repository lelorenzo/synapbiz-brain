using Nuvora.Decisions.Contracts;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Facts;

namespace Synapbiz.Brain.Parity;

/// <summary>Canonical facts JSON and hash: every type, key order, decimal scale, string escaping and the size cap.</summary>
internal static class FactsScenarios
{
    internal static void Register(ScenarioSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        set.Add("facts/all_eight_types", () => Canonical(new Dictionary<string, DecisionValue>
        {
            ["code"] = DecisionValue.Code("A-1"),
            ["enm"] = DecisionValue.Enum("B"),
            ["flag"] = DecisionValue.Boolean(true),
            ["count"] = DecisionValue.Integer(-42),
            ["ratio"] = DecisionValue.Decimal(2.50m),
            ["amount"] = DecisionValue.Money(12.50m, "USD"),
            ["day"] = DecisionValue.Date(new DateOnly(2026, 11, 18)),
            ["partner"] = DecisionValue.Reference(new Guid("33333333-3333-3333-3333-333333333333")),
        }));

        set.Add("facts/insertion_order_forward", () => Canonical(new Dictionary<string, DecisionValue>
        {
            ["alpha"] = DecisionValue.Code("1"),
            ["beta"] = DecisionValue.Integer(2),
            ["gamma"] = DecisionValue.Boolean(false),
        }));

        set.Add("facts/insertion_order_reverse", () => Canonical(new Dictionary<string, DecisionValue>
        {
            ["gamma"] = DecisionValue.Boolean(false),
            ["beta"] = DecisionValue.Integer(2),
            ["alpha"] = DecisionValue.Code("1"),
        }));

        set.Add("facts/insertion_order_results_are_equal", () =>
        {
            var forward = CanonicalFacts.ToCanonicalJson(new Dictionary<string, DecisionValue>
            {
                ["alpha"] = DecisionValue.Code("1"),
                ["beta"] = DecisionValue.Integer(2),
            });
            var reverse = CanonicalFacts.ToCanonicalJson(new Dictionary<string, DecisionValue>
            {
                ["beta"] = DecisionValue.Integer(2),
                ["alpha"] = DecisionValue.Code("1"),
            });
            return Format.Bool(string.Equals(forward, reverse, StringComparison.Ordinal));
        });

        set.Add("facts/decimal_1_50", () => Canonical(Single("v", DecisionValue.Decimal(1.50m))));
        set.Add("facts/decimal_1_5", () => Canonical(Single("v", DecisionValue.Decimal(1.5m))));
        set.Add("facts/decimal_zero_with_scale", () => Canonical(Single("v", DecisionValue.Decimal(0.00m))));
        set.Add("facts/decimal_whole_with_scale", () => Canonical(Single("v", DecisionValue.Decimal(100.000m))));
        set.Add("facts/decimal_small_fraction", () => Canonical(Single("v", DecisionValue.Decimal(0.0010m))));
        set.Add("facts/decimal_negative", () => Canonical(Single("v", DecisionValue.Decimal(-3.1400m))));
        set.Add("facts/money_trailing_zeros", () => Canonical(Single("v", DecisionValue.Money(10.00m, "USD"))));
        set.Add("facts/money_no_trailing_zeros", () => Canonical(Single("v", DecisionValue.Money(10m, "USD"))));
        set.Add("facts/string_escaping", () => Canonical(Single("v", DecisionValue.Code("quote\" backslash\\ tab\t accent é emoji 😀 <&>"))));
        set.Add("facts/empty_set", () => Canonical(new Dictionary<string, DecisionValue>()));

        set.Add("facts/cap_exactly_at_default", () => EnsureCap(DecisionKindDescriptor.DefaultMaxFactsBytes));
        set.Add("facts/cap_one_byte_over_default", () => EnsureCap(DecisionKindDescriptor.DefaultMaxFactsBytes + 1));
        set.Add("facts/cap_large_set_over_default", () => EnsureCap(DecisionKindDescriptor.DefaultMaxFactsBytes * 4));
        set.Add("facts/build_over_default_cap", () => BuildOverCap(DecisionKindDescriptor.DefaultMaxFactsBytes + 1));
        set.Add("facts/build_at_default_cap", () => BuildOverCap(DecisionKindDescriptor.DefaultMaxFactsBytes));
    }

    private static Dictionary<string, DecisionValue> Single(string name, DecisionValue value) =>
        new() { [name] = value };

    private static string Canonical(IReadOnlyDictionary<string, DecisionValue> values) =>
        CanonicalFacts.ToCanonicalJson(values) + "|" + CanonicalFacts.Hash(values).Value;

    /// <summary>The canonical JSON of one Code fact named "x" is <c>{"x":"..."}</c>: 8 bytes of structure plus the padding.</summary>
    private static Dictionary<string, DecisionValue> Padded(int totalBytes) =>
        Single("x", DecisionValue.Code(new string('a', totalBytes - 8)));

    private static string EnsureCap(int totalBytes)
    {
        var bytes = CanonicalFacts.ToCanonicalJsonBytes(Padded(totalBytes));
        CanonicalFacts.EnsureWithinCap(bytes, DecisionKindDescriptor.DefaultMaxFactsBytes, "synthetic.cap");
        return "within_cap:" + Format.Int(bytes.Length);
    }

    private static string BuildOverCap(int totalBytes)
    {
        var descriptor = Fixtures.Descriptor("synthetic.cap", [new FactDescriptor("x", FactType.Code)]);
        var facts = FactSet.Build(descriptor, Padded(totalBytes));
        return facts.Hash().Value;
    }
}

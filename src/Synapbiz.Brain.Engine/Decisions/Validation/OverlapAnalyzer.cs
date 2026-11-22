using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;

namespace Nuvora.Decisions.Engine.Validation;

/// <summary>
/// Pairwise column analysis between two rows' conditions on the same fact
/// (D-12): <see cref="RowsOverlap"/> for HitPolicy.Unique's overlap check,
/// <see cref="IsSubsumedByEarlierRow"/> for HitPolicy.First's unreachable
/// check. An unlisted column on either row is treated as <see cref="Operator.Any"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RowsOverlap"/> errs toward reporting an overlap when a
/// combination is not fully modeled: a false alarm on an exotic combination
/// is a reviewable nuisance, while a missed hazard would let an
/// authoring-time ambiguity ship undetected (GUARD-VERIFICATION-STANDARD —
/// a wrong-question pass is worse than an over-eager one here).
/// </para>
/// <para>
/// <see cref="IsSubsumedByEarlierRow"/> errs the OTHER way: it returns false
/// (not proven unreachable) whenever subsumption cannot be established,
/// because wrongly flagging a legitimately reachable row would block a
/// valid table at authoring time — the worse failure for a reachability
/// HINT rather than a safety guard.
/// </para>
/// </remarks>
/// <remarks>Introduced in P2-20.2 (paquete 0.A369).</remarks>
public static class OverlapAnalyzer
{
    public static bool RowsOverlap(Row a, Row b, DecisionKindDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(descriptor);

        foreach (var factName in ColumnsOf(a, b))
        {
            var fact = FactOf(descriptor, factName);
            var domainA = ToDomain(ConditionOn(a, factName));
            var domainB = ToDomain(ConditionOn(b, factName));
            if (!Compatible(domainA, domainB, fact))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsSubsumedByEarlierRow(Row later, Row earlier)
    {
        ArgumentNullException.ThrowIfNull(later);
        ArgumentNullException.ThrowIfNull(earlier);

        foreach (var factName in ColumnsOf(later, earlier))
        {
            var laterCondition = ConditionOn(later, factName);
            var earlierCondition = ConditionOn(earlier, factName);
            if (!IsSubsetOf(laterCondition, earlierCondition))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> ColumnsOf(Row x, Row y) =>
        x.Conditions.Select(c => c.FactName).Union(y.Conditions.Select(c => c.FactName), StringComparer.Ordinal);

    private static FactDescriptor FactOf(DecisionKindDescriptor descriptor, string factName) =>
        descriptor.Facts.First(f => string.Equals(f.Name, factName, StringComparison.Ordinal));

    private static Condition ConditionOn(Row row, string factName) =>
        row.Conditions.FirstOrDefault(c => string.Equals(c.FactName, factName, StringComparison.Ordinal))
        ?? Condition.Any(factName);

    // ---- Overlap: a small domain-set algebra per column ----

    private abstract record DomainSet
    {
        public sealed record Everything : DomainSet;

        public sealed record NullOnly : DomainSet;

        public sealed record NonNullAny : DomainSet;

        public sealed record Allowed(HashSet<DecisionValue> Values) : DomainSet;

        public sealed record Excluded(HashSet<DecisionValue> Values) : DomainSet;

        public sealed record Interval(DecisionValue? Lower, bool LowerInclusive, DecisionValue? Upper, bool UpperInclusive) : DomainSet;

        public sealed record Prefix(string Value) : DomainSet;
    }

    private static DomainSet ToDomain(Condition c) => c.Operator switch
    {
        Operator.Any => new DomainSet.Everything(),
        Operator.IsNull => new DomainSet.NullOnly(),
        Operator.IsNotNull => new DomainSet.NonNullAny(),
        Operator.Equals => new DomainSet.Allowed([c.Value!]),
        Operator.NotEquals => new DomainSet.Excluded([c.Value!]),
        Operator.In => new DomainSet.Allowed([.. c.Values]),
        Operator.NotIn => new DomainSet.Excluded([.. c.Values]),
        Operator.Range => new DomainSet.Interval(c.Value, c.LowerInclusive, c.UpperValue, c.UpperInclusive),
        Operator.StartsWith => new DomainSet.Prefix(((CodeValue)c.Value!).Value),
        _ => throw new ArgumentOutOfRangeException(nameof(c), c.Operator, "Unknown operator."),
    };

    private static bool Compatible(DomainSet x, DomainSet y, FactDescriptor fact)
    {
        if (x is DomainSet.Everything || y is DomainSet.Everything)
        {
            return true;
        }

        if (x is DomainSet.NullOnly || y is DomainSet.NullOnly)
        {
            return x is DomainSet.NullOnly && y is DomainSet.NullOnly;
        }

        if (x is DomainSet.NonNullAny)
        {
            return !IsProvablyEmpty(y, fact);
        }

        if (y is DomainSet.NonNullAny)
        {
            return !IsProvablyEmpty(x, fact);
        }

        if (MoneyCurrenciesAreDisjoint(x, y))
        {
            // D-11 / E-03: a Money condition speaks only to facts of its own currency, so two
            // conditions whose Money operands share no currency can never be satisfied by the
            // same fact, whichever operators or open bounds they use.
            return false;
        }

        return (x, y) switch
        {
            (DomainSet.Allowed a, DomainSet.Allowed b) => a.Values.Overlaps(b.Values),
            (DomainSet.Allowed a, DomainSet.Excluded b) => a.Values.Except(b.Values).Any(),
            (DomainSet.Excluded a, DomainSet.Allowed b) => b.Values.Except(a.Values).Any(),
            (DomainSet.Excluded a, DomainSet.Excluded b) => !IsBoundedDomainExhausted([.. a.Values, .. b.Values], fact),
            (DomainSet.Interval a, DomainSet.Interval b) => IntervalsOverlap(a, b),
            (DomainSet.Allowed a, DomainSet.Interval b) => a.Values.Any(v => InInterval(v, b)),
            (DomainSet.Interval a, DomainSet.Allowed b) => b.Values.Any(v => InInterval(v, a)),
            (DomainSet.Prefix a, DomainSet.Prefix b) =>
                a.Value.StartsWith(b.Value, StringComparison.Ordinal) || b.Value.StartsWith(a.Value, StringComparison.Ordinal),
            (DomainSet.Prefix a, DomainSet.Allowed b) => b.Values.OfType<CodeValue>().Any(v => v.Value.StartsWith(a.Value, StringComparison.Ordinal)),
            (DomainSet.Allowed a, DomainSet.Prefix b) => a.Values.OfType<CodeValue>().Any(v => v.Value.StartsWith(b.Value, StringComparison.Ordinal)),

            // Unmodeled combination (e.g. Excluded/Interval cross-pairs, Prefix/Excluded):
            // assume compatible — see the "errs toward overlap" remark on the type.
            _ => true,
        };
    }

    private static bool MoneyCurrenciesAreDisjoint(DomainSet x, DomainSet y)
    {
        var currenciesX = MoneyCurrencies(x);
        var currenciesY = MoneyCurrencies(y);
        return currenciesX.Count > 0 && currenciesY.Count > 0 && !currenciesX.Overlaps(currenciesY);
    }

    private static HashSet<string> MoneyCurrencies(DomainSet set)
    {
        IEnumerable<DecisionValue?> operands = set switch
        {
            DomainSet.Allowed a => a.Values,
            DomainSet.Excluded e => e.Values,
            DomainSet.Interval i => [i.Lower, i.Upper],
            _ => [],
        };

        return [.. operands.OfType<MoneyValue>().Select(m => m.Currency)];
    }

    private static bool IsProvablyEmpty(DomainSet set, FactDescriptor fact) => set switch
    {
        DomainSet.Allowed a => a.Values.Count == 0,
        DomainSet.Excluded e => IsBoundedDomainExhausted(e.Values, fact),
        DomainSet.Interval { Lower: not null, Upper: not null } i => i.LowerInclusive && i.UpperInclusive
            ? CompareOrdered(i.Lower!, i.Upper!) > 0
            : CompareOrdered(i.Lower!, i.Upper!) >= 0,
        _ => false,
    };

    private static bool IsBoundedDomainExhausted(IEnumerable<DecisionValue> excluded, FactDescriptor fact)
    {
        var fullDomain = FullDomainOrNull(fact);
        return fullDomain is not null && fullDomain.All(excluded.Contains);
    }

    private static HashSet<DecisionValue>? FullDomainOrNull(FactDescriptor fact) => fact.Type switch
    {
        FactType.Boolean => [DecisionValue.Boolean(true), DecisionValue.Boolean(false)],
        FactType.Enum when fact.AllowedValues is not null => [.. fact.AllowedValues.Select(DecisionValue.Enum)],
        _ => null,
    };

    private static bool IntervalsOverlap(DomainSet.Interval a, DomainSet.Interval b) =>
        BoundsCanCoexist(a.Upper, a.UpperInclusive, b.Lower, b.LowerInclusive)
        && BoundsCanCoexist(b.Upper, b.UpperInclusive, a.Lower, a.LowerInclusive);

    private static bool BoundsCanCoexist(DecisionValue? upper, bool upperInclusive, DecisionValue? lower, bool lowerInclusive)
    {
        if (upper is null || lower is null)
        {
            return true;
        }

        if (!CurrenciesCompatible(upper, lower))
        {
            // D-11: a Money bound only ever compares within its own currency. A fact has
            // exactly one currency, so it can satisfy at most one side's currency
            // requirement when the two bounds disagree - no fact value can ever coexist
            // with both, hence no overlap on this column.
            return false;
        }

        var cmp = CompareOrdered(upper, lower);
        if (cmp > 0)
        {
            return true;
        }

        return cmp == 0 && upperInclusive && lowerInclusive;
    }

    private static bool InInterval(DecisionValue v, DomainSet.Interval interval)
    {
        if ((interval.Lower is not null && !CurrenciesCompatible(v, interval.Lower))
            || (interval.Upper is not null && !CurrenciesCompatible(v, interval.Upper)))
        {
            // D-11: a Money value of a different currency than the interval's bound(s) is
            // never "in" that interval - the interval does not speak to that currency.
            return false;
        }

        if (interval.Lower is not null)
        {
            var cmp = CompareOrdered(v, interval.Lower);
            if (interval.LowerInclusive ? cmp < 0 : cmp <= 0)
            {
                return false;
            }
        }

        if (interval.Upper is not null)
        {
            var cmp = CompareOrdered(v, interval.Upper);
            if (interval.UpperInclusive ? cmp > 0 : cmp >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int CompareOrdered(DecisionValue a, DecisionValue b) => (a, b) switch
    {
        (IntegerValue x, IntegerValue y) => x.Value.CompareTo(y.Value),
        (DecimalValue x, DecimalValue y) => x.Value.CompareTo(y.Value),
        (DateValue x, DateValue y) => x.Value.CompareTo(y.Value),
        (MoneyValue x, MoneyValue y) => x.Amount.CompareTo(y.Amount),
        _ => throw new InvalidOperationException($"Cannot order-compare a {a.GetType().Name} with a {b.GetType().Name}."),
    };

    /// <summary>
    /// D-11: two Money values only ever compare within the same currency - the engine
    /// never converts. Not Money on either side is always compatible (every other
    /// comparable type has one implicit currency-equivalent, itself).
    /// </summary>
    private static bool CurrenciesCompatible(DecisionValue a, DecisionValue b) =>
        a is not MoneyValue moneyA || b is not MoneyValue moneyB || string.Equals(moneyA.Currency, moneyB.Currency, StringComparison.Ordinal);

    // ---- Subsumption (FIRST unreachability): a conservative subset check ----

    private static bool IsSubsetOf(Condition later, Condition earlier)
    {
        if (ConditionsEqual(later, earlier) || earlier.Operator == Operator.Any)
        {
            return true;
        }

        if (TryAllowedSet(later, out var laterSet) && TryAllowedSet(earlier, out var earlierSet))
        {
            return laterSet.IsSubsetOf(earlierSet);
        }

        if (later.Operator == Operator.Range && earlier.Operator == Operator.Range)
        {
            return LowerIsAtLeastAsStrict(later.Value, later.LowerInclusive, earlier.Value, earlier.LowerInclusive)
                && UpperIsAtLeastAsStrict(later.UpperValue, later.UpperInclusive, earlier.UpperValue, earlier.UpperInclusive);
        }

        // Unmodeled combination: not proven subsumed — see the "errs toward
        // reachability" remark on the type.
        return false;
    }

    private static bool ConditionsEqual(Condition a, Condition b) =>
        string.Equals(a.FactName, b.FactName, StringComparison.Ordinal)
        && a.Operator == b.Operator
        && Equals(a.Value, b.Value)
        && Equals(a.UpperValue, b.UpperValue)
        && a.LowerInclusive == b.LowerInclusive
        && a.UpperInclusive == b.UpperInclusive
        && a.Values.SequenceEqual(b.Values);

    private static bool TryAllowedSet(Condition c, out HashSet<DecisionValue> set)
    {
        switch (c.Operator)
        {
            case Operator.Equals:
                set = [c.Value!];
                return true;
            case Operator.In:
                set = [.. c.Values];
                return true;
            default:
                set = [];
                return false;
        }
    }

    private static bool LowerIsAtLeastAsStrict(
        DecisionValue? laterLower, bool laterInclusive, DecisionValue? earlierLower, bool earlierInclusive)
    {
        if (earlierLower is null)
        {
            return true;
        }

        if (laterLower is null)
        {
            return false;
        }

        if (!CurrenciesCompatible(laterLower, earlierLower))
        {
            // D-11: a later Money bound in a different currency than the earlier row's
            // bound is never a subset of it - the two describe disjoint fact spaces (a
            // fact has one currency), regardless of how the amounts compare.
            return false;
        }

        var cmp = CompareOrdered(laterLower, earlierLower);
        if (cmp != 0)
        {
            return cmp > 0;
        }

        return earlierInclusive || !laterInclusive;
    }

    private static bool UpperIsAtLeastAsStrict(
        DecisionValue? laterUpper, bool laterInclusive, DecisionValue? earlierUpper, bool earlierInclusive)
    {
        if (earlierUpper is null)
        {
            return true;
        }

        if (laterUpper is null)
        {
            return false;
        }

        if (!CurrenciesCompatible(laterUpper, earlierUpper))
        {
            // D-11: same reasoning as LowerIsAtLeastAsStrict's currency guard, for the upper bound.
            return false;
        }

        var cmp = CompareOrdered(laterUpper, earlierUpper);
        if (cmp != 0)
        {
            return cmp < 0;
        }

        return earlierInclusive || !laterInclusive;
    }
}

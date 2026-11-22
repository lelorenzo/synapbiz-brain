using System.Text.Json;

using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Contracts.Validation;
using Nuvora.Decisions.Engine.Conditions;
using Nuvora.Decisions.Engine.Serialization;

namespace Nuvora.Decisions.Engine.Validation;

/// <summary>
/// Validates a <see cref="DecisionTable"/> against its <see cref="DecisionKindDescriptor"/>
/// at publish (D-12): per-cell type mismatch, completeness, UNIQUE overlap,
/// FIRST unreachability, Money currency consistency within one row, and the
/// kind's own outcome semantics via <see cref="IDecisionOutcomeValidator"/>.
/// Mechanical and total — it never throws for a malformed table; every
/// defect becomes a typed <see cref="ValidationIssue"/> in the returned
/// <see cref="ValidationReport"/> instead.
/// </summary>
/// <remarks>Introduced in P2-20.2 (paquete 0.A369).</remarks>
public static class TableValidator
{
    public const string CodeHitPolicyUnsupported = "decisions.validation.hit_policy_unsupported";
    public const string CodeTypeMismatch = "decisions.validation.type_mismatch";
    public const string CodeIncomplete = "decisions.validation.incomplete";
    public const string CodeOverlap = "decisions.validation.overlap";
    public const string CodeUnreachable = "decisions.validation.unreachable";
    public const string CodeOutcomeInvalid = "decisions.validation.outcome_invalid";
    public const string CodeMoneyCurrencyMismatch = "decisions.validation.money_currency_mismatch";

    /// <summary>Stable error code: a condition references a fact name the descriptor does not declare.</summary>
    public const string CodeUnknownFact = "decisions.validation.unknown_fact";

    /// <summary>Stable error code: a condition's operand value is not the DecisionValue case the column's declared FactType expects (e.g. a Decimal bound on an Integer column).</summary>
    public const string CodeCellTypeMismatch = "decisions.validation.cell_type_mismatch";

    /// <summary>Stable error code: two rows of one table (the default row included) share a key, so traces, goldens and unreachability reports could not name a row unambiguously.</summary>
    public const string CodeDuplicateRowKey = "decisions.validation.duplicate_row_key";

    public static ValidationReport Validate(
        DecisionTable table, DecisionKindDescriptor descriptor, IDecisionOutcomeValidator? outcomeValidator = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(descriptor);

        var errors = new List<ValidationIssue>();
        if (table.HitPolicy is not (HitPolicy.Unique or HitPolicy.First or HitPolicy.Collect))
        {
            errors.Add(new ValidationIssue(CodeHitPolicyUnsupported, $"Hit policy {(int)table.HitPolicy} is unsupported.", []));
        }
        var facts = descriptor.Facts.ToDictionary(f => f.Name, StringComparer.Ordinal);
        List<Row> allRows = table.DefaultRow is null ? [.. table.Rows] : [.. table.Rows, table.DefaultRow];

        ValidateRowKeysAreUnique(allRows, errors);
        var structurallySound = ValidateCells(allRows, facts, errors);

        // The overlap, subsumption and coverage analyses presume well-typed cells over declared
        // facts; on a malformed table they would compare incomparable values, so they run only
        // once every cell is sound.
        if (structurallySound)
        {
            ValidateOverlapOrUnreachability(table, descriptor, errors);
            errors.AddRange(CoverageAnalyzer.FindIncompleteness(table, descriptor));
        }

        ValidateOutcomes(allRows, table.DefaultRow, outcomeValidator, errors);

        return new ValidationReport { Errors = errors };
    }

    private static void ValidateRowKeysAreUnique(IEnumerable<Row> rows, List<ValidationIssue> errors)
    {
        foreach (var duplicate in rows.GroupBy(r => r.Key, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add(new ValidationIssue(
                CodeDuplicateRowKey,
                $"Row key '{duplicate.Key}' is used by {duplicate.Count()} rows.",
                [duplicate.Key]));
        }
    }

    /// <summary>Checks every cell; returns false when any cell is structurally unsound (unknown fact, forbidden operator, mistyped or missing operand).</summary>
    private static bool ValidateCells(IEnumerable<Row> rows, Dictionary<string, FactDescriptor> facts, List<ValidationIssue> errors)
    {
        var sound = true;
        foreach (var row in rows)
        {
            foreach (var condition in row.Conditions)
            {
                if (!facts.TryGetValue(condition.FactName, out var fact))
                {
                    sound = false;
                    errors.Add(new ValidationIssue(
                        CodeUnknownFact,
                        $"Row '{row.Key}': fact '{condition.FactName}' is not declared by the kind.",
                        [row.Key]));
                    continue;
                }

                if (!OperatorMatrix.IsAllowed(condition.Operator, fact.Type))
                {
                    sound = false;
                    errors.Add(new ValidationIssue(
                        CodeTypeMismatch,
                        $"Row '{row.Key}': operator '{condition.Operator}' is not valid on fact '{condition.FactName}'.",
                        [row.Key]));
                    continue;
                }

                var operandProblem = OperandProblem(condition, fact.Type);
                if (operandProblem is not null)
                {
                    sound = false;
                    errors.Add(new ValidationIssue(
                        CodeCellTypeMismatch,
                        $"Row '{row.Key}': the condition on '{condition.FactName}' {operandProblem}.",
                        [row.Key]));
                    continue;
                }

                if (fact.Type == FactType.Money && HasMixedCurrencies(condition))
                {
                    errors.Add(new ValidationIssue(
                        CodeMoneyCurrencyMismatch,
                        $"Row '{row.Key}': the condition on '{condition.FactName}' mixes currencies within one row.",
                        [row.Key]));
                }
            }
        }

        return sound;
    }

    /// <summary>Describes what is wrong with the condition's operands for a column of <paramref name="type"/>, or null when they are sound.</summary>
    private static string? OperandProblem(Condition condition, FactType type)
    {
        switch (condition.Operator)
        {
            case Operator.Equals:
            case Operator.NotEquals:
            case Operator.StartsWith:
                return condition.Value is null
                    ? $"has no operand for operator '{condition.Operator}'"
                    : Mistyped(condition.Value, type);
            case Operator.In:
            case Operator.NotIn:
                return condition.Values.Select(v => Mistyped(v, type)).FirstOrDefault(p => p is not null);
            case Operator.Range:
                if (condition.Value is null && condition.UpperValue is null)
                {
                    return "has a Range with no bound";
                }

                var lowerProblem = condition.Value is null ? null : Mistyped(condition.Value, type);
                return lowerProblem ?? (condition.UpperValue is null ? null : Mistyped(condition.UpperValue, type));
            default:
                return null;
        }
    }

    private static string? Mistyped(DecisionValue value, FactType type) =>
        ValueTypes.Matches(type, value)
            ? null
            : $"has a {value.GetType().Name} operand where the column is {type}";

    private static void ValidateOverlapOrUnreachability(DecisionTable table, DecisionKindDescriptor descriptor, List<ValidationIssue> errors)
    {
        if (table.HitPolicy == HitPolicy.Unique)
        {
            for (var i = 0; i < table.Rows.Count; i++)
            {
                for (var j = i + 1; j < table.Rows.Count; j++)
                {
                    if (OverlapAnalyzer.RowsOverlap(table.Rows[i], table.Rows[j], descriptor))
                    {
                        errors.Add(new ValidationIssue(
                            CodeOverlap,
                            $"Rows '{table.Rows[i].Key}' and '{table.Rows[j].Key}' overlap under HitPolicy.Unique.",
                            [table.Rows[i].Key, table.Rows[j].Key]));
                    }
                }
            }

            return;
        }

        if (table.HitPolicy == HitPolicy.Collect)
        {
            ValidateCollectDuplicates(table, errors);
            return;
        }

        if (table.HitPolicy != HitPolicy.First)
        {
            return;
        }

        var unreachable = new List<string>();
        for (var i = 1; i < table.Rows.Count; i++)
        {
            for (var j = 0; j < i; j++)
            {
                if (OverlapAnalyzer.IsSubsumedByEarlierRow(table.Rows[i], table.Rows[j]))
                {
                    unreachable.Add(table.Rows[i].Key);
                    break;
                }
            }
        }

        unreachable.AddRange(CoverageAnalyzer.FindUnreachableRowsByEnumeration(table, descriptor));

        foreach (var key in unreachable.Distinct(StringComparer.Ordinal))
        {
            errors.Add(new ValidationIssue(CodeUnreachable, $"Row '{key}' is unreachable under HitPolicy.First.", [key]));
        }
    }

    private static void ValidateCollectDuplicates(DecisionTable table, List<ValidationIssue> errors)
    {
        var outcomes = table.Rows.Select(row => CanonicalCollectOutcome(row, errors)).ToList();
        if (table.DefaultRow is not null)
        {
            _ = CanonicalCollectOutcome(table.DefaultRow, errors);
        }
        for (var i = 1; i < table.Rows.Count; i++)
        {
            for (var j = 0; j < i; j++)
            {
                if (outcomes[i] is not null && string.Equals(outcomes[i], outcomes[j], StringComparison.Ordinal)
                    && SameConditionSet(table.Rows[i].Conditions, table.Rows[j].Conditions))
                {
                    errors.Add(new ValidationIssue(CodeUnreachable, $"Row '{table.Rows[i].Key}' duplicates an earlier condition set and outcome under HitPolicy.Collect.", [table.Rows[i].Key]));
                    break;
                }
            }
        }
    }

    private static string? CanonicalCollectOutcome(Row row, List<ValidationIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(row.Outcome))
        {
            return InvalidCollectOutcome(row, errors);
        }
        try
        {
            using var document = JsonDocument.Parse(row.Outcome);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? CanonicalOutcomeJson.Canonicalize(row.Outcome)
                : InvalidCollectOutcome(row, errors);
        }
        catch (JsonException)
        {
            return InvalidCollectOutcome(row, errors);
        }
    }

    private static string? InvalidCollectOutcome(Row row, List<ValidationIssue> errors)
    {
        errors.Add(new ValidationIssue(CodeOutcomeInvalid, $"Row '{row.Key}' must have a valid JSON object outcome.", [row.Key]));
        return null;
    }

    private static bool SameConditionSet(IReadOnlyList<Condition> left, IReadOnlyList<Condition> right) =>
        left.All(a => right.Any(b => SameCondition(a, b))) && right.All(b => left.Any(a => SameCondition(a, b)));

    private static bool SameCondition(Condition left, Condition right) =>
        string.Equals(left.FactName, right.FactName, StringComparison.Ordinal)
        && left.Operator == right.Operator
        && left.Value == right.Value && left.UpperValue == right.UpperValue
        && left.LowerInclusive == right.LowerInclusive && left.UpperInclusive == right.UpperInclusive
        && left.Values.ToHashSet().SetEquals(right.Values);

    private static void ValidateOutcomes(
        IEnumerable<Row> rows, Row? defaultRow, IDecisionOutcomeValidator? outcomeValidator, List<ValidationIssue> errors)
    {
        if (outcomeValidator is null)
        {
            return;
        }

        var rowValidator = outcomeValidator as IDecisionRowValidator;
        foreach (var row in rows)
        {
            errors.AddRange(outcomeValidator.Validate(row.Key, row.Outcome));
            if (rowValidator is not null)
            {
                // R-A7: default conditions never restrict applicability, even when authored with a misleading condition.
                var validationRow = ReferenceEquals(row, defaultRow) ? row with { Conditions = [] } : row;
                errors.AddRange(rowValidator.ValidateRow(validationRow));
            }
        }
    }

    private static bool HasMixedCurrencies(Condition condition)
    {
        var currencies = new HashSet<string>(StringComparer.Ordinal);
        CollectCurrency(condition.Value, currencies);
        CollectCurrency(condition.UpperValue, currencies);
        foreach (var value in condition.Values)
        {
            CollectCurrency(value, currencies);
        }

        return currencies.Count > 1;
    }

    private static void CollectCurrency(DecisionValue? value, HashSet<string> currencies)
    {
        if (value is MoneyValue money)
        {
            currencies.Add(money.Currency);
        }
    }
}

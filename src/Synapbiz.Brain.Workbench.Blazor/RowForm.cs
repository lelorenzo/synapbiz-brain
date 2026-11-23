using System.Globalization;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>What one fact line of a row form holds: the wording of the operator and the texts of its operands.</summary>
internal sealed class FactLine
{
    /// <summary>The id of the operator wording; see <see cref="RowForm.Wordings"/>.</summary>
    internal string Wording { get; set; } = RowForm.AnyWording;

    /// <summary>The single value, or the lower bound.</summary>
    internal string Value { get; set; } = string.Empty;

    /// <summary>The upper bound.</summary>
    internal string Upper { get; set; } = string.Empty;

    /// <summary>The currency of a money fact.</summary>
    internal string Currency { get; set; } = string.Empty;

    /// <summary>Whether the lower bound is inclusive; only a range read from the rule can have it otherwise than its wording says.</summary>
    internal bool LowerInclusive { get; set; } = true;

    /// <summary>Whether the upper bound is inclusive.</summary>
    internal bool UpperInclusive { get; set; } = true;

    /// <summary>The members of a set: the raw values of an enum fact, or the chips of any other fact.</summary>
    internal List<string> Items { get; } = [];

    /// <summary>The text typed in the chip input, not yet added.</summary>
    internal string ChipText { get; set; } = string.Empty;
}

/// <summary>A problem a form found in one of its own fields before asking the loader: what it is and the error code of the value when it is one.</summary>
/// <param name="Kind">The kind: <c>missing</c>, <c>reversed</c>, <c>value</c> or <c>rows</c>.</param>
/// <param name="Code">The <see cref="FactInput"/> code of a <c>value</c> problem.</param>
internal sealed record FieldProblem(string Kind, string? Code);

/// <summary>The edits of a form and what is wrong with its fields.</summary>
/// <param name="Edits">The edits, in order.</param>
/// <param name="Problems">The problem of each field by field key (<c>fact:name</c> or <c>out:name</c>); empty when the form can be applied.</param>
internal sealed record FormCompiled(IReadOnlyList<RuleEdit> Edits, IReadOnlyDictionary<string, FieldProblem> Problems);

/// <summary>The row form of the draft view: the lines of the conditions, the outcome and the name of one row, as the person edits them.</summary>
internal sealed class RowForm
{
    /// <summary>The id of the wording "any value".</summary>
    internal const string AnyWording = "any";

    /// <summary>The wordings in the order the form lists them, each with the operator it needs.</summary>
    internal static readonly (string Id, Operator Operator)[] Wordings =
    [
        (AnyWording, Operator.Any),
        ("eq", Operator.Equals),
        ("neq", Operator.NotEquals),
        ("in", Operator.In),
        ("nin", Operator.NotIn),
        ("between", Operator.Range),
        ("min", Operator.Range),
        ("gt", Operator.Range),
        ("max", Operator.Range),
        ("lt", Operator.Range),
        ("starts", Operator.StartsWith),
        ("notnull", Operator.IsNotNull),
        ("isnull", Operator.IsNull),
    ];

    private static readonly string[] SetWordings = ["in", "nin"];
    private static readonly string[] BoundWordings = ["between", "min", "gt", "max", "lt"];
    private static readonly string[] NoValueWordings = [AnyWording, "notnull", "isnull"];

    internal RowForm(string? rowKey, bool isDefault, string newKey, string? afterKey, string trigger)
    {
        RowKey = rowKey;
        IsDefault = isDefault;
        NewKey = newKey;
        AfterKey = afterKey;
        Trigger = trigger;
    }

    /// <summary>The key of the row edited; null for a new row.</summary>
    internal string? RowKey { get; }

    /// <summary>True when the form is of the default row.</summary>
    internal bool IsDefault { get; }

    /// <summary>The key a new row gets.</summary>
    internal string NewKey { get; }

    /// <summary>The row a new row goes after; null puts it first.</summary>
    internal string? AfterKey { get; }

    /// <summary>The ref key of the control that has the focus back when the form is cancelled.</summary>
    internal string Trigger { get; }

    /// <summary>True for a new row.</summary>
    internal bool IsNew => RowKey is null && !IsDefault;

    /// <summary>The key of the row the form's steps are about.</summary>
    internal string StepKey => RowKey ?? NewKey;

    /// <summary>The number the row has, for the title and for the sentence about another open form; null for a new row and the default row.</summary>
    internal int? Number { get; init; }

    /// <summary>The lines of the conditions by fact name.</summary>
    internal Dictionary<string, FactLine> Lines { get; } = new(StringComparer.Ordinal);

    /// <summary>The facts the row uses when the form opens; the others are folded.</summary>
    internal HashSet<string> Used { get; } = new(StringComparer.Ordinal);

    /// <summary>The texts of the outcome fields by field name.</summary>
    internal Dictionary<string, string> Outcome { get; } = new(StringComparer.Ordinal);

    /// <summary>The currencies of the money outcome fields by field name.</summary>
    internal Dictionary<string, string> OutcomeCurrency { get; } = new(StringComparer.Ordinal);

    /// <summary>The name of the row.</summary>
    internal string Name { get; set; } = string.Empty;

    /// <summary>The name the row had when the form opened.</summary>
    internal string InitialName { get; set; } = string.Empty;

    /// <summary>True once the person changed anything.</summary>
    internal bool Touched { get; set; }

    /// <summary>True when the folded facts are shown.</summary>
    internal bool ShowUnused { get; set; }

    /// <summary>The problems of the fields found when the form was applied.</summary>
    internal Dictionary<string, FieldProblem> Problems { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The fields the loader refused, by field key, with no sentence of their own.</summary>
    internal HashSet<string> Marked { get; } = new(StringComparer.Ordinal);

    /// <summary>The refusal of the loader or of the validator, shown inside the form; null when there is none.</summary>
    internal DraftRefusal? Refusal { get; set; }

    /// <summary>True when every reason of the refusal is one the person cannot cause.</summary>
    internal bool RefusalIsInternal { get; set; }

    /// <summary>The sentence under the form when another row was asked to open; empty when none was.</summary>
    internal string Blocked { get; set; } = string.Empty;

    /// <summary>The sentence of the row as the form reads now.</summary>
    internal string Preview { get; set; } = string.Empty;

    /// <summary>The warnings of the form, as sentences.</summary>
    internal List<string> Warnings { get; } = [];

    /// <summary>The wording the form shows for a fact; used to name the operator in a refusal.</summary>
    internal IReadOnlyDictionary<string, string> Chosen(Func<string, string> words) =>
        Lines.Where(pair => pair.Value.Wording != AnyWording).ToDictionary(pair => pair.Key, pair => words(pair.Value.Wording), StringComparer.Ordinal);

    /// <summary>The wordings a fact may use, in the order of the design; only the operators the library allows for the fact.</summary>
    internal static List<string> WordingsFor(FactChoices fact) =>
        [.. Wordings.Where(wording => fact.Operators.Contains(wording.Operator.ToString(), StringComparer.Ordinal)).Select(wording => wording.Id)];

    /// <summary>True for the wordings that take a set.</summary>
    internal static bool IsSet(string wording) => SetWordings.Contains(wording, StringComparer.Ordinal);

    /// <summary>True for the wordings that take a bound.</summary>
    internal static bool IsBound(string wording) => BoundWordings.Contains(wording, StringComparer.Ordinal);

    /// <summary>True for the wordings that take no value.</summary>
    internal static bool TakesNoValue(string wording) => NoValueWordings.Contains(wording, StringComparer.Ordinal);

    /// <summary>The line for a condition the row has.</summary>
    internal static FactLine LineOf(Condition condition)
    {
        var line = new FactLine { Wording = WordingOf(condition) };
        switch (condition.Operator)
        {
            case Operator.Equals or Operator.NotEquals or Operator.StartsWith:
                Put(line, condition.Value, upper: false);
                break;
            case Operator.In or Operator.NotIn:
                foreach (var value in condition.Values)
                {
                    line.Items.Add(FactForm.Text(value));
                }

                break;
            case Operator.Range:
                Put(line, condition.Value ?? condition.UpperValue, upper: false);
                if (condition.Value is not null)
                {
                    Put(line, condition.UpperValue, upper: true);
                }

                line.LowerInclusive = condition.LowerInclusive;
                line.UpperInclusive = condition.UpperInclusive;
                break;
            default:
                break;
        }

        return line;
    }

    private static string WordingOf(Condition condition) => condition.Operator switch
    {
        Operator.Equals => "eq",
        Operator.NotEquals => "neq",
        Operator.In => "in",
        Operator.NotIn => "nin",
        Operator.IsNull => "isnull",
        Operator.IsNotNull => "notnull",
        Operator.StartsWith => "starts",
        Operator.Range => RangeWording(condition),
        _ => AnyWording,
    };

    private static string RangeWording(Condition condition)
    {
        if (condition.Value is not null && condition.UpperValue is not null)
        {
            return "between";
        }

        if (condition.Value is not null)
        {
            return condition.LowerInclusive ? "min" : "gt";
        }

        return condition.UpperInclusive ? "max" : "lt";
    }

    private static void Put(FactLine line, DecisionValue? value, bool upper)
    {
        if (value is null)
        {
            return;
        }

        var text = value is MoneyValue money ? money.Amount.ToString(CultureInfo.InvariantCulture) : FactForm.Text(value);
        if (value is MoneyValue priced)
        {
            line.Currency = priced.Currency;
        }

        if (upper)
        {
            line.Upper = text;
        }
        else
        {
            line.Value = text;
        }
    }

    /// <summary>The line of a fact: the one the form has, or a fresh one with the fact's default currency.</summary>
    internal FactLine LineFor(string fact)
    {
        if (!Lines.TryGetValue(fact, out var line))
        {
            line = new FactLine();
            Lines[fact] = line;
        }

        return line;
    }

    /// <summary>
    /// Reads the form. Every field is read with <see cref="FactInput"/>, the same reading the simulator uses; the edits are those
    /// that bring the row to what the form says. A problem names the field it is in and nothing is built for a field with a problem.
    /// </summary>
    /// <param name="rule">The rule the form edits.</param>
    /// <param name="shown">The facts the form shows.</param>
    /// <param name="locale">The language of a row name.</param>
    /// <param name="hasLabel">True when the row's name is in the rule's own labels, so that clearing it is an edit.</param>
    internal FormCompiled Compile(BrainRule rule, IReadOnlyList<ShownFact> shown, WorkbenchLocale locale, bool hasLabel)
    {
        var problems = new Dictionary<string, FieldProblem>(StringComparer.Ordinal);
        var conditions = new List<(string Fact, Condition? Condition)>();
        if (!IsDefault)
        {
            foreach (var fact in shown.Select(item => item.Fact))
            {
                var line = Lines.GetValueOrDefault(fact.Name) ?? new FactLine();
                var read = ReadLine(fact, line);
                if (read.Problem is not null)
                {
                    problems["fact:" + fact.Name] = read.Problem;
                }

                conditions.Add((fact.Name, read.Condition));
            }
        }

        var outcome = new Dictionary<string, DecisionValue?>(StringComparer.Ordinal);
        foreach (var field in rule.Kind.OutcomeFields)
        {
            var typed = ReadOutcome(field);
            if (typed.Problem is not null)
            {
                problems["out:" + field.Name] = typed.Problem;
            }

            outcome[field.Name] = typed.Value;
        }

        if (problems.Count > 0)
        {
            return new FormCompiled([], problems);
        }

        return new FormCompiled(EditsOf(rule, conditions, outcome, locale, hasLabel), problems);
    }

    private List<RuleEdit> EditsOf(
        BrainRule rule, List<(string Fact, Condition? Condition)> conditions, Dictionary<string, DecisionValue?> outcome, WorkbenchLocale locale, bool hasLabel)
    {
        var edits = new List<RuleEdit>();
        var name = Name.Trim();
        if (IsDefault)
        {
            edits.AddRange(outcome.Select(pair => new SetDefaultOutcome(pair.Key, pair.Value)));
            return edits;
        }

        if (IsNew)
        {
            edits.Add(new AddRow(
                NewKey,
                AfterKey,
                [.. conditions.Where(item => item.Condition is not null).Select(item => item.Condition!)],
                outcome.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal)));
            if (name.Length > 0)
            {
                edits.Add(new SetRowLabel(NewKey, locale, name));
            }

            return edits;
        }

        var existing = rule.Table.Rows.First(row => string.Equals(row.Key, RowKey, StringComparison.Ordinal)).Conditions;
        foreach (var (fact, condition) in conditions)
        {
            var had = existing.Any(item => item.Operator != Operator.Any && string.Equals(item.FactName, fact, StringComparison.Ordinal));
            if (condition is not null)
            {
                edits.Add(new SetCondition(RowKey!, condition));
            }
            else if (had)
            {
                edits.Add(new ClearCondition(RowKey!, fact));
            }
        }

        edits.AddRange(outcome.Select(pair => new SetOutcome(RowKey!, pair.Key, pair.Value)));
        if (!string.Equals(name, InitialName, StringComparison.Ordinal) && (name.Length > 0 || hasLabel))
        {
            edits.Add(new SetRowLabel(RowKey!, locale, name.Length > 0 ? name : null));
        }

        return edits;
    }

    private (Condition? Condition, FieldProblem? Problem) ReadLine(FactDescriptor fact, FactLine line)
    {
        var name = fact.Name;
        var wording = line.Wording;
        if (wording == AnyWording)
        {
            return (null, null);
        }

        if (wording == "notnull")
        {
            return (Condition.IsNotNull(name), null);
        }

        if (wording == "isnull")
        {
            return (Condition.IsNull(name), null);
        }

        if (IsSet(wording))
        {
            return ReadSet(fact, line);
        }

        if (wording == "starts")
        {
            return string.IsNullOrWhiteSpace(line.Value) ? (null, new FieldProblem("missing", null)) : (Condition.StartsWith(name, line.Value.Trim()), null);
        }

        var strict = fact with { Nullable = false };
        if (IsBound(wording))
        {
            return ReadBounds(strict, line);
        }

        var single = ReadValue(strict, line.Value, line.Currency);
        if (single.Problem is not null)
        {
            return (null, single.Problem);
        }

        return (wording == "eq" ? Condition.Equals(name, single.Value!) : Condition.NotEquals(name, single.Value!), null);
    }

    private static (Condition? Condition, FieldProblem? Problem) ReadSet(FactDescriptor fact, FactLine line)
    {
        if (line.Items.Count == 0)
        {
            return (null, new FieldProblem("missing", null));
        }

        var strict = fact with { Nullable = false };
        var values = new List<DecisionValue>();
        foreach (var item in line.Items)
        {
            var read = FactInput.Parse(strict, item);
            if (!read.IsValid)
            {
                return (null, new FieldProblem("value", read.ErrorCode));
            }

            values.Add(read.Value!);
        }

        return (line.Wording == "in" ? Condition.In(fact.Name, values) : Condition.NotIn(fact.Name, values), null);
    }

    private static (Condition? Condition, FieldProblem? Problem) ReadBounds(FactDescriptor fact, FactLine line)
    {
        var wording = line.Wording;
        var needsLower = wording is "between" or "min" or "gt";
        var needsUpper = wording is "between" or "max" or "lt";
        DecisionValue? lower = null;
        DecisionValue? upper = null;
        if (needsLower)
        {
            var read = ReadValue(fact, line.Value, line.Currency);
            if (read.Problem is not null)
            {
                return (null, read.Problem);
            }

            lower = read.Value;
        }

        if (needsUpper)
        {
            var read = ReadValue(fact, wording == "between" ? line.Upper : line.Value, line.Currency);
            if (read.Problem is not null)
            {
                return (null, read.Problem);
            }

            upper = read.Value;
        }

        if (lower is not null && upper is not null && Compare(lower, upper) > 0)
        {
            return (null, new FieldProblem("reversed", null));
        }

        var keepsFlags = wording == "between";
        return (Condition.Range(fact.Name, lower, upper, keepsFlags ? line.LowerInclusive : wording != "gt", keepsFlags ? line.UpperInclusive : wording != "lt"), null);
    }

    private (DecisionValue? Value, FieldProblem? Problem) ReadOutcome(OutcomeFieldDescriptor field)
    {
        Outcome.TryGetValue(field.Name, out var text);
        var descriptor = new FactDescriptor(field.Name, field.Type, field.Nullable);
        var read = ReadValue(descriptor, text ?? string.Empty, OutcomeCurrency.GetValueOrDefault(field.Name, string.Empty), allowEmpty: field.Nullable);
        return (read.Value, read.Problem);
    }

    private static (DecisionValue? Value, FieldProblem? Problem) ReadValue(FactDescriptor fact, string text, string currency, bool allowEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return allowEmpty ? (null, null) : (null, new FieldProblem("missing", null));
        }

        var unit = currency.Length > 0 ? currency : FactForm.DefaultCurrency;
        var typed = fact.Type == FactType.Money ? text.Trim() + " " + unit : text;
        var read = FactInput.Parse(fact with { Nullable = false }, typed);
        return read.IsValid ? (read.Value, null) : (null, new FieldProblem("value", read.ErrorCode));
    }

    /// <summary>Orders two bounds of one kind; zero when they cannot be told apart (another kind, or two currencies).</summary>
    private static int Compare(DecisionValue first, DecisionValue second) => (first, second) switch
    {
        (IntegerValue a, IntegerValue b) => a.Value.CompareTo(b.Value),
        (DecimalValue a, DecimalValue b) => a.Value.CompareTo(b.Value),
        (DateValue a, DateValue b) => a.Value.CompareTo(b.Value),
        (MoneyValue a, MoneyValue b) when string.Equals(a.Currency, b.Currency, StringComparison.Ordinal) => a.Amount.CompareTo(b.Amount),
        _ => 0,
    };
}

using System.Globalization;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>A fact the form shows: the descriptor and its label.</summary>
internal sealed record ShownFact(int Index, FactDescriptor Fact, FactView View);

/// <summary>The texts typed into the fact fields of one form, and the errors found when they were read.</summary>
internal sealed class FactForm
{
    private readonly Dictionary<string, string> texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> currencies = new(StringComparer.Ordinal);

    /// <summary>The error code of each invalid fact by fact name.</summary>
    internal Dictionary<string, string> Errors { get; } = new(StringComparer.Ordinal);

    /// <summary>The facts a person types: not composed, not a reference.</summary>
    internal static List<ShownFact> Shown(BrainRule rule, RuleView view) =>
        [.. rule.Kind.Facts
            .Select((fact, index) => new ShownFact(index, fact, view.Facts[index]))
            .Where(shown => shown.Fact.ComposedFromKind is null && shown.Fact.Type != FactType.Reference)];

    internal string TextOf(string name) => texts.GetValueOrDefault(name, string.Empty);

    internal string CurrencyOf(string name) => currencies.GetValueOrDefault(name, string.Empty);

    internal void SetText(string name, string? text) => texts[name] = text ?? string.Empty;

    internal void SetCurrency(string name, string? text) => currencies[name] = text ?? string.Empty;

    /// <summary>Empties every field; a required enum with a list of values goes back to its first value, as the select shows it.</summary>
    internal void Reset(IReadOnlyList<ShownFact> shown)
    {
        texts.Clear();
        currencies.Clear();
        Errors.Clear();
        EnsureDefaults(shown);
    }

    /// <summary>Gives a required enum that has no text yet its first value.</summary>
    internal void EnsureDefaults(IReadOnlyList<ShownFact> shown)
    {
        foreach (var item in shown)
        {
            if (item.Fact.Type == FactType.Enum && !item.Fact.Nullable && item.View.AllowedValues.Count > 0
                && !texts.ContainsKey(item.Fact.Name))
            {
                texts[item.Fact.Name] = item.View.AllowedValues[0].Value;
            }
        }
    }

    /// <summary>Fills the fields from the values of a case; a fact the case does not carry is left empty.</summary>
    internal void Load(IReadOnlyList<ShownFact> shown, IReadOnlyDictionary<string, DecisionValue> facts)
    {
        Reset(shown);
        foreach (var fact in shown.Select(item => item.Fact))
        {
            if (!facts.TryGetValue(fact.Name, out var value))
            {
                if (fact.Type == FactType.Enum && fact.Nullable)
                {
                    texts[fact.Name] = string.Empty;
                }

                continue;
            }

            if (value is MoneyValue money)
            {
                texts[fact.Name] = money.Amount.ToString(CultureInfo.InvariantCulture);
                currencies[fact.Name] = money.Currency;
            }
            else
            {
                texts[fact.Name] = Text(value);
            }
        }
    }

    /// <summary>Reads every field with <see cref="FactInput.ParseAll"/>; errors of facts the form does not show are not reported.</summary>
    internal FactSetInputResult Parse(BrainRule rule, IReadOnlyList<ShownFact> shown)
    {
        var typed = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var fact in shown.Select(item => item.Fact))
        {
            typed[fact.Name] = fact.Type == FactType.Money ? JoinMoney(fact.Name) : TextOf(fact.Name);
        }

        var result = FactInput.ParseAll(rule.Kind, typed);
        var visible = shown.Select(item => item.Fact.Name).ToHashSet(StringComparer.Ordinal);
        var errors = result.Errors.Where(error => visible.Contains(error.Key)).ToDictionary(error => error.Key, error => error.Value, StringComparer.Ordinal);
        return new FactSetInputResult(result.Facts, errors);
    }

    /// <summary>The text of a typed value as <see cref="FactInput"/> reads it back.</summary>
    internal static string Text(DecisionValue value) => value switch
    {
        CodeValue code => code.Value,
        EnumValue member => member.Value,
        BooleanValue flag => flag.Value ? "true" : "false",
        IntegerValue integer => integer.Value.ToString(CultureInfo.InvariantCulture),
        DecimalValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        DateValue date => date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        MoneyValue money => money.Amount.ToString(CultureInfo.InvariantCulture) + " " + money.Currency,
        ReferenceValue reference => reference.Value.ToString("D", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    private string JoinMoney(string name)
    {
        var amount = TextOf(name).Trim();
        var currency = CurrencyOf(name).Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(amount) && string.IsNullOrEmpty(currency) ? string.Empty : (amount + " " + currency).Trim();
    }
}

using System.Globalization;
using System.Text;
using Nuvora.Decisions.Contracts.Tables;

namespace Synapbiz.Brain.Workbench;

/// <summary>Plain-language names and value texts shared by every part of the workbench.</summary>
public static class WorkbenchText
{
    /// <summary>
    /// A readable name for an identifier: <c>_</c>, <c>-</c> and <c>.</c> become a space, and an upper-case letter that follows
    /// a lower-case letter or a digit starts a new lower-case word. <c>hasInsuranceOnFile</c> gives <c>has insurance on file</c>.
    /// </summary>
    /// <param name="name">The identifier.</param>
    public static string Humanize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var text = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (character is '_' or '-' or '.')
            {
                text.Append(' ');
            }
            else if (char.IsUpper(character) && index > 0 && (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1])))
            {
                text.Append(' ').Append(char.ToLowerInvariant(character));
            }
            else
            {
                text.Append(character);
            }
        }

        return text.ToString().Trim();
    }

    /// <summary>
    /// The text of a typed value for a person. A null value is an empty string. A code or enum member that has a value label is
    /// written by its label. Otherwise the host's <see cref="RuleLabels.ValueFormatter"/> is asked and its text is used when it
    /// returns one. Otherwise the default text: a code as written, an enum member humanised, a boolean as yes or no, numbers with
    /// the locale's decimal separator, a date in the locale's order and a money amount with its currency.
    /// </summary>
    /// <param name="value">The value; null gives an empty string.</param>
    /// <param name="name">The fact or outcome field the value belongs to, the key of its value labels.</param>
    /// <param name="locale">The locale of the text.</param>
    /// <param name="labels">Labels to use; none when null.</param>
    public static string FormatValue(DecisionValue? value, string name, WorkbenchLocale locale, RuleLabels? labels = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (value is null)
        {
            return string.Empty;
        }

        var label = value switch
        {
            CodeValue code => labels?.ValueLabel(name, code.Value),
            EnumValue member => labels?.ValueLabel(name, member.Value),
            _ => null,
        };

        return label ?? labels?.ValueFormatter?.Invoke(value) ?? DefaultText(value, locale);
    }

    private static string DefaultText(DecisionValue value, WorkbenchLocale locale) => value switch
    {
        CodeValue code => code.Value,
        EnumValue member => Humanize(member.Value),
        BooleanValue flag => SentenceTemplates.Text(locale, flag.Value ? "value.true" : "value.false"),
        IntegerValue integer => integer.Value.ToString(CultureInfo.InvariantCulture),
        DecimalValue number => Number(number.Value, locale),
        MoneyValue money => SentenceTemplates.Text(locale, "value.money", ("amount", Number(money.Amount, locale)), ("currency", money.Currency)),
        DateValue date => date.Value.ToString(locale == WorkbenchLocale.Es ? "dd/MM/yyyy" : "MM/dd/yyyy", CultureInfo.InvariantCulture),
        ReferenceValue reference => reference.Value.ToString("D", CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown value type."),
    };

    private static string Number(decimal number, WorkbenchLocale locale)
    {
        var text = number.ToString(CultureInfo.InvariantCulture);
        return locale == WorkbenchLocale.Es ? text.Replace('.', ',') : text;
    }
}

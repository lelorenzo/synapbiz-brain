using System.Text.RegularExpressions;

namespace Synapbiz.Brain.Workbench;

/// <summary>
/// The sentence and value templates of the workbench, English and Spanish. The tables started as the <c>en</c> and <c>es</c>
/// tables of the source repository's sentence generator but are no longer a verbatim copy: the workbench words the set and
/// emptiness operators more plainly and joins every list with a final joiner and no comma before it (see <see cref="Join"/>).
/// </summary>
internal static partial class SentenceTemplates
{
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["sentence.if"] = "If {conditions}, then {outcome}.",
        ["sentence.always"] = "In every case, {outcome}.",
        ["sentence.default"] = "Otherwise, {outcome}.",
        ["sentence.and"] = " and ",
        ["sentence.or"] = " or ",
        ["sentence.nor"] = " or ",
        ["sentence.list"] = ", ",
        ["operator.any"] = "{fact} can be anything",
        ["operator.equals"] = "{fact} is {value}",
        ["operator.notEquals"] = "{fact} is not {value}",
        ["operator.in"] = "{fact} is {values}",
        ["operator.notIn"] = "{fact} is not {values}",
        ["operator.isNull"] = "{fact} has no value",
        ["operator.isNotNull"] = "{fact} has a value",
        ["operator.range"] = "{fact} is between {lower} and {upper}",
        ["operator.rangeBounds"] = "{fact} is {bounds}",
        ["operator.startsWith"] = "{fact} starts with {value}",
        ["range.atLeast"] = "at least {value}",
        ["range.greaterThan"] = "greater than {value}",
        ["range.atMost"] = "at most {value}",
        ["range.lessThan"] = "less than {value}",
        ["outcome.field"] = "{field} is {value}",
        ["outcome.none"] = "no outcome fields are set",
        ["value.true"] = "yes",
        ["value.false"] = "no",
        ["value.money"] = "{amount} {currency}",
    };

    private static readonly Dictionary<string, string> Spanish = new(StringComparer.Ordinal)
    {
        ["sentence.if"] = "Si {conditions}, entonces {outcome}.",
        ["sentence.always"] = "En todos los casos, {outcome}.",
        ["sentence.default"] = "En cualquier otro caso, {outcome}.",
        ["sentence.and"] = " y ",
        ["sentence.or"] = " o ",
        ["sentence.nor"] = " ni ",
        ["sentence.list"] = ", ",
        ["operator.any"] = "{fact} puede ser cualquier valor",
        ["operator.equals"] = "{fact} es {value}",
        ["operator.notEquals"] = "{fact} no es {value}",
        ["operator.in"] = "{fact} es {values}",
        ["operator.notIn"] = "{fact} no es {values}",
        ["operator.isNull"] = "{fact} no tiene valor",
        ["operator.isNotNull"] = "{fact} tiene valor",
        ["operator.range"] = "{fact} está entre {lower} y {upper}",
        ["operator.rangeBounds"] = "{fact} es {bounds}",
        ["operator.startsWith"] = "{fact} empieza por {value}",
        ["range.atLeast"] = "como mínimo {value}",
        ["range.greaterThan"] = "mayor que {value}",
        ["range.atMost"] = "como máximo {value}",
        ["range.lessThan"] = "menor que {value}",
        ["outcome.field"] = "{field} es {value}",
        ["outcome.none"] = "no hay campos de resultado",
        ["value.true"] = "sí",
        ["value.false"] = "no",
        ["value.money"] = "{amount} {currency}",
    };

    /// <summary>The template of <paramref name="key"/> in the locale, with its <c>{name}</c> placeholders filled from <paramref name="parameters"/>.</summary>
    internal static string Text(WorkbenchLocale locale, string key, params (string Name, string Value)[] parameters)
    {
        var template = Table(locale)[key];
        if (parameters.Length == 0)
        {
            return template;
        }

        var values = parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.Ordinal);
        return Placeholder().Replace(template, match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }

    /// <summary>
    /// Joins list items: one item as it is; two items with the final joiner; three or more with the list separator between all
    /// but the last two and the final joiner before the last, never a comma before the final joiner.
    /// </summary>
    /// <param name="locale">The locale of the joiners.</param>
    /// <param name="joinerKey">The template key of the final joiner, <c>sentence.and</c>, <c>sentence.or</c> or <c>sentence.nor</c>.</param>
    /// <param name="items">The items, in order.</param>
    internal static string Join(WorkbenchLocale locale, string joinerKey, IReadOnlyList<string> items)
    {
        if (items.Count <= 1)
        {
            return string.Concat(items);
        }

        var head = string.Join(Text(locale, "sentence.list"), items.Take(items.Count - 1));
        return head + Text(locale, joinerKey) + items[^1];
    }

    private static Dictionary<string, string> Table(WorkbenchLocale locale) => locale switch
    {
        WorkbenchLocale.En => English,
        WorkbenchLocale.Es => Spanish,
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, "Unknown locale."),
    };

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}

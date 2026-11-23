using System.Text.RegularExpressions;

namespace Synapbiz.Brain.Workbench;

/// <summary>
/// The sentence and value templates of the workbench, English and Spanish. The tables are the <c>en</c> and <c>es</c>
/// tables of the source repository's sentence generator, without the keys the workbench does not use.
/// </summary>
internal static partial class SentenceTemplates
{
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["sentence.if"] = "If {conditions}, then {outcome}.",
        ["sentence.always"] = "In every case, {outcome}.",
        ["sentence.default"] = "Otherwise, {outcome}.",
        ["sentence.and"] = " and ",
        ["sentence.list"] = ", ",
        ["operator.any"] = "{fact} can be anything",
        ["operator.equals"] = "{fact} is {value}",
        ["operator.notEquals"] = "{fact} is not {value}",
        ["operator.in"] = "{fact} is one of {values}",
        ["operator.notIn"] = "{fact} is none of {values}",
        ["operator.isNull"] = "{fact} is empty",
        ["operator.isNotNull"] = "{fact} is not empty",
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
        ["sentence.list"] = ", ",
        ["operator.any"] = "{fact} puede ser cualquier valor",
        ["operator.equals"] = "{fact} es {value}",
        ["operator.notEquals"] = "{fact} no es {value}",
        ["operator.in"] = "{fact} es uno de {values}",
        ["operator.notIn"] = "{fact} no es ninguno de {values}",
        ["operator.isNull"] = "{fact} está vacío",
        ["operator.isNotNull"] = "{fact} no está vacío",
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

    private static Dictionary<string, string> Table(WorkbenchLocale locale) => locale switch
    {
        WorkbenchLocale.En => English,
        WorkbenchLocale.Es => Spanish,
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, "Unknown locale."),
    };

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}

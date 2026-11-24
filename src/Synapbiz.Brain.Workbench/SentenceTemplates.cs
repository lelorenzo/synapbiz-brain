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
        ["change.summary"] = "This draft {changes}, {adds} and {removes}.",
        ["change.none"] = "This draft does not change the rule.",
        ["change.changes.zero"] = "changes no rows",
        ["change.changes.one"] = "changes 1 row",
        ["change.changes.many"] = "changes {n} rows",
        ["change.adds.zero"] = "adds none",
        ["change.adds.one"] = "adds 1",
        ["change.adds.many"] = "adds {n}",
        ["change.removes.zero"] = "removes none",
        ["change.removes.one"] = "removes 1",
        ["change.removes.many"] = "removes {n}",
        ["change.moves.one"] = "Moves 1 row.",
        ["change.moves.many"] = "Moves {n} rows.",
        ["change.hitPolicy"] = "The hit policy changes.",
        ["change.noResult"] = "no result",
        ["claims.relation.implements"] = "implements",
        ["claims.relation.adapts"] = "adapts",
        ["claims.relation.informed_by"] = "is informed by",
        ["claims.source.added"] = "The rule now cites the source {source}, version {version}, which it {relation}.",
        ["claims.source.removed"] = "The rule no longer cites the source {source}.",
        ["claims.source.version"] = "The rule follows the source {source} in version {now} instead of version {was}.",
        ["claims.source.relation"] = "The rule {now} the source {source} where it used to {was}.",
        ["claims.source.differences"] = "The differences from the source {source} changed.",
        ["claims.authority.set"] = "The rule now states its authority: {authority}.",
        ["claims.authority.cleared"] = "The rule no longer states its authority ({authority}).",
        ["claims.authority.changed"] = "The authority of the rule changed from {was} to {now}.",
        ["claims.other"] = "What the rule says about its sources changed.",
        ["change.outcomes"] = "; ",
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
        ["change.summary"] = "Este borrador {changes}, {adds} y {removes}.",
        ["change.none"] = "Este borrador no cambia la regla.",
        ["change.changes.zero"] = "no cambia ninguna fila",
        ["change.changes.one"] = "cambia 1 fila",
        ["change.changes.many"] = "cambia {n} filas",
        ["change.adds.zero"] = "no agrega ninguna",
        ["change.adds.one"] = "agrega 1",
        ["change.adds.many"] = "agrega {n}",
        ["change.removes.zero"] = "no quita ninguna",
        ["change.removes.one"] = "quita 1",
        ["change.removes.many"] = "quita {n}",
        ["change.moves.one"] = "Mueve 1 fila.",
        ["change.moves.many"] = "Mueve {n} filas.",
        ["change.hitPolicy"] = "Cambia la política de coincidencia.",
        ["change.noResult"] = "sin resultado",
        ["claims.relation.implements"] = "implementa",
        ["claims.relation.adapts"] = "adapta",
        ["claims.relation.informed_by"] = "se inspira en",
        ["claims.source.added"] = "La regla ahora cita la fuente {source}, versión {version}, que {relation}.",
        ["claims.source.removed"] = "La regla ya no cita la fuente {source}.",
        ["claims.source.version"] = "La regla sigue la fuente {source} en la versión {now} en lugar de la versión {was}.",
        ["claims.source.relation"] = "La regla {now} la fuente {source} donde antes {was}.",
        ["claims.source.differences"] = "Cambiaron las diferencias con la fuente {source}.",
        ["claims.authority.set"] = "La regla ahora declara su autoridad: {authority}.",
        ["claims.authority.cleared"] = "La regla ya no declara su autoridad ({authority}).",
        ["claims.authority.changed"] = "La autoridad de la regla cambió de {was} a {now}.",
        ["claims.other"] = "Cambió lo que la regla dice sobre sus fuentes.",
        ["change.outcomes"] = "; ",
    };

    /// <summary>The keys of the English table, for the tests that read every template.</summary>
    internal static IReadOnlyCollection<string> EnglishKeys => English.Keys;

    /// <summary>The keys of the Spanish table, for the tests that read every template.</summary>
    internal static IReadOnlyCollection<string> SpanishKeys => Spanish.Keys;

    /// <summary>The raw template of a key in a language, placeholders unfilled; for the tests.</summary>
    /// <param name="locale">The language.</param>
    /// <param name="key">The key.</param>
    internal static string Template(WorkbenchLocale locale, string key) => Table(locale)[key];

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

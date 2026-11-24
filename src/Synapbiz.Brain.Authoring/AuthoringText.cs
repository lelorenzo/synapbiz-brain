using System.Globalization;
using System.Text.RegularExpressions;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>
/// Every sentence the authoring tools write for a person, in English and Spanish, as templates with <c>{name}</c> placeholders. The texts say
/// "process" (<c>proceso</c>), never "flow" (<c>flujo</c>). A text is never built from free text of a document: the document supplies ids and
/// labels, which fill the placeholders. Each area adds its keys in one call that gives both languages, so the two tables always hold the same keys.
/// </summary>
internal static partial class AuthoringText
{
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Spanish = new(StringComparer.Ordinal);

    static AuthoringText()
    {
        AddCommon();
        AddChanges();
        AddReady();
        AddTools();
        AddErrors();
        AddPeople();
    }

    /// <summary>The keys whose Spanish text is, on purpose, the English text: separators, punctuation and the word "no".</summary>
    internal static IReadOnlySet<string> IntentionallyEqual { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "join.sep",
        "no",
    };

    internal static IReadOnlyCollection<string> Keys => English.Keys;

    internal static IReadOnlyCollection<string> SpanishKeys => Spanish.Keys;

    internal static string Template(WorkbenchLocale locale, string key) => Table(locale)[key];

    internal static bool Has(string key) => English.ContainsKey(key);

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

    internal static IReadOnlySet<string> Placeholders(string template) =>
        Placeholder().Matches(template).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    internal static string Join(WorkbenchLocale locale, IReadOnlyList<string> items, string joiner = "join.and")
    {
        if (items.Count <= 1)
        {
            return string.Concat(items);
        }

        var head = string.Join(Text(locale, "join.sep"), items.Take(items.Count - 1));
        return head + Text(locale, joiner) + items[^1];
    }

    /// <summary>A moment as a person says it, with its zone: "January 4, 2027 at 14:00 (UTC)".</summary>
    internal static string Moment(WorkbenchLocale locale, DateTimeOffset moment)
    {
        var spanish = locale == WorkbenchLocale.Es;
        var culture = CultureInfo.GetCultureInfo(spanish ? "es-ES" : "en-US");
        var offset = moment.Offset;
        var zone = "UTC";
        if (offset != TimeSpan.Zero)
        {
            var minutes = offset.Minutes == 0 ? string.Empty : ":" + Math.Abs(offset.Minutes).ToString("00", CultureInfo.InvariantCulture);
            zone = "UTC" + (offset < TimeSpan.Zero ? "-" : "+") + Math.Abs(offset.Hours).ToString(CultureInfo.InvariantCulture) + minutes;
        }

        var date = moment.ToString(spanish ? "d 'de' MMMM 'de' yyyy" : "MMMM d, yyyy", culture);
        var time = moment.ToString("HH:mm", CultureInfo.InvariantCulture);
        return date + (spanish ? " a las " : " at ") + time + " (" + zone + ")";
    }

    internal static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    internal static string Duration(WorkbenchLocale locale, TimeSpan span)
    {
        var seconds = Math.Abs(span.Ticks / TimeSpan.TicksPerSecond);
        var parts = new List<string>();
        Unit(locale, parts, "day", seconds / 86_400);
        Unit(locale, parts, "hour", seconds % 86_400 / 3_600);
        Unit(locale, parts, "minute", seconds % 3_600 / 60);
        Unit(locale, parts, "second", seconds % 60);
        if (parts.Count == 0)
        {
            Unit(locale, parts, "second", 0, force: true);
        }

        return Join(locale, parts);
    }

    private static void Unit(WorkbenchLocale locale, List<string> parts, string unit, long count, bool force = false)
    {
        if (count == 0 && !force)
        {
            return;
        }

        parts.Add(count == 1
            ? Text(locale, "duration." + unit + ".one")
            : Text(locale, "duration." + unit + ".many", ("n", Number(count))));
    }

    private static void T(string key, string english, string spanish)
    {
        English.Add(key, english);
        Spanish.Add(key, spanish);
    }

    private static Dictionary<string, string> Table(WorkbenchLocale locale) => locale switch
    {
        WorkbenchLocale.En => English,
        WorkbenchLocale.Es => Spanish,
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, "Unknown locale."),
    };

    private static void AddCommon()
    {
        T("join.sep", ", ", ", ");
        T("join.and", " and ", " y ");
        T("join.or", " or ", " o ");
        T("duration.day.one", "1 day", "1 día");
        T("duration.day.many", "{n} days", "{n} días");
        T("duration.hour.one", "1 hour", "1 hora");
        T("duration.hour.many", "{n} hours", "{n} horas");
        T("duration.minute.one", "1 minute", "1 minuto");
        T("duration.minute.many", "{n} minutes", "{n} minutos");
        T("duration.second.one", "1 second", "1 segundo");
        T("duration.second.many", "{n} seconds", "{n} segundos");
    }

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}

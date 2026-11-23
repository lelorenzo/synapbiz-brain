using System.Globalization;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>The small pieces every flow sentence is built from: ids in code style, lists, durations and the way a timeout is worded.</summary>
internal static class FlowText
{
    /// <summary>An id as it is written in a sentence: between backticks, exactly as the document spells it.</summary>
    internal static string Code(string id) => "`" + id + "`";

    /// <summary>A list of ids in code style joined with the word of the language.</summary>
    internal static string Codes(WorkbenchLocale locale, IEnumerable<string> ids, string joiner = "join.and") =>
        Join(locale, [.. ids.Select(Code)], joiner);

    /// <summary>Items joined as <c>a, b and c</c> (or <c>or</c>) in the language.</summary>
    internal static string Join(WorkbenchLocale locale, IReadOnlyList<string> items, string joiner = "join.and")
    {
        if (items.Count <= 1)
        {
            return string.Concat(items);
        }

        var head = string.Join(FlowTemplates.Text(locale, "join.sep"), items.Take(items.Count - 1));
        return head + FlowTemplates.Text(locale, joiner) + items[^1];
    }

    /// <summary>A length of time in words, largest unit first: <c>1 hour and 30 minutes</c>.</summary>
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

    /// <summary>How a timeout is worded after "if no answer arrives": within a duration, or before, after or at the instant of a variable.</summary>
    internal static string When(WorkbenchLocale locale, OutlineTimeout timeout)
    {
        if (timeout.Variable is { } variable)
        {
            var offset = timeout.Offset ?? TimeSpan.Zero;
            if (offset == TimeSpan.Zero)
            {
                return FlowTemplates.Text(locale, "when.at", ("variable", variable));
            }

            return FlowTemplates.Text(
                locale,
                offset < TimeSpan.Zero ? "when.before" : "when.after",
                ("duration", Duration(locale, offset)),
                ("variable", variable));
        }

        return FlowTemplates.Text(locale, "when.within", ("duration", Duration(locale, timeout.Duration ?? TimeSpan.Zero)));
    }

    /// <summary>A whole number written in the invariant culture.</summary>
    internal static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Unit(WorkbenchLocale locale, List<string> parts, string unit, long count, bool force = false)
    {
        if (count == 0 && !force)
        {
            return;
        }

        parts.Add(count == 1
            ? FlowTemplates.Text(locale, "duration." + unit + ".one")
            : FlowTemplates.Text(locale, "duration." + unit + ".many", ("n", Number(count))));
    }
}

/// <summary>The words of a document in one language: <see cref="ProcessDisplay"/> in the asked language, then English, then a readable form of the id.</summary>
internal sealed class FlowLabels
{
    private readonly ProcessDisplay display;
    private readonly string language;

    internal FlowLabels(BrainProcess process, WorkbenchLocale locale)
    {
        display = ProcessDisplay.From(process);
        language = locale == WorkbenchLocale.Es ? "es" : "en";
        Process = process;
    }

    internal BrainProcess Process { get; }

    internal string Title() => display.Title(language) ?? WorkbenchText.Humanize(Process.Id);

    internal string Stage(string id) => display.Stage(language, id) ?? WorkbenchText.Humanize(id);

    internal string Step(string id) => display.Step(language, id) ?? WorkbenchText.Humanize(id);

    internal string Result(string id) => display.Result(language, id) ?? WorkbenchText.Humanize(id);

    internal string Queue(string id) => display.Queue(language, id) ?? WorkbenchText.Humanize(id);

    internal string Outcome(string id) => display.Outcome(language, id) ?? WorkbenchText.Humanize(id);

    internal string Window(string id) => display.Window(language, id) ?? WorkbenchText.Humanize(id);
}

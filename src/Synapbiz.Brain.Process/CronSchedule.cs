using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Synapbiz.Brain.Process;

/// <summary>
/// A parsed five-field cron expression of the process format (minute, hour, day of month, month, day of week) and the
/// arithmetic that finds its next occurrence on the wall clock of a time zone. The type is pure: the zone is a parameter and
/// no clock is read. The grammar and the daylight saving rules are in section 2.1 of the W1-C specification.
/// </summary>
public sealed partial class CronSchedule
{
    private const int HorizonYears = 8;

    // A fixed instant used to prove that an expression can fire at all; nothing depends on its value but the leap-year pattern.
    private static readonly DateTimeOffset ReachabilityAnchor = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly bool[] months;
    private readonly bool[] daysOfMonth;
    private readonly bool[] daysOfWeek;
    private readonly bool bothDaysRestricted;
    private readonly int[] minutesOfDay;

    private CronSchedule(string text, bool[] minutes, bool[] hours, bool[] daysOfMonth, bool[] months, bool[] daysOfWeek, bool bothDaysRestricted)
    {
        Text = text;
        this.months = months;
        this.daysOfMonth = daysOfMonth;
        this.daysOfWeek = daysOfWeek;
        this.bothDaysRestricted = bothDaysRestricted;
        minutesOfDay =
        [
            .. Enumerable.Range(0, 24).Where(hour => hours[hour])
                .SelectMany(hour => Enumerable.Range(0, 60).Where(minute => minutes[minute]).Select(minute => (hour * 60) + minute)),
        ];
    }

    /// <summary>The text the schedule was parsed from.</summary>
    public string Text { get; }

    /// <summary>
    /// Parses a cron expression: five fields separated by single spaces, each a <c>*</c>, a number, a range <c>a-b</c>, a step
    /// <c>*/n</c> or <c>a-b/n</c>, or a comma list of those. Names, seconds, <c>L</c>, <c>W</c>, <c>#</c>, <c>?</c> and macros
    /// are not part of the grammar. When both day fields are other than <c>*</c> a day matches when either matches. False when
    /// the text is not valid or the expression can never fire (for example <c>0 0 31 2 *</c>).
    /// </summary>
    /// <param name="text">The expression.</param>
    /// <param name="schedule">The parsed schedule when the result is true.</param>
    public static bool TryParse(string text, [NotNullWhen(true)] out CronSchedule? schedule)
    {
        schedule = null;
        var fields = (text ?? "").Split(' ');
        if (fields.Length != 5
            || !TryField(fields[0], 0, 59, out var minutes)
            || !TryField(fields[1], 0, 23, out var hours)
            || !TryField(fields[2], 1, 31, out var daysOfMonth)
            || !TryField(fields[3], 1, 12, out var months)
            || !TryField(fields[4], 0, 7, out var daysOfWeek))
        {
            return false;
        }

        // 7 is Sunday as well as 0.
        daysOfWeek[0] |= daysOfWeek[7];
        var parsed = new CronSchedule(text!, minutes, hours, daysOfMonth, months, daysOfWeek, fields[2] != "*" && fields[4] != "*");
        if (!parsed.TryNext(ReachabilityAnchor, TimeZoneInfo.Utc, out _))
        {
            return false;
        }

        schedule = parsed;
        return true;
    }

    /// <summary>
    /// The first occurrence strictly after <paramref name="after"/>, as a UTC instant, evaluated on the wall clock of
    /// <paramref name="zone"/>. A local time that does not exist on a day (the gap of a daylight saving transition) fires at the
    /// first valid instant after the gap, once; a local time that occurs twice fires at its first occurrence only. Iterating
    /// the method is strictly increasing and never returns one instant for two local occurrences. The search looks at most
    /// eight years ahead.
    /// </summary>
    /// <param name="after">The instant the occurrence must follow.</param>
    /// <param name="zone">The time zone whose wall clock the expression is read on.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="after"/> is in the last years of the supported range.</exception>
    /// <exception cref="InvalidOperationException">There is no occurrence within eight years (not possible for a parsed schedule except at the end of the supported range).</exception>
    public DateTimeOffset Next(DateTimeOffset after, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (after.Year > DateTimeOffset.MaxValue.Year - HorizonYears - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(after), after, "The instant is too close to the end of the supported range.");
        }

        return TryNext(after, zone, out var next)
            ? next
            : throw new InvalidOperationException("The expression has no occurrence within eight years.");
    }

    private bool TryNext(DateTimeOffset after, TimeZoneInfo zone, out DateTimeOffset next)
    {
        var today = LocalTimes.Local(after, zone).Date;
        var last = today.AddYears(HorizonYears).AddDays(1);
        // One day back: a repeated hour at midnight can make a local time of the previous day follow the instant.
        for (var day = today.AddDays(-1); day <= last; day = day.AddDays(1))
        {
            if (!DayMatches(day))
            {
                continue;
            }

            foreach (var minute in minutesOfDay)
            {
                var instant = LocalTimes.ToInstant(zone, day.ToDateTime(new TimeOnly(minute / 60, minute % 60)));
                if (instant > after)
                {
                    next = instant;
                    return true;
                }
            }
        }

        next = default;
        return false;
    }

    private bool DayMatches(DateOnly day)
    {
        if (!months[day.Month])
        {
            return false;
        }

        var ofMonth = daysOfMonth[day.Day];
        var ofWeek = daysOfWeek[(int)day.DayOfWeek];
        return bothDaysRestricted ? ofMonth || ofWeek : ofMonth && ofWeek;
    }

    // The set of a field, indexed by value; false when the field is not valid.
    private static bool TryField(string text, int minimum, int maximum, [NotNullWhen(true)] out bool[]? values)
    {
        var set = new bool[maximum + 1];
        values = null;
        foreach (var item in text.Split(','))
        {
            if (!TryItem(item, minimum, maximum, set))
            {
                return false;
            }
        }

        values = set;
        return true;
    }

    private static bool TryItem(string item, int minimum, int maximum, bool[] set)
    {
        var parts = item.Split('/');
        if (parts.Length > 2)
        {
            return false;
        }

        var step = 1;
        if (parts.Length == 2)
        {
            if (!StepPattern().IsMatch(parts[1]))
            {
                return false;
            }

            step = int.Parse(parts[1], CultureInfo.InvariantCulture);
            if (step < 1)
            {
                return false;
            }
        }

        int from;
        int to;
        if (parts[0] == "*")
        {
            (from, to) = (minimum, maximum);
        }
        else if (!TryRange(parts[0], minimum, maximum, out from, out to) || (parts.Length == 2 && !parts[0].Contains('-', StringComparison.Ordinal)))
        {
            // A step applies to * or to a range, never to a single number.
            return false;
        }

        for (var value = from; value <= to; value += step)
        {
            set[value] = true;
        }

        return true;
    }

    private static bool TryRange(string text, int minimum, int maximum, out int from, out int to)
    {
        from = to = 0;
        var ends = text.Split('-');
        if (ends.Length > 2 || !NumberPattern().IsMatch(ends[0]) || (ends.Length == 2 && !NumberPattern().IsMatch(ends[1])))
        {
            return false;
        }

        from = to = int.Parse(ends[0], CultureInfo.InvariantCulture);
        if (ends.Length == 2)
        {
            to = int.Parse(ends[1], CultureInfo.InvariantCulture);
        }

        return from >= minimum && to <= maximum && from <= to;
    }

    [GeneratedRegex(@"\A[0-9]{1,2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\A[0-9]{1,4}\z", RegexOptions.CultureInvariant)]
    private static partial Regex StepPattern();
}

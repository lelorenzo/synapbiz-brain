namespace Synapbiz.Brain.Process;

/// <summary>
/// The pure arithmetic of delivery windows (section 3 of the W1-C specification), used by the host and by the scenario
/// runner. A window is open on a listed local day from its start inclusive to its end exclusive, read on the wall clock of a
/// time zone. The zone is a parameter and no clock is read.
/// </summary>
public static class DeliveryWindows
{
    // Windows repeat weekly, so an opening is always within a week; the extra days cover a window that a gap swallows.
    private const int SearchDays = 15;

    /// <summary>
    /// <paramref name="now"/> (in UTC) when the window is open at <paramref name="now"/>, else the next instant at which it
    /// opens (in UTC). The window is evaluated on the local wall clock. An opening time that falls inside a daylight saving gap
    /// opens at the end of the gap; an opening time that occurs twice opens at its first occurrence; a day on which the window
    /// does not exist any more because a gap swallows it is skipped.
    /// </summary>
    /// <param name="now">The instant to start from.</param>
    /// <param name="zone">The time zone whose wall clock the window is read on.</param>
    /// <param name="days">The local days of the week the window is open on; not empty.</param>
    /// <param name="from">The local time of day the window opens, inclusive.</param>
    /// <param name="to">The local time of day the window closes, exclusive; <c>00:00</c> means 24:00, the end of the day.</param>
    /// <exception cref="ArgumentException"><paramref name="days"/> is empty, or the window is empty (<paramref name="to"/> is not after <paramref name="from"/>).</exception>
    public static DateTimeOffset NextOpen(DateTimeOffset now, TimeZoneInfo zone, IReadOnlyList<DayOfWeek> days, TimeOnly from, TimeOnly to)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(days);
        if (days.Count == 0)
        {
            throw new ArgumentException("A window needs at least one day.", nameof(days));
        }

        var opens = from.ToTimeSpan();
        var closes = to == TimeOnly.MinValue ? TimeSpan.FromDays(1) : to.ToTimeSpan();
        if (closes <= opens)
        {
            throw new ArgumentException("The window must close after it opens.", nameof(to));
        }

        var instant = now.ToUniversalTime();
        if (IsOpen(instant, zone, days, opens, closes))
        {
            return instant;
        }

        var today = LocalTimes.Local(instant, zone).Date;
        for (var offset = -1; offset < SearchDays; offset++)
        {
            var day = today.AddDays(offset);
            if (!days.Contains(day.DayOfWeek))
            {
                continue;
            }

            var opening = LocalTimes.ToInstant(zone, day.ToDateTime(TimeOnly.MinValue) + opens);
            if (opening > instant && IsOpen(opening, zone, days, opens, closes))
            {
                return opening;
            }
        }

        throw new InvalidOperationException("The window never opens.");
    }

    /// <summary>
    /// True when the text has the shape of a time zone id that the format accepts: <c>UTC</c>, or <c>Area/Location</c> with up to two
    /// locations (<c>^[A-Za-z_]+(/[A-Za-z0-9_+\-]+){1,2}$</c>). Nothing is looked up: a host calls this before it asks its time zone
    /// database, so that an id such as <c>localtime</c>, <c>EST5EDT</c>, <c>Factory</c>, a path or an empty text never reaches it.
    /// </summary>
    /// <param name="text">The text to check; null is not a zone id.</param>
    public static bool IsZoneIdShape(string? text) => LocalTimes.IsZoneIdShape(text);

    private static bool IsOpen(DateTimeOffset instant, TimeZoneInfo zone, IReadOnlyList<DayOfWeek> days, TimeSpan opens, TimeSpan closes)
    {
        var (_, timeOfDay, day) = LocalTimes.Local(instant, zone);
        return days.Contains(day) && timeOfDay >= opens && timeOfDay < closes;
    }
}

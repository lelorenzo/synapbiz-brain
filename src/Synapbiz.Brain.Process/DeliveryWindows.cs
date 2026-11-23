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
    /// opens (in UTC). On each listed local day the window is the interval from its opening instant, inclusive, to its closing
    /// instant, exclusive. The instant of a local time that occurs twice is its first occurrence; the instant of a local time
    /// inside a daylight saving gap is the end of the gap; a window whose two instants coincide (a gap swallows it) is empty that
    /// day. A window that spans a repeated hour stays open through both passes; one that closes inside it closes on the first pass.
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
        var today = LocalTimes.Local(instant, zone).Date;
        DateTimeOffset? earliest = null;
        for (var offset = -2; offset < SearchDays; offset++)
        {
            var day = today.AddDays(offset);
            if (!days.Contains(day.DayOfWeek))
            {
                continue;
            }

            // The window of the day is the interval between two instants; a repeated local time stands for its first occurrence.
            var midnight = day.ToDateTime(TimeOnly.MinValue);
            var opening = LocalTimes.ToInstant(zone, midnight + opens);
            var closing = LocalTimes.ToInstant(zone, midnight + closes);
            if (opening <= instant && instant < closing)
            {
                return instant;
            }

            if (opening > instant && opening < closing && (earliest is null || opening < earliest))
            {
                earliest = opening;
            }
        }

        return earliest ?? throw new InvalidOperationException("The window never opens.");
    }

    /// <summary>
    /// True when the text has the shape of a time zone id that the format accepts: <c>UTC</c>, or <c>Area/Location</c> with up to two
    /// locations (<c>^[A-Za-z_]+(/[A-Za-z0-9_+\-]+){1,2}$</c>). Nothing is looked up: a host calls this before it asks its time zone
    /// database, so that an id such as <c>localtime</c>, <c>EST5EDT</c>, <c>Factory</c>, a path or an empty text never reaches it.
    /// </summary>
    /// <param name="text">The text to check; null is not a zone id.</param>
    public static bool IsZoneIdShape(string? text) => LocalTimes.IsZoneIdShape(text);
}

using System.Text.RegularExpressions;

namespace Synapbiz.Brain.Process;

/// <summary>
/// The one place where a local wall-clock time of a <see cref="TimeZoneInfo"/> becomes an instant, with the daylight saving
/// rules shared by <see cref="CronSchedule"/> and <see cref="DeliveryWindows"/>: a local time that does not exist (the gap)
/// is the first valid instant after the gap, and a local time that occurs twice is its first occurrence. It reads no clock
/// and no time zone database: the zone is always a parameter.
/// </summary>
internal static partial class LocalTimes
{
    // No offset of a time zone exceeds 14 hours; 15 hours on either side of a local time always brackets the end of a gap.
    private static readonly TimeSpan Bracket = TimeSpan.FromHours(15);

    /// <summary>The instant a local time (a <see cref="DateTime"/> of kind unspecified) stands for in the zone, in UTC.</summary>
    internal static DateTimeOffset ToInstant(TimeZoneInfo zone, DateTime local)
    {
        if (zone.IsInvalidTime(local))
        {
            return EndOfGap(zone, local);
        }

        if (zone.IsAmbiguousTime(local))
        {
            var first = zone.GetAmbiguousTimeOffsets(local).Max();
            return new DateTimeOffset(local, first).ToUniversalTime();
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>True when the text has the shape of an IANA time zone id (<c>UTC</c> or <c>Area/Location</c> with up to two locations); nothing is looked up.</summary>
    internal static bool IsZoneIdShape(string? text) => text is not null && ZoneShape().IsMatch(text);

    // The first instant at which the wall clock of the zone shows the local time or later: the wall clock is monotone around a gap.
    private static DateTimeOffset EndOfGap(TimeZoneInfo zone, DateTime local)
    {
        var anchor = DateTime.SpecifyKind(local, DateTimeKind.Utc);
        var low = anchor - Bracket;
        var high = anchor + Bracket;
        while (high - low > TimeSpan.FromSeconds(1))
        {
            var middle = low + TimeSpan.FromSeconds(Math.Floor((high - low).TotalSeconds / 2));
            if (TimeZoneInfo.ConvertTimeFromUtc(middle, zone) >= local)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return new DateTimeOffset(high, TimeSpan.Zero);
    }

    [GeneratedRegex(@"\A(?:UTC|[A-Za-z_]+(?:/[A-Za-z0-9_+\-]+){1,2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex ZoneShape();

    /// <summary>The local calendar date and time of day of an instant in the zone.</summary>
    internal static (DateOnly Date, TimeSpan TimeOfDay, DayOfWeek Day) Local(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return (DateOnly.FromDateTime(local.DateTime), local.TimeOfDay, local.DayOfWeek);
    }
}

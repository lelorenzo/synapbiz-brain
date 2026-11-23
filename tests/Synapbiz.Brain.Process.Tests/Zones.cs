namespace Synapbiz.Brain.Process.Tests;

/// <summary>Time zones for the tests, built without a system time zone database.</summary>
internal static class Zones
{
    /// <summary>
    /// A zone of the United States pattern: UTC-5, one hour of daylight saving from 02:00 on the second Sunday of March (the
    /// clock jumps to 03:00) to 02:00 on the first Sunday of November (the clock goes back to 01:00). In 2027 the two
    /// transitions are on 14 March and on 7 November.
    /// </summary>
    internal static TimeZoneInfo Eastern { get; } = CreateEastern();

    /// <summary>A zone with a fixed offset of +05:30 and no daylight saving.</summary>
    internal static TimeZoneInfo Fixed { get; } = TimeZoneInfo.CreateCustomTimeZone("Test/Fixed", new TimeSpan(5, 30, 0), "Fixed", "Fixed");

    /// <summary>The instant of a UTC date and time.</summary>
    internal static DateTimeOffset Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);

    private static TimeZoneInfo CreateEastern()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified), 3, 2, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("Test/Eastern", TimeSpan.FromHours(-5), "Eastern", "Eastern standard", "Eastern daylight", [rule]);
    }
}

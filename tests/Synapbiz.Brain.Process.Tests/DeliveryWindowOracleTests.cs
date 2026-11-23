using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>
/// <see cref="DeliveryWindows.NextOpen"/> against a brute-force oracle that reads section 10.5 of the W1-C specification on its own:
/// for every minute of a fortnight around a daylight saving transition, the earliest minute at or after it that lies inside the
/// window. The window of a listed day is the interval from the instant of its opening time, inclusive, to the instant of its closing
/// time, exclusive; the instant of a local time is the first minute at which the wall clock of the zone shows it or later (so a
/// repeated time stands for its first occurrence and a time inside a gap for the end of the gap). The windows open, close and lie
/// inside the repeated hour and the gap of each zone, and cover both.
/// </summary>
public sealed class DeliveryWindowOracleTests
{
    private static readonly DayOfWeek[] Every = [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];
    private static readonly DayOfWeek[] SundayAndWednesday = [DayOfWeek.Sunday, DayOfWeek.Wednesday];
    private static readonly DayOfWeek[] SundayAndFriday = [DayOfWeek.Sunday, DayOfWeek.Friday];
    private static readonly DayOfWeek[] Sunday = [DayOfWeek.Sunday];
    private static readonly DayOfWeek[] Weekend = [DayOfWeek.Saturday, DayOfWeek.Sunday];

    private static readonly TimeZoneInfo UnitedStates = Zones.Eastern;

    // A zone of the Lord Howe pattern: UTC+10:30, a daylight saving of thirty minutes from the first Sunday of October at 02:00
    // (the clock jumps to 02:30) to the first Sunday of April at 02:00 (the clock goes back to 01:30).
    private static readonly TimeZoneInfo HalfHour = Custom("Test/HalfHour", new TimeSpan(10, 30, 0), TimeSpan.FromMinutes(30), Transition(2, 0, 10, 1), Transition(2, 0, 4, 1));

    // A zone of the European pattern: UTC+1, from the last Sunday of March at 02:00 (the clock jumps to 03:00) to the last Sunday of
    // October at 03:00 (the clock goes back to 02:00).
    private static readonly TimeZoneInfo Europe = Custom("Test/Europe", TimeSpan.FromHours(1), TimeSpan.FromHours(1), Transition(2, 0, 3, 5), Transition(3, 0, 10, 5));

    public static TheoryData<string, DateTimeOffset> Fortnights => new()
    {
        { "us", new DateTimeOffset(2027, 3, 7, 0, 0, 0, TimeSpan.Zero) },
        { "us", new DateTimeOffset(2027, 10, 31, 0, 0, 0, TimeSpan.Zero) },
        { "half", new DateTimeOffset(2027, 3, 28, 0, 0, 0, TimeSpan.Zero) },
        { "half", new DateTimeOffset(2027, 9, 26, 0, 0, 0, TimeSpan.Zero) },
        { "europe", new DateTimeOffset(2027, 3, 21, 0, 0, 0, TimeSpan.Zero) },
        { "europe", new DateTimeOffset(2027, 10, 24, 0, 0, 0, TimeSpan.Zero) },
    };

    private sealed record Window(DayOfWeek[] Days, TimeOnly From, TimeOnly To);

    private static TimeZoneInfo.TransitionTime Transition(int hour, int minute, int month, int week) =>
        TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, hour, minute, 0, DateTimeKind.Unspecified), month, week, DayOfWeek.Sunday);

    private static TimeZoneInfo Custom(string id, TimeSpan offset, TimeSpan delta, TimeZoneInfo.TransitionTime start, TimeZoneInfo.TransitionTime end)
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, delta, start, end);
        return TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id + " standard", id + " daylight", [rule]);
    }

    // The repeated local hour is 01:00-02:00 (United States), 01:30-02:00 (half hour) and 02:00-03:00 (Europe); the gap is 02:00-03:00,
    // 02:00-02:30 and 02:00-03:00. The windows of each fortnight open, close and lie inside the one that its transition has.
    private static (TimeZoneInfo Zone, Window[] Windows) Case(string name, DateTimeOffset from) => name switch
    {
        "us" => (UnitedStates, from.Month == 3
            ?
            [
                new(Every, new TimeOnly(2, 30), new TimeOnly(3, 30)),
                new(Every, new TimeOnly(2, 0), new TimeOnly(2, 45)),
                new(Every, new TimeOnly(1, 30), new TimeOnly(2, 30)),
                new(Every, new TimeOnly(2, 15), new TimeOnly(4, 0)),
                new(Sunday, new TimeOnly(1, 0), new TimeOnly(3, 0)),
                new(SundayAndWednesday, new TimeOnly(9, 0), new TimeOnly(18, 0)),
                new(Sunday, new TimeOnly(23, 0), TimeOnly.MinValue),
                new(Weekend, new TimeOnly(0, 0), new TimeOnly(1, 30)),
            ]
            :
            [
                new(Every, new TimeOnly(2, 0), new TimeOnly(4, 0)),
                new(SundayAndFriday, new TimeOnly(1, 30), new TimeOnly(2, 45)),
                new(Sunday, new TimeOnly(0, 15), new TimeOnly(1, 30)),
                new(Sunday, new TimeOnly(1, 15), new TimeOnly(1, 45)),
                new(Sunday, new TimeOnly(0, 30), new TimeOnly(3, 0)),
                new(Every, new TimeOnly(1, 0), new TimeOnly(2, 0)),
                new(Sunday, new TimeOnly(0, 15), new TimeOnly(1, 0)),
                new(SundayAndWednesday, new TimeOnly(9, 0), new TimeOnly(18, 0)),
                new(Sunday, new TimeOnly(23, 0), TimeOnly.MinValue),
            ]),
        "half" => (HalfHour, from.Month == 9
            ?
            [
                new(Every, new TimeOnly(2, 15), new TimeOnly(3, 15)),
                new(Every, new TimeOnly(2, 0), new TimeOnly(2, 20)),
                new(Every, new TimeOnly(1, 45), new TimeOnly(2, 15)),
                new(Every, new TimeOnly(2, 10), new TimeOnly(2, 40)),
                new(Sunday, new TimeOnly(1, 0), new TimeOnly(2, 0)),
                new(Weekend, new TimeOnly(8, 0), new TimeOnly(17, 30)),
            ]
            :
            [
                new(Every, new TimeOnly(2, 0), new TimeOnly(5, 0)),
                new(Sunday, new TimeOnly(0, 30), new TimeOnly(1, 30)),
                new(Sunday, new TimeOnly(1, 45), new TimeOnly(2, 30)),
                new(Sunday, new TimeOnly(1, 0), new TimeOnly(3, 0)),
                new(Sunday, new TimeOnly(1, 30), new TimeOnly(2, 0)),
                new(Every, new TimeOnly(1, 40), new TimeOnly(1, 50)),
                new(Weekend, new TimeOnly(8, 0), new TimeOnly(17, 30)),
            ]),
        _ => (Europe, from.Month == 3
            ?
            [
                new(Every, new TimeOnly(2, 30), new TimeOnly(3, 30)),
                new(Every, new TimeOnly(2, 0), new TimeOnly(2, 45)),
                new(Every, new TimeOnly(1, 0), new TimeOnly(2, 0)),
                new(Every, new TimeOnly(1, 30), new TimeOnly(2, 30)),
                new(Every, new TimeOnly(2, 30), new TimeOnly(4, 0)),
                new(Sunday, new TimeOnly(1, 0), new TimeOnly(4, 0)),
                new(Sunday, new TimeOnly(23, 0), TimeOnly.MinValue),
            ]
            :
            [
                new(Every, new TimeOnly(3, 0), new TimeOnly(4, 0)),
                new(Every, new TimeOnly(2, 0), new TimeOnly(3, 0)),
                new(Sunday, new TimeOnly(0, 0), new TimeOnly(2, 0)),
                new(Sunday, new TimeOnly(2, 15), new TimeOnly(2, 45)),
                new(Sunday, new TimeOnly(1, 30), new TimeOnly(2, 30)),
                new(Sunday, new TimeOnly(2, 30), new TimeOnly(3, 30)),
                new(Sunday, new TimeOnly(1, 0), new TimeOnly(4, 0)),
                new(SundayAndWednesday, new TimeOnly(9, 0), new TimeOnly(18, 0)),
            ]),
    };

    [Theory]
    [MemberData(nameof(Fortnights))]
    public void EveryMinuteOfAFortnightAgreesWithTheIntervalOfTheSpecification(string name, DateTimeOffset start)
    {
        var (zone, windows) = Case(name, start);
        const int Lead = 2 * 24 * 60;
        const int Minutes = 14 * 24 * 60;
        const int Horizon = 16 * 24 * 60;
        var origin = start.AddMinutes(-Lead);

        // The wall clock of the zone at every minute of the range, read from the zone and nothing else.
        var length = Lead + Minutes + Horizon + 24 * 60;
        var wall = new DateTime[length];
        for (var minute = 0; minute < length; minute++)
        {
            wall[minute] = TimeZoneInfo.ConvertTime(origin.AddMinutes(minute), zone).DateTime;
        }

        // The instant of a local time: the first minute at which the wall clock shows it or later.
        int Instant(DateTime local) => Array.FindIndex(wall, shown => shown >= local);

        foreach (var window in windows)
        {
            var open = new bool[length + 1];
            for (var day = start.UtcDateTime.Date.AddDays(-1); day < start.UtcDateTime.Date.AddDays(14 + 16); day = day.AddDays(1))
            {
                if (!window.Days.Contains(day.DayOfWeek))
                {
                    continue;
                }

                var opening = Instant(DateTime.SpecifyKind(day, DateTimeKind.Unspecified) + window.From.ToTimeSpan());
                var closing = Instant(DateTime.SpecifyKind(day, DateTimeKind.Unspecified) + (window.To == TimeOnly.MinValue ? TimeSpan.FromDays(1) : window.To.ToTimeSpan()));
                for (var minute = opening; minute < closing; minute++)
                {
                    open[minute] = true;
                }
            }

            // The earliest minute at or after each minute that lies inside the window, by one backward sweep.
            var next = new int[length + 1];
            next[length] = int.MaxValue;
            for (var minute = length - 1; minute >= 0; minute--)
            {
                next[minute] = open[minute] ? minute : next[minute + 1];
            }

            for (var minute = Lead; minute < Lead + Minutes; minute++)
            {
                var now = origin.AddMinutes(minute);
                var opens = DeliveryWindows.NextOpen(now, zone, window.Days, window.From, window.To);

                opens.Should().Be(origin.AddMinutes(next[minute]), "{0} {1}-{2} {3} at {4:u}", name, window.From, window.To, string.Join(',', window.Days), now);
            }
        }
    }

    [Fact]
    public void TheZonesReallyHaveTheTransitionsTheWindowsAreChosenAround()
    {
        UnitedStates.IsInvalidTime(new DateTime(2027, 3, 14, 2, 30, 0, DateTimeKind.Unspecified)).Should().BeTrue();
        UnitedStates.IsAmbiguousTime(new DateTime(2027, 11, 7, 1, 30, 0, DateTimeKind.Unspecified)).Should().BeTrue();
        HalfHour.IsInvalidTime(new DateTime(2027, 10, 3, 2, 10, 0, DateTimeKind.Unspecified)).Should().BeTrue();
        HalfHour.IsInvalidTime(new DateTime(2027, 10, 3, 2, 40, 0, DateTimeKind.Unspecified)).Should().BeFalse();
        HalfHour.IsAmbiguousTime(new DateTime(2027, 4, 4, 1, 45, 0, DateTimeKind.Unspecified)).Should().BeTrue();
        Europe.IsInvalidTime(new DateTime(2027, 3, 28, 2, 30, 0, DateTimeKind.Unspecified)).Should().BeTrue();
        Europe.IsAmbiguousTime(new DateTime(2027, 10, 31, 2, 30, 0, DateTimeKind.Unspecified)).Should().BeTrue();
    }
}

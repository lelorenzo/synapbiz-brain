using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>
/// <see cref="DeliveryWindows.NextOpen"/> against a brute-force oracle: for every minute of a fortnight around a daylight saving
/// transition, the earliest minute at or after it at which the wall clock of the zone is inside the window. The windows tested do
/// not touch the repeated local hour of the zone (what the specification says for a window that does, section 10.5, is a separate
/// question); they do touch the gap, which the wall clock reading and the opening rule of the specification agree on.
/// </summary>
public sealed class DeliveryWindowOracleTests
{
    private static readonly DayOfWeek[] Every = [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];
    private static readonly DayOfWeek[] SundayAndWednesday = [DayOfWeek.Sunday, DayOfWeek.Wednesday];
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

    // The repeated local hour of each zone is 01:00-02:00 (United States), 01:30-02:00 (half hour) and 02:00-03:00 (Europe); the
    // windows end at or before its start, or start at or after its end, and the gap is 02:00-03:00, 02:00-02:30 and 02:00-03:00.
    private static (TimeZoneInfo Zone, Window[] Windows) Case(string name, DateTimeOffset from) => name switch
    {
        "us" => (UnitedStates, from.Month == 3
            ?
            [
                new(Every, new TimeOnly(2, 30), new TimeOnly(3, 30)),
                new(Every, new TimeOnly(2, 0), new TimeOnly(2, 45)),
                new(SundayAndWednesday, new TimeOnly(9, 0), new TimeOnly(18, 0)),
                new(Sunday, new TimeOnly(23, 0), TimeOnly.MinValue),
                new(Weekend, new TimeOnly(0, 0), new TimeOnly(1, 30)),
            ]
            :
            [
                new(Every, new TimeOnly(2, 0), new TimeOnly(4, 0)),
                new(SundayAndWednesday, new TimeOnly(9, 0), new TimeOnly(18, 0)),
                new(Sunday, new TimeOnly(0, 15), new TimeOnly(1, 0)),
                new(Sunday, new TimeOnly(23, 0), TimeOnly.MinValue),
            ]),
        "half" => (HalfHour, from.Month == 9
            ?
            [
                new(Every, new TimeOnly(2, 15), new TimeOnly(3, 15)),
                new(Every, new TimeOnly(2, 0), new TimeOnly(2, 20)),
                new(Sunday, new TimeOnly(1, 0), new TimeOnly(2, 0)),
                new(Weekend, new TimeOnly(8, 0), new TimeOnly(17, 30)),
            ]
            :
            [
                new(Every, new TimeOnly(2, 0), new TimeOnly(5, 0)),
                new(Sunday, new TimeOnly(0, 30), new TimeOnly(1, 30)),
                new(Weekend, new TimeOnly(8, 0), new TimeOnly(17, 30)),
            ]),
        _ => (Europe, from.Month == 3
            ?
            [
                new(Every, new TimeOnly(2, 30), new TimeOnly(3, 30)),
                new(Every, new TimeOnly(2, 0), new TimeOnly(2, 45)),
                new(Every, new TimeOnly(1, 0), new TimeOnly(2, 0)),
                new(Sunday, new TimeOnly(23, 0), TimeOnly.MinValue),
            ]
            :
            [
                new(Every, new TimeOnly(3, 0), new TimeOnly(4, 0)),
                new(Sunday, new TimeOnly(0, 0), new TimeOnly(2, 0)),
                new(SundayAndWednesday, new TimeOnly(9, 0), new TimeOnly(18, 0)),
            ]),
    };

    private static bool IsOpen(DateTimeOffset instant, TimeZoneInfo zone, Window window)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        var closes = window.To == TimeOnly.MinValue ? TimeSpan.FromDays(1) : window.To.ToTimeSpan();
        return window.Days.Contains(local.DayOfWeek) && local.TimeOfDay >= window.From.ToTimeSpan() && local.TimeOfDay < closes;
    }

    [Theory]
    [MemberData(nameof(Fortnights))]
    public void EveryMinuteOfAFortnightAroundATransitionAgreesWithTheWallClock(string name, DateTimeOffset start)
    {
        var (zone, windows) = Case(name, start);
        const int Minutes = 14 * 24 * 60;
        const int Horizon = 16 * 24 * 60;

        foreach (var window in windows)
        {
            // The earliest minute at or after each minute at which the window is open, found by one backward sweep.
            var next = new int[Minutes + Horizon + 1];
            next[^1] = int.MaxValue;
            for (var minute = next.Length - 2; minute >= 0; minute--)
            {
                next[minute] = IsOpen(start.AddMinutes(minute), zone, window) ? minute : next[minute + 1];
            }

            for (var minute = 0; minute < Minutes; minute++)
            {
                var now = start.AddMinutes(minute);
                var opens = DeliveryWindows.NextOpen(now, zone, window.Days, window.From, window.To);

                opens.Should().Be(start.AddMinutes(next[minute]), "{0} {1}-{2} {3} at {4:u}", name, window.From, window.To, string.Join(',', window.Days), now);
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

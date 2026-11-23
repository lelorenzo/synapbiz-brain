using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Section 3 of the W1-C specification: <see cref="DeliveryWindows.NextOpen"/>, the arithmetic of delivery windows.</summary>
public sealed class DeliveryWindowTests
{
    private static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private static DateTimeOffset Open(DateTimeOffset now, DayOfWeek[] days, TimeOnly from, TimeOnly to, TimeZoneInfo? zone = null) =>
        DeliveryWindows.NextOpen(now, zone ?? Zones.Eastern, days, from, to);

    private static DateTimeOffset Business(DateTimeOffset now) => Open(now, Weekdays, new TimeOnly(9, 0), new TimeOnly(18, 0));

    [Fact]
    public void AnOpenWindowReturnsNow()
    {
        // Monday 2027-01-04 10:00 local (UTC-5).
        var now = Zones.Utc(2027, 1, 4, 15);

        Business(now).Should().Be(now);
    }

    [Fact]
    public void TheOpeningInstantIsInsideAndTheClosingInstantIsOutside()
    {
        Business(Zones.Utc(2027, 1, 4, 14)).Should().Be(Zones.Utc(2027, 1, 4, 14));
        Business(Zones.Utc(2027, 1, 4, 22, 59, 59)).Should().Be(Zones.Utc(2027, 1, 4, 22, 59, 59));
        Business(Zones.Utc(2027, 1, 4, 23)).Should().Be(Zones.Utc(2027, 1, 5, 14));
    }

    [Fact]
    public void BeforeOpeningTheWindowOpensTheSameDay()
    {
        // Monday 07:00 local.
        Business(Zones.Utc(2027, 1, 4, 12)).Should().Be(Zones.Utc(2027, 1, 4, 14));
        Business(Zones.Utc(2027, 1, 4, 13, 59, 59)).Should().Be(Zones.Utc(2027, 1, 4, 14));
    }

    [Fact]
    public void AfterClosingTheWindowOpensTheNextListedDay()
    {
        // Tuesday 19:00 local.
        Business(Zones.Utc(2027, 1, 6, 0)).Should().Be(Zones.Utc(2027, 1, 6, 14));
    }

    [Fact]
    public void TheWeekendIsSkipped()
    {
        // Friday 2027-01-08 19:00 local, Saturday midday and Sunday midday all wait for Monday 2027-01-11 09:00 local.
        var monday = Zones.Utc(2027, 1, 11, 14);

        Business(Zones.Utc(2027, 1, 9, 0)).Should().Be(monday);
        Business(Zones.Utc(2027, 1, 9, 17)).Should().Be(monday);
        Business(Zones.Utc(2027, 1, 10, 17)).Should().Be(monday);
    }

    [Fact]
    public void TheDayIsTheLocalDayNotTheUtcDay()
    {
        // Monday 2027-01-04 22:00 UTC is 17:00 local on Monday: open. Tuesday 2027-01-05 00:30 UTC is Monday 19:30 local: closed.
        var evening = Zones.Utc(2027, 1, 4, 22);

        Business(evening).Should().Be(evening);
        Business(Zones.Utc(2027, 1, 5, 0, 30)).Should().Be(Zones.Utc(2027, 1, 5, 14));
    }

    [Fact]
    public void ATimeOfDayZeroAsTheEndMeansTheEndOfTheDay()
    {
        var days = new[] { DayOfWeek.Monday };
        var evening = new TimeOnly(20, 0);

        // Monday 23:30 local is open until midnight; Tuesday 00:00 local is closed and the window opens again next Monday.
        Open(Zones.Utc(2027, 1, 5, 4, 30), days, evening, TimeOnly.MinValue).Should().Be(Zones.Utc(2027, 1, 5, 4, 30));
        Open(Zones.Utc(2027, 1, 5, 5), days, evening, TimeOnly.MinValue).Should().Be(Zones.Utc(2027, 1, 12, 1));
    }

    [Fact]
    public void AWindowFromMidnightToMidnightIsOpenAllDayOnItsDays()
    {
        var days = new[] { DayOfWeek.Tuesday };

        // Tuesday 2027-01-05 local covers 05:00 UTC that day to 05:00 UTC the next.
        var now = Zones.Utc(2027, 1, 5, 5);
        Open(now, days, TimeOnly.MinValue, TimeOnly.MinValue).Should().Be(now);
        Open(Zones.Utc(2027, 1, 6, 4, 59, 59), days, TimeOnly.MinValue, TimeOnly.MinValue).Should().Be(Zones.Utc(2027, 1, 6, 4, 59, 59));
        Open(Zones.Utc(2027, 1, 6, 5), days, TimeOnly.MinValue, TimeOnly.MinValue).Should().Be(Zones.Utc(2027, 1, 12, 5));
    }

    [Fact]
    public void AnOpeningTimeInsideTheGapOpensAtTheEndOfTheGap()
    {
        var sunday = new[] { DayOfWeek.Sunday };

        // 14 March 2027 is a Sunday: 02:30 does not exist; the clock reads 03:00 at 07:00 UTC, inside the window 02:30 to 05:00.
        Open(Zones.Utc(2027, 3, 14, 5), sunday, new TimeOnly(2, 30), new TimeOnly(5, 0)).Should().Be(Zones.Utc(2027, 3, 14, 7));
        // At 07:30 UTC it is 03:30 local: open.
        var inside = Zones.Utc(2027, 3, 14, 7, 30);
        Open(inside, sunday, new TimeOnly(2, 30), new TimeOnly(5, 0)).Should().Be(inside);
    }

    [Fact]
    public void AWindowThatTheGapSwallowsHasNoOpeningThatDay()
    {
        var sunday = new[] { DayOfWeek.Sunday };

        // 02:15 to 02:45 on 14 March 2027 never exists; the next Sunday opens at 02:15 EDT = 06:15 UTC.
        Open(Zones.Utc(2027, 3, 14, 5), sunday, new TimeOnly(2, 15), new TimeOnly(2, 45)).Should().Be(Zones.Utc(2027, 3, 21, 6, 15));
    }

    [Fact]
    public void AnOpeningTimeThatOccursTwiceOpensAtItsFirstOccurrence()
    {
        var sunday = new[] { DayOfWeek.Sunday };

        // 7 November 2027 is a Sunday: 01:30 is 05:30 UTC (daylight) and 06:30 UTC (standard).
        Open(Zones.Utc(2027, 11, 7, 4), sunday, new TimeOnly(1, 30), new TimeOnly(5, 0)).Should().Be(Zones.Utc(2027, 11, 7, 5, 30));
        // The window is the interval from the first 01:30 (05:30 UTC): it stays open through the second pass of the hour, also at 01:15 on it.
        var second = Zones.Utc(2027, 11, 7, 6, 45);
        Open(second, sunday, new TimeOnly(1, 30), new TimeOnly(5, 0)).Should().Be(second);
        var early = Zones.Utc(2027, 11, 7, 6, 15);
        Open(early, sunday, new TimeOnly(1, 30), new TimeOnly(5, 0)).Should().Be(early);
    }

    [Fact]
    public void AZoneWithoutDaylightSavingAndTheResultInUtc()
    {
        var days = new[] { DayOfWeek.Monday };
        var now = new DateTimeOffset(2027, 1, 4, 0, 0, 0, TimeSpan.FromHours(9));

        // Sunday 2027-01-03 15:00 UTC; the zone at +05:30 reads Sunday 20:30: the window opens Monday 09:00 local = 03:30 UTC.
        var next = Open(now, days, new TimeOnly(9, 0), new TimeOnly(17, 0), Zones.Fixed);

        next.Should().Be(Zones.Utc(2027, 1, 4, 3, 30));
        next.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        var now = Zones.Utc(2027, 1, 4);
        var nine = new TimeOnly(9, 0);

        var noDays = () => DeliveryWindows.NextOpen(now, Zones.Eastern, [], nine, new TimeOnly(17, 0));
        var empty = () => DeliveryWindows.NextOpen(now, Zones.Eastern, Weekdays, nine, nine);
        var backwards = () => DeliveryWindows.NextOpen(now, Zones.Eastern, Weekdays, nine, new TimeOnly(8, 0));
        var noZone = () => DeliveryWindows.NextOpen(now, null!, Weekdays, nine, new TimeOnly(17, 0));
        var nullDays = () => DeliveryWindows.NextOpen(now, Zones.Eastern, null!, nine, new TimeOnly(17, 0));

        noDays.Should().Throw<ArgumentException>();
        empty.Should().Throw<ArgumentException>();
        backwards.Should().Throw<ArgumentException>();
        noZone.Should().Throw<ArgumentNullException>();
        nullDays.Should().Throw<ArgumentNullException>();
    }
}

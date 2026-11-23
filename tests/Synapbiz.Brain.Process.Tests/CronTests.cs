using FluentAssertions;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Section 2.1 of the W1-C specification: the cron grammar and the next occurrence on the wall clock of a zone.</summary>
public sealed class CronTests
{
    [Theory]
    [InlineData("* * * * *")]
    [InlineData("0 6 * * *")]
    [InlineData("00 06 * * *")]
    [InlineData("59 23 31 12 7")]
    [InlineData("0 0 29 2 *")]
    [InlineData("*/15 * * * *")]
    [InlineData("0-30/10 8-9 * * 1-5")]
    [InlineData("0,15,30,45 8,12,18 1,15 1,6 0,6")]
    [InlineData("5 4 * * 0-7")]
    [InlineData("0 0 13 * 5")]
    [InlineData("0 0 31 1,3,5,7,8,10,12 *")]
    [InlineData("*/1 */1 */1 */1 */1")]
    [InlineData("0 0 1 1 */7")]
    public void AValidExpressionIsParsedAndKeepsItsText(string text)
    {
        CronSchedule.TryParse(text, out var schedule).Should().BeTrue();

        schedule!.Text.Should().Be(text);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("* * * *")]
    [InlineData("* * * * * *")]
    [InlineData("*  * * * *")]
    [InlineData(" * * * * *")]
    [InlineData("* * * * * ")]
    [InlineData("*\t*\t*\t*\t*")]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("* * 0 * *")]
    [InlineData("* * 32 * *")]
    [InlineData("* * * 0 *")]
    [InlineData("* * * 13 *")]
    [InlineData("* * * * 8")]
    [InlineData("* * * jan *")]
    [InlineData("* * * * mon")]
    [InlineData("? * * * *")]
    [InlineData("* * L * *")]
    [InlineData("* * 15W * *")]
    [InlineData("* * * * 1#2")]
    [InlineData("@daily")]
    [InlineData("0 0 0 * * *")]
    [InlineData("*/0 * * * *")]
    [InlineData("*/ * * * *")]
    [InlineData("*/a * * * *")]
    [InlineData("*/2/3 * * * *")]
    [InlineData("*/12345 * * * *")]
    [InlineData("5/15 * * * *")]
    [InlineData("1- * * * *")]
    [InlineData("-5 * * * *")]
    [InlineData("5-3 * * * *")]
    [InlineData("1-2-3 * * * *")]
    [InlineData("1,,2 * * * *")]
    [InlineData("1, * * * *")]
    [InlineData(",1 * * * *")]
    [InlineData("+5 * * * *")]
    [InlineData("1.5 * * * *")]
    [InlineData("100 * * * *")]
    [InlineData("*-5 * * * *")]
    [InlineData("0 0 30 2 *")]
    [InlineData("0 0 31 2 *")]
    [InlineData("0 0 31 4 *")]
    [InlineData("0 0 31 2,4,6,9,11 *")]
    [InlineData("0 0 30-31 2 *")]
    public void AnInvalidExpressionOrOneThatCanNeverFireIsRefused(string text)
    {
        CronSchedule.TryParse(text, out var schedule).Should().BeFalse();

        schedule.Should().BeNull();
    }

    [Fact]
    public void ANullExpressionIsRefused()
    {
        CronSchedule.TryParse(null!, out var schedule).Should().BeFalse();

        schedule.Should().BeNull();
    }

    private static CronSchedule Cron(string text)
    {
        CronSchedule.TryParse(text, out var schedule).Should().BeTrue();
        return schedule!;
    }

    [Fact]
    public void TheNextOccurrenceIsStrictlyAfterTheInstant()
    {
        var cron = Cron("0 6 * * *");

        cron.Next(Zones.Utc(2027, 1, 4, 5, 59, 59), TimeZoneInfo.Utc).Should().Be(Zones.Utc(2027, 1, 4, 6));
        cron.Next(Zones.Utc(2027, 1, 4, 6), TimeZoneInfo.Utc).Should().Be(Zones.Utc(2027, 1, 5, 6));
        cron.Next(Zones.Utc(2027, 1, 4, 6, 0, 1), TimeZoneInfo.Utc).Should().Be(Zones.Utc(2027, 1, 5, 6));
    }

    [Fact]
    public void TheResultIsInUtcWhateverTheOffsetOfTheArgument()
    {
        var after = new DateTimeOffset(2027, 1, 4, 0, 30, 0, TimeSpan.FromHours(-5));

        var next = Cron("0 6 * * *").Next(after, TimeZoneInfo.Utc);

        next.Should().Be(Zones.Utc(2027, 1, 4, 6));
        next.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void AZoneWithoutDaylightSavingShiftsTheOccurrenceByItsOffset()
    {
        var cron = Cron("0 6 * * *");

        cron.Next(Zones.Utc(2027, 1, 4), Zones.Fixed).Should().Be(Zones.Utc(2027, 1, 4, 0, 30));
        cron.Next(Zones.Utc(2027, 1, 4, 0, 30), Zones.Fixed).Should().Be(Zones.Utc(2027, 1, 5, 0, 30));
        cron.Next(Zones.Utc(2027, 7, 1), Zones.Fixed).Should().Be(Zones.Utc(2027, 7, 1, 0, 30));
    }

    [Fact]
    public void TheLocalDayDecidesNotTheUtcDay()
    {
        var cron = Cron("30 23 * * 5");

        // 23:30 on a Friday in a zone at +05:30 is 18:00 UTC the same Friday; 2027-01-08 is a Friday.
        cron.Next(Zones.Utc(2027, 1, 1), Zones.Fixed).Should().Be(Zones.Utc(2027, 1, 1, 18));
        cron.Next(Zones.Utc(2027, 1, 1, 18), Zones.Fixed).Should().Be(Zones.Utc(2027, 1, 8, 18));
    }

    [Fact]
    public void ALocalTimeInTheSpringGapFiresOnceAtTheEndOfTheGap()
    {
        var cron = Cron("30 2 * * *");
        var gapEnd = Zones.Utc(2027, 3, 14, 7);

        // 02:30 does not exist on 14 March 2027 in the zone: the clock goes from 02:00 to 03:00 at 07:00 UTC.
        cron.Next(Zones.Utc(2027, 3, 13, 8), Zones.Eastern).Should().Be(gapEnd);
        cron.Next(gapEnd, Zones.Eastern).Should().Be(Zones.Utc(2027, 3, 15, 6, 30));
    }

    [Fact]
    public void SeveralLocalTimesInTheGapFireOnlyOnce()
    {
        var cron = Cron("*/15 2 * * *");
        var after = Zones.Utc(2027, 3, 13, 7, 45);
        var fired = new List<DateTimeOffset>();

        for (var index = 0; index < 5; index++)
        {
            after = cron.Next(after, Zones.Eastern);
            fired.Add(after);
        }

        fired.Should().Equal(
            Zones.Utc(2027, 3, 14, 7),
            Zones.Utc(2027, 3, 15, 6, 0),
            Zones.Utc(2027, 3, 15, 6, 15),
            Zones.Utc(2027, 3, 15, 6, 30),
            Zones.Utc(2027, 3, 15, 6, 45));
    }

    [Fact]
    public void ALocalTimeInTheGapAndTheLocalTimeAtItsEndAreOneOccurrence()
    {
        var cron = Cron("0,30 2-3 * * *");
        var after = Zones.Utc(2027, 3, 14, 6);
        var fired = new List<DateTimeOffset>();

        for (var index = 0; index < 3; index++)
        {
            after = cron.Next(after, Zones.Eastern);
            fired.Add(after);
        }

        // 02:00 and 02:30 do not exist and 03:00 is the end of the gap, all at 07:00 UTC; then 03:30 EDT, then 02:00 EDT the next day.
        fired.Should().Equal(Zones.Utc(2027, 3, 14, 7), Zones.Utc(2027, 3, 14, 7, 30), Zones.Utc(2027, 3, 15, 6));
    }

    [Fact]
    public void ALocalTimeThatOccursTwiceFiresAtItsFirstOccurrenceOnly()
    {
        var cron = Cron("30 1 * * *");

        // 01:30 on 7 November 2027 is 05:30 UTC (daylight) and again 06:30 UTC (standard): only the first fires.
        cron.Next(Zones.Utc(2027, 11, 6, 12), Zones.Eastern).Should().Be(Zones.Utc(2027, 11, 7, 5, 30));
        cron.Next(Zones.Utc(2027, 11, 7, 5, 30), Zones.Eastern).Should().Be(Zones.Utc(2027, 11, 8, 6, 30));
        cron.Next(Zones.Utc(2027, 11, 7, 6, 0), Zones.Eastern).Should().Be(Zones.Utc(2027, 11, 8, 6, 30));
    }

    [Fact]
    public void AnHourlyScheduleSkipsTheRepeatedHourAndTheGapIsOneOccurrence()
    {
        var hourly = Cron("0 * * * *");
        var spring = Enumerable.Range(0, 4).Aggregate(
            (After: Zones.Utc(2027, 3, 14, 4, 30), Fired: new List<DateTimeOffset>()),
            (state, _) =>
            {
                var next = hourly.Next(state.After, Zones.Eastern);
                state.Fired.Add(next);
                return (next, state.Fired);
            }).Fired;
        var autumn = Enumerable.Range(0, 4).Aggregate(
            (After: Zones.Utc(2027, 11, 7, 3, 30), Fired: new List<DateTimeOffset>()),
            (state, _) =>
            {
                var next = hourly.Next(state.After, Zones.Eastern);
                state.Fired.Add(next);
                return (next, state.Fired);
            }).Fired;

        // Spring: 00:00 EST, 01:00 EST, the gap (02:00 and 03:00 are one occurrence at 07:00 UTC), 04:00 EDT.
        spring.Should().Equal(Zones.Utc(2027, 3, 14, 5), Zones.Utc(2027, 3, 14, 6), Zones.Utc(2027, 3, 14, 7), Zones.Utc(2027, 3, 14, 8));
        // Autumn: 00:00 EDT, 01:00 EDT (the first of two), then 02:00 EST at 07:00 UTC; 01:00 EST at 06:00 UTC does not fire.
        autumn.Should().Equal(Zones.Utc(2027, 11, 7, 4), Zones.Utc(2027, 11, 7, 5), Zones.Utc(2027, 11, 7, 7), Zones.Utc(2027, 11, 7, 8));
    }

    [Fact]
    public void IteratingAcrossBothTransitionsIsStrictlyIncreasingWithoutRepeats()
    {
        var cron = Cron("*/20 0-4 * * *");
        var after = Zones.Utc(2027, 3, 12);
        var fired = new List<DateTimeOffset>();

        while (after < Zones.Utc(2027, 11, 10))
        {
            after = cron.Next(after, Zones.Eastern);
            fired.Add(after);
        }

        fired.Should().HaveCountGreaterThan(1000);
        fired.Zip(fired.Skip(1)).Should().OnlyContain(pair => pair.First < pair.Second);
        fired.Should().OnlyHaveUniqueItems();
        // Fifteen a day (5 hours of 3). The spring day has 12: the gap's 02:00, 02:20, 02:40 and 03:00 are one occurrence. The autumn day has 15: the repeated hour fires once.
        int OnDay(DateTimeOffset day) => fired.Count(instant => instant >= day && instant < day.AddDays(1));
        OnDay(Zones.Utc(2027, 3, 14)).Should().Be(12);
        OnDay(Zones.Utc(2027, 3, 15)).Should().Be(15);
        OnDay(Zones.Utc(2027, 11, 7)).Should().Be(15);
    }

    [Fact]
    public void WhenBothDayFieldsAreRestrictedEitherMatches()
    {
        var cron = Cron("0 0 13 * 5");
        var after = Zones.Utc(2027, 1, 1);
        var fired = new List<DateTimeOffset>();

        for (var index = 0; index < 5; index++)
        {
            after = cron.Next(after, TimeZoneInfo.Utc);
            fired.Add(after);
        }

        // 2027-01-01 is a Friday: it is after the instant only strictly, so the first is Friday the 8th, then the 13th (a Wednesday), the Fridays after.
        fired.Should().Equal(
            Zones.Utc(2027, 1, 8),
            Zones.Utc(2027, 1, 13),
            Zones.Utc(2027, 1, 15),
            Zones.Utc(2027, 1, 22),
            Zones.Utc(2027, 1, 29));
    }

    [Fact]
    public void WhenOnlyOneDayFieldIsRestrictedBothMustMatch()
    {
        // The day of week is * : only the 13th. The day of month is *: only Fridays.
        Cron("0 0 13 * *").Next(Zones.Utc(2027, 1, 1), TimeZoneInfo.Utc).Should().Be(Zones.Utc(2027, 1, 13));
        Cron("0 0 * * 5").Next(Zones.Utc(2027, 1, 9), TimeZoneInfo.Utc).Should().Be(Zones.Utc(2027, 1, 15));
    }

    [Fact]
    public void AStarWithAStepIsNotTheLiteralStarSoTheDayFieldsAreOred()
    {
        // */2 in the day of month is a restricted field here (only the text * is not), so Mondays also match.
        var cron = Cron("0 0 */2 * 1");

        cron.Next(Zones.Utc(2027, 1, 1), TimeZoneInfo.Utc).Should().Be(Zones.Utc(2027, 1, 3));
        cron.Next(Zones.Utc(2027, 1, 3), TimeZoneInfo.Utc).Should().Be(Zones.Utc(2027, 1, 4));
    }

    [Fact]
    public void SevenAndZeroAreBothSunday()
    {
        var zero = Cron("0 0 * * 0");
        var seven = Cron("0 0 * * 7");

        // 2027-01-03 is a Sunday.
        zero.Next(Zones.Utc(2027, 1, 1), TimeZoneInfo.Utc).Should().Be(Zones.Utc(2027, 1, 3));
        seven.Next(Zones.Utc(2027, 1, 1), TimeZoneInfo.Utc).Should().Be(Zones.Utc(2027, 1, 3));
        Cron("0 0 * * 5-7").Next(Zones.Utc(2027, 1, 1), TimeZoneInfo.Utc).Should().Be(Zones.Utc(2027, 1, 2));
    }

    [Fact]
    public void RangesStepsAndListsSelectTheirValues()
    {
        var cron = Cron("0-30/10 8-9 * * 1-5");
        var after = Zones.Utc(2027, 1, 4, 7);
        var fired = new List<DateTimeOffset>();

        for (var index = 0; index < 9; index++)
        {
            after = cron.Next(after, TimeZoneInfo.Utc);
            fired.Add(after);
        }

        // 2027-01-04 is a Monday: eight occurrences, then Tuesday morning.
        fired.Take(8).Select(instant => instant.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture))
            .Should().Equal("08:00", "08:10", "08:20", "08:30", "09:00", "09:10", "09:20", "09:30");
        fired[8].Should().Be(Zones.Utc(2027, 1, 5, 8));
    }

    [Fact]
    public void ADayThatIsNotInTheMonthIsSkipped()
    {
        var cron = Cron("0 0 31 * *");

        cron.Next(Zones.Utc(2027, 1, 31), TimeZoneInfo.Utc).Should().Be(Zones.Utc(2027, 3, 31));
    }

    [Fact]
    public void TheLeapDayIsFoundWithinEightYears()
    {
        var cron = Cron("0 0 29 2 *");

        cron.Next(Zones.Utc(2027, 3, 1), TimeZoneInfo.Utc).Should().Be(Zones.Utc(2028, 2, 29));
        // 2100 is not a leap year, so the gap between two leap days is eight years.
        cron.Next(Zones.Utc(2096, 3, 1), TimeZoneInfo.Utc).Should().Be(Zones.Utc(2104, 2, 29));
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        var cron = Cron("0 6 * * *");

        var nullZone = () => cron.Next(Zones.Utc(2027, 1, 1), null!);
        var late = () => cron.Next(Zones.Utc(9995, 1, 1), TimeZoneInfo.Utc);

        nullZone.Should().Throw<ArgumentNullException>();
        late.Should().Throw<ArgumentOutOfRangeException>();
    }
}

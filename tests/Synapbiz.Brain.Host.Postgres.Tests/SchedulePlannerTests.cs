using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>The misfire arithmetic, without a database: which occurrence is fired and when the schedule is due next.</summary>
public sealed class SchedulePlannerTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    private static readonly CronSchedule Hourly = Parse("0 * * * *");

    private static CronSchedule Parse(string text) => CronSchedule.TryParse(text, out var cron) ? cron : throw new InvalidOperationException(text);

    private static DateTimeOffset At(int day, int hour, int minute, int second = 0) => new(2027, 1, day, hour, minute, second, TimeSpan.Zero);

    [Fact]
    public void ASingleOccurrenceThatIsOnTimeIsFiredWhateverThePolicy()
    {
        foreach (var policy in new[] { ProcessMisfire.Skip, ProcessMisfire.FireOnce })
        {
            var plan = SchedulePlanner.Plan(Hourly, TimeZoneInfo.Utc, policy, At(4, 15, 0), At(4, 15, 2), Grace);

            plan.Should().Be(new SchedulePlan(At(4, 15, 0), false, At(4, 15, 0), At(4, 16, 0)));
        }
    }

    [Theory]
    [InlineData(ProcessMisfire.Skip, false)]
    [InlineData(ProcessMisfire.FireOnce, true)]
    public void ASingleOccurrenceThatIsLateIsDroppedBySkipAndFiredByFireOnce(ProcessMisfire policy, bool fires)
    {
        var plan = SchedulePlanner.Plan(Hourly, TimeZoneInfo.Utc, policy, At(4, 15, 0), At(4, 15, 5, 1), Grace);

        plan.Fire.Should().Be(fires ? At(4, 15, 0) : null);
        plan.Misfired.Should().BeTrue();
        plan.NextFireAt.Should().Be(At(4, 16, 0));
    }

    [Fact]
    public void TheOccurrenceExactlyAtTheEdgeOfTheGraceIsOnTime()
    {
        var plan = SchedulePlanner.Plan(Hourly, TimeZoneInfo.Utc, ProcessMisfire.Skip, At(4, 15, 0), At(4, 15, 5), Grace);

        plan.Fire.Should().Be(At(4, 15, 0));
        plan.Misfired.Should().BeFalse();
    }

    [Fact]
    public void SeveralOccurrencesFireOnlyTheLastAndOnlyWhenItIsOnTimeUnderSkip()
    {
        var late = SchedulePlanner.Plan(Hourly, TimeZoneInfo.Utc, ProcessMisfire.Skip, At(4, 15, 0), At(4, 17, 10), Grace);
        var onTime = SchedulePlanner.Plan(Hourly, TimeZoneInfo.Utc, ProcessMisfire.Skip, At(4, 15, 0), At(4, 17, 3), Grace);
        var once = SchedulePlanner.Plan(Hourly, TimeZoneInfo.Utc, ProcessMisfire.FireOnce, At(4, 15, 0), At(4, 17, 10), Grace);

        late.Should().Be(new SchedulePlan(null, true, At(4, 17, 0), At(4, 18, 0)));
        onTime.Should().Be(new SchedulePlan(At(4, 17, 0), true, At(4, 17, 0), At(4, 18, 0)));
        once.Should().Be(new SchedulePlan(At(4, 17, 0), true, At(4, 17, 0), At(4, 18, 0)));
    }

    [Fact]
    public void ANowThatIsAnOccurrenceItselfIsHandledNowAndTheNextOneIsAfterIt()
    {
        var plan = SchedulePlanner.Plan(Hourly, TimeZoneInfo.Utc, ProcessMisfire.Skip, At(4, 15, 0), At(4, 17, 0), Grace);

        plan.Last.Should().Be(At(4, 17, 0));
        plan.Fire.Should().Be(At(4, 17, 0));
        plan.NextFireAt.Should().Be(At(4, 18, 0));
    }

    [Fact]
    public void AHostThatWasDownForAYearOfMinutelyOccurrencesFindsTheLastOneWithoutWalkingThemAll()
    {
        var minutely = Parse("* * * * *");
        var now = new DateTimeOffset(2028, 1, 4, 15, 0, 30, TimeSpan.Zero);

        var plan = SchedulePlanner.Plan(minutely, TimeZoneInfo.Utc, ProcessMisfire.FireOnce, At(4, 15, 0), now, Grace);

        plan.Last.Should().Be(new DateTimeOffset(2028, 1, 4, 15, 0, 0, TimeSpan.Zero));
        plan.Fire.Should().Be(plan.Last);
        plan.NextFireAt.Should().Be(new DateTimeOffset(2028, 1, 4, 15, 1, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AnOccurrenceAYearAgoOfARareScheduleIsFoundToo()
    {
        var yearly = Parse("0 0 1 1 *");

        var plan = SchedulePlanner.Plan(yearly, TimeZoneInfo.Utc, ProcessMisfire.FireOnce, new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero), Grace);

        plan.Last.Should().Be(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        plan.NextFireAt.Should().Be(new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void TheArithmeticFollowsTheWallClockOfTheZoneAcrossATransition()
    {
        var daily = Parse("30 2 * * *");
        var zone = TestZoneResolver.Eastern;

        // 2027-03-14 02:30 does not exist in the zone: the occurrence is 03:00 EDT, 07:00 UTC; the next one is 02:30 EDT on the 15th.
        var plan = SchedulePlanner.Plan(daily, zone, ProcessMisfire.FireOnce, new DateTimeOffset(2027, 3, 14, 7, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 3, 14, 7, 1, 0, TimeSpan.Zero), Grace);

        plan.Fire.Should().Be(new DateTimeOffset(2027, 3, 14, 7, 0, 0, TimeSpan.Zero));
        plan.NextFireAt.Should().Be(new DateTimeOffset(2027, 3, 15, 6, 30, 0, TimeSpan.Zero));
    }
}

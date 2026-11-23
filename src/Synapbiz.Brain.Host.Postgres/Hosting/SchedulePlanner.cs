using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>What the pump does with a due schedule: the occurrence to start an instance for, if any, and the next fire time.</summary>
/// <param name="Fire">The occurrence to fire; null when nothing is fired.</param>
/// <param name="Misfired">True when the schedule missed occurrences (more than one is due, or the one that is due is not on time).</param>
/// <param name="Last">The last occurrence at or before now.</param>
/// <param name="NextFireAt">The first occurrence after now.</param>
internal readonly record struct SchedulePlan(DateTimeOffset? Fire, bool Misfired, DateTimeOffset Last, DateTimeOffset NextFireAt);

/// <summary>
/// The misfire arithmetic of section 7 of the W1-C specification, pure: given the occurrence a schedule is due at and the time,
/// which occurrence is fired and when the schedule is due next.
/// </summary>
internal static class SchedulePlanner
{
    // The first look back when the last occurrence at or before now is searched for; it doubles until an occurrence is inside.
    private static readonly TimeSpan FirstLookBack = TimeSpan.FromHours(1);

    /// <summary>
    /// Plans a due schedule. The occurrences are <c>o1 = nextFireAt, o2, ... ok</c> up to <paramref name="now"/>; an occurrence is on
    /// time when <paramref name="now"/> minus it is at most <paramref name="grace"/>. A single on-time occurrence is fired. Otherwise
    /// the schedule misfired: <see cref="ProcessMisfire.Skip"/> fires the last occurrence only if it is on time, and
    /// <see cref="ProcessMisfire.FireOnce"/> fires it whether or not it is on time; every earlier occurrence is dropped. The next
    /// fire time is the first occurrence after <paramref name="now"/>.
    /// </summary>
    internal static SchedulePlan Plan(CronSchedule cron, TimeZoneInfo zone, ProcessMisfire misfire, DateTimeOffset nextFireAt, DateTimeOffset now, TimeSpan grace)
    {
        var first = nextFireAt.ToUniversalTime();
        now = now.ToUniversalTime();
        var second = cron.Next(first, zone);
        var several = second <= now;
        var last = several ? LastAtOrBefore(cron, zone, second, now) : first;
        var next = cron.Next(now, zone);

        bool OnTime(DateTimeOffset occurrence) => now - occurrence <= grace;

        if (!several && OnTime(first))
        {
            return new SchedulePlan(first, false, last, next);
        }

        DateTimeOffset? fire = null;
        if (misfire == ProcessMisfire.FireOnce || OnTime(last))
        {
            fire = last;
        }

        return new SchedulePlan(fire, true, last, next);
    }

    /// <summary>
    /// The last occurrence at or before <paramref name="now"/>, given one that is known to be (<paramref name="known"/>). A host that
    /// was down for a long time must not walk every occurrence, so the search starts from a look-back that grows until it holds one.
    /// </summary>
    private static DateTimeOffset LastAtOrBefore(CronSchedule cron, TimeZoneInfo zone, DateTimeOffset known, DateTimeOffset now)
    {
        var last = known;
        var lookBack = FirstLookBack;
        while (now - lookBack > last)
        {
            var candidate = cron.Next(now - lookBack, zone);
            if (candidate <= now)
            {
                last = candidate;
                break;
            }

            lookBack += lookBack;
        }

        for (var next = cron.Next(last, zone); next <= now; next = cron.Next(last, zone))
        {
            last = next;
        }

        return last;
    }
}

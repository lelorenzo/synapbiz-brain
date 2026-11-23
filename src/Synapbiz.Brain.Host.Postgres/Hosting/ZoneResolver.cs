namespace Synapbiz.Brain.Process.Hosting;

/// <summary>
/// The one seam through which the host turns an IANA time zone id into a <see cref="TimeZoneInfo"/>. Everything that needs a zone
/// (activation, schedules, delivery windows) asks this interface, so that tests can supply zones without the system time zone
/// database.
/// </summary>
internal interface IZoneResolver
{
    /// <summary>The zone with the given id, or null when the id is not known.</summary>
    TimeZoneInfo? Find(string id);
}

/// <summary>The default resolver: the time zone database of the machine. The only place that asks the system for a zone.</summary>
internal sealed class SystemZoneResolver : IZoneResolver
{
    /// <inheritdoc />
    public TimeZoneInfo? Find(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

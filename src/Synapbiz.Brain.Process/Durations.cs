using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Synapbiz.Brain.Process;

/// <summary>ISO 8601 durations of the process format: days, hours, minutes and seconds, at most 3660 days.</summary>
internal static partial class Durations
{
    private const long MaximumSeconds = 3660L * 86_400;

    /// <summary>
    /// Reads a duration. False when the text does not match the pattern, has no component, is zero, or exceeds 3660 days.
    /// A leading minus sign gives a negative duration when <paramref name="signed"/> is true and is refused otherwise.
    /// </summary>
    internal static bool TryParse(string? text, bool signed, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var match = text is null ? Match.Empty : Pattern().Match(text);
        if (!match.Success || (match.Groups[1].Length > 0 && !signed))
        {
            return false;
        }

        var components = new[] { (2, 86_400L), (3, 3_600L), (4, 60L), (5, 1L) }
            .Where(part => match.Groups[part.Item1].Success)
            .Select(part => BigInteger.Parse(match.Groups[part.Item1].Value, NumberStyles.None, CultureInfo.InvariantCulture) * part.Item2)
            .ToList();
        if (components.Count == 0)
        {
            return false;
        }

        var seconds = components.Aggregate(BigInteger.Zero, (sum, part) => sum + part);
        if (seconds.IsZero || seconds > MaximumSeconds)
        {
            return false;
        }

        var ticks = (long)seconds * TimeSpan.TicksPerSecond;
        value = TimeSpan.FromTicks(match.Groups[1].Length > 0 ? -ticks : ticks);
        return true;
    }

    /// <summary>The instant <paramref name="start"/> plus <paramref name="span"/>, saturating at the ends of the supported range.</summary>
    internal static DateTimeOffset Add(DateTimeOffset start, TimeSpan span)
    {
        if (span >= TimeSpan.Zero)
        {
            return span > DateTimeOffset.MaxValue - start ? DateTimeOffset.MaxValue : start + span;
        }

        return span < DateTimeOffset.MinValue - start ? DateTimeOffset.MinValue : start + span;
    }

    [GeneratedRegex(@"\A(-?)P(?:([0-9]+)D)?(?:T(?:([0-9]+)H)?(?:([0-9]+)M)?(?:([0-9]+)S)?)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

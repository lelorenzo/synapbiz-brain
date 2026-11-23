using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>
/// The opaque cursor of an instance list: the creation instant (as UTC ticks, which holds the microsecond precision of a
/// <c>timestamptz</c> exactly) and the instance id of the last row of a page, as URL-safe base64.
/// </summary>
internal static class InstanceCursors
{
    internal static string Format(InstanceSummaryRecord last) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(last.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture) + "|" + last.InstanceId));

    /// <summary>Reads a cursor; anything that is not one is an <see cref="ArgumentException"/>.</summary>
    internal static InstanceCursor Parse(string cursor)
    {
        const string Message = "The cursor is not a cursor of this engine.";
        var buffer = new byte[cursor.Length];
        if (Base64Url.IsValid(cursor) && Base64Url.TryDecodeFromChars(cursor, buffer, out var written))
        {
            var text = Encoding.UTF8.GetString(buffer, 0, written);
            var bar = text.IndexOf('|', StringComparison.Ordinal);
            if (bar > 0
                && long.TryParse(text.AsSpan(0, bar), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks <= DateTime.MaxValue.Ticks)
            {
                var id = text[(bar + 1)..];
                if (id.Length is >= 1 and <= Guard.MaxIdLength)
                {
                    return new InstanceCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
                }
            }
        }

        throw new ArgumentException(Message, nameof(cursor));
    }
}

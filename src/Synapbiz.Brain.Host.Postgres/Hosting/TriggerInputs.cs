using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>Builds the business key and the input of an instance that a trigger starts. Nothing here touches the store.</summary>
internal static class TriggerInputs
{
    /// <summary>The names of the two values a schedule can hand to an instance.</summary>
    internal const string ScheduledFor = "scheduledFor";

    /// <inheritdoc cref="ScheduledFor"/>
    internal const string ScheduledDate = "scheduledDate";

    /// <summary>The first character of every business key the host makes (<c>@schedule/...</c>): a key taken from a payload never starts with it.</summary>
    internal const char ReservedPrefix = '@';

    /// <summary>
    /// The payload of an event as a JSON object; null when it cannot be read as one: it is not an object, it names a member twice, or
    /// a string or member name in it holds an unpaired surrogate (the JSON escape <c>\ud800</c> on its own, which parses but throws
    /// when it is read). Never throws.
    /// </summary>
    internal static JsonObject? ParsePayload(string payloadJson)
    {
        JsonObject? payload;
        try
        {
            payload = JsonNode.Parse(payloadJson) as JsonObject;

            // The members of a parsed object are indexed lazily, so a repeated member name only throws on first access: touch it here.
            _ = payload?.ContainsKey(string.Empty);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // A member name that holds an unpaired surrogate cannot be indexed.
            return null;
        }

        return payload is not null && IsReadable(payloadJson) ? payload : null;
    }

    /// <summary>
    /// The business key an event payload carries in a member, or null when the member is absent or holds no usable key. A usable key is
    /// a JSON string that is not blank, has at most 200 characters, holds no control character and no unpaired surrogate, and does
    /// not start with <c>@</c> (also after leading white space): the prefix is reserved for the keys the host makes. Never throws.
    /// </summary>
    internal static string? BusinessKey(JsonObject payload, string member)
    {
        string? key;
        try
        {
            if (!payload.TryGetPropertyValue(member, out var node) || node is not JsonValue value || !value.TryGetValue<string>(out key))
            {
                return null;
            }
        }
        catch (InvalidOperationException)
        {
            // A string that holds an unpaired surrogate cannot be read as a string.
            return null;
        }
        catch (ArgumentException)
        {
            // A member name that the object holds twice cannot be looked up.
            return null;
        }

        return IsBusinessKey(key) ? key : null;
    }

    /// <summary>True when the text can be the business key of an instance a trigger starts (see <see cref="BusinessKey"/>).</summary>
    internal static bool IsBusinessKey(string key)
    {
        if (key.Length > Guard.MaxIdLength)
        {
            return false;
        }

        var trimmed = key.Trim();
        if (trimmed.Length == 0 || trimmed[0] == ReservedPrefix)
        {
            return false;
        }

        for (var index = 0; index < key.Length; index++)
        {
            var character = key[index];
            if (char.IsControl(character) || (char.IsSurrogate(character) && !IsPaired(key, ref index)))
            {
                return false;
            }
        }

        return true;
    }

    // True when the surrogate at the index is the high half of a pair; the index then moves onto the low half.
    private static bool IsPaired(string text, ref int index)
    {
        if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            index++;
            return true;
        }

        return false;
    }

    // True when every string and member name of a parsed text can be read: an unpaired surrogate in one throws on first access.
    private static bool IsReadable(string json)
    {
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName)
                {
                    _ = reader.GetString();
                }
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        return true;
    }

    /// <summary>The input of an instance started by an event: the activation input with the mapped payload members laid over it. A mapped member the payload does not carry leaves the variable as the activation input has it.</summary>
    internal static JsonObject EventInput(string activationInputJson, string inputMapJson, JsonObject payload)
    {
        var input = Object(activationInputJson);
        foreach (var (variable, member) in Object(inputMapJson))
        {
            if (member is not null && payload.TryGetPropertyValue(member.GetValue<string>(), out var value))
            {
                input[variable] = value?.DeepClone();
            }
        }

        return input;
    }

    /// <summary>The input of an instance started by a schedule: the activation input with <c>scheduledFor</c> (the occurrence as an instant) and <c>scheduledDate</c> (its local date in the zone of the schedule) laid over it as the declaration maps them.</summary>
    internal static JsonObject ScheduleInput(string activationInputJson, ScheduleTriggerDeclaration declaration, DateTimeOffset occurrence, TimeZoneInfo zone)
    {
        var input = Object(activationInputJson);
        foreach (var (variable, source) in declaration.Input)
        {
            input[variable] = source switch
            {
                ScheduledFor => JsonValue.Create(occurrence.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
                ScheduledDate => JsonValue.Create(TimeZoneInfo.ConvertTime(occurrence, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                _ => throw new InvalidOperationException("A schedule input maps from an unknown source."),
            };
        }

        return input;
    }

    /// <summary>The business key of the instance a schedule starts for an occurrence: <c>@schedule/{scheduleId}/{yyyyMMddTHHmmZ}</c>.</summary>
    internal static string ScheduleBusinessKey(string scheduleId, DateTimeOffset occurrence) =>
        ReservedPrefix + "schedule/" + scheduleId + "/" + occurrence.UtcDateTime.ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture);

    private static JsonObject Object(string json) =>
        JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("A stored trigger value is not a JSON object.");
}

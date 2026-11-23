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

    /// <summary>The payload of an event as a JSON object; null when it cannot be read as one (for example duplicate member names).</summary>
    internal static JsonObject? ParsePayload(string payloadJson)
    {
        try
        {
            return JsonNode.Parse(payloadJson) as JsonObject;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The business key an event payload carries in a member: a non-empty JSON string of at most 200 characters, else null.</summary>
    internal static string? BusinessKey(JsonObject payload, string member)
    {
        if (!payload.TryGetPropertyValue(member, out var node) || node is not JsonValue value || !value.TryGetValue<string>(out var key))
        {
            return null;
        }

        return key.Length is > 0 and <= Guard.MaxIdLength ? key : null;
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

    /// <summary>The business key of the instance a schedule starts for an occurrence: <c>{scheduleId}/{yyyyMMddTHHmmZ}</c>.</summary>
    internal static string ScheduleBusinessKey(string scheduleId, DateTimeOffset occurrence) =>
        scheduleId + "/" + occurrence.UtcDateTime.ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture);

    private static JsonObject Object(string json) =>
        JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("A stored trigger value is not a JSON object.");
}

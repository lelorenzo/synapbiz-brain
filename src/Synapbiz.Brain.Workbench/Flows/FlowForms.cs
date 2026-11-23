using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>The kind of control a field of a flow form needs.</summary>
public enum FlowFieldKind
{
    /// <summary>A line of text.</summary>
    Text = 0,

    /// <summary>A number (whole for an <c>Integer</c> variable).</summary>
    Number = 1,

    /// <summary>A date.</summary>
    Date = 2,

    /// <summary>A date and time, read as UTC.</summary>
    DateTime = 3,

    /// <summary>A yes or no.</summary>
    YesNo = 4,

    /// <summary>Several lines: a JSON text, or one value on each line.</summary>
    Lines = 5,
}

/// <summary>One field of the form that starts a case or fills the payload of an event.</summary>
/// <param name="Name">The name of the field: the variable for a start form, the member of the payload for an event.</param>
/// <param name="Type">The type name of the variable the field is tied to (<c>Code</c>, <c>Integer</c>, <c>Instant</c>, <c>Json</c> and so on); <c>Code</c> for a payload member tied to no variable.</param>
/// <param name="Kind">The control the field needs.</param>
public sealed record FlowFormField(string Name, string Type, FlowFieldKind Kind);

/// <summary>What a form wrote: the JSON object, and the names of the fields whose text was not usable (left out of the object).</summary>
/// <param name="Json">The canonical JSON object of the fields that were filled in and usable.</param>
/// <param name="Invalid">The names of the fields whose text could not be read as their type, in the order of the form.</param>
public sealed record FlowFormResult(string Json, IReadOnlyList<string> Invalid);

/// <summary>The values a scenario starts a case with, as the texts of a start form.</summary>
/// <param name="Values">Field name to the text a control shows.</param>
/// <param name="At">The instant the scenario starts at.</param>
public sealed record FlowStartValues(IReadOnlyDictionary<string, string> Values, DateTimeOffset At);

/// <summary>
/// The forms of a flow: which fields start a case and which an event's payload fills, how the texts of those fields become the JSON
/// object the simulator takes, and the texts a scenario would put in the start form. Pure and deterministic; a screen holds no
/// knowledge of the types of variables.
/// </summary>
public static class FlowForms
{
    /// <summary>The form that starts a case: one field for each input variable of the document, in document order.</summary>
    /// <param name="process">The loaded process.</param>
    public static IReadOnlyList<FlowFormField> StartFields(BrainProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var outline = ProcessOutline.From(process);
        return new ReadOnlyCollection<FlowFormField>([.. outline.Variables.Where(variable => variable.IsInput).Select(variable => Field(variable.Name, variable.Type))]);
    }

    /// <summary>
    /// The payload of the event a step reads: one field for each member a <c>wait</c> step keeps, or the <c>interruptBy</c> of an <c>action</c>
    /// keeps, typed by the variable it is kept in. Empty for any other step, a step that keeps nothing, or a step that does not exist.
    /// </summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="stepId">The id of the step.</param>
    public static IReadOnlyList<FlowFormField> PayloadFields(BrainProcess process, string stepId)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(stepId);
        var outline = ProcessOutline.From(process);
        var step = outline.Step(stepId);
        var bindings = step switch
        {
            { Kind: OutlineStepKind.Wait } => step.Outputs,
            { Kind: OutlineStepKind.Action, Interrupt: { } interrupt } => interrupt.Output,
            _ => [],
        };
        var types = outline.Variables.ToDictionary(variable => variable.Name, variable => variable.Type, StringComparer.Ordinal);
        return new ReadOnlyCollection<FlowFormField>([.. bindings.Select(binding => Field(binding.Name, types.GetValueOrDefault(binding.Variable) ?? "Code"))]);
    }

    /// <summary>
    /// Writes the JSON object of a form. A field with no text is left out. A yes or no is <c>true</c> or <c>false</c>; a number is read
    /// in the invariant culture (a whole number for <c>Integer</c>); a date is <c>yyyy-MM-dd</c>; a date and time is <c>yyyy-MM-ddTHH:mm</c>
    /// (seconds optional) or an ISO instant with an offset, and is written as UTC; lines are a JSON object or array when the text is
    /// one, else one string for each non-empty line. A field whose text cannot be read is named in <see cref="FlowFormResult.Invalid"/> and left out.
    /// </summary>
    /// <param name="fields">The fields of the form.</param>
    /// <param name="values">Field name to the text of its control; a missing name counts as empty.</param>
    public static FlowFormResult ToJson(IReadOnlyList<FlowFormField> fields, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(values);
        var result = new JsonObject();
        var invalid = new List<string>();
        foreach (var field in fields)
        {
            var text = values.TryGetValue(field.Name, out var typed) ? typed.Trim() : string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            if (Read(field, text) is { } node)
            {
                result[field.Name] = node;
            }
            else
            {
                invalid.Add(field.Name);
            }
        }

        return new FlowFormResult(FlowJson.Canonical(result), new ReadOnlyCollection<string>(invalid));
    }

    /// <summary>
    /// The texts a start form would hold to start a case as an embedded scenario does: its input as the text of each control, and its
    /// start instant. Null when the document has no scenario with that name.
    /// </summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="scenarioName">The name of an embedded scenario.</param>
    public static FlowStartValues? FromScenario(BrainProcess process, string scenarioName)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(scenarioName);
        var outline = ProcessOutline.From(process);
        if (FlowJson.TryObject(outline.ScenarioJson(scenarioName)) is not { } scenario || scenario["start"] is not JsonObject start)
        {
            return null;
        }

        var types = outline.Variables.ToDictionary(variable => variable.Name, variable => variable.Type, StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, node) in start["input"] as JsonObject ?? [])
        {
            values[name] = TextOf(Field(name, types.GetValueOrDefault(name) ?? "Code"), node);
        }

        var at = DateTimeOffset.Parse(start["at"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None);
        return new FlowStartValues(new ReadOnlyDictionary<string, string>(values), at);
    }

    /// <summary>The text of a date and time control for an instant: UTC, <c>yyyy-MM-ddTHH:mm</c>, with seconds only when they are not zero.</summary>
    /// <param name="instant">The instant.</param>
    public static string DateTimeText(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString(instant.UtcDateTime.Second == 0 ? "yyyy-MM-dd'T'HH:mm" : "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    private static FlowFormField Field(string name, string type) => new(name, type, type switch
    {
        "Boolean" => FlowFieldKind.YesNo,
        "Integer" or "Decimal" => FlowFieldKind.Number,
        "Date" => FlowFieldKind.Date,
        "Instant" => FlowFieldKind.DateTime,
        "Json" or "Money" => FlowFieldKind.Lines,
        _ => FlowFieldKind.Text,
    });

    private static JsonNode? Read(FlowFormField field, string text) => field.Kind switch
    {
        FlowFieldKind.YesNo => text switch
        {
            "true" => JsonValue.Create(true),
            "false" => JsonValue.Create(false),
            _ => null,
        },
        FlowFieldKind.Number => ReadNumber(field.Type, text),
        FlowFieldKind.Date => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            : null,
        FlowFieldKind.DateTime => ReadInstant(text),
        FlowFieldKind.Lines => ReadLines(text),
        _ => JsonValue.Create(text),
    };

    private static JsonValue? ReadNumber(string type, string text)
    {
        if (type == "Integer")
        {
            return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole) ? JsonValue.Create(whole) : null;
        }

        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? JsonValue.Create(number) : null;
    }

    /// <summary>Reads the text of a date-and-time control: <c>yyyy-MM-ddTHH:mm</c> (seconds optional, read as UTC) or an ISO instant with an offset. Null when the text is not one.</summary>
    /// <param name="text">The text of the control.</param>
    public static DateTimeOffset? ParseInstant(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        return DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, styles, out var instant) ? instant : null;
    }

    private static JsonValue? ReadInstant(string text) =>
        ParseInstant(text) is { } instant ? JsonValue.Create(instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)) : null;

    private static JsonNode? ReadLines(string text)
    {
        if (FlowJson.TryParse(text, out var valid) is { } node && valid && node is JsonObject or JsonArray)
        {
            return node;
        }

        var lines = text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).Select(line => (JsonNode?)JsonValue.Create(line));
        return new JsonArray([.. lines]);
    }

    private static string TextOf(FlowFormField field, JsonNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        return field.Kind switch
        {
            FlowFieldKind.YesNo => node.GetValue<bool>() ? "true" : "false",
            FlowFieldKind.DateTime => DateTimeText(DateTimeOffset.Parse(node.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)),
            FlowFieldKind.Lines => FlowJson.Canonical(node),
            FlowFieldKind.Number => FlowJson.Canonical(node),
            _ => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : FlowJson.Canonical(node),
        };
    }
}

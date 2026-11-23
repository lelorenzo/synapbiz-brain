using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process;

/// <summary>
/// Reads a document that passed the schema into a <see cref="ProcessDefinition"/>. The schema guarantees the shape of
/// every member; what the schema cannot say (durations, initial values, JSON Schemas of ai steps) is checked here and
/// reported into <c>errors</c>.
/// </summary>
internal sealed partial class ModelReader(List<BrainSpecError> errors)
{
    private static readonly Dictionary<string, DayOfWeek> WeekDays = new(StringComparer.Ordinal)
    {
        ["mon"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday,
        ["sun"] = DayOfWeek.Sunday,
    };

    internal ProcessDefinition Read(JsonObject root, string json)
    {
        var body = root["body"]!.AsObject();
        var steps = ReadSteps(body["steps"]!.AsObject());
        return new ProcessDefinition
        {
            Id = JsonText.Text(root["id"])!,
            Version = long.Parse(root["version"]!.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture),
            Hash = ContentHash.FromBytes(System.Text.Encoding.UTF8.GetBytes(json)).Value,
            Subject = JsonText.Text(body["subject"])!,
            Stages = [.. body["stages"]!.AsArray().Select(stage => JsonText.Text(stage)!)],
            Variables = ReadVariables(body["variables"]!.AsObject()),
            Accepts = new HashSet<string>(Strings(body["accepts"]), StringComparer.Ordinal),
            MaxSteps = body["limits"]?["maxSteps"] is { } limit ? limit.GetValue<int>() : 10_000,
            Start = JsonText.Text(body["start"])!,
            Steps = steps,
            StepsById = steps.ToDictionary(step => step.Id, StringComparer.Ordinal),
            Uses = [.. root["uses"]!.AsArray().Select((use, index) => (JsonText.Text(use!["$ref"])!, index))],
            Meta = (JsonObject)root["meta"]!.AsObject().DeepClone(),
            Requires = Strings(root["requires"]),
            Scenarios = body["scenarios"] is JsonArray scenarios ? (JsonArray)scenarios.DeepClone() : [],
            Triggers = ReadTriggers(body["triggers"]),
            Windows = ReadWindows(body["windows"]),
        };
    }

    // The declarations are handed out through BrainProcess.Triggers, so the list and the input maps are read-only: a cast cannot change the definition.
    private static ReadOnlyCollection<ProcessTriggerDeclaration> ReadTriggers(JsonNode? node)
    {
        var result = new List<ProcessTriggerDeclaration>();
        foreach (var item in (node as JsonArray ?? []).Select(item => item!.AsObject()))
        {
            var input = ReadInput(item["input"]);
            result.Add(JsonText.Text(item["kind"]) == "event"
                ? new EventTriggerDeclaration(JsonText.Text(item["event"])!, JsonText.Text(item["businessKey"])!, input)
                : ReadSchedule(item, input));
        }

        return result.AsReadOnly();
    }

    private static ScheduleTriggerDeclaration ReadSchedule(JsonObject item, ReadOnlyDictionary<string, string> input)
    {
        var misfire = JsonText.Text(item["misfire"]) == "skip" ? ProcessMisfire.Skip : ProcessMisfire.FireOnce;
        return new ScheduleTriggerDeclaration(JsonText.Text(item["id"])!, JsonText.Text(item["cron"])!, JsonText.Text(item["zone"]), misfire, input);
    }

    private static ReadOnlyDictionary<string, string> ReadInput(JsonNode? node) =>
        new(new SortedDictionary<string, string>(
            (node as JsonObject ?? []).ToDictionary(entry => entry.Key, entry => JsonText.Text(entry.Value)!, StringComparer.Ordinal),
            StringComparer.Ordinal));

    private Dictionary<string, WindowDef> ReadWindows(JsonNode? node)
    {
        var result = new Dictionary<string, WindowDef>(StringComparer.Ordinal);
        foreach (var (name, window) in node as JsonObject ?? [])
        {
            result[name] = ReadWindow(name, window!.AsObject(), JsonText.Pointer("/body/windows", name));
        }

        return result;
    }

    // A window that is not valid is reported and still read, with what could be made of it: the document does not load.
    private WindowDef ReadWindow(string name, JsonObject window, string path)
    {
        string? zone = null;
        string? variable = null;
        if (window["zone"] is JsonObject reference)
        {
            variable = JsonText.Text(reference["variable"]);
        }
        else
        {
            zone = JsonText.Text(window["zone"]);
            if (!LocalTimes.IsZoneIdShape(zone))
            {
                errors.Add(Diagnostics.BadWindow(path + "/zone"));
            }
        }

        var days = ReadDays(window["days"], path + "/days");
        var from = ReadClock(window["from"], false, path + "/from");
        var to = ReadClock(window["to"], true, path + "/to");
        if (from is { } opens && to is { } closes && closes <= opens)
        {
            errors.Add(Diagnostics.BadWindow(path + "/to"));
        }

        return new WindowDef(name, zone, variable, days, ToClock(from ?? 0), ToClock(to ?? 0));
    }

    private List<DayOfWeek> ReadDays(JsonNode? node, string path)
    {
        var days = new List<DayOfWeek>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var valid = (node as JsonArray ?? []).Count > 0;
        foreach (var text in (node as JsonArray ?? []).Select(item => JsonText.Text(item)))
        {
            if (text is null || !WeekDays.TryGetValue(text, out var day) || !seen.Add(text))
            {
                valid = false;
            }
            else
            {
                days.Add(day);
            }
        }

        if (!valid)
        {
            errors.Add(Diagnostics.BadWindow(path));
        }

        return days;
    }

    // Minutes of the day of an HH:mm text; 24:00 (the end of the day) is 1440 and allowed for the closing time only.
    private int? ReadClock(JsonNode? node, bool closing, string path)
    {
        var match = JsonText.Text(node) is { } text ? ClockPattern().Match(text) : Match.Empty;
        if (match.Success)
        {
            var minutes = (int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 60)
                + int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (minutes < 24 * 60 || (closing && minutes == 24 * 60))
            {
                return minutes;
            }
        }

        errors.Add(Diagnostics.BadWindow(path));
        return null;
    }

    // The TimeOnly of the specification: 24:00 is written 00:00.
    private static TimeOnly ToClock(int minutes) => new((minutes / 60) % 24, minutes % 60);

    private static List<string> Strings(JsonNode? node) =>
        node is JsonArray array ? [.. array.Select(item => JsonText.Text(item)!)] : [];

    private static Mapping ReadMapping(JsonNode? node) =>
        node is JsonObject map
            ? new Mapping([.. map.Select(entry => new KeyValuePair<string, string>(entry.Key, JsonText.Text(entry.Value)!))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)])
            : Mapping.Empty;

    private static List<KeyValuePair<string, string>>? ReadTable(JsonNode? node) =>
        node is JsonObject map
            ? [.. map.Select(entry => new KeyValuePair<string, string>(entry.Key, JsonText.Text(entry.Value)!))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)]
            : null;

    private Dictionary<string, VariableDef> ReadVariables(JsonObject variables)
    {
        var result = new Dictionary<string, VariableDef>(StringComparer.Ordinal);
        foreach (var (name, node) in variables)
        {
            var declaration = node!.AsObject();
            var type = VariableValues.ParseType(JsonText.Text(declaration["type"]))!.Value;
            var path = JsonText.Pointer("/body/variables", name);
            var allowed = declaration["allowedValues"] is JsonArray values ? Strings(values) : null;
            var variable = new VariableDef(name, type, allowed, declaration["input"]?.GetValue<bool>() ?? false, declaration["initial"]);
            CheckAllowedValues(variable, declaration, path);
            if (declaration["initial"] is { } initial && !VariableValues.TryNormalize(variable, initial, out _))
            {
                errors.Add(Diagnostics.BadInitial(path + "/initial"));
            }

            result[name] = variable;
        }

        return result;
    }

    private void CheckAllowedValues(VariableDef variable, JsonObject declaration, string path)
    {
        var declared = declaration.ContainsKey("allowedValues");
        if (variable.Type != VariableType.Enum)
        {
            if (declared)
            {
                errors.Add(Diagnostics.BadInitial(path + "/allowedValues"));
            }
        }
        else if (!declared)
        {
            errors.Add(Diagnostics.BadInitial(path));
        }
        else if (variable.AllowedValues!.Count == 0)
        {
            errors.Add(Diagnostics.BadInitial(path + "/allowedValues"));
        }
    }

    private List<StepDef> ReadSteps(JsonObject steps)
    {
        var result = new List<StepDef>();
        foreach (var (id, node) in steps)
        {
            result.Add(ReadStep(id, node!.AsObject(), JsonText.Pointer("/body/steps", id)));
        }

        return result;
    }

    private StepDef ReadStep(string id, JsonObject step, string path)
    {
        var stage = JsonText.Text(step["stage"]);
        return JsonText.Text(step["kind"]) switch
        {
            "read" => new ReadStep(id, stage, Port(step), ReadMapping(step["input"]), ReadMapping(step["output"]), ReadRetry(step, path), Next(step), OnError(step), Route(step, "onNotAvailable"), Route(step, "onNotEntitled")),
            "action" => new ActionStep(id, stage, Port(step), ReadMapping(step["input"]), ReadMapping(step["output"]), ReadRetry(step, path), Next(step), OnError(step), Route(step, "onNotAvailable"), Route(step, "onNotEntitled"), JsonText.Text(step["delivery"]) == "atMostOnce", Route(step, "onUnknown"), step["contact"]?.GetValue<bool>() ?? false, JsonText.Text(step["window"]), ReadInterrupt(step["interruptBy"])),
            "ai" => ReadAi(id, stage, step, path),
            "decide" => ReadDecide(id, stage, step),
            "poll" => ReadPoll(id, stage, step, path),
            "humanTask" => new HumanTaskStep(id, stage, JsonText.Text(step["queue"])!, Strings(step["outcomes"]), Strings(step["show"]), ReadMapping(step["output"]), ReadTable(step["on"])!, ReadDuration(step, "timeout", path), Route(step, "onTimeout"), ReadTimeoutAt(step, path)),
            "wait" => new WaitStep(id, stage, JsonText.Text(step["event"])!, JsonText.Text(step["correlation"]), ReadMapping(step["output"]), Next(step), ReadDuration(step, "timeout", path), Route(step, "onTimeout"), ReadTimeoutAt(step, path)),
            "timer" => new TimerStep(id, stage, ReadDuration(step, "after", path), JsonText.Text(step["until"]), ReadDuration(step, "offset", path, signed: true), Next(step)),
            "loop" => new LoopStep(id, stage, step["maxIterations"]!.GetValue<int>(), Route(step, "body")!, Route(step, "onExhausted")!, JsonText.Text(step["forEach"]), JsonText.Text(step["item"]), Route(step, "next")),
            "subprocess" => new SubprocessStep(id, stage, JsonText.Text(step["process"])!, ReadMapping(step["input"]), ReadMapping(step["output"]), Route(step, "next"), ReadTable(step["on"]), OnError(step)),
            _ => new EndStep(id, stage, JsonText.Text(step["result"])!),
        };
    }

    // The object form of a timeout (a document of the schema has a text or an object there): the instant variable and the optional signed offset.
    private TimeoutAt? ReadTimeoutAt(JsonObject step, string path)
    {
        if (step["timeout"] is not JsonObject timeout)
        {
            return null;
        }

        var offset = ReadDuration(timeout, "offset", JsonText.Pointer(path, "timeout"), signed: true);
        return new TimeoutAt(JsonText.Text(timeout["until"])!, offset ?? TimeSpan.Zero);
    }

    private static InterruptDef? ReadInterrupt(JsonNode? node) =>
        node is JsonObject interrupt
            ? new InterruptDef(JsonText.Text(interrupt["event"])!, JsonText.Text(interrupt["correlation"]), ReadMapping(interrupt["output"]), Route(interrupt, "to")!)
            : null;

    private static string Port(JsonObject step) => JsonText.Text(step["port"])!;

    private static string Next(JsonObject step) => JsonText.Text(step["next"])!;

    private static string? OnError(JsonObject step) => JsonText.Text(step["onError"]);

    private static string? Route(JsonObject step, string member) => JsonText.Text(step[member]);

    private static DecideStep ReadDecide(string id, string? stage, JsonObject step) =>
        new(
            id,
            stage,
            JsonText.Text(step["rule"])!,
            ReadMapping(step["facts"]),
            ReadMapping(step["output"]),
            JsonText.Text(step["route"]),
            ReadTable(step["on"]),
            JsonText.Text(step["next"]),
            JsonText.Text(step["collectInto"]),
            JsonText.Text(step["onNoDecision"])!,
            OnError(step));

    private PollStep ReadPoll(string id, string? stage, JsonObject step, string path) =>
        new(
            id,
            stage,
            Port(step),
            ReadMapping(step["input"]),
            ReadMapping(step["output"]),
            ReadRetry(step, path),
            Next(step),
            OnError(step),
            Route(step, "onNotAvailable"),
            Route(step, "onNotEntitled"),
            JsonText.Text(step["rule"])!,
            ReadMapping(step["facts"]),
            JsonText.Text(step["field"])!,
            Strings(step["until"]),
            ReadDuration(step, "every", path) ?? TimeSpan.Zero,
            JsonText.Text(step["backoff"]) == "exponential",
            ReadDuration(step, "max", path),
            ReadDuration(step, "maxDuration", path) ?? TimeSpan.Zero,
            Route(step, "onExpired")!);

    private AiStep ReadAi(string id, string? stage, JsonObject step, string path)
    {
        var prompt = step["prompt"]!.AsObject();
        return new AiStep(
            id,
            stage,
            Port(step),
            ReadMapping(step["input"]),
            ReadMapping(step["output"]),
            ReadRetry(step, path),
            Next(step),
            OnError(step),
            Route(step, "onNotAvailable"),
            Route(step, "onNotEntitled"),
            JsonText.Text(step["model"])!,
            JsonText.Text(prompt["id"])!,
            long.Parse(prompt["version"]!.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture),
            BuildSchema(step["inputSchema"], path + "/inputSchema"),
            BuildSchema(step["outputSchema"], path + "/outputSchema"),
            step["maxAttempts"]?.GetValue<int>() ?? 1,
            JsonText.Text(step["onInvalid"])!);
    }

    private JsonSchema? BuildSchema(JsonNode? schema, string path)
    {
        try
        {
            var built = JsonSchema.FromText(schema!.ToJsonString(), new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            var check = MetaSchemas.Draft202012.Evaluate(JsonSerializer.SerializeToElement(schema), new EvaluationOptions { OutputFormat = OutputFormat.Flag });
            // The schema is proved by evaluating it once: a reference that does not resolve inside the schema throws here.
            _ = built.Evaluate(JsonSerializer.SerializeToElement(new JsonObject()), new EvaluationOptions { OutputFormat = OutputFormat.Flag });
            if (check.IsValid && ReferencesResolve(schema!))
            {
                return built;
            }
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or JsonSchemaException)
        {
            // The schema cannot be built: reported below.
        }

        errors.Add(Diagnostics.AiSchemaNotValid(path));
        return null;
    }

    // Schemas are self-contained: every $ref is a fragment that resolves inside the same schema object (no external reference).
    private static bool ReferencesResolve(JsonNode root)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var references = new List<string>();
        var pending = new Stack<JsonNode?>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case JsonObject map:
                    foreach (var (key, value) in map)
                    {
                        if (key == "$ref" && JsonText.Text(value) is { } reference)
                        {
                            references.Add(reference);
                        }
                        else if (key == "$anchor" && JsonText.Text(value) is { } anchor)
                        {
                            anchors.Add(anchor);
                        }

                        pending.Push(value);
                    }

                    break;
                case JsonArray list:
                    foreach (var item in list)
                    {
                        pending.Push(item);
                    }

                    break;
                default:
                    break;
            }
        }

        return references.All(reference => reference.StartsWith('#') && FragmentResolves(root, reference[1..], anchors));
    }

    private static bool FragmentResolves(JsonNode root, string fragment, HashSet<string> anchors)
    {
        if (fragment.Length == 0)
        {
            return true;
        }

        if (!fragment.StartsWith('/'))
        {
            return anchors.Contains(fragment);
        }

        var node = root;
        foreach (var part in Uri.UnescapeDataString(fragment).Split('/').Skip(1).Select(part => part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)))
        {
            node = node switch
            {
                JsonObject map => map[part],
                JsonArray list when int.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index) && index < list.Count => list[index],
                _ => null,
            };
            if (node is null)
            {
                return false;
            }
        }

        return true;
    }

    private RetryPolicy ReadRetry(JsonObject step, string path)
    {
        if (step["retry"] is not JsonObject retry)
        {
            return RetryPolicy.Once;
        }

        var retryPath = path + "/retry";
        return new RetryPolicy(
            retry["maxAttempts"]!.GetValue<int>(),
            JsonText.Text(retry["backoff"]) == "exponential",
            ReadDuration(retry, "initial", retryPath) ?? TimeSpan.Zero,
            ReadDuration(retry, "max", retryPath));
    }

    private TimeSpan? ReadDuration(JsonObject owner, string member, string path, bool signed = false)
    {
        if (JsonText.Text(owner[member]) is not { } text)
        {
            return null;
        }

        if (Durations.TryParse(text, signed, out var value))
        {
            return value;
        }

        errors.Add(Diagnostics.BadDuration(JsonText.Pointer(path, member)));
        return TimeSpan.Zero;
    }

    [GeneratedRegex(@"\A([01][0-9]|2[0-4]):([0-5][0-9])\z", RegexOptions.CultureInvariant)]
    private static partial Regex ClockPattern();
}

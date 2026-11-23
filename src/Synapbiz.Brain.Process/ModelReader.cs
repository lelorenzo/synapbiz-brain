using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process;

/// <summary>
/// Reads a document that passed the schema into a <see cref="ProcessDefinition"/>. The schema guarantees the shape of
/// every member; what the schema cannot say (durations, initial values, JSON Schemas of ai steps) is checked here and
/// reported into <c>errors</c>.
/// </summary>
internal sealed class ModelReader(List<BrainSpecError> errors)
{
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
            Scenarios = body["scenarios"] is JsonArray scenarios ? (JsonArray)scenarios.DeepClone() : [],
        };
    }

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
            "action" => new ActionStep(id, stage, Port(step), ReadMapping(step["input"]), ReadMapping(step["output"]), ReadRetry(step, path), Next(step), OnError(step), Route(step, "onNotAvailable"), Route(step, "onNotEntitled"), JsonText.Text(step["delivery"]) == "atMostOnce", Route(step, "onUnknown")),
            "ai" => ReadAi(id, stage, step, path),
            "decide" => ReadDecide(id, stage, step),
            "humanTask" => new HumanTaskStep(id, stage, JsonText.Text(step["queue"])!, Strings(step["outcomes"]), Strings(step["show"]), ReadMapping(step["output"]), ReadTable(step["on"])!, ReadDuration(step, "timeout", path), Route(step, "onTimeout")),
            "wait" => new WaitStep(id, stage, JsonText.Text(step["event"])!, JsonText.Text(step["correlation"]), ReadMapping(step["output"]), Next(step), ReadDuration(step, "timeout", path), Route(step, "onTimeout")),
            "timer" => new TimerStep(id, stage, ReadDuration(step, "after", path), JsonText.Text(step["until"]), ReadDuration(step, "offset", path, signed: true), Next(step)),
            "loop" => new LoopStep(id, stage, step["maxIterations"]!.GetValue<int>(), Route(step, "body")!, Route(step, "onExhausted")!, JsonText.Text(step["forEach"]), JsonText.Text(step["item"]), Route(step, "next")),
            "subprocess" => new SubprocessStep(id, stage, JsonText.Text(step["process"])!, ReadMapping(step["input"]), ReadMapping(step["output"]), Route(step, "next"), ReadTable(step["on"]), OnError(step)),
            _ => new EndStep(id, stage, JsonText.Text(step["result"])!),
        };
    }

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
            if (check.IsValid)
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
}

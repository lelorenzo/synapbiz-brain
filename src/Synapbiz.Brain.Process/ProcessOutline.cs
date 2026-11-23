using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process;

/// <summary>Read-only lists for the outline: a wrapper that cannot be cast back to a <see cref="List{T}"/> or an array and be changed.</summary>
internal static class OutlineLists
{
    /// <summary>A read-only copy of the items.</summary>
    internal static IReadOnlyList<T> Of<T>(IEnumerable<T> items) => new ReadOnlyCollection<T>([.. items]);

    /// <summary>The empty read-only list.</summary>
    internal static IReadOnlyList<T> Empty<T>() => EmptyList<T>.Value;

    private static class EmptyList<T>
    {
        internal static readonly IReadOnlyList<T> Value = new ReadOnlyCollection<T>([]);
    }
}

/// <summary>The kinds of steps of a process document, as the outline reports them.</summary>
public enum OutlineStepKind
{
    /// <summary>A <c>read</c> step: asks a port for information.</summary>
    Read,

    /// <summary>An <c>action</c> step: asks a port to do something.</summary>
    Action,

    /// <summary>An <c>ai</c> step: asks a model through a port, under a pinned prompt and an output schema.</summary>
    Ai,

    /// <summary>A <c>poll</c> step: reads a port again and again until a rule says it is done.</summary>
    Poll,

    /// <summary>A <c>decide</c> step: a rule judges facts.</summary>
    Decide,

    /// <summary>A <c>humanTask</c> step: a person chooses an outcome.</summary>
    HumanTask,

    /// <summary>A <c>wait</c> step: waits for an event.</summary>
    Wait,

    /// <summary>A <c>timer</c> step: waits for a time.</summary>
    Timer,

    /// <summary>A <c>loop</c> step: a bounded repetition.</summary>
    Loop,

    /// <summary>A <c>subprocess</c> step: starts a child process and waits for its result.</summary>
    Subprocess,

    /// <summary>An <c>end</c> step: ends the case with a result.</summary>
    End,
}

/// <summary>
/// One way out of a step. The label is the name of the member of the step that holds the route (<c>next</c>, <c>body</c>,
/// <c>onError</c>, <c>onTimeout</c> and so on), <c>on:&lt;value&gt;</c> for an entry of <c>on</c>, and <c>interrupt</c> for the
/// <c>to</c> of an <c>interruptBy</c>.
/// </summary>
/// <param name="Label">The label of the route.</param>
/// <param name="Target">The id of the step the route leads to.</param>
public sealed record OutlineRoute(string Label, string Target);

/// <summary>One entry of a mapping: a member of the object a port or a rule takes or gives, and the process variable it is tied to.</summary>
/// <param name="Name">The member name on the port or rule side.</param>
/// <param name="Variable">The process variable.</param>
public sealed record OutlineBinding(string Name, string Variable);

/// <summary>The retry policy of an effect step.</summary>
/// <param name="MaxAttempts">The number of attempts in all, at least 1.</param>
/// <param name="Backoff"><c>none</c> for one attempt, else <c>fixed</c> or <c>exponential</c>.</param>
/// <param name="Initial">The first delay.</param>
/// <param name="Max">The longest delay, when the document caps it.</param>
public sealed record OutlineRetry(int MaxAttempts, string Backoff, TimeSpan Initial, TimeSpan? Max);

/// <summary>The timeout of a <c>wait</c> or <c>humanTask</c>: a duration, or the value of an <c>Instant</c> variable plus an offset.</summary>
/// <param name="Duration">The duration, or null for a timeout tied to an instant.</param>
/// <param name="Variable">The <c>Instant</c> variable, or null for a duration.</param>
/// <param name="Offset">The signed offset from the instant of the variable; null for a duration.</param>
public sealed record OutlineTimeout(TimeSpan? Duration, string? Variable, TimeSpan? Offset);

/// <summary>The <c>interruptBy</c> of an action that names a delivery window.</summary>
/// <param name="Event">The event that ends the wait for the window.</param>
/// <param name="Correlation">The variable that holds the correlation, or null.</param>
/// <param name="Target">The step the flow goes to.</param>
/// <param name="Output">What the event payload fills.</param>
public sealed record OutlineInterrupt(string Event, string? Correlation, string Target, IReadOnlyList<OutlineBinding> Output);

/// <summary>The delay of a <c>timer</c> step.</summary>
/// <param name="After">The duration, or null for a timer tied to an instant.</param>
/// <param name="Variable">The <c>Instant</c> variable, or null for a duration.</param>
/// <param name="Offset">The signed offset from the instant of the variable; null for a duration.</param>
public sealed record OutlineTimer(TimeSpan? After, string? Variable, TimeSpan? Offset);

/// <summary>The bound of a <c>loop</c> step.</summary>
/// <param name="MaxIterations">The most rounds.</param>
/// <param name="ForEach">The list variable the loop walks, or null.</param>
/// <param name="Item">The variable that holds the current element, or null.</param>
public sealed record OutlineLoop(int MaxIterations, string? ForEach, string? Item);

/// <summary>The timing of a <c>poll</c> step.</summary>
/// <param name="Field">The field of the rule outcome that ends the poll.</param>
/// <param name="Until">The values of that field that end the poll.</param>
/// <param name="Every">The first interval.</param>
/// <param name="Backoff"><c>fixed</c> or <c>exponential</c>.</param>
/// <param name="Max">The longest interval, when the document caps it.</param>
/// <param name="MaxDuration">The longest time the poll may last.</param>
public sealed record OutlinePoll(string Field, IReadOnlyList<string> Until, TimeSpan Every, string Backoff, TimeSpan? Max, TimeSpan MaxDuration);

/// <summary>A declared variable of the document.</summary>
/// <param name="Name">The variable name.</param>
/// <param name="Type">The name of the type (<c>Code</c>, <c>Enum</c>, <c>Boolean</c>, <c>Integer</c>, <c>Decimal</c>, <c>Money</c>, <c>Date</c>, <c>Reference</c>, <c>Instant</c> or <c>Json</c>).</param>
/// <param name="IsInput">True when a case may be started with a value for it.</param>
public sealed record OutlineVariable(string Name, string Type, bool IsInput);

/// <summary>A delivery window of the document.</summary>
/// <param name="Name">The name of the window.</param>
/// <param name="Zone">The zone id, or null when the zone comes from a variable.</param>
/// <param name="ZoneVariable">The variable that holds the zone id, or null.</param>
/// <param name="Days">The days of the week the window is open.</param>
/// <param name="From">The opening time of the day.</param>
/// <param name="To">The closing time of the day; 00:00 means the end of the day.</param>
public sealed record OutlineWindow(string Name, string? Zone, string? ZoneVariable, IReadOnlyList<DayOfWeek> Days, TimeOnly From, TimeOnly To);

/// <summary>
/// A step of the document, in plain data. The members that belong to one kind of step are null (or empty) on the others. The
/// text members are ids and names exactly as the document writes them.
/// </summary>
public sealed record OutlineStep
{
    /// <summary>The id of the step.</summary>
    public required string Id { get; init; }

    /// <summary>The kind of step.</summary>
    public required OutlineStepKind Kind { get; init; }

    /// <summary>The stage the step belongs to, or null.</summary>
    public string? Stage { get; init; }

    /// <summary>Every way out of the step, in the order the interpreter's edge enumerator gives them.</summary>
    public IReadOnlyList<OutlineRoute> Routes { get; init; } = OutlineLists.Empty<OutlineRoute>();

    /// <summary>The port of a <c>read</c>, <c>action</c>, <c>ai</c> or <c>poll</c> step.</summary>
    public string? Port { get; init; }

    /// <summary>The delivery guarantee of an <c>action</c>.</summary>
    public ProcessDelivery? Delivery { get; init; }

    /// <summary>True when an <c>action</c> contacts a person; null on the other steps.</summary>
    public bool? IsContact { get; init; }

    /// <summary>The delivery window an <c>action</c> waits for.</summary>
    public string? Window { get; init; }

    /// <summary>The retry policy of an effect step (<c>read</c>, <c>action</c>, <c>ai</c>, <c>poll</c>).</summary>
    public OutlineRetry? Retry { get; init; }

    /// <summary>The rule a <c>decide</c> or <c>poll</c> step uses, as the document writes its id.</summary>
    public string? Rule { get; init; }

    /// <summary>The outcome field a <c>decide</c> routes on, or null.</summary>
    public string? RouteField { get; init; }

    /// <summary>The variable a <c>decide</c> collects every matching outcome into, or null.</summary>
    public string? CollectInto { get; init; }

    /// <summary>The queue of a <c>humanTask</c>.</summary>
    public string? Queue { get; init; }

    /// <summary>The outcomes a person may choose in a <c>humanTask</c>.</summary>
    public IReadOnlyList<string>? Outcomes { get; init; }

    /// <summary>The variables a <c>humanTask</c> shows to the person.</summary>
    public IReadOnlyList<string>? Show { get; init; }

    /// <summary>The event a <c>wait</c> waits for.</summary>
    public string? Event { get; init; }

    /// <summary>The variable whose value an event must carry as its correlation, for a <c>wait</c>.</summary>
    public string? Correlation { get; init; }

    /// <summary>The timeout of a <c>wait</c> or <c>humanTask</c>.</summary>
    public OutlineTimeout? Timeout { get; init; }

    /// <summary>The <c>interruptBy</c> of an <c>action</c> with a window.</summary>
    public OutlineInterrupt? Interrupt { get; init; }

    /// <summary>The delay of a <c>timer</c>.</summary>
    public OutlineTimer? Timer { get; init; }

    /// <summary>The bound of a <c>loop</c>.</summary>
    public OutlineLoop? Loop { get; init; }

    /// <summary>The timing of a <c>poll</c>.</summary>
    public OutlinePoll? Poll { get; init; }

    /// <summary>The child process a <c>subprocess</c> starts, as the document writes it.</summary>
    public string? Process { get; init; }

    /// <summary>The result of an <c>end</c> step.</summary>
    public string? Result { get; init; }

    /// <summary>The input mapping of the step: what it sends to its port or child, by member name.</summary>
    public IReadOnlyList<OutlineBinding> Inputs { get; init; } = OutlineLists.Empty<OutlineBinding>();

    /// <summary>The facts mapping of a <c>decide</c> or <c>poll</c> step: what it gives its rule, by fact name.</summary>
    public IReadOnlyList<OutlineBinding> Facts { get; init; } = OutlineLists.Empty<OutlineBinding>();

    /// <summary>The output mapping of the step: what it keeps, by member name.</summary>
    public IReadOnlyList<OutlineBinding> Outputs { get; init; } = OutlineLists.Empty<OutlineBinding>();

    /// <summary>The names of the variables the step reads, in order of first use.</summary>
    public IReadOnlyList<string> InputNames { get; init; } = OutlineLists.Empty<string>();

    /// <summary>The names of the variables the step writes, in order of first use.</summary>
    public IReadOnlyList<string> OutputNames { get; init; } = OutlineLists.Empty<string>();

    /// <summary>The model an <c>ai</c> step is pinned to.</summary>
    public string? Model { get; init; }

    /// <summary>The id of the prompt an <c>ai</c> step is pinned to.</summary>
    public string? PromptId { get; init; }

    /// <summary>The version of the prompt an <c>ai</c> step is pinned to.</summary>
    public long? PromptVersion { get; init; }

    /// <summary>How many answers an <c>ai</c> step may ask for when the answer does not fit its output schema.</summary>
    public int? MaxAttempts { get; init; }

    /// <summary>True when an <c>ai</c> step declares an input schema.</summary>
    public bool HasInputSchema { get; init; }

    /// <summary>True when an <c>ai</c> step declares an output schema.</summary>
    public bool HasOutputSchema { get; init; }

    /// <summary>The input schema of an <c>ai</c> step as canonical JSON (compact, members in ordinal order), or null.</summary>
    public string? InputSchemaJson { get; init; }

    /// <summary>The output schema of an <c>ai</c> step as canonical JSON (compact, members in ordinal order), or null.</summary>
    public string? OutputSchemaJson { get; init; }
}

/// <summary>
/// A language-free, immutable description of a loaded process document, for a screen or a report: what it declares and,
/// step by step, where it can go. It only reads the document; it changes nothing about how the document runs. The routes of a
/// step come from the same enumerator the validator and the reachability check use, so a new kind of edge cannot be missed.
/// </summary>
public sealed record ProcessOutline
{
    // The smallest document the format schema accepts, to hold one scenario at the path /body/scenarios/0.
    private const string SchemaEnvelope =
        """
        {"$spec":"brain.process@1","id":"scenario.check","version":1,"uses":[],"requires":[],"meta":{},
         "body":{"subject":"case","stages":[],"variables":{},"start":"end","steps":{"end":{"kind":"end","result":"done"}},"scenarios":[]}}
        """;

    private readonly IReadOnlyList<KeyValuePair<string, string>> scenarioTexts;

    private ProcessOutline(IReadOnlyList<KeyValuePair<string, string>> scenarioTexts)
    {
        this.scenarioTexts = scenarioTexts;
    }

    /// <summary>The id of the document.</summary>
    public required string Id { get; init; }

    /// <summary>The version of the document.</summary>
    public required long Version { get; init; }

    /// <summary>The content hash of the document text that was loaded.</summary>
    public required string ContentHash { get; init; }

    /// <summary>The case type the process runs for.</summary>
    public required string Subject { get; init; }

    /// <summary>The stages, in document order.</summary>
    public required IReadOnlyList<string> Stages { get; init; }

    /// <summary>The id of the first step.</summary>
    public required string Start { get; init; }

    /// <summary>The events the process accepts at any step, in ordinal order.</summary>
    public required IReadOnlyList<string> Accepts { get; init; }

    /// <summary>The rule references of the document (<c>module/rule</c>), in document order.</summary>
    public required IReadOnlyList<string> Uses { get; init; }

    /// <summary>The module contracts the document requires (<c>module@major</c>), in document order.</summary>
    public required IReadOnlyList<string> Requires { get; init; }

    /// <summary>The declared variables, in document order.</summary>
    public required IReadOnlyList<OutlineVariable> Variables { get; init; }

    /// <summary>The delivery windows, in document order.</summary>
    public required IReadOnlyList<OutlineWindow> Windows { get; init; }

    /// <summary>The triggers the document declares, in document order.</summary>
    public required IReadOnlyList<ProcessTriggerDeclaration> Triggers { get; init; }

    /// <summary>The names of the embedded scenarios, in document order.</summary>
    public required IReadOnlyList<string> Scenarios { get; init; }

    /// <summary>The steps, in document order.</summary>
    public required IReadOnlyList<OutlineStep> Steps { get; init; }

    /// <summary>Describes a loaded process.</summary>
    /// <param name="process">The loaded process.</param>
    public static ProcessOutline From(BrainProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var definition = process.Definition;
        var texts = definition.Scenarios.Select(scenario => new KeyValuePair<string, string>(JsonText.Text(scenario!["name"])!, JsonText.Canonical(scenario))).ToList();
        return new ProcessOutline(texts)
        {
            Id = definition.Id,
            Version = definition.Version,
            ContentHash = definition.Hash,
            Subject = definition.Subject,
            Stages = OutlineLists.Of(definition.Stages),
            Start = definition.Start,
            Accepts = OutlineLists.Of(definition.Accepts.Order(StringComparer.Ordinal)),
            Uses = OutlineLists.Of(definition.Uses.OrderBy(use => use.Index).Select(use => use.Reference)),
            Requires = OutlineLists.Of(definition.Requires),
            Variables = OutlineLists.Of(definition.Variables.Values.Select(variable => new OutlineVariable(variable.Name, variable.Type.ToString(), variable.Input))),
            Windows = OutlineLists.Of(definition.Windows.Values.Select(window => new OutlineWindow(window.Name, window.Zone, window.ZoneVariable, OutlineLists.Of(window.Days), window.From, window.To))),
            Triggers = OutlineLists.Of(definition.Triggers),
            Scenarios = OutlineLists.Of(texts.Select(text => text.Key)),
            Steps = OutlineLists.Of(definition.Steps.Select(Describe)),
        };
    }

    /// <summary>The step with an id, or null.</summary>
    /// <param name="id">The id of the step.</param>
    public OutlineStep? Step(string id) => Steps.FirstOrDefault(step => string.Equals(step.Id, id, StringComparison.Ordinal));

    /// <summary>The canonical JSON (compact, members in ordinal order) of the first embedded scenario with a name, or null when there is none.</summary>
    /// <param name="name">The name of the scenario.</param>
    public string? ScenarioJson(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return scenarioTexts.Where(item => string.Equals(item.Key, name, StringComparison.Ordinal)).Select(item => item.Value).FirstOrDefault();
    }

    /// <summary>
    /// Checks one scenario in the embedded format against the document, with the same rules the loader applies to the embedded
    /// ones: the shape of the format schema first, then what the schema cannot say (a step of the right kind, a known event, a
    /// known variable, an outcome of the task). The paths are relative to the scenario, for example <c>/start/at</c>. The
    /// document is not changed. An empty list means the scenario would load.
    /// </summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="scenario">The scenario object.</param>
    public static IReadOnlyList<BrainSpecError> CheckScenario(BrainProcess process, JsonObject scenario)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(scenario);

        const string prefix = "/body/scenarios/0";
        var copy = (JsonObject)scenario.DeepClone();
        var errors = new List<BrainSpecError>();
        var envelope = JsonNode.Parse(SchemaEnvelope)!.AsObject();
        envelope["body"]!["scenarios"] = new JsonArray(copy.DeepClone());
        errors.AddRange(ProcessSchema.Violations(envelope).Select(Diagnostics.Schema));
        if (errors.Count == 0)
        {
            var definition = process.Definition;
            var single = new ProcessDefinition
            {
                Id = definition.Id,
                Version = definition.Version,
                Hash = definition.Hash,
                Subject = definition.Subject,
                Stages = definition.Stages,
                Variables = definition.Variables,
                Accepts = definition.Accepts,
                MaxSteps = definition.MaxSteps,
                Start = definition.Start,
                Steps = definition.Steps,
                StepsById = definition.StepsById,
                Uses = definition.Uses,
                Meta = definition.Meta,
                Scenarios = new JsonArray(copy),
                Triggers = definition.Triggers,
                Windows = definition.Windows,
            };
            new ScenarioValidator(single, errors).Validate();
        }

        return [.. errors
            .Select(error => error with { Path = error.Path.StartsWith(prefix, StringComparison.Ordinal) ? error.Path[prefix.Length..] : error.Path })
            .Distinct()
            .OrderBy(error => error.Path, StringComparer.Ordinal)
            .ThenBy(error => error.Code, StringComparer.Ordinal)];
    }

    private static OutlineStep Describe(StepDef step)
    {
        var routes = OutlineLists.Of(step.References().Select(reference => new OutlineRoute(Label(reference.Member), reference.Target)));
        var outline = new OutlineStep
        {
            Id = step.Id,
            Kind = KindOf(step.Kind),
            Stage = step.Stage,
            Routes = routes,
        };
        return step switch
        {
            PollStep poll => DescribePoll(outline, poll),
            AiStep ai => DescribeAi(outline, ai),
            ActionStep action => DescribeAction(outline, action),
            EffectStep effect => DescribeEffect(outline, effect),
            DecideStep decide => DescribeDecide(outline, decide),
            HumanTaskStep task => DescribeTask(outline, task),
            WaitStep wait => DescribeWait(outline, wait),
            TimerStep timer => DescribeTimer(outline, timer),
            LoopStep loop => DescribeLoop(outline, loop),
            SubprocessStep subprocess => DescribeSubprocess(outline, subprocess),
            EndStep end => outline with { Result = end.Result },
            _ => outline,
        };
    }

    private static OutlineStep DescribeEffect(OutlineStep outline, EffectStep step) => outline with
    {
        Port = step.Port,
        Retry = RetryOf(step.Retry),
        Inputs = Bindings(step.Input),
        Outputs = Bindings(step.Output),
        InputNames = Distinct(step.Input.Entries.Select(entry => entry.Value)),
        OutputNames = Distinct(step.Output.Entries.Select(entry => entry.Value)),
    };

    private static OutlineStep DescribeAction(OutlineStep outline, ActionStep step)
    {
        var effect = DescribeEffect(outline, step);
        return effect with
        {
            Delivery = step.AtMostOnce ? ProcessDelivery.AtMostOnce : ProcessDelivery.AtLeastOnce,
            IsContact = step.Contact,
            Window = step.Window,
            Interrupt = step.Interrupt is { } interrupt
                ? new OutlineInterrupt(interrupt.Event, interrupt.Correlation, interrupt.To, Bindings(interrupt.Output))
                : null,
            InputNames = Distinct(effect.InputNames.Concat(Present(step.Interrupt?.Correlation))),
            OutputNames = Distinct(effect.OutputNames.Concat((step.Interrupt?.Output.Entries ?? []).Select(entry => entry.Value))),
        };
    }

    private static OutlineStep DescribeAi(OutlineStep outline, AiStep step)
    {
        var effect = DescribeEffect(outline, step);
        return effect with
        {
            Model = step.Model,
            PromptId = step.PromptId,
            PromptVersion = step.PromptVersion,
            MaxAttempts = step.MaxAttempts,
            HasInputSchema = step.InputSchema is not null,
            HasOutputSchema = step.OutputSchema is not null,
            InputSchemaJson = SchemaText(step.InputSchema),
            OutputSchemaJson = SchemaText(step.OutputSchema),
        };
    }

    private static OutlineStep DescribePoll(OutlineStep outline, PollStep step)
    {
        var effect = DescribeEffect(outline, step);
        return effect with
        {
            Rule = step.Rule,
            Facts = Bindings(step.Facts),
            InputNames = Distinct(effect.InputNames.Concat(step.Facts.Entries.Select(entry => entry.Value))),
            Poll = new OutlinePoll(step.Field, OutlineLists.Of(step.Until), step.Every, step.Exponential ? "exponential" : "fixed", step.Max, step.MaxDuration),
        };
    }

    private static OutlineStep DescribeDecide(OutlineStep outline, DecideStep step) => outline with
    {
        Rule = step.Rule,
        RouteField = step.Route,
        CollectInto = step.CollectInto,
        Facts = Bindings(step.Facts),
        Outputs = Bindings(step.Output),
        InputNames = Distinct(step.Facts.Entries.Select(entry => entry.Value)),
        OutputNames = Distinct(step.Output.Entries.Select(entry => entry.Value).Concat(Present(step.CollectInto))),
    };

    private static OutlineStep DescribeTask(OutlineStep outline, HumanTaskStep step) => outline with
    {
        Queue = step.Queue,
        Outcomes = OutlineLists.Of(step.Outcomes),
        Show = OutlineLists.Of(step.Show),
        Timeout = TimeoutOf(step.Timeout, step.TimeoutAt),
        Outputs = Bindings(step.Output),
        InputNames = Distinct(step.Show.Concat(Present(step.TimeoutAt?.Variable))),
        OutputNames = Distinct(step.Output.Entries.Select(entry => entry.Value)),
    };

    private static OutlineStep DescribeWait(OutlineStep outline, WaitStep step) => outline with
    {
        Event = step.Event,
        Correlation = step.Correlation,
        Timeout = TimeoutOf(step.Timeout, step.TimeoutAt),
        Outputs = Bindings(step.Output),
        InputNames = Distinct(Present(step.Correlation).Concat(Present(step.TimeoutAt?.Variable))),
        OutputNames = Distinct(step.Output.Entries.Select(entry => entry.Value)),
    };

    private static OutlineStep DescribeTimer(OutlineStep outline, TimerStep step) => outline with
    {
        Timer = new OutlineTimer(step.After, step.Until, step.Until is null ? null : step.Offset ?? TimeSpan.Zero),
        InputNames = Distinct(Present(step.Until)),
    };

    private static OutlineStep DescribeLoop(OutlineStep outline, LoopStep step) => outline with
    {
        Loop = new OutlineLoop(step.MaxIterations, step.ForEach, step.Item),
        InputNames = Distinct(Present(step.ForEach)),
        OutputNames = Distinct(Present(step.Item)),
    };

    private static OutlineStep DescribeSubprocess(OutlineStep outline, SubprocessStep step) => outline with
    {
        Process = step.Process,
        Inputs = Bindings(step.Input),
        Outputs = Bindings(step.Output),
        InputNames = Distinct(step.Input.Entries.Select(entry => entry.Value)),
        OutputNames = Distinct(step.Output.Entries.Select(entry => entry.Value)),
    };

    private static OutlineTimeout? TimeoutOf(TimeSpan? duration, TimeoutAt? at)
    {
        if (at is not null)
        {
            return new OutlineTimeout(null, at.Variable, at.Offset);
        }

        return duration is { } span ? new OutlineTimeout(span, null, null) : null;
    }

    private static OutlineRetry RetryOf(RetryPolicy policy)
    {
        var backoff = policy.Exponential ? "exponential" : "fixed";
        return new OutlineRetry(policy.MaxAttempts, policy.MaxAttempts == 1 ? "none" : backoff, policy.Initial, policy.Max);
    }

    private static IEnumerable<string> Present(string? name) => name is null ? [] : [name];

    private static IReadOnlyList<OutlineBinding> Bindings(Mapping mapping) =>
        OutlineLists.Of(mapping.Entries.Select(entry => new OutlineBinding(entry.Key, entry.Value)));

    private static IReadOnlyList<string> Distinct(IEnumerable<string> names) => OutlineLists.Of(names.Distinct(StringComparer.Ordinal));

    private static string? SchemaText(Json.Schema.JsonSchema? schema) =>
        schema is null ? null : JsonText.Canonical(JsonNode.Parse(JsonSerializer.Serialize(schema)));

    // The pointer of a reference, relative to the step, as the label of its route.
    private static string Label(string member)
    {
        if (member.StartsWith("/on/", StringComparison.Ordinal))
        {
            return "on:" + Unescape(member["/on/".Length..]);
        }

        return string.Equals(member, "/interruptBy/to", StringComparison.Ordinal) ? "interrupt" : member.TrimStart('/');
    }

    private static string Unescape(string token) =>
        token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

    private static OutlineStepKind KindOf(StepKind kind) => kind switch
    {
        StepKind.Read => OutlineStepKind.Read,
        StepKind.Action => OutlineStepKind.Action,
        StepKind.Ai => OutlineStepKind.Ai,
        StepKind.Poll => OutlineStepKind.Poll,
        StepKind.Decide => OutlineStepKind.Decide,
        StepKind.HumanTask => OutlineStepKind.HumanTask,
        StepKind.Wait => OutlineStepKind.Wait,
        StepKind.Timer => OutlineStepKind.Timer,
        StepKind.Loop => OutlineStepKind.Loop,
        StepKind.Subprocess => OutlineStepKind.Subprocess,
        StepKind.End => OutlineStepKind.End,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown step kind."),
    };
}

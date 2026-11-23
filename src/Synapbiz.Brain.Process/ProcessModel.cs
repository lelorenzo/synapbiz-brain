using System.Text.Json.Nodes;
using Json.Schema;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process;

/// <summary>The kinds of steps of the document.</summary>
internal enum StepKind
{
    Read,
    Action,
    Ai,
    Decide,
    HumanTask,
    Wait,
    Timer,
    Loop,
    Subprocess,
    End,
    Poll,
}

/// <summary>A reference from a step to another step: the target and the pointer of the member that holds it, relative to the step.</summary>
internal readonly record struct StepRef(string Target, string Member);

/// <summary>A mapping of the document: pairs of names in ordinal order of the first name.</summary>
internal sealed record Mapping(IReadOnlyList<KeyValuePair<string, string>> Entries)
{
    internal static Mapping Empty { get; } = new([]);

    internal bool IsEmpty => Entries.Count == 0;
}

/// <summary>A retry policy of an effect step.</summary>
internal sealed record RetryPolicy(int MaxAttempts, bool Exponential, TimeSpan Initial, TimeSpan? Max)
{
    /// <summary>One attempt: the policy of a step that declares no <c>retry</c>.</summary>
    internal static RetryPolicy Once { get; } = new(1, false, TimeSpan.Zero, null);
}

/// <summary>A step of the document.</summary>
internal abstract record StepDef(string Id, string? Stage)
{
    internal abstract StepKind Kind { get; }

    /// <summary>Every step reference of the step, with the member that holds it.</summary>
    internal abstract IEnumerable<StepRef> References();

    /// <summary>Every variable the step names, with the member that names it relative to the step.</summary>
    internal virtual IEnumerable<(string Variable, string Member)> VariableUses() => [];

    /// <summary>The reference of an optional member.</summary>
    protected static IEnumerable<StepRef> Optional(string? target, string member) =>
        target is null ? [] : [new StepRef(target, JsonText.Pointer("", member))];

    /// <summary>The references of a routing table.</summary>
    protected static IEnumerable<StepRef> Table(IReadOnlyList<KeyValuePair<string, string>>? table, string member) =>
        (table ?? []).Select(entry => new StepRef(entry.Value, JsonText.Pointer(JsonText.Pointer("", member), entry.Key)));

    /// <summary>The variables of a mapping, with the member that names each.</summary>
    protected static IEnumerable<(string Variable, string Member)> MappedVariables(Mapping mapping, string member) =>
        mapping.Entries.Select(entry => (entry.Value, JsonText.Pointer(JsonText.Pointer("", member), entry.Key)));
}

/// <summary>A <c>read</c>, <c>action</c> or <c>ai</c> step: it asks a port and waits for the result.</summary>
internal abstract record EffectStep(
    string Id,
    string? Stage,
    string Port,
    Mapping Input,
    Mapping Output,
    RetryPolicy Retry,
    string Next,
    string? OnError,
    string? OnNotAvailable,
    string? OnNotEntitled,
    string? OnUnknown) : StepDef(Id, Stage)
{
    internal override IEnumerable<StepRef> References() => EffectReferences();

    internal override IEnumerable<(string Variable, string Member)> VariableUses() =>
        MappedVariables(Input, "input").Concat(MappedVariables(Output, "output"));

    protected IEnumerable<StepRef> EffectReferences() =>
        Optional(Next, "next")
            .Concat(Optional(OnError, "onError"))
            .Concat(Optional(OnNotAvailable, "onNotAvailable"))
            .Concat(Optional(OnNotEntitled, "onNotEntitled"))
            .Concat(Optional(OnUnknown, "onUnknown"));
}

/// <summary>A <c>read</c> step.</summary>
internal sealed record ReadStep(
    string Id, string? Stage, string Port, Mapping Input, Mapping Output, RetryPolicy Retry, string Next,
    string? OnError, string? OnNotAvailable, string? OnNotEntitled)
    : EffectStep(Id, Stage, Port, Input, Output, Retry, Next, OnError, OnNotAvailable, OnNotEntitled, null)
{
    internal override StepKind Kind => StepKind.Read;
}

/// <summary>An <c>action</c> step.</summary>
internal sealed record ActionStep(
    string Id, string? Stage, string Port, Mapping Input, Mapping Output, RetryPolicy Retry, string Next,
    string? OnError, string? OnNotAvailable, string? OnNotEntitled, bool AtMostOnce, string? OnUnknown,
    bool Contact = false, string? Window = null)
    : EffectStep(Id, Stage, Port, Input, Output, Retry, Next, OnError, OnNotAvailable, OnNotEntitled, OnUnknown)
{
    internal override StepKind Kind => StepKind.Action;
}

/// <summary>
/// A <c>poll</c> step: it reads a port, evaluates a rule on the result and, until the rule's field takes one of the
/// <see cref="Until"/> values, reads again after an interval that is fixed or doubles each round, until the time is up.
/// </summary>
internal sealed record PollStep(
    string Id, string? Stage, string Port, Mapping Input, Mapping Output, RetryPolicy Retry, string Next,
    string? OnError, string? OnNotAvailable, string? OnNotEntitled, string Rule, Mapping Facts, string Field,
    IReadOnlyList<string> Until, TimeSpan Every, bool Exponential, TimeSpan? Max, TimeSpan MaxDuration, string OnExpired)
    : EffectStep(Id, Stage, Port, Input, Output, Retry, Next, OnError, OnNotAvailable, OnNotEntitled, null)
{
    // Beyond this an interval is longer than any poll can last, so doubling stops: the arithmetic never overflows.
    private static readonly long Saturation = TimeSpan.FromDays(4 * 3660).Ticks;

    internal override StepKind Kind => StepKind.Poll;

    internal override IEnumerable<StepRef> References() => EffectReferences().Concat(Optional(OnExpired, "onExpired"));

    internal override IEnumerable<(string Variable, string Member)> VariableUses() =>
        base.VariableUses().Concat(MappedVariables(Facts, "facts"));

    /// <summary>The wait after round <paramref name="round"/> (from 1): <c>every</c>, doubled each round when exponential, never above <c>max</c>.</summary>
    internal TimeSpan IntervalAfter(int round)
    {
        var ticks = Every.Ticks;
        for (var doubling = 1; Exponential && doubling < round && ticks < Saturation; doubling++)
        {
            ticks *= 2;
        }

        if (Max is { } cap && ticks > cap.Ticks)
        {
            ticks = cap.Ticks;
        }

        return TimeSpan.FromTicks(Math.Min(ticks, Saturation));
    }

    /// <summary>The number of rounds that fit in <c>maxDuration</c>, counted up to <paramref name="stopAfter"/> + 1.</summary>
    internal int CountRounds(int stopAfter)
    {
        var rounds = 1;
        var elapsed = TimeSpan.Zero;
        while (rounds <= stopAfter)
        {
            elapsed += IntervalAfter(rounds);
            if (elapsed > MaxDuration)
            {
                break;
            }

            rounds++;
        }

        return rounds;
    }
}

/// <summary>A delivery window of the document. <see cref="To"/> is 00:00 for a window that closes at the end of the day.</summary>
internal sealed record WindowDef(string Name, string? Zone, string? ZoneVariable, IReadOnlyList<DayOfWeek> Days, TimeOnly From, TimeOnly To);

/// <summary>An <c>ai</c> step.</summary>
internal sealed record AiStep(
    string Id, string? Stage, string Port, Mapping Input, Mapping Output, RetryPolicy Retry, string Next,
    string? OnError, string? OnNotAvailable, string? OnNotEntitled, string Model, string PromptId, long PromptVersion,
    JsonSchema? InputSchema, JsonSchema? OutputSchema, int MaxAttempts, string OnInvalid)
    : EffectStep(Id, Stage, Port, Input, Output, Retry, Next, OnError, OnNotAvailable, OnNotEntitled, null)
{
    internal override StepKind Kind => StepKind.Ai;

    internal override IEnumerable<StepRef> References() => EffectReferences().Concat(Optional(OnInvalid, "onInvalid"));
}

/// <summary>A <c>decide</c> step.</summary>
internal sealed record DecideStep(
    string Id, string? Stage, string Rule, Mapping Facts, Mapping Output, string? Route,
    IReadOnlyList<KeyValuePair<string, string>>? On, string? Next, string? CollectInto, string OnNoDecision, string? OnError)
    : StepDef(Id, Stage)
{
    internal override StepKind Kind => StepKind.Decide;

    internal override IEnumerable<StepRef> References() =>
        Optional(Next, "next").Concat(Table(On, "on")).Concat(Optional(OnNoDecision, "onNoDecision")).Concat(Optional(OnError, "onError"));

    internal override IEnumerable<(string Variable, string Member)> VariableUses() =>
        MappedVariables(Facts, "facts").Concat(MappedVariables(Output, "output"))
            .Concat(CollectInto is null ? [] : new[] { (CollectInto, "/collectInto") });
}

/// <summary>A <c>humanTask</c> step.</summary>
internal sealed record HumanTaskStep(
    string Id, string? Stage, string Queue, IReadOnlyList<string> Outcomes, IReadOnlyList<string> Show, Mapping Output,
    IReadOnlyList<KeyValuePair<string, string>> On, TimeSpan? Timeout, string? OnTimeout)
    : StepDef(Id, Stage)
{
    internal override StepKind Kind => StepKind.HumanTask;

    internal override IEnumerable<StepRef> References() => Table(On, "on").Concat(Optional(OnTimeout, "onTimeout"));

    internal override IEnumerable<(string Variable, string Member)> VariableUses() =>
        Show.Select((name, index) => (name, JsonText.Pointer("/show", index)))
            .Concat(MappedVariables(Output, "output"));
}

/// <summary>A <c>wait</c> step.</summary>
internal sealed record WaitStep(
    string Id, string? Stage, string Event, string? Correlation, Mapping Output, string Next, TimeSpan? Timeout, string? OnTimeout)
    : StepDef(Id, Stage)
{
    internal override StepKind Kind => StepKind.Wait;

    internal override IEnumerable<StepRef> References() => Optional(Next, "next").Concat(Optional(OnTimeout, "onTimeout"));

    internal override IEnumerable<(string Variable, string Member)> VariableUses() =>
        (Correlation is null ? [] : new[] { (Correlation, "/correlation") }).Concat(MappedVariables(Output, "output"));
}

/// <summary>A <c>timer</c> step.</summary>
internal sealed record TimerStep(string Id, string? Stage, TimeSpan? After, string? Until, TimeSpan? Offset, string Next)
    : StepDef(Id, Stage)
{
    internal override StepKind Kind => StepKind.Timer;

    internal override IEnumerable<StepRef> References() => Optional(Next, "next");

    internal override IEnumerable<(string Variable, string Member)> VariableUses() =>
        Until is null ? [] : [(Until, "/until")];
}

/// <summary>A <c>loop</c> step.</summary>
internal sealed record LoopStep(
    string Id, string? Stage, int MaxIterations, string Body, string OnExhausted, string? ForEach, string? Item, string? Next)
    : StepDef(Id, Stage)
{
    internal override StepKind Kind => StepKind.Loop;

    internal override IEnumerable<StepRef> References() =>
        Optional(Body, "body").Concat(Optional(OnExhausted, "onExhausted")).Concat(Optional(Next, "next"));

    internal override IEnumerable<(string Variable, string Member)> VariableUses() =>
        (ForEach is null ? [] : new[] { (ForEach, "/forEach") }).Concat(Item is null ? [] : new[] { (Item, "/item") });
}

/// <summary>A <c>subprocess</c> step.</summary>
internal sealed record SubprocessStep(
    string Id, string? Stage, string Process, Mapping Input, Mapping Output, string? Next,
    IReadOnlyList<KeyValuePair<string, string>>? On, string? OnError)
    : StepDef(Id, Stage)
{
    internal override StepKind Kind => StepKind.Subprocess;

    internal override IEnumerable<StepRef> References() =>
        Optional(Next, "next").Concat(Table(On, "on")).Concat(Optional(OnError, "onError"));

    internal override IEnumerable<(string Variable, string Member)> VariableUses() =>
        MappedVariables(Input, "input").Concat(MappedVariables(Output, "output"));
}

/// <summary>An <c>end</c> step.</summary>
internal sealed record EndStep(string Id, string? Stage, string Result) : StepDef(Id, Stage)
{
    internal override StepKind Kind => StepKind.End;

    internal override IEnumerable<StepRef> References() => [];
}

/// <summary>A loaded process document in the form the validator and the interpreter use.</summary>
internal sealed class ProcessDefinition
{
    internal required string Id { get; init; }

    internal required long Version { get; init; }

    internal required string Hash { get; init; }

    internal required string Subject { get; init; }

    internal required IReadOnlyList<string> Stages { get; init; }

    internal required IReadOnlyDictionary<string, VariableDef> Variables { get; init; }

    internal required IReadOnlySet<string> Accepts { get; init; }

    internal required int MaxSteps { get; init; }

    internal required string Start { get; init; }

    internal required IReadOnlyList<StepDef> Steps { get; init; }

    internal required IReadOnlyDictionary<string, StepDef> StepsById { get; init; }

    internal required IReadOnlyList<(string Reference, int Index)> Uses { get; init; }

    internal required JsonObject Meta { get; init; }

    internal IReadOnlyList<ProcessTriggerDeclaration> Triggers { get; init; } = [];

    internal IReadOnlyDictionary<string, WindowDef> Windows { get; init; } = new Dictionary<string, WindowDef>(StringComparer.Ordinal);

    internal required JsonArray Scenarios { get; init; }

    internal IReadOnlyDictionary<string, BrainRule> Rules { get; set; } = new Dictionary<string, BrainRule>(StringComparer.Ordinal);
}

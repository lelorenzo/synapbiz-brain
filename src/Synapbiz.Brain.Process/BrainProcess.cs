using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Process;

/// <summary>The clock the interpreter reads: the instant and the calendar date it works with. The interpreter reads no clock of its own.</summary>
/// <param name="Now">The current instant.</param>
/// <param name="Today">The current calendar date, as the practice sees it.</param>
public readonly record struct ProcessClock(DateTimeOffset Now, DateOnly Today);

/// <summary>What <see cref="BrainProcess.Advance"/> did with a trigger.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "Ordinals start at 1 by design and by project convention (the same documented CA1008 suppression as FactType in Synapbiz.Brain.Contracts): default(AdvanceDisposition) == 0 is deliberately not a valid disposition.")]
public enum AdvanceDisposition
{
    /// <summary>The trigger changed the state.</summary>
    Applied = 1,

    /// <summary>The trigger did not concern the instance any more (for example a redelivered result); nothing changed.</summary>
    Ignored = 2,

    /// <summary>The trigger was refused; nothing changed.</summary>
    Rejected = 3,
}

/// <summary>The result of starting or advancing an instance.</summary>
/// <param name="State">The new state; the unchanged input state when the trigger was ignored or rejected.</param>
/// <param name="Disposition">What was done with the trigger.</param>
/// <param name="Code">The code that explains an ignored or rejected trigger; null when applied.</param>
/// <param name="Requests">What the host must do after committing the state, in order.</param>
/// <param name="History">The entries to append to the history, in order.</param>
public sealed record AdvanceResult(
    ProcessState State,
    AdvanceDisposition Disposition,
    string? Code,
    IReadOnlyList<ProcessRequest> Requests,
    IReadOnlyList<ProcessHistoryEntry> History);

/// <summary>
/// A loaded <c>brain.process@1</c> document and the pure interpreter of it: a function of the definition, the state of
/// an instance, a trigger and a clock. Instances are created by <see cref="BrainProcessLoader"/>.
/// </summary>
public sealed class BrainProcess
{
    private readonly ProcessDefinition definition;

    internal BrainProcess(ProcessDefinition definition)
    {
        this.definition = definition;
    }

    /// <summary>The id of the process document.</summary>
    public string Id => definition.Id;

    /// <summary>The version of the process document.</summary>
    public long Version => definition.Version;

    /// <summary>The content hash of the document text that was loaded.</summary>
    public string ContentHash => definition.Hash;

    /// <summary>The case type the process runs for; opaque to Brain.</summary>
    public string Subject => definition.Subject;

    /// <summary>The stages of the process.</summary>
    public IReadOnlyList<string> Stages => definition.Stages;

    /// <summary>The ids of the steps, in document order.</summary>
    public IReadOnlyList<string> StepIds => [.. definition.Steps.Select(step => step.Id)];

    /// <summary>A deep clone of the <c>meta</c> object of the document.</summary>
    public JsonObject Meta => (JsonObject)definition.Meta.DeepClone();

    /// <summary>
    /// Starts an instance: sets its variables from <c>initial</c> and <paramref name="input"/>, enters the start step and
    /// runs until the instance has to wait, ends or is suspended. An input member that is not an input variable, or a value
    /// of the wrong type, gives a rejected result with a cancelled state.
    /// </summary>
    /// <param name="instanceId">The id of the instance: not empty and without <c>/</c>.</param>
    /// <param name="input">The input variables, or null.</param>
    /// <param name="clock">The clock to work with.</param>
    public AdvanceResult Start(string instanceId, JsonObject? input, ProcessClock clock)
    {
        if (string.IsNullOrEmpty(instanceId) || instanceId.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("The instance id must not be empty and must not contain '/'.", nameof(instanceId));
        }

        return new ProcessRun(definition, clock).Start(instanceId, input);
    }

    /// <summary>Applies a trigger to the state of an instance and runs until the instance has to wait, ends or is suspended.</summary>
    /// <param name="state">The state of the instance.</param>
    /// <param name="trigger">What happened.</param>
    /// <param name="clock">The clock to work with.</param>
    public AdvanceResult Advance(ProcessState state, ProcessTrigger trigger, ProcessClock clock)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(trigger);

        if (state.DefinitionId != definition.Id || state.DefinitionVersion != definition.Version || state.DefinitionHash != definition.Hash)
        {
            return Unchanged(state, AdvanceDisposition.Rejected, "process.definition.mismatch");
        }

        return new ProcessRun(definition, clock).Advance(state, trigger);
    }

    /// <summary>Runs the scenarios embedded in the document on a virtual clock.</summary>
    public ScenarioRunResult RunScenarios() => ScenarioRunner.Run(this, definition);

    internal static AdvanceResult Unchanged(ProcessState state, AdvanceDisposition disposition, string code) =>
        new(state, disposition, code, [], []);
}

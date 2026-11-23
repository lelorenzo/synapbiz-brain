using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Workbench.Flows;

/// <summary>
/// An embedded scenario played one command at a time, so that a screen can show it step by step: the case is started, and each call
/// to <see cref="Next"/> applies the next command of the scenario to the session. When the last command has been applied (or a command
/// the interpreter did not apply stopped the scenario) the replay is finished; the session is then an ordinary one the reader may go
/// on with by hand. Playing all the commands gives the same session, history and failure as <see cref="BrainProcess.RunScenarios"/>.
/// </summary>
public sealed class FlowReplay
{
    private readonly JsonArray events;
    private readonly JsonObject expect;
    private readonly ScriptedAnswerSource script;
    private int played;
    private string? failure;
    private bool checkedEnd;

    internal FlowReplay(BrainProcess process, JsonObject scenario, IFlowAnswerSource? answerSource, IReadOnlyCollection<BrainProcess>? childProcesses)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(scenario);
        (Session, script) = FlowSimulation.BeginScenario(process, scenario, answerSource, childProcesses);
        events = scenario["events"]!.AsArray();
        expect = scenario["expect"]!.AsObject();
        failure = FlowSimulation.Problem(Session.Log);
    }

    /// <summary>The session the scenario is played on. It follows the replay and is the reader's to continue once <see cref="IsFinished"/>.</summary>
    public FlowSimulation Session { get; }

    /// <summary>The name of the scenario.</summary>
    public string Name => Session.Name;

    /// <summary>The number of commands the scenario has.</summary>
    public int Total => events.Count;

    /// <summary>The number of commands applied so far.</summary>
    public int Played => played;

    /// <summary>True when every command was applied, or a command was not applied by the interpreter and so ended the scenario.</summary>
    public bool IsFinished => failure is not null || played >= events.Count;

    /// <summary>
    /// What went wrong: the first command the interpreter did not apply or, when the scenario was played to its end, the first point
    /// where the case differs from the scenario's own <c>expect</c>. Null while the replay is not finished and when the scenario passes.
    /// </summary>
    public string? Failure
    {
        get
        {
            if (failure is null && IsFinished && !checkedEnd)
            {
                checkedEnd = true;
                failure = FlowSimulation.Check(Session, expect);
            }

            return failure;
        }
    }

    /// <summary>Starts the case of an embedded scenario and stops before its first command.</summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="scenarioName">The name of an embedded scenario.</param>
    /// <param name="answerSource">Who answers effects once the reader goes on by hand, or null.</param>
    /// <param name="childProcesses">Child documents for <c>subprocess</c> steps, or null.</param>
    /// <exception cref="ArgumentException">The document has no scenario with that name.</exception>
    public static FlowReplay Start(BrainProcess process, string scenarioName, IFlowAnswerSource? answerSource = null, IReadOnlyCollection<BrainProcess>? childProcesses = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(scenarioName);
        var text = ProcessOutline.From(process).ScenarioJson(scenarioName)
            ?? throw new ArgumentException($"The document has no scenario named '{scenarioName}'.", nameof(scenarioName));
        return new FlowReplay(process, JsonNode.Parse(text)!.AsObject(), answerSource, childProcesses);
    }

    /// <summary>Starts the case of a scenario written in the embedded format that is not in the document (a proposed one) and stops before its first command.</summary>
    /// <param name="process">The loaded process.</param>
    /// <param name="scenarioJson">The scenario as a JSON object.</param>
    /// <param name="answerSource">Who answers effects once the reader goes on by hand, or null.</param>
    /// <param name="childProcesses">Child documents for <c>subprocess</c> steps, or null.</param>
    /// <exception cref="ArgumentException">The text is not a scenario that is valid for the document.</exception>
    public static FlowReplay StartJson(BrainProcess process, string scenarioJson, IFlowAnswerSource? answerSource = null, IReadOnlyCollection<BrainProcess>? childProcesses = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(scenarioJson);
        var scenario = FlowJson.TryObject(scenarioJson);
        if (scenario is null || ProcessOutline.CheckScenario(process, scenario).Count > 0)
        {
            throw new ArgumentException("The text is not a scenario that is valid for the document.", nameof(scenarioJson));
        }

        return new FlowReplay(process, scenario, answerSource, childProcesses);
    }

    /// <summary>Applies the next command of the scenario; nothing when the replay is finished.</summary>
    /// <returns>The log entries the command made.</returns>
    public IReadOnlyList<FlowSimulationEntry> Next()
    {
        if (IsFinished)
        {
            return [];
        }

        var before = Session.Log.Count;
        failure = Session.Apply(events[played]!.AsObject(), script);
        played++;
        return [.. Session.Log.Skip(before)];
    }

    /// <summary>Applies every command that is left.</summary>
    /// <returns>The log entries the commands made.</returns>
    public IReadOnlyList<FlowSimulationEntry> PlayToEnd()
    {
        var before = Session.Log.Count;
        while (!IsFinished)
        {
            _ = Next();
        }

        _ = Failure;
        return [.. Session.Log.Skip(before)];
    }
}

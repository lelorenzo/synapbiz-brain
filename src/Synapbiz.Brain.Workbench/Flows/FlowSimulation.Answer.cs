namespace Synapbiz.Brain.Workbench.Flows;

public sealed partial class FlowSimulation
{
    /// <summary>
    /// Answers an outstanding effect with a <see cref="FlowAnswer"/> (an output or a failure) and records who gave it. It is the same
    /// command as <see cref="CompleteEffect(string, string)"/> or <see cref="FailEffect(string, string, bool)"/>, with the source of the
    /// log entry chosen by the caller: a screen that answers "as the test does" passes <see cref="ScriptedSource"/>.
    /// </summary>
    /// <param name="effectId">The id from <see cref="Outstanding"/>.</param>
    /// <param name="answer">The answer.</param>
    /// <param name="source">The source to write in the log entry.</param>
    public IReadOnlyList<FlowSimulationEntry> ApplyAnswer(string effectId, FlowAnswer answer, string source = ScriptedSource)
    {
        ArgumentNullException.ThrowIfNull(effectId);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(source);
        return answer.IsFailure
            ? FailEffect(effectId, answer.Kind, answer.Code!, answer.RetryAfter, source)
            : CompleteEffect(effectId, answer.OutputJson!, source);
    }
}

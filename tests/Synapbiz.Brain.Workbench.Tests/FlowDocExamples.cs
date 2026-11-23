using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Flows;

// The C# blocks of docs/reference/flow-workbench.md. Every block of the page is a region of this file, between the two markers
// "doc:begin <name>" and "doc:end", and FlowDocumentationTests checks that the page shows each region verbatim and shows no other C#.
// Everything here is synthetic: IHostAi stands for the host's own account with a model vendor.
namespace Synapbiz.Brain.Workbench.Tests;

// doc:begin answer-source
/// <summary>The host's own AI account behind a port; the library holds no vendor, key or client.</summary>
internal interface IHostAi
{
    Task<string> CompleteAsync(string model, string promptId, long promptVersion, string inputJson, string outputSchemaJson, CancellationToken cancellationToken);
}

/// <summary>Answers the <c>ai</c> steps of a simulated case with the host's AI account and leaves every other effect to the person.</summary>
internal sealed class HostAiAnswerSource(IHostAi account) : IFlowAnswerSource
{
    public string Name => "HostAi";

    public async ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken)
    {
        if (request.StepKind != OutlineStepKind.Ai)
        {
            return null;
        }

        try
        {
            var text = await account.CompleteAsync(request.Model!, request.PromptId!, request.PromptVersion!.Value, request.InputJson, request.OutputSchemaJson!, cancellationToken);
            return FlowAnswer.Output(text);
        }
        catch (HttpRequestException)
        {
            return FlowAnswer.Failure(ProcessErrorKind.NotAvailable, "host.ai.unreachable");
        }
    }
}

// doc:end

// doc:begin try-out
internal static class FlowTryOut
{
    /// <summary>Starts a case of a flow at the given time, lets the answer source answer, and says what is left for a person.</summary>
    internal static async Task<(FlowSimulation Session, IReadOnlyList<string> WaitingFor)> RunAsync(
        BrainProcess process,
        string inputJson,
        IFlowAnswerSource source,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var session = FlowSimulation.Start(process, new FlowSimulationSetup
        {
            At = now,
            Today = DateOnly.FromDateTime(now.UtcDateTime),
            InputJson = inputJson,
            AnswerSource = source,
            Zones = new Dictionary<string, TimeSpan> { ["America/New_York"] = TimeSpan.FromHours(-5) },
        });
        await session.RunAnswersAsync(cancellationToken: cancellationToken);

        var waiting = session.Outstanding.Select(item => item switch
        {
            OutstandingEffect effect => $"an answer from {effect.Port}",
            OutstandingTask task => $"a person in the queue {task.Queue}",
            OutstandingWait wait => $"the event {wait.Event}",
            OutstandingTimer timer => $"the time {timer.Due:u}",
            OutstandingWindow window => $"the window to open at {window.OpensAt:u}",
            OutstandingChild child => $"the process {child.ProcessId}",
            _ => item.Id,
        }).ToList();
        return (session, waiting);
    }
}

// doc:end

// doc:begin review
internal static class FlowReviews
{
    /// <summary>The document a reviewer's answers become: one item per target and the scenario of a try-out they want to keep.</summary>
    internal static string Write(BrainProcess process, string reviewer, DateTimeOffset now, FlowSimulation keep)
    {
        var outline = ProcessOutline.From(process);
        var review = new FlowReview
        {
            ProcessId = outline.Id,
            ProcessVersion = outline.Version,
            ProcessContentHash = outline.ContentHash,
            Reviewer = reviewer,
            CreatedAt = now,
            Language = WorkbenchLocale.En,
            Items = [new ReviewItem("process", ReviewState.Confirmed, "The flow reads as the practice works.")],
            ProposedScenarios = [new FlowProposedScenario("a try-out worth keeping", keep.ToScenarioJson("a try-out worth keeping"))],
        };
        return FlowReviewJson.Serialize(review);
    }

    /// <summary>What an author learns from a review that came back: whether it is for this version, and which proposals pass.</summary>
    internal static IReadOnlyList<string> Read(string text, BrainProcess process)
    {
        var parsed = FlowReviewJson.Parse(text);
        if (parsed.Review is not { } review)
        {
            return [.. parsed.Errors.Select(error => error.Code + " at " + error.Path)];
        }

        var report = FlowReviewCheck.Against(review, process);
        return
        [
            .. report.Findings.Select(finding => finding.Code + " at " + finding.Path),
            .. report.Scenarios.Select(result => result.Name + (result.Passed == true ? ": passes" : ": does not pass")),
        ];
    }
}

// doc:end

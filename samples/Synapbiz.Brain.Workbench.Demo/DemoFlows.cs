using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Demo;

/// <summary>The flows the demo shows: the six reference documents of <c>samples/process-reference</c>, the zones its try-outs may meet, and its source of answers.</summary>
internal sealed class DemoFlows
{
    private static readonly string[] Ids =
    [
        "previsit.outreach",
        "visit.note_wait",
        "postvisit.coding_review",
        "collection.followup",
        "case.lifecycle",
        "practice.daily_refresh",
    ];

    private readonly Dictionary<WorkbenchLocale, IReadOnlyList<WorkbenchFlow>> byLocale = [];
    private readonly Dictionary<string, BrainProcess> processes = new(StringComparer.Ordinal);

    public DemoFlows()
    {
        foreach (var id in Ids)
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "process-reference", id);
            var text = File.ReadAllText(Path.Combine(folder, "process.json"));
            var contractPath = Path.Combine(folder, "contract.json");
            var contract = File.Exists(contractPath) ? BrainContract.Parse(File.ReadAllText(contractPath)) : null;
            var rules = Directory.GetFiles(folder, "*.rule.json").Order(StringComparer.Ordinal)
                .Select(file => BrainRuleLoader.Load(File.ReadAllText(file), contract is null ? [] : [contract]).Rule!)
                .ToList();
            var loaded = BrainProcessLoader.Load(text, new ProcessLoadContext(rules, Ids));
            processes[id] = loaded.Process ?? throw new InvalidOperationException("The reference flow " + id + " does not load.");
        }

        Answers = new DemoAnswers(processes);
    }

    /// <summary>The fixed offsets of the zones the reference flows use. A real host would resolve them from a time zone database.</summary>
    public IReadOnlyDictionary<string, TimeSpan> Zones { get; } = new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
    {
        ["America/New_York"] = TimeSpan.FromHours(-5),
        ["America/Chicago"] = TimeSpan.FromHours(-6),
        ["America/Denver"] = TimeSpan.FromHours(-7),
        ["America/Los_Angeles"] = TimeSpan.FromHours(-8),
    };

    /// <summary>The scripted source of answers, named "Demo answers".</summary>
    public IFlowAnswerSource Answers { get; }

    /// <summary>The flows of the list for a language; the same instances every time so the panels can keep what they computed.</summary>
    public IReadOnlyList<WorkbenchFlow> For(WorkbenchLocale locale)
    {
        lock (byLocale)
        {
            if (!byLocale.TryGetValue(locale, out var flows))
            {
                var es = locale == WorkbenchLocale.Es;
                flows =
                [
                    .. Ids.Select(id => new WorkbenchFlow
                    {
                        Key = id,
                        Group = es ? "Procesos de referencia" : "Reference processes",
                        Process = processes[id],
                        FallbackTitle = id,
                        SourceLine = es ? "Fuente: documento de ejemplo " + id + "." : "Source: example document " + id + ".",
                    }),
                ];
                byLocale[locale] = flows;
            }

            return flows;
        }
    }
}

/// <summary>
/// The demo's answer source: for each effect it gives the first answer, that is not a failure, which a test of the document gives for
/// that step, and leaves the effect for a manual answer when no test answers it. It is deterministic and calls no service.
/// </summary>
internal sealed class DemoAnswers(IReadOnlyDictionary<string, BrainProcess> processes) : IFlowAnswerSource
{
    /// <inheritdoc />
    public string Name => "Demo answers";

    /// <inheritdoc />
    public ValueTask<FlowAnswer?> AnswerAsync(FlowEffectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        FlowAnswer? answer = null;
        if (processes.TryGetValue(request.ProcessId, out var process))
        {
            answer = FlowScenarioAnswers.For(process, request.StepId, 0).Select(item => item.Answer).FirstOrDefault(item => !item.IsFailure);
        }

        return ValueTask.FromResult(answer);
    }
}

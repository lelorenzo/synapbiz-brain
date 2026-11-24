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

    /// <summary>The authority each rule of the reference flows states, by rule id. The codes are the demo's; a host reads them from its rules with <c>RuleClaims.From(rule.Meta).Authority</c>.</summary>
    public IReadOnlyDictionary<string, string> RuleAuthorities { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["collection.claim_progress"] = "payment_condition",
        ["collection.claim_result"] = "payer_measured",
        ["postvisit.opportunities"] = "recommendation",
        ["previsit.missing_items"] = "local_preference",
    };

    /// <summary>The label of each authority code of the demo, for a language.</summary>
    /// <param name="locale">The language.</param>
    internal static IReadOnlyDictionary<string, string> AuthorityLabelsFor(WorkbenchLocale locale) => locale == WorkbenchLocale.Es
        ? new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["payment_condition"] = "Condición de pago",
            ["payer_measured"] = "Medida por el pagador",
            ["recommendation"] = "Recomendación",
            ["local_preference"] = "Preferencia local",
        }
        : new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["payment_condition"] = "Payment condition",
            ["payer_measured"] = "Payer measured",
            ["recommendation"] = "Recommendation",
            ["local_preference"] = "Local preference",
        };

    /// <summary>The label of each authority code, for a language.</summary>
    /// <param name="locale">The language.</param>
    public static IReadOnlyDictionary<string, string> AuthorityLabels(WorkbenchLocale locale) => AuthorityLabelsFor(locale);

    /// <summary>
    /// The names the map shows for the rules and connectors of the reference flows, for a language (keys from <c>ProcessMap.LabelKey</c>).
    /// Two connectors and one rule are left out on purpose, so that the map also shows a name it has no label for, as code.
    /// </summary>
    /// <param name="locale">The language.</param>
    public static IReadOnlyDictionary<string, string> MapLabels(WorkbenchLocale locale)
    {
        var es = locale == WorkbenchLocale.Es;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rule:lifecycle.zone_ready"] = es ? "Siguiente fase del caso" : "Next phase of the case",
            ["rule:previsit.missing_items"] = es ? "Qué falta para la visita" : "What is missing for the visit",
            ["rule:visit.note_ready"] = es ? "La nota está lista" : "The note is ready",
            ["rule:postvisit.opportunities"] = es ? "Oportunidades de codificación" : "Coding opportunities",
            ["rule:postvisit.suggestion_check"] = es ? "Aceptar la propuesta" : "Accept the proposal",
            ["rule:collection.claim_progress"] = es ? "Avance del reclamo" : "Claim progress",
            ["rule:collection.claim_result"] = es ? "Encaminar el reclamo" : "Route the claim",
            ["connector:intake.requirements"] = es ? "Requisitos de ingreso" : "Intake requirements",
            ["connector:messaging.reminder"] = es ? "Mensaje al paciente" : "Message to the patient",
            ["connector:records.note_status"] = es ? "Historia clínica" : "Medical record",
            ["connector:records.note_facts"] = es ? "Datos de la nota" : "Note facts",
            ["connector:claims.status"] = es ? "Estado del reclamo" : "Claim status",
            ["queue:front_desk"] = es ? "Recepción" : "Front desk",
        };
    }

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

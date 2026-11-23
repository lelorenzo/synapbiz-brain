using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Demo;

/// <summary>The rules the demo shows: the synthetic fixture files, with full Spanish and English labels and one entry that cannot be read.</summary>
internal sealed class DemoRules
{
    private sealed record Source(string Key, string File, string Contract, string? Group, RuleApprovalState State);

    private static readonly Source[] Sources =
    [
        new("readiness", "readiness.rule.json", "readiness.contract.json", "Scoring", RuleApprovalState.Approved),
        new("intake", "intake.first.rule.json", "intake.contract.json", "Intake", RuleApprovalState.Approved),
        new("intake-v2", "intake.first.v2.rule.json", "intake.contract.json", "Intake", RuleApprovalState.Draft),
        new("unique", "intake.unique.rule.json", "intake.contract.json", "Intake", RuleApprovalState.Draft),
    ];

    private readonly Dictionary<WorkbenchLocale, IReadOnlyList<WorkbenchRule>> byLocale = [];

    /// <summary>The rules of the list for a language; the same instances every time so the panels can keep what they computed.</summary>
    public IReadOnlyList<WorkbenchRule> For(WorkbenchLocale locale)
    {
        lock (byLocale)
        {
            if (!byLocale.TryGetValue(locale, out var rules))
            {
                rules = Build(locale);
                byLocale[locale] = rules;
            }

            return rules;
        }
    }

    private static List<WorkbenchRule> Build(WorkbenchLocale locale)
    {
        var es = locale == WorkbenchLocale.Es;
        var rules = new List<WorkbenchRule>();
        foreach (var source in Sources)
        {
            var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", source.File));
            var contract = BrainContract.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", source.Contract)));
            var loaded = BrainRuleLoader.Load(text, [contract]);
            var rule = loaded.Rule;
            rules.Add(new WorkbenchRule
            {
                Key = source.Key,
                Group = source.Group,
                Rule = rule,
                RuleText = rule is null ? null : text,
                FallbackTitle = source.File,
                Labels = rule is null ? RuleLabels.Empty : Labels(source.Key, rule, locale),
                State = source.State,
                StateLine = StateLine(source.State, es),
                SourceLine = es ? "Fuente: archivo de ejemplo " + source.File + "." : "Source: example file " + source.File + ".",
                RowTexts = RowTexts(source.Key, es),
                HeadlineFields = source.Key == "intake" ? ["route", "queue"] : null,
            });
        }

        rules.Add(new WorkbenchRule
        {
            Key = "unreadable",
            Group = "Intake",
            FallbackTitle = es ? "Archivo que no se pudo leer" : "File that could not be read",
        });
        return rules;
    }

    private static string? StateLine(RuleApprovalState state, bool es)
    {
        if (state != RuleApprovalState.Approved)
        {
            return null;
        }

        return es ? "Aprobada en una fecha de ejemplo." : "Approved on an example date.";
    }

    private static RuleLabels Labels(string key, BrainRule rule, WorkbenchLocale locale)
    {
        var es = locale == WorkbenchLocale.Es;
        var fromMeta = RuleLabels.FromMeta(rule.Meta, locale);
        if (key == "readiness")
        {
            return fromMeta with
            {
                Title = es ? "Notas de preparación" : "Readiness notes",
                Facts = new Dictionary<string, string> { ["stage"] = es ? "Etapa" : "Stage", ["score"] = es ? "Puntaje" : "Score" },
                OutcomeFields = new Dictionary<string, string> { ["note"] = es ? "Nota" : "Note" },
                Values = new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    ["stage"] = new Dictionary<string, string>
                    {
                        ["Intake"] = es ? "Ingreso" : "Intake",
                        ["Review"] = es ? "Revisión" : "Review",
                        ["Complete"] = es ? "Completa" : "Complete",
                    },
                    ["note"] = new Dictionary<string, string>
                    {
                        ["low-score"] = es ? "Puntaje bajo" : "Low score",
                        ["mid-score"] = es ? "Puntaje medio" : "Middle score",
                        ["late-stage"] = es ? "Etapa avanzada" : "Late stage",
                    },
                },
            };
        }

        var title = key switch
        {
            "intake-v2" => es ? "Enrutamiento de solicitudes, segunda versión" : "Intake routing, second version",
            "unique" => es ? "Solicitudes con una sola fila" : "Intake with one row at a time",
            _ => fromMeta.Title,
        };
        return fromMeta with
        {
            Title = title,
            Facts = new Dictionary<string, string>
            {
                ["channel"] = es ? "Canal de contacto" : "Contact channel",
                ["topic"] = es ? "Tema" : "Topic",
                ["urgent"] = es ? "Urgente" : "Urgent",
                ["ageDays"] = es ? "Antigüedad en días" : "Age in days",
                ["rating"] = es ? "Valoración" : "Rating",
                ["amount"] = es ? "Monto" : "Amount",
                ["receivedOn"] = es ? "Recibida el" : "Received on",
                ["account"] = es ? "Cuenta" : "Account",
            },
            OutcomeFields = new Dictionary<string, string>
            {
                ["route"] = es ? "Ruta" : "Route",
                ["queue"] = es ? "Cola" : "Queue",
                ["notify"] = es ? "Avisar" : "Notify",
                ["priority"] = es ? "Prioridad" : "Priority",
            },
            Values = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["channel"] = new Dictionary<string, string>
                {
                    ["Web"] = es ? "el sitio web" : "the website",
                    ["Phone"] = es ? "la línea telefónica" : "the phone line",
                    ["Email"] = es ? "el correo" : "email",
                },
                ["route"] = new Dictionary<string, string>
                {
                    ["Standard"] = es ? "trámite estándar" : "standard handling",
                    ["Fast"] = es ? "trámite rápido" : "fast handling",
                    ["Escalate"] = es ? "escalado" : "escalation",
                    ["Manual"] = es ? "revisión manual" : "manual review",
                },
                ["queue"] = new Dictionary<string, string>
                {
                    ["urgent"] = es ? "Cola urgente" : "Urgent queue",
                    ["billing"] = es ? "Cola de facturas" : "Billing queue",
                    ["billing-email"] = es ? "Buzón de facturas" : "Billing mailbox",
                    ["new"] = es ? "Solicitudes nuevas" : "New requests",
                    ["followup"] = es ? "Seguimiento" : "Follow-up",
                    ["review"] = es ? "Cola de revisión" : "Review queue",
                    ["backlog"] = es ? "Pendientes" : "Backlog",
                    ["unassigned"] = es ? "Sin asignar" : "Unassigned",
                    ["general"] = es ? "Cola general" : "General queue",
                    ["phone"] = es ? "Cola telefónica" : "Phone queue",
                },
                ["topic"] = new Dictionary<string, string>
                {
                    ["bill"] = es ? "factura" : "bill",
                    ["invoice"] = es ? "recibo" : "invoice",
                    ["refund"] = es ? "reembolso" : "refund",
                },
            },
        };
    }

    private static Dictionary<string, string> RowTexts(string key, bool es) => key switch
    {
        "readiness" => new Dictionary<string, string>
        {
            ["low-score"] = es ? "Puntaje bajo. Conviene repasar el material." : "Low score. The material is worth reviewing.",
            ["mid-score"] = es ? "Puntaje medio." : "Middle score.",
        },
        "intake" or "intake-v2" => new Dictionary<string, string>
        {
            ["urgent-phone"] = es ? "Se atiende primero por teléfono." : "Handled first by phone.",
            ["fallback"] = es ? "Todo lo demás va a la cola general." : "Everything else goes to the general queue.",
        },
        _ => [],
    };
}

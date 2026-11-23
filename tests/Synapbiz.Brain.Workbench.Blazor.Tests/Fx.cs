using System.Text.Json;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The shared fixtures: the rule files above the test assembly and the labels the tests add to them.</summary>
internal static class Fx
{
    internal static readonly string[] Names = ["readiness", "intake.first", "intake.first.v2", "intake.unique"];

    internal static readonly DateTimeOffset Moment = new(2026, 11, 22, 21, 0, 0, TimeSpan.Zero);

    internal static string Locate(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("Could not locate the fixture file above the test assembly.", relativePath);
    }

    internal static string Read(string relativePath) => File.ReadAllText(Locate(relativePath));

    internal static string TextOf(string name) => name switch
    {
        "readiness" => Read("tests/fixtures/brain-spec/readiness.rule.json"),
        "intake.first" => Read("tests/fixtures/workbench/intake.first.rule.json"),
        "intake.first.v2" => Read("tests/fixtures/workbench/intake.first.v2.rule.json"),
        "intake.unique" => Read("tests/fixtures/workbench/intake.unique.rule.json"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown fixture."),
    };

    internal static BrainContract ContractOf(string name) => BrainContract.Parse(name == "readiness"
        ? Read("tests/fixtures/brain-spec/readiness.contract.json")
        : Read("tests/fixtures/workbench/intake.contract.json"));

    internal static BrainRule Load(string name) => Load(TextOf(name), ContractOf(name));

    internal static BrainRule Load(string text, BrainContract contract)
    {
        var result = BrainRuleLoader.Load(text, [contract]);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(error => $"{error.Code}@{error.Path}")));
        return result.Rule!;
    }

    /// <summary>The labels of a fixture for a locale: its <c>meta.display</c> plus a label for every value the rule shows.</summary>
    internal static RuleLabels Labels(string name, BrainRule rule, WorkbenchLocale locale)
    {
        var es = locale == WorkbenchLocale.Es;
        var fromMeta = RuleLabels.FromMeta(rule.Meta, locale);
        var values = fromMeta.Values.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);

        void Add(string field, params (string Raw, string En, string Es)[] labels)
        {
            var merged = values.TryGetValue(field, out var existing) ? new Dictionary<string, string>(existing, StringComparer.Ordinal) : new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (raw, en, spanish) in labels)
            {
                merged.TryAdd(raw, es ? spanish : en);
            }

            values[field] = merged;
        }

        if (name == "readiness")
        {
            Add("note", ("low-score", "Low score", "Puntaje bajo"), ("mid-score", "Middle score", "Puntaje medio"), ("late-stage", "Late stage", "Etapa avanzada"));
            return fromMeta with
            {
                Title = es ? "Notas de preparación" : "Readiness notes",
                Facts = new Dictionary<string, string> { ["score"] = es ? "Puntaje" : "Score", ["stage"] = es ? "Etapa" : "Stage" },
                OutcomeFields = new Dictionary<string, string> { ["note"] = es ? "Nota" : "Note" },
                Values = values,
            };
        }

        Add(
            "queue",
            ("urgent", "Urgent queue", "Cola urgente"),
            ("billing", "Billing queue", "Cola de facturas"),
            ("billing-desk", "Billing desk", "Mesa de facturas"),
            ("billing-email", "Billing mailbox", "Buzón de facturas"),
            ("new", "New requests", "Solicitudes nuevas"),
            ("followup", "Follow-up", "Seguimiento"),
            ("review", "Review queue", "Cola de revisión"),
            ("backlog", "Backlog", "Pendientes"),
            ("unassigned", "Unassigned", "Sin asignar"),
            ("general", "General queue", "Cola general"),
            ("phone", "Phone queue", "Cola telefónica"),
            ("exports", "Exports queue", "Cola de exportaciones"));
        Add("topic", ("bill", "bill", "factura"), ("invoice", "invoice", "recibo"), ("refund", "refund", "reembolso"));
        var title = name switch
        {
            "intake.unique" => es ? "Solicitudes únicas" : "Unique intake",
            _ => fromMeta.Title,
        };
        return fromMeta with { Title = title, Values = values };
    }

    /// <summary>Texts a host would give for the rows of a fixture, by row id.</summary>
    internal static IReadOnlyDictionary<string, string> RowTexts(string name, WorkbenchLocale locale)
    {
        var es = locale == WorkbenchLocale.Es;
        return name switch
        {
            "intake.first" => new Dictionary<string, string>
            {
                ["urgent-phone"] = es ? "Se atiende primero por teléfono." : "Handled first by phone.",
                ["fallback"] = es ? "Todo lo demás va a la cola general." : "Everything else goes to the general queue.",
            },
            "readiness" => new Dictionary<string, string> { ["low-score"] = es ? "Puntaje bajo." : "Low score." },
            _ => new Dictionary<string, string>(),
        };
    }

    internal static WorkbenchRule Rule(
        string name,
        WorkbenchLocale locale = WorkbenchLocale.En,
        string? key = null,
        string? group = null,
        RuleApprovalState state = RuleApprovalState.Draft,
        string? stateLine = null,
        string? sourceLine = null)
    {
        var rule = Load(name);
        return new WorkbenchRule
        {
            Key = key ?? name,
            Group = group,
            Rule = rule,
            RuleText = TextOf(name),
            Labels = Labels(name, rule, locale),
            State = state,
            StateLine = stateLine,
            SourceLine = sourceLine,
            RowTexts = RowTexts(name, locale),
        };
    }

    /// <summary>Every fixture in one list, the intake ones grouped under a heading.</summary>
    internal static List<WorkbenchRule> All(WorkbenchLocale locale = WorkbenchLocale.En) =>
    [
        Rule("readiness", locale, group: "Scoring"),
        Rule("intake.first", locale, group: "Intake", state: RuleApprovalState.Approved),
        Rule("intake.first.v2", locale, group: "Intake"),
        Rule("intake.unique", locale, group: "Intake"),
    ];

    /// <summary>The text of a fixture with one exact piece replaced; the piece must be there once.</summary>
    internal static string Edit(string text, string from, string to)
    {
        var first = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(first >= 0, "The piece to replace is not in the text: " + from);
        Assert.Equal(-1, text.IndexOf(from, first + from.Length, StringComparison.Ordinal));
        return text.Replace(from, to, StringComparison.Ordinal);
    }

    /// <summary>The intake contract with the reference fact required, so a simulation that cannot give it is refused by the engine.</summary>
    internal static BrainContract RequiredAccountContract() => BrainContract.Parse(System.Text.RegularExpressions.Regex.Replace(
        Read("tests/fixtures/workbench/intake.contract.json"),
        "(\"name\": \"account\",\\s*\"type\": \"Reference\",\\s*\"nullable\": )true",
        "$1false"));

    /// <summary>The intake contract with the channel and the urgent flag optional, so their inputs offer "no value".</summary>
    internal static BrainContract OptionalContract()
    {
        var root = JsonNode.Parse(Read("tests/fixtures/workbench/intake.contract.json"))!.AsObject();
        foreach (var fact in root["facts"]!.AsArray().Select(item => item!.AsObject()))
        {
            if (fact["name"]!.GetValue<string>() is "channel" or "urgent")
            {
                fact["nullable"] = true;
            }
        }

        return BrainContract.Parse(root.ToJsonString());
    }

    /// <summary>The first intake rule with a fact computed from another kind, which the panels cannot resolve.</summary>
    internal static WorkbenchRule Composed()
    {
        var source = Rule("intake.first");
        var kind = source.Rule!.Kind with
        {
            Facts = [.. source.Rule.Kind.Facts, new FactDescriptor("derived", FactType.Integer, true, null, DecisionKindKey.From("sample.other"))],
        };
        return source with { Key = "composed", Rule = WithTable(source.Rule, source.Rule.Table, kind) };
    }

    /// <summary>The readiness rule with many more rows that never match, to fold the rows that do not apply.</summary>
    internal static string ManyRows() => Edited("readiness", root =>
    {
        var rows = root["body"]!["rows"]!.AsArray();
        for (var index = 4; index <= 15; index++)
        {
            var id = "extra-" + index;
            rows.Add(JsonNode.Parse(
                "{\"id\":\"" + id + "\",\"version\":1,\"legacyKey\":\"" + id + "\",\"conditions\":[{\"fact\":\"score\",\"op\":\"range\","
                + "\"value\":{\"type\":\"Integer\",\"value\":\"" + (1000 + index) + "\"},\"upper\":{\"type\":\"Integer\",\"value\":\"" + (2000 + index) + "\"},"
                + "\"lowerInclusive\":true,\"upperInclusive\":true}],\"outcome\":{\"note\":\"low-score\"}}"));
        }
    });

    /// <summary>The first readiness row with no condition that says anything, so its sentence has nothing to give as a reason.</summary>
    internal static string AlwaysRow() => Edited("readiness", root =>
        root["body"]!["rows"]![0]!["conditions"] = JsonNode.Parse("[{\"fact\":\"score\",\"op\":\"any\"}]"));

    /// <summary>A rule read from edited text, with the labels and row texts of its fixture.</summary>
    internal static WorkbenchRule FromText(
        string name,
        string text,
        BrainContract? contract = null,
        WorkbenchLocale locale = WorkbenchLocale.En,
        string? key = null,
        string? group = null)
    {
        var rule = Load(text, contract ?? ContractOf(name));
        return new WorkbenchRule
        {
            Key = key ?? name,
            Group = group,
            Rule = rule,
            RuleText = text,
            Labels = Labels(name, rule, locale),
            RowTexts = RowTexts(name, locale),
        };
    }

    /// <summary>The text of a fixture after an edit of its JSON.</summary>
    internal static string Edited(string name, Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(TextOf(name))!.AsObject();
        edit(root);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    internal static JsonArray GoldensOf(JsonObject root) => root["body"]!["goldens"]!.AsArray();

    /// <summary>The readiness rule whose first case expects a note it does not get, so that case fails.</summary>
    internal static string FailingReadiness() => Edited("readiness", root =>
        GoldensOf(root)[0]!["expect"]!["outcome"] = JsonNode.Parse("[{\"note\":\"mid-score\"}]"));

    /// <summary>The readiness rule whose first two cases fail.</summary>
    internal static string TwoFailingReadiness() => Edited("readiness", root =>
    {
        GoldensOf(root)[0]!["expect"]!["outcome"] = JsonNode.Parse("[{\"note\":\"mid-score\"}]");
        GoldensOf(root)[1]!["expect"]!["outcome"] = JsonNode.Parse("[{\"note\":\"low-score\"}]");
    });

    /// <summary>The readiness rule with only its first case, which fails.</summary>
    internal static string OneFailingCase() => Edited("readiness", root =>
    {
        var goldens = GoldensOf(root);
        goldens[0]!["expect"]!["outcome"] = JsonNode.Parse("[{\"note\":\"mid-score\"}]");
        while (goldens.Count > 1)
        {
            goldens.RemoveAt(1);
        }
    });

    /// <summary>The readiness rule with no case at all.</summary>
    internal static string WithoutCases() => Edited("readiness", root => GoldensOf(root).Clear());

    /// <summary>A copy of a rule with another table and kind, for the cases the fixtures do not cover.</summary>
    internal static BrainRule WithTable(BrainRule rule, DecisionTable table, Nuvora.Decisions.Contracts.Kinds.DecisionKindDescriptor? kind = null)
    {
        var constructor = typeof(BrainRule).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
        var rowIds = table.Rows.Concat(table.DefaultRow is null ? [] : [table.DefaultRow]).ToDictionary(row => row.Key, row => row.Key, StringComparer.Ordinal);
        return (BrainRule)constructor.Invoke([rule.Id, rule.Version, kind ?? rule.Kind, table, rule.Goldens, rowIds, rule.Meta.DeepClone()]);
    }
}

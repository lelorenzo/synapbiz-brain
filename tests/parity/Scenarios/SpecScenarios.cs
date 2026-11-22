using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Nuvora.Decisions.Contracts.Goldens;
using Nuvora.Decisions.Contracts.Tables;
using Nuvora.Decisions.Engine.Compilation;
using Nuvora.Decisions.Engine.Facts;
using Nuvora.Decisions.Engine.Goldens;

namespace Synapbiz.Brain.Parity;

/// <summary>
/// The 77 <c>brain.rule@1</c> cases of the shared conformance suite and the hand-written invalid documents, each projected
/// into an engine table: the errors, the table, the golden cases and the golden run.
/// </summary>
internal static class SpecScenarios
{
    private const string ConformancePath = "tests/fixtures/spec/brain/conformance.json";
    private const string StructurePath = "tests/fixtures/brain-spec/structure-documents.json";
    private const string RuleFormat = "brain.rule@1";

    internal static void Register(ScenarioSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        var projector = RuleProjectorFactory.Create();
        using var conformance = JsonDocument.Parse(FixtureFiles.Read(ConformancePath));
        var index = -1;
        foreach (var item in conformance.RootElement.EnumerateArray())
        {
            index++;
            var document = item.GetProperty("document");
            if (document.GetProperty("$spec").GetString() != RuleFormat)
            {
                continue;
            }

            var contracts = item.GetProperty("context").GetProperty("contracts").EnumerateArray()
                .Select(name => FixtureFiles.Read($"tests/fixtures/spec/brain/contracts/contract-{name.GetString()}.json")).ToList();
            var prefix = $"spec/{index.ToString("D3", CultureInfo.InvariantCulture)}";
            AddCase(set, projector, prefix, document.GetRawText(), contracts, withTable: true);
        }

        using var structure = JsonDocument.Parse(FixtureFiles.Read(StructurePath));
        var position = 0;
        foreach (var item in structure.RootElement.EnumerateArray())
        {
            var contract = FixtureFiles.Read($"tests/fixtures/spec/brain/contracts/contract-{item.GetProperty("contract").GetString()}.json");
            AddCase(
                set,
                projector,
                $"spec/structure/{position.ToString(CultureInfo.InvariantCulture)}",
                item.GetProperty("document").GetRawText(),
                [contract],
                withTable: false);
            position++;
        }
    }

    private static void AddCase(
        ScenarioSet set, IRuleProjector projector, string prefix, string documentJson, IReadOnlyList<string> contracts, bool withTable)
    {
        IReadOnlyList<(string Code, string Path)> errors = [];
        ProjectedRule? rule = null;
        set.Add($"{prefix}/errors", () =>
        {
            rule = projector.Project(documentJson, contracts, out errors);
            return string.Join(",", errors.Select(error => $"{error.Code}@{error.Path}").Order(StringComparer.Ordinal));
        });

        if (!withTable || rule is null)
        {
            return;
        }

        var projected = rule;
        set.Add($"{prefix}/table", () => DescribeTable(projected.Table));
        set.Add($"{prefix}/goldens", () => DescribeGoldens(projected.Goldens));
        set.Add($"{prefix}/run", () => Run(projected));
    }

    private static string Run(ProjectedRule rule)
    {
        var result = GoldenRunner.Run(TableCompiler.Compile(rule.Table, rule.Kind), rule.Goldens, Fixtures.AsOf);
        return string.Join(
            "|",
            Format.Bool(result.IsOk),
            Format.Join(result.Failed.Select(failed => failed.Name)),
            Format.Join(result.UncoveredRowKeys),
            Format.Join(result.ToIssues().Select(issue => issue.Code)));
    }

    private static string DescribeTable(DecisionTable table)
    {
        var parts = new List<string> { "policy=" + table.HitPolicy };
        parts.AddRange(table.Rows.Select((row, position) => DescribeRow($"row{Format.Int(position)}", row)));
        parts.Add(table.DefaultRow is null ? "default=none" : DescribeRow("default", table.DefaultRow));
        return string.Join("\n", parts);
    }

    private static string DescribeRow(string label, Row row) =>
        $"{label} key={row.Key} conditions=[{string.Join(";", row.Conditions.Select(Describe))}] outcome={CanonicalOutcome(row.Outcome)} stored={row.Outcome}";

    private static string Describe(Condition condition) =>
        $"{condition.FactName}:{condition.Operator}:value={Describe(condition.Value)}:upper={Describe(condition.UpperValue)}"
        + $":lower={Format.Bool(condition.LowerInclusive)}:upper={Format.Bool(condition.UpperInclusive)}"
        + $":values=[{string.Join(",", condition.Values.Select(Describe))}]";

    private static string Describe(DecisionValue? value) => value switch
    {
        null => "null",
        CodeValue code => $"code({code.Value})",
        EnumValue label => $"enum({label.Value})",
        BooleanValue flag => $"bool({Format.Bool(flag.Value)})",
        IntegerValue integer => $"int({integer.Value.ToString(CultureInfo.InvariantCulture)})",
        DecimalValue number => $"dec({number.Value.ToString(CultureInfo.InvariantCulture)})",
        MoneyValue money => $"money({money.Amount.ToString(CultureInfo.InvariantCulture)},{money.Currency})",
        DateValue date => $"date({date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})",
        ReferenceValue reference => $"ref({reference.Value.ToString("D", CultureInfo.InvariantCulture)})",
        _ => throw new InvalidOperationException($"Unsupported decision value {value.GetType().Name}."),
    };

    /// <summary>
    /// The engine's own canonical outcome comparison form. The class is internal in both copies of the engine, so it is
    /// called through reflection, the same way against either.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields", Justification = "The canonical outcome form is internal in both engine copies and is the very form the parity compares; it is read through reflection from test code only.")]
    private static string CanonicalOutcome(string json)
    {
        var type = typeof(TableCompiler).Assembly.GetType("Nuvora.Decisions.Engine.Serialization.CanonicalOutcomeJson", throwOnError: true)!;
        var method = type.GetMethod("Canonicalize", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, [typeof(string)])!;
        return (string)method.Invoke(null, [json])!;
    }

    private static string DescribeGoldens(IReadOnlyList<GoldenCase> goldens) => string.Join(
        "\n",
        goldens.Select(golden => $"name={golden.Name} facts={CanonicalFacts.ToCanonicalJson(golden.Facts)} expect={DescribeExpectation(golden.Expected)}"));

    private static string DescribeExpectation(GoldenExpectation expectation) => expectation switch
    {
        ExpectedDecided decided => $"decided:{CanonicalOutcome(decided.OutcomeJson)}:{decided.RowKey ?? "-"}:stored={decided.OutcomeJson}",
        ExpectedNoDecision => "no_decision",
        _ => throw new InvalidOperationException($"Unsupported expectation {expectation.GetType().Name}."),
    };
}

/// <summary>Reads fixture files of the repository, found by walking up from the test assembly.</summary>
internal static class FixtureFiles
{
    internal static string Read(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        throw new FileNotFoundException("Could not locate the fixture file above the test assembly.", relativePath);
    }
}

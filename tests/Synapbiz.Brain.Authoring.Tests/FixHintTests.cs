using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nuvora.Decisions.Contracts.Kinds;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>Every code the loaders, validators and guided edits can give has a hint for the assistant. The codes are found by reflection over the constants, so a new code without a hint fails here.</summary>
public sealed partial class FixHintTests
{
    private static readonly Assembly[] Sources =
    [
        typeof(BrainProcessLoader).Assembly,
        typeof(BrainRuleLoader).Assembly,
        typeof(WorkbenchLocale).Assembly,
        typeof(BrainContract).Assembly,
        typeof(Nuvora.Decisions.Engine.Validation.TableValidator).Assembly,
    ];

    // Codes of the runtime of a case, not of a document: they are never given to someone writing a draft.
    private static readonly string[] RuntimePrefixes = ["process.trigger.", "process.input.", "process.variable.type_mismatch", "process.limits.", "process.run.", "process.history."];

    [GeneratedRegex(@"^(process|spec|brain|decisions|workbench)\.[a-zA-Z_]+(\.[a-zA-Z_]+)+$")]
    private static partial Regex CodeShape();

    internal static IEnumerable<(string Owner, string Code)> ConstantCodes() =>
        Sources.Distinct().SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string) && field.GetRawConstantValue() is string value && CodeShape().IsMatch(value))
                .Select(field => (type.FullName + "." + field.Name, (string)field.GetRawConstantValue()!)))
            .DistinctBy(item => item.Item2)
            .OrderBy(item => item.Item2, StringComparer.Ordinal);

    [Fact]
    public void The_codes_are_found_by_reflection_and_there_are_many()
    {
        ConstantCodes().Count().Should().BeGreaterThan(80);
        ConstantCodes().Select(item => item.Code).Should().Contain(["process.step.unknown", "process.cycle.unbounded", "spec.structure.schema"]);
    }

    [Fact]
    public void Every_document_code_has_a_hint()
    {
        var missing = ConstantCodes()
            .Where(item => !RuntimePrefixes.Any(prefix => item.Code.StartsWith(prefix, StringComparison.Ordinal)))
            .Where(item => string.IsNullOrWhiteSpace(FixHints.For(item.Code)))
            .Select(item => item.Code + " (" + item.Owner + ")")
            .ToList();
        missing.Should().BeEmpty("a code without a hint leaves the assistant guessing: add a sentence to FixHints");
    }

    [Fact]
    public void A_hint_is_one_plain_sentence_of_reasonable_length()
    {
        foreach (var code in FixHints.Codes)
        {
            var hint = FixHints.For(code)!;
            hint.Should().NotBeNullOrWhiteSpace(code);
            hint.Length.Should().BeLessThan(400, code);
            hint.Should().EndWith(".", code);
            hint.Should().NotContain("\n", code);
        }
    }
}

using System.Reflection;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

public sealed class FlowStringsTests
{
    private static readonly string[] Sources =
    [
        "FlowWorkbench.razor", "FlowWorkbench.razor.cs", "FlowWorkbench.Try.cs", "FlowWorkbench.Review.cs", "FlowData.cs", "DiagramLayout.cs", "FlowWork.cs", "FlowWorkbenchSession.cs",
    ];

    private static string Source(string file) => Fx.Read("src/Synapbiz.Brain.Workbench.Blazor/" + file);

    private static List<string> Literals() =>
        [.. Sources.SelectMany(file => Regex.Matches(Source(file), "\"(flow\\.[A-Za-z0-9.]*)\"").Select(match => match.Groups[1].Value))];

    private static IEnumerable<string> FlowKeys => WorkbenchStrings.Keys.Where(key => key.StartsWith("flow.", StringComparison.Ordinal));

    [Fact]
    public void EveryFlowKeyHasBothLanguagesAndTheFlowKeysAreMoreThanAHundred()
    {
        FlowKeys.Should().HaveCountGreaterThan(100);
        foreach (var key in FlowKeys)
        {
            WorkbenchStrings.Default(key, WorkbenchLocale.Es).Should().NotBeNullOrWhiteSpace(key);
            WorkbenchStrings.Default(key, WorkbenchLocale.En).Should().NotBeNullOrWhiteSpace(key);
        }
    }

    [Fact]
    public void TheSpanishTextsAreNotTheEnglishOnesExceptForPunctuationAndNamesAndTheyKeepTheirPlaceholders()
    {
        var same = new[] { "flow.join", "flow.join.label", "flow.who.source", "flow.field.no", "flow.coverage.untriedRoute", "flow.review.routeOf", "flow.ai.field" };
        foreach (var key in FlowKeys.Where(key => !same.Contains(key, StringComparer.Ordinal)))
        {
            WorkbenchStrings.Default(key, WorkbenchLocale.Es).Should().NotBe(WorkbenchStrings.Default(key, WorkbenchLocale.En), key);
        }

        foreach (var key in FlowKeys)
        {
            Placeholders(WorkbenchStrings.Default(key, WorkbenchLocale.Es)).Should().BeEquivalentTo(Placeholders(WorkbenchStrings.Default(key, WorkbenchLocale.En)), key);
        }
    }

    private static string[] Placeholders(string text) => [.. Regex.Matches(text, "\\{[a-zA-Z]+\\}").Select(match => match.Value).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void EveryFlowKeyIsUsedByThePanelAndEveryKeyThePanelNamesIsInTheTable()
    {
        var literals = Literals();
        var keys = FlowKeys.ToList();
        foreach (var key in keys)
        {
            var plain = Regex.Replace(key, "\\.(one|other)$", string.Empty);
            var used = literals.Contains(key, StringComparer.Ordinal)
                || literals.Contains(plain, StringComparer.Ordinal)
                || literals.Any(literal => literal.EndsWith('.') && key.StartsWith(literal, StringComparison.Ordinal))
                || literals.Any(literal => !literal.EndsWith('.') && plain.StartsWith(literal + ".", StringComparison.Ordinal) && plain.Count(c => c == '.') == literal.Count(c => c == '.') + 1);
            used.Should().BeTrue(key + " is in the table but the panel never uses it");
        }

        foreach (var literal in literals.Where(literal => !literal.EndsWith('.')).Distinct(StringComparer.Ordinal))
        {
            var known = keys.Contains(literal, StringComparer.Ordinal)
                || keys.Contains(literal + ".one", StringComparer.Ordinal)
                || keys.Any(key => key.StartsWith(literal + ".", StringComparison.Ordinal));
            known.Should().BeTrue(literal + " is used by the panel but is not in the table");
        }
    }

    /// <summary>The markup of the razor file with every <c>@expression</c> taken out, including the ones with balanced parentheses.</summary>
    private static string WithoutExpressions(string markup)
    {
        var result = new System.Text.StringBuilder();
        var position = 0;
        while (position < markup.Length)
        {
            if (markup[position] != '@')
            {
                result.Append(markup[position]);
                position++;
                continue;
            }

            position++;
            while (position < markup.Length && (char.IsLetterOrDigit(markup[position]) || markup[position] is '_' or '.' or '?'))
            {
                position++;
            }

            if (position < markup.Length && markup[position] is '(' or '{')
            {
                position = AfterBalanced(markup, position);
            }
        }

        return result.ToString();
    }

    private static int AfterBalanced(string text, int start)
    {
        var open = text[start];
        var close = open == '(' ? ')' : '}';
        var depth = 0;
        var position = start;
        while (position < text.Length)
        {
            if (text[position] == open)
            {
                depth++;
            }
            else if (text[position] == close)
            {
                depth--;
            }

            position++;
            if (depth == 0)
            {
                break;
            }
        }

        return position;
    }

    [Fact]
    public void NoTextOrNamedAttributeOfThePanelIsWrittenOutsideTheTable()
    {
        var markup = Source("FlowWorkbench.razor");
        var template = markup[..markup.IndexOf("@code {", StringComparison.Ordinal)];
        var cleaned = WithoutExpressions(template);
        Regex.Matches(cleaned, ">([^<>\\n]*[A-Za-z]{2,}[^<>\\n]*)<").Select(match => match.Groups[1].Value.Trim()).Should().BeEmpty();
        Regex.Matches(markup, "\\b(aria-label|title|placeholder|alt)=\"([^\"@]*[A-Za-z][^\"@]*)\"").Select(match => match.Value).Should().BeEmpty();

        // The fragments below the code marker are templates too: their text nodes are expressions or markup the table fills.
        var fragments = WithoutExpressions(Regex.Replace(markup[markup.IndexOf("@code {", StringComparison.Ordinal)..], "^\\s*//.*$", string.Empty, RegexOptions.Multiline));
        Regex.Matches(fragments, "<(span|div|p|li|dt|dd|h[1-6]|strong|button|label|summary|td|th)[^>]*>([^<>\\n{}]*[A-Za-z]{2,}[^<>\\n{}]*)</").Select(match => match.Groups[2].Value.Trim()).Should().BeEmpty();
    }

    [Fact]
    public void ThePanelWritesNoPersonFacingTextInItsCodeEither()
    {
        foreach (var file in new[] { "FlowWorkbench.razor.cs", "FlowWorkbench.Try.cs", "FlowWorkbench.Review.cs", "FlowData.cs", "FlowWork.cs", "FlowWorkbenchSession.cs", "DiagramLayout.cs" })
        {
            var code = Regex.Replace(Source(file), "^\\s*///.*$|^\\s*//.*$", string.Empty, RegexOptions.Multiline);
            var words = Regex.Matches(code, "\"([^\"\\\\\\r\\n]*)\"").Select(match => match.Groups[1].Value)
                .Where(text => Regex.IsMatch(text, "[A-Za-z]{3,} [A-Za-z]{3,}")).ToList();
            words.Should().BeEmpty(file);
        }
    }

    [Fact]
    public void TheMarkingsOfTheFlowPanelsAreMandatory()
    {
        WorkbenchStrings.Keys.Should().Contain(["flow.band.try", "flow.band.tests"]);
        var type = typeof(WorkbenchStrings).GetMethod("IsMandatory", BindingFlags.NonPublic | BindingFlags.Static);
        type.Should().NotBeNull();
        ((bool)type!.Invoke(null, ["flow.band.try"])!).Should().BeTrue();
        ((bool)type.Invoke(null, ["flow.band.tests"])!).Should().BeTrue();
        ((bool)type.Invoke(null, ["flow.tabs.read"])!).Should().BeFalse();
    }

    [Fact]
    public void ThePluralKeysOfThePanelComeInPairsAndTheCountIsFilledIn()
    {
        foreach (var one in FlowKeys.Where(key => key.EndsWith(".one", StringComparison.Ordinal)))
        {
            WorkbenchStrings.Keys.Should().Contain(one[..^4] + ".other");
        }

        WorkbenchStrings.Default("flow.list.count.other", WorkbenchLocale.En).Should().Contain("{n}");
    }

    [Fact]
    public void ThePanelTakesTheFixedParametersOfTheSpec()
    {
        var parameters = typeof(FlowWorkbench).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<ParameterAttribute>() is not null)
            .Select(property => property.Name);
        parameters.Should().BeEquivalentTo("Flows", "Locale", "Session", "Now", "Zones", "AnswerSource", "OnExport", "TextOverrides");
    }

    [Fact]
    public void AnOverrideOfAnOrdinaryFlowStringMayBeEmptyAndTheHostsWordsAreEncoded()
    {
        using var harness = new FlowHarness();
        harness.Render([FlowHarness.One("previsit.outreach")], overrides: new Dictionary<string, string> { ["flow.tabs.review"] = "<b>Look</b> & **see**" });
        harness.Tab(FlowWorkbenchTab.Review).InnerHtml.Should().Contain("&lt;b&gt;Look&lt;/b&gt; &amp; <strong>see</strong>");
    }
}

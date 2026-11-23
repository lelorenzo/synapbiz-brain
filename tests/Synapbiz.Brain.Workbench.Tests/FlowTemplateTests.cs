using System.Text.RegularExpressions;
using FluentAssertions;
using Synapbiz.Brain.Workbench.Flows;
using Xunit;

namespace Synapbiz.Brain.Workbench.Tests;

public sealed partial class FlowTemplateTests
{
    private static readonly string[] Units = ["day", "hour", "minute", "second"];

    [GeneratedRegex(@"\{([a-zA-Z_]+)\}")]
    private static partial Regex Placeholder();

    private static string[] Names(string text) => [.. Placeholder().Matches(text).Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal)];

    [Fact]
    public void Both_languages_have_the_same_keys()
    {
        FlowTemplates.EnglishKeys.Order(StringComparer.Ordinal).Should().Equal(FlowTemplates.SpanishKeys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Both_languages_use_the_same_placeholders()
    {
        foreach (var key in FlowTemplates.EnglishKeys)
        {
            Names(FlowTemplates.Template(WorkbenchLocale.Es, key)).Should().Equal(Names(FlowTemplates.Template(WorkbenchLocale.En, key)), key);
        }
    }

    [Fact]
    public void Spanish_differs_from_English_unless_the_key_is_listed()
    {
        foreach (var key in FlowTemplates.EnglishKeys)
        {
            var same = FlowTemplates.Template(WorkbenchLocale.Es, key) == FlowTemplates.Template(WorkbenchLocale.En, key);
            same.Should().Be(FlowTemplates.IntentionallyEqual.Contains(key), key);
        }
    }

    [Fact]
    public void The_listed_exceptions_exist()
    {
        FlowTemplates.IntentionallyEqual.Should().OnlyContain(key => FlowTemplates.Has(WorkbenchLocale.En, key));
    }

    [Fact]
    public void Filling_a_template_replaces_every_placeholder()
    {
        foreach (var key in FlowTemplates.EnglishKeys)
        {
            foreach (var locale in new[] { WorkbenchLocale.En, WorkbenchLocale.Es })
            {
                var parameters = Names(FlowTemplates.Template(locale, key)).Distinct().Select(name => (name, "VALUE")).ToArray();
                var text = FlowTemplates.Text(locale, key, parameters);
                Placeholder().IsMatch(text).Should().BeFalse(key);
            }
        }
    }

    [Fact]
    public void Every_template_is_used_by_the_code()
    {
        // No recorder in the library: a key is used when its text is written in the source of the flow surface, or when it belongs to a
        // family the code builds from a closed domain (kinds, weekdays, duration units, and the route keys the documents lead to).
        var folder = Path.GetDirectoryName(TestFiles.Locate("src/Synapbiz.Brain.Workbench/Flows/FlowDescriber.cs"))!;
        var source = string.Concat(Directory.GetFiles(folder, "*.cs").Where(file => Path.GetFileName(file) != "FlowTemplates.cs").Select(File.ReadAllText));
        var keys = FlowTemplates.EnglishKeys.ToHashSet(StringComparer.Ordinal);
        var used = LiteralPattern().Matches(source).Select(match => match.Groups[1].Value).Where(keys.Contains).ToHashSet(StringComparer.Ordinal);
        used.UnionWith(Enum.GetValues<Synapbiz.Brain.Process.OutlineStepKind>().Select(kind => "kind." + FlowDescriber.KindKey(kind)));
        used.UnionWith(Enumerable.Range(0, 7).Select(day => "day." + day.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        used.UnionWith(Units.SelectMany(unit => new[] { "duration." + unit + ".one", "duration." + unit + ".many" }));
        foreach (var (_, process) in FlowDocs.All)
        {
            var outline = Synapbiz.Brain.Process.ProcessOutline.From(process);
            used.UnionWith(outline.Steps.SelectMany(step => step.Routes.Select(route => FlowDescriber.RouteKey(step, route, WorkbenchLocale.En))));
        }

        string.Join(", ", keys.Except(used).Order(StringComparer.Ordinal)).Should().BeEmpty("a template nothing uses is dead text, and a route template no document leads to needs a fixture");
        used.Except(keys).Should().BeEmpty("a key the code names must exist");
    }

    [Fact]
    public void The_duration_words_of_every_unit_render_in_both_languages()
    {
        FlowText.Duration(WorkbenchLocale.En, TimeSpan.Parse("1.01:01:01", System.Globalization.CultureInfo.InvariantCulture)).Should().Be("1 day, 1 hour, 1 minute and 1 second");
        FlowText.Duration(WorkbenchLocale.En, TimeSpan.Parse("2.02:02:02", System.Globalization.CultureInfo.InvariantCulture)).Should().Be("2 days, 2 hours, 2 minutes and 2 seconds");
        FlowText.Duration(WorkbenchLocale.Es, TimeSpan.Parse("2.02:02:02", System.Globalization.CultureInfo.InvariantCulture)).Should().Be("2 días, 2 horas, 2 minutos y 2 segundos");
        FlowText.Duration(WorkbenchLocale.Es, TimeSpan.Parse("1.01:01:01", System.Globalization.CultureInfo.InvariantCulture)).Should().Be("1 día, 1 hora, 1 minuto y 1 segundo");
    }

    [GeneratedRegex("\"([a-z][A-Za-z]*(?:\\.[A-Za-z0-9_]+)+)\"")]
    private static partial Regex LiteralPattern();

    [Fact]
    public void An_unknown_key_is_a_defect_and_not_silent()
    {
        FluentActions.Invoking(() => FlowTemplates.Text(WorkbenchLocale.En, "no.such.key")).Should().Throw<Exception>();
    }
}

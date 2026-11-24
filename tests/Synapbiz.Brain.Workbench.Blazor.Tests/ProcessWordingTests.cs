using System.Text.RegularExpressions;
using FluentAssertions;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The owner's wording (2026-11-23): every default text of the panels says "process" ("proceso"), never "flow" ("flujo"). Code names and the <c>flow.</c> keys stay.</summary>
public sealed partial class ProcessWordingTests
{
    [GeneratedRegex(@"\b(flows?|flujos?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Forbidden();

    [GeneratedRegex(@"\{\w+\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    [Fact]
    public void No_default_text_of_either_language_says_flow_or_flujo()
    {
        WorkbenchStrings.Keys.Should().HaveCountGreaterThan(100);
        foreach (var key in WorkbenchStrings.Keys)
        {
            foreach (var locale in new[] { WorkbenchLocale.En, WorkbenchLocale.Es })
            {
                var text = Placeholder().Replace(WorkbenchStrings.Default(key, locale), string.Empty);
                Forbidden().IsMatch(text).Should().BeFalse(key + " (" + locale + "): " + text);
            }
        }
    }

    [Fact]
    public void The_keys_keep_their_prefix_so_hosts_that_override_a_text_by_key_do_not_break()
    {
        WorkbenchStrings.Keys.Should().Contain(["flow.list.title", "flow.tabs.read", "flow.list.empty"]);
    }
}

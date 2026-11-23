using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>Renders the workbench with the usual parameters and offers short ways to find and press things.</summary>
internal sealed class Harness : IDisposable
{
    internal Harness() => Ctx.JSInterop.Mode = JSRuntimeMode.Loose;

    internal TestContext Ctx { get; } = new();

    internal WorkbenchSession Session { get; private set; } = new();

    internal List<WorkbenchExport> Exports { get; } = [];

    internal IRenderedComponent<RuleWorkbench> Cut { get; private set; } = default!;

    internal Func<DateTimeOffset> Now { get; set; } = () => Fx.Moment;

    internal IRenderedComponent<RuleWorkbench> Render(
        IReadOnlyList<WorkbenchRule>? rules,
        WorkbenchLocale locale = WorkbenchLocale.En,
        WorkbenchSession? session = null,
        bool withExport = true,
        string? place = "the demo folder",
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        Session = session ?? Session;
        Cut = Ctx.RenderComponent<RuleWorkbench>(parameters =>
        {
            parameters
                .Add(component => component.Rules, rules)
                .Add(component => component.Locale, locale)
                .Add(component => component.Session, Session)
                .Add(component => component.Now, Now)
                .Add(component => component.TextOverrides, overrides);
            if (withExport)
            {
                parameters.Add(component => component.OnExport, export =>
                {
                    Exports.Add(export);
                    return Task.FromResult(place);
                });
            }
        });
        return Cut;
    }

    internal IElement Find(string selector) => Cut.Find(selector);

    internal IReadOnlyList<IElement> FindAll(string selector) => Cut.FindAll(selector);

    internal IElement Tab(WorkbenchTab tab) => Cut.FindAll("[role=tab]")[(int)tab];

    internal void Open(WorkbenchTab tab) => Tab(tab).Click();

    internal IElement ButtonByText(string text) =>
        Cut.FindAll("[role=tabpanel] button").First(button => string.Equals(button.TextContent.Trim(), text, StringComparison.Ordinal));

    internal void Press(string text) => ButtonByText(text).Click();

    /// <summary>The text of the open tab panel.</summary>
    internal string PanelText => Normalise(Find("[role=tabpanel]").TextContent);

    /// <summary>The text of the whole detail side.</summary>
    internal string DetailText => Normalise(Find(".wb-detail").TextContent);

    internal static string Normalise(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>The input, select or textarea whose label has exactly this text.</summary>
    internal IElement Input(string label)
    {
        var owner = Cut.FindAll("label.wb-label").First(item => string.Equals(Normalise(item.TextContent), label, StringComparison.Ordinal));
        return Cut.Find("#" + owner.GetAttribute("for"));
    }

    internal void Type(string label, string value) => Input(label).Change(value);

    /// <summary>The radio group whose caption has exactly this text.</summary>
    internal IElement Group(string caption)
    {
        var owner = Cut.FindAll("span.wb-label").First(item => string.Equals(Normalise(item.TextContent), caption, StringComparison.Ordinal));
        return Cut.Find("[aria-labelledby=" + owner.Id + "]");
    }

    internal void Choose(string caption, string value) =>
        Group(caption).QuerySelectorAll("input").First(item => item.GetAttribute("value") == value).Change(value);

    internal void Select(WorkbenchRule rule) => Session.SelectedRuleKey = rule.Key;

    public void Dispose() => Ctx.Dispose();
}

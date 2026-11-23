using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench.Blazor;
using Synapbiz.Brain.Workbench.Flows;
using Synapbiz.Brain.Workbench.Tests;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>Renders the flow workbench with the usual parameters and offers short ways to find and press things.</summary>
internal sealed class FlowHarness : IDisposable
{
    private const string FocusCall = "Blazor._internal.domWrapper.focus";

    private string? focusedHtml;

    internal FlowHarness()
    {
        Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        Ctx.JSInterop.SetupVoid(call => call.Identifier == FocusCall && NoteFocus(call));
    }

    internal TestContext Ctx { get; } = new();

    internal FlowWorkbenchSession Session { get; private set; } = new();

    internal List<FlowWorkbenchExport> Exports { get; } = [];

    internal IRenderedComponent<FlowWorkbench> Cut { get; private set; } = default!;

    internal Func<DateTimeOffset> Now { get; set; } = () => Fx.Moment;

    internal static readonly IReadOnlyDictionary<string, TimeSpan> Zones = new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
    {
        ["America/New_York"] = TimeSpan.FromHours(-5),
        ["America/Chicago"] = TimeSpan.FromHours(-6),
        ["America/Denver"] = TimeSpan.FromHours(-7),
        ["America/Los_Angeles"] = TimeSpan.FromHours(-8),
    };

    /// <summary>The six reference documents as flows, in the order of the reference list.</summary>
    internal static IReadOnlyList<WorkbenchFlow> Reference() =>
    [.. FlowDocs.ReferenceIds.Select(id => new WorkbenchFlow
    {
        Key = id,
        Group = "Reference flows",
        Process = FlowDocs.Process(id),
    })];

    internal static WorkbenchFlow One(string id) => Reference().First(flow => flow.Key == id);

    internal IRenderedComponent<FlowWorkbench> Render(
        IReadOnlyList<WorkbenchFlow>? flows,
        WorkbenchLocale locale = WorkbenchLocale.En,
        FlowWorkbenchSession? session = null,
        bool withExport = true,
        string? place = "the demo folder",
        IReadOnlyDictionary<string, string>? overrides = null,
        IFlowAnswerSource? source = null,
        IReadOnlyDictionary<string, TimeSpan>? zones = null)
    {
        Session = session ?? Session;
        Cut = Ctx.RenderComponent<FlowWorkbench>(parameters =>
        {
            parameters
                .Add(component => component.Flows, flows)
                .Add(component => component.Locale, locale)
                .Add(component => component.Session, Session)
                .Add(component => component.Now, Now)
                .Add(component => component.Zones, zones ?? Zones)
                .Add(component => component.AnswerSource, source)
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

    internal IElement Tab(FlowWorkbenchTab tab) => Cut.FindAll("[role=tab]")[(int)tab];

    internal void Open(FlowWorkbenchTab tab) => Tab(tab).Click();

    internal IReadOnlyList<IElement> Buttons => Cut.FindAll("[role=tabpanel] button");

    internal IElement ButtonByText(string text) =>
        Buttons.First(button => string.Equals(Normalise(button.TextContent), text, StringComparison.Ordinal));

    internal bool HasButton(string text) => Buttons.Any(button => string.Equals(Normalise(button.TextContent), text, StringComparison.Ordinal));

    internal void Press(string text) => ButtonByText(text).Click();

    internal string PanelText => Normalise(Find("[role=tabpanel]").TextContent);

    internal string DetailText => Normalise(Find(".wb-detail").TextContent);

    internal string ListText => Normalise(Find(".wb-list-panel").TextContent);

    internal bool IsFocused(IElement element) => focusedHtml is not null && string.Equals(focusedHtml, WithoutReference(element.OuterHtml), StringComparison.Ordinal);

    internal int FocusCount => Ctx.JSInterop.Invocations.Count(call => call.Identifier == FocusCall);

    private static string WithoutReference(string html) => System.Text.RegularExpressions.Regex.Replace(html, " blazor:elementreference=\"[^\"]*\"", string.Empty);

    /// <summary>When set, the focus call throws as the browser does when its element is gone or cannot take focus; the panel must go on.</summary>
    internal Exception? FocusThrows { get; set; }

    /// <summary>The ids of the element references the panel asked the browser to focus, in order.</summary>
    internal List<string> FocusIds { get; } = [];

    private bool NoteFocus(Bunit.JSRuntimeInvocation call)
    {
        var id = ((ElementReference)call.Arguments[0]!).Id;
        FocusIds.Add(id);
        var found = Cut?.FindAll("[blazor\\:elementreference=\"" + id + "\"]");
        focusedHtml = found is { Count: > 0 } ? WithoutReference(found[0].OuterHtml) : null;
        if (FocusThrows is not null)
        {
            throw FocusThrows;
        }

        return true;
    }

    /// <summary>The element reference the panel itself holds for a key such as <c>note:process</c>: what it asks the browser to focus.</summary>
    internal ElementReference RefOf(string key) =>
        ((Dictionary<string, ElementReference>)typeof(FlowWorkbench).GetField("refs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(Cut.Instance)!)[key];

    /// <summary>Sets what the panel will focus after its next render, as a click that asks for focus does.</summary>
    internal void AskFocus(string key) =>
        typeof(FlowWorkbench).GetField("pendingFocus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(Cut.Instance, key);

    internal static string Normalise(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>The input, select or textarea whose label has exactly this text.</summary>
    internal IElement Input(string label)
    {
        var owner = Cut.FindAll("label.wb-label").First(item => string.Equals(Normalise(item.TextContent), label, StringComparison.Ordinal));
        return Cut.Find("#" + owner.GetAttribute("for"));
    }

    internal void Type(string label, string value) => Input(label).Change(value);

    private IElement? AnswerCard() =>
        FindAll("section.wb-wait").FirstOrDefault(card => card.QuerySelectorAll("button").Any(button => Normalise(button.TextContent) == "Use this answer"));

    /// <summary>
    /// Answers the waiting effect from a test: picks the scenario in the select of its card (the first one that does not fail when none
    /// is named) and presses "Use this answer". False when no card offers an answer from a test.
    /// </summary>
    internal bool UseAnswer(string? scenario = null)
    {
        var card = AnswerCard();
        if (card is null)
        {
            return false;
        }

        var options = card.QuerySelectorAll("select option").ToList();
        var option = scenario is not null
            ? options.FirstOrDefault(item => item.GetAttribute("value") == scenario)
            : options.FirstOrDefault(item => !item.TextContent.Contains("(fails)", StringComparison.Ordinal));
        if (option is null)
        {
            return false;
        }

        card.QuerySelector("select")!.Change(option.GetAttribute("value")!);
        AnswerCard()!.QuerySelectorAll("button").First(button => Normalise(button.TextContent) == "Use this answer").Click();
        return true;
    }

    internal void Select(string key) => Session.SelectedFlowKey = key;

    internal void Start(string id, string? scenario = null)
    {
        Select(id);
        Open(FlowWorkbenchTab.Try);
        if (scenario is not null)
        {
            Cut.Find("#" + Cut.FindAll("label.wb-label").First(item => Normalise(item.TextContent) == "Start like the test").GetAttribute("for")).Change(scenario);
        }

        Press("Start");
    }

    public void Dispose() => Ctx.Dispose();
}

using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Blazor;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>Short ways to drive the draft view of the Read panel: the rows, their buttons and the row form.</summary>
internal static class Ed
{
    internal static Harness Open(WorkbenchLocale locale = WorkbenchLocale.En, WorkbenchRule? rule = null, string name = "intake.first")
    {
        var harness = new Harness();
        harness.Render([rule ?? Fx.Editable(name, locale)], locale);
        harness.Find(".wb-head button").Click();
        return harness;
    }

    /// <summary>The texts joined by a bar, so that a list reads as one string in an assertion.</summary>
    internal static string Join(IEnumerable<string> texts) => string.Join("|", texts);

    internal static bool IsEs(WorkbenchLocale locale) => locale == WorkbenchLocale.Es;

    /// <summary>The rows of the panel (the numbered ones, the removed ones and the fallback), without the form.</summary>
    internal static IReadOnlyList<IElement> Rows(Harness harness) => harness.FindAll("[role=tabpanel] .wb-row");

    internal static IElement Row(Harness harness, int number) => Rows(harness)[number - 1];

    /// <summary>The button of an element with this exact text; fails when there is none.</summary>
    internal static IElement Button(IElement root, string text) =>
        root.QuerySelectorAll("button").First(button => string.Equals(Harness.Normalise(button.TextContent), text, StringComparison.Ordinal));

    internal static bool HasButton(IElement root, string text) =>
        root.QuerySelectorAll("button").Any(button => string.Equals(Harness.Normalise(button.TextContent), text, StringComparison.Ordinal));

    /// <summary>Presses the "Edit" button of a numbered row, found by its name so that an open form does not shift the numbers.</summary>
    internal static void Edit(Harness harness, int number, WorkbenchLocale locale = WorkbenchLocale.En) =>
        harness.Find("[role=tabpanel] button[aria-label='" + (IsEs(locale) ? "Editar la fila " : "Edit row ") + number + "']").Click();

    internal static IElement Form(Harness harness) => harness.Find(".wb-rowform");

    internal static IElement Line(Harness harness, string label) =>
        harness.FindAll(".wb-rowform .wb-fline").First(line => string.Equals(Harness.Normalise(line.QuerySelector(".wb-flabel")!.TextContent), label, StringComparison.Ordinal));

    /// <summary>The operator select of a fact line.</summary>
    internal static IElement Operator(Harness harness, string label) => Line(harness, label).QuerySelector("select")!;

    internal static void Choose(Harness harness, string label, string wording)
    {
        var select = Operator(harness, label);
        var option = select.QuerySelectorAll("option").First(item => string.Equals(item.TextContent, wording, StringComparison.Ordinal));
        select.Change(option.GetAttribute("value")!);
    }

    internal static string Preview(Harness harness) => Harness.Normalise(harness.Find(".wb-rowform .wb-preview-t").TextContent);

    internal static void Apply(Harness harness, WorkbenchLocale locale = WorkbenchLocale.En) => Button(Form(harness), IsEs(locale) ? "Aplicar a mi borrador" : "Apply to my draft").Click();

    internal static void Cancel(Harness harness, WorkbenchLocale locale = WorkbenchLocale.En) => Button(Form(harness), IsEs(locale) ? "Cancelar" : "Cancel").Click();

    /// <summary>Sets the text of the value input of a fact line (the first or, with <paramref name="second"/>, the second).</summary>
    internal static void Type(Harness harness, string label, string value, bool second = false)
    {
        var inputs = Line(harness, label).QuerySelectorAll(".wb-fvalue input:not([type=checkbox])");
        inputs[second ? 1 : 0].Change(value);
    }

    internal static void Pick(Harness harness, string label, string optionText)
    {
        var select = Line(harness, label).QuerySelector(".wb-fvalue select")!;
        select.Change(select.QuerySelectorAll("option").First(item => item.TextContent == optionText).GetAttribute("value")!);
    }

    internal static void PickValue(Harness harness, string label, string value) => Line(harness, label).QuerySelector(".wb-fvalue select")!.Change(value);

    internal static IElement OutcomeInput(Harness harness, string label) => Line(harness, label).QuerySelector(".wb-fvalue input, .wb-fvalue select")!;

    internal static string RowSentence(Harness harness, int number) => Harness.Normalise(Row(harness, number).QuerySelector(".wb-sent")!.TextContent);

    internal static string Tags(IElement row) => string.Join("|", row.QuerySelectorAll(".wb-rowctl .wb-pill").Select(item => item.TextContent));

    /// <summary>Applies steps to the draft of the only rule without a form, as a menu action would.</summary>
    internal static void Step(Harness harness, DraftStep step, string key = "intake.first")
    {
        var host = harness.Cut.Instance.Rules!.Single(item => item.Key == key);
        var draft = harness.Session.Work(key).Draft!;
        var result = DraftEngine.Apply(host, draft, [.. draft.Steps, step]);
        Assert.True(result.IsOk, string.Join("; ", result.Problems.Select(item => item.Code)));
        harness.Session.Touch();
    }

    internal static DraftStep Outcome(string row, string field, DecisionValue value) => new(StepKind.Row, row, [new SetOutcome(row, field, value)]);

    /// <summary>The intake rule with its text changed by <paramref name="change"/>, offered for editing with the intake contract.</summary>
    internal static WorkbenchRule Variant(Action<JsonNode> change, WorkbenchLocale locale = WorkbenchLocale.En)
    {
        var node = JsonNode.Parse(Fx.TextOf("intake.first"))!;
        change(node);
        return Fx.EditableText("intake.first", node.ToJsonString(), locale, Fx.ContractOf("intake.first"));
    }

    /// <summary>The intake rule without the test case that expects a row, so the row can be removed.</summary>
    internal static WorkbenchRule WithoutCaseFor(string rowKey, WorkbenchLocale locale = WorkbenchLocale.En) => Variant(
        node =>
        {
            var goldens = node["body"]!["goldens"]!.AsArray();
            var kept = goldens.Where(golden => (string?)golden!["expect"]!["row"] != rowKey).Select(golden => golden!.DeepClone()).ToList();
            goldens.Clear();
            foreach (var golden in kept)
            {
                goldens.Add(golden);
            }
        },
        locale);

    internal static void OpenMenu(Harness harness, int number, WorkbenchLocale locale = WorkbenchLocale.En) => Button(Row(harness, number), IsEs(locale) ? "Más" : "More").Click();

    /// <summary>Opens the "More" menu of a test case line and chooses one of its items.</summary>
    internal static void CaseAction(Harness harness, IElement line, string item, WorkbenchLocale locale = WorkbenchLocale.En)
    {
        Button(line, IsEs(locale) ? "Más" : "More").Click();
        MenuItem(harness, item).Click();
    }

    internal static IElement MenuItem(Harness harness, string text) => harness.Find(".wb-menu").QuerySelectorAll("button").First(item => item.TextContent == text);

    internal static DraftState Draft(Harness harness) => harness.Session.Work("intake.first").Draft!;
}

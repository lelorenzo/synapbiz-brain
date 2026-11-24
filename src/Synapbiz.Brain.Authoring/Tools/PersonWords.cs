using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Authoring;

/// <summary>
/// Turns what the loaders, the interpreter and the checks found into sentences for a person. Only the code is used, never the text the engine,
/// the schema validator or the platform wrote: a code that has no sentence gets one generic sentence, and the raw detail goes to the data of
/// the answer, for the assistant.
/// </summary>
internal static partial class PersonWords
{
    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex("^[a-z][a-z_.]*$")]
    private static partial Regex PlainId();

    /// <summary>
    /// A sentence of the workbench that marks names with backticks (its screen shows them as code), as a person reads it: every marked name is put
    /// in quotes, and an identifier made only of lower-case words (<c>claim_status</c>, <c>records.note_status</c>) is written as the words it is made of.
    /// </summary>
    internal static string Plain(string? sentence) =>
        sentence is null ? string.Empty : CodeSpan().Replace(sentence, match => FlowChangeDescriber.Quote(PlainId().IsMatch(match.Groups[1].Value) ? WorkbenchText.Humanize(match.Groups[1].Value) : match.Groups[1].Value));

    /// <summary>The sentence for a code of the loaders and the interpreter, with a phrase that says where.</summary>
    internal static string Error(WorkbenchLocale locale, string code, string where)
    {
        var key = AuthoringText.ErrorPrefix + code;
        return AuthoringText.Text(locale, AuthoringText.Has(key) ? key : "err.generic", ("where", where));
    }

    /// <summary>The sentence for an error of a loaded document, placed with the labels the document carries.</summary>
    internal static string Error(WorkbenchLocale locale, BrainSpecError error, JsonObject? document) =>
        Error(locale, error.Code, PathWords.Describe(error.Path, document, locale));

    /// <summary>The sentence for a code met while a case runs: it says where with the step the case is at, or with nothing in particular.</summary>
    internal static string Runtime(WorkbenchLocale locale, string? code, string? stepLabel)
    {
        var where = stepLabel is null
            ? AuthoringText.Text(locale, "where.case")
            : AuthoringText.Text(locale, "path.in.step", ("step", FlowChangeDescriber.Quote(stepLabel)));
        return Error(locale, code ?? "unknown", where);
    }

    internal static string PatchReason(WorkbenchLocale locale, JsonPatchException error, JsonObject? document)
    {
        var key = "patch.reason." + error.Reason;
        var where = error.Path is null ? AuthoringText.Text(locale, "path.in.document") : PathWords.Describe(error.Path, document, locale);
        return AuthoringText.Text(locale, AuthoringText.Has(key) ? key : "patch.reason.missing", ("where", where));
    }

    /// <summary>The sentence for an error found in a test (a scenario) that is not part of the document yet.</summary>
    internal static string ScenarioProblem(WorkbenchLocale locale, BrainSpecError error, JsonObject? scenario) =>
        Error(locale, error.Code, ScenarioWhere(error.Path, locale));

    internal static string ScenarioWhere(string path, WorkbenchLocale locale)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string T(string key, params (string, string)[] values) => AuthoringText.Text(locale, key, values);
        if (parts.Length >= 2 && parts[0] == "events" && int.TryParse(parts[1], out var index))
        {
            return T("where.test.event", ("n", AuthoringText.Number(index + 1L)));
        }

        return parts.Length == 0
            ? T("where.test")
            : parts[0] switch
            {
                "zones" => T("where.test.zones"),
                "start" => T("where.test.start"),
                "expect" => T("where.test.expect"),
                "name" => T("where.test.name"),
                _ => T("where.test"),
            };
    }

    /// <summary>The name of a fact or result field for a person: its label in the rule, else its identifier written as words.</summary>
    internal static string FactName(string name, RuleLabels labels)
    {
        if (labels.Facts.TryGetValue(name, out var fact))
        {
            return FlowChangeDescriber.Quote(fact);
        }

        return FlowChangeDescriber.Quote(labels.OutcomeFields.TryGetValue(name, out var field) ? field : WorkbenchText.Humanize(name));
    }

    /// <summary>The sentence for an edit of a rule that cannot be used.</summary>
    internal static string EditProblemSentence(WorkbenchLocale locale, AuthoringProblemException problem, RuleLabels labels)
    {
        var name = problem.Name is null ? string.Empty : FactName(problem.Name, labels);
        if (problem.Code.StartsWith("value:", StringComparison.Ordinal))
        {
            return Error(locale, problem.Code["value:".Length..], AuthoringText.Text(locale, "where.fact", ("name", name)));
        }

        var key = "edit." + problem.Code;
        return AuthoringText.Text(locale, AuthoringText.Has(key) ? key : "edit.other", ("name", name), ("member", problem.Name is null ? string.Empty : FlowChangeDescriber.Quote(problem.Name)));
    }

    /// <summary>
    /// What a test that does not pass did wrong, in words. The test player writes its findings in English from a few fixed forms; each form is
    /// recognised and told again with the labels of the process. A finding of any other form gets one generic sentence.
    /// </summary>
    internal static string ScenarioFailure(WorkbenchLocale locale, string? failure, BrainProcess process)
    {
        string T(string key, params (string, string)[] values) => AuthoringText.Text(locale, key, values);
        var words = FlowWords.For(process, locale);
        string Step(string id) => FlowChangeDescriber.Quote(words.Step(id));
        if (failure is null)
        {
            return T("scenario.fail.other");
        }

        if (Misaddressed().Match(failure) is { Success: true } wrong)
        {
            return T("scenario.fail.misaddressed", ("step", Step(wrong.Groups[1].Value)));
        }

        if (Refused().Match(failure) is { Success: true } refused)
        {
            return T("scenario.fail.refused", ("reason", Runtime(locale, refused.Groups[2].Value.Trim(), null)));
        }

        if (NoAnswer().Match(failure) is { Success: true } none)
        {
            return T("scenario.fail.noanswer", ("step", Step(none.Groups[1].Value)));
        }

        if (Status().Match(failure) is { Success: true } status && AuthoringText.Has("status.expect." + status.Groups[1].Value) && AuthoringText.Has("status.got." + status.Groups[2].Value))
        {
            return T("scenario.fail.status", ("expected", T("status.expect." + status.Groups[1].Value)), ("actual", T("status.got." + status.Groups[2].Value)));
        }

        if (Result().Match(failure) is { Success: true } result)
        {
            return T("scenario.fail.result", ("expected", FlowChangeDescriber.Quote(words.Result(result.Groups[1].Value))), ("actual", FlowChangeDescriber.Quote(words.Result(result.Groups[2].Value))));
        }

        if (failure.StartsWith("expected trace ", StringComparison.Ordinal))
        {
            return T("scenario.fail.trace");
        }

        return Variable().Match(failure) is { Success: true } variable
            ? T("scenario.fail.variable", ("name", FlowChangeDescriber.Quote(WorkbenchText.Humanize(variable.Groups[1].Value))))
            : T("scenario.fail.other");
    }

    [GeneratedRegex(@"^the event addresses \w+ of step '(.*)' but the instance is \w+ on '.*'$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Misaddressed();

    [GeneratedRegex(@"^\w+ was (Ignored|Rejected|Applied): (.+)$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Refused();

    [GeneratedRegex(@"^there is no scripted answer for step '(.*)'$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex NoAnswer();

    [GeneratedRegex(@"^expected status (\w+) but it was (\w+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Status();

    [GeneratedRegex(@"^expected result '(.*)' but it was '(.*)'$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Result();

    [GeneratedRegex(@"^expected variable '(.*?)' to be ", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Variable();
}

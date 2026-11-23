using System.Text.Json.Nodes;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>Says where in a document a JSON Pointer points, in words a person understands, using the labels the document carries.</summary>
internal static class PathWords
{
    internal static string Describe(string path, JsonObject? document, WorkbenchLocale locale)
    {
        var parts = Segments(path);
        if (parts.Count == 0)
        {
            return AuthoringText.Text(locale, "path.in.document");
        }

        string T(string key, params (string, string)[] values) => AuthoringText.Text(locale, key, values);
        var tag = AuthoringJson.Tag(locale);
        if (parts[0] == "body" && parts.Count >= 3 && parts[1] == "steps")
        {
            var step = T("path.in.step", ("step", FlowChangeDescriber.Quote(Label(document, tag, "steps", parts[2]))));
            return parts.Count > 3 ? step + " " + T("path.member", ("member", Humanize(parts[3]))) : step;
        }

        if (parts[0] == "body" && parts.Count >= 3 && parts[1] == "windows")
        {
            return T("path.in.window", ("window", FlowChangeDescriber.Quote(Label(document, tag, "windows", parts[2]))));
        }

        if (parts[0] == "body" && parts.Count >= 3 && parts[1] == "variables")
        {
            return T("path.in.variable", ("variable", "`" + parts[2] + "`"));
        }

        if (parts[0] == "body" && parts.Count >= 3 && parts[1] == "scenarios" && int.TryParse(parts[2], out var index))
        {
            var name = ((document?["body"] as JsonObject)?["scenarios"] as JsonArray) is { } scenarios && index >= 0 && index < scenarios.Count
                ? AuthoringJson.Text((scenarios[index] as JsonObject)?["name"])
                : null;
            return name is null
                ? T("path.in.testNumber", ("n", AuthoringText.Number(index + 1L)))
                : T("path.in.test", ("name", FlowChangeDescriber.Quote(name)));
        }

        return parts[0] switch
        {
            "body" when parts.Count >= 2 => T("path.in.part", ("part", Humanize(parts[1]))),
            "meta" => T("path.in.meta"),
            "uses" or "requires" => T("path.in.uses"),
            _ => T("path.in.header"),
        };
    }

    private static List<string> Segments(string path) =>
        [.. path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))];

    private static string Humanize(string name) => WorkbenchText.Humanize(name);

    private static string Label(JsonObject? document, string language, string group, string id)
    {
        var display = (document?["meta"] as JsonObject)?["display"] as JsonObject;
        foreach (var tag in new[] { language, "en" })
        {
            if (AuthoringJson.Text(((display?[tag] as JsonObject)?[group] as JsonObject)?[id]) is { } text)
            {
                return text;
            }
        }

        return Humanize(id);
    }
}

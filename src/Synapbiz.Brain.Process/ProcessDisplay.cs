using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process;

/// <summary>
/// The labels a process document carries for the people who read its cases, read from the convention <c>meta.display.&lt;language&gt;</c>
/// (see the reference page). <c>meta</c> stays a free object: nothing here is an error and nothing here changes how a process runs.
/// The reader never throws, whatever shape <c>meta</c> has: a member that does not have the shape of the convention is ignored.
/// </summary>
public sealed partial class ProcessDisplay
{
    /// <summary>The longest text of a label, in characters.</summary>
    public const int MaximumLabelLength = 120;

    private const string Fallback = "en";
    private static readonly string[] Groups = ["stages", "steps", "results", "queues", "outcomes", "events", "windows"];

    private readonly Dictionary<string, Language> languages;

    private ProcessDisplay(Dictionary<string, Language> languages) => this.languages = languages;

    /// <summary>Reads the labels of a loaded process. A process whose <c>meta</c> has no <c>display</c> gives a reader that returns null for everything.</summary>
    /// <param name="process">The loaded process.</param>
    public static ProcessDisplay From(BrainProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return new ProcessDisplay(Read(process.Meta, null));
    }

    /// <summary>
    /// The warnings about <c>meta.display</c>, ordered by path then code: <c>process.display.invalid</c> for a member of the wrong shape
    /// (at its path) and <c>process.display.unknown_id</c> for a label of a stage, step, result, queue, outcome, event or window the
    /// document does not have. They are never errors and the loader never reports them: a process with warnings loads and runs as any other.
    /// </summary>
    /// <param name="process">The loaded process.</param>
    public static IReadOnlyList<BrainSpecError> Validate(BrainProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var warnings = new List<BrainSpecError>();
        Read(process.Meta, new Known(process.Definition, warnings));
        return [.. warnings.Distinct().OrderBy(warning => warning.Path, StringComparer.Ordinal).ThenBy(warning => warning.Code, StringComparer.Ordinal)];
    }

    /// <summary>The title of the process in a language; null when neither that language nor <c>en</c> has one.</summary>
    /// <param name="language">A language code such as <c>es</c>; the case and a region suffix (<c>es-MX</c>) are ignored.</param>
    public string? Title(string? language) => Find(language, label => label.Text);

    /// <summary>The label of a stage; null when neither the language nor <c>en</c> labels it.</summary>
    /// <param name="language">A language code such as <c>es</c>.</param>
    /// <param name="id">The stage name.</param>
    public string? Stage(string? language, string id) => Find(language, label => Entry(label, "stages", id));

    /// <summary>The label of a step; null when neither the language nor <c>en</c> labels it.</summary>
    /// <param name="language">A language code such as <c>es</c>.</param>
    /// <param name="id">The step id.</param>
    public string? Step(string? language, string id) => Find(language, label => Entry(label, "steps", id));

    /// <summary>The label of a result of an <c>end</c> step; null when neither the language nor <c>en</c> labels it.</summary>
    /// <param name="language">A language code such as <c>es</c>.</param>
    /// <param name="id">The result name.</param>
    public string? Result(string? language, string id) => Find(language, label => Entry(label, "results", id));

    /// <summary>The label of the queue of a <c>humanTask</c> step; null when neither the language nor <c>en</c> labels it.</summary>
    /// <param name="language">A language code such as <c>es</c>.</param>
    /// <param name="id">The queue name.</param>
    public string? Queue(string? language, string id) => Find(language, label => Entry(label, "queues", id));

    /// <summary>The label of an outcome of a <c>humanTask</c> step; null when neither the language nor <c>en</c> labels it.</summary>
    /// <param name="language">A language code such as <c>es</c>.</param>
    /// <param name="id">The outcome name.</param>
    public string? Outcome(string? language, string id) => Find(language, label => Entry(label, "outcomes", id));

    /// <summary>The label of an event a <c>wait</c> step, an <c>interruptBy</c> or an event trigger names; null when neither the language nor <c>en</c> labels it.</summary>
    /// <param name="language">A language code such as <c>es</c>.</param>
    /// <param name="id">The event name.</param>
    public string? Event(string? language, string id) => Find(language, label => Entry(label, "events", id));

    /// <summary>The label of a delivery window; null when neither the language nor <c>en</c> labels it.</summary>
    /// <param name="language">A language code such as <c>es</c>.</param>
    /// <param name="id">The window name.</param>
    public string? Window(string? language, string id) => Find(language, label => Entry(label, "windows", id));

    private static string? Entry(Language label, string group, string id) =>
        id is not null && label.Labels.TryGetValue(group, out var entries) && entries.TryGetValue(id, out var text) ? text : null;

    // The asked language first, then English; the first one that has the label wins.
    private string? Find(string? language, Func<Language, string?> pick)
    {
        var code = Normalize(language);
        if (code is not null && languages.TryGetValue(code, out var asked) && pick(asked) is { } text)
        {
            return text;
        }

        return languages.TryGetValue(Fallback, out var english) ? pick(english) : null;
    }

    // "ES" and "es-MX" both ask for "es"; the keys of the dictionary compare without regard to case.
    private static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var code = language.Trim();
        var cut = code.IndexOfAny(['-', '_']);
        return cut < 0 ? code : code[..cut];
    }

    private static Dictionary<string, Language> Read(JsonObject meta, Known? known)
    {
        var result = new Dictionary<string, Language>(StringComparer.OrdinalIgnoreCase);
        if (!meta.TryGetPropertyValue("display", out var display) || display is null)
        {
            return result;
        }

        const string root = "/meta/display";
        if (display is not JsonObject byLanguage)
        {
            known?.Warnings.Add(Diagnostics.BadDisplay(root));
            return result;
        }

        foreach (var (code, node) in byLanguage)
        {
            var path = JsonText.Pointer(root, code);
            if (!LanguagePattern().IsMatch(code) || node is not JsonObject label)
            {
                known?.Warnings.Add(Diagnostics.BadDisplay(path));
                continue;
            }

            result[code] = ReadLanguage(label, path, known);
        }

        return result;
    }

    private static Language ReadLanguage(JsonObject label, string path, Known? known)
    {
        string? title = null;
        var groups = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (name, node) in label)
        {
            var member = JsonText.Pointer(path, name);
            if (name == "title")
            {
                title = LabelText(node);
                if (title is null)
                {
                    known?.Warnings.Add(Diagnostics.BadDisplay(member));
                }
            }
            else if (!Groups.Contains(name, StringComparer.Ordinal) || node is not JsonObject entries)
            {
                known?.Warnings.Add(Diagnostics.BadDisplay(member));
            }
            else
            {
                groups[name] = ReadGroup(name, entries, member, known);
            }
        }

        return new Language(title, groups);
    }

    private static Dictionary<string, string> ReadGroup(string group, JsonObject entries, string path, Known? known)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, node) in entries)
        {
            var member = JsonText.Pointer(path, id);
            if (LabelText(node) is not { } text)
            {
                known?.Warnings.Add(Diagnostics.BadDisplay(member));
                continue;
            }

            result[id] = text;
            if (known is not null && !known.Has(group, id))
            {
                known.Warnings.Add(Diagnostics.UnknownDisplayId(member));
            }
        }

        return result;
    }

    private static string? LabelText(JsonNode? node) =>
        JsonText.Text(node) is { Length: > 0 and <= MaximumLabelLength } text ? text : null;

    [GeneratedRegex(@"\A[a-z]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();

    private sealed record Language(string? Text, Dictionary<string, Dictionary<string, string>> Labels);

    // The ids the document has, by group, for the unknown_id warning.
    private sealed class Known(ProcessDefinition definition, List<BrainSpecError> warnings)
    {
        private readonly Dictionary<string, HashSet<string>> ids = Collect(definition);

        internal List<BrainSpecError> Warnings { get; } = warnings;

        internal bool Has(string group, string id) => ids.TryGetValue(group, out var set) && set.Contains(id);

        private static Dictionary<string, HashSet<string>> Collect(ProcessDefinition definition) => new(StringComparer.Ordinal)
        {
            ["stages"] = [.. definition.Stages],
            ["steps"] = [.. definition.Steps.Select(step => step.Id)],
            ["results"] = [.. definition.Steps.OfType<EndStep>().Select(step => step.Result)],
            ["queues"] = [.. definition.Steps.OfType<HumanTaskStep>().Select(step => step.Queue)],
            ["outcomes"] = [.. definition.Steps.OfType<HumanTaskStep>().SelectMany(step => step.Outcomes)],
            ["events"] =
            [
                .. definition.Steps.OfType<WaitStep>().Select(step => step.Event),
                .. definition.Steps.OfType<ActionStep>().Where(step => step.Interrupt is not null).Select(step => step.Interrupt!.Event),
                .. definition.Triggers.OfType<EventTriggerDeclaration>().Select(trigger => trigger.Event),
            ],
            ["windows"] = [.. definition.Windows.Keys],
        };
    }
}

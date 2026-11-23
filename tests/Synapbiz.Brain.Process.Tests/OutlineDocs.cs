using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>The documents the outline tests run on, and the text a golden file holds.</summary>
internal static class OutlineDocs
{
    internal const string GoldenFolder = "tests/fixtures/brain-process/outline/";

    /// <summary>The six reference documents and the three process fixtures of the format, by the name of their golden file.</summary>
    internal static IReadOnlyList<(string Name, BrainProcess Process)> All() =>
    [
        .. ReferenceDocs.Ids.Select(id => (id, ReferenceDocs.Process(id))),
        ("fixture.outreach", TestFiles.OutreachProcess()),
        ("fixture.followup", TestFiles.FollowupProcess()),
        ("fixture.reminder", Reminder()),
    ];

    internal static BrainProcess Reminder()
    {
        var loaded = BrainProcessLoader.Load(TestFiles.Read(TestFiles.Fixtures + "reminder.process.json"), new ProcessLoadContext([]));
        return loaded.Process ?? throw new InvalidOperationException("The reminder fixture does not load.");
    }

    /// <summary>Every member of the outline in a fixed, line-oriented text: the content hash is left out, so that a label edit does not rewrite the file.</summary>
    internal static string Render(ProcessOutline outline)
    {
        var text = new StringBuilder();
        text.Append("process ").Append(outline.Id).Append(" v").Append(outline.Version.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var property in typeof(ProcessOutline).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name is nameof(ProcessOutline.Id) or nameof(ProcessOutline.Version) or nameof(ProcessOutline.ContentHash) or nameof(ProcessOutline.Steps))
            {
                continue;
            }

            Member(text, 0, property.Name, property.GetValue(outline));
        }

        foreach (var step in outline.Steps)
        {
            text.Append("step ").Append(step.Id).Append('\n');
            foreach (var property in typeof(OutlineStep).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.Name != nameof(OutlineStep.Id)))
            {
                Member(text, 1, property.Name, property.GetValue(step));
            }
        }

        return text.ToString();
    }

    private static void Member(StringBuilder text, int depth, string name, object? value)
    {
        if (value is null || (value is IEnumerable and not string && !value.GetType().IsValueType && !((IEnumerable)value).Cast<object>().Any()) || value is false)
        {
            return;
        }

        text.Append(' ', depth * 2).Append(name).Append(": ").Append(Format(value)).Append('\n');
    }

    private static string Format(object value) => value switch
    {
        string text => text,
        bool flag => flag ? "true" : "false",
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm", CultureInfo.InvariantCulture),
        IFormattable number when value.GetType().IsPrimitive => number.ToString(null, CultureInfo.InvariantCulture),
        Enum member => member.ToString(),
        IEnumerable list => "[" + string.Join(", ", list.Cast<object>().Select(Format)) + "]",
        _ => value.GetType().IsValueType ? value.ToString()! : Record(value),
    };

    private static string Record(object value) =>
        "{" + string.Join(
            ", ",
            value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetIndexParameters().Length == 0 && property.Name != "EqualityContract")
                .Select(property => (property.Name, Value: property.GetValue(value)))
                .Where(pair => pair.Value is not null)
                .Select(pair => pair.Name + "=" + Format(pair.Value!))) + "}";

    /// <summary>The golden file of a document; written first when the variable <c>BRAIN_UPDATE_GOLDEN</c> is <c>1</c>.</summary>
    internal static string Golden(string name, string actual)
    {
        var directory = Path.Combine(Path.GetDirectoryName(TestFiles.Locate(TestFiles.Fixtures + "conformance.json"))!, "outline");
        var path = Path.Combine(directory, name + ".outline.txt");
        if (Environment.GetEnvironmentVariable("BRAIN_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual);
        }

        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}

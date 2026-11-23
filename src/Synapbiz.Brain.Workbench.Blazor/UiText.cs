using Microsoft.AspNetCore.Components;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Workbench.Blazor;

/// <summary>The strings of one render: the defaults of the locale, replaced by the host's overrides where the rules allow.</summary>
internal sealed class UiText
{
    private const string BoldMark = "**";

    private readonly WorkbenchLocale locale;
    private readonly IReadOnlyDictionary<string, string>? overrides;

    internal UiText(WorkbenchLocale locale, IReadOnlyDictionary<string, string>? overrides)
    {
        this.locale = locale;
        this.overrides = overrides;
    }

    internal WorkbenchLocale Locale => locale;

    /// <summary>The text of a key with every <c>{name}</c> placeholder filled; bold marks are removed.</summary>
    internal string Plain(string key, params (string Name, string Value)[] values) =>
        string.Concat(Segments(key).Select(segment => Fill(segment.Text, values)));

    /// <summary>The text of a key as markup: a bold pair becomes <c>strong</c>, the values are filled into the text and encoded.</summary>
    internal RenderFragment Rich(string key, params (string Name, string Value)[] values) => builder =>
    {
        foreach (var segment in Segments(key))
        {
            var text = Fill(segment.Text, values);
            if (segment.Bold)
            {
                builder.OpenElement(0, "strong");
                builder.AddContent(1, text);
                builder.CloseElement();
            }
            else
            {
                builder.AddContent(2, text);
            }
        }
    };

    /// <summary>The key with <c>.one</c> when the count is 1 and <c>.other</c> otherwise.</summary>
    internal static string Plural(string key, long count) => key + (count == 1 ? ".one" : ".other");

    /// <summary>The template of a key, after the override rules.</summary>
    internal string Template(string key)
    {
        var text = WorkbenchStrings.Default(key, locale);
        if (overrides is not null && overrides.TryGetValue(key, out var replacement) && replacement is not null
            && (!WorkbenchStrings.IsMandatory(key) || !string.IsNullOrWhiteSpace(replacement)))
        {
            text = replacement;
        }

        return text;
    }

    private static string Fill(string template, (string Name, string Value)[] values)
    {
        var text = template;
        foreach (var (name, value) in values)
        {
            text = text.Replace("{" + name + "}", value, StringComparison.Ordinal);
        }

        return text;
    }

    private List<(string Text, bool Bold)> Segments(string key)
    {
        var parts = Template(key).Split(BoldMark);
        var segments = new List<(string Text, bool Bold)>();
        var pairs = parts.Length - 1 - ((parts.Length - 1) % 2);
        for (var index = 0; index < parts.Length; index++)
        {
            var text = parts[index];
            if (index > pairs)
            {
                // An unmatched mark stays as typed.
                segments[^1] = (segments[^1].Text + BoldMark + text, segments[^1].Bold);
                continue;
            }

            segments.Add((text, index % 2 == 1));
        }

        return segments;
    }
}

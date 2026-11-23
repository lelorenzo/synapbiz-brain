using System.Text.RegularExpressions;
using FluentAssertions;

namespace Synapbiz.Brain.Workbench.Blazor.Tests;

/// <summary>The stylesheet of the package holds no palette, no font and no fallback: it reads only the host's <c>--wb-*</c> tokens.</summary>
public sealed partial class StylesheetTests
{
    private static readonly string[] Tokens =
    [
        "--wb-font", "--wb-text", "--wb-text-small", "--wb-text-label", "--wb-text-title",
        "--wb-surface", "--wb-surface-head", "--wb-selected", "--wb-selected-edge", "--wb-hover",
        "--wb-line", "--wb-line-soft", "--wb-ink", "--wb-ink-2", "--wb-muted",
        "--wb-accent", "--wb-accent-ink", "--wb-accent-hover", "--wb-control-edge", "--wb-focus",
        "--wb-ok-bg", "--wb-ok-ink", "--wb-ok-edge", "--wb-warn-bg", "--wb-warn-ink", "--wb-warn-edge",
        "--wb-bad-bg", "--wb-bad-ink", "--wb-bad-edge", "--wb-neutral-bg", "--wb-neutral-ink", "--wb-neutral-edge",
        "--wb-notice-bg", "--wb-notice-ink", "--wb-notice-edge", "--wb-radius", "--wb-radius-sm", "--wb-scrim",
    ];

    private static readonly string[] ColourNames =
    [
        "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black", "blanchedalmond", "blue", "blueviolet", "brown",
        "burlywood", "cadetblue", "chartreuse", "chocolate", "coral", "cornflowerblue", "cornsilk", "crimson", "cyan", "darkblue", "darkcyan",
        "darkgoldenrod", "darkgray", "darkgreen", "darkgrey", "darkkhaki", "darkmagenta", "darkolivegreen", "darkorange", "darkorchid", "darkred",
        "darksalmon", "darkseagreen", "darkslateblue", "darkslategray", "darkslategrey", "darkturquoise", "darkviolet", "deeppink", "deepskyblue",
        "dimgray", "dimgrey", "dodgerblue", "firebrick", "floralwhite", "forestgreen", "fuchsia", "gainsboro", "ghostwhite", "gold", "goldenrod",
        "gray", "green", "greenyellow", "grey", "honeydew", "hotpink", "indianred", "indigo", "ivory", "khaki", "lavender", "lavenderblush",
        "lawngreen", "lemonchiffon", "lightblue", "lightcoral", "lightcyan", "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey",
        "lightpink", "lightsalmon", "lightseagreen", "lightskyblue", "lightslategray", "lightslategrey", "lightsteelblue", "lightyellow", "lime",
        "limegreen", "linen", "magenta", "maroon", "mediumaquamarine", "mediumblue", "mediumorchid", "mediumpurple", "mediumseagreen",
        "mediumslateblue", "mediumspringgreen", "mediumturquoise", "mediumvioletred", "midnightblue", "mintcream", "mistyrose", "moccasin",
        "navajowhite", "navy", "oldlace", "olive", "olivedrab", "orange", "orangered", "orchid", "palegoldenrod", "palegreen", "paleturquoise",
        "palevioletred", "papayawhip", "peachpuff", "peru", "pink", "plum", "powderblue", "purple", "rebeccapurple", "red", "rosybrown",
        "royalblue", "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell", "sienna", "silver", "skyblue", "slateblue", "slategray",
        "slategrey", "snow", "springgreen", "steelblue", "tan", "teal", "thistle", "tomato", "turquoise", "violet", "wheat", "white", "whitesmoke",
        "yellow", "yellowgreen",
    ];

    private static readonly Lazy<string> Sheet = new(() =>
        CommentPattern().Replace(File.ReadAllText(Fx.Locate("src/Synapbiz.Brain.Workbench.Blazor/wwwroot/workbench.css")), string.Empty));

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"var\(\s*(--[A-Za-z0-9_-]+)\s*([,)])")]
    private static partial Regex VarPattern();

    [GeneratedRegex(@"font-family\s*:\s*([^;}]+)")]
    private static partial Regex FontFamilyPattern();

    [GeneratedRegex(@"(^|[^A-Za-z0-9_-])font\s*:")]
    private static partial Regex FontShorthandPattern();

    [GeneratedRegex(@"--[A-Za-z0-9_-]+\s*:")]
    private static partial Regex CustomPropertyDeclarationPattern();

    [GeneratedRegex(@"#[0-9A-Fa-f]{3,8}\b|\b(rgb|rgba|hsl|hsla|hwb|lab|lch|oklab|oklch|color|color-mix|light-dark)\(", RegexOptions.IgnoreCase)]
    private static partial Regex ColourFunctionPattern();

    [GeneratedRegex(@"[A-Za-z-]+")]
    private static partial Regex WordPattern();

    /// <summary>The rules of the sheet as selector list and declaration block; at-rule wrappers are flattened.</summary>
    private static List<(string Selector, string Body)> Rules()
    {
        var rules = new List<(string, string)>();
        foreach (Match match in Regex.Matches(Sheet.Value, @"([^{}]+)\{([^{}]*)\}"))
        {
            rules.Add((match.Groups[1].Value.Trim(), match.Groups[2].Value));
        }

        return rules;
    }

    private static string Declarations() => string.Join("\n", Rules().Select(rule => rule.Body));

    [Fact]
    public void TheSheetIsFoundAndHasRules()
    {
        Rules().Should().HaveCountGreaterThan(50);
    }

    [Fact]
    public void ItHoldsNoColourLiteral()
    {
        ColourFunctionPattern().Matches(Sheet.Value).Select(item => item.Value).Should().BeEmpty();
        var values = Declarations().Split(';').Select(item => item.Trim()).Where(item => item.Contains(':', StringComparison.Ordinal))
            .Select(item => item[(item.IndexOf(':', StringComparison.Ordinal) + 1)..]);
        var words = values.SelectMany(value => WordPattern().Matches(value.Replace("var(", " ", StringComparison.Ordinal)).Select(item => item.Value.ToUpperInvariant()));
        words.Where(word => ColourNames.Contains(word, StringComparer.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    [Fact]
    public void ItNamesNoFont()
    {
        var families = FontFamilyPattern().Matches(Sheet.Value).Select(item => item.Groups[1].Value.Trim()).ToList();
        families.Should().NotBeEmpty().And.OnlyContain(value => value == "var(--wb-font)" || value == "inherit");
        FontShorthandPattern().IsMatch(Sheet.Value).Should().BeFalse();
        Sheet.Value.Should().NotContain("@font-face").And.NotContain("@import").And.NotContain("url(");
    }

    [Fact]
    public void NoVarHasAFallback()
    {
        VarPattern().Matches(Sheet.Value).Where(item => item.Groups[2].Value == ",").Select(item => item.Value).Should().BeEmpty();
    }

    [Fact]
    public void EveryTokenItReadsIsInTheContract()
    {
        var read = VarPattern().Matches(Sheet.Value).Select(item => item.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
        read.Should().NotBeEmpty();
        read.Where(token => !Tokens.Contains(token, StringComparer.Ordinal)).Should().BeEmpty();
        read.Where(token => !token.StartsWith("--wb-", StringComparison.Ordinal)).Should().BeEmpty();
    }

    [Fact]
    public void ItDeclaresNoCustomPropertyOfItsOwn()
    {
        CustomPropertyDeclarationPattern().Matches(Declarations()).Select(item => item.Value).Should().BeEmpty();
    }

    [Fact]
    public void EveryClassIsPrefixedAndTheRootHasItsOwn()
    {
        var classes = Regex.Matches(Sheet.Value, @"\.([A-Za-z_][A-Za-z0-9_-]*)").Select(item => item.Groups[1].Value)
            .Where(item => !item.All(char.IsDigit)).Distinct(StringComparer.Ordinal);
        classes.Where(item => !item.StartsWith("wb-", StringComparison.Ordinal)).Should().BeEmpty();
        Sheet.Value.Should().Contain(".wb-root");
    }

    [Fact]
    public void NoElementLevelHoverSetsABackgroundOrAColour()
    {
        var offenders = Rules()
            .SelectMany(rule => rule.Selector.Split(',').Select(selector => (Selector: selector.Trim(), rule.Body)))
            .Where(item => item.Selector.EndsWith("button:hover", StringComparison.Ordinal) || item.Selector.EndsWith(".wb-btn:hover", StringComparison.Ordinal))
            .Where(item => Regex.IsMatch(item.Body, @"(^|[;\s])(background|color)\s*:"))
            .Select(item => item.Selector);
        offenders.Should().BeEmpty();
    }

    [Theory]
    [InlineData(".wb-btn-primary", "var(--wb-accent-hover)", "var(--wb-accent-ink)")]
    [InlineData(".wb-btn-secondary", "var(--wb-hover)", "var(--wb-ink)")]
    public void EachButtonVariantStatesItsOwnRestHoverPressedAndDisabledColours(string variant, string hover, string ink)
    {
        string BodyOf(string selector) => string.Join("\n", Rules().Where(rule => rule.Selector.Split(',').Select(item => item.Trim()).Contains(selector, StringComparer.Ordinal)).Select(rule => rule.Body));

        BodyOf(variant).Should().Contain("background:").And.Contain("color:");
        BodyOf(variant + ":hover").Should().Contain("background: " + hover).And.Contain("color: " + ink);
        BodyOf(variant + ":active").Should().Contain("background: " + hover);
        BodyOf(variant + ":disabled").Should().Contain("background:");
    }

    [Fact]
    public void ThePrimaryButtonRestsOnTheAccentWithItsInk()
    {
        var rest = Rules().First(rule => rule.Selector == ".wb-btn-primary").Body;
        rest.Should().Contain("background: var(--wb-accent)").And.Contain("color: var(--wb-accent-ink)");
    }

    [Fact]
    public void TheFocusRingIsTwoPixelsWithATwoPixelOffsetOnEveryInteractiveElement()
    {
        var rule = Rules().First(item => item.Selector == ".wb-root :focus-visible");
        rule.Body.Should().Contain("outline: 2px solid var(--wb-focus)").And.Contain("outline-offset: 2px");
    }

    [Fact]
    public void BelowElevenHundredPixelsTheListCollapsesToASelect()
    {
        var at = Sheet.Value.IndexOf("@media (max-width: 1100px)", StringComparison.Ordinal);
        at.Should().BeGreaterThan(0);
        var block = Sheet.Value[at..Sheet.Value.IndexOf("@media (max-width: 760px)", StringComparison.Ordinal)];
        block.Should().Contain(".wb-list").And.Contain("display: none").And.Contain(".wb-list-select-wrap").And.Contain("display: block");
    }
}

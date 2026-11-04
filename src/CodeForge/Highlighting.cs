using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Xml;
using CodeForge.Core;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace CodeForge;

/// <summary>
/// Syntax highlighting for each language and theme. Languages AvalonEdit knows use its definitions, recolored for dark
/// themes; others get a definition made from their keywords, comments and strings.
/// </summary>
public static partial class Highlighting
{
    private static readonly Dictionary<(string, Themes.Palette), IHighlightingDefinition?> Cache = [];
    private static Dictionary<string, string>? builtInXml;

    public static IHighlightingDefinition? For(Language language, Themes.Palette palette)
    {
        var key = (language.Id, palette);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        IHighlightingDefinition? definition = null;
        try
        {
            if (language.Highlighting != null)
            {
                definition = BuiltInXml().TryGetValue(language.Highlighting, out var xml)
                    ? Load(Recolor(xml, palette))
                    : HighlightingManager.Instance.GetDefinition(language.Highlighting);
            }
            else if (language.KeywordList.Any() || language.LineComment != null)
            {
                definition = Load(Generate(language, palette));
            }
        }
        catch (Exception)
        {
            definition = language.Highlighting != null ? HighlightingManager.Instance.GetDefinition(language.Highlighting) : null;
        }
        Cache[key] = definition;
        return definition;
    }

    private static IHighlightingDefinition Load(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    /// <summary>AvalonEdit's built-in .xshd files, by the name each declares.</summary>
    private static Dictionary<string, string> BuiltInXml()
    {
        if (builtInXml != null) return builtInXml;
        builtInXml = [];
        var assembly = typeof(HighlightingManager).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".xshd", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream == null) continue;
            var text = new StreamReader(stream).ReadToEnd();
            var name = SyntaxName().Match(text);
            if (name.Success) builtInXml[name.Groups[1].Value] = text;
        }
        return builtInXml;
    }

    [GeneratedRegex(@"<SyntaxDefinition[^>]*\sname=""([^""]+)""")]
    private static partial Regex SyntaxName();

    [GeneratedRegex(@"foreground=""([^""]+)""")]
    private static partial Regex ForegroundAttribute();

    /// <summary>Gives a built-in definition the theme's syntax colors, by what each original color means.</summary>
    private static string Recolor(string xml, Themes.Palette palette) => ForegroundAttribute().Replace(xml, match =>
    {
        var original = match.Groups[1].Value;
        var named = original.ToLowerInvariant() switch
        {
            "green" or "darkgreen" or "seagreen" => palette.Comment,
            "blue" or "darkblue" or "navy" or "mediumblue" => palette.Keyword,
            "red" or "darkred" or "brown" or "maroon" or "firebrick" or "sienna" => palette.String,
            "black" => palette.EditorText,
            "gray" or "grey" or "dimgray" or "darkgray" => palette.Comment,
            "teal" or "darkcyan" => palette.Type,
            "purple" or "darkmagenta" or "darkviolet" or "blueviolet" => palette.Keyword,
            "olive" or "darkgoldenrod" or "darkorange" => palette.Function,
            "magenta" => palette.Number,
            _ => null,
        };
        if (named != null) return $"foreground=\"{named}\"";
        if (!palette.IsDark) return match.Value;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(original);
            return $"foreground=\"{Lighten(color)}\"";
        }
        catch (FormatException) { return match.Value; }
    });

    private static string Lighten(Color color)
    {
        // Mirror the lightness around the middle, keeping the hue.
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        if (lightness >= 0.55) return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var scale = (0.55 + (0.55 - lightness) * 0.6) / Math.Max(lightness, 0.05);
        byte Channel(double value) => (byte)Math.Clamp(value * 255 * scale, 0, 255);
        return $"#{Channel(r):X2}{Channel(g):X2}{Channel(b):X2}";
    }

    /// <summary>An .xshd definition from a language's keywords, types, comments and string delimiters.</summary>
    private static string Generate(Language language, Themes.Palette colors)
    {
        string Escape(string value) => SecurityElement.Escape(value) ?? value;
        var xml = new StringBuilder();
        xml.Append($"""<SyntaxDefinition name="{Escape(language.Name)}" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">""");
        xml.Append($"""<Color name="Comment" foreground="{colors.Comment}"/><Color name="String" foreground="{colors.String}"/>""");
        xml.Append($"""<Color name="Keyword" foreground="{colors.Keyword}"/><Color name="Type" foreground="{colors.Type}"/><Color name="Number" foreground="{colors.Number}"/>""");
        xml.Append($"""<RuleSet ignoreCase="{(language.CaseInsensitive ? "true" : "false")}">""");
        if (language.LineComment != null)
            xml.Append($"""<Span color="Comment"><Begin>{Escape(Regex.Escape(language.LineComment))}</Begin></Span>""");
        if (language.BlockStart != null && language.BlockEnd != null)
            xml.Append($"""<Span color="Comment" multiline="true"><Begin>{Escape(Regex.Escape(language.BlockStart))}</Begin><End>{Escape(Regex.Escape(language.BlockEnd))}</End></Span>""");
        foreach (var quote in language.Strings)
        {
            var q = Escape(Regex.Escape(quote.ToString()));
            xml.Append($"""<Span color="String" multiline="{(quote == '`' ? "true" : "false")}"><Begin>{q}</Begin><End>{q}</End><RuleSet><Span begin="\\" end="."/></RuleSet></Span>""");
        }
        if (language.KeywordList.Any())
            xml.Append("""<Keywords color="Keyword">""").Append(string.Concat(language.KeywordList.Select(k => $"<Word>{Escape(k)}</Word>"))).Append("</Keywords>");
        if (language.TypeList.Any())
            xml.Append("""<Keywords color="Type">""").Append(string.Concat(language.TypeList.Select(k => $"<Word>{Escape(k)}</Word>"))).Append("</Keywords>");
        xml.Append("""<Rule color="Number">\b0[xX][0-9a-fA-F]+|\b\d+(\.\d+)?([eE][+-]?\d+)?\b</Rule>""");
        xml.Append("</RuleSet></SyntaxDefinition>");
        return xml.ToString();
    }
}

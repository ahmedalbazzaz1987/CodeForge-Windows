using System.Text;
using System.Text.RegularExpressions;

namespace CodeForge.Core;

/// <summary>
/// The HTML start tag around a position, for the Properties window and the HTML designer (as on the Mac): its name,
/// its attributes in order (a null value is an attribute without one, like <c>required</c>) and where it is.
/// </summary>
public sealed partial class HtmlTag
{
    public int Start { get; }
    public int Length { get; }
    public string Name { get; }
    public List<(string Name, string? Value)> Attributes { get; }
    public bool SelfClosing { get; }

    private HtmlTag(int start, int length, string name, List<(string, string?)> attributes, bool selfClosing)
    {
        Start = start;
        Length = length;
        Name = name;
        Attributes = attributes;
        SelfClosing = selfClosing;
    }

    [GeneratedRegex(@"([^\s=/>""']+)(?:\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+))?")]
    private static partial Regex Attribute();

    /// <summary>The start tag the offset is in, or else the nearest one before it.</summary>
    public static HtmlTag? At(int offset, string text)
    {
        for (var index = Math.Min(offset, text.Length - 1); index >= 0; index--)
        {
            if (text[index] == '<' && index + 1 < text.Length && char.IsLetter(text[index + 1]) && Parse(text, index) is { } tag)
                return tag;
        }
        return null;
    }

    private static HtmlTag? Parse(string text, int start)
    {
        var index = start + 1;
        var quote = '\0';
        for (; index < text.Length; index++)
        {
            var c = text[index];
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '"' or '\'') quote = c;
            else if (c == '>') break;
        }
        if (index >= text.Length) return null;
        var inner = text[(start + 1)..index];
        var selfClosing = inner.EndsWith('/');
        if (selfClosing) inner = inner[..^1];
        var name = new string(inner.TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
        var attributes = new List<(string, string?)>();
        foreach (Match match in Attribute().Matches(inner[name.Length..]))
        {
            string? value = null;
            if (match.Groups[2].Success)
            {
                value = match.Groups[2].Value;
                if (value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0]) value = value[1..^1];
            }
            attributes.Add((match.Groups[1].Value, value));
        }
        return new HtmlTag(start, index - start + 1, name, attributes, selfClosing);
    }

    /// <summary>The start tag written back out, e.g. <c>&lt;input type="text" required&gt;</c>.</summary>
    public string Source
    {
        get
        {
            var text = new StringBuilder("<").Append(Name);
            foreach (var (key, value) in Attributes)
            {
                text.Append(' ').Append(key);
                if (value != null) text.Append("=\"").Append(value.Replace("\"", "&quot;")).Append('"');
            }
            return text.Append(SelfClosing ? " />" : ">").ToString();
        }
    }

    /// <summary>
    /// Where the n-th element with a tag name starts (just after its &lt;), as the design view reports a click: the tag
    /// and its index among the page's elements with that name. Null when there's no such element.
    /// </summary>
    public static int? ElementOffset(string text, string tag, int index)
    {
        if (index < 0) return null;
        var matches = Regex.Matches(text, "<" + Regex.Escape(tag) + @"(?=[\s/>])", RegexOptions.IgnoreCase);
        return index < matches.Count ? matches[index].Index + 1 : null;
    }

    /// <summary>
    /// The page for the design view: a &lt;base&gt; element so relative links (images, styles) load from
    /// <paramref name="baseUrl"/>, which the designer maps to the page's folder.
    /// </summary>
    public static string WithBase(string html, string baseUrl)
    {
        var element = $"<base href=\"{baseUrl}\">";
        var head = Regex.Match(html, @"<head(\s[^>]*)?>", RegexOptions.IgnoreCase);
        if (head.Success) return html.Insert(head.Index + head.Length, element);
        var htmlTag = Regex.Match(html, @"<html(\s[^>]*)?>", RegexOptions.IgnoreCase);
        return htmlTag.Success ? html.Insert(htmlTag.Index + htmlTag.Length, "<head>" + element + "</head>") : element + html;
    }
}

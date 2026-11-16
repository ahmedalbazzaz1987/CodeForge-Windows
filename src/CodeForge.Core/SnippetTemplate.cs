using System.Text;

namespace CodeForge.Core;

/// <summary>A part of a snippet: plain text, or a field to fill in (Tab moves between fields).</summary>
/// <param name="Index">The field's number: 1, 2… in Tab order; 0 is where the caret ends up. Text parts have -1.</param>
/// <param name="Text">The text, or the field's placeholder.</param>
public sealed record SnippetPart(int Index, string Text)
{
    public bool IsField => Index >= 0;
}

/// <summary>A field's place in the expanded snippet.</summary>
public sealed record SnippetField(int Index, int Start, int Length);

/// <summary>
/// Snippets with fields to fill in, in the syntax Visual Studio Code and language servers use: <c>$1</c>, <c>${1:name}</c>,
/// <c>${1|a,b|}</c> (the first choice) and <c>$0</c> for where the caret ends up. A number used twice is the same field:
/// typing in the first copy changes the others. Snippets with only <c>$0</c> (the older syntax) work as before, and a
/// <c>$</c> that isn't followed by a number (PHP variables, JavaScript <c>${name}</c>) stays as it is.
/// </summary>
public static class SnippetTemplate
{
    public static List<SnippetPart> Parse(string body)
    {
        var parts = new List<SnippetPart>();
        var text = new StringBuilder();
        void Flush()
        {
            if (text.Length > 0) parts.Add(new SnippetPart(-1, text.ToString()));
            text.Clear();
        }
        var i = 0;
        while (i < body.Length)
        {
            var c = body[i];
            if (c == '\\' && i + 1 < body.Length && body[i + 1] is '$' or '}')
            {
                text.Append(body[i + 1]);
                i += 2;
                continue;
            }
            if (c == '$' && i + 1 < body.Length && char.IsAsciiDigit(body[i + 1]))
            {
                var j = i + 1;
                while (j < body.Length && char.IsAsciiDigit(body[j])) j++;
                Flush();
                parts.Add(new SnippetPart(int.Parse(body[(i + 1)..j]), ""));
                i = j;
                continue;
            }
            if (c == '$' && i + 2 < body.Length && body[i + 1] == '{' && char.IsAsciiDigit(body[i + 2]))
            {
                var j = i + 2;
                while (j < body.Length && char.IsAsciiDigit(body[j])) j++;
                var index = int.Parse(body[(i + 2)..j]);
                if (j < body.Length && body[j] == '}')
                {
                    Flush();
                    parts.Add(new SnippetPart(index, ""));
                    i = j + 1;
                    continue;
                }
                if (j < body.Length && body[j] is ':' or '|' && Closing(body, j + 1) is { } end)
                {
                    var inner = body[(j + 1)..end];
                    string placeholder;
                    if (body[j] == '|')
                        placeholder = inner.TrimEnd('|').Split(',')[0];
                    else
                        // A placeholder with fields inside becomes plain text: ${1:foo(${2:x})} → foo(x).
                        placeholder = string.Concat(Parse(inner).Select(p => p.Text));
                    Flush();
                    parts.Add(new SnippetPart(index, placeholder));
                    i = end + 1;
                    continue;
                }
            }
            text.Append(c);
            i++;
        }
        Flush();
        // Copies of a field show the same placeholder: ${1:i} … $1 is i … i.
        var placeholders = parts.Where(p => p.IsField && p.Text.Length > 0).GroupBy(p => p.Index).ToDictionary(g => g.Key, g => g.First().Text);
        return parts.Select(p => p.IsField && p.Text.Length == 0 && placeholders.TryGetValue(p.Index, out var text) ? p with { Text = text } : p).ToList();
    }

    /// <summary>The <c>}</c> that closes a placeholder starting at <paramref name="from"/> (braces inside are counted).</summary>
    private static int? Closing(string body, int from)
    {
        var depth = 1;
        for (var i = from; i < body.Length; i++)
        {
            if (body[i] == '\\' && i + 1 < body.Length) { i++; continue; }
            if (body[i] == '{') depth++;
            else if (body[i] == '}' && --depth == 0) return i;
        }
        return null;
    }

    /// <summary>The snippet's text as inserted at a line indented with <paramref name="indent"/>, and where its fields are.</summary>
    public static (string Text, List<SnippetField> Fields) Expand(string body, string indent = "")
    {
        var builder = new StringBuilder();
        var fields = new List<SnippetField>();
        foreach (var part in Parse(body))
        {
            var text = part.Text.Replace("\n", "\n" + indent);
            if (part.IsField) fields.Add(new SnippetField(part.Index, builder.Length, text.Length));
            builder.Append(text);
        }
        return (builder.ToString(), fields);
    }

    /// <summary>Whether the snippet has fields to Tab through (more than a caret position).</summary>
    public static bool HasFields(string body) => Parse(body).Any(p => p.Index > 0);

    /// <summary>The plain text, as inserted when the snippet isn't expanded (fields keep their placeholders).</summary>
    public static string PlainText(string body) => Expand(body).Text;
}

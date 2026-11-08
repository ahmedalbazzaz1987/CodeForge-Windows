using System.Text.RegularExpressions;

namespace CodeForge.Core;

/// <summary>
/// Completion without a language server (VB.NET, and any language whose server isn't installed): the language's keywords,
/// its snippets and the other words in the file, like Visual Studio's word completion. Nothing inside strings or comments.
/// </summary>
public static partial class BasicIntelliSense
{
    /// <summary>AvalonEdit highlights these languages from its own definitions, so their keywords are listed here.</summary>
    private static readonly Dictionary<string, string> MoreKeywords = new()
    {
        ["csharp"] = "abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum " +
                     "event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new " +
                     "null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static " +
                     "string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while var async await " +
                     "record init required get set value yield nameof when where dynamic",
        ["vb"] = "AddHandler AddressOf Alias And AndAlso As Boolean ByRef Byte ByVal Call Case Catch CBool CByte CChar CDate CDbl CDec Char CInt Class " +
                 "CLng CObj Const Continue CShort CSng CStr CType Date Decimal Declare Default Delegate Dim DirectCast Do Double Each Else ElseIf End " +
                 "Enum Erase Error Event Exit False Finally For Friend Function Get GetType Global GoTo Handles If Implements Imports In Inherits Integer " +
                 "Interface Is IsNot Let Lib Like Long Loop Me Mod Module MustInherit MustOverride MyBase MyClass Namespace Narrowing New Next Not Nothing " +
                 "NotInheritable NotOverridable Object Of On Operator Option Optional Or OrElse Overloads Overridable Overrides ParamArray Partial Private " +
                 "Property Protected Public RaiseEvent ReadOnly ReDim RemoveHandler Resume Return Select Set Shadows Shared Short Single Static Step Stop " +
                 "String Structure Sub SyncLock Then Throw To True Try TryCast TypeOf UInteger ULong UShort Using When While Widening With WithEvents " +
                 "WriteOnly Xor Async Await Iterator Yield Console WriteLine ReadLine",
        ["python"] = "False None True and as assert async await break class continue def del elif else except finally for from global if import in " +
                     "is lambda nonlocal not or pass raise return try while with yield print len range str int float list dict set tuple self",
        ["java"] = "abstract assert boolean break byte case catch char class const continue default do double else enum extends final finally float " +
                   "for goto if implements import instanceof int interface long native new package private protected public return short static " +
                   "strictfp super switch synchronized this throw throws transient try void volatile while var record true false null String System",
        ["javascript"] = "break case catch class const continue debugger default delete do else export extends finally for function if import in " +
                         "instanceof let new return super switch this throw try typeof var void while with yield async await of null undefined true false console",
        ["cpp"] = "alignas alignof auto bool break case catch char class const constexpr const_cast continue decltype default delete do double " +
                  "dynamic_cast else enum explicit export extern false float for friend goto if inline int long mutable namespace new noexcept nullptr " +
                  "operator private protected public return short signed sizeof static static_assert static_cast struct switch template this throw true " +
                  "try typedef typeid typename union unsigned using virtual void volatile while include define std cout cin endl string vector",
        ["php"] = "abstract and array as break callable case catch class clone const continue declare default do echo else elseif empty enddeclare " +
                  "endfor endforeach endif endswitch endwhile extends final finally fn for foreach function global if implements include instanceof " +
                  "interface isset list match namespace new or print private protected public readonly require return static switch throw trait try " +
                  "unset use var while yield true false null",
        ["powershell"] = "begin break catch class continue data do dynamicparam else elseif end enum exit filter finally for foreach function if in " +
                         "param process return switch throw trap try until using while Write-Host Write-Output Get-ChildItem Set-Location",
        ["sql"] = "SELECT FROM WHERE INSERT INTO VALUES UPDATE SET DELETE CREATE TABLE ALTER DROP INDEX VIEW JOIN INNER LEFT RIGHT OUTER ON GROUP BY " +
                  "ORDER HAVING AS AND OR NOT NULL IS IN LIKE BETWEEN DISTINCT TOP LIMIT UNION ALL PRIMARY KEY FOREIGN REFERENCES DEFAULT COUNT SUM AVG MIN MAX",
    };

    /// <summary>Languages where completion would only get in the way.</summary>
    private static readonly HashSet<string> Plain = ["text", "markdown", "diff", "json", "yaml", "toml", "xml"];

    public static bool Offers(Language language) => !Plain.Contains(language.Id);

    public static IEnumerable<string> Keywords(Language language)
    {
        var all = language.KeywordList.Concat(language.TypeList);
        var key = language.Id == "c" ? "cpp" : language.Id == "typescript" ? "javascript" : language.Id;
        if (MoreKeywords.TryGetValue(key, out var more))
            all = all.Concat(more.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return all.Distinct();
    }

    [GeneratedRegex(@"\b[\p{L}_][\p{L}\p{N}_]{2,}\b")]
    private static partial Regex Word();

    /// <summary>The start of the identifier the caret is in or after.</summary>
    public static int WordStart(string text, int offset)
    {
        var start = Math.Min(offset, text.Length);
        while (start > 0 && IsIdentifier(text[start - 1])) start--;
        return start;
    }

    public static bool IsIdentifier(char c) => char.IsLetterOrDigit(c) || c == '_';

    public static (List<CompletionEntry> Items, int WordStart) Completions(string text, int offset, Language language)
    {
        offset = Math.Min(offset, text.Length);
        var start = WordStart(text, offset);
        if (!Offers(language) || InStringOrComment(text, start, language) || (start > 0 && text[start - 1] == '.')) return ([], start);
        var items = new List<CompletionEntry>();
        var seen = new HashSet<string>(language.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        void Add(string label, string detail, int kind, string? snippet = null, string sort = "")
        {
            if (seen.Add(label)) items.Add(new CompletionEntry(label, detail, kind, label, label, sort + label.ToLowerInvariant(), Snippet: snippet));
        }
        foreach (var snippet in Snippets.ForLanguage(language.Id)) Add(snippet.Shortcut, snippet.Title, 15, snippet.Body, "~");
        foreach (var keyword in Keywords(language).Order()) Add(keyword, "Keyword", 14);
        var limit = Math.Min(text.Length, 400_000);
        foreach (Match match in Word().Matches(text[..limit]))
            if (match.Index != start) Add(match.Value, "", 1, sort: "~~");
        return (items, start);
    }

    /// <summary>Whether an offset is inside a string or comment, judging by its line (and block comments before it).</summary>
    public static bool InStringOrComment(string text, int offset, Language language)
    {
        var lineStart = offset > 0 ? text.LastIndexOf('\n', offset - 1) + 1 : 0;
        if (language.BlockStart != null && language.BlockEnd != null)
        {
            var head = text[..offset];
            var open = head.LastIndexOf(language.BlockStart, StringComparison.Ordinal);
            if (open >= 0 && head.IndexOf(language.BlockEnd, open + language.BlockStart.Length, StringComparison.Ordinal) < 0 && !InString(text, lineStart, open, language))
                return true;
        }
        return InString(text, lineStart, offset, language, checkComment: true);
    }

    private static bool InString(string text, int from, int to, Language language, bool checkComment = false)
    {
        char? quote = null;
        for (var i = from; i < to; i++)
        {
            var c = text[i];
            if (quote != null)
            {
                if (c == '\\' && language.Id != "vb") i++;
                else if (c == quote) quote = null;
            }
            // The comment first: VB's ' is a comment, not a string.
            else if (checkComment && language.LineComment is { } comment && comment.Trim().Length > 0 &&
                     string.CompareOrdinal(text, i, comment.Trim(), 0, comment.Trim().Length) == 0)
            {
                return true;
            }
            else if (language.Strings.Contains(c))
            {
                quote = c;
            }
        }
        return quote != null;
    }

    private static readonly string[] NotTypes = ["return", "new", "await", "throw", "else", "yield", "in", "is", "as", "case", "not", "and", "or", "print", "echo", "Call"];

    /// <summary>
    /// Where a name is declared, for Go To Definition without a language server: after a declaring keyword
    /// (<c>class</c>, <c>def</c>, <c>Function</c>, <c>Dim</c>, <c>let</c>…), or as <c>Type name(</c> / <c>Type name =</c> in C-like
    /// languages. The offset of the name, or null.
    /// </summary>
    public static int? Declaration(string text, string name, Language language)
    {
        var word = Regex.Escape(name);
        var options = language.CaseInsensitive ? RegexOptions.IgnoreCase | RegexOptions.Multiline : RegexOptions.Multiline;
        var keywords = @"\b(?:class|struct|interface|enum|record|trait|impl|object|module|namespace|type|typedef|def|function|func|fn|fun|sub|property|" +
                       @"event|delegate|var|let|const|val|dim|static|local|my|our|macro|union|protocol|extension|actor)\s+(?:(?:async|mut|function|sub|readonly)\s+)?(" + word + @")\b";
        foreach (Match match in Regex.Matches(text, keywords, options))
        {
            var at = match.Groups[1].Index;
            if (!InStringOrComment(text, at, language)) return at;
        }
        var typed = @"[\w>\]?]\s+(" + word + @")\s*(?:\(|=(?!=)|;|\{|,|\))";
        foreach (Match match in Regex.Matches(text, typed, options))
        {
            var at = match.Groups[1].Index;
            var before = text[..match.Index];
            var lineStart = before.LastIndexOf('\n') + 1;
            var previous = Regex.Match(text[lineStart..(match.Index + 1)], @"(\w+)\W*$").Groups[1].Value;
            if (NotTypes.Contains(previous, StringComparer.OrdinalIgnoreCase) || InStringOrComment(text, at, language)) continue;
            return at;
        }
        return null;
    }
}

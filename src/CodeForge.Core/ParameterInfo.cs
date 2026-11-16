using System.Text.Json.Nodes;

namespace CodeForge.Core;

/// <summary>One way to call a function, for Parameter Info: its label and where each parameter is in it.</summary>
/// <param name="Parameters">(start, length) of each parameter in <see cref="Label"/>.</param>
/// <param name="ParameterDocs">What each parameter is for, when known (same order as <see cref="Parameters"/>).</param>
public sealed record SignatureInfo(string Label, IReadOnlyList<(int Start, int Length)> Parameters, string Documentation = "",
    IReadOnlyList<string>? ParameterDocs = null)
{
    public static SignatureInfo FromLabel(string label, string documentation = "") =>
        new(label, ParameterInfo.ParameterRanges(label), documentation);
}

/// <summary>The call the caret is in: <c>Console.WriteLine(a, |</c> is WriteLine on Console, second argument.</summary>
public sealed record CallSite(IReadOnlyList<string> Qualifier, string Name, int NameStart, int OpenParen, int Argument, bool IsConstructor);

/// <summary>
/// Parameter Info (Ctrl+Shift+Space): which call the caret is in and the ways to call it. Language servers answer with
/// textDocument/signatureHelp; without one, the project's own declarations, the .NET catalog (VB.NET and C#) and the common
/// built-in functions of each language answer.
/// </summary>
public static class ParameterInfo
{
    /// <summary>Words that are followed by "(" but don't call anything (or declare rather than call).</summary>
    private static readonly HashSet<string> NotCalls = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "elif", "while", "for", "foreach", "switch", "catch", "using", "lock", "fixed", "return", "when", "with", "sizeof", "typeof",
        "nameof", "and", "or", "not", "in", "is", "function", "def", "func", "fn", "sub", "await", "yield", "throw", "do", "else", "until",
        "select", "case", "match", "assert", "del",
    };

    /// <summary>Words that, before a name, mean the name is being declared rather than called.</summary>
    private static readonly HashSet<string> Declarers = new(StringComparer.OrdinalIgnoreCase)
    {
        "def", "function", "func", "fn", "sub", "void", "class", "struct", "interface", "record", "enum", "property", "event", "delegate", "operator",
    };

    /// <summary>The innermost call whose argument list the caret is in, or null (outside any call, or in a string or comment).</summary>
    public static CallSite? CallAt(string text, int offset, Language language)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var from = Math.Max(0, offset - 6000);
        from = from > 0 ? text.LastIndexOf('\n', from) + 1 : 0;
        var lineComments = LineComments(language);
        var quotes = language.Strings;
        var vb = language.Id == "vb";
        var stack = new List<(char Bracket, int Position, int Commas)>();
        var i = from;
        while (i < offset)
        {
            var c = text[i];
            if (lineComments.Any(m => string.CompareOrdinal(text, i, m, 0, m.Length) == 0))
            {
                var end = text.IndexOf('\n', i);
                if (end < 0 || end >= offset) return null;
                i = end + 1;
                continue;
            }
            if (language.BlockStart is { } open && language.BlockEnd is { } close && string.CompareOrdinal(text, i, open, 0, open.Length) == 0)
            {
                var end = text.IndexOf(close, i + open.Length, StringComparison.Ordinal);
                if (end < 0 || end + close.Length > offset) return null;
                i = end + close.Length;
                continue;
            }
            if (quotes.Contains(c))
            {
                var j = i + 1;
                while (j < text.Length)
                {
                    if (text[j] == '\\' && !vb) { j += 2; continue; }
                    if (text[j] == c)
                    {
                        if (vb && j + 1 < text.Length && text[j + 1] == c) { j += 2; continue; }
                        break;
                    }
                    if (text[j] == '\n' && c != '`') break;
                    j++;
                }
                if (j >= offset) return null;
                i = j + 1;
                continue;
            }
            switch (c)
            {
                case '(' or '[' or '{':
                    stack.Add((c, i, 0));
                    break;
                case ')' or ']' or '}':
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    break;
                case ',' when stack.Count > 0:
                    stack[^1] = stack[^1] with { Commas = stack[^1].Commas + 1 };
                    break;
            }
            i++;
        }
        if (stack.Count == 0 || stack[^1].Bracket != '(') return null;
        var (_, paren, commas) = stack[^1];
        var k = paren;
        while (k > 0 && text[k - 1] is ' ' or '\t') k--;
        var nameEnd = k;
        while (k > 0 && BasicIntelliSense.IsIdentifier(text[k - 1])) k--;
        if (k == nameEnd || char.IsDigit(text[k])) return null;
        var name = text[k..nameEnd];
        if (NotCalls.Contains(name)) return null;
        var lineStart = k > 0 ? text.LastIndexOf('\n', k - 1) + 1 : 0;
        var before = text[lineStart..k];
        var previous = before.TrimEnd().Split(' ', '\t').LastOrDefault() ?? "";
        if (Declarers.Contains(previous)) return null;
        var qualifier = before.EndsWith('.') ? DotNetIntelliSense.MemberExpression(before[..^1]) : [];
        var isConstructor = previous.Equals("new", StringComparison.OrdinalIgnoreCase);
        return new CallSite(qualifier, name, k, paren, commas, isConstructor);
    }

    private static List<string> LineComments(Language language)
    {
        var list = new List<string>();
        if (language.LineComment != null) list.Add(language.LineComment);
        if (language.Id == "vb") list.Add("REM ");
        return list;
    }

    /// <summary>Where each parameter is in a label like <c>Add(a As Integer, b As Integer) As Integer</c>.</summary>
    public static List<(int Start, int Length)> ParameterRanges(string label)
    {
        var ranges = new List<(int, int)>();
        var open = label.IndexOf('(');
        if (open < 0) return ranges;
        // VB generics: Function Map(Of T)(items As T()) — the parameters are in the second pair.
        if (label.AsSpan(open + 1).TrimStart().StartsWith("Of ", StringComparison.OrdinalIgnoreCase))
        {
            var closeOf = Matching(label, open);
            if (closeOf > 0 && closeOf + 1 < label.Length && label[closeOf + 1] == '(') open = closeOf + 1;
        }
        var close = Matching(label, open);
        if (close < 0) close = label.Length;
        var depth = 0;
        var start = open + 1;
        for (var i = open + 1; i <= close; i++)
        {
            var c = i < close ? label[i] : ',';
            if (c is '(' or '[' or '{' or '<') depth++;
            else if (c is ')' or ']' or '}' or '>' && depth > 0) depth--;
            else if (c == ',' && depth == 0)
            {
                var s = start;
                var e = i;
                while (s < e && char.IsWhiteSpace(label[s])) s++;
                while (e > s && char.IsWhiteSpace(label[e - 1])) e--;
                if (e > s) ranges.Add((s, e - s));
                start = i + 1;
            }
        }
        return ranges;
    }

    private static int Matching(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>Signatures without a language server: project declarations, the .NET catalog and built-in functions.</summary>
    public static List<SignatureInfo> Basic(string text, CallSite call, Language language, IReadOnlyList<CodeSymbol> symbols)
    {
        var comparison = language.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var labels = new List<string>();
        if (DotNetIntelliSense.Supports(language.Id) && call.Qualifier.Count > 0)
        {
            labels.AddRange(DotNetIntelliSense.MethodSignatures(call.Qualifier.ToList(), call.Name, text, call.NameStart, language.Id, symbols));
        }
        else if (call.IsConstructor)
        {
            labels.AddRange(symbols.Where(s => s.Kind == SymbolKind.Constructor && string.Equals(s.Container, call.Name, comparison)).Select(s => s.Signature));
        }
        else if (call.Qualifier.Count == 0)
        {
            labels.AddRange(symbols.Where(s => s.Kind is SymbolKind.Method or SymbolKind.Function && string.Equals(s.Name, call.Name, comparison))
                .Select(s => s.Signature));
        }
        var result = labels.Where(l => l.Contains('(')).Distinct().Select(l => SignatureInfo.FromLabel(l)).ToList();
        var qualified = string.Join(".", call.Qualifier.Append(call.Name));
        foreach (var builtIn in BuiltInFunctions.For(language.Id, qualified))
            if (!result.Any(r => r.Label == builtIn.Label)) result.Add(builtIn);
        return result;
    }

    /// <summary>A language server's signatureHelp answer: the signatures, which one is active and which parameter.</summary>
    public static (List<SignatureInfo> Signatures, int Active, int Parameter) FromLsp(JsonNode? result)
    {
        var signatures = new List<SignatureInfo>();
        var actives = new List<int?>();
        foreach (var signature in (result?["signatures"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            if (Text(signature["label"]) is not { } label) continue;
            var ranges = new List<(int, int)>();
            var docs = new List<string>();
            var searchFrom = Math.Max(label.IndexOf('('), 0);
            foreach (var parameter in (signature["parameters"] as JsonArray)?.OfType<JsonObject>() ?? [])
            {
                var name = parameter["label"];
                if (name is JsonArray { Count: 2 } pair && pair[0]?.GetValue<int>() is { } s && pair[1]?.GetValue<int>() is { } e && s <= e && e <= label.Length)
                {
                    ranges.Add((s, e - s));
                    searchFrom = e;
                }
                else if (Text(name) is { Length: > 0 } part)
                {
                    var at = label.IndexOf(part, searchFrom, StringComparison.Ordinal);
                    if (at < 0) at = label.IndexOf(part, StringComparison.Ordinal);
                    ranges.Add(at < 0 ? (0, 0) : (at, part.Length));
                    if (at >= 0) searchFrom = at + part.Length;
                }
                else ranges.Add((0, 0));
                docs.Add(Documentation(parameter["documentation"]));
            }
            signatures.Add(new SignatureInfo(label, ranges, Documentation(signature["documentation"]), docs));
            actives.Add(Int(signature["activeParameter"]));
        }
        var active = Math.Clamp(Int(result?["activeSignature"]) ?? 0, 0, Math.Max(signatures.Count - 1, 0));
        var parameterIndex = (actives.Count > active ? actives[active] : null) ?? Int(result?["activeParameter"]) ?? 0;
        return (signatures, active, parameterIndex);
    }

    private static string Documentation(JsonNode? node) => node switch
    {
        JsonObject markup => HoverText.Plain(Text(markup["value"]) ?? ""),
        JsonValue value => HoverText.Plain(Text(value) ?? ""),
        _ => "",
    };

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    /// <summary>The parameter to show in bold: the argument the caret is in (the last one for a params/... list).</summary>
    public static int ActiveParameter(SignatureInfo signature, int argument)
    {
        if (signature.Parameters.Count == 0) return -1;
        if (argument < signature.Parameters.Count) return argument;
        var (start, length) = signature.Parameters[^1];
        var last = signature.Label.Substring(start, length);
        return last.Contains("...") || last.Contains("ParamArray", StringComparison.OrdinalIgnoreCase) || last.StartsWith("params ") || last.StartsWith('*')
            ? signature.Parameters.Count - 1 : -1;
    }

    /// <summary>The overload that best fits the arguments typed so far: the first with enough parameters.</summary>
    public static int BestOverload(IReadOnlyList<SignatureInfo> signatures, int argument)
    {
        for (var i = 0; i < signatures.Count; i++)
            if (ActiveParameter(signatures[i], argument) >= 0) return i;
        return 0;
    }
}

/// <summary>
/// The built-in functions beginners use most, with a line on what each does, so Parameter Info and the completion list
/// can explain them even without a language server.
/// </summary>
public static class BuiltInFunctions
{
    private static readonly (string Language, string Name, string Label, string Documentation)[] All =
    [
        ("python", "print", "print(*values, sep=' ', end='\\n')", "Writes the values to the output, separated by sep and followed by end."),
        ("python", "input", "input(prompt='') -> str", "Shows the prompt and waits for the user to type a line; returns what was typed as text."),
        ("python", "len", "len(obj) -> int", "How many items are in a list, string, dictionary or other collection."),
        ("python", "range", "range(start, stop, step=1)", "The numbers from start up to (not including) stop. range(5) is 0, 1, 2, 3, 4."),
        ("python", "int", "int(value, base=10) -> int", "Turns text or a number into a whole number: int(\"42\") is 42."),
        ("python", "float", "float(value) -> float", "Turns text or a number into a decimal number: float(\"2.5\") is 2.5."),
        ("python", "str", "str(value) -> str", "Turns a value into text: str(42) is \"42\"."),
        ("python", "open", "open(file, mode='r', encoding=None)", "Opens a file. mode is 'r' to read, 'w' to write (replacing it) or 'a' to add to the end."),
        ("python", "enumerate", "enumerate(iterable, start=0)", "Pairs each item with its position: for i, item in enumerate(items)."),
        ("python", "zip", "zip(*iterables)", "Walks several lists side by side: for a, b in zip(list1, list2)."),
        ("python", "sorted", "sorted(iterable, key=None, reverse=False) -> list", "A new list with the items in order."),
        ("python", "abs", "abs(x)", "The number without its sign: abs(-3) is 3."),
        ("python", "round", "round(number, ndigits=None)", "Rounds to ndigits decimal places (to a whole number without it)."),
        ("python", "min", "min(iterable, key=None)", "The smallest item."),
        ("python", "max", "max(iterable, key=None)", "The largest item."),
        ("python", "sum", "sum(iterable, start=0)", "Adds up the numbers."),
        ("python", "isinstance", "isinstance(obj, class_or_tuple) -> bool", "Whether a value is of a type: isinstance(x, int)."),
        ("python", "list", "list(iterable=()) -> list", "A new list, from the items of another collection."),
        ("python", "dict", "dict(**kwargs) -> dict", "A new dictionary (key → value pairs)."),
        ("c", "printf", "int printf(const char *format, ...)", "Writes formatted text: %d for whole numbers, %f for decimals, %s for text, %c for a character."),
        ("c", "scanf", "int scanf(const char *format, ...)", "Reads input into variables; pass their addresses: scanf(\"%d\", &number)."),
        ("c", "malloc", "void *malloc(size_t size)", "Reserves size bytes of memory; give it back with free() when done."),
        ("c", "free", "void free(void *ptr)", "Gives back memory reserved with malloc."),
        ("c", "strlen", "size_t strlen(const char *s)", "How many characters a string has (not counting the ending \\0)."),
        ("c", "strcpy", "char *strcpy(char *dest, const char *src)", "Copies src into dest; dest must be big enough."),
        ("c", "strcmp", "int strcmp(const char *a, const char *b)", "Compares two strings: 0 when they're equal."),
        ("c", "fopen", "FILE *fopen(const char *path, const char *mode)", "Opens a file: mode \"r\" reads, \"w\" writes, \"a\" appends. Returns NULL if it fails."),
        ("c", "fclose", "int fclose(FILE *stream)", "Closes a file opened with fopen."),
        ("cpp", "printf", "int printf(const char *format, ...)", "Writes formatted text: %d for whole numbers, %f for decimals, %s for text."),
        ("javascript", "console.log", "console.log(...data)", "Writes the values to the console (the Output window)."),
        ("javascript", "parseInt", "parseInt(string, radix?)", "Reads a whole number from text: parseInt(\"42\") is 42."),
        ("javascript", "parseFloat", "parseFloat(string)", "Reads a decimal number from text: parseFloat(\"2.5\") is 2.5."),
        ("javascript", "setTimeout", "setTimeout(callback, delay)", "Runs the function once, after delay milliseconds."),
        ("javascript", "setInterval", "setInterval(callback, delay)", "Runs the function again and again, every delay milliseconds."),
        ("javascript", "alert", "alert(message)", "Shows a message box in the browser."),
        ("javascript", "fetch", "fetch(url, options?)", "Downloads from a URL; await it to get the response."),
        ("javascript", "document.getElementById", "document.getElementById(id)", "The element on the page with this id, or null."),
        ("javascript", "document.querySelector", "document.querySelector(selectors)", "The first element on the page that matches the CSS selector, or null."),
        ("go", "fmt.Println", "func Println(a ...any) (n int, err error)", "Writes the values separated by spaces, then a new line."),
        ("go", "fmt.Printf", "func Printf(format string, a ...any) (n int, err error)", "Writes formatted text: %d for whole numbers, %f for decimals, %s for text, %v for any value."),
        ("go", "fmt.Sprintf", "func Sprintf(format string, a ...any) string", "Like Printf, but returns the text instead of writing it."),
        ("go", "len", "func len(v Type) int", "How many items are in a slice, map, string or array."),
        ("go", "append", "func append(slice []Type, elems ...Type) []Type", "Adds items to the end of a slice; keep the result: s = append(s, x)."),
        ("go", "make", "func make(t Type, size ...IntegerType) Type", "Creates a slice, map or channel: make([]int, 5), make(map[string]int)."),
        ("java", "System.out.println", "void println(Object x)", "Writes the value, then a new line."),
        ("java", "System.out.print", "void print(Object x)", "Writes the value without a new line."),
        ("java", "System.out.printf", "PrintStream printf(String format, Object... args)", "Writes formatted text: %d for whole numbers, %f for decimals, %s for text."),
        ("java", "Integer.parseInt", "static int parseInt(String s)", "Reads a whole number from text: Integer.parseInt(\"42\") is 42."),
        ("rust", "println", "println!(\"{}\", value)", "Writes formatted text and a new line; {} is replaced by each value in turn."),
        ("swift", "print", "print(_ items: Any..., separator: String = \" \", terminator: String = \"\\n\")", "Writes the values to the output."),
        ("vb", "MsgBox", "MsgBox(Prompt As Object, Buttons As MsgBoxStyle, Title As Object) As MsgBoxResult", "Shows a message box and returns the button that was clicked."),
        ("vb", "InputBox", "InputBox(Prompt As String, Title As String, DefaultResponse As String) As String", "Asks the user to type a value and returns it."),
    ];

    /// <summary>The built-in function with this name (or qualified name, like <c>console.log</c>) in a language.</summary>
    public static IEnumerable<SignatureInfo> For(string languageId, string name)
    {
        var language = languageId switch { "typescript" => "javascript", "c" or "cpp" => languageId, _ => languageId };
        var comparison = languageId == "vb" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return All.Where(f => (f.Language == language || (language == "cpp" && f.Language == "c")) && string.Equals(f.Name, name, comparison))
            .Select(f => SignatureInfo.FromLabel(f.Label, f.Documentation));
    }

    /// <summary>What a built-in function does, for the completion list's explanation.</summary>
    public static string? Explain(string languageId, string name) => For(languageId, name).FirstOrDefault() is { } found
        ? found.Label + "\n\n" + found.Documentation : null;
}

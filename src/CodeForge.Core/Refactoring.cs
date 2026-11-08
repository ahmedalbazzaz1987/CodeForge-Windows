using System.Text;
using System.Text.RegularExpressions;

namespace CodeForge.Core;

/// <summary>
/// Extract Method for languages without a language server that offers it (VB.NET above all), and as a fallback for
/// C#, Python, JavaScript, TypeScript and Swift. It works on whole lines inside one method: variables declared before
/// the selection become parameters, and one variable set in the selection and used after it becomes the return value.
/// (The same rules as the Mac version.)
/// </summary>
public static class ExtractMethod
{
    /// <param name="NameStart">Where the new method's name is in <paramref name="Text"/>, so the editor can select it.</param>
    public sealed record Result(string Text, int NameStart, int NameLength);

    public sealed class Failure(string message) : Exception(message);

    public static string DefaultName(string languageId) => languageId switch
    {
        "python" => "new_method",
        "javascript" or "typescript" or "swift" => "newMethod",
        _ => "NewMethod",
    };

    public static bool Supports(string languageId) => Flavor.For(languageId) != null;

    /// <summary>Extracts the lines the selection touches; throws <see cref="Failure"/> with a message for the user.</summary>
    public static Result Extract(string text, int selectionStart, int selectionLength, string languageId, string? name = null)
    {
        var flavor = Flavor.For(languageId) ?? throw new Failure("Extract Method works for VB.NET, C#, Python, JavaScript, TypeScript and Swift.");
        var language = Languages.ById(languageId);
        name ??= DefaultName(languageId);
        text = text.Replace("\r\n", "\n");
        var lines = text.Split('\n');
        var masked = Mask(text, language).Split('\n');
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++) if (text[i] == '\n') starts.Add(i + 1);
        int LineOf(int offset) => starts.FindLastIndex(s => s <= offset) is var line && line >= 0 ? line : 0;

        // Whole lines, without blank ones at either end.
        var first = LineOf(selectionStart);
        var last = LineOf(Math.Max(selectionStart, selectionStart + selectionLength - (selectionLength > 0 ? 1 : 0)));
        while (first <= last && lines[first].Trim().Length == 0) first++;
        while (last >= first && lines[last].Trim().Length == 0) last--;
        if (first > last) throw new Failure("Select the lines to move into the new method.");

        var selected = string.Join("\n", masked[first..(last + 1)]);
        if (Regex.IsMatch(selected, flavor.ReturnPattern, flavor.CaseInsensitive ? RegexOptions.IgnoreCase | RegexOptions.Multiline : RegexOptions.Multiline))
            throw new Failure("The selection returns from the method, so it can't be moved into a new one.");

        // The enclosing method: the nearest header above the selection whose body contains it.
        (int Header, int End)? method = null;
        for (var index = first - 1; index >= 0; index--)
        {
            if (flavor.IsHeader(masked[index]) && flavor.End(index, masked) is { } end && end > last) { method = (index, end); break; }
        }
        if (method == null && !flavor.AllowsTopLevel(masked)) throw new Failure("Select lines inside one method or function.");
        var bodyStart = method is { } m1 ? m1.Header + 1 : 0;
        var bodyEnd = method?.End ?? lines.Length;

        // Variables: parameters of the method, and declarations before, inside and after the selection.
        var known = new Dictionary<string, string?>();
        var order = new List<string>();
        void Remember(IEnumerable<(string Name, string? Type)> found)
        {
            foreach (var (variable, type) in found)
            {
                if (known.ContainsKey(Key(variable))) continue;
                known[Key(variable)] = type;
                order.Add(variable);
            }
        }
        string Key(string variable) => flavor.CaseInsensitive ? variable.ToLowerInvariant() : variable;
        if (method is { } m2) Remember(flavor.Parameters(lines[m2.Header]));
        for (var index = bodyStart; index < first; index++) Remember(flavor.Declarations(masked[index]));
        var declaredBefore = order.Select(Key).ToHashSet();
        var declaredInside = new List<(string Name, string? Type)>();
        for (var index = first; index <= last; index++) declaredInside.AddRange(flavor.Declarations(masked[index]));

        var after = string.Join("\n", masked[(last + 1)..Math.Max(bodyEnd, last + 1)]);
        var usedInside = Identifiers(selected).Select(Key).ToHashSet();
        var usedAfter = Identifiers(after).Select(Key).ToHashSet();
        var assignedInside = Assignments(selected).Select(Key).ToHashSet();

        var parameters = order.Where(v => usedInside.Contains(Key(v)) && declaredBefore.Contains(Key(v)) && !flavor.IsReceiver(v)).ToList();
        var results = new List<(string Name, string? Type, bool DeclaredInside)>();
        foreach (var (variable, type) in declaredInside)
        {
            if (usedAfter.Contains(Key(variable)) && !declaredBefore.Contains(Key(variable)) && !results.Any(r => Key(r.Name) == Key(variable)))
                results.Add((variable, type, true));
        }
        foreach (var variable in parameters)
        {
            if (assignedInside.Contains(Key(variable)) && usedAfter.Contains(Key(variable))) results.Add((variable, known[Key(variable)], false));
        }
        if (results.Count > 1)
            throw new Failure($"The selection sets {string.Join(", ", results.Select(r => $"“{r.Name}”"))}, which are all used afterwards. A new method can return only one of them.");

        // Types, where the language needs them.
        var typed = new List<(string Name, string Type)>();
        foreach (var variable in parameters)
        {
            var type = known[Key(variable)] ?? flavor.InferType(variable, lines[bodyStart..first]);
            if (flavor.NeedsTypes && type == null) throw UnknownType(variable);
            typed.Add((variable, type ?? ""));
        }
        string? resultType = null;
        if (results.Count == 1)
        {
            resultType = results[0].Type ?? flavor.InferType(results[0].Name, lines[first..(last + 1)]);
            if (flavor.NeedsTypes && resultType == null) throw UnknownType(results[0].Name);
        }

        // The new method's body, re-indented one level inside the method's own indentation.
        var methodIndent = method is { } m3 ? Indentation(lines[m3.Header]) : "";
        var unit = lines.Any(l => l.StartsWith('\t')) ? "\t" : "    ";
        var selectionIndent = lines[first..(last + 1)].Where(l => l.Trim().Length > 0).Select(Indentation).MinBy(i => i.Length) ?? "";
        var body = lines[first..(last + 1)].Select(l => l.Trim().Length == 0 ? "" : methodIndent + unit + l[selectionIndent.Length..]).ToList();
        var isStatic = method is { } m4 && flavor.IsStatic(lines, m4.Header);
        var receiver = method is { } m5 ? flavor.Receiver(lines[m5.Header]) : null;
        (string, string?)? result = results.Count == 1 ? (results[0].Name, resultType) : null;
        var declaration = flavor.Declare(name, typed, result, isStatic, receiver, methodIndent, unit, body, topLevel: method == null);

        // The call that replaces the selection.
        var callee = receiver != null ? $"{receiver}.{name}" : name;
        var call = $"{callee}({string.Join(", ", parameters)})";
        call = results.Count == 1 ? flavor.AssignCall(results[0].Name, resultType, results[0].DeclaredInside, call) : flavor.StatementCall(call);

        var output = lines.ToList();
        // Insert the method first (it comes after the selection), then replace the selection.
        var insertAt = method is { } m6 ? m6.End + (flavor.EndIsLine ? 1 : 0) : flavor.TopLevelInsertion(lines, masked);
        var block = declaration.Split('\n');
        var insertion = insertAt <= first ? block.Append("").ToList() : block.Prepend("").ToList();
        var clamped = Math.Min(insertAt, output.Count);
        output.InsertRange(clamped, insertion);
        var shift = clamped <= first ? insertion.Count : 0;
        output.RemoveRange(first + shift, last - first + 1);
        output.Insert(first + shift, selectionIndent + call);
        var newText = string.Join("\n", output);

        // Find the declaration's name to select it.
        var removed = clamped > first ? last - first : 0;
        var nameLine = Array.FindIndex(block, l => l.Contains(name + "("));
        var declarationLine = clamped - removed + (clamped <= first ? 0 : 1) + Math.Max(nameLine, 0);
        var lineStart = 0;
        for (var i = 0; i < declarationLine; i++) lineStart += output[i].Length + 1;
        var column = output[declarationLine].IndexOf(name + "(", StringComparison.Ordinal);
        return new Result(newText, lineStart + Math.Max(column, 0), name.Length);
    }

    private static Failure UnknownType(string name) => new($"Couldn't work out the type of “{name}”. Give it an explicit type, then try again.");

    private static string Indentation(string line) => new(line.TakeWhile(c => c is ' ' or '\t').ToArray());

    /// <summary>Blanks out strings and comments (keeping line breaks) so that words in them aren't taken for code.</summary>
    public static string Mask(string text, Language language)
    {
        var output = new StringBuilder(text);
        var comment = language.LineComment?.Trim();
        var i = 0;
        void Blank(int from, int to)
        {
            for (var k = from; k < Math.Min(to, text.Length); k++) if (text[k] != '\n') output[k] = ' ';
        }
        while (i < text.Length)
        {
            var c = text[i];
            if (!string.IsNullOrEmpty(comment) && string.CompareOrdinal(text, i, comment, 0, comment.Length) == 0)
            {
                var end = text.IndexOf('\n', i);
                end = end < 0 ? text.Length : end;
                Blank(i, end);
                i = end;
            }
            else if (language.BlockStart != null && language.BlockEnd != null && string.CompareOrdinal(text, i, language.BlockStart, 0, language.BlockStart.Length) == 0)
            {
                var end = text.IndexOf(language.BlockEnd, i + language.BlockStart.Length, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + language.BlockEnd.Length;
                Blank(i, end);
                i = end;
            }
            else if (language.Strings.Contains(c))
            {
                // Python's """ and ''' strings run over lines; other strings end at the line.
                var triple = new string(c, 3);
                var multiline = language.Id == "python" && string.CompareOrdinal(text, i, triple, 0, 3) == 0 || c == '`';
                var close = multiline && c != '`' ? triple : c.ToString();
                var k = i + (multiline && c != '`' ? 3 : 1);
                while (k < text.Length)
                {
                    if (text[k] == '\\' && language.Id != "vb") { k += 2; continue; }
                    if (!multiline && text[k] == '\n') break;
                    if (string.CompareOrdinal(text, k, close, 0, close.Length) == 0)
                    {
                        // VB writes a quote inside a string as "".
                        if (language.Id == "vb" && k + 1 < text.Length && text[k + 1] == c) { k += 2; continue; }
                        k += close.Length;
                        break;
                    }
                    k++;
                }
                // Keep the quotes, so the code still reads as a string (inferring a type from "…").
                Blank(i + 1, Math.Max(i + 1, k - 1));
                i = Math.Max(k, i + 1);
            }
            else
            {
                i++;
            }
        }
        return output.ToString();
    }

    private static IEnumerable<string> Identifiers(string text) =>
        Regex.Matches(text, @"(?<![\w.$])[A-Za-z_$][\w$]*").Select(m => m.Value);

    private static IEnumerable<string> Assignments(string text)
    {
        foreach (Match match in Regex.Matches(text, @"(?<![\w.$])([A-Za-z_$][\w$]*)\s*(?:[-+*/&|^%]|<<|>>)?=(?!=)")) yield return match.Groups[1].Value;
        foreach (Match match in Regex.Matches(text, @"([A-Za-z_$][\w$]*)\s*(?:\+\+|--)|(?:\+\+|--)\s*([A-Za-z_$][\w$]*)"))
            yield return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }
}

/// <summary>How one language writes methods, variables and calls.</summary>
internal sealed class Flavor
{
    public enum Blocks { Braces, Vb, Indentation }

    public required string Id { get; init; }
    public bool CaseInsensitive { get; init; }
    public Blocks Kind { get; init; }
    public bool NeedsTypes { get; init; }
    public required Regex Header { get; init; }
    public required string ReturnPattern { get; init; }
    public required Regex[] DeclarationPatterns { get; init; }

    private const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.Multiline;
    private const RegexOptions Ml = RegexOptions.Multiline;

    public static Flavor? For(string id) => id switch
    {
        "vb" => new Flavor
        {
            Id = id, CaseInsensitive = true, Kind = Blocks.Vb, NeedsTypes = true,
            Header = new Regex(@"^\s*(?:(?:Public|Private|Protected|Friend|Shared|Overrides|Overridable|Overloads|Async|Static|Iterator)\s+)*(?:Sub|Function)\s+\w+\s*(?:\(Of[^)]*\))?\s*\(", Ci),
            ReturnPattern = @"^\s*(?:Return\b|Exit\s+(?:Sub|Function)\b|Yield\b)",
            DeclarationPatterns =
            [
                new Regex(@"\b(?:Dim|Static)\s+(?<name>\w+)(?:\(\))?(?:\s+As\s+(?:New\s+)?(?<type>[\w.]+(?:\(Of[^)]*\))?(?:\(\))?))?", Ci),
                new Regex(@"\bFor\s+(?:Each\s+)?(?<name>\w+)(?:\s+As\s+(?<type>[\w.]+(?:\(Of[^)]*\))?))?\s+(?:=|In\b)", Ci),
                new Regex(@"\bUsing\s+(?<name>\w+)(?:\s+As\s+(?:New\s+)?(?<type>[\w.]+))?", Ci),
            ],
        },
        "csharp" => new Flavor
        {
            Id = id, Kind = Blocks.Braces, NeedsTypes = true,
            Header = new Regex(@"^\s*(?:(?:public|private|protected|internal|static|async|override|virtual|sealed|abstract|partial|unsafe|extern|new)\s+)*(?!(?:if|for|foreach|while|switch|catch|using|lock|return|new|else|fixed|when|await|throw|yield|case|var)\b)[\w<>\[\],.?]+(?:\s*<[^>]*>)?\s+(?!(?:if|for|foreach|while|switch|catch|using|lock)\b)\w+\s*(?:<[^>]*>)?\s*\([^;]*$", Ml),
            ReturnPattern = @"\b(?:return|yield)\b",
            DeclarationPatterns =
            [
                new Regex(@"(?<![\w.])(?!(?:return|new|else|case|throw|await|using|goto|in|is|as|out|ref|params|yield)\b)(?<type>var|[A-Za-z_][\w.]*(?:<[^;=()]*?>)?(?:\[\])?\??)\s+(?<name>[A-Za-z_]\w*)\s*(?:=(?!=)|;|\bin\b)", Ml),
            ],
        },
        "python" => new Flavor
        {
            Id = id, Kind = Blocks.Indentation,
            Header = new Regex(@"^\s*(?:async\s+)?def\s+\w+\s*\(", Ml),
            ReturnPattern = @"\b(?:return|yield)\b",
            DeclarationPatterns =
            [
                new Regex(@"^\s*(?<name>[A-Za-z_]\w*)\s*(?::\s*(?<type>[^=\n]+))?\s*=(?!=)", Ml),
                new Regex(@"\bfor\s+(?<name>[A-Za-z_]\w*)\s+in\b", Ml),
                new Regex(@"\bas\s+(?<name>[A-Za-z_]\w*)\s*:", Ml),
            ],
        },
        "javascript" or "typescript" => new Flavor
        {
            Id = id, Kind = Blocks.Braces,
            Header = new Regex(@"^\s*(?:(?:export|default|async|static|public|private|protected)\s+)*(?:function\s*\*?\s*\w+\s*\(|(?:const|let|var)\s+\w+\s*=\s*(?:async\s+)?(?:function\b|\([^)]*\)\s*(?::[^=]+)?=>)|(?!(?:if|for|while|switch|catch|return|function)\b)\w+\s*\([^)]*\)\s*(?::\s*[^{]+)?\{)", Ml),
            ReturnPattern = @"\b(?:return|yield)\b",
            DeclarationPatterns =
            [
                new Regex(@"\b(?:let|const|var)\s+(?<name>[A-Za-z_$][\w$]*)(?:\s*:\s*(?<type>[^=;]+?))?\s*(?:=(?!=)|;|$|\bof\b|\bin\b)", Ml),
            ],
        },
        "swift" => new Flavor
        {
            Id = id, Kind = Blocks.Braces, NeedsTypes = true,
            Header = new Regex(@"^\s*(?:(?:@\w+|public|private|fileprivate|internal|open|static|class|override|mutating|final|nonisolated)\s+)*func\s+\w+", Ml),
            ReturnPattern = @"\breturn\b",
            DeclarationPatterns =
            [
                new Regex(@"\b(?:let|var)\s+(?<name>[A-Za-z_]\w*)(?:\s*:\s*(?<type>[^={\n]+?))?\s*(?:=|$|\{)", Ml),
                new Regex(@"\bfor\s+(?<name>[A-Za-z_]\w*)\s+in\b", Ml),
            ],
        },
        _ => null,
    };

    public bool EndIsLine => Kind != Blocks.Indentation;

    public bool IsHeader(string line) => Header.IsMatch(line);

    private static int IndentWidth(string line) => line.TakeWhile(c => c is ' ' or '\t').Count();

    /// <summary>For braces and VB: the line with the method's closing <c>}</c> or <c>End Sub</c>. For Python: the first line after the body.</summary>
    public int? End(int headerIndex, string[] lines)
    {
        switch (Kind)
        {
            case Blocks.Vb:
                var headerIndent = IndentWidth(lines[headerIndex]);
                for (var index = headerIndex + 1; index < lines.Length; index++)
                {
                    if (Regex.IsMatch(lines[index], @"^\s*End\s+(?:Sub|Function)\b", RegexOptions.IgnoreCase) && IndentWidth(lines[index]) <= headerIndent) return index;
                }
                return null;
            case Blocks.Braces:
                var depth = 0;
                var opened = false;
                for (var index = headerIndex; index < lines.Length; index++)
                {
                    foreach (var character in lines[index])
                    {
                        if (character == '{') { depth++; opened = true; }
                        if (character == '}') depth--;
                        if (opened && depth == 0) return index;
                    }
                    // An expression-bodied or abstract method has no body to extract from.
                    if (!opened && index > headerIndex + 3) return null;
                }
                return null;
            default:
                var indent = IndentWidth(lines[headerIndex]);
                var lastContent = headerIndex;
                for (var index = headerIndex + 1; index < lines.Length; index++)
                {
                    if (lines[index].Trim().Length == 0) continue;
                    if (IndentWidth(lines[index]) <= indent) break;
                    lastContent = index;
                }
                return lastContent + 1;
        }
    }

    /// <summary>Top-level code (C# top-level statements, Python and JavaScript scripts, Swift main.swift).</summary>
    public bool AllowsTopLevel(string[] masked) => Id switch
    {
        "python" or "javascript" or "typescript" => true,
        "csharp" or "swift" => !masked.Any(l => Regex.IsMatch(l, @"^\s*(?:(?:public|internal|private|static|partial|sealed|abstract|final)\s+)*(?:class|struct|record|namespace|enum|interface|actor|extension)\b")),
        _ => false,
    };

    /// <summary>Where a new top-level function goes: after the imports (Python, JavaScript), or at the end (C#, Swift).</summary>
    public int TopLevelInsertion(string[] lines, string[] masked)
    {
        if (Id is "python" or "javascript" or "typescript")
            return Array.FindLastIndex(masked, l => Regex.IsMatch(l, @"^(?:import|from)\s")) + 1;
        var end = lines.Length;
        while (end > 0 && lines[end - 1].Trim().Length == 0) end--;
        return end;
    }

    public List<(string Name, string? Type)> Declarations(string masked)
    {
        var found = new List<(string Name, string? Type)>();
        foreach (var pattern in DeclarationPatterns)
        {
            foreach (Match match in pattern.Matches(masked))
            {
                var type = match.Groups["type"].Success ? match.Groups["type"].Value.Trim() : null;
                if (type == "var" || Id == "python") type = null;
                found.Add((match.Groups["name"].Value, type));
            }
        }
        // VB: `Dim a, b As Integer` gives both names the type.
        if (Id == "vb" && Regex.Match(masked, @"\bDim\s+(?<names>\w+\s*,[\w\s,]+?)\s+As\s+(?!New\b)(?<type>[\w.]+)", RegexOptions.IgnoreCase) is { Success: true } list)
        {
            var type = list.Groups["type"].Value;
            foreach (var name in list.Groups["names"].Value.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
            {
                var index = found.FindIndex(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) found[index] = (found[index].Name, type); else found.Add((name, type));
            }
        }
        return found;
    }

    public List<(string Name, string? Type)> Parameters(string headerLine)
    {
        var open = headerLine.IndexOf('(');
        var close = headerLine.LastIndexOf(')');
        if (open < 0 || close <= open) return [];
        var list = headerLine[(open + 1)..close];
        if (Id == "vb" && list.StartsWith("of ", StringComparison.OrdinalIgnoreCase))
        {
            // Sub Foo(Of T)(x As T)
            var inner = Regex.Match(headerLine, @"\)\s*\(");
            if (!inner.Success) return [];
            list = headerLine[(inner.Index + inner.Length)..close];
        }
        var parts = new List<string>();
        var depth = 0;
        var current = new StringBuilder();
        foreach (var character in list)
        {
            if ("<([".Contains(character)) depth++;
            if (">)]".Contains(character)) depth--;
            if (character == ',' && depth == 0) { parts.Add(current.ToString()); current.Clear(); } else current.Append(character);
        }
        parts.Add(current.ToString());
        var result = new List<(string Name, string? Type)>();
        foreach (var raw in parts)
        {
            var part = raw.Trim();
            if (part.IndexOf('=') is var equals and >= 0) part = part[..equals].Trim();
            if (part.Length == 0) continue;
            switch (Id)
            {
                case "vb":
                    var words = Regex.Replace(part, @"^(?:(?:ByVal|ByRef|Optional|ParamArray)\s+)+", "", RegexOptions.IgnoreCase);
                    var pieces = Regex.Split(words, @"\s+As\s+", RegexOptions.IgnoreCase);
                    result.Add((pieces[0].Replace("()", "").Trim(), pieces.Length > 1 ? pieces[1].Trim() : null));
                    break;
                case "csharp":
                    var cleaned = Regex.Replace(part, @"^(?:(?:this|ref|out|in|params)\s+)+", "");
                    var space = cleaned.LastIndexOf(' ');
                    if (space > 0) result.Add((cleaned[(space + 1)..], cleaned[..space].Trim()));
                    break;
                case "swift":
                    var halves = part.Split(':', 2).Select(p => p.Trim()).ToArray();
                    if (halves.Length == 2) result.Add((halves[0].Split(' ').Last(), halves[1].Replace("inout ", "")));
                    break;
                default:
                    var sides = part.Split(':', 2).Select(p => p.Trim()).ToArray();
                    var bare = sides[0].Trim('*', '.', ' ', '{', '}', '[', ']', '?');
                    if (bare.Length > 0 && bare.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '$'))
                        result.Add((bare, Id == "typescript" && sides.Length > 1 ? sides[1] : null));
                    break;
            }
        }
        return result;
    }

    /// <summary>Python's <c>self</c> and <c>cls</c> aren't passed as arguments.</summary>
    public bool IsReceiver(string name) => Id == "python" && name is "self" or "cls";

    /// <summary><c>self</c> for Python methods, <c>this</c> for JavaScript class methods; null for plain functions.</summary>
    public string? Receiver(string headerLine) => Id switch
    {
        "python" => Parameters(headerLine).FirstOrDefault().Name is "self" or "cls" ? Parameters(headerLine)[0].Name : null,
        "javascript" or "typescript" => Regex.IsMatch(headerLine, @"\bfunction\b|=>|^\s*(?:export\s+)?(?:const|let|var)\s") ? null : "this",
        _ => null,
    };

    public bool IsStatic(string[] lines, int header) => Id switch
    {
        "vb" => Regex.IsMatch(lines[header], @"\bShared\b", RegexOptions.IgnoreCase),
        "csharp" or "swift" => Regex.IsMatch(lines[header], @"\bstatic\b"),
        "python" => header > 0 && lines[header - 1].Trim() == "@staticmethod",
        _ => false,
    };

    /// <summary>The type of a variable declared without one, from the value it starts with.</summary>
    public string? InferType(string name, string[] lines)
    {
        var options = CaseInsensitive ? RegexOptions.IgnoreCase : RegexOptions.None;
        foreach (var line in lines.Reverse())
        {
            var match = Regex.Match(line, @"\b" + Regex.Escape(name) + @"\s*=\s*(.+)$", options);
            if (!match.Success) continue;
            var value = match.Groups[1].Value.Trim();
            var (integer, real, text, boolean) = Id switch
            {
                "vb" => ("Integer", "Double", "String", "Boolean"),
                "swift" => ("Int", "Double", "String", "Bool"),
                _ => ("int", "double", "string", "bool"),
            };
            if (Regex.IsMatch(value, @"^-?\d+;?$")) return integer;
            if (Regex.IsMatch(value, @"^-?\d+\.\d+;?$")) return real;
            if (value.StartsWith('"') || value.StartsWith("$\"")) return text;
            if (value.ToLowerInvariant() is "true" or "false" or "true;" or "false;") return boolean;
            if (Regex.Match(value, @"^(?:New|new)\s+([\w.]+(?:<[^>]*>|\(Of[^)]*\))?)") is { Success: true } created) return created.Groups[1].Value;
            if (Id == "swift" && Regex.Match(value, @"^[A-Z]\w*(?=\()") is { Success: true } initializer) return initializer.Value;
            return null;
        }
        return null;
    }

    public string Declare(string name, List<(string Name, string Type)> parameters, (string Name, string? Type)? result, bool isStatic,
        string? receiver, string indent, string unit, List<string> body, bool topLevel)
    {
        var inner = indent + unit;
        var lines = new List<string>();
        switch (Id)
        {
            case "vb":
                var vbList = string.Join(", ", parameters.Select(p => $"{p.Name} As {p.Type}"));
                var shared = isStatic ? "Shared " : "";
                if (result is { } vbResult)
                {
                    lines.Add($"{indent}Private {shared}Function {name}({vbList}) As {vbResult.Type ?? "Object"}");
                    lines.AddRange(body);
                    lines.Add($"{inner}Return {vbResult.Name}");
                    lines.Add($"{indent}End Function");
                }
                else
                {
                    lines.Add($"{indent}Private {shared}Sub {name}({vbList})");
                    lines.AddRange(body);
                    lines.Add($"{indent}End Sub");
                }
                break;
            case "csharp":
                var csList = string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"));
                var modifiers = topLevel ? "static " : "private " + (isStatic ? "static " : "");
                lines.Add($"{indent}{modifiers}{(result is { } r ? r.Type ?? "object" : "void")} {name}({csList})");
                lines.Add($"{indent}{{");
                lines.AddRange(body);
                if (result is { } csResult) lines.Add($"{inner}return {csResult.Name};");
                lines.Add($"{indent}}}");
                break;
            case "python":
                var pyList = string.Join(", ", (receiver != null ? new[] { receiver } : []).Concat(parameters.Select(p => p.Name)));
                if (isStatic) lines.Add($"{indent}@staticmethod");
                lines.Add($"{indent}def {name}({pyList}):");
                lines.AddRange(body);
                if (result is { } pyResult) lines.Add($"{inner}return {pyResult.Name}");
                break;
            case "javascript" or "typescript":
                var jsList = string.Join(", ", parameters.Select(p => Id == "typescript" && p.Type.Length > 0 ? $"{p.Name}: {p.Type}" : p.Name));
                lines.Add(receiver != null ? $"{indent}{(isStatic ? "static " : "")}{name}({jsList}) {{" : $"{indent}function {name}({jsList}) {{");
                lines.AddRange(body);
                if (result is { } jsResult) lines.Add($"{inner}return {jsResult.Name};");
                lines.Add($"{indent}}}");
                break;
            default:
                var swiftList = string.Join(", ", parameters.Select(p => $"_ {p.Name}: {p.Type}"));
                var swiftModifiers = topLevel ? "" : "private " + (isStatic ? "static " : "");
                lines.Add($"{indent}{swiftModifiers}func {name}({swiftList}){(result is { } sr ? $" -> {sr.Type ?? "Any"}" : "")} {{");
                lines.AddRange(body);
                if (result is { } swiftResult) lines.Add($"{inner}return {swiftResult.Name}");
                lines.Add($"{indent}}}");
                break;
        }
        return string.Join("\n", lines);
    }

    public string StatementCall(string call) => Id is "vb" or "python" or "swift" ? call : call + ";";

    public string AssignCall(string variable, string? type, bool declare, string call)
    {
        if (!declare) return StatementCall($"{variable} = {call}");
        return Id switch
        {
            "vb" => $"Dim {variable} As {type ?? "Object"} = {call}",
            "csharp" => $"{type ?? "var"} {variable} = {call};",
            "python" => $"{variable} = {call}",
            "swift" => $"let {variable} = {call}",
            _ => $"let {variable} = {call};",
        };
    }
}

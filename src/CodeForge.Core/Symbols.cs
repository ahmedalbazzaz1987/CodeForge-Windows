using System.Text.RegularExpressions;

namespace CodeForge.Core;

public enum SymbolKind
{
    Namespace, Module, Class, Structure, Interface, Enumeration, Method, Constructor, Property, Field, Constant, Event, EnumMember, Function, Variable,
}

/// <summary>
/// A declaration found in source code: a namespace, type or member. Used by Class View and the Object Browser
/// (the same parser as the Mac version's).
/// </summary>
/// <param name="Container">The enclosing type ("Program" for <c>Sub Main</c> in <c>Module Program</c>).</param>
/// <param name="Signature">The declaration, trimmed: <c>Public Function Add(a As Integer, b As Integer) As Integer</c>.</param>
/// <param name="Type">The declared type or return type, when it's written down.</param>
/// <param name="Line">1-based.</param>
public sealed record CodeSymbol(string Name, SymbolKind Kind, string? Container, string? Namespace, string Signature, string? Type,
    bool IsShared, int Line, string? Path)
{
    public bool IsType => Kind is SymbolKind.Module or SymbolKind.Class or SymbolKind.Structure or SymbolKind.Interface or SymbolKind.Enumeration;

    /// <summary>"MyApp.Calculator.Add"</summary>
    public string QualifiedName => string.Join(".", new[] { Namespace, Container, Name }.Where(p => !string.IsNullOrEmpty(p)));

    public string KindTitle => Kind switch
    {
        SymbolKind.Enumeration => "Enum",
        SymbolKind.EnumMember => "Enum member",
        _ => Kind.ToString(),
    };

    /// <summary>The LSP CompletionItemKind number, for the same badges as the completion list.</summary>
    public int CompletionKind => Kind switch
    {
        SymbolKind.Namespace or SymbolKind.Module => 9,
        SymbolKind.Class => 7,
        SymbolKind.Structure => 22,
        SymbolKind.Interface => 8,
        SymbolKind.Enumeration => 13,
        SymbolKind.Method or SymbolKind.Function => 2,
        SymbolKind.Constructor => 4,
        SymbolKind.Property => 10,
        SymbolKind.Field or SymbolKind.Variable => 5,
        SymbolKind.Constant => 21,
        SymbolKind.Event => 23,
        _ => 20,
    };
}

public static class SymbolParser
{
    private static readonly HashSet<string> Supported =
        ["vb", "csharp", "python", "javascript", "typescript", "swift", "java", "kotlin", "go", "rust", "cpp", "c", "objc", "php", "fsharp"];

    public static bool Supports(string languageId) => Supported.Contains(languageId);

    public static List<CodeSymbol> Symbols(string text, string languageId, string? path = null)
    {
        if (!Supports(languageId)) return [];
        text = text.Replace("\r\n", "\n");
        var masked = ExtractMethod.Mask(text, Languages.ById(languageId)).Split('\n');
        var original = text.Split('\n');
        return languageId switch
        {
            "vb" => new VbParser(masked, original, path).Parse(),
            "python" => new PythonParser(masked, original, path).Parse(),
            _ => new BraceParser(languageId, masked, original, path).Parse(),
        };
    }

    /// <summary>The symbols of every supported file under a folder (skipping bin, obj, node_modules…).</summary>
    public static List<CodeSymbol> Index(string folder, int limit = 3000)
    {
        var result = new List<CodeSymbol>();
        foreach (var file in FileSearch.Files(folder).Take(limit))
        {
            var language = Languages.ForFile(file);
            if (!Supports(language.Id) || FileSearch.ReadText(file) is not { Length: < 1_000_000 } text) continue;
            result.AddRange(Symbols(text, language.Id, file));
        }
        return result;
    }

    internal static string Trimmed(string line)
    {
        var text = line.Trim();
        if (text.EndsWith('{')) text = text[..^1].TrimEnd();
        return text.Length > 200 ? text[..200] + "…" : text;
    }

    internal static int Indent(string line) => line.TakeWhile(c => c is ' ' or '\t').Sum(c => c == '\t' ? 4 : 1);

    internal static string[]? Captures(string text, string pattern, RegexOptions options = RegexOptions.None)
    {
        var match = Regex.Match(text, pattern, options);
        return match.Success ? match.Groups.Cast<Group>().Skip(1).Where(g => g.Success).Select(g => g.Value).ToArray() : null;
    }
}

internal sealed class VbParser(string[] lines, string[] original, string? path)
{
    private const string Modifiers = @"(?:(?:Public|Private|Protected|Friend|Shared|Overrides|Overridable|Overloads|MustOverride|MustInherit|NotInheritable|NotOverridable|Partial|ReadOnly|WriteOnly|Shadows|Async|Iterator|Static|Default|WithEvents|Widening|Narrowing)\s+)*";
    private const RegexOptions Ci = RegexOptions.IgnoreCase;

    private static string? Capture(string text, string pattern) => SymbolParser.Captures(text, pattern, Ci)?.LastOrDefault();

    public List<CodeSymbol> Parse()
    {
        var result = new List<CodeSymbol>();
        var namespaces = new List<string>();
        var types = new List<(string Name, SymbolKind Kind)>();
        var inMember = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var text = lines[index].Trim();
            if (text.Length == 0) continue;
            if (inMember)
            {
                if (Regex.IsMatch(text, @"^End\s+(?:Sub|Function|Property|Operator|Get|Set|Event)\b", Ci) && !Regex.IsMatch(text, @"^End\s+(?:Get|Set)\b", Ci))
                    inMember = false;
                continue;
            }
            if (Capture(text, @"^Namespace\s+([\w.]+)") is { } ns) { namespaces.Add(ns); continue; }
            if (Regex.IsMatch(text, @"^End\s+Namespace\b", Ci)) { if (namespaces.Count > 0) namespaces.RemoveAt(namespaces.Count - 1); continue; }
            if (Regex.IsMatch(text, @"^End\s+(?:Class|Module|Structure|Interface|Enum)\b", Ci)) { if (types.Count > 0) types.RemoveAt(types.Count - 1); continue; }

            var line = index;
            void Add(string name, SymbolKind kind, string? type = null, bool isShared = false) =>
                result.Add(new CodeSymbol(name, kind, types.Count > 0 ? types[^1].Name : null, namespaces.Count == 0 ? null : string.Join(".", namespaces),
                    SymbolParser.Trimmed(original[line]), type, isShared, line + 1, path));

            var isShared = Regex.IsMatch(text, @"\bShared\b", Ci) || (types.Count > 0 && types[^1].Kind == SymbolKind.Module);
            if (SymbolParser.Captures(text, "^" + Modifiers + @"(Class|Module|Structure|Interface|Enum)\s+(\w+)", Ci) is { Length: 2 } type)
            {
                var kind = type[0].ToLowerInvariant() switch
                {
                    "module" => SymbolKind.Module,
                    "structure" => SymbolKind.Structure,
                    "interface" => SymbolKind.Interface,
                    "enum" => SymbolKind.Enumeration,
                    _ => SymbolKind.Class,
                };
                Add(type[1], kind);
                types.Add((type[1], kind));
                continue;
            }
            if (types.Count > 0 && types[^1].Kind == SymbolKind.Enumeration && Capture(text, @"^(\w+)\s*(?:=.*)?$") is { } member)
            {
                Add(member, SymbolKind.EnumMember, types[^1].Name, true);
                continue;
            }
            var isAbstract = Regex.IsMatch(text, @"\bMustOverride\b|\bDeclare\b", Ci) || (types.Count > 0 && types[^1].Kind == SymbolKind.Interface);
            if (SymbolParser.Captures(text, "^" + Modifiers + @"(Sub|Function|Operator)\s+(\w+)", Ci) is { Length: 2 } method)
            {
                var returns = Capture(text, @"\)\s+As\s+([\w.]+(?:\(Of[^)]*\))?)\s*(?:Implements\b.*|Handles\b.*)?$");
                if (method[1].Equals("new", StringComparison.OrdinalIgnoreCase)) Add(types.Count > 0 ? types[^1].Name : "New", SymbolKind.Constructor);
                else Add(method[1], SymbolKind.Method, method[0].Equals("sub", StringComparison.OrdinalIgnoreCase) ? null : returns, isShared);
                if (!isAbstract) inMember = true;
                continue;
            }
            if (Capture(text, "^" + Modifiers + @"Property\s+(\w+)") is { } property)
            {
                Add(property, SymbolKind.Property, Capture(text, @"\bAs\s+(?:New\s+)?([\w.]+(?:\(Of[^)]*\))?)"), isShared);
                // Auto-implemented properties (`Property Name As String`) have no body.
                var hasBody = Regex.IsMatch(text, @"\b(?:ReadOnly|WriteOnly)\b", Ci) ||
                              (index + 1 < lines.Length && Regex.IsMatch(lines[index + 1].Trim(), @"^(?:Get|Set)\b", Ci));
                if (hasBody && !isAbstract) inMember = true;
                continue;
            }
            if (Capture(text, "^" + Modifiers + @"Event\s+(\w+)") is { } evt) { Add(evt, SymbolKind.Event, isShared: isShared); continue; }
            if (types.Count > 0 && SymbolParser.Captures(text,
                    @"^(?:(?:Public|Private|Protected|Friend|Dim|Shared|ReadOnly|Const|Shadows|WithEvents)\s+)+(\w+)(?:\(\))?\s+As\s+(?:New\s+)?([\w.]+(?:\(Of[^)]*\))?)", Ci) is { Length: 2 } field)
            {
                var isConstant = Regex.IsMatch(text, @"\bConst\b", Ci);
                Add(field[0], isConstant ? SymbolKind.Constant : SymbolKind.Field, field[1], isShared || isConstant);
            }
        }
        return result;
    }
}

internal sealed class PythonParser(string[] lines, string[] original, string? path)
{
    public List<CodeSymbol> Parse()
    {
        var result = new List<CodeSymbol>();
        var stack = new List<(int Indent, string Name, bool IsClass)>();
        for (var index = 0; index < lines.Length; index++)
        {
            var text = lines[index].Trim();
            if (text.Length == 0) continue;
            var indent = SymbolParser.Indent(lines[index]);
            while (stack.Count > 0 && stack[^1].Indent >= indent) stack.RemoveAt(stack.Count - 1);
            var container = stack.LastOrDefault(s => s.IsClass).Name;
            var inFunction = stack.Count > 0 && !stack[^1].IsClass;
            var line = index;
            void Add(string name, SymbolKind kind, string? type = null, bool isShared = false) =>
                result.Add(new CodeSymbol(name, kind, container, null, SymbolParser.Trimmed(original[line]), type, isShared, line + 1, path));

            if (Regex.Match(text, @"^class\s+(\w+)") is { Success: true } cls)
            {
                Add(cls.Groups[1].Value, SymbolKind.Class);
                stack.Add((indent, cls.Groups[1].Value, true));
            }
            else if (Regex.Match(text, @"^(?:async\s+)?def\s+(\w+)") is { Success: true } def)
            {
                var name = def.Groups[1].Value;
                var returns = Regex.Match(text, @"->\s*([^:]+):") is { Success: true } r ? r.Groups[1].Value.Trim() : null;
                if (!inFunction)
                {
                    var isStatic = index > 0 && lines[index - 1].Trim() is "@staticmethod" or "@classmethod";
                    Add(name, container == null ? SymbolKind.Function : name == "__init__" ? SymbolKind.Constructor : SymbolKind.Method, returns, isStatic);
                }
                stack.Add((indent, name, false));
            }
            else if (!inFunction && Regex.Match(text, @"^[A-Za-z_]\w*(?=\s*(?::[^=]+)?=(?!=))") is { Success: true } variable)
            {
                var name = variable.Value;
                Add(name, container == null ? (name == name.ToUpperInvariant() ? SymbolKind.Constant : SymbolKind.Variable) : SymbolKind.Field, isShared: container != null);
            }
            else if (container != null && inFunction && stack[^1].Name == "__init__" &&
                     Regex.Match(text, @"^self\.(\w+)(?=\s*(?::[^=]+)?=(?!=))") is { Success: true } self &&
                     !result.Any(s => s.Container == container && s.Name == self.Groups[1].Value))
            {
                Add(self.Groups[1].Value, SymbolKind.Field);
            }
        }
        return result;
    }
}

internal sealed class BraceParser(string languageId, string[] lines, string[] original, string? path)
{
    private string TypePattern => languageId switch
    {
        "swift" => @"^(?:(?:@\w+(?:\([^)]*\))?|public|private|fileprivate|internal|open|final|indirect)\s+)*(class|struct|enum|protocol|extension|actor)\s+([\w.]+)",
        "go" => @"^type\s+(\w+)\s+(struct|interface)\b",
        "rust" => @"^(?:pub(?:\([^)]*\))?\s+)?(struct|enum|trait|impl|mod)\s+(?:<[^>]*>\s*)?(?:[\w:]+\s+for\s+)?(\w+)",
        "kotlin" => @"^(?:(?:public|private|internal|protected|open|abstract|data|sealed|enum|inner|annotation)\s+)*(class|interface|object)\s+(\w+)",
        "javascript" or "typescript" => @"^(?:export\s+)?(?:default\s+)?(?:declare\s+)?(?:abstract\s+)?(class|interface|enum|namespace)\s+(\w+)",
        "php" => @"^(?:(?:abstract|final)\s+)?(class|interface|trait|enum)\s+(\w+)",
        "cpp" or "c" or "objc" => @"^(?:typedef\s+)?(class|struct|enum|union|namespace|@interface|@implementation|@protocol)\s+(\w+)(?!\s*;)",
        _ => @"^(?:(?:public|private|protected|internal|static|abstract|sealed|partial|readonly|ref|unsafe|file|final|new)\s+)*(class|struct|interface|enum|record(?:\s+struct|\s+class)?|namespace)\s+([\w.]+)",
    };

    private string MethodPattern => languageId switch
    {
        "swift" => @"^(?:(?:@\w+(?:\([^)]*\))?|public|private|fileprivate|internal|open|static|class|override|mutating|final|nonisolated|convenience|required)\s+)*(func\s+(\w+)|init)\b",
        "go" => @"^func\s+(?:\([^)]*\)\s*)?(\w+)",
        "rust" => @"^(?:pub(?:\([^)]*\))?\s+)?(?:const\s+|async\s+|unsafe\s+|extern\s+""C""\s+)*fn\s+(\w+)",
        "kotlin" => @"^(?:(?:public|private|internal|protected|open|override|abstract|suspend|inline|operator)\s+)*fun\s+(?:<[^>]*>\s*)?(?:[\w.]+\.)?(\w+)",
        "php" => @"^(?:(?:public|private|protected|static|abstract|final)\s+)*function\s+(\w+)",
        "javascript" or "typescript" =>
            @"^(?:export\s+)?(?:default\s+)?(?:async\s+)?(?:function\s*\*?\s*(\w+)|(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s+)?(?:function\b|\([^)]*\)\s*(?::[^=]+)?=>|\w+\s*=>)|(?:(?:public|private|protected|static|readonly|async|get|set|override)\s+)*(?!(?:if|for|while|switch|catch|return|function|new|else)\b)(\w+)\s*\([^)]*\)\s*(?::\s*[^{;]+)?\{)",
        _ => @"^(?:(?:public|private|protected|internal|static|async|override|virtual|sealed|abstract|partial|unsafe|extern|new|final|synchronized|native|default|inline|constexpr|explicit)\s+)*(?!(?:if|for|foreach|while|switch|catch|using|lock|return|new|else|throw|await|yield|case|delete|sizeof|typeof)\b)(?:[\w<>\[\],.?:*&~]+\s+)+?[*&]?(~?\w+)\s*\(.*$",
    };

    public List<CodeSymbol> Parse()
    {
        var result = new List<CodeSymbol>();
        var depth = 0;
        // Open blocks: namespaces and types (with the depth inside them), and member bodies to skip.
        var scopes = new List<(string Name, SymbolKind Kind, int Depth)>();
        (string Name, SymbolKind Kind)? pending = null;
        int? bodyDepth = null;
        var awaitingBody = 0;
        var typeRegex = new Regex(TypePattern);
        var methodRegex = new Regex(MethodPattern);

        for (var index = 0; index < lines.Length; index++)
        {
            var text = lines[index].Trim();
            var opens = text.Count(c => c == '{');
            var closes = text.Count(c => c == '}');
            try
            {
                if (awaitingBody > 0 && text.Length > 0)
                {
                    awaitingBody--;
                    if (opens > 0) { bodyDepth = depth; awaitingBody = 0; continue; }
                    if (text.EndsWith(';')) { awaitingBody = 0; continue; }
                    if (text.StartsWith(')') || text.StartsWith(':') || text.StartsWith("where") || text.StartsWith("throws") || text.EndsWith(',')) continue;
                    awaitingBody = 0;
                }
                if (bodyDepth != null || text.Length == 0) continue;
                (string Name, SymbolKind Kind, int Depth)? typeScope = scopes.LastOrDefault(s => s.Kind != SymbolKind.Namespace) is { Name: not null } t ? t : null;
                var ns = string.Join(".", scopes.Where(s => s.Kind == SymbolKind.Namespace).Select(s => s.Name));
                var atTypeLevel = depth == (scopes.Count > 0 ? scopes[^1].Depth : 0);
                var line = index;
                void Add(string name, SymbolKind kind, string? type = null)
                {
                    var isShared = Regex.IsMatch(text, @"\bstatic\b|\bconst\b|\bclass\s+func\b") || kind == SymbolKind.EnumMember;
                    result.Add(new CodeSymbol(name, kind, typeScope?.Name, ns.Length == 0 ? null : ns, SymbolParser.Trimmed(original[line]), type, isShared, line + 1, path));
                }

                if (atTypeLevel && typeRegex.Match(text) is { Success: true } typeMatch)
                {
                    var keyword = typeMatch.Groups[1].Value;
                    var name = typeMatch.Groups[2].Value;
                    if (languageId == "go") (keyword, name) = (name, keyword);
                    var kind = keyword.ToLowerInvariant() switch
                    {
                        "namespace" or "mod" => SymbolKind.Namespace,
                        "struct" or "record struct" or "union" => SymbolKind.Structure,
                        "interface" or "protocol" or "trait" or "@protocol" => SymbolKind.Interface,
                        "enum" => SymbolKind.Enumeration,
                        "object" => SymbolKind.Module,
                        _ => SymbolKind.Class,
                    };
                    if (kind == SymbolKind.Namespace && text.EndsWith(';'))
                    {
                        // C#'s file-scoped namespace.
                        scopes.Add((name, SymbolKind.Namespace, depth));
                        continue;
                    }
                    if (keyword is not ("extension" or "impl" or "@implementation") || typeScope == null) Add(name, kind);
                    if (languageId == "go") continue;
                    if (opens > closes || opens == 0) pending = (name, kind);
                    if (opens > 0 && opens > closes) { scopes.Add((name, kind, depth + opens - closes)); pending = null; }
                    continue;
                }
                if (typeScope?.Kind == SymbolKind.Enumeration && atTypeLevel && languageId is not ("swift" or "rust") &&
                    Regex.Match(text, @"^\w+(?=\s*(?:=[^,]*)?,?\s*$)") is { Success: true } enumMember)
                {
                    Add(enumMember.Value, SymbolKind.EnumMember, typeScope?.Name);
                    continue;
                }
                if (languageId == "swift" && typeScope?.Kind == SymbolKind.Enumeration && Regex.Match(text, @"^(?:indirect\s+)?case\s+(\w+)") is { Success: true } swiftCase)
                {
                    Add(swiftCase.Groups[1].Value, SymbolKind.EnumMember, typeScope?.Name);
                    continue;
                }
                if ((atTypeLevel || typeScope == null) && methodRegex.Match(text) is { Success: true } methodMatch)
                {
                    var groups = methodMatch.Groups.Cast<Group>().Skip(1).Where(g => g.Success).Select(g => g.Value).ToList();
                    var name = groups.LastOrDefault() ?? "";
                    if (languageId == "swift" && groups.FirstOrDefault() == "init") name = "init";
                    if (name.StartsWith("func ")) name = name[5..];
                    var isConstructor = name == typeScope?.Name || name is "init" or "constructor" or "__construct";
                    var kind = isConstructor ? SymbolKind.Constructor : typeScope == null ? SymbolKind.Function : SymbolKind.Method;
                    Add(name, kind, ReturnType(text, name));
                    if (opens > closes) bodyDepth = depth;
                    else if (opens == 0 && !text.EndsWith(';') && !text.Contains("=>") && typeScope?.Kind != SymbolKind.Interface && !text.Contains("abstract"))
                        awaitingBody = 3;
                    continue;
                }
                if (!atTypeLevel || typeScope == null || typeScope.Value.Kind == SymbolKind.Enumeration) continue;
                if (Member(text) is { } member)
                {
                    Add(member.Name, member.IsProperty ? SymbolKind.Property : Regex.IsMatch(text, @"\bconst\b") ? SymbolKind.Constant : SymbolKind.Field, member.Type);
                    if (opens > closes) bodyDepth = depth;
                }
            }
            finally
            {
                depth += opens - closes;
                if (bodyDepth is { } body && depth <= body) bodyDepth = null;
                while (scopes.Count > 0 && depth < scopes[^1].Depth) scopes.RemoveAt(scopes.Count - 1);
                if (pending is { } type && opens > 0)
                {
                    scopes.Add((type.Name, type.Kind, depth));
                    pending = null;
                }
            }
        }
        return result;
    }

    /// <summary>Fields and properties declared directly in a type.</summary>
    private (string Name, string? Type, bool IsProperty)? Member(string text)
    {
        string[]? First(string pattern)
        {
            var match = Regex.Match(text, pattern);
            return match.Success ? match.Groups.Cast<Group>().Skip(1).Select(g => g.Success ? g.Value : "").ToArray() : null;
        }
        switch (languageId)
        {
            case "swift" or "kotlin":
                if (First(@"^(?:(?:@\w+(?:\([^)]*\))?|public|private|fileprivate|internal|open|static|class|lazy|weak|unowned|final|override|nonisolated|const|lateinit)\s+)*(?:let|var|val)\s+(\w+)(?:\s*:\s*([^={]+))?") is not { } swift) return null;
                return (swift[0], swift[1].Length == 0 ? null : swift[1].Trim(), text.Contains('{'));
            case "javascript" or "typescript":
                if (First(@"^(?:(?:public|private|protected|static|readonly|declare|override|#)\s*)*([A-Za-z_$#][\w$]*)\s*[?!]?\s*(?::\s*([^=;]+))?\s*(?:=|;|$)") is not { } js ||
                    js[0] is "return" or "if" or "else") return null;
                return (js[0], js[1].Length == 0 ? null : js[1].Trim(), false);
            case "go" or "rust":
                if (First(@"^(?:pub(?:\([^)]*\))?\s+)?(\w+)\s*:?\s+([\w.*\[\]<>&']+)") is not { } go) return null;
                return (go[0], go[1], false);
            default:
                if (First(@"^(?:(?:public|private|protected|internal|static|readonly|virtual|override|abstract|sealed|new|required|const|volatile|final|transient|unsafe|extern)\s+)*([\w<>\[\],.?]+(?:<[^>]*>)?)\s+(\w+)\s*(?:\{|=>)") is { } property)
                    return (property[1], property[0], true);
                if (First(@"^(?:(?:public|private|protected|internal|static|readonly|const|volatile|final|transient|mutable|unsafe|extern|inline|constexpr)\s+)*([\w<>\[\],.?:*&]+(?:<[^>]*>)?)\s+[*&]?(\w+)\s*(?:=[^=].*|\[[^\]]*\])?;") is { } field &&
                    field[0] is not ("return" or "using" or "goto" or "break" or "continue" or "throw" or "delete"))
                    return (field[1], field[0], false);
                return null;
        }
    }

    private static readonly HashSet<string> Keywords =
        ["public", "private", "protected", "internal", "static", "async", "override", "virtual", "sealed", "abstract", "new", "partial", "extern", "unsafe", "final", "void"];

    private string? ReturnType(string text, string name)
    {
        switch (languageId)
        {
            case "swift" or "rust":
                return Regex.Match(text, @"->\s*([^{]+)") is { Success: true } arrow ? arrow.Groups[1].Value.Trim() : null;
            case "kotlin" or "typescript":
                return Regex.Match(text, @"\)\s*:\s*([^{=]+)") is { Success: true } colon ? colon.Groups[1].Value.Trim() : null;
            case "go" or "javascript" or "php":
                return null;
            default:
                // `public static int Add(` → "int"
                var at = text.IndexOf(name + "(", StringComparison.Ordinal);
                if (at < 0) at = text.IndexOf(name + " (", StringComparison.Ordinal);
                if (at < 0) return null;
                var before = text[..at].Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                return before == null || Keywords.Contains(before) ? null : before;
        }
    }
}

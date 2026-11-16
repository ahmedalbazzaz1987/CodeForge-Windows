namespace CodeForge.Core;

/// <summary>A code snippet: type its shortcut and press Tab, as in Visual Studio. $0 marks where the cursor goes.</summary>
/// <param name="Id">Stays the same when the shortcut changes; built-ins use "language.shortcut".</param>
public sealed record CodeSnippet(string Shortcut, string Title, string LanguageId, string Body, string? Id = null)
{
    public ToolboxItem ToolboxItem => new(Title, "\uE8C8", Body);
    public string Key => Id ?? $"{LanguageId}.{Shortcut}";
}

/// <summary>
/// The built-in snippets plus the user's own (Tools ▸ Code Snippets Manager), saved as JSON. A user snippet with a
/// built-in's key replaces it; deleted built-ins are remembered so they stay deleted.
/// </summary>
public sealed class SnippetLibrary
{
    public List<CodeSnippet> User { get; } = [];
    public HashSet<string> Hidden { get; } = [];

    public IReadOnlyList<CodeSnippet> All
    {
        get
        {
            var replaced = User.Select(s => s.Key).ToHashSet();
            return [.. Snippets.BuiltIn.Where(s => !replaced.Contains(s.Key) && !Hidden.Contains(s.Key)), .. User];
        }
    }

    public static bool IsBuiltIn(CodeSnippet snippet) => Snippets.BuiltIn.Any(s => s.Key == snippet.Key);

    /// <summary>Adds or replaces a snippet (a changed built-in is kept as a user copy with the same key).</summary>
    public void Save(CodeSnippet snippet)
    {
        var copy = snippet.Id == null ? snippet with { Id = snippet.Key } : snippet;
        var index = User.FindIndex(s => s.Key == copy.Key);
        if (index >= 0) User[index] = copy; else User.Add(copy);
    }

    public void Delete(CodeSnippet snippet)
    {
        User.RemoveAll(s => s.Key == snippet.Key);
        if (IsBuiltIn(snippet)) Hidden.Add(snippet.Key);
    }

    /// <summary>Brings back the built-in snippets of a language (deleted or changed), keeping the user's new ones.</summary>
    public void RestoreBuiltIns(string languageId)
    {
        foreach (var builtIn in Snippets.BuiltIn.Where(s => s.LanguageId == languageId))
        {
            Hidden.Remove(builtIn.Key);
            User.RemoveAll(s => s.Key == builtIn.Key);
        }
    }

    private sealed record Stored(List<CodeSnippet> Snippets, List<string> Hidden);

    public static SnippetLibrary Load(string path)
    {
        var library = new SnippetLibrary();
        try
        {
            if (File.Exists(path) && System.Text.Json.JsonSerializer.Deserialize<Stored>(File.ReadAllText(path)) is { } stored)
            {
                library.User.AddRange(stored.Snippets ?? []);
                foreach (var key in stored.Hidden ?? []) library.Hidden.Add(key);
            }
        }
        catch (Exception) { }
        return library;
    }

    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new Stored(User, [.. Hidden.Order()]),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>The built-in code snippets, the same set as the Mac version.</summary>
public static class Snippets
{
    /// <summary>The snippets in use: set by the app to the user's library.</summary>
    public static SnippetLibrary Library { get; set; } = new();

    public static IReadOnlyList<CodeSnippet> All => Library.All;

    public static IEnumerable<CodeSnippet> ForLanguage(string languageId) => All.Where(s => s.LanguageId == languageId);

    /// <summary>The snippet for a shortcut typed before Tab. VB.NET's shortcuts ignore case, like the language.</summary>
    public static CodeSnippet? Find(string languageId, string shortcut) =>
        All.FirstOrDefault(s => s.LanguageId == languageId && s.Shortcut.Equals(shortcut, languageId == "vb" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

    public static readonly IReadOnlyList<CodeSnippet> BuiltIn =
    [
        // vb
        new("Sub", "Sub procedure", "vb", "Public Sub ${1:MethodName}()\n    $0\nEnd Sub"),
        new("Function", "Function", "vb", "Public Function ${1:FunctionName}() As ${2:Integer}\n    Return ${3:0}$0\nEnd Function"),
        new("Property", "Property", "vb", "Public Property ${1:Name} As ${2:String}$0"),
        new("PropertyFull", "Property with backing field", "vb", "Private ${1:_name} As ${2:String}\n\nPublic Property ${3:Name} As $2\n    Get\n        Return $1\n    End Get\n    Set(value As $2)\n        $1 = value$0\n    End Set\nEnd Property"),
        new("Class", "Class", "vb", "Public Class ${1:ClassName}\n    $0\nEnd Class"),
        new("Module", "Module", "vb", "Module ${1:ModuleName}\n    $0\nEnd Module"),
        new("If", "If…Then…End If", "vb", "If ${1:condition} Then\n    $0\nEnd If"),
        new("IfElse", "If…Else…End If", "vb", "If ${1:condition} Then\n    $2\nElse\n    $0\nEnd If"),
        new("For", "For…Next", "vb", "For ${1:i} As Integer = ${2:0} To ${3:10}\n    $0\nNext"),
        new("ForEach", "For Each…Next", "vb", "For Each ${1:item} In ${2:collection}\n    $0\nNext"),
        new("While", "While…End While", "vb", "While ${1:condition}\n    $0\nEnd While"),
        new("DoLoop", "Do…Loop Until", "vb", "Do\n    $0\nLoop Until condition"),
        new("Select", "Select Case", "vb", "Select Case ${1:value}\n    Case ${2:1}\n        $0\n    Case Else\n        \nEnd Select"),
        new("Try", "Try…Catch…End Try", "vb", "Try\n    $0\nCatch ex As Exception\n    Console.WriteLine(ex.Message)\nEnd Try"),
        new("TryF", "Try…Catch…Finally", "vb", "Try\n    $0\nCatch ex As Exception\n    \nFinally\n    \nEnd Try"),
        new("Using", "Using block", "vb", "Using ${1:resource} = ${2:New Object()}\n    $0\nEnd Using"),
        new("cw", "Console.WriteLine", "vb", "Console.WriteLine($0)"),
        new("Dim", "Dim variable", "vb", "Dim ${1:name} As ${2:String} = ${3:\"\"}$0"),
        new("Ctor", "Constructor (Sub New)", "vb", "Public Sub New($1)\n    $0\nEnd Sub"),
        new("Enum", "Enum", "vb", "Public Enum ${1:Name}\n    ${2:First}\n    ${3:Second}\nEnd Enum$0"),
        new("Interface", "Interface", "vb", "Public Interface I${1:Name}\n    $0\nEnd Interface"),
        new("Main", "Sub Main", "vb", "Sub Main(args As String())\n    $0\nEnd Sub"),
        // csharp
        new("prop", "Auto-implemented property", "csharp", "public ${1:int} ${2:MyProperty} { get; set; }$0"),
        new("propfull", "Property with backing field", "csharp", "private ${1:int} ${2:_myVar};\n\npublic $1 ${3:MyProperty}\n{\n    get { return $2; }\n    set { $2 = value; }\n}$0"),
        new("ctor", "Constructor", "csharp", "public ${1:ClassName}()\n{\n    $0\n}"),
        new("class", "Class", "csharp", "public class ${1:MyClass}\n{\n    $0\n}"),
        new("interface", "Interface", "csharp", "public interface I${1:Name}\n{\n    $0\n}"),
        new("enum", "Enum", "csharp", "public enum ${1:MyEnum}\n{\n    ${2:First},\n    ${3:Second}\n}$0"),
        new("record", "Record", "csharp", "public record ${1:Person}(${2:string Name, int Age});$0"),
        new("for", "for loop", "csharp", "for (int ${1:i} = 0; $1 < ${2:length}; $1++)\n{\n    $0\n}"),
        new("forr", "Reverse for loop", "csharp", "for (int ${1:i} = ${2:length} - 1; $1 >= 0; $1--)\n{\n    $0\n}"),
        new("foreach", "foreach loop", "csharp", "foreach (${1:var} ${2:item} in ${3:collection})\n{\n    $0\n}"),
        new("while", "while loop", "csharp", "while (${1:condition})\n{\n    $0\n}"),
        new("do", "do…while loop", "csharp", "do\n{\n    $0\n} while (${1:condition});"),
        new("if", "if statement", "csharp", "if (${1:condition})\n{\n    $0\n}"),
        new("else", "else block", "csharp", "else\n{\n    $0\n}"),
        new("switch", "switch statement", "csharp", "switch (${1:value})\n{\n    case ${2:1}:\n        $0\n        break;\n    default:\n        break;\n}"),
        new("try", "try…catch", "csharp", "try\n{\n    $0\n}\ncatch (Exception ex)\n{\n    Console.WriteLine(ex.Message);\n}"),
        new("tryf", "try…finally", "csharp", "try\n{\n    $0\n}\nfinally\n{\n    \n}"),
        new("using", "using statement", "csharp", "using (var ${1:resource} = ${2:new Resource()})\n{\n    $0\n}"),
        new("cw", "Console.WriteLine", "csharp", "Console.WriteLine($0);"),
        new("svm", "static void Main", "csharp", "static void Main(string[] args)\n{\n    $0\n}"),
        new("async", "Async method", "csharp", "public async Task ${1:MethodAsync}()\n{\n    await Task.Delay(1);$0\n}"),
        new("test", "xUnit test", "csharp", "[Fact]\npublic void ${1:Test}()\n{\n    Assert.True(true);$0\n}"),
        // fsharp
        new("let", "Function", "fsharp", "let ${1:name} ${2:x} =\n    ${3:x}$0"),
        new("rec", "Recursive function", "fsharp", "let rec ${1:name} n =\n    if n <= 0 then 0 else n + $1 (n - 1)$0"),
        new("type", "Record type", "fsharp", "type ${1:Name} = { ${2:Field}: ${3:string} }$0"),
        new("union", "Discriminated union", "fsharp", "type ${1:Shape} =\n    | ${2:Circle} of float\n    | ${3:Square} of float$0"),
        new("match", "match expression", "fsharp", "match ${1:value} with\n| Some x -> x\n| None -> 0$0"),
        new("for", "for…in loop", "fsharp", "for ${1:item} in ${2:items} do\n    printfn \"%A\" $1$0"),
        new("pf", "printfn", "fsharp", "printfn \"%A\" $0"),
        new("async", "async block", "fsharp", "async {\n    $0\n    return ()\n}"),
        new("try", "try…with", "fsharp", "try\n    $0\nwith ex ->\n    printfn \"%s\" ex.Message"),
        // python
        new("def", "Function", "python", "def ${1:name}($2):\n    ${3:pass}$0"),
        new("class", "Class", "python", "class ${1:Name}:\n    def __init__(self$2):\n        ${3:pass}$0"),
        new("main", "Main guard", "python", "def main():\n    $0\n\n\nif __name__ == \"__main__\":\n    main()"),
        new("for", "for loop", "python", "for ${1:item} in ${2:items}:\n    ${3:pass}$0"),
        new("if", "if statement", "python", "if ${1:condition}:\n    ${2:pass}$0"),
        new("try", "try…except", "python", "try:\n    $0\nexcept Exception as error:\n    print(error)"),
        new("with", "with open", "python", "with open(${1:\"file.txt\"}) as ${2:file}:\n    ${3:text} = $2.read()$0"),
        // javascript
        new("fn", "Function", "javascript", "function ${1:name}($2) {\n    $0\n}"),
        new("afn", "Arrow function", "javascript", "const ${1:name} = ($2) => {\n    $0\n};"),
        new("for", "for loop", "javascript", "for (let ${1:i} = 0; $1 < ${2:array}.length; $1++) {\n    $0\n}"),
        new("forof", "for…of loop", "javascript", "for (const ${1:item} of ${2:items}) {\n    $0\n}"),
        new("if", "if statement", "javascript", "if (${1:condition}) {\n    $0\n}"),
        new("try", "try…catch", "javascript", "try {\n    $0\n} catch (error) {\n    console.error(error);\n}"),
        new("log", "console.log", "javascript", "console.log($0);"),
        new("class", "Class", "javascript", "class ${1:Name} {\n    constructor($2) {\n        $0\n    }\n}"),
        new("fetch", "fetch JSON", "javascript", "const response = await fetch(${1:url});\nconst ${2:data} = await response.json();$0"),
        // swift
        new("func", "Function", "swift", "func ${1:name}($2) {\n    $0\n}"),
        new("struct", "Struct", "swift", "struct ${1:Name} {\n    $0\n}"),
        new("class", "Class", "swift", "final class ${1:Name} {\n    $0\n}"),
        new("guard", "guard let", "swift", "guard let ${1:value} else { ${2:return} }$0"),
        new("iflet", "if let", "swift", "if let ${1:value} {\n    $0\n}"),
        new("for", "for…in loop", "swift", "for ${1:item} in ${2:items} {\n    $0\n}"),
        new("switch", "switch", "swift", "switch ${1:value} {\ncase ${2:.first}:\n    ${3:break}\ndefault:\n    break\n}$0"),
        new("do", "do…catch", "swift", "do {\n    try $0\n} catch {\n    print(error)\n}"),
        new("view", "SwiftUI view", "swift", "struct ${1:ContentView}: View {\n    var body: some View {\n        ${2:Text(\"Hello\")}\n    }\n}$0"),
        // c
        new("main", "main", "c", "#include <stdio.h>\n\nint main(void) {\n    $0\n    return 0;\n}"),
        new("for", "for loop", "c", "for (int ${1:i} = 0; $1 < ${2:count}; $1++) {\n    $0\n}"),
        new("if", "if statement", "c", "if (${1:condition}) {\n    $0\n}"),
        new("while", "while loop", "c", "while (${1:condition}) {\n    $0\n}"),
        new("struct", "typedef struct", "c", "typedef struct {\n    ${1:int} ${2:value};\n} ${3:Name};$0"),
        new("pf", "printf", "c", "printf(\"$1\\n\");$0"),
        // cpp
        new("main", "main", "cpp", "#include <iostream>\n\nint main() {\n    $0\n    return 0;\n}"),
        new("class", "Class", "cpp", "class ${1:Name} {\npublic:\n    $1() = default;\n\nprivate:\n    int ${2:value_} = 0;\n};$0"),
        new("for", "Range for", "cpp", "for (const auto& ${1:item} : ${2:items}) {\n    $0\n}"),
        new("if", "if statement", "cpp", "if (${1:condition}) {\n    $0\n}"),
        new("cout", "std::cout", "cpp", "std::cout << $1 << std::endl;$0"),
        new("try", "try…catch", "cpp", "try {\n    $0\n} catch (const std::exception& error) {\n    std::cerr << error.what() << std::endl;\n}"),
        // java
        new("main", "main method", "java", "public static void main(String[] args) {\n    $0\n}"),
        new("sout", "System.out.println", "java", "System.out.println($0);"),
        new("class", "Class", "java", "public class ${1:Name} {\n    $0\n}"),
        new("for", "for each", "java", "for (var ${1:item} : ${2:items}) {\n    $0\n}"),
        new("if", "if statement", "java", "if (${1:condition}) {\n    $0\n}"),
        new("try", "try…catch", "java", "try {\n    $0\n} catch (Exception error) {\n    error.printStackTrace();\n}"),
        // typescript
        new("fn", "Typed function", "typescript", "function ${1:name}(${2:value}: ${3:string}): ${4:string} {\n    ${5:return value;}\n}$0"),
        new("afn", "Arrow function", "typescript", "const ${1:name} = (${2:value}: ${3:string}): ${4:string} => {\n    ${5:return value;}\n};$0"),
        new("int", "Interface", "typescript", "interface ${1:Name} {\n    ${2:id}: ${3:number};\n}$0"),
        new("type", "Type", "typescript", "type ${1:Name} = {\n    ${2:id}: ${3:number};\n};$0"),
        new("class", "Class", "typescript", "class ${1:Name} {\n    constructor(private readonly ${2:id}: ${3:number}) {}\n}$0"),
        new("log", "console.log", "typescript", "console.log($0);"),
        // go
        new("main", "main", "go", "func main() {\n\t$0\n}"),
        new("func", "Function", "go", "func ${1:name}($2) ${3:error} {\n\t${4:return nil}\n}$0"),
        new("iferr", "if err != nil", "go", "if err != nil {\n\treturn ${1:err}\n}$0"),
        new("for", "for range", "go", "for _, ${1:item} := range ${2:items} {\n\t$0\n}"),
        new("pl", "fmt.Println", "go", "fmt.Println($0)"),
        // rust
        new("main", "main", "rust", "fn main() {\n    $0\n}"),
        new("fn", "Function", "rust", "fn ${1:name}($2) -> ${3:i32} {\n    ${4:0}\n}$0"),
        new("struct", "Struct", "rust", "struct ${1:Name} {\n    ${2:id}: ${3:u32},\n}$0"),
        new("match", "match", "rust", "match ${1:value} {\n    Some(${2:item}) => {}\n    None => {}\n}$0"),
        new("pl", "println!", "rust", "println!(\"{}\", $1);$0"),
        // html
        new("html5", "HTML5 page", "html", "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n    <meta charset=\"utf-8\">\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n    <title>${1:Page}</title>\n</head>\n<body>\n    $0\n</body>\n</html>"),
        new("div", "div", "html", "<div>$0</div>"),
        new("a", "Link", "html", "<a href=\"${1:#}\">${2:text}</a>$0"),
        new("img", "Image", "html", "<img src=\"${1:image.png}\" alt=\"${2:description}\">$0"),
        new("ul", "List", "html", "<ul>\n    <li>$0</li>\n</ul>"),
        new("script", "Script", "html", "<script>\n    $0\n</script>"),
    ];
}

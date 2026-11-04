namespace CodeForge.Core;

/// <summary>A code snippet: type its shortcut and press Tab, as in Visual Studio. $0 marks where the cursor goes.</summary>
public sealed record CodeSnippet(string Shortcut, string Title, string LanguageId, string Body)
{
    public ToolboxItem ToolboxItem => new(Title, "\uE8C8", Body);
}

/// <summary>The built-in code snippets, the same set as the Mac version.</summary>
public static class Snippets
{
    public static IEnumerable<CodeSnippet> ForLanguage(string languageId) => All.Where(s => s.LanguageId == languageId);

    /// <summary>The snippet for a shortcut typed before Tab. VB.NET's shortcuts ignore case, like the language.</summary>
    public static CodeSnippet? Find(string languageId, string shortcut) =>
        All.FirstOrDefault(s => s.LanguageId == languageId && s.Shortcut.Equals(shortcut, languageId == "vb" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

    public static readonly IReadOnlyList<CodeSnippet> All =
    [
        // vb
        new("Sub", "Sub procedure", "vb", "Public Sub $0MethodName()\n    \nEnd Sub"),
        new("Function", "Function", "vb", "Public Function $0FunctionName() As Integer\n    Return 0\nEnd Function"),
        new("Property", "Property", "vb", "Public Property $0Name As String"),
        new("PropertyFull", "Property with backing field", "vb", "Private _name As String\n\nPublic Property Name As String\n    Get\n        Return _name\n    End Get\n    Set(value As String)\n        _name = value$0\n    End Set\nEnd Property"),
        new("Class", "Class", "vb", "Public Class $0ClassName\n    \nEnd Class"),
        new("Module", "Module", "vb", "Module $0ModuleName\n    \nEnd Module"),
        new("If", "If…Then…End If", "vb", "If $0condition Then\n    \nEnd If"),
        new("IfElse", "If…Else…End If", "vb", "If $0condition Then\n    \nElse\n    \nEnd If"),
        new("For", "For…Next", "vb", "For i As Integer = 0 To $010\n    \nNext"),
        new("ForEach", "For Each…Next", "vb", "For Each item In $0collection\n    \nNext"),
        new("While", "While…End While", "vb", "While $0condition\n    \nEnd While"),
        new("DoLoop", "Do…Loop Until", "vb", "Do\n    $0\nLoop Until condition"),
        new("Select", "Select Case", "vb", "Select Case $0value\n    Case 1\n        \n    Case Else\n        \nEnd Select"),
        new("Try", "Try…Catch…End Try", "vb", "Try\n    $0\nCatch ex As Exception\n    Console.WriteLine(ex.Message)\nEnd Try"),
        new("TryF", "Try…Catch…Finally", "vb", "Try\n    $0\nCatch ex As Exception\n    \nFinally\n    \nEnd Try"),
        new("Using", "Using block", "vb", "Using $0resource = New Object()\n    \nEnd Using"),
        new("cw", "Console.WriteLine", "vb", "Console.WriteLine($0)"),
        new("Dim", "Dim variable", "vb", "Dim $0name As String = \"\""),
        new("Ctor", "Constructor (Sub New)", "vb", "Public Sub New($0)\n    \nEnd Sub"),
        new("Enum", "Enum", "vb", "Public Enum $0Name\n    First\n    Second\nEnd Enum"),
        new("Interface", "Interface", "vb", "Public Interface I$0Name\n    \nEnd Interface"),
        new("Main", "Sub Main", "vb", "Sub Main(args As String())\n    $0\nEnd Sub"),
        // csharp
        new("prop", "Auto-implemented property", "csharp", "public $0int MyProperty { get; set; }"),
        new("propfull", "Property with backing field", "csharp", "private int _myVar;\n\npublic int MyProperty\n{\n    get { return _myVar; }\n    set { _myVar = value;$0 }\n}"),
        new("ctor", "Constructor", "csharp", "public $0ClassName()\n{\n    \n}"),
        new("class", "Class", "csharp", "public class $0MyClass\n{\n    \n}"),
        new("interface", "Interface", "csharp", "public interface I$0Name\n{\n    \n}"),
        new("enum", "Enum", "csharp", "public enum $0MyEnum\n{\n    First,\n    Second\n}"),
        new("record", "Record", "csharp", "public record $0Person(string Name, int Age);"),
        new("for", "for loop", "csharp", "for (int i = 0; i < $0length; i++)\n{\n    \n}"),
        new("forr", "Reverse for loop", "csharp", "for (int i = $0length - 1; i >= 0; i--)\n{\n    \n}"),
        new("foreach", "foreach loop", "csharp", "foreach (var item in $0collection)\n{\n    \n}"),
        new("while", "while loop", "csharp", "while ($0condition)\n{\n    \n}"),
        new("do", "do…while loop", "csharp", "do\n{\n    $0\n} while (condition);"),
        new("if", "if statement", "csharp", "if ($0condition)\n{\n    \n}"),
        new("else", "else block", "csharp", "else\n{\n    $0\n}"),
        new("switch", "switch statement", "csharp", "switch ($0value)\n{\n    case 1:\n        break;\n    default:\n        break;\n}"),
        new("try", "try…catch", "csharp", "try\n{\n    $0\n}\ncatch (Exception ex)\n{\n    Console.WriteLine(ex.Message);\n}"),
        new("tryf", "try…finally", "csharp", "try\n{\n    $0\n}\nfinally\n{\n    \n}"),
        new("using", "using statement", "csharp", "using (var $0resource = new Resource())\n{\n    \n}"),
        new("cw", "Console.WriteLine", "csharp", "Console.WriteLine($0);"),
        new("svm", "static void Main", "csharp", "static void Main(string[] args)\n{\n    $0\n}"),
        new("async", "Async method", "csharp", "public async Task $0MethodAsync()\n{\n    await Task.Delay(1);\n}"),
        new("test", "xUnit test", "csharp", "[Fact]\npublic void $0Test()\n{\n    Assert.True(true);\n}"),
        // python
        new("def", "Function", "python", "def $0name():\n    pass"),
        new("class", "Class", "python", "class $0Name:\n    def __init__(self):\n        pass"),
        new("main", "Main guard", "python", "def main():\n    $0\n\n\nif __name__ == \"__main__\":\n    main()"),
        new("for", "for loop", "python", "for item in $0items:\n    pass"),
        new("if", "if statement", "python", "if $0condition:\n    pass"),
        new("try", "try…except", "python", "try:\n    $0\nexcept Exception as error:\n    print(error)"),
        new("with", "with open", "python", "with open($0\"file.txt\") as file:\n    text = file.read()"),
        // javascript
        new("fn", "Function", "javascript", "function $0name() {\n    \n}"),
        new("afn", "Arrow function", "javascript", "const $0name = () => {\n    \n};"),
        new("for", "for loop", "javascript", "for (let i = 0; i < $0array.length; i++) {\n    \n}"),
        new("forof", "for…of loop", "javascript", "for (const item of $0items) {\n    \n}"),
        new("if", "if statement", "javascript", "if ($0condition) {\n    \n}"),
        new("try", "try…catch", "javascript", "try {\n    $0\n} catch (error) {\n    console.error(error);\n}"),
        new("log", "console.log", "javascript", "console.log($0);"),
        new("class", "Class", "javascript", "class $0Name {\n    constructor() {\n        \n    }\n}"),
        new("fetch", "fetch JSON", "javascript", "const response = await fetch($0url);\nconst data = await response.json();"),
        // swift
        new("func", "Function", "swift", "func $0name() {\n    \n}"),
        new("struct", "Struct", "swift", "struct $0Name {\n    \n}"),
        new("class", "Class", "swift", "final class $0Name {\n    \n}"),
        new("guard", "guard let", "swift", "guard let $0value else { return }"),
        new("iflet", "if let", "swift", "if let $0value {\n    \n}"),
        new("for", "for…in loop", "swift", "for item in $0items {\n    \n}"),
        new("switch", "switch", "swift", "switch $0value {\ncase .first:\n    break\ndefault:\n    break\n}"),
        new("do", "do…catch", "swift", "do {\n    try $0\n} catch {\n    print(error)\n}"),
        new("view", "SwiftUI view", "swift", "struct $0ContentView: View {\n    var body: some View {\n        Text(\"Hello\")\n    }\n}"),
        // html
        new("html5", "HTML5 page", "html", "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n    <meta charset=\"utf-8\">\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n    <title>$0Page</title>\n</head>\n<body>\n    \n</body>\n</html>"),
        new("div", "div", "html", "<div>$0</div>"),
        new("a", "Link", "html", "<a href=\"$0\"></a>"),
        new("img", "Image", "html", "<img src=\"$0\" alt=\"\">"),
        new("ul", "List", "html", "<ul>\n    <li>$0</li>\n</ul>"),
        new("script", "Script", "html", "<script>\n    $0\n</script>"),
    ];
}

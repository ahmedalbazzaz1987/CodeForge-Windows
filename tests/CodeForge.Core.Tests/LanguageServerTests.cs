using System.Text.Json.Nodes;
using CodeForge.Core;

namespace CodeForge.Core.Tests;

public class LanguageServerTests
{
    [Fact]
    public void PositionsCountUtf16UnitsPerLine()
    {
        var lines = new TextLines("ab\r\nمرحبا x\n\nend");
        Assert.Equal((0, 2), lines.Position(2));
        Assert.Equal((1, 6), lines.Position(10));
        Assert.Equal(10, lines.Offset(1, 6));
        Assert.Equal((3, 0), lines.Position(13));
        // Past the end of a line stops at its end; past the last line, at the end of the text.
        Assert.Equal(11, lines.Offset(1, 99));
        Assert.Equal(16, lines.Offset(9, 0));
    }

    [Fact]
    public void WindowsPathsRoundTripThroughFileUris()
    {
        var path = @"C:\Users\أحمد\My Projects\app.cs";
        var uri = LspUri.FromPath(path);
        Assert.StartsWith("file:///C:/Users/", uri);
        Assert.Contains("My%20Projects/app.cs", uri);
        Assert.Equal(path, LspUri.ToPath(uri));
        // Servers built on vscode-uri send a lowercase drive and an escaped colon.
        Assert.Equal(@"C:\work\main.py", LspUri.ToPath("file:///c%3A/work/main.py"));
        Assert.Null(LspUri.ToPath("untitled:Untitled-1"));
    }

    [Fact]
    public void CompletionItemsKeepTheirReplaceRangeAndLoseSnippetFields()
    {
        var result = JsonNode.Parse("""
            { "isIncomplete": false, "items": [
              { "label": "WriteLine", "kind": 2, "detail": "void Console.WriteLine()",
                "textEdit": { "range": { "start": { "line": 1, "character": 8 }, "end": { "line": 1, "character": 10 } }, "newText": "WriteLine" } },
              { "label": "print", "kind": 3, "insertText": "print(${1:value})$0", "insertTextFormat": 2 },
              { "kind": 14 }
            ] }
            """);
        var items = LspResults.Completions(result, new TextLines("x\nConsole.Wr"));
        Assert.Equal(2, items.Count);
        Assert.Equal(10, items[0].ReplaceStart);
        Assert.Equal("void Console.WriteLine()", items[0].Detail);
        Assert.Equal("print(value)", items[1].Text);
        // A bare array is an answer too.
        Assert.Single(LspResults.Completions(JsonNode.Parse("""[{ "label": "x" }]"""), new TextLines("")));
    }

    [Fact]
    public void HoverMarkdownBecomesPlainText()
    {
        var hover = JsonNode.Parse("""{ "contents": { "kind": "markdown", "value": "```csharp\nvoid Greet(string name)\n```\n---\nSays **hello** to *name*. See [docs](https://x)" } }""");
        Assert.Equal("void Greet(string name)\n---\nSays hello to name. See docs", LspResults.Hover(hover));
        Assert.Equal("a\n\nb", LspResults.Hover(JsonNode.Parse("""{ "contents": ["a", { "language": "python", "value": "b" }] }""")));
        Assert.Null(LspResults.Hover(JsonNode.Parse("""{ "contents": "" }""")));
        Assert.Null(LspResults.Hover(null));
    }

    [Fact]
    public void DefinitionsAcceptLocationsAndLinks()
    {
        var one = LspResults.Locations(JsonNode.Parse("""{ "uri": "file:///C:/p/a.cs", "range": { "start": { "line": 4, "character": 2 } } }"""));
        Assert.Equal(new CodeLocation(@"C:\p\a.cs", 5, 3), one.Single());
        var links = LspResults.Locations(JsonNode.Parse("""[{ "targetUri": "file:///C:/p/b.cs", "targetSelectionRange": { "start": { "line": 0, "character": 0 } } }]"""));
        Assert.Equal(new CodeLocation(@"C:\p\b.cs", 1, 1), links.Single());
        Assert.Empty(LspResults.Locations(null));
    }

    [Fact]
    public void DiagnosticsKeepErrorsAndWarningsWithTheirRange()
    {
        var items = JsonNode.Parse("""
            [ { "severity": 1, "code": "CS0103", "message": "The name 'x' does not exist\nin the current context",
                "range": { "start": { "line": 2, "character": 4 }, "end": { "line": 2, "character": 5 } } },
              { "severity": 2, "message": "unused", "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 3 } } },
              { "severity": 4, "message": "hint", "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } } } ]
            """) as JsonArray;
        var diagnostics = LspResults.Diagnostics(items, @"C:\p\a.cs");
        Assert.Equal(2, diagnostics.Count);
        Assert.Equal(new Diagnostic(Severity.Error, "The name 'x' does not exist", @"C:\p\a.cs", 3, 5, "CS0103", 3, 6), diagnostics[0]);
        Assert.Equal(Severity.Warning, diagnostics[1].Severity);
    }

    [Fact]
    public void FindsServersInCodeForgesFolderAndOnPath()
    {
        var root = Directory.CreateTempSubdirectory("cf-lsp").FullName;
        try
        {
            var install = Path.Combine(root, "servers");
            var bin = Path.Combine(root, "bin");
            Directory.CreateDirectory(bin);
            Directory.CreateDirectory(Path.Combine(install, "dotnet-tools"));
            // On Windows a server must be an .exe, .cmd or .bat.
            var exe = OperatingSystem.IsWindows() ? ".exe" : "";
            File.WriteAllText(Path.Combine(install, "dotnet-tools", "csharp-ls" + exe), "");
            File.WriteAllText(Path.Combine(bin, "gopls" + exe), "");
            File.WriteAllText(Path.Combine(bin, "tsc" + exe), "");
            var locator = new LanguageServerLocator(install, path: bin, home: root, localAppData: root);
            Assert.Equal("csharp-ls", locator.Find("csharp")?.Spec.Id);
            Assert.Equal(Path.Combine(bin, "gopls" + exe), locator.Find("go")?.Launch.Executable);
            // TypeScript's server is only taken from CodeForge's own copy: an older tsc on PATH has none.
            Assert.Null(locator.Find("typescript"));
            Assert.Null(locator.Find("vb"));
            Assert.Contains("--tool-path", locator.InstallCommand(LanguageServerSpecs.All.First(s => s.Id == "csharp-ls")));
            Assert.Contains("npm install", locator.InstallCommand(LanguageServerSpecs.All.First(s => s.Id == "pyright")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void JediRunsFromCodeForgesPipFolder()
    {
        var root = Directory.CreateTempSubdirectory("cf-jedi").FullName;
        try
        {
            var bin = Path.Combine(root, "bin");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, OperatingSystem.IsWindows() ? "python.exe" : "python"), "");
            var locator = new LanguageServerLocator(Path.Combine(root, "servers"), path: bin, home: root, localAppData: root);
            var jedi = LanguageServerSpecs.All.First(s => s.Id == "jedi");
            Assert.Null(locator.Find("python"));
            // No npm here, so Python gets Jedi.
            Assert.Equal("jedi", locator.Installable("python")?.Id);
            Directory.CreateDirectory(Path.Combine(locator.PipFolder(jedi), "jedi_language_server"));
            var launch = locator.Find("python")!.Value.Launch;
            Assert.Equal("-c", launch.Arguments[0]);
            Assert.Equal(locator.PipFolder(jedi), launch.Environment["PYTHONPATH"]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ServerRequestsAreAnsweredAndDiagnosticsReported()
    {
        var client = new LanguageClient(action => action());
        string? path = null;
        List<Diagnostic>? reported = null;
        client.DiagnosticsPublished += (p, d) => { path = p; reported = d; };
        client.Receive("""{ "jsonrpc": "2.0", "method": "textDocument/publishDiagnostics", "params": { "uri": "file:///C:/p/main.py", "diagnostics": [ { "severity": 1, "message": "bad", "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } } } ] } }""");
        Assert.Equal(@"C:\p\main.py", path);
        Assert.Single(reported!);
        // Not started: answering a request from the server must not throw.
        client.Receive("""{ "jsonrpc": "2.0", "id": 7, "method": "workspace/configuration", "params": { "items": [{}, {}] } }""");
    }

    [Fact]
    public void BasicCompletionOffersKeywordsSnippetsAndWords()
    {
        var vb = Languages.ById("vb");
        var text = "Module Program\n    Sub Main()\n        Dim total = 0\n        to\n";
        var (items, start) = BasicIntelliSense.Completions(text, text.Length - 1, vb);
        Assert.Equal(text.Length - 3, start);
        Assert.Contains(items, i => i.Label == "total" && i.Kind == 1);
        Assert.Contains(items, i => i.Label == "AndAlso" && i.Kind == 14);
        Assert.Contains(items, i => i.Label == "ForEach" && i.Snippet != null);
        // VB ignores case: "Module" the keyword and "Module" the word in the file are one entry.
        Assert.Single(items, i => i.Label.Equals("module", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(BasicIntelliSense.Completions("x = 1\npr", 8, Languages.ById("python")).Items, i => i.Label == "print");
    }

    [Fact]
    public void NoCompletionInStringsOrComments()
    {
        var csharp = Languages.ById("csharp");
        Assert.True(BasicIntelliSense.InStringOrComment("var s = \"hel", 12, csharp));
        Assert.True(BasicIntelliSense.InStringOrComment("x(); // not", 11, csharp));
        Assert.True(BasicIntelliSense.InStringOrComment("/* open\n still", 14, csharp));
        Assert.False(BasicIntelliSense.InStringOrComment("/* done */ var", 14, csharp));
        Assert.False(BasicIntelliSense.InStringOrComment("var s = \"a\\\"b\"; x", 17, csharp));
        // VB's apostrophe starts a comment.
        Assert.True(BasicIntelliSense.InStringOrComment("Dim x ' note", 12, Languages.ById("vb")));
        Assert.Empty(BasicIntelliSense.Completions("Console.", 8, csharp).Items);
        Assert.Empty(BasicIntelliSense.Completions("{ \"a", 4, Languages.ById("json")).Items);
    }

    [Fact]
    public void FindsDeclarationsWithoutAServer()
    {
        var vb = "Module Program\n    ' Function Total in a comment\n    Function Total(x As Integer) As Integer\n        Dim count = 0\n    End Function\nEnd Module";
        Assert.Equal(vb.IndexOf("Total(x"), BasicIntelliSense.Declaration(vb, "total", Languages.ById("vb")));
        Assert.Equal(vb.IndexOf("count"), BasicIntelliSense.Declaration(vb, "count", Languages.ById("vb")));
        var csharp = "class A {\n    int Run() { return Helper(1); }\n    static int Helper(int n) => n;\n    string name = \"x\";\n}";
        Assert.Equal(csharp.IndexOf("Helper(int"), BasicIntelliSense.Declaration(csharp, "Helper", Languages.ById("csharp")));
        Assert.Equal(csharp.IndexOf("name ="), BasicIntelliSense.Declaration(csharp, "name", Languages.ById("csharp")));
        Assert.Null(BasicIntelliSense.Declaration(csharp, "Missing", Languages.ById("csharp")));
        var python = "def greet(name):\n    return name\n\nclass Greeter:\n    pass\n";
        Assert.Equal(python.IndexOf("Greeter"), BasicIntelliSense.Declaration(python, "Greeter", Languages.ById("python")));
    }
}

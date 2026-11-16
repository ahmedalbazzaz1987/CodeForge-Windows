using System.Text.Json.Nodes;
using CodeForge.Core;
using Xunit;

namespace CodeForge.Core.Tests;

public class ParameterInfoTests
{
    private static readonly Language CSharp = Languages.ById("csharp");
    private static readonly Language Python = Languages.ById("python");
    private static readonly Language VB = Languages.ById("vb");

    [Fact]
    public void FindsTheCallAndArgument()
    {
        var text = "Console.WriteLine(a, ";
        var call = ParameterInfo.CallAt(text, text.Length, CSharp)!;
        Assert.Equal("WriteLine", call.Name);
        Assert.Equal(["Console"], call.Qualifier);
        Assert.Equal(1, call.Argument);
        Assert.Equal(17, call.OpenParen);
    }

    [Fact]
    public void InnermostCallWins()
    {
        var text = "Math.Max(Math.Abs(x), ";
        Assert.Equal("Max", ParameterInfo.CallAt(text, text.Length, CSharp)!.Name);
        var inner = "Math.Max(Math.Abs(";
        Assert.Equal("Abs", ParameterInfo.CallAt(inner, inner.Length, CSharp)!.Name);
    }

    [Fact]
    public void IgnoresStringsCommentsAndClosedCalls()
    {
        Assert.Null(ParameterInfo.CallAt("Print(\"a, (b", 12, CSharp));
        var commas = "Print(\"a, b\", ";
        Assert.Equal(1, ParameterInfo.CallAt(commas, commas.Length, CSharp)!.Argument);
        Assert.Null(ParameterInfo.CallAt("Print(a) // (", 13, CSharp));
        Assert.Null(ParameterInfo.CallAt("Print(a); x = 1", 15, CSharp));
        Assert.Null(ParameterInfo.CallAt("if (x > ", 8, CSharp));
        Assert.Null(ParameterInfo.CallAt("def greet(name", 14, Python));
        // VB: ' starts a comment and "" is a quote inside a string.
        var vb = "MsgBox(\"say \"\"hi\"\", ok\", ";
        Assert.Equal(1, ParameterInfo.CallAt(vb, vb.Length, VB)!.Argument);
    }

    [Fact]
    public void KnowsConstructors()
    {
        var text = "var r = new Random(";
        Assert.True(ParameterInfo.CallAt(text, text.Length, CSharp)!.IsConstructor);
    }

    [Fact]
    public void SplitsParameters()
    {
        var label = "Substring(startIndex As Integer, length As Integer) As String";
        var ranges = ParameterInfo.ParameterRanges(label);
        Assert.Equal(["startIndex As Integer", "length As Integer"], ranges.Select(r => label.Substring(r.Start, r.Length)));
        var generic = "void Add(Dictionary<string, int> map, int count)";
        Assert.Equal(["Dictionary<string, int> map", "int count"], ParameterInfo.ParameterRanges(generic).Select(r => generic.Substring(r.Start, r.Length)));
        Assert.Empty(ParameterInfo.ParameterRanges("Now()"));
        var vbGeneric = "Function Map(Of T)(items As T(), f As Func(Of T, T)) As T()";
        Assert.Equal(["items As T()", "f As Func(Of T, T)"], ParameterInfo.ParameterRanges(vbGeneric).Select(r => vbGeneric.Substring(r.Start, r.Length)));
    }

    [Fact]
    public void BasicSignaturesComeFromTheCatalogProjectAndBuiltIns()
    {
        var vb = "Dim r As New Random()\nr.Next(";
        var call = ParameterInfo.CallAt(vb, vb.Length, VB)!;
        var signatures = ParameterInfo.Basic(vb, call, VB, []);
        Assert.Equal(3, signatures.Count);
        Assert.Contains(signatures, s => s.Label == "Next(minValue As Integer, maxValue As Integer) As Integer");

        var code = "Module M\n    Function Add(a As Integer, b As Integer) As Integer\n        Return a + b\n    End Function\nEnd Module\nAdd(1, ";
        var symbols = SymbolParser.Symbols(code, "vb", "m.vb");
        var add = ParameterInfo.CallAt(code, code.Length, VB)!;
        var found = Assert.Single(ParameterInfo.Basic(code, add, VB, symbols));
        Assert.Equal(1, ParameterInfo.ActiveParameter(found, add.Argument));

        var py = "print(";
        var print = Assert.Single(ParameterInfo.Basic(py, ParameterInfo.CallAt(py, 6, Python)!, Python, []));
        Assert.Contains("output", print.Documentation);
    }

    [Fact]
    public void ActiveParameterAndOverloads()
    {
        var two = SignatureInfo.FromLabel("Max(a, b)");
        var rest = SignatureInfo.FromLabel("print(*values, sep=' ')");
        Assert.Equal(-1, ParameterInfo.ActiveParameter(two, 2));
        var varargs = SignatureInfo.FromLabel("Printf(format string, a ...any)");
        Assert.Equal(1, ParameterInfo.ActiveParameter(varargs, 5));
        Assert.Equal(1, ParameterInfo.BestOverload([SignatureInfo.FromLabel("Next()"), SignatureInfo.FromLabel("Next(max)")], 0));
        Assert.Equal(0, ParameterInfo.ActiveParameter(rest, 0));
    }

    [Fact]
    public void ReadsSignatureHelp()
    {
        var result = JsonNode.Parse("""
            { "signatures": [
                { "label": "Println(a ...any) (n int, err error)", "documentation": "Println formats.", "parameters": [ { "label": "a ...any" } ] },
                { "label": "f(x int, y int)", "parameters": [ { "label": [2, 7] }, { "label": [9, 14], "documentation": { "kind": "markdown", "value": "the **y**" } } ] }
              ], "activeSignature": 1, "activeParameter": 1 }
            """);
        var (signatures, active, parameter) = ParameterInfo.FromLsp(result);
        Assert.Equal(2, signatures.Count);
        Assert.Equal((8, 8), signatures[0].Parameters[0]);
        Assert.Equal("Println formats.", signatures[0].Documentation);
        Assert.Equal((9, 5), signatures[1].Parameters[1]);
        Assert.Equal("the y", signatures[1].ParameterDocs![1]);
        Assert.Equal(1, active);
        Assert.Equal(1, parameter);
        Assert.Empty(ParameterInfo.FromLsp(null).Signatures);
    }
}

public class SnippetTemplateTests
{
    [Fact]
    public void ExpandsFieldsAndIndents()
    {
        var (text, fields) = SnippetTemplate.Expand("for (int ${1:i} = 0; $1 < ${2:length}; $1++)\n{\n    $0\n}", "  ");
        Assert.Equal("for (int i = 0; i < length; i++)\n  {\n      \n  }", text);
        Assert.Equal([new SnippetField(1, 9, 1), new SnippetField(1, 16, 1), new SnippetField(2, 20, 6), new SnippetField(1, 28, 1), new SnippetField(0, 43, 0)], fields);
    }

    [Fact]
    public void ChoicesNestingAndEscapes()
    {
        Assert.Equal("public void Run(a)", SnippetTemplate.PlainText("${1|public,private|} void ${2:Run(${3:a})}"));
        Assert.Equal("cost: $5 }", SnippetTemplate.PlainText("cost: \\$5 \\}"));
        Assert.Equal("{ get; set; }", SnippetTemplate.PlainText("{ ${1:get; set;} }"));
    }

    [Fact]
    public void OlderSnippetsAndDollarSignsStayAsTheyWere()
    {
        // Only $0: the caret's place, as before.
        var (text, fields) = SnippetTemplate.Expand("if ($0condition) {\n}");
        Assert.Equal("if (condition) {\n}", text);
        Assert.Equal([new SnippetField(0, 4, 0)], fields);
        Assert.False(SnippetTemplate.HasFields("if ($0condition) {\n}"));
        Assert.True(SnippetTemplate.HasFields("print(${1:x})"));
        // PHP variables and JavaScript template literals aren't fields.
        Assert.Equal("echo $name; `${total}`", SnippetTemplate.PlainText("echo $name; `${total}`"));
        Assert.Equal("$", SnippetTemplate.PlainText("$"));
    }

    [Fact]
    public void BuiltInSnippetsAllParse()
    {
        foreach (var snippet in Snippets.BuiltIn)
        {
            var (text, fields) = SnippetTemplate.Expand(snippet.Body);
            Assert.DoesNotContain("${", text.Replace("`${", ""));
            Assert.All(fields, f => Assert.InRange(f.Start + f.Length, 0, text.Length));
        }
    }
}

public class CompletionHistoryTests
{
    [Fact]
    public void RecentNamesRankHigher()
    {
        var history = new CompletionHistory();
        Assert.Equal(0, history.Recency("csharp", "WriteLine"));
        history.Used("csharp", "WriteLine");
        history.Used("csharp", "Write");
        Assert.True(history.Recency("csharp", "Write") > history.Recency("csharp", "WriteLine"));
        Assert.Equal(0, history.Recency("python", "Write"));
        history.Used("csharp", "WriteLine");
        Assert.True(history.Recency("csharp", "WriteLine") > history.Recency("csharp", "Write"));
    }
}

public class ExplanationTests
{
    [Fact]
    public void ExplainsCommonErrors()
    {
        Assert.Contains("semicolon", ErrorExplanations.Explain("; expected", "CS1002", "csharp"));
        Assert.Contains("Dim", ErrorExplanations.Explain("BC30451: 'x' is not declared.", null, "vb"));
        Assert.Contains("indented", ErrorExplanations.Explain("IndentationError: expected an indented block", null, "python"));
        Assert.Contains("#include", ErrorExplanations.Explain("use of undeclared identifier 'printf'", null, "cpp"));
        Assert.Contains("let or const", ErrorExplanations.Explain("ReferenceError: x is not defined", null, "javascript"));
        Assert.Contains("unused", ErrorExplanations.Explain("\"os\" imported and not used", null, "go"));
        Assert.Contains("let mut", ErrorExplanations.Explain("cannot assign twice to immutable variable `x`", "E0384", "rust"));
        // A JavaScript error doesn't get Python's explanation, and an unknown message gets none.
        Assert.DoesNotContain("Python", ErrorExplanations.Explain("x is not defined", null, "javascript") ?? "");
        Assert.Null(ErrorExplanations.Explain("something unusual happened", null, "csharp"));
        // Without a language, any rule may answer.
        Assert.NotNull(ErrorExplanations.Explain("error CS0103: The name 'x' does not exist in the current context"));
    }

    [Fact]
    public void ExplainsKeywords()
    {
        Assert.Contains("no value back", KeywordGuide.Explain("void", "csharp"));
        Assert.Contains("constant", KeywordGuide.Explain("let", "swift"));
        Assert.Contains("can change", KeywordGuide.Explain("let", "javascript"));
        Assert.Contains("Dim", KeywordGuide.Explain("dim", "vb"));
        Assert.Contains("inside", KeywordGuide.Explain("padding", "css"));
        Assert.Null(KeywordGuide.Explain("padding", "csharp"));
        Assert.Null(KeywordGuide.Explain("Padding", "css"));
        Assert.Contains("JavaScript", "JavaScript " + KeywordGuide.Explain("let", "typescript"));
    }
}

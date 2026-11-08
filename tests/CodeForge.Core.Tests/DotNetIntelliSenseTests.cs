using CodeForge.Core;

namespace CodeForge.Core.Tests;

/// <summary>The same cases as the Mac version's BasicIntelliSenseTests.</summary>
public class DotNetIntelliSenseTests
{
    private const string Code = """
        Module Program
            Sub Main(args As String())
                Dim names As New List(Of String)
                Dim message = "hi"
                Dim calc As New Calculator()
                Console.
            End Sub
        End Module
        Public Class Calculator
            Public Function Add(a As Integer, b As Integer) As Integer
                Return a + b
            End Function
            Public Shared Function Create() As Calculator
                Return New Calculator()
            End Function
        End Class
        """;

    private static List<string> Complete(string after, string? replacing = null)
    {
        var text = Code.Replace("\r\n", "\n");
        if (replacing != null) text = text.Replace("Console.", replacing);
        var offset = text.IndexOf(after, StringComparison.Ordinal) + after.Length;
        var symbols = SymbolParser.Symbols(text, "vb");
        return BasicIntelliSense.Completions(text, offset, Languages.ById("vb"), symbols).Items.Select(i => i.Label).ToList();
    }

    [Fact]
    public void MemberLists()
    {
        var console = Complete("Console.");
        Assert.Contains("WriteLine", console);
        Assert.Contains("ReadLine", console);
        Assert.DoesNotContain("Length", console);
        Assert.Contains("Add", Complete("names.", "names."));
        Assert.Contains("Where", Complete("names.", "names."));          // LINQ
        Assert.Contains("ToUpper", Complete("message.", "message."));
        var calc = Complete("calc.", "calc.");
        Assert.Contains("Add", calc);
        Assert.DoesNotContain("Create", calc);                            // Shared: not on an instance
        Assert.Equal("Create", string.Join(",", Complete("Calculator.", "Calculator.")));
        Assert.Contains("File", Complete("IO.", "System.IO."));
        Assert.Contains("AddDays", Complete("Now.", "DateTime.Now."));   // through a property's type
    }

    [Fact]
    public void WordsKeywordsHoverAndDefinition()
    {
        var types = Complete("Dim x As ", "Dim x As ");
        Assert.Contains("Integer", types);
        Assert.Contains("Calculator", types);
        var start = Complete("        Dim calc As New Calculator()\n        ", "");
        Assert.Contains("Dim", start);
        Assert.Contains("names", start);
        var text = Code.Replace("\r\n", "\n");
        var symbols = SymbolParser.Symbols(text, "vb");
        Assert.Equal("Class System.Console", DotNetIntelliSense.Hover(text, text.IndexOf("Console") + 2, "vb", symbols));
        Assert.StartsWith("Public Function Add", DotNetIntelliSense.Hover(text, text.IndexOf("Add(a"), "vb", symbols));
        Assert.Equal(9, DotNetIntelliSense.Definition(text, text.IndexOf("New Calculator()") + 5, "vb", symbols)?.Line);
        Assert.Equal("String()", DotNetCatalog.Type("String")!.Members.First(m => m.Name == "Split").ReturnType);
    }
}

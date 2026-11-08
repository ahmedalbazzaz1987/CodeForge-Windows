using CodeForge.Core;

namespace CodeForge.Core.Tests;

public class RefactoringTests
{
    /// <summary>Extracts whole lines (1-based, inclusive).</summary>
    private static ExtractMethod.Result Extract(string text, int first, int last, string language)
    {
        var lines = text.Split('\n');
        var start = lines.Take(first - 1).Sum(l => l.Length + 1);
        var end = lines.Take(last).Sum(l => l.Length + 1);
        return ExtractMethod.Extract(text, start, Math.Min(end, text.Length) - start, language);
    }

    [Fact]
    public void VisualBasicGetsAFunctionWithTypedParameters()
    {
        var code = """
            Module Program
                Sub Main(args As String())
                    Dim price As Double = 9.5
                    Dim count = 3
                    Dim total As Double = price * count
                    total = total * 1.2
                    Console.WriteLine(total)
                End Sub
            End Module
            """.Replace("\r\n", "\n");
        var result = Extract(code, 5, 6, "vb");
        Assert.Equal("""
            Module Program
                Sub Main(args As String())
                    Dim price As Double = 9.5
                    Dim count = 3
                    Dim total As Double = NewMethod(price, count)
                    Console.WriteLine(total)
                End Sub

                Private Function NewMethod(price As Double, count As Integer) As Double
                    Dim total As Double = price * count
                    total = total * 1.2
                    Return total
                End Function
            End Module
            """.Replace("\r\n", "\n"), result.Text);
        Assert.Equal("NewMethod", result.Text.Substring(result.NameStart, result.NameLength));
        Assert.EndsWith("Private Function ", result.Text[..result.NameStart]);

        // `As New Type` gives the type; `Dim a, b As Integer` gives both names the type.
        var objects = "Module M\n    Sub Main()\n        Dim cart As New List(Of String)\n        Dim a, b As Integer\n        cart.Add(\"x\")\n        Console.WriteLine(a + b)\n    End Sub\nEnd Module";
        Assert.Contains("Private Sub NewMethod(cart As List(Of String), a As Integer, b As Integer)", Extract(objects, 5, 6, "vb").Text);

        // A Sub when nothing is used afterwards.
        var sub = Extract(code, 7, 7, "vb").Text;
        Assert.Contains("        NewMethod(total)\n", sub);
        Assert.Contains("    Private Sub NewMethod(total As Double)\n        Console.WriteLine(total)\n    End Sub", sub);
    }

    [Fact]
    public void CSharpAndWhatCantBeExtracted()
    {
        var code = """
            class Calculator
            {
                public static int Run(int a, int b)
                {
                    int sum = a + b;
                    Console.WriteLine(sum);
                    return sum;
                }
            }
            """.Replace("\r\n", "\n");
        var result = Extract(code, 5, 5, "csharp").Text;
        Assert.Contains("        int sum = NewMethod(a, b);", result);
        Assert.Contains("    private static int NewMethod(int a, int b)\n    {\n        int sum = a + b;\n        return sum;\n    }", result);
        Assert.Contains("returns from the method", Assert.Throws<ExtractMethod.Failure>(() => Extract(code, 6, 7, "csharp")).Message);
        // Top-level statements.
        Assert.Contains("static void NewMethod()", Extract("int x = 1;\nConsole.WriteLine(\"hi\");", 2, 2, "csharp").Text);
        Assert.Throws<ExtractMethod.Failure>(() => ExtractMethod.Extract("x", 0, 1, "rust"));
    }

    [Fact]
    public void PythonMethodsKeepSelf()
    {
        var code = "class Cart:\n    def total(self, items):\n        subtotal = sum(items)\n        tax = subtotal * 0.2\n        return subtotal + tax";
        var result = Extract(code, 4, 4, "python").Text;
        Assert.Contains("        tax = self.new_method(subtotal)\n", result);
        Assert.Contains("    def new_method(self, subtotal):\n        tax = subtotal * 0.2\n        return tax", result);
    }

    [Fact]
    public void WordsInStringsAndCommentsAreMasked()
    {
        var masked = ExtractMethod.Mask("x = \"total\" # total\ny = 'a'", Languages.ById("python"));
        Assert.Equal("x = \"     \"        \ny = ' '", masked);
        // VB writes a quote in a string as "", and ' starts a comment.
        Assert.Equal("Dim s = \"    \"    ", ExtractMethod.Mask("Dim s = \"a\"\"b\" ' c", Languages.ById("vb")));
    }
}

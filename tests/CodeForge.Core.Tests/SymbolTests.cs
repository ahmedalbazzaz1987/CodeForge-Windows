using CodeForge.Core;

namespace CodeForge.Core.Tests;

/// <summary>The same cases as the Mac version's SymbolTests.</summary>
public class SymbolTests
{
    [Fact]
    public void VisualBasic()
    {
        var code = """
            Namespace Shop
                Public Class Product
                    Public Property Name As String
                    Private _price As Decimal
                    Public Shared Count As Integer
                    Public Sub New(name As String)
                        Me.Name = name
                        Dim local As Integer = 1
                    End Sub
                    Public Function Total(quantity As Integer) As Decimal
                        Return _price * quantity
                    End Function
                End Class
                Public Enum Size
                    Small
                    Large = 2
                End Enum
            End Namespace
            Module Helpers
                Sub Greet()
                End Sub
            End Module
            """;
        var symbols = SymbolParser.Symbols(code, "vb");
        Assert.Equal("Product Name _price Count Product Total Size Small Large Helpers Greet", string.Join(" ", symbols.Select(s => s.Name)));
        var total = symbols.First(s => s.Name == "Total");
        Assert.Equal(SymbolKind.Method, total.Kind);
        Assert.Equal("Decimal", total.Type);
        Assert.Equal("Shop.Product.Total", total.QualifiedName);
        Assert.True(symbols.First(s => s.Name == "Count").IsShared);
        Assert.Equal(SymbolKind.Constructor, symbols[4].Kind);
        Assert.True(symbols.First(s => s.Name == "Greet").IsShared);
        Assert.Equal(SymbolKind.EnumMember, symbols.First(s => s.Name == "Large").Kind);
    }

    [Fact]
    public void CSharpPythonAndSwift()
    {
        var cs = """
            namespace Shop;

            public class Cart
            {
                private readonly List<Item> _items = new();
                public int Count { get; set; }
                public Cart() { }
                public decimal Total(decimal tax)
                {
                    var sum = 0m;
                    return sum;
                }
                public override string ToString() => "Cart";
            }
            public enum Color { Red, Green }
            """;
        var symbols = SymbolParser.Symbols(cs, "csharp");
        Assert.Equal("Cart _items Count Cart Total ToString Color", string.Join(" ", symbols.Select(s => s.Name)));
        Assert.Equal("decimal", symbols.First(s => s.Name == "Total").Type);
        Assert.Equal(SymbolKind.Property, symbols.First(s => s.Name == "Count").Kind);
        Assert.Equal("Shop", symbols[0].Namespace);

        var python = "class Dog:\n    legs = 4\n    def __init__(self, name):\n        self.name = name\n    def bark(self) -> str:\n        return 'woof'\n\ndef helper():\n    pass\n";
        Assert.Equal("Class Dog|Field legs|Constructor __init__|Field name|Method bark|Function helper",
            string.Join("|", SymbolParser.Symbols(python, "python").Select(s => $"{s.Kind} {s.Name}")));

        var swift = "struct Point {\n    var x: Double\n    func length() -> Double {\n        let y = 1\n        return x\n    }\n}\nprotocol Shape {\n    func area() -> Double\n    var name: String { get }\n}\n";
        Assert.Equal("Point x length Shape area name", string.Join(" ", SymbolParser.Symbols(swift, "swift").Select(s => s.Name)));
    }
}

using CodeForge.Core;

namespace CodeForge.Core.Tests;

public class EditorToolsTests
{
    [Fact]
    public void FindsAcrossFilesWithOptions()
    {
        var folder = Directory.CreateTempSubdirectory("cf-find").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "bin"));
            File.WriteAllText(Path.Combine(folder, "Program.vb"), "Module Program\r\n    Sub Main()\r\n        Console.WriteLine(total)\r\n        Dim totals = 2\r\n    End Sub\r\nEnd Module\r\n");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "total = 1\n");
            File.WriteAllText(Path.Combine(folder, "bin", "skip.vb"), "total");

            var all = FileSearch.Search("total", folder, new SearchOptions());
            Assert.Equal(3, all.Count);    // bin\ is skipped
            var vb = all.Where(m => m.Path.EndsWith(".vb")).ToList();
            Assert.Equal([3, 4], vb.Select(m => m.Line));
            Assert.Equal(27, vb[0].Column);
            Assert.Equal("        Console.WriteLine(total)", vb[0].Preview);

            Assert.Equal(2, FileSearch.Search("total", folder, new SearchOptions(WholeWord: true)).Count);
            Assert.Empty(FileSearch.Search("TOTAL", folder, new SearchOptions(MatchCase: true)));
            Assert.Equal(2, FileSearch.Search("total", folder, new SearchOptions(FileTypes: "*.vb; *.cs")).Count);
            Assert.Single(FileSearch.Search(@"Dim (\w+)", folder, new SearchOptions(RegularExpression: true)));
            Assert.Null(FileSearch.Expression("(", new SearchOptions(RegularExpression: true)));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ReplacesTextAndGroups()
    {
        var word = FileSearch.Expression("count", new SearchOptions(WholeWord: true))!;
        Assert.Equal(("total = total + 1 ' counter", 2), FileSearch.Replace("count = count + 1 ' counter", word, "total", false));
        var regex = FileSearch.Expression(@"(\w+)\.Length", new SearchOptions(RegularExpression: true))!;
        Assert.Equal("Len(name)", FileSearch.Replace("name.Length", regex, "Len($1)", true).Text);
        var literal = FileSearch.Expression("a", new SearchOptions())!;
        Assert.Equal("$1", FileSearch.Replace("a", literal, "$1", false).Text);
    }

    [Fact]
    public void TaskListFindsOnlyComments()
    {
        var vb = TaskScanner.Tasks("' TODO: check the total\r\nDim note = \"TODO not this one\"\r\nDim x = 1 ' HACK works for now\r\n", "a.vb", Languages.ById("vb"));
        Assert.Equal(["TODO", "HACK"], vb.Select(t => t.Token));
        Assert.Equal(("check the total", 3), (vb[0].Text, vb[1].Line));
        var c = TaskScanner.Tasks("int a; /* FIXME: leak */\n/* start\n   BUG: spans lines\n*/\nchar *s = \"// TODO no\";\n", "a.c", Languages.ById("c"));
        Assert.Equal([("FIXME", "leak"), ("BUG", "spans lines")], c.Select(t => (t.Token, t.Text)));
        Assert.Empty(TaskScanner.Tasks("x = 'it''s TODO'\n", "a.py", Languages.ById("python")));
        Assert.Empty(TaskScanner.Tasks("MyTODOList = 1 # all good\n", "a.py", Languages.ById("python")));
    }

    [Fact]
    public void BookmarkOrderWraps()
    {
        var marks = new Dictionary<string, List<int>> { ["a"] = [3, 9], ["b"] = [1] };
        Assert.Equal(("a", 9), Bookmarks.Next(("a", 3), marks, false));
        Assert.Equal(("b", 1), Bookmarks.Next(("a", 9), marks, false));
        Assert.Equal(("a", 3), Bookmarks.Next(("b", 1), marks, false));   // wraps around
        Assert.Equal(("b", 1), Bookmarks.Next(("a", 3), marks, true));
        Assert.Null(Bookmarks.Next(("a", 1), new Dictionary<string, List<int>>(), false));
    }

    [Fact]
    public void NavigationWorksLikeABrowser()
    {
        var history = new NavigationHistory<string>();
        history.Record("a");
        history.Record("b");
        Assert.True(history.TryGoBack("c", out var place));
        Assert.Equal("b", place);
        Assert.True(history.TryGoBack("b", out place));
        Assert.Equal("a", place);
        Assert.False(history.TryGoBack("a", out _));
        Assert.True(history.TryGoForward("a", out place));
        Assert.Equal("b", place);
        history.Record("z");     // a new jump clears Forward
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void SnippetsExpandByShortcut()
    {
        Assert.Equal("For Each…Next", Snippets.Find("vb", "foreach")?.Title);   // VB ignores case
        Assert.Null(Snippets.Find("csharp", "Prop"));
        var prop = Snippets.Find("csharp", "prop")!;
        var (text, cursor) = prop.ToolboxItem.Expanded("    ");
        Assert.Equal("public int MyProperty { get; set; }", text);
        Assert.Equal("public ".Length, cursor);
        Assert.Equal(Snippets.All.Count, Snippets.All.Select(s => s.LanguageId + s.Shortcut).Distinct().Count());
        Assert.True(Snippets.All.Count > 60);
    }
}

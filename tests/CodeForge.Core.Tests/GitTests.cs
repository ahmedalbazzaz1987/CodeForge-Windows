using CodeForge.Core;

namespace CodeForge.Core.Tests;

public class GitTests
{
    [Fact]
    public void ParsesBranchCountsAndChanges()
    {
        var status = GitStatus.Parse("## main...origin/main [ahead 2, behind 1]\nM  src/a.cs\n M b.cs\nR  old.cs -> new.cs\n?? \"my file.txt\"\nUU conflict.cs\n");
        Assert.Equal("main", status.Branch);
        Assert.Equal("origin/main", status.Upstream);
        Assert.Equal((2, 1), (status.Ahead, status.Behind));
        Assert.Equal(5, status.Changes.Count);
        Assert.True(status.Changes[0].IsStaged);
        Assert.False(status.Changes[1].IsStaged);
        Assert.Equal("M", status.Changes[1].Letter);
        Assert.Equal(("new.cs", "old.cs"), (status.Changes[2].Path, status.Changes[2].OriginalPath));
        Assert.Equal(("my file.txt", "U"), (status.Changes[3].Path, status.Changes[3].Letter));
        Assert.Equal("!", status.Changes[4].Letter);
        var fresh = GitStatus.Parse("## No commits yet on main\n?? a.py\n");
        Assert.Equal("main", fresh.Branch);
        Assert.Null(fresh.Upstream);
    }

    [Fact]
    public void ParsesBlameAndDescribesTimes()
    {
        var hash = new string('a', 40);
        var blame = GitBlame.Parse($"{hash} 1 1 2\nauthor Ahmed\nauthor-time 1700000000\nsummary First\n\tline one\n{hash} 2 2\nauthor Ahmed\nauthor-time 1700000000\nsummary First\n\tline two\n");
        Assert.Equal(2, blame.Count);
        Assert.Equal("Ahmed", blame[2].Author);
        Assert.Equal("First", GitBlame.Latest(blame, 1, 5)!.Summary);
        var now = DateTimeOffset.FromUnixTimeSeconds(1700000000);
        Assert.Equal("just now", RelativeTime.Describe(now, now));
        Assert.Equal("5 minutes ago", RelativeTime.Describe(now.AddMinutes(-5), now));
        Assert.Equal("yesterday", RelativeTime.Describe(now.AddHours(-30), now));
        Assert.Equal("3 weeks ago", RelativeTime.Describe(now.AddDays(-21), now));
    }

    [Fact]
    public void RunsGitInAFolderWithAnArabicName()
    {
        if (GitCommand.Program() == null) return;
        var folder = Path.Combine(Directory.CreateTempSubdirectory("cf-git").FullName, "مشروع");
        Directory.CreateDirectory(folder);
        try
        {
            Assert.True(GitCommand.Run(folder, "init", "-b", "main").Succeeded);
            File.WriteAllText(Path.Combine(folder, "ملف.txt"), "x");
            var status = GitStatus.Parse(GitCommand.Run(folder, "status", "--porcelain=v1", "--branch", "--untracked-files=all").Output);
            Assert.Equal("main", status.Branch);
            Assert.Equal("ملف.txt", status.Changes.Single().Path);
            Assert.False(GitCommand.Run(folder, "no-such-command").Succeeded);
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(folder)!, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path.GetDirectoryName(folder)!, true);
        }
    }

    [Fact]
    public void RecognisesAMissingIdentity()
    {
        Assert.True(GitIdentity.IsMissing("Author identity unknown\n\n*** Please tell me who you are."));
        Assert.False(GitIdentity.IsMissing("nothing to commit"));
        Assert.Contains("bin/", GitIdentity.DefaultIgnore);
    }

    [Fact]
    public void CodeLensCountsUsesAndShowsTheLastChange()
    {
        var code = "Module M\n    Sub Main()\n        Greet() ' Greet in a comment\n        Greet()\n    End Sub\n    Sub Greet()\n        Console.WriteLine(\"Greet\")\n    End Sub\nEnd Module";
        var vb = Languages.ById("vb");
        Assert.Equal(3, CodeLens.CountWord(code, "greet", vb));
        var symbols = SymbolParser.Symbols(code, "vb");
        var hash = new string('b', 40);
        var blame = Enumerable.Range(1, 9).ToDictionary(l => l, l => new GitBlameLine(l == 7 ? new string('0', 40) : hash, "Ahmed", DateTimeOffset.Now.AddDays(-3), "x"));
        var lens = CodeLens.Describe(symbols, name => CodeLens.CountWord(code, name, vb), blame, 9);
        Assert.Equal("0 references  |  Ahmed, 3 days ago", lens[2]);
        Assert.Equal("2 references  |  not committed yet", lens[6]);
        Assert.StartsWith("0 references", lens[1]);
    }
}


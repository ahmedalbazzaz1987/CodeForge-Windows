using CodeForge.Core;

namespace CodeForge.Core.Tests;

public class DiagnosticParserTests
{
    [Fact]
    public void ReadsMSBuildErrorsWithWindowsPaths()
    {
        var output = """
            C:\Users\me\Shop\Program.vb(12,9): error BC30451: 'totl' is not declared. It may be inaccessible due to its protection level. [C:\Users\me\Shop\Shop.vbproj]
            C:\Users\me\Shop\Form1.cs(3,1): warning CS8019: Unnecessary using directive. [C:\Users\me\Shop\Shop.csproj]
            Build FAILED.
            """;
        var list = DiagnosticParser.Parse(output);
        Assert.Equal(2, list.Count);
        Assert.Equal(Severity.Error, list[0].Severity);
        Assert.Equal(@"C:\Users\me\Shop\Program.vb", list[0].File);
        Assert.Equal(12, list[0].Line);
        Assert.Equal(9, list[0].Column);
        Assert.Equal("BC30451", list[0].Code);
        Assert.StartsWith("'totl' is not declared", list[0].Message);
        Assert.Equal(Severity.Warning, list[1].Severity);
    }

    [Fact]
    public void ReadsGccPythonAndNodeErrors()
    {
        var gcc = DiagnosticParser.Parse(@"C:\src\main.c:4:5: error: expected ';' before 'return'");
        Assert.Equal((@"C:\src\main.c", 4, 5), (gcc[0].File, gcc[0].Line, gcc[0].Column!.Value));

        var python = DiagnosticParser.Parse("Traceback (most recent call last):\n  File \"C:\\app\\main.py\", line 7, in <module>\n    x = 1 / 0\nZeroDivisionError: division by zero\n");
        Assert.Equal(("ZeroDivisionError: division by zero", 7), (python[0].Message, python[0].Line));

        var node = DiagnosticParser.Parse("C:\\app\\index.js:3\nthrow new Error('boom');\n^\n\nError: boom\n");
        Assert.Equal((@"C:\app\index.js", 3), (node[0].File, node[0].Line));

        Assert.Empty(DiagnosticParser.Parse("Time Elapsed 00:00:01.20\nBuild succeeded."));
    }

    [Fact]
    public void ResolvesRelativePaths()
    {
        var diagnostic = new Diagnostic(Severity.Error, "x", Path.Combine("src", "a.cs"), 1);
        Assert.Equal(Path.GetFullPath(Path.Combine("/work", "src", "a.cs")), diagnostic.FullPath("/work"));
    }
}

public class LanguageAndBuildTests
{
    [Fact]
    public void FindsLanguagesByFile()
    {
        Assert.Equal("vb", Languages.ForFile(@"C:\x\Form1.vb").Id);
        Assert.Equal("xaml", Languages.ForFile("MainWindow.xaml").Id);
        Assert.Equal("xml", Languages.ForFile("App.vbproj").Id);
        Assert.Equal("dockerfile", Languages.ForFile("Dockerfile").Id);
        Assert.Equal("text", Languages.ForFile("README").Id);
        Assert.Equal(Languages.All.Count, Languages.All.Select(l => l.Id).Distinct().Count());
    }

    [Fact]
    public void ExpandsRunCommands()
    {
        var file = Path.Combine("C:", "code", "hello.c");
        var command = CommandTemplate.Expand(Languages.ById("c").RunCommand!, file, "OUT");
        Assert.Contains(file, command);
        Assert.Contains(Path.Combine("OUT", "hello") + ".exe", command);
    }

    [Fact]
    public void DetectsDotNetProjectsAndConfigurations()
    {
        var folder = Directory.CreateTempSubdirectory("cf-build").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "Shop.vbproj"), "<Project />");
            var plan = BuildSystem.Detect(folder)!;
            Assert.Equal(".NET (Shop)", plan.Name);
            Assert.Equal("dotnet run", plan.Run);
            Assert.Contains("vb", plan.Languages);
            Assert.Equal("dotnet build -c Release", BuildSystem.Detect(folder, BuildConfiguration.Release)!.Build);
            Assert.Equal("dotnet test", BuildSystem.TestCommand(folder));

            // A solution with its project in a subfolder: run that project.
            File.Delete(Path.Combine(folder, "Shop.vbproj"));
            File.WriteAllText(Path.Combine(folder, "Shop.slnx"), "<Solution />");
            Directory.CreateDirectory(Path.Combine(folder, "App"));
            File.WriteAllText(Path.Combine(folder, "App", "App.csproj"), "<Project />");
            Assert.Equal("dotnet run --project \"" + Path.Combine("App", "App.csproj") + "\"", BuildSystem.Detect(folder)!.Run);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void FindsTheListeningAddress()
    {
        Assert.Equal("http://localhost:5080", BuildSystem.ListeningAddress("info: Now listening on: http://[::]:5080"));
        Assert.Null(BuildSystem.ListeningAddress("Application started."));
    }
}

public class TemplateTests
{
    [Fact]
    public void MakesIdentifiersAndCommands()
    {
        Assert.Equal("MyApp2", ProjectTemplates.Identifier("My App 2"));
        Assert.Equal("_3D", ProjectTemplates.Identifier("3D"));
        var winforms = ProjectTemplates.All.First(t => t.Id == "winforms-vb");
        Assert.Equal("dotnet new winforms -lang VB -o \"My App\" -n MyApp", ProjectTemplates.CreationCommand(winforms, "My App"));
        Assert.Equal(ProjectTemplates.All.Count, ProjectTemplates.All.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void CreatesFileTemplates()
    {
        var location = Directory.CreateTempSubdirectory("cf-new").FullName;
        try
        {
            var folder = ProjectTemplates.Create(ProjectTemplates.All.First(t => t.Id == "node"), "Hello App", location);
            Assert.Contains("\"name\": \"HelloApp\"", File.ReadAllText(Path.Combine(folder, "package.json")));
            Assert.Throws<IOException>(() => ProjectTemplates.Create(ProjectTemplates.All.First(t => t.Id == "node"), "Hello App", location));
        }
        finally { Directory.Delete(location, true); }
    }
}

public class UpdateTests
{
    [Fact]
    public void ComparesVersionsByNumber()
    {
        Assert.True(AppVersion.Parse("0.10.0")! > AppVersion.Parse("0.9.3")!);
        Assert.True(AppVersion.Parse("v1.2")! < AppVersion.Parse("1.2.1")!);
        Assert.Equal(AppVersion.Parse("1.0")!, AppVersion.Parse("1.0.0-beta")!);
        Assert.Null(AppVersion.Parse("next"));
    }

    [Fact]
    public void ReadsGitHubReleases()
    {
        var json = """
            {"tag_name":"v0.2.0","body":"Notes","html_url":"https://github.com/o/r/releases/tag/v0.2.0","assets":[
              {"name":"CodeForge-0.2.0-windows.zip","browser_download_url":"https://x/zip"},
              {"name":"CodeForge-0.2.0-windows.zip.sha256","browser_download_url":"https://x/sha"},
              {"name":"CodeForge-0.2.0-setup.exe","browser_download_url":"https://x/setup"},
              {"name":"CodeForge-0.2.0-setup.exe.sha256","browser_download_url":"https://x/setup-sha"}]}
            """;
        var release = ReleaseInfo.Parse(json)!;
        Assert.Equal("0.2.0", release.Version.ToString());
        Assert.Equal("https://x/zip", release.ZipUrl);
        Assert.Equal(("https://x/setup", "https://x/setup-sha"), (release.SetupUrl, release.SetupChecksumUrl));
        Assert.Null(ReleaseInfo.Parse("""{"tag_name":"v1","assets":[]}"""));
        Assert.Equal(new string('a', 64), ReleaseInfo.ChecksumValue(new string('A', 64) + "  CodeForge.zip\n"));
        Assert.Null(ReleaseInfo.ChecksumValue("oops"));
    }

    [Fact]
    public void MatchesFuzzily()
    {
        Assert.NotNull(FuzzyMatch.Score("frm1", "Form1.vb"));
        Assert.Null(FuzzyMatch.Score("xyz", "Form1.vb"));
        Assert.True(FuzzyMatch.Score("pv", "Program.vb") > FuzzyMatch.Score("pv", "MapView.cs"));
    }
}

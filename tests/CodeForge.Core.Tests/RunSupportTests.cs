using CodeForge.Core;
using Xunit;

namespace CodeForge.Core.Tests;

public class RunSupportTests
{
    [Fact]
    public void ACarriageReturnRedrawsTheLine()
    {
        Assert.Equal("Receiving 100%\n", ConsoleText.Rewrite("", "Receiving 10%\rReceiving 50%\rReceiving 100%\n"));
        Assert.Equal("done\n", ConsoleText.Rewrite("50%", "\rdone\n"));
        // Split between chunks, a CR LF is still one line break.
        var first = ConsoleText.Rewrite("", "line\r");
        Assert.Equal("line\r", first);
        Assert.Equal("line\nnext", ConsoleText.Rewrite(first, "\nnext"));
    }

    [Fact]
    public void FindsTheToolACommandNeeds()
    {
        Assert.Equal("go", ToolCheck.Program("go run ."));
        Assert.Equal("C:\\Tools\\x.exe", ToolCheck.Program("\"C:\\Tools\\x.exe\" --flag"));
        Assert.Null(ToolCheck.Program("start \"\" \"index.html\""));
        Assert.Contains("winget install GoLang.Go", ToolCheck.InstallHint("go"));
        Assert.Contains("Rustup", ToolCheck.InstallHint("cargo"));

        var folder = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(folder, "fake.cmd"), "");
        Assert.True(ToolCheck.IsAvailable("fake", folder, ".EXE;.CMD"));
        Assert.False(ToolCheck.IsAvailable("missing", folder, ".EXE;.CMD"));
        Directory.Delete(folder, true);
    }

    private static string ProjectFolder(string project)
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(folder, "App.csproj"), project);
        return folder;
    }

    [Fact]
    public void TestProjectsRunTheirTestsAndLibrariesCantStart()
    {
        var tests = ProjectFolder("<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" /></ItemGroup></Project>");
        Assert.Equal("dotnet test", BuildSystem.Detect(tests)!.Run);
        Assert.Null(BuildSystem.LibraryProject(tests));

        var library = ProjectFolder("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        Assert.Equal("App", BuildSystem.LibraryProject(library));

        var console = ProjectFolder("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");
        Assert.Null(BuildSystem.LibraryProject(console));
        Assert.Equal("dotnet run", BuildSystem.Detect(console)!.Run);
    }

    [Fact]
    public void MauiBuildsAndRunsTheWindowsVersion()
    {
        var maui = ProjectFolder("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net10.0-android;net10.0-ios</TargetFrameworks>" +
            "<TargetFrameworks Condition=\"$([MSBuild]::IsOSPlatform('windows'))\">$(TargetFrameworks);net10.0-windows10.0.19041.0</TargetFrameworks>" +
            "<UseMaui>true</UseMaui><OutputType>Exe</OutputType></PropertyGroup></Project>");
        var plan = BuildSystem.Detect(maui)!;
        Assert.Equal("dotnet build -f net10.0-windows10.0.19041.0", plan.Build);
        Assert.Equal("dotnet run -f net10.0-windows10.0.19041.0", plan.Run);
        Assert.Equal("dotnet run -f net10.0-windows10.0.19041.0 -c Release", BuildSystem.Detect(maui, BuildConfiguration.Release)!.Run);
    }

    [Fact]
    public void TestRunnerLinesAreNotErrors()
    {
        Assert.Empty(DiagnosticParser.Parse("[xUnit.net 00:00:01.23]     Tests.UnitTest1.Test1 [FAIL]"));
        Assert.Single(DiagnosticParser.Parse("./main.go:3:5: undefined: x"));
    }

    [Fact]
    public void SnippetsAndToolboxForMoreLanguages()
    {
        foreach (var id in new[] { "c", "cpp", "java", "typescript", "go", "rust" })
        {
            Assert.NotEmpty(Snippets.ForLanguage(id));
            Assert.NotEmpty(ToolboxCatalog.Groups(id));
        }
        Assert.Equal("fmt.Println($0)", Snippets.Find("go", "pl")!.Body);
    }

    [Fact]
    public void StreamsAreSplitIntoWholeLines()
    {
        var lines = new StreamLines();
        Assert.Equal("", lines.Add("A stdout 1"));          // Python writes the text…
        Assert.Equal("A stdout 1\n", lines.Add("\r\n"));   // …and the line break separately
        Assert.Equal("one\n", lines.Add("one\r\ntwo\r"));  // a CR at the end may be half of a CR LF
        Assert.Equal("two\n", lines.Add("\nName: "));
        Assert.Equal("Name: ", lines.Pending);
        Assert.Equal("Name: ", lines.Flush());
        Assert.Equal("", lines.Pending);
    }

    [Fact]
    public void AnsiEscapesAreRemoved()
    {
        Assert.Equal("red text", ConsoleText.StripAnsi("\u001B[31mred text\u001B[0m"));
        Assert.Equal("bold", ConsoleText.StripAnsi("\u001B[1;32mbold\u001B[m"));
        Assert.Equal("title gone", ConsoleText.StripAnsi("\u001B]0;My Title\u0007title gone"));
        Assert.Equal("plain", ConsoleText.StripAnsi("plain"));
    }

    [Fact]
    public void CrashExitCodesAreExplained()
    {
        Assert.Equal("1", ExitCodes.Describe(1));
        Assert.Equal("-532462766 (0xE0434352: an unhandled .NET exception)", ExitCodes.Describe(-532462766));
        Assert.Contains("0xC00000FD: a stack overflow", ExitCodes.Describe(unchecked((int)0xC00000FD)));
        Assert.Equal("-5 (0xFFFFFFFB)", ExitCodes.Describe(-5));
    }

    [Fact]
    public void MenuGesturesUseKeyCharacters()
    {
        Assert.Equal("Ctrl+`", KeyNames.Display("Ctrl+OemTilde"));
        Assert.Equal("Ctrl+Shift+-", KeyNames.Display("Ctrl+Shift+OemMinus"));
        Assert.Equal("Ctrl+Shift+.", KeyNames.Display("Ctrl+Shift+OemPeriod"));
        Assert.Equal("Ctrl+Shift+F", KeyNames.Display("Ctrl+Shift+F"));
    }

    [Fact]
    public void DotNetExceptionsAndFailedTestsGoToTheErrorList()
    {
        var crash = DiagnosticParser.Parse(
            "next line throws\n" +
            "Unhandled exception. System.IndexOutOfRangeException: Index was outside the bounds of the array.\n" +
            "   at Program.<Main>$(String[] args) in C:\\Users\\rayan\\OneDrive\\المستندات\\CFT_CSharp\\Program.cs:line 33\n" +
            "   at Program.<Main>(String[] args)\n");
        var error = Assert.Single(crash);
        Assert.Equal(33, error.Line);
        Assert.EndsWith("Program.cs", error.File);
        Assert.StartsWith("System.IndexOutOfRangeException", error.Message);

        var test = DiagnosticParser.Parse(
            "  Failed CFT_MSTest.Test1.Intentional_Failure [3 ms]\n" +
            "  Error Message:\n" +
            "   Assert.AreEqual failed. Expected:<10>. Actual:<4>.\n" +
            "  Stack Trace:\n" +
            "     at CFT_MSTest.Test1.Intentional_Failure() in C:\\p\\Test1.cs:line 31\n");
        var failed = Assert.Single(test);
        Assert.Equal(31, failed.Line);
        Assert.Equal("Test failed: CFT_MSTest.Test1.Intentional_Failure — Assert.AreEqual failed. Expected:<10>. Actual:<4>.", failed.Message);
    }

    [Fact]
    public void BrowserCommandsTakeTheAddress()
    {
        Assert.Equal(("C:\\Edge\\msedge.exe", "--single-argument file:///C:/a.html"),
            BrowserCommand.Build("\"C:\\Edge\\msedge.exe\" --single-argument %1", "file:///C:/a.html"));
        Assert.Equal(("C:\\Chrome\\chrome.exe", "--osint -- \"https://x y\""),
            BrowserCommand.Build("\"C:\\Chrome\\chrome.exe\" --osint -- \"%1\"", "https://x y"));
        Assert.Equal(("firefox.exe", "https://x"), BrowserCommand.Build("firefox.exe", "https://x"));
        Assert.Null(BrowserCommand.Build("", "https://x"));
    }
}

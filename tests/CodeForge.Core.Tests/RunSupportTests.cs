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
}

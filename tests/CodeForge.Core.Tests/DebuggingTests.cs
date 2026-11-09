using System.Text;
using CodeForge.Core;

namespace CodeForge.Core.Tests;

public class DebuggingTests
{
    [Fact]
    public void FramedMessagesAreSplitWhereverTheBytesBreak()
    {
        var one = DapFraming.Frame("{\"seq\":1,\"text\":\"مرحبا\"}");
        var two = DapFraming.Frame("{\"seq\":2}");
        var all = one.Concat(two).ToArray();
        var framing = new DapFraming();
        var messages = new List<string>();
        // One byte at a time: the Arabic text's UTF-8 bytes are split too.
        foreach (var b in all) messages.AddRange(framing.Append(new[] { b }));
        Assert.Equal(new[] { "{\"seq\":1,\"text\":\"مرحبا\"}", "{\"seq\":2}" }, messages);
        Assert.Equal(2, new DapFraming().Append(all).Count);
    }

    [Fact]
    public void ContentLengthCountsBytesNotCharacters()
    {
        var frame = Encoding.UTF8.GetString(DapFraming.Frame("\"أ\""));
        Assert.StartsWith("Content-Length: 4\r\n\r\n", frame);
    }

    [Fact]
    public void FindsTheNewestBuiltAssembly()
    {
        var folder = Directory.CreateTempSubdirectory("cf-debug").FullName;
        try
        {
            var project = Path.Combine(folder, "App.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>Renamed</AssemblyName></PropertyGroup></Project>");
            Assert.Equal(project, DebugTools.DotNetProject(folder));
            Assert.Null(DebugTools.DotNetAssembly(project));
            var old = Path.Combine(folder, "bin", "Debug", "net8.0", "Renamed.dll");
            var current = Path.Combine(folder, "bin", "Debug", "net10.0", "Renamed.dll");
            foreach (var file in new[] { old, current })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, "");
            }
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-1));
            Assert.Equal(current, DebugTools.DotNetAssembly(project));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void AspNetAppsGetTheirHttpAddress()
    {
        var folder = Directory.CreateTempSubdirectory("cf-launch").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "Properties"));
            File.WriteAllText(Path.Combine(folder, "Properties", "launchSettings.json"), """
                {
                  // comments are allowed in this file
                  "profiles": { "https": { "applicationUrl": "https://localhost:7001;http://localhost:5001" } }
                }
                """);
            Assert.Equal("http://localhost:5001", DebugTools.LaunchUrl(folder));
            var configuration = DebugTools.DotNet(@"C:\dbg\netcoredbg.exe", @"C:\dotnet\dotnet.exe", @"C:\p\bin\Debug\net10.0\App.dll", folder);
            Assert.Equal("coreclr", configuration.AdapterId);
            Assert.Equal(new[] { "--interpreter=vscode" }, configuration.AdapterArguments);
            Assert.Equal(@"C:\p\bin\Debug\net10.0\App.dll", (string?)configuration.Launch["args"]![0]);
            Assert.Equal("http://localhost:5001", (string?)configuration.Launch["env"]!["ASPNETCORE_URLS"]);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void PythonRunsUnderDebugpyFromCodeForgesFolder()
    {
        var configuration = DebugTools.Python(@"C:\Python\python.exe", @"C:\cf\debugpy", @"C:\p\main.py", @"C:\p");
        Assert.Equal(new[] { "-m", "debugpy.adapter" }, configuration.AdapterArguments);
        Assert.Equal(@"C:\cf\debugpy", configuration.Environment["PYTHONPATH"]);
        Assert.Equal(@"C:\p\main.py", (string?)configuration.Launch["program"]);
    }

    [Fact]
    public void CompiledLanguagesBuildWithDebugInformation()
    {
        var out_ = Path.Combine(Path.GetTempPath(), "cf-build");
        var c = NativeDebugging.ForFile(Path.Combine("C:", "work", "main.c"), "c", out_)!;
        Assert.StartsWith("gcc -g -O0", c.Command);
        Assert.EndsWith("main-debug.exe", c.Program);
        Assert.Contains("-std=c++20", NativeDebugging.ForFile("x.cpp", "cpp", out_)!.Command);
        Assert.Contains("all=-N -l", NativeDebugging.ForFile("x.go", "go", out_)!.Command);
        Assert.StartsWith("rustc -g", NativeDebugging.ForFile("x.rs", "rust", out_)!.Command);
        Assert.Null(NativeDebugging.ForFile("x.py", "python", out_));
    }

    [Fact]
    public void CargoAndGoProjectsBuildForDebugging()
    {
        var folder = Directory.CreateTempSubdirectory("cf-native").FullName;
        File.WriteAllText(Path.Combine(folder, "Cargo.toml"), "[package]\nname = \"hello-app\"\nversion = \"0.1.0\"\n\n[dependencies]\nname = \"x\"\n");
        var cargo = NativeDebugging.ForProject(folder, Path.GetTempPath())!;
        Assert.Equal("cargo build", cargo.Command);
        Assert.Equal(Path.Combine(folder, "target", "debug", "hello-app.exe"), cargo.Program);
        File.Delete(Path.Combine(folder, "Cargo.toml"));
        File.WriteAllText(Path.Combine(folder, "go.mod"), "module hello\n");
        Assert.Equal("go", NativeDebugging.ForProject(folder, Path.GetTempPath())!.LanguageId);
        File.Delete(Path.Combine(folder, "go.mod"));
        Assert.Null(NativeDebugging.ForProject(folder, Path.GetTempPath()));
    }

    [Fact]
    public void PicksADebuggerForEachLanguage()
    {
        var build = new NativeDebugBuild("", "main.exe", "C:\\work", "cpp");
        string? All(string name) => name + ".exe";
        Assert.Equal("gdb", NativeDebugging.Configuration(build, All)!.AdapterId);
        Assert.Equal("lldb-dap", NativeDebugging.Configuration(build, n => n == "lldb-dap" ? "lldb-dap.exe" : null)!.AdapterId);
        Assert.Equal("lldb-dap", NativeDebugging.Configuration(build with { LanguageId = "rust" }, All)!.AdapterId);
        var go = NativeDebugging.Configuration(build with { LanguageId = "go" }, All)!;
        Assert.True(go.UsesSocket);
        Assert.Equal("exec", (string?)go.Launch["mode"]);
        Assert.Null(NativeDebugging.Configuration(build, _ => null));
        Assert.Contains("dlv", NativeDebugging.InstallHint("go"));
    }

    [Fact]
    public void HidesGdbsPendingBreakpointNotes()
    {
        Assert.True(DebugTools.IsPendingBreakpointNote("No source file named main.c.\n"));
        Assert.True(DebugTools.IsPendingBreakpointNote("Breakpoint 1 (-source main.c -line 6) pending.\n"));
        Assert.False(DebugTools.IsPendingBreakpointNote("Program received signal SIGSEGV\n"));
    }

    [Fact]
    public void ReadsGdbsVersion()
    {
        Assert.Equal(15, NativeDebugging.ParseGdbVersion("GNU gdb (GDB) 15.2\nCopyright (C) 2024"));
        Assert.Equal(12, NativeDebugging.ParseGdbVersion("GNU gdb (GDB for MinGW-W64 x86_64, built by Brecht Sanders) 12.1\n"));
        Assert.Equal(0, NativeDebugging.ParseGdbVersion("not gdb"));
    }

    [Fact]
    public void ReadsDelvesAddress()
    {
        Assert.Equal(("127.0.0.1", 52123), DebugAdapter.ListeningAddress("DAP server listening at: 127.0.0.1:52123"));
        Assert.Equal(("::1", 4000), DebugAdapter.ListeningAddress("DAP server listening at: [::1]:4000"));
        Assert.Null(DebugAdapter.ListeningAddress("Type 'dlv help' for list of commands."));
    }
}

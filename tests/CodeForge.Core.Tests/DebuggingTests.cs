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
}

using System.Text;

namespace CodeForge.Core;

/// <summary>Output from programs that redraw their current line with a carriage return (progress counters, git's percentages).</summary>
public static class ConsoleText
{
    /// <summary>
    /// The new last line after <paramref name="chunk"/> arrives, given the current last line (<paramref name="tail"/>, the text
    /// after the last line break). A carriage return not followed by a line break starts the line over, so only the last
    /// version shows. One at the very end is kept, in case the line break comes in the next chunk.
    /// </summary>
    public static string Rewrite(string tail, string chunk)
    {
        var text = tail + chunk;
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\r') { result.Append(c); continue; }
            if (i + 1 == text.Length) { result.Append(c); continue; }
            if (text[i + 1] == '\n') continue;
            var lineStart = result.ToString().LastIndexOf('\n') + 1;
            result.Length = lineStart;
        }
        return result.ToString();
    }
}

/// <summary>Finds the tool a command needs, to say what to install instead of letting cmd.exe fail with "is not recognized".</summary>
public static class ToolCheck
{
    private static readonly HashSet<string> BuiltIns = new(StringComparer.OrdinalIgnoreCase)
        { "start", "cmd", "echo", "set", "cd", "call", "if", "for", "rmdir", "rd", "del", "copy", "move", "type", "dir", "mkdir", "md" };

    private static readonly Dictionary<string, string> Hints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dotnet"] = "Install the .NET SDK: winget install Microsoft.DotNet.SDK.10",
        ["go"] = "Install Go: winget install GoLang.Go",
        ["cargo"] = "Install Rust: winget install Rustlang.Rustup",
        ["rustc"] = "Install Rust: winget install Rustlang.Rustup",
        ["java"] = "Install a JDK: winget install Microsoft.OpenJDK.21",
        ["node"] = "Install Node.js: winget install OpenJS.NodeJS.LTS",
        ["npm"] = "Install Node.js: winget install OpenJS.NodeJS.LTS",
        ["npx"] = "Install Node.js: winget install OpenJS.NodeJS.LTS",
        ["python"] = "Install Python: winget install Python.Python.3.13",
        ["gcc"] = "Install a C compiler, for example: winget install MSYS2.MSYS2 (then pacman -S mingw-w64-ucrt-x86_64-gcc)",
        ["g++"] = "Install a C++ compiler, for example: winget install MSYS2.MSYS2 (then pacman -S mingw-w64-ucrt-x86_64-gcc)",
        ["cmake"] = "Install CMake: winget install Kitware.CMake",
        ["ruby"] = "Install Ruby: winget install RubyInstallerTeam.Ruby.3.3",
        ["php"] = "Install PHP from windows.php.net.",
        ["swift"] = "Install Swift for Windows from swift.org.",
        ["bash"] = "Install Git for Windows (it includes bash): winget install Git.Git",
        ["lua"] = "Install Lua, for example: winget install DEVCOM.Lua",
    };

    /// <summary>The program a command starts first: "go run ." → "go". Null for cmd.exe's own commands.</summary>
    public static string? Program(string command)
    {
        var text = command.TrimStart();
        string first;
        if (text.StartsWith('"')) { var end = text.IndexOf('"', 1); first = end < 0 ? text[1..] : text[1..end]; }
        else first = text.Split(' ', 2)[0];
        if (first.Length == 0 || BuiltIns.Contains(first)) return null;
        return first;
    }

    public static string? InstallHint(string program) => Hints.GetValueOrDefault(Path.GetFileNameWithoutExtension(program));

    /// <summary>True if the program can be found the way cmd.exe finds it: a path, or a name on PATH with an extension from PATHEXT.</summary>
    public static bool IsAvailable(string program, string? path = null, string? pathExt = null)
    {
        if (program.Contains('\\') || program.Contains('/')) return File.Exists(program);
        path ??= Environment.GetEnvironmentVariable("PATH") ?? "";
        pathExt ??= Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
        var extensions = Path.HasExtension(program) ? [""] : pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries).Prepend("").ToArray();
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                try { if (File.Exists(Path.Combine(folder.Trim('"'), program + extension))) return true; }
                catch (ArgumentException) { }
            }
        }
        return false;
    }
}

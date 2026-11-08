using System.Text;
using System.Text.RegularExpressions;

namespace CodeForge.Core;

/// <summary>Output from programs that redraw their current line with a carriage return (progress counters, git's percentages).</summary>
public static partial class ConsoleText
{
    [GeneratedRegex(@"\u001B(?:\[[0-?]*[ -/]*[@-~]|\][^\u0007\u001B]*(?:\u0007|\u001B\\)|[@-Z\\-_])")]
    private static partial Regex Ansi();

    /// <summary>Removes ANSI escape sequences (colors, cursor moves, window titles), which the Output window can't show.</summary>
    public static string StripAnsi(string text) => text.Contains('\u001B') ? Ansi().Replace(text, "") : text;

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
    public static bool IsAvailable(string program, string? path = null, string? pathExt = null) => Find(program, path, pathExt) != null;

    /// <summary>The full path of a program on PATH (trying PATHEXT's extensions), or null.</summary>
    public static string? Find(string program, string? path = null, string? pathExt = null)
    {
        if (program.Contains('\\') || program.Contains('/')) return File.Exists(program) ? program : null;
        path ??= Environment.GetEnvironmentVariable("PATH") ?? "";
        pathExt ??= Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
        var extensions = Path.HasExtension(program) ? [""] : pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries).Prepend("").ToArray();
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(folder.Trim('"'), program + extension);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
        }
        return null;
    }
}

/// <summary>
/// Splits one output stream into whole lines, so that stdout and stderr, read separately, interleave line by line instead of
/// mid-line (Python, for one, writes a line's text and its line break separately). What follows the last line break waits
/// in <see cref="Pending"/>: the reader shows it after a short pause, so prompts without a line break still appear.
/// </summary>
public sealed class StreamLines
{
    public string Pending { get; private set; } = "";

    /// <summary>The whole lines that <paramref name="chunk"/> completes, with CR LF made LF (empty when there are none yet).</summary>
    public string Add(string chunk)
    {
        var text = Pending + chunk;
        // A CR at the very end may be the first half of a CR LF.
        var end = text.EndsWith('\r') ? text.Length - 1 : text.Length;
        var cut = text.LastIndexOf('\n', Math.Max(end - 1, 0)) + 1;
        if (end == 0 || cut == 0) { Pending = text; return ""; }
        Pending = text[cut..];
        return text[..cut].Replace("\r\n", "\n");
    }

    /// <summary>Everything still waiting (a prompt, or the end of the output).</summary>
    public string Flush()
    {
        var text = Pending.Replace("\r\n", "\n");
        Pending = "";
        return text;
    }
}

/// <summary>Explains the exit codes Windows uses for crashes, which print as large negative numbers.</summary>
public static class ExitCodes
{
    private static readonly Dictionary<uint, string> Known = new()
    {
        [0xE0434352] = "an unhandled .NET exception",
        [0xC0000005] = "an access violation",
        [0xC00000FD] = "a stack overflow",
        [0xC0000409] = "a fail-fast or stack buffer overrun",
        [0xC0000094] = "an integer division by zero",
        [0xC000013A] = "Ctrl+C",
        [0xC0000135] = "a missing DLL",
    };

    /// <summary>"-532462766 (0xE0434352: an unhandled .NET exception)", or the plain number for ordinary codes.</summary>
    public static string Describe(int code)
    {
        if (code is >= 0 and < 0x10000) return code.ToString();
        var unsigned = unchecked((uint)code);
        var hex = $"0x{unsigned:X8}";
        return Known.TryGetValue(unsigned, out var meaning) ? $"{code} ({hex}: {meaning})" : $"{code} ({hex})";
    }
}

/// <summary>Key gestures as menus show them: "Ctrl+OemMinus" → "Ctrl+-".</summary>
public static class KeyNames
{
    private static readonly (string Name, string Shown)[] Keys =
    [
        ("OemTilde", "`"), ("Oem3", "`"), ("OemMinus", "-"), ("OemPlus", "+"), ("OemPeriod", "."), ("OemComma", ","),
        ("OemQuestion", "/"), ("Oem2", "/"), ("OemSemicolon", ";"), ("Oem1", ";"), ("OemQuotes", "'"), ("Oem7", "'"),
        ("OemOpenBrackets", "["), ("Oem4", "["), ("OemCloseBrackets", "]"), ("Oem6", "]"), ("OemPipe", "\\"), ("Oem5", "\\"),
    ];

    public static string Display(string gesture)
    {
        var parts = gesture.Split('+');
        for (var i = 0; i < parts.Length; i++)
            foreach (var (name, shown) in Keys)
                if (parts[i] == name) { parts[i] = shown; break; }
        return string.Join("+", parts);
    }
}

/// <summary>Turns a browser's registered open command into a program and arguments for one address.</summary>
public static class BrowserCommand
{
    /// <summary>
    /// <c>"C:\…\msedge.exe" --single-argument %1</c> and <c>https://x</c> → (<c>C:\…\msedge.exe</c>, <c>--single-argument https://x</c>).
    /// Null when the command is empty.
    /// </summary>
    public static (string Program, string Arguments)? Build(string? command, string address)
    {
        command = command?.Trim();
        if (string.IsNullOrEmpty(command)) return null;
        string program, rest;
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end < 0) return null;
            program = command[1..end];
            rest = command[(end + 1)..].Trim();
        }
        else
        {
            var space = command.IndexOf(' ');
            program = space < 0 ? command : command[..space];
            rest = space < 0 ? "" : command[(space + 1)..].Trim();
        }
        var quoted = address.Contains(' ') ? $"\"{address}\"" : address;
        var arguments = rest.Contains("%1") ? rest.Replace("\"%1\"", quoted).Replace("%1", quoted) : (rest + " " + quoted).Trim();
        return (program, arguments);
    }
}

/// <summary>
/// PATH as it is now: a program started from Explorer keeps the PATH Explorer had when it started, so tools installed
/// since (Node.js with winget) aren't found until sign-out. The saved machine and user PATHs are added to it.
/// </summary>
public static class PathVariable
{
    public static string Merge(string? current, params string?[] saved)
    {
        var entries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in saved.Prepend(current))
        {
            foreach (var entry in (list ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var expanded = Environment.ExpandEnvironmentVariables(entry);
                if (seen.Add(expanded.TrimEnd('\\'))) entries.Add(expanded);
            }
        }
        return string.Join(";", entries);
    }

    /// <summary>Brings this process's PATH up to date (Windows only), so servers, runs and terminals find new tools.</summary>
    public static void Refresh()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            Environment.SetEnvironmentVariable("PATH", Merge(Environment.GetEnvironmentVariable("PATH"),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)));
        }
        catch (Exception) { }
    }
}


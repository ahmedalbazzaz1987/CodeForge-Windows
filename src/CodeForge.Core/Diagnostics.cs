using System.Text.RegularExpressions;

namespace CodeForge.Core;

public enum Severity { Error, Warning }

/// <summary>An error or warning found in a tool's output, for the Error List.</summary>
/// <param name="File">The path as the tool printed it (absolute, or relative to the folder it ran in).</param>
/// <param name="EndLine">Where the problem ends, when a language server said (build output doesn't).</param>
public sealed record Diagnostic(Severity Severity, string Message, string File, int Line, int? Column = null, string? Code = null,
    int? EndLine = null, int? EndColumn = null)
{
    public string FullPath(string folder) =>
        Path.GetFullPath(Path.IsPathRooted(File) ? File : Path.Combine(folder, File));
}

/// <summary>
/// Reads compiler and runtime error formats from plain output: MSBuild and TypeScript (<c>file(line,col): error CODE: message</c>),
/// clang/gcc/go/javac (<c>file:line:col: error: message</c>), Rust, Python tracebacks, Node.js stack traces, and .NET
/// unhandled exceptions and failed tests (the first stack frame with a file and line).
/// Windows paths with drive letters (<c>C:\…</c>) are understood.
/// </summary>
public static partial class DiagnosticParser
{
    [GeneratedRegex(@"^\s*([^\s(][^(\n]*\.\w+)\((\d+),(\d+)(?:,\d+,\d+)?\):\s*(error|warning)\s*([A-Z]+\d+)?:?\s*(.+?)(?:\s+\[[^\]\n]+\])?$")]
    private static partial Regex MSBuild();

    [GeneratedRegex(@"^((?:[A-Za-z]:)?(?=[^:\n]*[./\\])[^\s:\[][^:\n]*?):(\d+):(?:(\d+):)?\s*(?:(fatal error|error|warning|note)\s*(?:\[[^\]]*\])?:)?\s*(.+)$")]
    private static partial Regex Compiler();

    [GeneratedRegex(@"^(error|warning)(?:\[\w+\])?: (.+)$")]
    private static partial Regex RustHeader();

    [GeneratedRegex(@"^\s*--> ((?:[A-Za-z]:)?[^:\n]+):(\d+):(\d+)")]
    private static partial Regex RustLocation();

    [GeneratedRegex(@"^\s*File ""([^""]+)"", line (\d+)")]
    private static partial Regex PythonFrame();

    [GeneratedRegex(@"^((?:[A-Za-z]:)?[\\/][^:\n]*\.[cm]?[jt]sx?):(\d+)$")]
    private static partial Regex NodeLocation();

    [GeneratedRegex(@"^((?:[A-Za-z_][\w.]*)?(?:Error|Exception|Warning|Interrupt|Exit)(?::.*)?)$")]
    private static partial Regex ExceptionLine();

    [GeneratedRegex(@"^Unhandled exception\. (.+)$")]
    private static partial Regex DotNetUnhandled();

    [GeneratedRegex(@"^\s*Failed (\S+) \[")]
    private static partial Regex DotNetFailedTest();

    [GeneratedRegex(@"^\s+at .+ in (.+):line (\d+)\s*$")]
    private static partial Regex DotNetFrame();

    public static List<Diagnostic> Parse(string output)
    {
        var results = new List<Diagnostic>();
        var seen = new HashSet<Diagnostic>();
        void Add(Diagnostic diagnostic)
        {
            if (seen.Add(diagnostic)) results.Add(diagnostic);
        }

        (Severity Severity, string Message)? pendingRust = null;
        (string File, int Line)? pendingFrame = null;   // last Python/Node location, waiting for its exception line
        string? pendingDotNet = null;                    // a .NET exception or failed test, waiting for its first frame with a file
        var testMessageNext = false;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            Match match;
            if ((match = DotNetUnhandled().Match(line)).Success) { pendingDotNet = match.Groups[1].Value; continue; }
            if ((match = DotNetFailedTest().Match(line)).Success) { pendingDotNet = "Test failed: " + match.Groups[1].Value; continue; }
            if (pendingDotNet != null && line.Trim() == "Error Message:") { testMessageNext = true; continue; }
            if (testMessageNext && pendingDotNet != null && line.Trim().Length > 0)
            {
                pendingDotNet += " — " + line.Trim();
                testMessageNext = false;
                continue;
            }
            if (pendingDotNet != null && (match = DotNetFrame().Match(line)).Success)
            {
                Add(new Diagnostic(Severity.Error, pendingDotNet, match.Groups[1].Value, int.Parse(match.Groups[2].Value)));
                pendingDotNet = null;
                continue;
            }
            if ((match = RustHeader().Match(line)).Success)
            {
                pendingRust = (match.Groups[1].Value == "warning" ? Severity.Warning : Severity.Error, match.Groups[2].Value);
                continue;
            }
            if (pendingRust is { } rust && (match = RustLocation().Match(line)).Success)
            {
                Add(new Diagnostic(rust.Severity, rust.Message, match.Groups[1].Value, int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value)));
                pendingRust = null;
                continue;
            }
            if ((match = PythonFrame().Match(line)).Success || (match = NodeLocation().Match(line)).Success)
            {
                pendingFrame = (match.Groups[1].Value, int.Parse(match.Groups[2].Value));
                continue;
            }
            if (pendingFrame is { } frame && (match = ExceptionLine().Match(line)).Success)
            {
                Add(new Diagnostic(Severity.Error, match.Groups[1].Value, frame.File, frame.Line));
                pendingFrame = null;
                continue;
            }
            if ((match = MSBuild().Match(line)).Success)
            {
                Add(new Diagnostic(match.Groups[4].Value == "warning" ? Severity.Warning : Severity.Error, match.Groups[6].Value,
                                   match.Groups[1].Value, int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value),
                                   match.Groups[5].Success ? match.Groups[5].Value : null));
                continue;
            }
            if ((match = Compiler().Match(line)).Success)
            {
                var kind = match.Groups[4].Value;
                if (kind == "note") continue;
                Add(new Diagnostic(kind == "warning" ? Severity.Warning : Severity.Error, match.Groups[5].Value, match.Groups[1].Value,
                                   int.Parse(match.Groups[2].Value), match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : null));
            }
        }
        return results;
    }
}

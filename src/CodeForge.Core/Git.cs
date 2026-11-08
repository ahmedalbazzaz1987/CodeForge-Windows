using System.Diagnostics;
using System.Text;

namespace CodeForge.Core;

/// <summary>One changed file from <c>git status --porcelain=v1 --branch</c>.</summary>
/// <param name="Staged">Index status letter: M, A, D, R, ? or a space.</param>
/// <param name="Unstaged">Work-tree status letter.</param>
public sealed record GitChange(char Staged, char Unstaged, string Path, string? OriginalPath = null)
{
    public bool IsStaged => Staged != ' ' && Staged != '?';
    public bool IsUntracked => Staged == '?';
    public bool IsConflicted => Staged == 'U' || Unstaged == 'U' || (Staged == 'A' && Unstaged == 'A') || (Staged == 'D' && Unstaged == 'D');

    /// <summary>The letter shown in the list: the staged change if any, otherwise the work-tree one.</summary>
    public string Letter => IsUntracked ? "U" : IsConflicted ? "!" : (Staged != ' ' ? Staged : Unstaged).ToString();
}

public sealed class GitStatus
{
    public string Branch { get; set; } = "";
    public string? Upstream { get; set; }
    public int Ahead { get; set; }
    public int Behind { get; set; }
    public List<GitChange> Changes { get; } = [];

    /// <summary>Parses <c>git status --porcelain=v1 --branch</c>.</summary>
    public static GitStatus Parse(string output)
    {
        var status = new GitStatus();
        foreach (var line in output.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length == 0) continue;
            if (line.StartsWith("## "))
            {
                var header = line[3..];
                var bracket = header.IndexOf(" [", StringComparison.Ordinal);
                if (bracket >= 0)
                {
                    foreach (var part in header[(bracket + 2)..].TrimEnd(']').Split(", "))
                    {
                        var words = part.Split(' ');
                        if (words.Length == 2 && int.TryParse(words[1], out var number))
                        {
                            if (words[0] == "ahead") status.Ahead = number;
                            else if (words[0] == "behind") status.Behind = number;
                        }
                    }
                    header = header[..bracket];
                }
                foreach (var prefix in new[] { "No commits yet on ", "Initial commit on " })
                    if (header.StartsWith(prefix)) header = header[prefix.Length..];
                var parts = header.Split("...");
                status.Branch = parts[0];
                status.Upstream = parts.Length > 1 ? parts[1] : null;
                continue;
            }
            if (line.Length <= 3) continue;
            var path = line[3..];
            string? original = null;
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                original = Unquote(path[..arrow]);
                path = path[(arrow + 4)..];
            }
            status.Changes.Add(new GitChange(line[0], line[1], Unquote(path), original));
        }
        return status;
    }

    /// <summary>Git quotes paths with spaces or special characters: "my file.txt".</summary>
    public static string Unquote(string path)
    {
        if (path.Length < 2 || path[0] != '"' || path[^1] != '"') return path;
        return path[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}

/// <summary>Who last changed a line, from <c>git blame --line-porcelain</c>.</summary>
public sealed record GitBlameLine(string Commit, string Author, DateTimeOffset Time, string Summary)
{
    /// <summary>Lines that aren't committed yet have an all-zero commit hash.</summary>
    public bool IsUncommitted => Commit.All(c => c == '0');
}

public static class GitBlame
{
    /// <summary>Blame information by 1-based line number.</summary>
    public static Dictionary<int, GitBlameLine> Parse(string output)
    {
        var result = new Dictionary<int, GitBlameLine>();
        string commit = "", author = "", summary = "";
        var time = DateTimeOffset.UnixEpoch;
        var line = 0;
        foreach (var raw in output.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.StartsWith('\t'))
            {
                if (line > 0) result[line] = new GitBlameLine(commit, author, time, summary);
                continue;
            }
            var space = raw.IndexOf(' ');
            var key = space < 0 ? raw : raw[..space];
            var value = space < 0 ? "" : raw[(space + 1)..];
            if (key.Length == 40 && key.All(Uri.IsHexDigit))
            {
                commit = key;
                var numbers = value.Split(' ');
                line = numbers.Length >= 2 && int.TryParse(numbers[1], out var number) ? number : 0;
            }
            else if (key == "author") author = value;
            else if (key == "author-time" && long.TryParse(value, out var seconds)) time = DateTimeOffset.FromUnixTimeSeconds(seconds);
            else if (key == "summary") summary = value;
        }
        return result;
    }

    /// <summary>The most recent change among some lines, for CodeLens on a method.</summary>
    public static GitBlameLine? Latest(Dictionary<int, GitBlameLine> blame, int first, int last) =>
        Enumerable.Range(first, Math.Max(last - first + 1, 0)).Select(l => blame.GetValueOrDefault(l)).OfType<GitBlameLine>().MaxBy(b => b.Time);
}

public static class RelativeTime
{
    /// <summary>"just now", "5 minutes ago", "yesterday", "3 weeks ago"…</summary>
    public static string Describe(DateTimeOffset date, DateTimeOffset? now = null)
    {
        var seconds = (long)((now ?? DateTimeOffset.Now) - date).TotalSeconds;
        static string Plural(long value, string unit) => $"{value} {unit}{(value == 1 ? "" : "s")} ago";
        return seconds switch
        {
            < 60 => "just now",
            < 3600 => Plural(seconds / 60, "minute"),
            < 86_400 => Plural(seconds / 3600, "hour"),
            < 172_800 => "yesterday",
            < 1_209_600 => Plural(seconds / 86_400, "day"),
            < 2_592_000 => Plural(seconds / 604_800, "week"),
            < 31_557_600 => Plural(Math.Max(seconds / 2_629_800, 1), "month"),
            _ => Plural(seconds / 31_557_600, "year"),
        };
    }
}

/// <summary>The result of one git command.</summary>
public sealed record GitResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;
    /// <summary>What to show when it failed: git's own message.</summary>
    public string Message => (Error.Trim().Length > 0 ? Error : Output).Trim() is { Length: > 0 } text ? text : $"Exit code {ExitCode}";
}

/// <summary>Runs git in a folder, in UTF-8, never waiting for a password nobody can type.</summary>
public static class GitCommand
{
    public static string? Program() => ToolCheck.Find("git");

    public static GitResult Run(string folder, params string[] arguments)
    {
        if (Program() is not { } git) return new GitResult(-1, "", "Git isn't installed. Install it with: winget install Git.Git");
        var info = new ProcessStartInfo(git)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = folder,
        };
        // Paths with Arabic letters come out as they are, not as "\330\247…".
        foreach (var argument in new[] { "-c", "core.quotepath=false", "-c", "i18n.logOutputEncoding=UTF-8", "-c", "color.ui=false" }.Concat(arguments))
            info.ArgumentList.Add(argument);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.Environment["LC_ALL"] = "C.UTF-8";
        try
        {
            using var process = Process.Start(info)!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return new GitResult(process.ExitCode, output, error.Result);
        }
        catch (Exception exception)
        {
            return new GitResult(-1, "", exception.Message);
        }
    }

    public static Task<GitResult> RunAsync(string folder, params string[] arguments) => Task.Run(() => Run(folder, arguments));
}

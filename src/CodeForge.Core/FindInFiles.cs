using System.Text.RegularExpressions;

namespace CodeForge.Core;

/// <summary>Edit ▸ Find and Replace ▸ Find in Files: the options in Visual Studio's dialog.</summary>
/// <param name="FileTypes">Patterns separated by ; or , ("*.vb; *.cs"). Empty or *.* means all files.</param>
public sealed record SearchOptions(bool MatchCase = false, bool WholeWord = false, bool RegularExpression = false, string FileTypes = "");

/// <param name="Line">1-based.</param>
/// <param name="Column">1-based.</param>
/// <param name="Preview">The whole line, without its line break.</param>
public sealed record SearchMatch(string Path, int Line, int Column, int Length, string Preview);

public static class FileSearch
{
    /// <summary>Folders that hold build output, dependencies or tool data rather than the project's own code.</summary>
    public static readonly IReadOnlySet<string> SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".git", ".svn", ".hg", ".vs", ".idea", "node_modules", "bin", "obj", "build", "dist", "__pycache__", ".venv", "venv", "target", "packages" };

    /// <summary>The expression for a query, or null when it's empty or an invalid regular expression.</summary>
    public static Regex? Expression(string query, SearchOptions options)
    {
        if (query.Length == 0) return null;
        var pattern = options.RegularExpression ? query : Regex.Escape(query);
        if (options.WholeWord) pattern = @"(?<![\p{L}\p{N}_])(?:" + pattern + @")(?![\p{L}\p{N}_])";
        try
        {
            return new Regex(pattern, (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>The files a search looks at, in a stable order.</summary>
    public static List<string> Files(string root, string fileTypes = "")
    {
        var patterns = fileTypes.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p != "*.*" && p != "*").Select(WildcardToRegex).ToList();
        var result = new List<string>();
        void Walk(string folder)
        {
            try
            {
                foreach (var file in Directory.GetFiles(folder))
                {
                    var info = new FileInfo(file);
                    if (info.Attributes.HasFlag(FileAttributes.Hidden) || info.Length >= 5_000_000) continue;
                    if (patterns.Count == 0 || patterns.Any(p => p.IsMatch(info.Name))) result.Add(file);
                }
                foreach (var sub in Directory.GetDirectories(folder))
                {
                    var name = Path.GetFileName(sub);
                    if (!SkippedFolders.Contains(name) && !name.StartsWith('.')) Walk(sub);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        Walk(root);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static Regex WildcardToRegex(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase);

    /// <summary>A file's text, or null for binary files and files that can't be read.</summary>
    public static string? ReadText(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0)) return null;
            using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Every match in one text.</summary>
    public static List<SearchMatch> Matches(string text, string path, Regex expression)
    {
        var result = new List<SearchMatch>();
        int line = 1, lineStart = 0, scanned = 0;
        try
        {
            foreach (Match match in expression.Matches(text))
            {
                if (match.Length == 0) continue;
                for (; scanned < match.Index; scanned++)
                {
                    if (text[scanned] != '\n') continue;
                    line++;
                    lineStart = scanned + 1;
                }
                var end = text.IndexOf('\n', lineStart);
                var preview = (end < 0 ? text[lineStart..] : text[lineStart..end]).TrimEnd('\r');
                result.Add(new SearchMatch(path, line, match.Index - lineStart + 1, match.Length, preview));
            }
        }
        catch (RegexMatchTimeoutException) { }   // a pathological pattern: keep what was found
        return result;
    }

    /// <summary>Searches a folder. <paramref name="limit"/> stops runaway searches (like one letter in a big project).</summary>
    public static List<SearchMatch> Search(string query, string root, SearchOptions options, int limit = 5000)
    {
        var result = new List<SearchMatch>();
        if (Expression(query, options) is not { } expression) return result;
        foreach (var file in Files(root, options.FileTypes))
        {
            if (ReadText(file) is not { } text) continue;
            result.AddRange(Matches(text, file, expression));
            if (result.Count >= limit) return result.GetRange(0, limit);
        }
        return result;
    }

    /// <summary>Replaces every match. With a regular expression, $1 in the replacement is the first group; otherwise it's literal.</summary>
    public static (string Text, int Count) Replace(string text, Regex expression, string replacement, bool regularExpression)
    {
        var count = 0;
        var updated = expression.Replace(text, match =>
        {
            count++;
            return regularExpression ? match.Result(replacement) : replacement;
        });
        return (updated, count);
    }
}

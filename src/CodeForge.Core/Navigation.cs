using System.Text.RegularExpressions;

namespace CodeForge.Core;

/// <summary>A comment that says there's work left: Visual Studio's Task List tokens plus a few common ones.</summary>
public sealed record TaskComment(string Path, int Line, string Token, string Text);

/// <summary>View ▸ Task List: finds TODO, HACK and similar comments. Strings and ordinary code don't count.</summary>
public static partial class TaskScanner
{
    [GeneratedRegex(@"\b(TODO|FIXME|HACK|UNDONE|BUG|XXX)\b:?[ \t]*(.*)$")]
    private static partial Regex Token();

    /// <summary>
    /// The comment on a line, if any. Quotes are followed so that a marker inside a string isn't taken for a comment.
    /// Block comments that span lines count from the line they start on.
    /// </summary>
    public static List<TaskComment> Tasks(string text, string path, Language language)
    {
        var result = new List<TaskComment>();
        var inBlock = false;
        var lines = text.Split('\n');
        for (var number = 0; number < lines.Length; number++)
        {
            var line = lines[number].TrimEnd('\r');
            var comment = CommentText(line, language, ref inBlock);
            if (comment == null) continue;
            var match = Token().Match(comment);
            if (!match.Success) continue;
            var note = match.Groups[2].Value.Trim();
            if (language.BlockEnd != null && note.EndsWith(language.BlockEnd)) note = note[..^language.BlockEnd.Length].TrimEnd();
            result.Add(new TaskComment(path, number + 1, match.Groups[1].Value, note));
        }
        return result;
    }

    private static string? CommentText(string line, Language language, ref bool inBlock)
    {
        var markup = language.LineComment == null && language.BlockStart == null;
        if (markup) return line.Contains("<!--") ? line : null;   // HTML and XML comments
        var comment = new System.Text.StringBuilder();
        char quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            if (inBlock)
            {
                if (language.BlockEnd != null && string.CompareOrdinal(line, i, language.BlockEnd, 0, language.BlockEnd.Length) == 0)
                {
                    inBlock = false;
                    i += language.BlockEnd.Length - 1;
                    continue;
                }
                comment.Append(line[i]);
                continue;
            }
            if (quote != '\0')
            {
                if (line[i] == '\\' && !language.CaseInsensitive) i++;
                else if (line[i] == quote) quote = '\0';
                continue;
            }
            if (language.LineComment != null && string.Compare(line, i, language.LineComment, 0, language.LineComment.Length,
                    language.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == 0)
            {
                comment.Append(line[(i + language.LineComment.Length)..]);
                break;
            }
            if (language.BlockStart != null && string.CompareOrdinal(line, i, language.BlockStart, 0, language.BlockStart.Length) == 0)
            {
                inBlock = true;
                i += language.BlockStart.Length - 1;
                continue;
            }
            if (language.Strings.Contains(line[i]) && !(language.LineComment == "'" && line[i] == '\'')) quote = line[i];
        }
        return comment.Length > 0 ? comment.ToString() : null;
    }

    /// <summary>Every task in a folder's source files, in file and line order.</summary>
    public static List<TaskComment> InFolder(string folder, int limit = 2000)
    {
        var result = new List<TaskComment>();
        foreach (var file in FileSearch.Files(folder))
        {
            var language = Languages.ForFile(file);
            if (language == Languages.PlainText || FileSearch.ReadText(file) is not { } text) continue;
            result.AddRange(Tasks(text, file, language));
            if (result.Count >= limit) break;
        }
        return result;
    }
}

public static class Bookmarks
{
    /// <summary>The next bookmark after a position (or before it, going backward), wrapping across files in order.</summary>
    public static (string Path, int Line)? Next((string Path, int Line) place, IReadOnlyDictionary<string, List<int>> bookmarks, bool backward)
    {
        static int Compare((string Path, int Line) a, (string Path, int Line) b)
        {
            var order = string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            return order != 0 ? order : a.Line.CompareTo(b.Line);
        }
        var all = bookmarks.SelectMany(pair => pair.Value.Select(line => (pair.Key, line))).ToList();
        if (all.Count == 0) return null;
        all.Sort(Compare);
        if (backward)
        {
            for (var i = all.Count - 1; i >= 0; i--) if (Compare(all[i], place) < 0) return all[i];
            return all[^1];
        }
        foreach (var bookmark in all) if (Compare(bookmark, place) > 0) return bookmark;
        return all[0];
    }
}

/// <summary>View ▸ Navigate Backward and Forward: where you've been, like a browser's history.</summary>
public sealed class NavigationHistory<TPlace>(int limit = 100) where TPlace : IEquatable<TPlace>
{
    private readonly List<TPlace> back = [];
    private readonly List<TPlace> forward = [];

    public bool CanGoBack => back.Count > 0;
    public bool CanGoForward => forward.Count > 0;

    /// <summary>Call before jumping somewhere new. A new jump clears the forward list, as in a browser.</summary>
    public void Record(TPlace place)
    {
        if (back.Count == 0 || !back[^1].Equals(place)) back.Add(place);
        if (back.Count > limit) back.RemoveRange(0, back.Count - limit);
        forward.Clear();
    }

    public bool TryGoBack(TPlace current, out TPlace? place)
    {
        while (back.Count > 0)
        {
            place = back[^1];
            back.RemoveAt(back.Count - 1);
            if (place.Equals(current)) continue;
            forward.Add(current);
            return true;
        }
        place = default;
        return false;
    }

    public bool TryGoForward(TPlace current, out TPlace? place)
    {
        place = default;
        if (forward.Count == 0) return false;
        place = forward[^1];
        forward.RemoveAt(forward.Count - 1);
        back.Add(current);
        return true;
    }
}

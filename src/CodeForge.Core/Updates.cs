using System.Text.Json;

namespace CodeForge.Core;

/// <summary>A version number like 0.10.2; compared number by number (0.10 is newer than 0.9).</summary>
public sealed record AppVersion(int[] Parts) : IComparable<AppVersion>
{
    public static AppVersion? Parse(string text)
    {
        var value = text.Trim().TrimStart('v', 'V');
        var dash = value.IndexOf('-');
        if (dash >= 0) value = value[..dash];
        var pieces = value.Split('.');
        var parts = new int[pieces.Length];
        for (var i = 0; i < pieces.Length; i++)
            if (!int.TryParse(pieces[i], out parts[i])) return null;
        return parts.Length == 0 ? null : new AppVersion(parts);
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        for (var i = 0; i < Math.Max(Parts.Length, other.Parts.Length); i++)
        {
            int a = i < Parts.Length ? Parts[i] : 0, b = i < other.Parts.Length ? other.Parts[i] : 0;
            if (a != b) return a.CompareTo(b);
        }
        return 0;
    }

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
    public override string ToString() => string.Join('.', Parts);
    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;
    public override int GetHashCode() => ToString().GetHashCode();
}

/// <summary>A GitHub release that the updater can install: the zip and its SHA-256 checksum file.</summary>
/// <param name="SetupUrl">The installer (CodeForge-x.y.z-setup.exe), which installed copies update with; the zip is for portable copies.</param>
public sealed record ReleaseInfo(AppVersion Version, string Notes, string ZipUrl, string ChecksumUrl, string PageUrl,
                                 string? SetupUrl = null, string? SetupChecksumUrl = null)
{
    /// <summary>Reads GitHub's "latest release" JSON. Returns null when the release has no CodeForge-x.y.z-windows.zip.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (AppVersion.Parse(root.GetProperty("tag_name").GetString() ?? "") is not { } version) return null;
        string? zip = null, checksum = null, setup = null, setupChecksum = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name.EndsWith("-windows.zip", StringComparison.OrdinalIgnoreCase)) zip = url;
            if (name.EndsWith("-windows.zip.sha256", StringComparison.OrdinalIgnoreCase)) checksum = url;
            if (name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase)) setup = url;
            if (name.EndsWith("-setup.exe.sha256", StringComparison.OrdinalIgnoreCase)) setupChecksum = url;
        }
        if (zip == null || checksum == null) return null;
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        var page = root.TryGetProperty("html_url", out var html) ? html.GetString() ?? "" : "";
        return new ReleaseInfo(version, notes, zip, checksum, page, setup, setupChecksum);
    }

    /// <summary>The hash in a "&lt;hex&gt;  file.zip" checksum file.</summary>
    public static string? ChecksumValue(string text)
    {
        var first = text.Trim().Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is { Length: 64 } && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }
}

/// <summary>Quick Open's matching: the query's letters in order, with a bonus for word starts and runs.</summary>
public static class FuzzyMatch
{
    public static int? Score(string query, string candidate)
    {
        if (query.Length == 0) return 0;
        int score = 0, position = 0, run = 0;
        foreach (var q in query.ToLowerInvariant())
        {
            var found = candidate.IndexOf(q.ToString(), position, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return null;
            run = found == position ? run + 1 : 0;
            var wordStart = found == 0 || !char.IsLetterOrDigit(candidate[found - 1]) || (char.IsUpper(candidate[found]) && char.IsLower(candidate[found - 1]));
            score += 1 + run * 2 + (wordStart ? 3 : 0) - Math.Min(found - position, 3);
            position = found + 1;
        }
        return score;
    }
}

public static class ReleaseNotes
{
    /// <summary>
    /// Joins the lines of a paragraph or list item that were wrapped in the changelog, as Markdown does, so the notes
    /// read as flowing text in the update message. Code fences are left as they are.
    /// </summary>
    public static string Unwrapped(string markdown)
    {
        var lines = new List<string>();
        var inCode = false;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```")) { inCode = !inCode; lines.Add(line); continue; }
            var startsBlock = trimmed.Length == 0 || trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith('#') ||
                              System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+\. ");
            if (!inCode && !startsBlock && lines.Count > 0 && lines[^1].Trim() is { Length: > 0 } previous &&
                !previous.StartsWith('#') && !previous.StartsWith("```"))
                lines[^1] += " " + trimmed;
            else
                lines.Add(line);
        }
        return string.Join("\n", lines);
    }
}

namespace CodeForge.Core;

/// <summary>
/// The names picked from the completion list lately, per language, so the list offers them first next time (Visual Studio
/// does the same). Kept for the session.
/// </summary>
public sealed class CompletionHistory
{
    private const int Limit = 200;
    private readonly Dictionary<string, List<string>> recent = [];

    public void Used(string languageId, string label)
    {
        if (label.Length == 0) return;
        if (!recent.TryGetValue(languageId, out var list)) recent[languageId] = list = [];
        list.Remove(label);
        list.Insert(0, label);
        if (list.Count > Limit) list.RemoveAt(list.Count - 1);
    }

    /// <summary>How recently a name was picked: 0 if not lately, up to <see cref="Limit"/> for the last one.</summary>
    public int Recency(string languageId, string label)
    {
        if (!recent.TryGetValue(languageId, out var list)) return 0;
        var index = list.IndexOf(label);
        return index < 0 ? 0 : Limit - index;
    }
}

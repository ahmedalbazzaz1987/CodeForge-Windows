using System.IO;
using System.Text.Json;

namespace CodeForge;

/// <summary>Preferences and the last session, saved as JSON in %APPDATA%\CodeForge\settings.json.</summary>
public sealed class Settings
{
    public string Theme { get; set; } = "system";        // system, light, dark, blue
    public double FontSize { get; set; } = 14;
    public bool CheckForUpdates { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; }
    public bool RestoreSession { get; set; } = true;
    public string Configuration { get; set; } = "Debug";
    public string? Workspace { get; set; }
    public List<string> OpenFiles { get; set; } = [];
    public string? SelectedFile { get; set; }
    public List<string> RecentFolders { get; set; } = [];
    public double ExplorerWidth { get; set; } = 280;
    public double ToolboxWidth { get; set; } = 220;
    public double PanelHeight { get; set; } = 220;
    public bool ToolboxVisible { get; set; } = true;
    public bool ExplorerVisible { get; set; } = true;
    public bool PanelVisible { get; set; } = true;
    /// <summary>Custom run commands per project folder (Debug ▸ Set Run Command).</summary>
    public Dictionary<string, string> RunCommands { get; set; } = [];
    /// <summary>Bookmarked lines per file (Edit ▸ Bookmarks).</summary>
    public Dictionary<string, List<int>> Bookmarks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Breakpoint lines per file (Debug ▸ Toggle Breakpoint, or a click in the margin).</summary>
    public Dictionary<string, List<int>> Breakpoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double[]? WindowBounds { get; set; }
    public bool WindowMaximized { get; set; }

    public static string Folder { get; } = Environment.GetEnvironmentVariable("CF_SETTINGS_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodeForge");
    private static string FilePath => Path.Combine(Folder, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Settings Current { get; } = Load();

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception) { }   // a damaged file just means default settings
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (IOException) { }
    }

    public void AddRecent(string folder)
    {
        RecentFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, folder);
        if (RecentFolders.Count > 12) RecentFolders.RemoveRange(12, RecentFolders.Count - 12);
        Save();
    }
}

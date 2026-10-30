using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace CodeForge;

/// <summary>Visual Studio's color themes. Controls use WPF's Fluent style (light or dark); CodeForge's own surfaces use
/// these brushes, looked up with DynamicResource so a theme change repaints everything.</summary>
public static class Themes
{
    public sealed record Palette(bool IsDark,
        string Window, string Panel, string Chrome, string Border, string Text, string SubtleText, string Accent,
        string Editor, string EditorText, string LineNumbers, string CurrentLine, string Selection, string TabSelected);

    private static readonly Dictionary<string, Palette> Palettes = new()
    {
        ["light"] = new(false, "#EEEEF2", "#F5F5F5", "#EEEEF2", "#CCCEDB", "#1E1E1E", "#6D6D6D", "#007ACC",
                        "#FFFFFF", "#1E1E1E", "#2B91AF", "#0F000000", "#ADD6FF", "#FFFFFF"),
        ["dark"] = new(true, "#2D2D30", "#252526", "#2D2D30", "#3F3F46", "#F1F1F1", "#9D9D9D", "#007ACC",
                       "#1E1E1E", "#D4D4D4", "#858585", "#14FFFFFF", "#264F78", "#1E1E1E"),
        ["blue"] = new(false, "#CCD5F0", "#F0F3FA", "#CCD5F0", "#8E9BBC", "#1E1E1E", "#4D5A7A", "#0E639C",
                       "#FFFFFF", "#1E1E1E", "#2B91AF", "#0F000000", "#ADD6FF", "#FFFFFF"),
    };

    public static string[] Names { get; } = ["system", "light", "dark", "blue"];
    public static string Title(string name) => name switch
    {
        "system" => "Use system setting (Light or Dark)",
        "light" => "Light",
        "dark" => "Dark",
        "blue" => "Blue",
        _ => name,
    };

    public static Palette Current { get; private set; } = Palettes["light"];
    public static event Action? Changed;

    /// <summary>Windows' "Choose your app mode" setting.</summary>
    public static bool SystemUsesDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    public static void Apply(string name)
    {
        var resolved = name == "system" ? (SystemUsesDarkMode() ? "dark" : "light") : name;
        Current = Palettes.GetValueOrDefault(resolved, Palettes["light"]);
#pragma warning disable WPF0001   // ThemeMode is the Fluent style; it's marked experimental
        Application.Current.ThemeMode = Current.IsDark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        var resources = Application.Current.Resources;
        void Set(string key, string color) => resources[key] = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));
        Set("WindowBrush", Current.Window);
        Set("PanelBrush", Current.Panel);
        Set("ChromeBrush", Current.Chrome);
        Set("BorderBrush", Current.Border);
        Set("TextBrush", Current.Text);
        Set("SubtleTextBrush", Current.SubtleText);
        Set("AccentBrush", Current.Accent);
        Set("EditorBrush", Current.Editor);
        Set("EditorTextBrush", Current.EditorText);
        Set("LineNumberBrush", Current.LineNumbers);
        Set("TabSelectedBrush", Current.TabSelected);
        Set("ErrorBrush", Current.IsDark ? "#F14C4C" : "#E51400");
        Set("WarningBrush", Current.IsDark ? "#CCA700" : "#BF8803");
        Changed?.Invoke();
    }

    public static Brush Brush(string color) => Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

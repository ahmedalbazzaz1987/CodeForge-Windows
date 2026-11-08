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
        string Editor, string EditorText, string LineNumbers, string CurrentLine, string Selection, string TabSelected,
        string Comment, string String, string Keyword, string Type, string Number, string Function);

    private static readonly Dictionary<string, Palette> Palettes = new()
    {
        // The default look: calm neutrals, one indigo accent, and softer syntax colors.
        ["light"] = new(false, "#F1F3F6", "#F8F9FB", "#F1F3F6", "#E2E5EA", "#1F2937", "#6B7280", "#4F5BD5",
                        "#FFFFFF", "#1F2937", "#9CA3AF", "#0F000000", "#C7D2FE", "#FFFFFF",
                        "#6B7280", "#047857", "#6D3FC9", "#0E7490", "#B45309", "#2563EB"),
        ["dark"] = new(true, "#22252C", "#1B1E24", "#22252C", "#2E323B", "#E4E7EC", "#9097A3", "#6E7BF2",
                       "#16181D", "#E4E7EC", "#5A6270", "#12FFFFFF", "#33407A", "#16181D",
                       "#6B7686", "#8FD4A6", "#B69CFF", "#6FD3E8", "#F2B872", "#7FB4FF"),
        // The second dark option: neutral graphite grays with a soft teal accent.
        ["gray"] = new(true, "#3B3C41", "#333438", "#3B3C41", "#4A4B50", "#DADBDD", "#A3A5AA", "#3E9C92",
                       "#2B2C2F", "#DADBDD", "#6F7176", "#14FFFFFF", "#4A5A5C", "#2B2C2F",
                       "#85878C", "#A8C992", "#8FBBD9", "#7CC7BC", "#DDB779", "#D8C48F"),
        ["midnight"] = new(true, "#1A2540", "#131D33", "#1A2540", "#27344F", "#E2E8F0", "#94A3B8", "#2F8FD0",
                           "#0F172A", "#E2E8F0", "#475569", "#12FFFFFF", "#264B73", "#0F172A",
                           "#64748B", "#86EFAC", "#C4B5FD", "#67E8F9", "#FDBA74", "#7DD3FC"),
        // Visual Studio's own colors.
        ["vs-light"] = new(false, "#EEEEF2", "#F5F5F5", "#EEEEF2", "#CCCEDB", "#1E1E1E", "#6D6D6D", "#007ACC",
                           "#FFFFFF", "#1E1E1E", "#2B91AF", "#0F000000", "#ADD6FF", "#FFFFFF",
                           "#008000", "#A31515", "#0000FF", "#2B91AF", "#098658", "#74531F"),
        ["vs-dark"] = new(true, "#2D2D30", "#252526", "#2D2D30", "#3F3F46", "#F1F1F1", "#9D9D9D", "#007ACC",
                          "#1E1E1E", "#D4D4D4", "#858585", "#14FFFFFF", "#264F78", "#1E1E1E",
                          "#6A9955", "#CE9178", "#569CD6", "#4EC9B0", "#B5CEA8", "#DCDCAA"),
        ["blue"] = new(false, "#CCD5F0", "#F0F3FA", "#CCD5F0", "#8E9BBC", "#1E1E1E", "#4D5A7A", "#0E639C",
                       "#FFFFFF", "#1E1E1E", "#2B91AF", "#0F000000", "#ADD6FF", "#FFFFFF",
                       "#008000", "#A31515", "#0000FF", "#2B91AF", "#098658", "#74531F"),
        // The same extra themes as the Mac version.
        ["high-contrast"] = new(true, "#000000", "#000000", "#000000", "#6FC3DF", "#FFFFFF", "#D0D0D0", "#1AEBFF",
                                "#000000", "#FFFFFF", "#FFFFFF", "#30FFFFFF", "#264F78", "#000000",
                                "#7CA668", "#FFD68F", "#569CD6", "#4EC9B0", "#B5CEA8", "#DCDCAA"),
        ["monokai"] = new(true, "#1E1F1C", "#22231F", "#1E1F1C", "#3E3D32", "#F8F8F2", "#90908A", "#A6E22E",
                          "#272822", "#F8F8F2", "#90908A", "#12FFFFFF", "#49483E", "#272822",
                          "#75715E", "#E6DB74", "#F92672", "#66D9EF", "#AE81FF", "#A6E22E"),
        ["dracula"] = new(true, "#191A21", "#21222C", "#191A21", "#44475A", "#F8F8F2", "#8A93B8", "#BD93F9",
                          "#282A36", "#F8F8F2", "#6272A4", "#12FFFFFF", "#44475A", "#282A36",
                          "#6272A4", "#F1FA8C", "#FF79C6", "#8BE9FD", "#BD93F9", "#50FA7B"),
        ["solarized-dark"] = new(true, "#073642", "#073642", "#073642", "#0F4B5A", "#93A1A1", "#6C8087", "#268BD2",
                                 "#002B36", "#93A1A1", "#586E75", "#12FFFFFF", "#274642", "#002B36",
                                 "#586E75", "#2AA198", "#859900", "#B58900", "#D33682", "#268BD2"),
        ["solarized-light"] = new(false, "#EEE8D5", "#EEE8D5", "#EEE8D5", "#D3CBB7", "#586E75", "#839496", "#268BD2",
                                  "#FDF6E3", "#586E75", "#93A1A1", "#0F000000", "#E4DCC4", "#FDF6E3",
                                  "#93A1A1", "#2AA198", "#859900", "#B58900", "#D33682", "#268BD2"),
    };

    public static string[] Names { get; } =
        ["system", "light", "dark", "gray", "midnight", "vs-light", "vs-dark", "blue", "high-contrast", "monokai", "dracula", "solarized-dark", "solarized-light"];
    public static string Title(string name) => name switch
    {
        "system" => "Use system setting (Modern Light or Dark)",
        "light" => "Modern Light",
        "dark" => "Modern Dark",
        "gray" => "Gray",
        "midnight" => "Midnight",
        "vs-light" => "Visual Studio Light",
        "vs-dark" => "Visual Studio Dark",
        "blue" => "Blue",
        "high-contrast" => "High Contrast",
        "monokai" => "Monokai",
        "dracula" => "Dracula",
        "solarized-dark" => "Solarized Dark",
        "solarized-light" => "Solarized Light",
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

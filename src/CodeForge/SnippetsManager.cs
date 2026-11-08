using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodeForge.Core;

namespace CodeForge;

/// <summary>
/// Tools ▸ Code Snippets Manager: browse, add, change and delete the snippets of each language (type a shortcut and
/// press Tab, or pick it from the Toolbox or the completion list). Changes are saved as you type.
/// </summary>
public static class SnippetsManager
{
    public static string FilePath => Path.Combine(Settings.Folder, "snippets.json");

    public static void Load() => Snippets.Library = SnippetLibrary.Load(FilePath);

    public static void Show(Window owner, string? languageId, Action changed)
    {
        var window = DialogKit.Make(owner, "Code Snippets Manager", 860, 560);
        window.ResizeMode = ResizeMode.CanResize;
        window.MinWidth = 640;
        window.MinHeight = 400;

        var languages = new ComboBox { Width = 220, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        var withSnippets = Languages.All.Where(l => Snippets.BuiltIn.Any(s => s.LanguageId == l.Id) || Snippets.All.Any(s => s.LanguageId == l.Id)).ToList();
        foreach (var language in withSnippets) languages.Items.Add(new ComboBoxItem { Content = language.Name, Tag = language.Id });
        var list = new ListBox { MinWidth = 240 };
        var title = new TextBox();
        var shortcut = new TextBox();
        var body = new TextBox
        {
            AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap,
        };
        var origin = new TextBlock { Opacity = 0.6, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        var add = new Button { Content = "Add", MinWidth = 80 };
        var remove = new Button { Content = "Delete", MinWidth = 80, Margin = new Thickness(6, 0, 0, 0) };
        var restore = new Button { Content = "Restore Built-in Snippets", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        var reveal = new Button { Content = "Show File", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        CodeSnippet? editing = null;
        var loading = false;

        string Language() => (languages.SelectedItem as ComboBoxItem)?.Tag as string ?? "csharp";

        void Reload(string? select = null)
        {
            loading = true;
            list.Items.Clear();
            foreach (var snippet in Snippets.ForLanguage(Language()).OrderBy(s => s.Shortcut, StringComparer.OrdinalIgnoreCase))
                list.Items.Add(new ListBoxItem { Content = $"{snippet.Shortcut}  —  {snippet.Title}", Tag = snippet });
            loading = false;
            var index = select == null ? 0 : list.Items.Cast<ListBoxItem>().ToList().FindIndex(i => ((CodeSnippet)i.Tag).Key == select);
            list.SelectedIndex = list.Items.Count == 0 ? -1 : Math.Max(index, 0);
            if (list.Items.Count == 0) Edit(null);
        }

        void Edit(CodeSnippet? snippet)
        {
            loading = true;
            editing = snippet;
            title.Text = snippet?.Title ?? "";
            shortcut.Text = snippet?.Shortcut ?? "";
            body.Text = snippet?.Body.Replace("\n", Environment.NewLine) ?? "";
            title.IsEnabled = shortcut.IsEnabled = body.IsEnabled = remove.IsEnabled = snippet != null;
            origin.Text = snippet == null ? "" : SnippetLibrary.IsBuiltIn(snippet)
                ? "A built-in snippet. Your changes are kept as your own copy; Restore Built-in Snippets brings the original back."
                : "Your snippet.";
            loading = false;
        }

        // Saved on every change, like the Mac version.
        void Commit()
        {
            if (loading || editing == null) return;
            var key = shortcut.Text.Trim();
            if (key.Length == 0 || key.Any(char.IsWhiteSpace)) return;
            var updated = editing with
            {
                Title = title.Text.Trim().Length > 0 ? title.Text.Trim() : key, Shortcut = key,
                Body = body.Text.Replace("\r\n", "\n"), Id = editing.Key,
            };
            Snippets.Library.Save(updated);
            editing = updated;
            Write();
            if (list.SelectedItem is ListBoxItem item)
            {
                item.Content = $"{updated.Shortcut}  —  {updated.Title}";
                item.Tag = updated;
            }
        }

        void Write()
        {
            try { Snippets.Library.Write(FilePath); } catch (Exception) { }
            changed();
        }

        languages.SelectionChanged += (_, _) => Reload();
        list.SelectionChanged += (_, _) => { if (!loading) Edit((list.SelectedItem as ListBoxItem)?.Tag as CodeSnippet); };
        title.TextChanged += (_, _) => Commit();
        shortcut.TextChanged += (_, _) => Commit();
        body.TextChanged += (_, _) => Commit();
        add.Click += (_, _) =>
        {
            var used = Snippets.ForLanguage(Language()).Select(s => s.Shortcut).ToHashSet();
            var name = "snippet";
            for (var n = 2; used.Contains(name); n++) name = $"snippet{n}";
            var snippet = new CodeSnippet(name, "New Snippet", Language(), "$0", Id: Guid.NewGuid().ToString("N"));
            Snippets.Library.Save(snippet);
            Write();
            Reload(snippet.Key);
            shortcut.Focus();
            shortcut.SelectAll();
        };
        remove.Click += (_, _) =>
        {
            if (editing == null) return;
            Snippets.Library.Delete(editing);
            Write();
            Reload();
        };
        restore.Click += (_, _) =>
        {
            var name = Languages.ById(Language()).Name;
            if (MessageBox.Show(window, $"The built-in {name} snippets come back as they were. Snippets you added stay.", "Restore built-in snippets?",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            Snippets.Library.RestoreBuiltIns(Language());
            Write();
            Reload();
        };
        reveal.Click += (_, _) =>
        {
            Write();
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{FilePath}\"") { UseShellExecute = true });
        };

        // Layout: languages on top; the list on the left; the snippet on the right; buttons at the bottom.
        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(languages, Dock.Top);
        root.Children.Add(languages);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var button in new[] { add, remove, restore, reveal }) buttons.Children.Add(button);
        var close = new Button { Content = "Close", MinWidth = 90, IsCancel = true };
        close.Click += (_, _) => window.Close();
        var bottom = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(buttons);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(list);
        var details = new DockPanel();
        Grid.SetColumn(details, 2);
        var fields = new StackPanel();
        fields.Children.Add(new TextBlock { Text = "Title" });
        fields.Children.Add(title);
        fields.Children.Add(new TextBlock { Text = "Shortcut (type it, then press Tab)", Margin = new Thickness(0, 8, 0, 0) });
        fields.Children.Add(shortcut);
        fields.Children.Add(new TextBlock { Text = "Code ($0 marks where the caret goes)", Margin = new Thickness(0, 8, 0, 4) });
        DockPanel.SetDock(fields, Dock.Top);
        details.Children.Add(fields);
        DockPanel.SetDock(origin, Dock.Bottom);
        details.Children.Add(origin);
        details.Children.Add(body);
        grid.Children.Add(details);
        root.Children.Add(grid);
        window.Content = root;

        var start = withSnippets.FindIndex(l => l.Id == languageId);
        languages.SelectedIndex = start >= 0 ? start : Math.Max(withSnippets.FindIndex(l => l.Id == "csharp"), 0);
        window.ShowDialog();
    }
}

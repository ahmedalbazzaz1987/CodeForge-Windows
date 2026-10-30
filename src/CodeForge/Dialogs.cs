using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodeForge.Core;
using Microsoft.Win32;

namespace CodeForge;

/// <summary>Small dialogs built in code, styled like the rest of the window.</summary>
internal static class DialogKit
{
    public static Window Make(Window owner, string title, double width, double height)
    {
        var window = new Window
        {
            Title = title, Owner = owner, Width = width, Height = height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };
        window.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
        window.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        return window;
    }

    public static StackPanel Buttons(Window window, string confirm, Action? onConfirm = null)
    {
        var ok = new Button { Content = confirm, IsDefault = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, _) => { if (onConfirm != null) onConfirm(); else window.DialogResult = true; };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        row.Children.Add(ok);
        row.Children.Add(cancel);
        return row;
    }
}

/// <summary>Asks for one line of text.</summary>
public static class Prompt
{
    public static string? Ask(Window owner, string title, string message, string value)
    {
        if (Environment.GetEnvironmentVariable("CF_ANSWER") is { } answer) return answer.Length == 0 ? value : answer;
        var window = DialogKit.Make(owner, title, 460, 210);
        window.SizeToContent = SizeToContent.Height;
        var box = new TextBox { Text = value, Margin = new Thickness(0, 8, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        panel.Children.Add(DialogKit.Buttons(window, "OK"));
        window.Content = panel;
        window.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return window.ShowDialog() == true ? box.Text : null;
    }
}

/// <summary>Go To File (Ctrl+T): type part of a file name.</summary>
public static class QuickOpenWindow
{
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "bin", "obj", "node_modules", "__pycache__", ".venv", "packages" };

    public static string? Pick(Window owner, string root)
    {
        var files = new List<string>();
        void Walk(string folder, int depth)
        {
            if (depth > 12 || files.Count > 20000) return;
            try
            {
                files.AddRange(Directory.GetFiles(folder));
                foreach (var sub in Directory.GetDirectories(folder))
                    if (!Skipped.Contains(Path.GetFileName(sub))) Walk(sub, depth + 1);
            }
            catch (UnauthorizedAccessException) { }
        }
        Walk(root, 0);

        var window = DialogKit.Make(owner, "Go To File", 620, 420);
        var search = new TextBox();
        var list = new ListBox { Margin = new Thickness(0, 8, 0, 0), Height = 300 };
        void Refresh()
        {
            var query = search.Text.Trim();
            list.ItemsSource = files
                .Select(f => (Path: f, Score: FuzzyMatch.Score(query, Path.GetFileName(f)) ?? (FuzzyMatch.Score(query, Path.GetRelativePath(root, f)) - 20)))
                .Where(x => x.Score != null).OrderByDescending(x => x.Score).ThenBy(x => x.Path.Length).Take(200)
                .Select(x => new ListBoxItem { Content = $"{Path.GetFileName(x.Path)}    {Path.GetRelativePath(root, Path.GetDirectoryName(x.Path)!)}", Tag = x.Path })
                .ToList();
            list.SelectedIndex = 0;
        }
        string? chosen = null;
        void Accept()
        {
            if (list.SelectedItem is ListBoxItem { Tag: string path }) { chosen = path; window.DialogResult = true; }
        }
        search.TextChanged += (_, _) => Refresh();
        search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down) { list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.Items.Count - 1); e.Handled = true; }
            if (e.Key == Key.Up) { list.SelectedIndex = Math.Max(list.SelectedIndex - 1, 0); e.Handled = true; }
            if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
        };
        list.MouseDoubleClick += (_, _) => Accept();
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(search);
        panel.Children.Add(list);
        window.Content = panel;
        window.Loaded += (_, _) => search.Focus();
        Refresh();
        return window.ShowDialog() == true ? chosen : null;
    }
}

/// <summary>File ▸ New Project: templates in groups, a name and a location.</summary>
public static class NewProjectWindow
{
    public static (ProjectTemplate Template, string Name, string Location)? Pick(Window owner)
    {
        var window = DialogKit.Make(owner, "Create a new project", 820, 600);
        window.SizeToContent = SizeToContent.Height;
        var list = new ListBox { Height = 330 };
        string? lastGroup = null;
        foreach (var template in ProjectTemplates.All)
        {
            if (template.Group != lastGroup)
            {
                lastGroup = template.Group;
                list.Items.Add(new ListBoxItem
                {
                    Content = new TextBlock { Text = template.Group, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) },
                    IsEnabled = false, Focusable = false,
                });
            }
            var row = new StackPanel { Margin = new Thickness(4, 3, 4, 3) };
            row.Children.Add(new TextBlock { Text = template.Name, FontWeight = FontWeights.SemiBold });
            row.Children.Add(new TextBlock { Text = template.Summary, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
            var tags = new TextBlock { Text = template.Tags, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
            tags.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            row.Children.Add(tags);
            list.Items.Add(new ListBoxItem { Content = row, Tag = template });
        }
        list.SelectedIndex = 1;
        var name = new TextBox { Text = "WinFormsApp1" };
        var location = new TextBox
        {
            Text = Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos"))
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos")
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        var browse = new Button { Content = "…", Width = 36, Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { InitialDirectory = location.Text };
            if (dialog.ShowDialog(window) == true) location.Text = dialog.FolderName;
        };
        list.SelectionChanged += (_, _) =>
        {
            // Suggest a name like Visual Studio does (WpfApp1, ConsoleApp1…) until the user types one.
            if (list.SelectedItem is not ListBoxItem { Tag: ProjectTemplate template } || !System.Text.RegularExpressions.Regex.IsMatch(name.Text, @"^\w+App\d*$|^\w*\d$")) return;
            var stem = template.Id switch
            {
                var id when id.StartsWith("winforms") => "WinFormsApp",
                var id when id.StartsWith("wpf") => "WpfApp",
                var id when id.Contains("console") => "ConsoleApp",
                var id when id.Contains("library") || id.Contains("lib") => "ClassLibrary",
                var id when id.StartsWith("aspnet") || id.StartsWith("blazor") => "WebApplication",
                var id when id.Contains("test") || id.StartsWith("xunit") || id == "mstest" => "TestProject",
                _ => "App",
            };
            var number = 1;
            while (Directory.Exists(Path.Combine(location.Text, stem + number))) number++;
            name.Text = stem + number;
        };

        (ProjectTemplate, string, string)? result = null;
        void Create()
        {
            if (list.SelectedItem is not ListBoxItem { Tag: ProjectTemplate template }) return;
            var projectName = name.Text.Trim();
            if (projectName.Length == 0 || projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(window, "Enter a project name without \\ / : * ? \" < > |", "New Project");
                return;
            }
            if (!Directory.Exists(location.Text)) Directory.CreateDirectory(location.Text);
            result = (template, projectName, location.Text);
            window.DialogResult = true;
        }

        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        void Place(UIElement element, int row, int column) { Grid.SetRow(element, row); Grid.SetColumn(element, column); grid.Children.Add(element); }
        Place(new TextBlock { Text = "Project name", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 6) }, 0, 0);
        Place(name, 0, 1);
        Place(new TextBlock { Text = "Location", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 10, 0) }, 1, 0);
        location.Margin = new Thickness(0, 6, 0, 0);
        Place(location, 1, 1);
        browse.Margin = new Thickness(6, 6, 0, 0);
        Place(browse, 1, 2);

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Create a new project", FontSize = 20, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(list);
        panel.Children.Add(grid);
        panel.Children.Add(DialogKit.Buttons(window, "Create", Create));
        window.Content = panel;
        list.MouseDoubleClick += (_, _) => Create();
        return window.ShowDialog() == true && result is { } chosen ? chosen : null;
    }
}

/// <summary>Tools ▸ Options.</summary>
public static class OptionsWindow
{
    public static bool Edit(Window owner, Settings settings)
    {
        var window = DialogKit.Make(owner, "Options", 480, 330);
        var theme = new ComboBox { ItemsSource = Themes.Names.Select(Themes.Title).ToList(), SelectedIndex = Math.Max(Array.IndexOf(Themes.Names, settings.Theme), 0) };
        var font = new ComboBox { ItemsSource = Enumerable.Range(9, 20).Select(n => n.ToString()).ToList(), Text = ((int)settings.FontSize).ToString(), IsEditable = true };
        var updates = new CheckBox { Content = "Check for updates automatically", IsChecked = settings.CheckForUpdates };
        var session = new CheckBox { Content = "Reopen the last folder and files at startup", IsChecked = settings.RestoreSession };
        var panel = new StackPanel { Margin = new Thickness(18) };
        TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };
        panel.Children.Add(Label("Environment ▸ Color theme"));
        panel.Children.Add(theme);
        panel.Children.Add(Label("Text Editor ▸ Font size"));
        panel.Children.Add(font);
        panel.Children.Add(Label("Startup and updates"));
        panel.Children.Add(updates);
        panel.Children.Add(session);
        panel.Children.Add(DialogKit.Buttons(window, "OK"));
        window.SizeToContent = SizeToContent.Height;
        window.Content = panel;
        if (window.ShowDialog() != true) return false;
        settings.Theme = Themes.Names[Math.Max(theme.SelectedIndex, 0)];
        if (double.TryParse(font.Text, out var size)) settings.FontSize = Math.Clamp(size, 8, 36);
        settings.CheckForUpdates = updates.IsChecked == true;
        settings.RestoreSession = session.IsChecked == true;
        return true;
    }
}

/// <summary>The Start Page shown when no file is open: Get started and recent projects, like Visual Studio's start window.</summary>
public static class StartPageView
{
    public static void Fill(ScrollViewer host, MainWindow window)
    {
        var root = new StackPanel { Margin = new Thickness(56, 44, 56, 40), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Logo.png")), Width = 56, Height = 56, Margin = new Thickness(0, 0, 14, 0) });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = "CodeForge", FontSize = 30, FontWeight = FontWeights.Light });
        var version = new TextBlock { Text = $"Version {Updater.CurrentVersion} for Windows" };
        version.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        words.Children.Add(version);
        title.Children.Add(words);
        root.Children.Add(title);

        var columns = new Grid { Margin = new Thickness(0, 30, 0, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var recent = new StackPanel();
        recent.Children.Add(new TextBlock { Text = "Open recent", FontSize = 18, Margin = new Thickness(0, 0, 0, 10) });
        var folders = Settings.Current.RecentFolders.Where(Directory.Exists).Take(10).ToList();
        if (folders.Count == 0)
        {
            var none = new TextBlock { Text = "Projects you open appear here.", TextWrapping = TextWrapping.Wrap };
            none.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            recent.Children.Add(none);
        }
        foreach (var folder in folders)
        {
            var item = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 2) };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = Path.GetFileName(folder.TrimEnd('\\')), FontWeight = FontWeights.SemiBold });
            var path = new TextBlock { Text = folder, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            path.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            text.Children.Add(path);
            item.Content = text;
            item.Click += (_, _) => window.SetWorkspace(folder);
            recent.Children.Add(item);
        }
        Grid.SetColumn(recent, 0);
        columns.Children.Add(recent);

        var start = new StackPanel();
        start.Children.Add(new TextBlock { Text = "Get started", FontSize = 18, Margin = new Thickness(0, 0, 0, 10) });
        void Action(string glyph, string heading, string detail, Action action)
        {
            var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 22, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            row.Children.Add(icon);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeights.SemiBold });
            var sub = new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            text.Children.Add(sub);
            row.Children.Add(text);
            button.Content = row;
            button.Click += (_, _) => action();
            start.Children.Add(button);
        }
        Action("", "Create a new project", "Windows Forms, WPF, ASP.NET Core, console apps and more", window.NewProject);
        Action("", "Open a local folder", "Open a project or any folder of code", window.OpenFolder);
        Action("", "Open a file", "Edit a single file", window.OpenFile);
        Action("", "Clone a repository", "Get code from GitHub or another Git server", window.CloneRepository);
        Action("", "New file", "Start writing right away", window.NewFile);
        Grid.SetColumn(start, 2);
        columns.Children.Add(start);
        root.Children.Add(columns);
        host.Content = root;
    }
}

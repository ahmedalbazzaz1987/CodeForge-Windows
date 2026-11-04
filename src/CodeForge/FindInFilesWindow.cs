using System.IO;
using System.Windows;
using System.Windows.Controls;
using CodeForge.Core;

namespace CodeForge;

/// <summary>Edit ▸ Find in Files (Ctrl+Shift+F) and Replace in Files (Ctrl+Shift+H), like Visual Studio's Find and Replace window.</summary>
public sealed class FindInFilesWindow : Window
{
    private readonly MainWindow main;
    private readonly ComboBox find = new() { IsEditable = true };
    private readonly ComboBox replace = new() { IsEditable = true };
    private readonly TextBlock replaceLabel = new() { Text = "Replace with:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly ComboBox scope = new() { ItemsSource = new[] { "Entire Solution (project folder)", "Current Folder", "All Open Documents" }, SelectedIndex = 0 };
    private readonly ComboBox fileTypes = new() { IsEditable = true, ItemsSource = new[] { "*.*", "*.vb", "*.cs", "*.cs; *.vb", "*.xaml", "*.cshtml; *.razor", "*.html; *.css; *.js", "*.py", "*.ts; *.tsx", "*.json; *.xml" }, Text = "*.*" };
    private readonly CheckBox matchCase = new() { Content = "Match case" };
    private readonly CheckBox wholeWord = new() { Content = "Match whole word" };
    private readonly CheckBox regex = new() { Content = "Use regular expressions" };
    private readonly Button replaceAll = new() { Content = "Replace All", MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button findAll = new() { Content = "Find All", MinWidth = 100, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };

    public FindInFilesWindow(MainWindow main)
    {
        this.main = main;
        Owner = main;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        void Row(UIElement label, UIElement field)
        {
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(label, row);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            ((FrameworkElement)field).Margin = new Thickness(0, 0, 0, 8);
            grid.Children.Add(label);
            grid.Children.Add(field);
        }
        TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 8) };
        replaceLabel.Margin = new Thickness(0, 0, 10, 8);
        Row(Label("Find what:"), find);
        Row(replaceLabel, replace);
        Row(Label("Look in:"), scope);
        Row(Label("File types:"), fileTypes);
        var options = new StackPanel();
        foreach (var box in new[] { matchCase, wholeWord, regex }) options.Children.Add(box);
        Row(new TextBlock(), options);
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80 };
        close.Click += (_, _) => Hide();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(close);
        buttons.Children.Add(replaceAll);
        buttons.Children.Add(findAll);
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(grid);
        panel.Children.Add(buttons);
        Content = panel;
        findAll.Click += (_, _) => FindAll();
        replaceAll.Click += (_, _) => ReplaceAll();
        // Closing only hides it, so the options and history stay for next time.
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    public void Open(bool replacing, string? selection)
    {
        Title = replacing ? "Replace in Files" : "Find in Files";
        var visibility = replacing ? Visibility.Visible : Visibility.Collapsed;
        replace.Visibility = replaceLabel.Visibility = replaceAll.Visibility = visibility;
        if (!string.IsNullOrEmpty(selection) && !selection.Contains('\n')) find.Text = selection;
        Show();
        Activate();
        find.Focus();
    }

    /// <summary>For the test harness: search without showing the window.</summary>
    public void Run(string query)
    {
        find.Text = query;
        FindAll();
    }

    public void RunReplace(string query, string replacement)
    {
        find.Text = query;
        replace.Text = replacement;
        ReplaceAll();
    }

    private SearchOptions Options => new(matchCase.IsChecked == true, wholeWord.IsChecked == true, regex.IsChecked == true, fileTypes.Text);

    private static void Remember(ComboBox box)
    {
        var text = box.Text;
        if (text.Length == 0) return;
        box.Items.Remove(text);
        box.Items.Insert(0, text);
        box.Text = text;
    }

    /// <summary>The files to look at, with the text of those open in tabs (it may not be saved yet).</summary>
    private List<(string Path, string? Text)>? Sources()
    {
        var (project, currentFolder, open) = main.SearchContext();
        var texts = open.GroupBy(o => o.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Text, StringComparer.OrdinalIgnoreCase);
        if (scope.SelectedIndex == 2) return open.Select(o => (o.Path, (string?)o.Text)).ToList();
        var folder = scope.SelectedIndex == 1 ? currentFolder ?? project : project ?? currentFolder;
        if (folder == null) return null;
        return FileSearch.Files(folder, Options.FileTypes).Select(f => (f, texts.TryGetValue(f, out var text) ? text : null)).ToList();
    }

    private void FindAll()
    {
        var query = find.Text;
        if (FileSearch.Expression(query, Options) is not { } expression)
        {
            main.SetStatus(query.Length == 0 ? "Enter the text to find" : "That regular expression isn't valid");
            return;
        }
        Remember(find);
        if (Sources() is not { } sources)
        {
            main.ShowError("Open a folder first", "Find in Files searches the open project folder. Choose All Open Documents to search only the files you have open.");
            return;
        }
        findAll.IsEnabled = false;
        main.SetStatus("Searching…");
        Task.Run(() =>
        {
            var matches = new List<SearchMatch>();
            foreach (var (path, openText) in sources)
            {
                if ((openText ?? FileSearch.ReadText(path)) is not { } text) continue;
                matches.AddRange(FileSearch.Matches(text, path, expression));
                if (matches.Count >= 5000) break;
            }
            Dispatcher.Invoke(() =>
            {
                findAll.IsEnabled = true;
                var files = matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var summary = matches.Count == 0 ? $"No matches for “{query}” in {sources.Count} file{(sources.Count == 1 ? "" : "s")}."
                    : $"{matches.Count} match{(matches.Count == 1 ? "" : "es")} for “{query}” in {files} file{(files == 1 ? "" : "s")}" + (matches.Count >= 5000 ? " (stopped at 5,000)" : "");
                main.ShowFindResults(summary, matches);
                main.SetStatus(summary);
                Hide();
            });
        });
    }

    private void ReplaceAll()
    {
        var options = Options;
        var query = find.Text;
        var replacement = replace.Text;
        if (FileSearch.Expression(query, options) is not { } expression) return;
        Remember(find);
        Remember(replace);
        if (Sources() is not { } sources)
        {
            main.ShowError("Open a folder first", "Replace in Files works in the open project folder.");
            return;
        }
        var perFile = new List<(string Path, int Count)>();
        foreach (var (path, openText) in sources)
        {
            if ((openText ?? FileSearch.ReadText(path)) is not { } text) continue;
            var count = expression.Count(text);
            if (count > 0) perFile.Add((path, count));
        }
        var total = perFile.Sum(f => f.Count);
        if (total == 0) { main.SetStatus($"No matches for “{query}”"); return; }
        if (Environment.GetEnvironmentVariable("CF_SNAPSHOT") == null &&
            MessageBox.Show(this, $"Replace {total} occurrence{(total == 1 ? "" : "s")} in {perFile.Count} file{(perFile.Count == 1 ? "" : "s")}?\n\n“{query}” will be replaced with “{replacement}”. Open files can be undone with Ctrl+Z; other files are saved right away.",
                            "Replace in Files", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var (handled, replaced) = main.ReplaceInOpenDocuments(expression, replacement, options.RegularExpression,
                                                              perFile.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var failed = new List<string>();
        foreach (var (path, _) in perFile.Where(f => !handled.Contains(f.Path)))
        {
            if (FileSearch.ReadText(path) is not { } text) continue;
            var (updated, count) = FileSearch.Replace(text, expression, replacement, options.RegularExpression);
            try
            {
                File.WriteAllText(path, updated);
                replaced += count;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add(Path.GetFileName(path)); }
        }
        main.SetStatus($"Replaced {replaced} occurrence{(replaced == 1 ? "" : "s")} in {perFile.Count} file{(perFile.Count == 1 ? "" : "s")}");
        if (failed.Count > 0) main.ShowError("Some files couldn't be changed", string.Join(", ", failed));
    }
}

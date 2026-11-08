using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodeForge.Core;

namespace CodeForge;

/// <summary>
/// Class View, beside Solution Explorer as in Visual Studio: the project's namespaces and types with their members,
/// read from the source files (no build needed). Double-click a member to go to it.
/// </summary>
public sealed class ClassViewPanel : DockPanel
{
    public Func<string?> Folder { get; init; } = () => null;
    public Action<string, int> Open { get; init; } = (_, _) => { };

    private readonly TextBox search = new() { Margin = new Thickness(8, 2, 8, 6) };
    private readonly TreeView tree = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBlock empty = new() { Margin = new Thickness(12), Opacity = 0.6, TextWrapping = TextWrapping.Wrap };
    private List<CodeSymbol> symbols = [];
    private int generation;

    public IReadOnlyList<CodeSymbol> Symbols => symbols;

    public ClassViewPanel()
    {
        var title = new DockPanel();
        var refresh = new Button
        {
            Content = "", ToolTip = "Refresh", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12,
            Padding = new Thickness(8, 5, 8, 5), MinWidth = 0, MinHeight = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 2, 4, 0),
        };
        refresh.Click += (_, _) => Refresh();
        DockPanel.SetDock(refresh, Dock.Right);
        title.Children.Add(refresh);
        title.Children.Add(new TextBlock { Text = "CLASS VIEW", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 8, 10, 6), Opacity = 0.7 });
        SetDock(title, Dock.Top);
        Children.Add(title);
        var hint = new Grid();
        hint.Children.Add(search);
        hint.Children.Add(new TextBlock { Text = "Search", Opacity = 0.5, IsHitTestVisible = false, Margin = new Thickness(14, 5, 0, 0) });
        search.TextChanged += (_, _) =>
        {
            ((TextBlock)hint.Children[1]).Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Show();
        };
        SetDock(hint, Dock.Top);
        Children.Add(hint);
        SetDock(empty, Dock.Top);
        Children.Add(empty);
        Children.Add(tree);
        tree.MouseDoubleClick += (_, e) =>
        {
            if (tree.SelectedItem is TreeViewItem { Tag: CodeSymbol symbol } && symbol.Path != null) { e.Handled = true; Open(symbol.Path, symbol.Line); }
        };
        tree.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && tree.SelectedItem is TreeViewItem { Tag: CodeSymbol symbol } && symbol.Path != null) Open(symbol.Path, symbol.Line);
        };
    }

    /// <summary>Reads the project's symbols in the background, then shows them.</summary>
    public async void Refresh()
    {
        var folder = Folder();
        var request = ++generation;
        if (folder == null)
        {
            symbols = [];
            Show();
            return;
        }
        empty.Text = "Reading the project…";
        empty.Visibility = Visibility.Visible;
        var found = await Task.Run(() => SymbolParser.Index(folder));
        if (request != generation) return;
        symbols = found;
        Show();
    }

    private void Show()
    {
        tree.Items.Clear();
        var filter = search.Text.Trim();
        var shown = filter.Length == 0 ? symbols
            : symbols.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || (s.Container?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        empty.Text = Folder() == null ? "Open a folder to see its classes." : symbols.Count == 0 ? "No classes or functions found." : "Nothing matches.";
        empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Namespaces (or "(Global)"), then types, then members, sorted by name like Visual Studio's alphabetical view.
        var expand = filter.Length > 0;
        foreach (var ns in shown.GroupBy(s => s.Namespace ?? "").OrderBy(g => g.Key))
        {
            var nsItem = Item(ns.Key.Length == 0 ? "{ } (Global)" : "{ } " + ns.Key, "#7F8C8D", null);
            nsItem.IsExpanded = true;
            var types = ns.Where(s => s.IsType).GroupBy(s => s.Name).Select(g => g.First()).ToList();
            var containers = types.Select(t => t.Name).ToHashSet();
            foreach (var type in types.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var typeItem = Item(type.Name, Color(type), type);
                typeItem.IsExpanded = expand;
                foreach (var member in ns.Where(s => !s.IsType && s.Container == type.Name).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                    typeItem.Items.Add(Item(Label(member), Color(member), member));
                nsItem.Items.Add(typeItem);
            }
            // Functions and variables outside any type (Python, JavaScript, Go…), and members of types filtered out.
            foreach (var loose in ns.Where(s => !s.IsType && (s.Container == null || !containers.Contains(s.Container))).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                nsItem.Items.Add(Item(Label(loose), Color(loose), loose));
            if (nsItem.Items.Count > 0) tree.Items.Add(nsItem);
        }
    }

    private static string Label(CodeSymbol symbol) =>
        symbol.Kind is SymbolKind.Method or SymbolKind.Function or SymbolKind.Constructor ? symbol.Name + "()" : symbol.Name;

    private static string Color(CodeSymbol symbol) => symbol.Kind switch
    {
        SymbolKind.Class or SymbolKind.Module => "#E67E22",
        SymbolKind.Structure or SymbolKind.Interface => "#16A085",
        SymbolKind.Enumeration or SymbolKind.EnumMember => "#C9A400",
        SymbolKind.Method or SymbolKind.Function or SymbolKind.Constructor => "#8E44AD",
        _ => "#2F80ED",
    };

    private static TreeViewItem Item(string text, string color, CodeSymbol? symbol)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "■", Foreground = Themes.Brush(color), FontSize = 9, Margin = new Thickness(0, 2, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text });
        return new TreeViewItem { Header = row, Tag = symbol, ToolTip = symbol == null ? null : $"{symbol.KindTitle}: {symbol.Signature}\n{Path.GetFileName(symbol.Path)}, line {symbol.Line}" };
    }
}

/// <summary>
/// View ▸ Object Browser: every type in the project, its members, and the selected one's declaration, as in Visual Studio.
/// </summary>
public static class ObjectBrowser
{
    public static void Show(Window owner, IReadOnlyList<CodeSymbol> symbols, Action<string, int> open)
    {
        var window = DialogKit.Make(owner, "Object Browser", 900, 560);
        window.ResizeMode = ResizeMode.CanResize;
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        var types = new ListBox();
        var members = new ListBox();
        var details = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 90, Margin = new Thickness(0, 8, 0, 0),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        };
        details.SetResourceReference(Control.ForegroundProperty, "TextBrush");

        void Describe(CodeSymbol? symbol) => details.Text = symbol == null ? "" :
            $"{symbol.Signature}\n\n{symbol.KindTitle} of {symbol.Container ?? symbol.Namespace ?? "(global)"}" +
            (symbol.Type != null ? $", type {symbol.Type}" : "") + (symbol.IsShared ? ", shared" : "") +
            $"\n{symbol.QualifiedName} — {Path.GetFileName(symbol.Path)}, line {symbol.Line}";

        void ShowTypes()
        {
            types.Items.Clear();
            var filter = search.Text.Trim();
            var all = symbols.Where(s => s.IsType).GroupBy(s => s.QualifiedName).Select(g => g.First())
                .Where(t => filter.Length == 0 || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            symbols.Any(m => m.Container == t.Name && m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var type in all) types.Items.Add(new ListBoxItem { Content = $"{type.Name}   ({type.KindTitle.ToLowerInvariant()}{(type.Namespace != null ? ", " + type.Namespace : "")})", Tag = type });
            if (types.Items.Count > 0) types.SelectedIndex = 0;
        }

        types.SelectionChanged += (_, _) =>
        {
            members.Items.Clear();
            if ((types.SelectedItem as ListBoxItem)?.Tag is not CodeSymbol type) { Describe(null); return; }
            foreach (var member in symbols.Where(s => !s.IsType && s.Container == type.Name && s.Namespace == type.Namespace).OrderBy(s => s.Kind).ThenBy(s => s.Name))
                members.Items.Add(new ListBoxItem { Content = member.Signature, Tag = member });
            Describe(type);
        };
        members.SelectionChanged += (_, _) => Describe((members.SelectedItem as ListBoxItem)?.Tag as CodeSymbol);
        MouseButtonEventHandler go = (sender, _) =>
        {
            if (((sender as ListBox)?.SelectedItem as ListBoxItem)?.Tag is CodeSymbol { Path: not null } symbol)
            {
                window.Close();
                open(symbol.Path, symbol.Line);
            }
        };
        types.MouseDoubleClick += go;
        members.MouseDoubleClick += go;
        search.TextChanged += (_, _) => ShowTypes();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        grid.Children.Add(types);
        Grid.SetColumn(members, 2);
        grid.Children.Add(members);
        var root = new DockPanel { Margin = new Thickness(14) };
        var top = new DockPanel();
        top.Children.Add(new TextBlock { Text = "Search:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
        top.Children.Add(search);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        DockPanel.SetDock(details, Dock.Bottom);
        root.Children.Add(details);
        root.Children.Add(grid);
        window.Content = root;
        ShowTypes();
        if (types.Items.Count == 0) details.Text = "Open a folder with source files to browse its classes.";
        window.Loaded += (_, _) => search.Focus();
        // Escape closes it, as in other dialogs (it has no Cancel button).
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
        window.ShowDialog();
    }
}

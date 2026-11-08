using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CodeForge.Core;
using Microsoft.Win32;

namespace CodeForge;

/// <summary>
/// Server Explorer: data connections (SQLite files; SQL Server and LocalDB, PostgreSQL and MySQL through their clients),
/// their tables, views and columns. Double-click a table to see its rows in Query Results; New Query opens a SQL
/// editor whose text Tools ▸ Execute Query (Ctrl+Shift+E) runs on the selected connection.
/// </summary>
public sealed class ServerExplorerPanel : DockPanel
{
    /// <summary>(title, sql, connection): opens a SQL editor for a connection.</summary>
    public Action<string, string> NewQueryDocument { get; init; } = (_, _) => { };
    /// <summary>Shows a result (or an error message) in the Query Results tab.</summary>
    public Action<string, QueryResult?, string?> ShowResult { get; init; } = (_, _, _) => { };
    public Action<string> Status { get; init; } = _ => { };
    public Func<Window> Owner { get; init; } = () => Application.Current.MainWindow;

    private static string FilePath => Path.Combine(Settings.Folder, "connections.json");
    private readonly List<DataConnection> connections = [];
    /// <summary>Passwords typed this session, by connection id. They're never saved.</summary>
    private readonly Dictionary<string, string> passwords = [];
    private readonly TreeView tree = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBlock empty = new()
    {
        Text = "Add a connection to a SQLite file or a database server (SQL Server LocalDB, PostgreSQL, MySQL).",
        Margin = new Thickness(12), Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
    };

    public ServerExplorerPanel()
    {
        var title = new DockPanel();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 4, 0) };
        tools.Children.Add(Icon("", "Add Connection…", AddConnection));
        tools.Children.Add(Icon("", "New Query", () => NewQuery()));
        tools.Children.Add(Icon("", "Refresh", () => { if (Selected is { } c) Load(c); }));
        DockPanel.SetDock(tools, Dock.Right);
        title.Children.Add(tools);
        title.Children.Add(new TextBlock { Text = "SERVER EXPLORER", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 8, 10, 6), Opacity = 0.7 });
        SetDock(title, Dock.Top);
        Children.Add(title);
        SetDock(empty, Dock.Top);
        Children.Add(empty);
        Children.Add(tree);
        tree.MouseDoubleClick += (_, e) =>
        {
            if (tree.SelectedItem is TreeViewItem { Tag: (DataConnection connection, DatabaseTable table) }) { e.Handled = true; ShowTable(connection, table.Name); }
        };
        tree.ContextMenuOpening += (_, e) =>
        {
            if (Selected is not { } connection) { e.Handled = true; return; }
            var menu = new ContextMenu();
            if (tree.SelectedItem is TreeViewItem { Tag: (DataConnection, DatabaseTable table) })
                menu.Items.Add(Item("Show Table Data", () => ShowTable(connection, table.Name)));
            menu.Items.Add(Item("New Query", () => NewQuery(connection)));
            menu.Items.Add(Item("Refresh", () => Load(connection)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Remove Connection", () => Remove(connection)));
            tree.ContextMenu = menu;
        };
        foreach (var connection in LoadConnections()) connections.Add(connection);
        Rebuild();
    }

    /// <summary>The connection of the selected node, or the only one there is.</summary>
    public DataConnection? Selected
    {
        get
        {
            var item = tree.SelectedItem as TreeViewItem;
            while (item != null)
            {
                if (item.Tag is DataConnection connection) return connection;
                if (item.Tag is (DataConnection owner, DatabaseTable)) return owner;
                item = ItemsControl.ItemsControlFromItemContainer(item) as TreeViewItem;
            }
            return connections.Count == 1 ? connections[0] : null;
        }
    }

    private static Button Icon(string glyph, string tip, Action action)
    {
        var button = new Button
        {
            Content = glyph, ToolTip = tip, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12,
            Padding = new Thickness(8, 5, 8, 5), MinWidth = 0, MinHeight = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static List<DataConnection> LoadConnections()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<DataConnection>>(File.ReadAllText(FilePath)) ?? [] : []; }
        catch (Exception) { return []; }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(connections, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { }
    }

    private void Rebuild()
    {
        tree.Items.Clear();
        empty.Visibility = connections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var connection in connections)
        {
            var node = Node(connection.Name, "", connection);
            node.Items.Add(new TreeViewItem { Header = "…" });
            node.Expanded += (_, e) => { if (e.OriginalSource == node && node.Items.Count == 1 && node.Items[0] is TreeViewItem { Tag: null }) Load(connection); };
            tree.Items.Add(node);
        }
    }

    private static TreeViewItem Node(string text, string glyph, object? tag, string? tip = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Margin = new Thickness(0, 1, 6, 0), Opacity = 0.75 });
        row.Children.Add(new TextBlock { Text = text });
        return new TreeViewItem { Header = row, Tag = tag, ToolTip = tip };
    }

    /// <summary>Reads a connection's tables and views (in the background) into its node.</summary>
    public async void Load(DataConnection connection)
    {
        var node = tree.Items.Cast<TreeViewItem>().FirstOrDefault(n => n.Tag is DataConnection c && c.Id == connection.Id);
        if (node == null || !EnsurePassword(connection)) return;
        node.Items.Clear();
        node.Items.Add(new TreeViewItem { Header = "Loading…", IsEnabled = false, Tag = "loading" });
        Status($"Connecting to {connection.Name}…");
        var password = passwords.GetValueOrDefault(connection.Id);
        try
        {
            var tables = await Task.Run(() => DatabaseClient.Tables(connection, password));
            node.Items.Clear();
            var tablesNode = Node($"Tables ({tables.Count(t => !t.IsView)})", "", null);
            var viewsNode = Node($"Views ({tables.Count(t => t.IsView)})", "", null);
            foreach (var table in tables)
            {
                var tableNode = Node(table.Name, table.IsView ? "" : "", (connection, table));
                foreach (var column in table.Columns) tableNode.Items.Add(Column(column));
                if (table.Columns.Count == 0 && connection.Kind != DataConnectionKind.Sqlite)
                {
                    // Servers: columns load when the table is opened.
                    tableNode.Items.Add(new TreeViewItem { Header = "…" });
                    tableNode.Expanded += async (_, e) =>
                    {
                        if (e.OriginalSource != tableNode || tableNode.Items.Count != 1 || tableNode.Items[0] is not TreeViewItem { Tag: null }) return;
                        var columns = await Task.Run(() => DatabaseClient.Columns(connection, table.Name, password));
                        tableNode.Items.Clear();
                        foreach (var column in columns) tableNode.Items.Add(Column(column));
                    };
                }
                (table.IsView ? viewsNode : tablesNode).Items.Add(tableNode);
            }
            node.Items.Add(tablesNode);
            if (viewsNode.Items.Count > 0) node.Items.Add(viewsNode);
            tablesNode.IsExpanded = true;
            node.IsExpanded = true;
            Status($"Connected to {connection.Name}");
        }
        catch (Exception exception)
        {
            node.Items.Clear();
            node.Items.Add(new TreeViewItem { Header = "Couldn't connect: " + exception.Message.Split('\n')[0], ToolTip = exception.Message, IsEnabled = false, Tag = "error" });
            passwords.Remove(connection.Id);
            Status($"Couldn't connect to {connection.Name}");
        }
    }

    private static TreeViewItem Column((string Name, string Type, bool IsKey) column) =>
        Node($"{column.Name}  {column.Type}".TrimEnd(), column.IsKey ? "" : "", null, column.IsKey ? "Primary key" : null);

    /// <summary>Servers may need a password: asked once per session, never saved.</summary>
    private bool EnsurePassword(DataConnection connection)
    {
        if (connection.Kind == DataConnectionKind.Sqlite || connection.User.Length == 0 || passwords.ContainsKey(connection.Id)) return true;
        var password = PasswordPrompt.Ask(Owner(), $"Password for {connection.User} on {connection.Name}");
        if (password == null) return false;
        passwords[connection.Id] = password;
        return true;
    }

    public void ShowTable(DataConnection connection, string table) =>
        Run(connection, DatabaseClient.TopRowsQuery(connection.Kind, table), table);

    /// <summary>Runs SQL on a connection and shows the result in Query Results.</summary>
    public async void Run(DataConnection connection, string sql, string? title = null)
    {
        if (!EnsurePassword(connection)) return;
        var password = passwords.GetValueOrDefault(connection.Id);
        Status($"Running the query on {connection.Name}…");
        var heading = $"{title ?? "Query"} — {connection.Name}";
        try
        {
            var result = await Task.Run(() => DatabaseClient.Run(connection, sql, password));
            ShowResult(heading, result, null);
            Status(result.Message);
            // The query may have created or dropped tables.
            if (title == null && connection.Kind == DataConnectionKind.Sqlite) Load(connection);
        }
        catch (Exception exception)
        {
            ShowResult(heading, null, exception.Message);
            Status("The query failed");
        }
    }

    public void NewQuery(DataConnection? connection = null)
    {
        connection ??= Selected;
        if (connection == null) { AddConnection(); return; }
        NewQueryDocument($"{connection.Name}.sql", connection.Kind == DataConnectionKind.Sqlite
            ? "-- Ctrl+Shift+E runs this (or the selected text) on " + connection.Name + "\nSELECT name FROM sqlite_master WHERE type = 'table';\n"
            : "-- Ctrl+Shift+E runs this (or the selected text) on " + connection.Name + "\n");
    }

    private void Remove(DataConnection connection)
    {
        if (MessageBox.Show(Owner(), $"“{connection.Name}” is removed from Server Explorer. The database itself isn't changed.", "Remove the connection?",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        connections.RemoveAll(c => c.Id == connection.Id);
        passwords.Remove(connection.Id);
        Save();
        Rebuild();
    }

    public void AddConnection()
    {
        if (ConnectionDialog.Ask(Owner()) is not { } connection) return;
        connections.Add(connection);
        Save();
        Rebuild();
        Load(connection);
    }

    /// <summary>For scripts: adds a SQLite connection without the dialog.</summary>
    public void AddSqlite(string path)
    {
        if (!File.Exists(path)) using (new SqliteDatabase(path, create: true)) { }
        if (connections.Any(c => c.Kind == DataConnectionKind.Sqlite && string.Equals(c.Location, path, StringComparison.OrdinalIgnoreCase))) return;
        var connection = new DataConnection(Guid.NewGuid().ToString("N"), Path.GetFileName(path), DataConnectionKind.Sqlite, path);
        connections.Add(connection);
        Save();
        Rebuild();
        Load(connection);
    }
}

/// <summary>Add Connection: a SQLite file (existing or new), or a SQL Server, PostgreSQL or MySQL database.</summary>
public static class ConnectionDialog
{
    public static DataConnection? Ask(Window owner)
    {
        var window = DialogKit.Make(owner, "Add Connection", 520, 400);
        window.SizeToContent = SizeToContent.Height;
        var kind = new ComboBox();
        foreach (var value in Enum.GetValues<DataConnectionKind>()) kind.Items.Add(new ComboBoxItem { Content = DataConnection.Title(value), Tag = value });
        kind.SelectedIndex = 0;
        var location = new TextBox();
        var browse = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        var create = new Button { Content = "New File…", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        var port = new TextBox();
        var database = new TextBox();
        var user = new TextBox();
        var name = new TextBox();
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.65, Margin = new Thickness(0, 8, 0, 0) };
        var locationLabel = new TextBlock();
        var serverFields = new StackPanel();

        DataConnectionKind Kind() => (DataConnectionKind)((ComboBoxItem)kind.SelectedItem).Tag;
        void Update()
        {
            var isFile = Kind() == DataConnectionKind.Sqlite;
            locationLabel.Text = isFile ? "Database file" : "Server";
            browse.Visibility = create.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
            serverFields.Visibility = isFile ? Visibility.Collapsed : Visibility.Visible;
            if (Kind() == DataConnectionKind.SqlServer && location.Text.Length == 0) location.Text = @"(localdb)\MSSQLLocalDB";
            hint.Text = isFile ? "CodeForge opens SQLite files with the SQLite that comes with Windows."
                : (Kind() == DataConnectionKind.SqlServer ? "Leave User empty to sign in with your Windows account (LocalDB, SQL Express). " : "") +
                  (ToolCheck.Find(DataConnection.Client(Kind())!) == null ? DataConnection.InstallHint(Kind()) : $"Uses {DataConnection.Client(Kind())}. The password is asked when you connect and never saved.");
        }
        kind.SelectionChanged += (_, _) => Update();
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "SQLite databases|*.db;*.sqlite;*.sqlite3;*.db3|All files|*.*" };
            if (dialog.ShowDialog(window) == true) location.Text = dialog.FileName;
        };
        create.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "SQLite database|*.db", FileName = "Database.db" };
            if (dialog.ShowDialog(window) != true) return;
            try
            {
                using (new SqliteDatabase(dialog.FileName, create: true)) { }
                location.Text = dialog.FileName;
            }
            catch (Exception exception)
            {
                MessageBox.Show(window, exception.Message, "Couldn't create the database", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        DataConnection? result = null;
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Data source" });
        panel.Children.Add(kind);
        locationLabel.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(locationLabel);
        var locationRow = new DockPanel();
        DockPanel.SetDock(create, Dock.Right);
        DockPanel.SetDock(browse, Dock.Right);
        locationRow.Children.Add(create);
        locationRow.Children.Add(browse);
        locationRow.Children.Add(location);
        panel.Children.Add(locationRow);
        foreach (var (label, box) in new[] { ("Port (optional)", port), ("Database", database), ("User (optional)", user) })
        {
            serverFields.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 0) });
            serverFields.Children.Add(box);
        }
        panel.Children.Add(serverFields);
        panel.Children.Add(new TextBlock { Text = "Name in Server Explorer (optional)", Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(name);
        panel.Children.Add(hint);
        panel.Children.Add(DialogKit.Buttons(window, "Add", () =>
        {
            var where = location.Text.Trim();
            if (where.Length == 0) { location.Focus(); return; }
            if (Kind() == DataConnectionKind.Sqlite && !File.Exists(where))
            {
                MessageBox.Show(window, "That file doesn't exist. Use New File… to create a database.", "Add Connection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var title = name.Text.Trim().Length > 0 ? name.Text.Trim() : Kind() == DataConnectionKind.Sqlite ? Path.GetFileName(where)
                : database.Text.Trim().Length > 0 ? $"{database.Text.Trim()} ({where})" : where;
            result = new DataConnection(Guid.NewGuid().ToString("N"), title, Kind(), where, port.Text.Trim(), database.Text.Trim(), user.Text.Trim());
            window.DialogResult = true;
        }));
        window.Content = panel;
        Update();
        return window.ShowDialog() == true ? result : null;
    }
}

/// <summary>Asks for a password (masked); it isn't saved anywhere.</summary>
public static class PasswordPrompt
{
    public static string? Ask(Window owner, string message)
    {
        var window = DialogKit.Make(owner, "Password", 420, 180);
        window.SizeToContent = SizeToContent.Height;
        var box = new PasswordBox { Margin = new Thickness(0, 8, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        panel.Children.Add(DialogKit.Buttons(window, "Connect"));
        window.Content = panel;
        window.Loaded += (_, _) => box.Focus();
        return window.ShowDialog() == true ? box.Password : null;
    }
}

/// <summary>Fills the Query Results grid from a result.</summary>
public static class QueryResultsView
{
    public static void Show(DataGrid grid, TextBlock summary, string title, QueryResult? result, string? error)
    {
        grid.Columns.Clear();
        grid.ItemsSource = null;
        if (result == null)
        {
            summary.Text = $"{title}: {error}";
            return;
        }
        summary.Text = $"{title}: {result.Message}";
        for (var i = 0; i < result.Columns.Count; i++)
        {
            // Column names can contain dots or brackets, which bindings would read as paths: bind by index.
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = result.Columns[i],
                Binding = new Binding($"[{i}]") { TargetNullValue = "NULL" },
            });
        }
        grid.ItemsSource = result.Rows;
    }
}

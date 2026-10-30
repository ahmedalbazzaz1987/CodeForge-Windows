using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodeForge.Core;
using Microsoft.Win32;

namespace CodeForge;

/// <summary>The main window: Toolbox, editor tabs, Solution Explorer, the Output / Error List / Terminal panel and the status bar.</summary>
public partial class MainWindow : Window
{
    private readonly List<EditorDocument> documents = [];
    private readonly Runner runner = new();
    private readonly Terminal terminal;
    private readonly Settings settings = Settings.Current;
    private string? workspace;
    private string? runFolder;
    private bool openedBrowser;
    private Action? afterRun;
    private string? pendingOpenFolder;
    private int untitledCount;
    private List<Diagnostic> diagnostics = [];

    public string? Workspace => workspace;
    public EditorDocument? Current => (DocumentTabs.SelectedItem as TabItem)?.Tag as EditorDocument;
    private BuildConfiguration Configuration => settings.Configuration == "Release" ? BuildConfiguration.Release : BuildConfiguration.Debug;

    public MainWindow()
    {
        InitializeComponent();
        terminal = new Terminal(TerminalText, TerminalInput, TerminalPrompt);
        BuildMenu();
        WireControls();
        RestoreLayout();
        UpdateChrome();
        Themes.Changed += UpdateChrome;
        Closing += (_, e) => { if (!ConfirmCloseAll()) e.Cancel = true; else SaveSession(); };
        Closed += (_, _) => { runner.Stop(); terminal.Stop(); };
    }

    // MARK: Menus and shortcuts

    private void BuildMenu()
    {
        MenuItem Menu(string header, params object[] items)
        {
            var menu = new MenuItem { Header = header };
            foreach (var item in items) menu.Items.Add(item);
            MainMenu.Items.Add(menu);
            return menu;
        }
        MenuItem Item(string header, Action action, string gesture = "", Func<bool>? enabled = null)
        {
            var command = new RelayCommand(action, enabled);
            if (gesture.Length > 0 && new KeyGestureConverter().ConvertFromString(gesture) is KeyGesture key)
                InputBindings.Add(new KeyBinding(command, key));
            return new MenuItem { Header = header, Command = command, InputGestureText = gesture };
        }
        Separator Line() => new();
        bool HasFolder() => workspace != null;
        bool HasDocument() => Current != null;

        Menu("_File",
            Item("New _Project…", NewProject, "Ctrl+Shift+N"),
            Item("_New File", NewFile, "Ctrl+N"),
            Line(),
            Item("_Open File…", OpenFile, "Ctrl+O"),
            Item("Open _Folder…", OpenFolder, "Ctrl+Shift+O"),
            Item("Clone _Repository…", CloneRepository),
            Line(),
            Item("_Save", () => Save(Current), "Ctrl+S", HasDocument),
            Item("Save _As…", () => SaveAs(Current), "", HasDocument),
            Item("Save A_ll", SaveAll, "Ctrl+Shift+S"),
            Line(),
            Item("_Close", () => { if (Current is { } d) CloseDocument(d); }, "Ctrl+F4", HasDocument),
            Item("Close Fol_der", () => SetWorkspace(null), "", HasFolder),
            Line(),
            Item("E_xit", Close, "Alt+F4"));

        Menu("_Edit",
            Item("_Undo", () => Current?.Editor.Undo(), "", HasDocument),
            Item("_Redo", () => Current?.Editor.Redo(), "", HasDocument),
            Line(),
            Item("_Find…", () => ShowFind(), "", HasDocument),
            Item("_Go To Line…", GoToLine, "Ctrl+G", HasDocument),
            Item("Go To _File…", QuickOpen, "Ctrl+T"),
            Line(),
            Item("Toggle Line _Comment", ToggleComment, "Ctrl+K", HasDocument),
            Item("_Duplicate Line", DuplicateLine, "Ctrl+D", HasDocument));

        Menu("_View",
            Item("Solution _Explorer", () => SetPane(ExplorerPane, true), "Ctrl+Alt+L"),
            Item("Tool_box", () => SetPane(ToolboxPane, true), "Ctrl+Alt+X"),
            Item("_Output", () => ShowPanel(OutputTab), "Ctrl+Alt+O"),
            Item("Error L_ist", () => ShowPanel(ErrorsTab), "Ctrl+Alt+E"),
            Item("_Terminal", () => ShowPanel(TerminalTab), "Ctrl+OemTilde"),
            Line(),
            Item("Hide Solution Explorer", () => SetPane(ExplorerPane, false)),
            Item("Hide Toolbox", () => SetPane(ToolboxPane, false)),
            Item("Hide Bottom Panel", () => SetPanelVisible(false)),
            Line(),
            Item("Zoom _In", () => SetFontSize(settings.FontSize + 1), "Ctrl+OemPlus"),
            Item("Zoom O_ut", () => SetFontSize(settings.FontSize - 1), "Ctrl+OemMinus"),
            Item("Reset Zoom", () => SetFontSize(14), "Ctrl+D0"));

        Menu("_Project",
            Item("Add New _Item…", AddNewItem, "Ctrl+Shift+A", HasFolder),
            Item("Manage _NuGet Packages…", ManageNuGet, "", () => Plan() is { Name: var n } && n.StartsWith(".NET")),
            Item("Project _Properties", OpenProjectFile, "", HasFolder));

        Menu("_Build",
            Item("_Build Solution", Build, "Ctrl+Shift+B", () => !runner.IsRunning),
            Item("_Rebuild Solution", () => RunCommand("dotnet build --no-incremental" + (Configuration == BuildConfiguration.Release ? " -c Release" : ""), workspace!, "Rebuilding…"), "", () => !runner.IsRunning && HasFolder()),
            Item("_Clean Solution", Clean, "", () => !runner.IsRunning && HasFolder()));

        Menu("_Debug",
            Item("_Start", Start, "F5", () => !runner.IsRunning),
            Item("Start _Without Debugging", Start, "Ctrl+F5", () => !runner.IsRunning),
            Item("Sto_p", runner.Stop, "Shift+F5", () => runner.IsRunning),
            Line(),
            Item("Set _Run Command…", EditRunCommand, "", HasFolder));

        Menu("_Test",
            Item("Run _All Tests", RunAllTests, "Ctrl+R", () => !runner.IsRunning && HasFolder()));

        Menu("_Tools",
            Item("_Terminal", () => ShowPanel(TerminalTab)),
            Item("Open Folder in File _Explorer", () => { if (workspace != null) Process.Start("explorer.exe", workspace); }, "", HasFolder),
            Line(),
            Item("_Options…", ShowOptions));

        Menu("_Window",
            Item("_Next Document", () => SwitchTab(1), "Ctrl+Tab"),
            Item("_Previous Document", () => SwitchTab(-1), "Ctrl+Shift+Tab"),
            Item("Close _All Documents", CloseAll, "", () => documents.Count > 0));

        Menu("_Help",
            Item("Check for _Updates…", () => _ = Updater.CheckAsync(this, userInitiated: true)),
            Item("CodeForge on _GitHub", () => Shell(Updater.ProjectPage)),
            Line(),
            Item("_About CodeForge", () => MessageBox.Show(this,
                $"CodeForge {Updater.CurrentVersion}\nA small Visual Studio-style IDE for Windows.\n\n{Updater.ProjectPage}",
                "About CodeForge", MessageBoxButton.OK, MessageBoxImage.Information)));
    }

    private void WireControls()
    {
        NewProjectButton.Click += (_, _) => NewProject();
        OpenButton.Click += (_, _) => OpenFolder();
        SaveAllButton.Click += (_, _) => SaveAll();
        UndoButton.Click += (_, _) => Current?.Editor.Undo();
        RedoButton.Click += (_, _) => Current?.Editor.Redo();
        StartButton.Click += (_, _) => Start();
        StopButton.Click += (_, _) => runner.Stop();
        BuildButton.Click += (_, _) => Build();
        ConfigurationBox.ItemsSource = new[] { "Debug", "Release" };
        ConfigurationBox.SelectedItem = settings.Configuration;
        ConfigurationBox.SelectionChanged += (_, _) =>
        {
            settings.Configuration = ConfigurationBox.SelectedItem as string ?? "Debug";
            settings.Save();
            StatusText.Text = "Configuration: " + settings.Configuration;
        };

        DocumentTabs.SelectionChanged += (_, e) => { if (e.OriginalSource == DocumentTabs) DocumentSelected(); };
        OutputInput.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            AppendOutput(OutputInput.Text + "\n");
            runner.Send(OutputInput.Text);
            OutputInput.Clear();
        };
        runner.Output += (text, _) =>
        {
            AppendOutput(text);
            if (!openedBrowser && BuildSystem.ListeningAddress(text) is { } address)
            {
                openedBrowser = true;
                AppendOutput($"Opening {address} in your browser…\n");
                if (Environment.GetEnvironmentVariable("CF_NO_BROWSER") == null) Shell(address);
            }
        };
        runner.Exited += RunFinished;
        ErrorList.MouseDoubleClick += (_, _) =>
        {
            if (ErrorList.SelectedItem is ErrorRow row) OpenPath(row.FullPath, row.Diagnostic.Line, row.Diagnostic.Column);
        };

        ExplorerOpenFolder.Click += (_, _) => OpenFolder();
        ExplorerRefresh.Click += (_, _) => ReloadExplorer();
        ExplorerNewFile.Click += (_, _) => CreateInExplorer(isFolder: false);
        ExplorerNewFolder.Click += (_, _) => CreateInExplorer(isFolder: true);
        ExplorerTree.MouseDoubleClick += (_, e) =>
        {
            if (ExplorerTree.SelectedItem is TreeViewItem { Tag: string path } && File.Exists(path))
            {
                OpenPath(path);
                e.Handled = true;
            }
        };
        ExplorerTree.KeyDown += (_, e) =>
        {
            if (ExplorerTree.SelectedItem is not TreeViewItem { Tag: string path }) return;
            if (e.Key == Key.Enter && File.Exists(path)) OpenPath(path);
            if (e.Key == Key.F2) RenameInExplorer(path);
            if (e.Key == Key.Delete) DeleteInExplorer(path);
        };

        ToolboxSearch.TextChanged += (_, _) => UpdateToolbox();
        ToolboxTree.MouseDoubleClick += (_, _) => { if (ToolboxTree.SelectedItem is TreeViewItem { Tag: ToolboxItem item }) InsertToolboxItem(item); };
        ToolboxTree.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || ToolboxTree.SelectedItem is not TreeViewItem { Tag: ToolboxItem item }) return;
            var indent = "";
            DragDrop.DoDragDrop(ToolboxTree, item.Expanded(indent).Text, DragDropEffects.Copy);
        };
    }

    // MARK: Session and layout

    public void Restore(string[] args)
    {
        if (settings.WindowBounds is { Length: 4 } b && b[2] > 300 && b[3] > 200)
        {
            Left = b[0]; Top = b[1]; Width = b[2]; Height = b[3];
        }
        if (settings.WindowMaximized) WindowState = WindowState.Maximized;
        if (args.Length > 0)
        {
            foreach (var arg in args) OpenPath(Path.GetFullPath(arg));
        }
        else if (settings.RestoreSession && Environment.GetEnvironmentVariable("CF_FRESH") == null)
        {
            var files = settings.OpenFiles.ToList();
            var selected = settings.SelectedFile;
            if (settings.Workspace is { } folder && Directory.Exists(folder)) SetWorkspace(folder);
            foreach (var file in files.Where(File.Exists)) OpenPath(file);
            if (selected != null && documents.FirstOrDefault(d => SamePath(d.Path, selected)) is { } document) Select(document);
        }
        UpdateChrome();
        if (settings.CheckForUpdates && Environment.GetEnvironmentVariable("CF_SNAPSHOT") == null && DateTime.UtcNow - settings.LastUpdateCheck > TimeSpan.FromDays(1))
            _ = Updater.CheckAsync(this, userInitiated: false);
    }

    private void SaveSession()
    {
        settings.Workspace = workspace;
        settings.OpenFiles = documents.Where(d => d.Path != null).Select(d => d.Path!).ToList();
        settings.SelectedFile = Current?.Path;
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal) settings.WindowBounds = [Left, Top, Width, Height];
        if (ToolboxPane.Visibility == Visibility.Visible) settings.ToolboxWidth = ToolboxColumn.ActualWidth;
        if (ExplorerPane.Visibility == Visibility.Visible) settings.ExplorerWidth = ExplorerColumn.ActualWidth;
        if (BottomPanel.Visibility == Visibility.Visible) settings.PanelHeight = PanelRow.ActualHeight;
        settings.Save();
    }

    private void RestoreLayout()
    {
        ToolboxColumn.Width = new GridLength(Math.Clamp(settings.ToolboxWidth, 150, 450));
        ExplorerColumn.Width = new GridLength(Math.Clamp(settings.ExplorerWidth, 160, 600));
        PanelRow.Height = new GridLength(Math.Clamp(settings.PanelHeight, 80, 800));
        SetPane(ToolboxPane, settings.ToolboxVisible);
        SetPane(ExplorerPane, settings.ExplorerVisible);
        SetPanelVisible(settings.PanelVisible);
    }

    private void SetPane(Border pane, bool visible)
    {
        var isToolbox = pane == ToolboxPane;
        var column = isToolbox ? ToolboxColumn : ExplorerColumn;
        if (!visible && pane.Visibility == Visibility.Visible)
        {
            if (isToolbox) settings.ToolboxWidth = column.ActualWidth; else settings.ExplorerWidth = column.ActualWidth;
        }
        pane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        (isToolbox ? ToolboxSplitter : ExplorerSplitter).Visibility = pane.Visibility;
        column.Width = visible ? new GridLength(isToolbox ? Math.Clamp(settings.ToolboxWidth, 150, 450) : Math.Clamp(settings.ExplorerWidth, 160, 600)) : new GridLength(0);
        if (isToolbox) settings.ToolboxVisible = visible; else settings.ExplorerVisible = visible;
    }

    private void SetPanelVisible(bool visible)
    {
        if (!visible && BottomPanel.Visibility == Visibility.Visible) settings.PanelHeight = PanelRow.ActualHeight;
        BottomPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PanelSplitter.Visibility = BottomPanel.Visibility;
        PanelRow.Height = visible ? new GridLength(Math.Clamp(settings.PanelHeight, 80, 800)) : new GridLength(0);
        settings.PanelVisible = visible;
    }

    private void ShowPanel(TabItem tab)
    {
        SetPanelVisible(true);
        PanelTabs.SelectedItem = tab;
        if (tab == TerminalTab) terminal.Start(workspace ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    // MARK: Workspace and Solution Explorer

    public void SetWorkspace(string? folder)
    {
        workspace = folder;
        if (folder != null)
        {
            settings.AddRecent(folder);
            if (ExplorerPane.Visibility != Visibility.Visible) SetPane(ExplorerPane, true);
        }
        terminal.SetFolder(folder);
        ReloadExplorer();
        UpdateChrome();
        UpdateBranch();
        SaveSession();
    }

    private void ReloadExplorer()
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Collect(ItemCollection items)
        {
            foreach (var item in items.OfType<TreeViewItem>())
            {
                if (item.IsExpanded && item.Tag is string path) { expanded.Add(path); Collect(item.Items); }
            }
        }
        Collect(ExplorerTree.Items);
        ExplorerTree.Items.Clear();
        ExplorerEmpty.Visibility = workspace == null ? Visibility.Visible : Visibility.Collapsed;
        if (workspace == null) return;
        var root = MakeNode(workspace, isFolder: true);
        root.Header = MakeHeader("", Path.GetFileName(workspace.TrimEnd('\\', '/')), bold: true);
        ExplorerTree.Items.Add(root);
        root.IsExpanded = true;
        void Reopen(ItemCollection items)
        {
            foreach (var item in items.OfType<TreeViewItem>())
                if (item.Tag is string path && expanded.Contains(path)) { item.IsExpanded = true; Reopen(item.Items); }
        }
        Reopen(root.Items);
    }

    private static readonly HashSet<string> HiddenFolders = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "bin", "obj", "node_modules", "__pycache__", ".venv" };

    private TreeViewItem MakeNode(string path, bool isFolder)
    {
        var name = Path.GetFileName(path);
        var item = new TreeViewItem
        {
            Tag = path,
            Header = MakeHeader(isFolder ? "" : FileGlyph(path), name, dim: isFolder && HiddenFolders.Contains(name)),
            ContextMenu = ExplorerMenu(path, isFolder),
        };
        if (isFolder)
        {
            item.Items.Add("…");   // placeholder until expanded
            item.Expanded += (_, e) =>
            {
                if (e.OriginalSource != item) return;
                if (item.Items.Count == 1 && item.Items[0] is string) Fill(item, path);
            };
        }
        return item;
    }

    private void Fill(TreeViewItem item, string folder)
    {
        item.Items.Clear();
        try
        {
            foreach (var sub in Directory.GetDirectories(folder).Order(StringComparer.OrdinalIgnoreCase))
                if (!new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.Hidden)) item.Items.Add(MakeNode(sub, true));
            foreach (var file in Directory.GetFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
                if (!new FileInfo(file).Attributes.HasFlag(FileAttributes.Hidden)) item.Items.Add(MakeNode(file, false));
        }
        catch (UnauthorizedAccessException) { }
    }

    private static string FileGlyph(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".sln" or ".slnx" or ".csproj" or ".vbproj" or ".fsproj" => "",
        ".cs" or ".vb" or ".fs" or ".py" or ".js" or ".ts" or ".cpp" or ".c" or ".h" or ".java" or ".go" or ".rs" => "",
        ".xaml" or ".axaml" or ".html" or ".htm" or ".cshtml" or ".razor" or ".xml" => "",
        ".json" or ".config" or ".yml" or ".yaml" or ".toml" or ".ini" => "",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".ico" or ".bmp" or ".svg" => "",
        ".md" or ".txt" => "",
        _ => "",
    };

    private static StackPanel MakeHeader(string glyph, string text, bool bold = false, bool dim = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13, Margin = new Thickness(0, 1, 6, 0),
            Foreground = glyph == "" ? Themes.Brush("#DCB67A") : (Brush)Application.Current.Resources["AccentBrush"],
        });
        var label = new TextBlock { Text = text, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        if (dim) label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        panel.Children.Add(label);
        return panel;
    }

    private ContextMenu ExplorerMenu(string path, bool isFolder)
    {
        var menu = new ContextMenu();
        void Add(string header, Action action) => menu.Items.Add(new MenuItem { Header = header, Command = new RelayCommand(action) });
        if (!isFolder) Add("Open", () => OpenPath(path));
        if (isFolder)
        {
            Add("New File…", () => CreateInExplorer(false, path));
            Add("New Folder…", () => CreateInExplorer(true, path));
        }
        Add("Rename…", () => RenameInExplorer(path));
        Add("Delete", () => DeleteInExplorer(path));
        menu.Items.Add(new Separator());
        Add("Open in File Explorer", () => Process.Start("explorer.exe", isFolder ? $"\"{path}\"" : $"/select,\"{path}\""));
        Add("Open in Terminal", () =>
        {
            ShowPanel(TerminalTab);
            terminal.SetFolder(isFolder ? path : Path.GetDirectoryName(path));
        });
        Add("Copy Full Path", () => Clipboard.SetText(path));
        return menu;
    }

    private string? SelectedFolder()
    {
        if (ExplorerTree.SelectedItem is TreeViewItem { Tag: string path })
            return Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        return workspace;
    }

    private void CreateInExplorer(bool isFolder, string? inFolder = null)
    {
        var folder = inFolder ?? SelectedFolder();
        if (folder == null) { OpenFolder(); return; }
        var name = Prompt.Ask(this, isFolder ? "New Folder" : "New File", "Name:", isFolder ? "NewFolder" : "NewFile.txt");
        if (string.IsNullOrWhiteSpace(name)) return;
        var target = Path.Combine(folder, name.Trim());
        try
        {
            if (Path.Exists(target)) throw new IOException($"“{name}” already exists.");
            if (isFolder) Directory.CreateDirectory(target);
            else { File.WriteAllText(target, ""); }
            ReloadExplorer();
            if (!isFolder) OpenPath(target);
        }
        catch (Exception error) { ShowError("Couldn't create it", error.Message); }
    }

    private void RenameInExplorer(string path)
    {
        var name = Prompt.Ask(this, "Rename", "New name:", Path.GetFileName(path));
        if (string.IsNullOrWhiteSpace(name) || name == Path.GetFileName(path)) return;
        var target = Path.Combine(Path.GetDirectoryName(path)!, name.Trim());
        try
        {
            if (Directory.Exists(path)) Directory.Move(path, target); else File.Move(path, target);
            ReloadExplorer();
            foreach (var document in documents.Where(d => SamePath(d.Path, path)).ToList())
            {
                document.Save(target);
                UpdateTab(document);
            }
        }
        catch (Exception error) { ShowError("Couldn't rename it", error.Message); }
    }

    private void DeleteInExplorer(string path)
    {
        if (MessageBox.Show(this, $"Move “{Path.GetFileName(path)}” to the Recycle Bin?", "Delete", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            var mode = Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin;
            var ui = Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs;
            if (Directory.Exists(path)) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, ui, mode);
            else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, ui, mode);
            foreach (var document in documents.Where(d => d.Path != null && (SamePath(d.Path, path) || d.Path.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase))).ToList())
                RemoveDocument(document);
            ReloadExplorer();
        }
        catch (Exception error) { ShowError("Couldn't delete it", error.Message); }
    }

    // MARK: Documents

    /// <summary>Opens a file in a tab (or a folder as the workspace), optionally at a 1-based line and column.</summary>
    public void OpenPath(string path, int? line = null, int? column = null)
    {
        if (Directory.Exists(path)) { SetWorkspace(path); return; }
        if (!File.Exists(path)) return;
        var document = documents.FirstOrDefault(d => SamePath(d.Path, path));
        if (document == null)
        {
            try { document = EditorDocument.Open(path); }
            catch (Exception error) { ShowError($"Couldn't open “{Path.GetFileName(path)}”", error.Message); return; }
            AddDocument(document);
        }
        Select(document);
        if (line is { } l) Dispatcher.BeginInvoke(() => document.Reveal(l, column), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void AddDocument(EditorDocument document)
    {
        documents.Add(document);
        var tab = new TabItem { Tag = document, Content = document.Editor };
        DocumentTabs.Items.Add(tab);
        document.StateChanged += () => { UpdateTab(document); UpdateChrome(); };
        document.CaretMoved += UpdatePosition;
        document.Editor.TextArea.PreviewMouseDown += (_, _) => { };
        document.Editor.MouseHover += (_, e) =>
        {
            var position = document.Editor.GetPositionFromPoint(e.GetPosition(document.Editor));
            if (position == null) return;
            var message = document.DiagnosticAt(document.Editor.Document.GetOffset(position.Value.Location));
            if (message != null) document.Editor.ToolTip = new ToolTip { Content = message, IsOpen = true };
        };
        document.Editor.MouseHoverStopped += (_, _) => { if (document.Editor.ToolTip is ToolTip tip) tip.IsOpen = false; document.Editor.ToolTip = null; };
        UpdateTab(document);
        document.SetDiagnostics(DiagnosticsFor(document));
    }

    private void UpdateTab(EditorDocument document)
    {
        var tab = TabFor(document);
        if (tab == null) return;
        var title = new TextBlock { Text = document.DisplayName + (document.IsDirty ? " ●" : ""), VerticalAlignment = VerticalAlignment.Center };
        var close = new Button
        {
            Content = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9,
            Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(8, 0, 0, 0), MinWidth = 0, MinHeight = 0,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = "Close (Ctrl+F4)",
        };
        close.Click += (_, _) => CloseDocument(document);
        var header = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = document.Path };
        header.Children.Add(title);
        header.Children.Add(close);
        header.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) CloseDocument(document); };
        tab.Header = header;
    }

    private TabItem? TabFor(EditorDocument document) => DocumentTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag == document);

    private void Select(EditorDocument document)
    {
        if (TabFor(document) is { } tab) DocumentTabs.SelectedItem = tab;
        DocumentSelected();
    }

    private void DocumentSelected()
    {
        StartPage.Visibility = documents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DocumentTabs.Visibility = documents.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (documents.Count == 0) StartPageView.Fill(StartPage, this);
        if (Current is { } document) Dispatcher.BeginInvoke(() => document.Editor.TextArea.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        UpdateToolbox();
        UpdateChrome();
    }

    public void NewFile()
    {
        untitledCount++;
        var document = new EditorDocument(null, "", untitledCount == 1 ? "Untitled" : $"Untitled {untitledCount}");
        AddDocument(document);
        Select(document);
    }

    public void OpenFile()
    {
        var dialog = new OpenFileDialog { Multiselect = true, InitialDirectory = workspace ?? "" };
        if (dialog.ShowDialog(this) == true) foreach (var file in dialog.FileNames) OpenPath(file);
    }

    public void OpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Open a project folder", InitialDirectory = workspace ?? "" };
        if (dialog.ShowDialog(this) == true) SetWorkspace(dialog.FolderName);
    }

    private bool Save(EditorDocument? document)
    {
        if (document == null) return false;
        if (document.Path == null) return SaveAs(document);
        try
        {
            document.Save();
            UpdateBranch();
            return true;
        }
        catch (Exception error)
        {
            ShowError($"Couldn't save “{document.DisplayName}”", error.Message);
            return false;
        }
    }

    private bool SaveAs(EditorDocument? document)
    {
        if (document == null) return false;
        var dialog = new SaveFileDialog { FileName = document.Path != null ? Path.GetFileName(document.Path) : document.DisplayName, InitialDirectory = workspace ?? "" };
        if (dialog.ShowDialog(this) != true) return false;
        try
        {
            document.Save(dialog.FileName);
            ReloadExplorer();
            UpdateToolbox();
            return true;
        }
        catch (Exception error)
        {
            ShowError($"Couldn't save “{document.DisplayName}”", error.Message);
            return false;
        }
    }

    public void SaveAll()
    {
        foreach (var document in documents.Where(d => d.IsDirty).ToList()) Save(document);
    }

    private bool ConfirmDiscard(EditorDocument document)
    {
        if (!document.IsDirty) return true;
        Select(document);
        var answer = MessageBox.Show(this, $"Save changes to “{document.DisplayName}”?", "CodeForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => Save(document),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private bool ConfirmCloseAll() => documents.ToList().All(ConfirmDiscard);

    private void CloseDocument(EditorDocument document)
    {
        if (ConfirmDiscard(document)) RemoveDocument(document);
    }

    private void RemoveDocument(EditorDocument document)
    {
        var index = documents.IndexOf(document);
        documents.Remove(document);
        document.Detach();
        if (TabFor(document) is { } tab) DocumentTabs.Items.Remove(tab);
        if (documents.Count > 0) Select(documents[Math.Clamp(index, 0, documents.Count - 1)]);
        else DocumentSelected();
        SaveSession();
    }

    private void CloseAll()
    {
        foreach (var document in documents.ToList()) if (ConfirmDiscard(document)) RemoveDocument(document); else return;
    }

    private void SwitchTab(int delta)
    {
        if (documents.Count < 2) return;
        var index = (DocumentTabs.SelectedIndex + delta + documents.Count) % documents.Count;
        DocumentTabs.SelectedIndex = index;
    }

    // MARK: Editing commands

    private void ShowFind()
    {
        if (Current == null) return;
        ApplicationCommands.Find.Execute(null, Current.Editor.TextArea);
    }

    private void GoToLine()
    {
        if (Current is not { } document) return;
        var text = Prompt.Ask(this, "Go To Line", $"Line number (1 - {document.Editor.Document.LineCount}):", document.CaretPosition.Line.ToString());
        if (int.TryParse(text, out var line)) document.Reveal(line);
    }

    private void QuickOpen()
    {
        if (workspace == null) { OpenFile(); return; }
        if (QuickOpenWindow.Pick(this, workspace) is { } file) OpenPath(file);
    }

    private void ToggleComment()
    {
        if (Current is not { } document || document.Language.LineComment is not { } marker) return;
        var editor = document.Editor;
        var start = editor.Document.GetLineByOffset(editor.SelectionStart);
        var end = editor.Document.GetLineByOffset(editor.SelectionStart + editor.SelectionLength);
        var lines = Enumerable.Range(start.LineNumber, end.LineNumber - start.LineNumber + 1).Select(editor.Document.GetLineByNumber).ToList();
        var texts = lines.Select(l => editor.Document.GetText(l)).ToList();
        var content = texts.Where(t => t.Trim().Length > 0).ToList();
        var commented = content.Count > 0 && content.All(t => t.TrimStart().StartsWith(marker, StringComparison.OrdinalIgnoreCase));
        var indent = content.Count == 0 ? 0 : content.Min(t => t.Length - t.TrimStart().Length);
        editor.Document.BeginUpdate();
        foreach (var line in lines.AsEnumerable().Reverse())
        {
            var text = editor.Document.GetText(line);
            if (text.Trim().Length == 0) continue;
            if (commented)
            {
                var at = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                var length = marker.Length + (at + marker.Length < text.Length && text[at + marker.Length] == ' ' ? 1 : 0);
                editor.Document.Remove(line.Offset + at, length);
            }
            else
            {
                editor.Document.Insert(line.Offset + indent, marker + " ");
            }
        }
        editor.Document.EndUpdate();
    }

    private void DuplicateLine()
    {
        if (Current is not { } document) return;
        var line = document.Editor.Document.GetLineByOffset(document.Editor.CaretOffset);
        document.Editor.Document.Insert(line.EndOffset, Environment.NewLine + document.Editor.Document.GetText(line));
    }

    private void SetFontSize(double size)
    {
        settings.FontSize = Math.Clamp(size, 8, 36);
        settings.Save();
        foreach (var document in documents) document.SetFontSize(settings.FontSize);
    }

    // MARK: Toolbox

    private void UpdateToolbox()
    {
        ToolboxTree.Items.Clear();
        var language = Current?.Language;
        var query = ToolboxSearch.Text.Trim();
        var groups = language == null ? [] : ToolboxCatalog.Groups(language.Id);
        foreach (var group in groups)
        {
            var items = group.Items.Where(i => query.Length == 0 || i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (items.Count == 0) continue;
            var node = new TreeViewItem { Header = new TextBlock { Text = group.Name, FontWeight = FontWeights.SemiBold }, IsExpanded = true };
            foreach (var item in items) node.Items.Add(new TreeViewItem { Header = MakeHeader(item.Glyph, item.Name), Tag = item, ToolTip = item.Snippet.Replace("$0", "") });
            ToolboxTree.Items.Add(node);
        }
        ToolboxEmpty.Text = language == null ? "Open a file to see its controls and snippets."
            : groups.Count == 0 ? $"There are no Toolbox items for {language.Name} files. Open a XAML, HTML, Razor, C#, VB.NET, Python or JavaScript file."
            : "No matches.";
        ToolboxEmpty.Visibility = ToolboxTree.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InsertToolboxItem(ToolboxItem item)
    {
        if (Current is not { IsReadOnly: false } document) return;
        var editor = document.Editor;
        var line = editor.Document.GetLineByOffset(editor.CaretOffset);
        var lineText = editor.Document.GetText(line);
        var indent = new string(lineText.TakeWhile(char.IsWhiteSpace).ToArray());
        var (text, cursor) = item.Expanded(indent);
        var start = editor.SelectionStart;
        editor.Document.Replace(start, editor.SelectionLength, text);
        editor.CaretOffset = start + cursor;
        editor.TextArea.Focus();
    }

    // MARK: Run, build and test

    private BuildPlan? Plan() => workspace != null ? BuildSystem.Detect(workspace, Configuration) : null;

    /// <summary>The project's plan when Start should run the project rather than the current file.</summary>
    private BuildPlan? ProjectRunPlan()
    {
        if (Plan() is not { Run: not null } plan) return null;
        return Current == null || plan.Languages.Contains(Current.Language.Id) ? plan : null;
    }

    private string? CustomRunCommand => workspace != null && settings.RunCommands.TryGetValue(workspace, out var command) ? command : null;

    public void Start()
    {
        if (runner.IsRunning) return;
        if (CustomRunCommand is { } custom) { SaveAll(); RunCommand(custom, workspace!, "Running…"); return; }
        if (ProjectRunPlan() is { } plan) { SaveAll(); RunCommand(plan.Run!, plan.Folder, $"Running {plan.Name}…"); return; }
        if (Current is not { } document)
        {
            ShowError("Nothing to start", "Open a project folder or a file first.");
            return;
        }
        if (document.Path == null && !SaveAs(document)) return;
        if (document.IsDirty && !Save(document)) return;
        if (document.Language.RunCommand is not { } template)
        {
            ShowError($"CodeForge doesn't know how to run {document.Language.Name} files", "Open the project folder to run the whole project, or use Debug ▸ Set Run Command….");
            return;
        }
        var output = Path.Combine(Path.GetTempPath(), "CodeForge", "build");
        Directory.CreateDirectory(output);
        RunCommand(CommandTemplate.Expand(template, document.Path!, output), Path.GetDirectoryName(document.Path)!, $"Running {document.DisplayName}…", document.Language);
    }

    private Language? runningLanguage;

    public void RunCommand(string command, string folder, string status, Language? language = null)
    {
        runningLanguage = language;
        ShowPanel(OutputTab);
        OutputText.Clear();
        AppendOutput($"> {command}\n");
        runFolder = folder;
        openedBrowser = false;
        diagnostics = [];
        RefreshDiagnostics();
        StatusText.Text = status;
        runner.Run(command, folder);
        UpdateChrome();
    }

    private void RunFinished(int code, double seconds)
    {
        AppendOutput($"\n[Finished with exit code {code} in {seconds:0.00}s]\n");
        if (code is 9009 or 1 && runningLanguage?.InstallHint is { } hint && OutputText.Text.Contains("is not recognized"))
            AppendOutput($"A required tool wasn't found. {hint}\n");
        diagnostics = DiagnosticParser.Parse(OutputText.Text);
        RefreshDiagnostics();
        var errors = diagnostics.Count(d => d.Severity == Severity.Error);
        StatusText.Text = code == 0 ? $"Finished in {seconds:0.00}s" : errors > 0 ? $"Failed — {errors} error{(errors == 1 ? "" : "s")}" : $"Failed (exit code {code})";
        if (code != 0 && errors > 0) ShowPanel(ErrorsTab);
        UpdateChrome();
        UpdateBranch();
        var next = afterRun;
        afterRun = null;
        if (code == 0) next?.Invoke();
        if (pendingOpenFolder is { } folder)
        {
            pendingOpenFolder = null;
            if (code == 0 && Directory.Exists(folder)) SetWorkspace(folder);
        }
    }

    private void AppendOutput(string text)
    {
        OutputText.AppendText(text);
        if (OutputText.Text.Length > 2_000_000) OutputText.Text = OutputText.Text[^1_500_000..];
        OutputText.ScrollToEnd();
    }

    private void RefreshDiagnostics()
    {
        var folder = runFolder ?? workspace ?? "";
        ErrorList.ItemsSource = diagnostics.Select(d => new ErrorRow(d, SafeFullPath(d, folder))).ToList();
        var errors = diagnostics.Count(d => d.Severity == Severity.Error);
        var warnings = diagnostics.Count - errors;
        ErrorsTab.Header = diagnostics.Count == 0 ? "Error List" : $"Error List  ({errors} errors, {warnings} warnings)";
        foreach (var document in documents) document.SetDiagnostics(DiagnosticsFor(document));
    }

    private IEnumerable<Diagnostic> DiagnosticsFor(EditorDocument document)
    {
        var folder = runFolder ?? workspace ?? "";
        return document.Path == null ? [] : diagnostics.Where(d => SamePath(SafeFullPath(d, folder), document.Path));
    }

    private static string SafeFullPath(Diagnostic diagnostic, string folder)
    {
        try { return diagnostic.FullPath(folder); } catch (Exception) { return diagnostic.File; }
    }

    public void Build()
    {
        if (runner.IsRunning) return;
        SaveAll();
        if (Plan() is { } plan) { RunCommand(plan.Build, plan.Folder, $"Building {plan.Name}…"); return; }
        if (Current?.Path is { } file && Current.Language.Id is "csharp")
        {
            RunCommand($"dotnet build \"{file}\"", Path.GetDirectoryName(file)!, "Building…");
            return;
        }
        ShowError("Nothing to build", "Open a project folder (with a .sln, .csproj, .vbproj, package.json, Cargo.toml or CMakeLists.txt).");
    }

    private void Clean()
    {
        if (Plan() is { Clean: { } clean } plan) RunCommand(clean, plan.Folder, "Cleaning…");
    }

    private void RunAllTests()
    {
        if (workspace == null) return;
        if (BuildSystem.TestCommand(workspace) is not { } command)
        {
            ShowError("No tests found", "CodeForge runs dotnet test, cargo test, go test, npm test and pytest.");
            return;
        }
        SaveAll();
        RunCommand(command, workspace, "Running tests…");
    }

    private void EditRunCommand()
    {
        if (workspace == null) return;
        var current = CustomRunCommand ?? "";
        var command = Prompt.Ask(this, "Run Command", "This command runs in the project folder when you press Start. Leave it empty to use the detected project or the current file.", current);
        if (command == null) return;
        if (command.Trim().Length == 0) settings.RunCommands.Remove(workspace); else settings.RunCommands[workspace] = command.Trim();
        settings.Save();
        UpdateChrome();
    }

    // MARK: Projects

    public void NewProject()
    {
        if (NewProjectWindow.Pick(this) is not { } choice) return;
        var (template, name, location) = choice;
        var folder = Path.Combine(location, name);
        if (ProjectTemplates.CreationCommand(template, name) is { } command)
        {
            if (Directory.Exists(folder)) { ShowError($"“{name}” already exists", "Choose a different name or location."); return; }
            afterRun = () =>
            {
                SetWorkspace(folder);
                OpenPath(Path.Combine(folder, template.MainFile.Replace('/', '\\')));
            };
            RunCommand(command, location, $"Creating {template.Name}…", Languages.ById("csharp"));
            return;
        }
        try
        {
            var created = ProjectTemplates.Create(template, name, location);
            SetWorkspace(created);
            OpenPath(Path.Combine(created, template.MainFile));
        }
        catch (Exception error) { ShowError("Couldn't create the project", error.Message); }
    }

    public void CloneRepository()
    {
        var address = Prompt.Ask(this, "Clone a repository", "Repository URL, for example https://github.com/owner/project.git", "");
        if (string.IsNullOrWhiteSpace(address)) return;
        var dialog = new OpenFolderDialog { Title = "Choose the folder to put the repository in" };
        if (dialog.ShowDialog(this) != true) return;
        var name = Path.GetFileNameWithoutExtension(address.Trim().TrimEnd('/'));
        pendingOpenFolder = Path.Combine(dialog.FolderName, name);
        RunCommand($"git clone --progress {CommandTemplate.Quote(address.Trim())}", dialog.FolderName, "Cloning…");
    }

    private void AddNewItem()
    {
        var folder = SelectedFolder() ?? workspace;
        if (folder == null) return;
        var name = Prompt.Ask(this, "Add New Item", "File name (for example Customer.vb, Form2.cs, Page.html):", "Class1.vb");
        if (string.IsNullOrWhiteSpace(name)) return;
        var path = Path.Combine(folder, name.Trim());
        if (File.Exists(path)) { ShowError($"“{name}” already exists", "Choose a different name."); return; }
        var className = ProjectTemplates.Identifier(Path.GetFileNameWithoutExtension(name));
        var contents = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".vb" => $"Public Class {className}\r\n\r\nEnd Class\r\n",
            ".cs" => $"namespace {ProjectTemplates.Identifier(Path.GetFileName(workspace ?? folder))};\r\n\r\npublic class {className}\r\n{{\r\n}}\r\n",
            ".html" => "<!DOCTYPE html>\r\n<html lang=\"en\">\r\n<head>\r\n    <meta charset=\"utf-8\">\r\n    <title></title>\r\n</head>\r\n<body>\r\n\r\n</body>\r\n</html>\r\n",
            _ => "",
        };
        File.WriteAllText(path, contents);
        ReloadExplorer();
        OpenPath(path);
    }

    private void ManageNuGet()
    {
        if (Plan() is not { } plan) return;
        var package = Prompt.Ask(this, "Add NuGet Package", "Package name, optionally with a version (for example Newtonsoft.Json or Dapper 2.1.35). Leave empty to list the project's packages.", "");
        if (package == null) return;
        var parts = package.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts.Length == 0 ? "dotnet list package"
            : $"dotnet add package {CommandTemplate.Quote(parts[0])}" + (parts.Length > 1 ? $" --version {CommandTemplate.Quote(parts[1])}" : "") + " && dotnet list package";
        RunCommand(command, plan.Folder, "NuGet…");
    }

    private void OpenProjectFile()
    {
        if (workspace == null) return;
        var file = Directory.GetFiles(workspace).FirstOrDefault(f => new[] { ".csproj", ".vbproj", ".fsproj", ".slnx", ".sln", ".json" }.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
        if (file != null) OpenPath(file);
    }

    private void ShowOptions()
    {
        if (!OptionsWindow.Edit(this, settings)) return;
        settings.Save();
        Themes.Apply(settings.Theme);
        SetFontSize(settings.FontSize);
    }

    // MARK: Chrome

    private void UpdateChrome()
    {
        var document = Current;
        Title = (document != null ? document.DisplayName + (document.IsDirty ? " ●" : "") + " - " : "") + (workspace != null ? Path.GetFileName(workspace.TrimEnd('\\')) + " - " : "") + "CodeForge";
        var plan = CustomRunCommand == null ? ProjectRunPlan() : null;
        StartLabel.Text = CustomRunCommand != null || plan != null ? Path.GetFileName(workspace!.TrimEnd('\\')) : document?.DisplayName ?? "Start";
        StartButton.ToolTip = plan != null ? $"Start {plan.Name}: {plan.Run} (F5)" : "Start (F5)";
        StartButton.IsEnabled = !runner.IsRunning;
        StopButton.IsEnabled = runner.IsRunning;
        StatusBar.Background = runner.IsRunning ? Themes.Brush("#CA5100") : workspace == null ? Themes.Brush("#68217A") : (Brush)Application.Current.Resources["AccentBrush"];
        StatusLanguage.Text = document?.Language.Name ?? "";
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (Current is not { } document) { StatusPosition.Text = ""; return; }
        var (line, column) = document.CaretPosition;
        StatusPosition.Text = $"Ln {line}, Col {column}";
    }

    private void UpdateBranch()
    {
        StatusBranch.Text = "";
        if (workspace == null) return;
        var folder = workspace;
        Task.Run(() =>
        {
            try
            {
                var info = new ProcessStartInfo("git", "branch --show-current") { WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                using var git = Process.Start(info);
                var branch = git?.StandardOutput.ReadToEnd().Trim();
                git?.WaitForExit(3000);
                if (!string.IsNullOrEmpty(branch) && git?.ExitCode == 0) Dispatcher.Invoke(() => { if (workspace == folder) StatusBranch.Text = " " + branch; });
            }
            catch (Exception) { }   // no Git, or not a repository
        });
    }

    // MARK: Helpers

    public void ShowError(string title, string message) => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void Shell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception) { }
    }

    private static bool SamePath(string? a, string? b) =>
        a != null && b != null && string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    // For the snapshot harness.
    internal void Perform(string action)
    {
        switch (action)
        {
            case "start": Start(); break;
            case "build": Build(); break;
            case "errors": ShowPanel(ErrorsTab); break;
            case "terminal": ShowPanel(TerminalTab); break;
            case "dark": settings.Theme = "dark"; Themes.Apply("dark"); break;
            case "blue": settings.Theme = "blue"; Themes.Apply("blue"); break;
            case "newproject": Dispatcher.BeginInvoke(NewProject); break;
            case "options": Dispatcher.BeginInvoke(ShowOptions); break;
            case "find": ShowFind(); break;
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodeForge.Core;
using CodeForge.Designer;
using Microsoft.Win32;

namespace CodeForge;

/// <summary>The main window: Toolbox, editor tabs, Solution Explorer, the Output / Error List / Terminal panel and the status bar.</summary>
public partial class MainWindow : Window
{
    private readonly List<EditorDocument> documents = [];
    /// <summary>Forms open in the Windows Forms designer ("Form1.vb [Design]").</summary>
    private readonly List<DesignerDocument> designers = [];
    private readonly PropertiesPane properties = new();
    private readonly Runner runner = new();
    private readonly Terminal terminal;
    private readonly Settings settings = Settings.Current;
    private string? workspace;
    private string? runFolder;
    private bool openedBrowser;
    private Action? afterRun;
    private string? pendingOpenFolder;
    private int untitledCount;
    private readonly NavigationHistory<Place> history = new();
    private bool navigatingHistory;
    private FindInFilesWindow? findWindow;

    /// <summary>A place in the code, for Navigate Backward and Forward.</summary>
    private readonly record struct Place(string Path, int Line);
    private List<Diagnostic> diagnostics = [];

    public string? Workspace => workspace;
    public EditorDocument? Current => (DocumentTabs.SelectedItem as TabItem)?.Tag as EditorDocument;
    public DesignerDocument? CurrentDesigner => (DocumentTabs.SelectedItem as TabItem)?.Tag as DesignerDocument;
    private int OpenTabs => documents.Count + designers.Count;
    private BuildConfiguration Configuration => settings.Configuration == "Release" ? BuildConfiguration.Release : BuildConfiguration.Debug;

    public MainWindow()
    {
        InitializeComponent();
        terminal = new Terminal(TerminalText, TerminalInput, TerminalPrompt);
        PropertiesHost.Child = properties;
        BuildMenu();
        WireControls();
        RestoreLayout();
        UpdateChrome();
        Themes.Changed += UpdateChrome;
        Closing += (_, e) => { if (!ConfirmCloseAll()) e.Cancel = true; else SaveSession(); };
        Closed += (_, _) => { debugSession?.Stop(); runner.Stop(); terminal.Stop(); };
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
            return new MenuItem { Header = header, Command = command, InputGestureText = KeyNames.Display(gesture) };
        }
        Separator Line() => new();
        bool HasFolder() => workspace != null;
        bool HasDocument() => Current != null;
        bool HasTab() => Current != null || CurrentDesigner != null;

        Menu("_File",
            Item("New _Project…", NewProject, "Ctrl+Shift+N"),
            Item("_New File", NewFile, "Ctrl+N"),
            Line(),
            Item("_Open File…", OpenFile, "Ctrl+O"),
            Item("Open _Folder…", OpenFolder, "Ctrl+Shift+O"),
            Item("Clone _Repository…", CloneRepository),
            Line(),
            Item("_Save", SaveCurrent, "Ctrl+S", HasTab),
            Item("Save _As…", () => SaveAs(Current), "", HasDocument),
            Item("Save A_ll", SaveAll, "Ctrl+Shift+S"),
            Line(),
            Item("_Close", CloseCurrent, "Ctrl+F4", HasTab),
            Item("Close Fol_der", () => SetWorkspace(null), "", HasFolder),
            Line(),
            Item("E_xit", Close, "Alt+F4"));

        Menu("_Edit",
            Item("_Undo", Undo, "", HasTab),
            Item("_Redo", Redo, "", HasTab),
            Line(),
            Item("_Find…", () => ShowFind(), "", HasDocument),
            Item("Find in _Files…", () => ShowFindInFiles(false), "Ctrl+Shift+F"),
            Item("Replace in File_s…", () => ShowFindInFiles(true), "Ctrl+Shift+H"),
            Line(),
            Item("_Go To Line…", GoToLine, "Ctrl+G", HasDocument),
            Item("Go To _File…", QuickOpen, "Ctrl+T"),
            Line(),
            Item("Toggle Line _Comment", ToggleComment, "Ctrl+K", HasDocument),
            Item("_Duplicate Line", DuplicateLine, "Ctrl+D", HasDocument),
            Line(),
            Item("Toggle _Bookmark", ToggleBookmark, "Ctrl+F2", HasDocument),
            Item("Ne_xt Bookmark", () => GoToBookmark(false), "F2", () => settings.Bookmarks.Count > 0),
            Item("Pre_vious Bookmark", () => GoToBookmark(true), "Shift+F2", () => settings.Bookmarks.Count > 0),
            Item("Clear All Bookmarks", ClearBookmarks, "Ctrl+Shift+F2", () => settings.Bookmarks.Count > 0));

        Menu("_View",
            Item("_Code", ViewCode, "F7", () => CurrentDesigner != null || (Current?.Path is { } p && IsForm(p))),
            Item("_Designer", ViewDesigner, "Shift+F7", () => Current?.Path is { } p && IsForm(p)),
            Line(),
            Item("Solution _Explorer", () => SetPane(ExplorerPane, true), "Ctrl+Alt+L"),
            Item("_Properties Window", () => SetPane(ExplorerPane, true), "F4"),
            Item("Tool_box", () => SetPane(ToolboxPane, true), "Ctrl+Alt+X"),
            Item("_Output", () => ShowPanel(OutputTab), "Ctrl+Alt+O"),
            Item("Error L_ist", () => ShowPanel(ErrorsTab), "Ctrl+Alt+E"),
            Item("Task _List", ShowTaskList, "Ctrl+Alt+K"),
            Item("Find _Results", () => ShowPanel(FindResultsTab)),
            Item("_Terminal", () => ShowPanel(TerminalTab), "Ctrl+OemTilde"),
            Line(),
            Item("Navigate _Backward", NavigateBackward, "Ctrl+OemMinus", () => history.CanGoBack),
            Item("Navigate _Forward", NavigateForward, "Ctrl+Shift+OemMinus", () => history.CanGoForward),
            Line(),
            Item("Hide Solution Explorer", () => SetPane(ExplorerPane, false)),
            Item("Hide Toolbox", () => SetPane(ToolboxPane, false)),
            Item("Hide Bottom Panel", () => SetPanelVisible(false)),
            Line(),
            Item("Zoom _In", () => SetFontSize(settings.FontSize + 1), "Ctrl+Shift+OemPeriod"),
            Item("Zoom O_ut", () => SetFontSize(settings.FontSize - 1), "Ctrl+Shift+OemComma"),
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
            Item("_Start Debugging / Continue", StartDebugging, "F5", () => (!runner.IsRunning && debugSession == null) || debugSession?.State == DebugState.Paused),
            Item("Start _Without Debugging", Start, "Ctrl+F5", () => !runner.IsRunning && debugSession == null),
            Item("Sto_p", StopAll, "Shift+F5", () => runner.IsRunning || debugSession != null),
            Line(),
            Item("Step _Over", () => debugSession?.StepOver(), "F10", () => debugSession?.State == DebugState.Paused),
            Item("Step _Into", () => debugSession?.StepInto(), "F11", () => debugSession?.State == DebugState.Paused),
            Item("Step Ou_t", () => debugSession?.StepOut(), "Shift+F11", () => debugSession?.State == DebugState.Paused),
            Item("_Break All", () => debugSession?.Pause(), "Ctrl+Alt+Pause", () => debugSession?.State == DebugState.Running),
            Line(),
            Item("Toggle _Breakpoint", ToggleBreakpoint, "F9", HasDocument),
            Item("_Delete All Breakpoints", DeleteAllBreakpoints, "Ctrl+Shift+F9", () => settings.Breakpoints.Count > 0),
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
                $"CodeForge {Updater.CurrentVersion}\nA small Visual Studio-style IDE for Windows.\n\nBy Ahmed Al-Bazzaz\n{Updater.ProjectPage}",
                "About CodeForge", MessageBoxButton.OK, MessageBoxImage.Information)));
    }

    private void WireControls()
    {
        NewProjectButton.Click += (_, _) => NewProject();
        OpenButton.Click += (_, _) => OpenFolder();
        SaveAllButton.Click += (_, _) => SaveAll();
        BackButton.Click += (_, _) => NavigateBackward();
        ForwardButton.Click += (_, _) => NavigateForward();
        TaskList.MouseDoubleClick += (_, _) => { if (TaskList.SelectedItem is LocationRow row) OpenPath(row.FullPath, row.Line, row.Column); };
        FindResults.MouseDoubleClick += (_, _) => { if (FindResults.SelectedItem is LocationRow row) OpenPath(row.FullPath, row.Line, row.Column); };
        UndoButton.Click += (_, _) => Undo();
        RedoButton.Click += (_, _) => Redo();
        StartButton.Click += (_, _) => StartDebugging();
        StopButton.Click += (_, _) => StopAll();
        DebugContinueButton.Click += (_, _) => debugSession?.Resume();
        DebugPauseButton.Click += (_, _) => debugSession?.Pause();
        DebugStopButton.Click += (_, _) => debugSession?.Stop();
        DebugStepOverButton.Click += (_, _) => debugSession?.StepOver();
        DebugStepIntoButton.Click += (_, _) => debugSession?.StepInto();
        DebugStepOutButton.Click += (_, _) => debugSession?.StepOut();
        EvaluateButton.Click += (_, _) => EvaluateExpression();
        EvaluateInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { EvaluateExpression(); e.Handled = true; } };
        CallStackList.SelectionChanged += (_, _) =>
        {
            if (!showingFrames && CallStackList.SelectedItem is ListBoxItem { Tag: StackFrameInfo frame }) SelectFrame(frame);
        };
        BuildButton.Click += (_, _) => Build();
        ConfigurationBox.ItemsSource = new[] { "Debug", "Release" };
        ConfigurationBox.SelectedItem = settings.Configuration;
        ConfigurationBox.SelectionChanged += (_, _) =>
        {
            settings.Configuration = ConfigurationBox.SelectedItem as string ?? "Debug";
            settings.Save();
            StatusText.Text = "Configuration: " + settings.Configuration;
        };

        DocumentTabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != DocumentTabs) return;
            // Leaving a file is a place to come back to.
            if (!navigatingHistory && e.RemovedItems.Count > 0 && e.RemovedItems[0] is TabItem { Tag: EditorDocument { Path: { } path } left })
                history.Record(new Place(path, left.CaretPosition.Line));
            DocumentSelected();
        };
        PanelTabs.SelectionChanged += (_, e) => { if (e.OriginalSource == PanelTabs && PanelTabs.SelectedItem == TaskListTab) RefreshTaskList(); };
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
                if (IsForm(path)) OpenDesigner(path); else OpenPath(path);
                e.Handled = true;
            }
        };
        ExplorerTree.KeyDown += (_, e) =>
        {
            if (ExplorerTree.SelectedItem is not TreeViewItem { Tag: string path }) return;
            if (e.Key == Key.Enter && File.Exists(path)) OpenPath(path);
            if (e.Key == Key.F2) { RenameInExplorer(path); e.Handled = true; }
            if (e.Key == Key.Delete) { DeleteInExplorer(path); e.Handled = true; }
        };

        ToolboxSearch.TextChanged += (_, _) => UpdateToolbox();
        ToolboxTree.MouseDoubleClick += (_, _) =>
        {
            if (ToolboxTree.SelectedItem is TreeViewItem { Tag: ToolboxItem item }) InsertToolboxItem(item);
            if (ToolboxTree.SelectedItem is TreeViewItem { Tag: ControlKind kind } && CurrentDesigner is { } designer) designer.Surface.PlaceDefault(kind);
        };
        ToolboxTree.SelectedItemChanged += (_, _) =>
        {
            // In the designer, picking a control arms it: the next click on the form places one (Pointer disarms).
            if (CurrentDesigner is { } designer) designer.Surface.ArmedTool = (ToolboxTree.SelectedItem as TreeViewItem)?.Tag as ControlKind;
        };
        ToolboxTree.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (ToolboxTree.SelectedItem is TreeViewItem { Tag: ToolboxItem item })
                DragDrop.DoDragDrop(ToolboxTree, item.Expanded("").Text, DragDropEffects.Copy);
            else if (ToolboxTree.SelectedItem is TreeViewItem { Tag: ControlKind kind })
                DragDrop.DoDragDrop(ToolboxTree, new DataObject(DesignSurface.ToolboxFormat, kind.Name), DragDropEffects.Copy);
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
            foreach (var file in files)
            {
                if (file.StartsWith("design:") && File.Exists(file[7..])) OpenDesigner(file[7..]);
                else if (File.Exists(file)) OpenPath(file);
            }
            if (selected?.StartsWith("design:") == true && designers.FirstOrDefault(d => SamePath(d.CodePath, selected[7..])) is { } form) SelectTab(form);
            else if (selected != null && documents.FirstOrDefault(d => SamePath(d.Path, selected)) is { } document) Select(document);
        }
        DocumentSelected();   // the Start Page when nothing was reopened
        if (settings.CheckForUpdates && Environment.GetEnvironmentVariable("CF_SNAPSHOT") == null && DateTime.UtcNow - settings.LastUpdateCheck > TimeSpan.FromDays(1))
            _ = Updater.CheckAsync(this, userInitiated: false);
    }

    private void SaveSession()
    {
        settings.Workspace = workspace;
        foreach (var document in documents) StoreBookmarks(document);
        settings.OpenFiles = documents.Where(d => d.Path != null).Select(d => d.Path!).Concat(designers.Select(d => "design:" + d.CodePath)).ToList();
        settings.SelectedFile = Current?.Path ?? (CurrentDesigner != null ? "design:" + CurrentDesigner.CodePath : null);
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
        if (workspace != null && !SamePath(workspace, folder) && !CloseProject(workspace)) return;
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

    /// <summary>
    /// Closes what belongs to the project being left, like closing a solution in Visual Studio: its tabs (asking about
    /// unsaved changes first), the program it runs, its output, errors, Find Results and Task List.
    /// </summary>
    private bool CloseProject(string folder)
    {
        bool Inside(string? path) => path != null && path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        var designersInside = designers.Where(d => Inside(d.CodePath)).ToList();
        var documentsInside = documents.Where(d => Inside(d.Path)).ToList();
        if (!designersInside.All(ConfirmDiscard) || !documentsInside.All(ConfirmDiscard)) return false;
        runner.Stop();
        foreach (var designer in designersInside) RemoveDesigner(designer);
        foreach (var document in documentsInside) RemoveDocument(document);
        OutputText.Clear();
        diagnostics = [];
        runFolder = null;
        RefreshDiagnostics();
        FindSummary.Text = "";
        FindResults.ItemsSource = null;
        FindResultsTab.Header = "Find Results";
        TaskList.ItemsSource = null;
        TaskListSummary.Text = "";
        TaskListTab.Header = "Task List";
        StatusText.Text = "";
        return true;
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
        if (!isFolder && IsForm(path))
        {
            Add("View Designer", () => OpenDesigner(path));
            Add("View Code", () => OpenPath(path));
        }
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
        if (line != null && !navigatingHistory && CurrentPlace is { } here) history.Record(here);
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
        // Saving changes the size and date in the Properties window.
        document.StateChanged += () => { UpdateTab(document); UpdateChrome(); if (document == Current && !document.IsDirty) ShowFileProperties(); };
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
        if (document.Path != null && settings.Bookmarks.TryGetValue(document.Path, out var saved)) document.SetBookmarks(saved);
        if (document.Path != null && settings.Breakpoints.TryGetValue(document.Path, out var breakpointLines)) document.SetBreakpoints(breakpointLines);
        document.BreakpointsChanged += () => BreakpointsChanged(document);
    }

    private void UpdateTab(EditorDocument document)
    {
        if (TabFor(document) is { } tab) tab.Header = TabHeader(document.DisplayName, document.IsDirty, document.Path, () => CloseDocument(document));
    }

    private void UpdateTab(DesignerDocument designer)
    {
        if (DocumentTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag == designer) is { } tab)
            tab.Header = TabHeader(designer.DisplayName, designer.IsDirty, designer.DesignerPath, () => CloseDesigner(designer));
    }

    private static StackPanel TabHeader(string name, bool dirty, string? path, Action onClose)
    {
        var title = new TextBlock { Text = name + (dirty ? " ●" : ""), VerticalAlignment = VerticalAlignment.Center };
        var close = new Button
        {
            Content = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9,
            Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(8, 0, 0, 0), MinWidth = 0, MinHeight = 0,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = "Close (Ctrl+F4)",
        };
        close.Click += (_, _) => onClose();
        var header = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = path };
        header.Children.Add(title);
        header.Children.Add(close);
        header.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) onClose(); };
        return header;
    }

    private TabItem? TabFor(EditorDocument document) => DocumentTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag == document);

    private void Select(EditorDocument document)
    {
        if (TabFor(document) is { } tab) DocumentTabs.SelectedItem = tab;
        DocumentSelected();
    }

    private void ShowFileProperties()
    {
        if (CurrentDesigner == null) properties.ShowFile(Current?.Path, Current?.Language.Name, Current?.Editor.LineCount, Current?.EncodingName);
    }

    private void DocumentSelected()
    {
        StartPage.Visibility = OpenTabs == 0 ? Visibility.Visible : Visibility.Collapsed;
        DocumentTabs.Visibility = OpenTabs == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (OpenTabs == 0) StartPageView.Fill(StartPage, this);
        if (Current is { } document) Dispatcher.BeginInvoke(() => document.Editor.TextArea.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        properties.Show(CurrentDesigner?.Surface);
        ShowFileProperties();
        if (CurrentDesigner is { } designer)
        {
            designer.Surface.Select(designer.Surface.SelectedName);
            Dispatcher.BeginInvoke(() => designer.Surface.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }
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
            StoreBookmarks(document);
            settings.Save();
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
        foreach (var designer in designers.Where(d => d.IsDirty).ToList()) SaveDesigner(designer);
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

    private bool ConfirmCloseAll() => designers.ToList().All(ConfirmDiscard) && documents.ToList().All(ConfirmDiscard);

    private void CloseDocument(EditorDocument document)
    {
        if (ConfirmDiscard(document)) RemoveDocument(document);
    }

    private void RemoveDocument(EditorDocument document)
    {
        StoreBookmarks(document);
        var index = documents.IndexOf(document);
        documents.Remove(document);
        document.Detach();
        if (TabFor(document) is { } tab) DocumentTabs.Items.Remove(tab);
        if (documents.Count > 0) Select(documents[Math.Clamp(index, 0, documents.Count - 1)]);
        else if (designers.Count > 0) SelectTab(designers[^1]);
        else DocumentSelected();
        SaveSession();
    }

    private void CloseAll()
    {
        foreach (var designer in designers.ToList()) if (ConfirmDiscard(designer)) RemoveDesigner(designer); else return;
        foreach (var document in documents.ToList()) if (ConfirmDiscard(document)) RemoveDocument(document); else return;
    }

    private void SwitchTab(int delta)
    {
        if (OpenTabs < 2) return;
        var index = (DocumentTabs.SelectedIndex + delta + OpenTabs) % OpenTabs;
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
        if (CurrentDesigner != null)
        {
            // Visual Studio's Windows Forms tabs: Pointer, then Common Controls and Containers.
            var query0 = ToolboxSearch.Text.Trim();
            var pointer = new TreeViewItem { Header = MakeHeader("\uE8B0", "Pointer"), IsSelected = true };
            foreach (var (groupName, containers) in new[] { ("Common Controls", false), ("Containers", true) })
            {
                var node = new TreeViewItem { Header = new TextBlock { Text = groupName, FontWeight = FontWeights.SemiBold }, IsExpanded = true };
                if (!containers) node.Items.Add(pointer);
                foreach (var kind in ControlKinds.All.Where(k => k.IsContainer == containers && (query0.Length == 0 || k.Name.Contains(query0, StringComparison.OrdinalIgnoreCase))))
                    node.Items.Add(new TreeViewItem { Header = MakeHeader(kind.Glyph, kind.Name), Tag = kind, ToolTip = $"{kind.Name}: click it, then click the form (or drag it there, or double-click it)" });
                if (node.Items.Count > 0) ToolboxTree.Items.Add(node);
            }
            ToolboxEmpty.Visibility = Visibility.Collapsed;
            return;
        }
        var language = Current?.Language;
        var query = ToolboxSearch.Text.Trim();
        var groups = language == null ? [] : ToolboxCatalog.Groups(language.Id).ToList();
        // The code snippets for this language: they also expand from their shortcut with Tab.
        if (language != null && Snippets.ForLanguage(language.Id).ToList() is { Count: > 0 } snippets)
            groups.Add(new ToolboxGroup("Code Snippets", snippets.Select(s => s.ToolboxItem with { Name = $"{s.Title}  ({s.Shortcut})" }).ToList()));
        foreach (var group in groups)
        {
            var items = group.Items.Where(i => query.Length == 0 || i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (items.Count == 0) continue;
            var node = new TreeViewItem { Header = new TextBlock { Text = group.Name, FontWeight = FontWeights.SemiBold }, IsExpanded = true };
            foreach (var item in items) node.Items.Add(new TreeViewItem { Header = MakeHeader(item.Glyph, item.Name), Tag = item, ToolTip = item.Snippet.Replace("$0", "") });
            ToolboxTree.Items.Add(node);
        }
        ToolboxEmpty.Text = language == null ? "Open a file to see its controls and snippets."
            : groups.Count == 0 ? $"There are no Toolbox items for {language.Name} files. Open a XAML, HTML, Razor, C#, VB.NET, F#, Python, JavaScript, TypeScript, C, C++, Java, Go or Rust file."
            : "No matches.";
        ToolboxEmpty.Visibility = ToolboxTree.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The last Toolbox insertion, while nothing has been typed since: (document, end of the inserted text, document length and caret right after it).</summary>
    private (EditorDocument Document, ICSharpCode.AvalonEdit.Document.TextAnchor End, int Length, int Caret)? lastToolboxInsert;

    private void InsertToolboxItem(ToolboxItem item)
    {
        if (Current is not { IsReadOnly: false } document) return;
        var editor = document.Editor;
        // Inserting twice in a row puts the second item after the first, on its own line, rather than inside it
        // (the caret is left inside the first, ready for its content).
        if (lastToolboxInsert is { } last && last.Document == document && !last.End.IsDeleted && editor.SelectionLength == 0 &&
            editor.Document.TextLength == last.Length && editor.CaretOffset == last.Caret)
        {
            var endLine = editor.Document.GetLineByOffset(last.End.Offset);
            var endIndent = new string(editor.Document.GetText(endLine).TakeWhile(char.IsWhiteSpace).ToArray());
            var at = last.End.Offset;
            var gap = Environment.NewLine + endIndent;
            editor.Document.Insert(at, gap);
            editor.CaretOffset = at + gap.Length;
        }
        var line = editor.Document.GetLineByOffset(editor.CaretOffset);
        var lineText = editor.Document.GetText(line);
        var indent = new string(lineText.TakeWhile(char.IsWhiteSpace).ToArray());
        var (text, cursor) = item.Expanded(indent);
        var start = editor.SelectionStart;
        editor.Document.Replace(start, editor.SelectionLength, text);
        editor.CaretOffset = start + cursor;
        var end = editor.Document.CreateAnchor(start + text.Length);
        lastToolboxInsert = (document, end, editor.Document.TextLength, editor.CaretOffset);
        editor.TextArea.Focus();
    }

    // MARK: Debugging

    private DebugSession? debugSession;
    private StackFrameInfo? selectedFrame;
    private bool showingFrames;

    /// <summary>Where CodeForge keeps the debuggers it downloads (netcoredbg, debugpy).</summary>
    private static string DebuggerFolder(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeForge", name);

    /// <summary>
    /// F5, as in Visual Studio: debugs a .NET program or a Python file, continues when paused, and runs anything else
    /// without debugging (test projects run their tests, class libraries say they can't start).
    /// </summary>
    private void StartDebugging()
    {
        if (debugSession is { State: DebugState.Paused }) { debugSession.Resume(); return; }
        if (debugSession != null || runner.IsRunning) return;
        if (CustomRunCommand == null && ProjectRunPlan() != null && workspace != null &&
            DebugTools.DotNetProject(workspace) is { } project && BuildSystem.LibraryProject(workspace) == null)
        {
            var text = File.ReadAllText(project);
            if (BuildSystem.IsTestProject(text) || text.Contains("<UseMaui>true</UseMaui>")) { Start(); return; }
            StartDotNetDebugging(project);
            return;
        }
        if (CustomRunCommand == null && ProjectRunPlan() == null && Current is { Language.Id: "python" } document)
        {
            StartPythonDebugging(document);
            return;
        }
        Start();
    }

    private void StopAll()
    {
        if (debugSession != null) debugSession.Stop();
        else runner.Stop();
    }

    /// <summary>C#, VB.NET and F#: build a Debug build, then run it under netcoredbg (downloaded once, with the user's OK).</summary>
    private async void StartDotNetDebugging(string project)
    {
        var folder = DebuggerFolder("netcoredbg");
        var netcoredbg = DebugTools.NetcoredbgPath(folder);
        if (!File.Exists(netcoredbg))
        {
            var answer = MessageBox.Show(this,
                "CodeForge debugs C#, VB.NET and F# with netcoredbg, Samsung's open-source .NET debugger (MIT license). " +
                "It will download it from GitHub (about 10 MB) into its own folder. This happens once.\n\nInstall it now?",
                "Install the .NET debugger?", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;
            if (!await DownloadNetcoredbg(folder) || !File.Exists(netcoredbg)) return;
        }
        if (ToolCheck.Find("dotnet") is not { } dotnet)
        {
            ShowError("The .NET SDK wasn't found", "Install it from https://dot.net, then restart CodeForge.");
            return;
        }
        SaveAll();
        var projectFolder = Path.GetDirectoryName(project)!;
        // Always a Debug build, whatever the toolbar says: a Release build can't stop on most lines.
        afterRun = () =>
        {
            if (DebugTools.DotNetAssembly(project) is not { } assembly)
            {
                ShowError("The program wasn't found", $"The build didn't produce {Path.GetFileNameWithoutExtension(project)}.dll in bin\\Debug.");
                return;
            }
            StartSession(DebugTools.DotNet(netcoredbg, dotnet, assembly, projectFolder), Path.GetFileName(assembly));
        };
        RunCommand("dotnet build -c Debug " + CommandTemplate.Quote(project), projectFolder, "Building for debugging…");
    }

    private async Task<bool> DownloadNetcoredbg(string folder)
    {
        ShowPanel(OutputTab);
        OutputText.Clear();
        AppendOutput($"Downloading netcoredbg from {DebugTools.NetcoredbgDownloadUrl}…\n");
        StatusText.Text = "Installing the .NET debugger…";
        try
        {
            using var http = new System.Net.Http.HttpClient();
            var bytes = await http.GetByteArrayAsync(DebugTools.NetcoredbgDownloadUrl);
            var archive = Path.Combine(Path.GetTempPath(), "netcoredbg-win64.zip");
            await File.WriteAllBytesAsync(archive, bytes);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(archive, folder, overwriteFiles: true);
            File.Delete(archive);
            AppendOutput($"netcoredbg is installed in {folder}\n");
            StatusText.Text = "The .NET debugger is installed";
            return true;
        }
        catch (Exception error)
        {
            AppendOutput($"The download failed: {error.Message}\n");
            ShowError("Couldn't install the .NET debugger", error.Message);
            StatusText.Text = "Ready";
            return false;
        }
    }

    /// <summary>Python files run under debugpy, installed with pip into CodeForge's folder the first time.</summary>
    private void StartPythonDebugging(EditorDocument document)
    {
        if (document.Path == null && !SaveAs(document)) return;
        SaveAll();
        if (ToolCheck.Find("python") is not { } python || python.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
        {
            ShowError("Python wasn't found", "Install Python 3 from https://www.python.org (or: winget install Python.Python.3.13), then restart CodeForge.");
            return;
        }
        var file = document.Path!;
        var folder = workspace != null && file.StartsWith(workspace, StringComparison.OrdinalIgnoreCase) ? workspace : Path.GetDirectoryName(file)!;
        var debugpy = DebuggerFolder("debugpy");
        void Launch() => StartSession(DebugTools.Python(python, debugpy, file, folder), Path.GetFileName(file));
        if (DebugTools.DebugpyInstalled(debugpy)) { Launch(); return; }
        var answer = MessageBox.Show(this,
            "CodeForge debugs Python with debugpy. It will install it with pip (about 20 MB) into its own folder. This happens once.\n\nInstall it now?",
            "Install the Python debugger?", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        afterRun = Launch;
        RunCommand($"{CommandTemplate.Quote(python)} -m pip install --disable-pip-version-check --upgrade --target {CommandTemplate.Quote(debugpy)} debugpy",
                   folder, "Installing the Python debugger…");
    }

    private void StartSession(DebugConfiguration configuration, string title)
    {
        foreach (var document in documents) StoreBreakpoints(document);
        var session = new DebugSession(configuration, action => Dispatcher.BeginInvoke(action));
        session.Breakpoints = () => settings.Breakpoints.ToDictionary(b => b.Key, b => (IReadOnlyCollection<int>)b.Value, StringComparer.OrdinalIgnoreCase);
        session.Output += (text, _) =>
        {
            AppendOutput(ConsoleText.StripAnsi(text.Replace("\r\n", "\n")));
            if (!openedBrowser && BuildSystem.ListeningAddress(text) is { } address)
            {
                openedBrowser = true;
                AppendOutput($"Opening {address} in your browser…\n");
                if (Environment.GetEnvironmentVariable("CF_NO_BROWSER") == null) Shell(address);
            }
        };
        session.Stopped += DebuggerStopped;
        session.StateChanged += DebugStateChanged;
        ShowPanel(OutputTab);
        openedBrowser = false;
        AppendOutput($"\nDebugging {title}\n");
        if (!session.Start())
        {
            ShowError("Couldn't start the debugger", $"{Path.GetFileName(configuration.Adapter)} didn't start.");
            return;
        }
        debugSession = session;
        DebugBar.Visibility = Visibility.Visible;
        DebugStateChanged();
    }

    private void DebuggerStopped()
    {
        if (debugSession is not { } session) return;
        SelectFrame(session.Frames.FirstOrDefault(f => f.File != null && File.Exists(f.File)) ?? session.Frames.FirstOrDefault());
        ShowPanel(DebugTab);
        Activate();
    }

    private void SelectFrame(StackFrameInfo? frame)
    {
        if (debugSession is not { } session || frame == null) return;
        selectedFrame = frame;
        foreach (var document in documents) document.SetExecutionLine(0);
        if (frame.File != null && File.Exists(frame.File))
        {
            OpenPath(frame.File);
            Current?.SetExecutionLine(frame.Line);
        }
        showingFrames = true;
        CallStackList.Items.Clear();
        foreach (var each in session.Frames)
        {
            var location = each.File != null ? $"  —  {Path.GetFileName(each.File)}:{each.Line}" : "";
            var item = new ListBoxItem { Content = each.Name + location, Tag = each, IsSelected = each.Id == frame.Id };
            if (each.File == null || !File.Exists(each.File)) item.Foreground = (Brush)Application.Current.Resources["SubtleTextBrush"];
            CallStackList.Items.Add(item);
        }
        showingFrames = false;
        session.Locals(frame.Id, variables =>
        {
            LocalsTree.Items.Clear();
            foreach (var variable in variables) LocalsTree.Items.Add(VariableItem(variable));
        });
    }

    /// <summary>A Locals row: name = value (type). Objects, lists and arrays expand on demand.</summary>
    private TreeViewItem VariableItem(DebugVariable variable)
    {
        var header = new TextBlock();
        header.Inlines.Add(new System.Windows.Documents.Run(variable.Name) { FontWeight = FontWeights.SemiBold });
        header.Inlines.Add(new System.Windows.Documents.Run(" = " + variable.Value));
        if (variable.Type.Length > 0)
            header.Inlines.Add(new System.Windows.Documents.Run("   " + variable.Type) { Foreground = (Brush)Application.Current.Resources["SubtleTextBrush"] });
        var item = new TreeViewItem { Header = header, Tag = variable, ToolTip = $"{variable.Name} = {variable.Value}" };
        if (variable.Reference > 0)
        {
            item.Items.Add(new TreeViewItem { Header = "Loading…" });
            item.Expanded += (_, e) =>
            {
                if (e.OriginalSource != item || variable.Children != null) return;
                variable.Children = [];
                debugSession?.Variables(variable.Reference, children =>
                {
                    variable.Children = children;
                    item.Items.Clear();
                    foreach (var child in children) item.Items.Add(VariableItem(child));
                });
            };
        }
        return item;
    }

    private void EvaluateExpression()
    {
        var expression = EvaluateInput.Text.Trim();
        if (expression.Length == 0) return;
        if (debugSession is not { State: DebugState.Paused } session)
        {
            EvaluateResult.Text = "Expressions can be evaluated while the program is paused (at a breakpoint).";
            return;
        }
        session.Evaluate(expression, selectedFrame?.Id, result => EvaluateResult.Text = $"{expression} = {result}");
    }

    private void DebugStateChanged()
    {
        if (debugSession is not { } session) return;
        var paused = session.State == DebugState.Paused;
        DebugContinueButton.IsEnabled = paused;
        DebugPauseButton.IsEnabled = session.State == DebugState.Running;
        DebugStepOverButton.IsEnabled = DebugStepIntoButton.IsEnabled = DebugStepOutButton.IsEnabled = paused;
        DebugStatus.Text = session.State switch
        {
            DebugState.Starting => "Starting…",
            DebugState.Running => "Running",
            DebugState.Paused => "Paused — " + session.StopReason,
            _ => "Ended",
        };
        if (session.State == DebugState.Running)
        {
            foreach (var document in documents) document.SetExecutionLine(0);
            StatusText.Text = "Debugging…";
        }
        else if (paused) StatusText.Text = "Paused";
        if (session.State == DebugState.Ended)
        {
            debugSession = null;
            selectedFrame = null;
            foreach (var document in documents) document.SetExecutionLine(0);
            LocalsTree.Items.Clear();
            CallStackList.Items.Clear();
            EvaluateResult.Text = "";
            DebugBar.Visibility = Visibility.Collapsed;
            StatusText.Text = "Debugging ended";
        }
        UpdateChrome();
        CommandManager.InvalidateRequerySuggested();
    }

    private void ToggleBreakpoint()
    {
        if (Current is not { } document) return;
        if (document.Path == null) { StatusText.Text = "Save the file first to set a breakpoint"; return; }
        document.ToggleBreakpoint(document.CaretPosition.Line);
    }

    private void BreakpointsChanged(EditorDocument document)
    {
        if (document.Path == null) { StatusText.Text = "Save the file first to set a breakpoint"; document.SetBreakpoints([]); return; }
        StoreBreakpoints(document);
        settings.Save();
        debugSession?.SendBreakpoints(document.Path, document.BreakpointLines);
    }

    private void StoreBreakpoints(EditorDocument document)
    {
        if (document.Path == null) return;
        var lines = document.BreakpointLines;
        if (lines.Count == 0) settings.Breakpoints.Remove(document.Path); else settings.Breakpoints[document.Path] = lines;
    }

    private void DeleteAllBreakpoints()
    {
        foreach (var (file, _) in settings.Breakpoints) debugSession?.SendBreakpoints(file, []);
        settings.Breakpoints.Clear();
        foreach (var document in documents) document.SetBreakpoints([]);
        settings.Save();
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
        if (workspace != null && CustomRunCommand == null && BuildSystem.LibraryProject(workspace) is { } library && ProjectRunPlan() != null)
        {
            ShowPanel(OutputTab);
            OutputText.Clear();
            AppendOutput($"{library} is a class library, so it can't be started directly. Use Build to compile it, or add it to a program that uses it.\n");
            StatusText.Text = "A class library can't be started";
            return;
        }
        if (ProjectRunPlan() is { } plan)
        {
            if (!ToolIsInstalled(plan.Run!)) return;
            SaveAll();
            RunCommand(plan.Run!, plan.Folder, $"Running {plan.Name}…");
            return;
        }
        if (Current is not { } document)
        {
            ShowError("Nothing to start", "Open a project folder or a file first.");
            return;
        }
        if (document.Path == null && !SaveAs(document)) return;
        if (document.IsDirty && !Save(document)) return;
        if (document.Language.Id is "html")
        {
            ShowPanel(OutputTab);
            OutputText.Clear();
            AppendOutput($"Opening {document.DisplayName} in your browser…\n");
            OpenInBrowser(document.Path!);
            StatusText.Text = $"Opened {document.DisplayName}";
            return;
        }
        if (document.Language.RunCommand is not { } template)
        {
            ShowError($"CodeForge doesn't know how to run {document.Language.Name} files", "Open the project folder to run the whole project, or use Debug ▸ Set Run Command….");
            return;
        }
        if (!ToolIsInstalled(template)) return;
        var output = Path.Combine(Path.GetTempPath(), "CodeForge", "build");
        Directory.CreateDirectory(output);
        RunCommand(CommandTemplate.Expand(template, document.Path!, output), Path.GetDirectoryName(document.Path)!, $"Running {document.DisplayName}…", document.Language);
    }

    private Language? runningLanguage;

    /// <summary>Says what to install, instead of running a command that can only fail with "is not recognized".</summary>
    private bool ToolIsInstalled(string command)
    {
        if (ToolCheck.Program(command) is not { } program || ToolCheck.IsAvailable(program)) return true;
        ShowPanel(OutputTab);
        OutputText.Clear();
        var name = Path.GetFileNameWithoutExtension(program);
        AppendOutput($"“{name}” isn't installed (or isn't on PATH), so this can't run yet.\n{ToolCheck.InstallHint(program) ?? "Install it, then restart CodeForge."}\n");
        StatusText.Text = $"{name} isn't installed";
        return false;
    }

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
        if (runner.WasStopped)
        {
            AppendOutput($"\n[Stopped after {seconds:0.00}s]\n");
            StatusText.Text = "Stopped";
            afterRun = null;
            pendingOpenFolder = null;
            UpdateChrome();
            return;
        }
        AppendOutput($"\n[Finished with exit code {ExitCodes.Describe(code)} in {seconds:0.00}s]\n");
        if (code is 9009 or 1 && runningLanguage?.InstallHint is { } hint && OutputText.Text.Contains("is not recognized"))
            AppendOutput($"A required tool wasn't found. {hint}\n");
        diagnostics = DiagnosticParser.Parse(OutputText.Text);
        RefreshDiagnostics();
        var errors = diagnostics.Count(d => d.Severity == Severity.Error);
        StatusText.Text = code == 0 ? $"Finished in {seconds:0.00}s" : errors > 0 ? $"Failed — {errors} error{(errors == 1 ? "" : "s")}" : $"Failed (exit code {ExitCodes.Describe(code)})";
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
        // A carriage return on its own redraws the line (progress counters): keep only its last version.
        var current = OutputText.Text;
        var lineStart = current.LastIndexOf('\n') + 1;
        if (text.Contains('\r') || (current.Length > 0 && current[^1] == '\r'))
        {
            OutputText.Select(lineStart, current.Length - lineStart);
            OutputText.SelectedText = ConsoleText.Rewrite(current[lineStart..], text);
            OutputText.Select(OutputText.Text.Length, 0);
        }
        else OutputText.AppendText(text);
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
        if (Plan() is { } plan)
        {
            if (ToolIsInstalled(plan.Build)) RunCommand(plan.Build, plan.Folder, $"Building {plan.Name}…");
            return;
        }
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

    // MARK: Find in Files, the Task List, bookmarks and navigation

    private string? SelectedText => Current is { Editor.SelectionLength: > 0 } document ? document.Editor.SelectedText : null;

    private FindInFilesWindow FindWindow => findWindow ??= new FindInFilesWindow(this);

    private void ShowFindInFiles(bool replacing) => FindWindow.Open(replacing, SelectedText);

    /// <summary>What Find in Files searches: the project folder, the current file's folder and the open files with their current text.</summary>
    internal (string? Project, string? CurrentFolder, List<(string Path, string Text)> Open) SearchContext() =>
        (workspace, Current?.Path is { } path ? Path.GetDirectoryName(path) : null,
         documents.Where(d => d.Path != null).Select(d => (d.Path!, d.Editor.Text)).ToList());

    internal void ShowFindResults(string summary, List<SearchMatch> matches)
    {
        FindSummary.Text = summary;
        FindResults.ItemsSource = matches.Select(m => new LocationRow(m.Path, m.Line, m.Column, m.Preview.Trim())).ToList();
        FindResultsTab.Header = matches.Count == 0 ? "Find Results" : $"Find Results  ({matches.Count})";
        ShowPanel(FindResultsTab);
    }

    /// <summary>Replace in Files, for files open in tabs: through the editor, so Ctrl+Z undoes it. Returns the files it handled and the count.</summary>
    internal (HashSet<string> Handled, int Count) ReplaceInOpenDocuments(System.Text.RegularExpressions.Regex expression, string replacement, bool regular, ISet<string> paths)
    {
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var document in documents.Where(d => d.Path != null && paths.Contains(d.Path)))
        {
            var (text, replaced) = FileSearch.Replace(document.Editor.Text, expression, replacement, regular);
            handled.Add(document.Path!);
            if (replaced == 0) continue;
            document.Editor.Document.Replace(0, document.Editor.Document.TextLength, text);
            count += replaced;
        }
        return (handled, count);
    }

    internal void SetStatus(string text) => StatusText.Text = text;

    private void ShowTaskList()
    {
        ShowPanel(TaskListTab);
        RefreshTaskList();
    }

    /// <summary>Scans the project for TODO comments in the background; open files count with their current, possibly unsaved, text.</summary>
    private void RefreshTaskList()
    {
        var folder = workspace;
        var open = documents.Where(d => d.Path != null).Select(d => (Path: d.Path!, Text: d.Editor.Text, d.Language)).ToList();
        Task.Run(() =>
        {
            var tasks = folder != null ? TaskScanner.InFolder(folder) : [];
            foreach (var (path, text, language) in open)
            {
                tasks.RemoveAll(t => SamePath(t.Path, path));
                tasks.AddRange(TaskScanner.Tasks(text, path, language));
            }
            var ordered = tasks.OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Line).ToList();
            Dispatcher.Invoke(() =>
            {
                TaskList.ItemsSource = ordered.Select(t => new LocationRow(t.Path, t.Line, null, t.Text.Length == 0 ? t.Token : $"{t.Token}: {t.Text}")).ToList();
                var files = ordered.Select(t => t.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                TaskListSummary.Text = ordered.Count == 0 ? "No TODO, FIXME, HACK, UNDONE, BUG or XXX comments found."
                    : $"{ordered.Count} task{(ordered.Count == 1 ? "" : "s")} in {files} file{(files == 1 ? "" : "s")}";
                TaskListTab.Header = ordered.Count == 0 ? "Task List" : $"Task List  ({ordered.Count})";
            });
        });
    }

    private void StoreBookmarks(EditorDocument document)
    {
        if (document.Path == null) return;
        var lines = document.BookmarkLines;
        if (lines.Count == 0) settings.Bookmarks.Remove(document.Path); else settings.Bookmarks[document.Path] = lines;
    }

    private void ToggleBookmark()
    {
        if (Current is not { } document) return;
        if (document.Path == null) { StatusText.Text = "Save the file first to bookmark it"; return; }
        document.ToggleBookmark(document.CaretPosition.Line);
        StoreBookmarks(document);
        settings.Save();
    }

    private void GoToBookmark(bool backward)
    {
        foreach (var document in documents) StoreBookmarks(document);
        var existing = settings.Bookmarks.Where(b => File.Exists(b.Key)).ToDictionary(b => b.Key, b => b.Value, StringComparer.OrdinalIgnoreCase);
        var place = (Current?.Path ?? "", Current?.CaretPosition.Line ?? 0);
        if (Bookmarks.Next(place, existing, backward) is { } target) OpenPath(target.Path, target.Line);
        else StatusText.Text = "No bookmarks yet: toggle one with Ctrl+F2";
    }

    private void ClearBookmarks()
    {
        settings.Bookmarks.Clear();
        foreach (var document in documents) document.SetBookmarks([]);
        settings.Save();
    }

    private Place? CurrentPlace => Current is { Path: { } path } document ? new Place(path, document.CaretPosition.Line) : null;

    private void GoTo(Place place)
    {
        navigatingHistory = true;
        OpenPath(place.Path, place.Line);
        navigatingHistory = false;
    }

    private void NavigateBackward()
    {
        if (history.TryGoBack(CurrentPlace ?? new Place("", 0), out var place)) GoTo(place);
    }

    private void NavigateForward()
    {
        if (CurrentPlace is { } here && history.TryGoForward(here, out var place)) GoTo(place);
    }

    // MARK: Windows Forms designer

    /// <summary>A .vb or .cs file with a Form1.Designer file next to it that holds a form.</summary>
    private static bool IsForm(string path)
    {
        if (!(path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))) return false;
        try { return FormDesign.DesignerFileFor(path) is { } designer && FormDesign.IsFormDesigner(File.ReadAllText(designer)); }
        catch (IOException) { return false; }
    }

    public void OpenDesigner(string path)
    {
        var codePath = path.Contains(".Designer.", StringComparison.OrdinalIgnoreCase) ? FormDesign.CodeFileFor(path) : path;
        if (designers.FirstOrDefault(d => SamePath(d.CodePath, codePath)) is { } open) { SelectTab(open); return; }
        DesignerDocument designer;
        try { designer = new DesignerDocument(codePath); }
        catch (Exception error) { ShowError($"Couldn't open “{Path.GetFileName(codePath)}” in the designer", error.Message); OpenPath(codePath); return; }
        designers.Add(designer);
        DocumentTabs.Items.Add(new TabItem { Tag = designer, Content = designer.Surface });
        designer.StateChanged += () => { UpdateTab(designer); UpdateChrome(); };
        // After the double-click's own mouse handling, which would otherwise keep the designer in front.
        designer.Surface.HandlerRequested += name => Dispatcher.BeginInvoke(() => AddEventHandler(designer, name), System.Windows.Threading.DispatcherPriority.Background);
        designer.Surface.ViewCodeRequested += () => OpenPath(designer.CodePath);
        designer.Surface.ToolUsed += () => { if (CurrentDesigner == designer) UpdateToolbox(); };
        designer.Surface.Renamed += (oldName, newName) => RenameInCode(designer, oldName, newName);
        UpdateTab(designer);
        SelectTab(designer);
    }

    private void SelectTab(DesignerDocument designer)
    {
        if (DocumentTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag == designer) is { } tab) DocumentTabs.SelectedItem = tab;
        DocumentSelected();
    }

    private void ViewCode()
    {
        if (CurrentDesigner is { } designer) OpenPath(designer.CodePath);
    }

    private void ViewDesigner()
    {
        if (Current?.Path is { } path && IsForm(path)) OpenDesigner(path);
    }

    /// <summary>Double-clicking a control (or the form) opens its default event handler in the code, creating it if needed.</summary>
    private void AddEventHandler(DesignerDocument designer, string? controlName)
    {
        var kind = controlName != null ? designer.Design.Control(controlName)?.Kind : null;
        var eventName = controlName == null ? "Load" : kind?.DefaultEvent ?? "Click";
        var eventArgs = controlName == null ? "EventArgs" : kind?.EventArgs ?? "EventArgs";
        OpenPath(designer.CodePath);
        if (Current is not { } code || !SamePath(code.Path, designer.CodePath)) return;
        var before = designer.Design.Generate();
        designer.Snapshot();
        var (text, line) = designer.Design.AddHandler(code.Editor.Text, controlName, eventName, eventArgs);
        if (designer.Design.Generate() != before) designer.Changed(); else designer.DropSnapshot();
        if (text != code.Editor.Text) code.Editor.Document.Replace(0, code.Editor.Document.TextLength, text);
        var indent = designer.Language == "vb" ? 9 : 9;
        Dispatcher.BeginInvoke(() => code.Reveal(line, indent), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>VB.NET's Handles clauses use control names; keep them in step when a control is renamed.</summary>
    private void RenameInCode(DesignerDocument designer, string oldName, string newName)
    {
        if (designer.Language != "vb") return;
        if (documents.FirstOrDefault(d => SamePath(d.Path, designer.CodePath)) is { } open)
        {
            var updated = FormDesign.RenameInCode(open.Editor.Text, "vb", oldName, newName);
            if (updated != open.Editor.Text) open.Editor.Document.Replace(0, open.Editor.Document.TextLength, updated);
        }
        else if (File.Exists(designer.CodePath))
        {
            var text = File.ReadAllText(designer.CodePath);
            var updated = FormDesign.RenameInCode(text, "vb", oldName, newName);
            if (updated != text) File.WriteAllText(designer.CodePath, updated);
        }
    }

    private bool SaveDesigner(DesignerDocument designer)
    {
        try
        {
            designer.Save();
            // A text tab of the designer file shows the new code (unless it has its own unsaved edits).
            if (documents.FirstOrDefault(d => SamePath(d.Path, designer.DesignerPath)) is { IsDirty: false } text)
            {
                text.Editor.Text = File.ReadAllText(designer.DesignerPath);
                text.Editor.IsModified = false;
            }
            return true;
        }
        catch (Exception error)
        {
            ShowError($"Couldn't save “{designer.DisplayName}”", error.Message);
            return false;
        }
    }

    private bool ConfirmDiscard(DesignerDocument designer)
    {
        if (!designer.IsDirty) return true;
        SelectTab(designer);
        var answer = MessageBox.Show(this, $"Save changes to “{designer.DisplayName}”?", "CodeForge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch { MessageBoxResult.Yes => SaveDesigner(designer), MessageBoxResult.No => true, _ => false };
    }

    private void CloseDesigner(DesignerDocument designer)
    {
        if (ConfirmDiscard(designer)) RemoveDesigner(designer);
    }

    private void RemoveDesigner(DesignerDocument designer)
    {
        designers.Remove(designer);
        if (DocumentTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag == designer) is { } tab) DocumentTabs.Items.Remove(tab);
        designer.Surface.Close();
        if (documents.Count > 0) Select(documents[^1]);
        else if (designers.Count > 0) SelectTab(designers[^1]);
        else DocumentSelected();
        SaveSession();
    }

    private void SaveCurrent()
    {
        if (CurrentDesigner is { } designer) SaveDesigner(designer); else Save(Current);
    }

    private void CloseCurrent()
    {
        if (CurrentDesigner is { } designer) CloseDesigner(designer);
        else if (Current is { } document) CloseDocument(document);
    }

    private void Undo()
    {
        if (CurrentDesigner is { } designer) designer.Undo(); else Current?.Editor.Undo();
    }

    private void Redo()
    {
        if (CurrentDesigner is { } designer) designer.Redo(); else Current?.Editor.Redo();
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
        var designerTitle = CurrentDesigner is { } d0 ? d0.DisplayName + (d0.IsDirty ? " ●" : "") + " - " : "";
        Title = designerTitle + (document != null ? document.DisplayName + (document.IsDirty ? " ●" : "") + " - " : "") + (workspace != null ? Path.GetFileName(workspace.TrimEnd('\\')) + " - " : "") + "CodeForge";
        var plan = CustomRunCommand == null ? ProjectRunPlan() : null;
        StartLabel.Text = CustomRunCommand != null || plan != null ? Path.GetFileName(workspace!.TrimEnd('\\')) : document?.DisplayName ?? "Start";
        StartButton.ToolTip = plan != null ? $"Start {plan.Name}: {plan.Run} (F5 debugs, Ctrl+F5 runs without debugging)" : "Start (F5)";
        StartButton.IsEnabled = (!runner.IsRunning && debugSession == null) || debugSession?.State == DebugState.Paused;
        StopButton.IsEnabled = runner.IsRunning || debugSession != null;
        StatusBar.Background = runner.IsRunning || debugSession != null ? Themes.Brush("#C2410C") : workspace == null ? Themes.Brush("#475569") : (Brush)Application.Current.Resources["AccentBrush"];
        StatusLanguage.Text = document?.Language.Name ?? (CurrentDesigner != null ? "Windows Forms Designer" : "");
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

    /// <summary>
    /// Opens a local page in the default web browser. Opening the .html file itself shows Windows' "Open with" box when no
    /// program is set for .html files, even though a browser is set for web links, so this asks for the https handler.
    /// </summary>
    public static void OpenInBrowser(string path)
    {
        var address = new Uri(path).AbsoluteUri;
        try
        {
            string? ProgId(string scheme) => Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey($@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}\UserChoice")?.GetValue("ProgId") as string;
            if ((ProgId("https") ?? ProgId("http")) is { } progId &&
                BrowserCommand.Build(Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command")?.GetValue("") as string, address) is { } browser &&
                File.Exists(browser.Program))
            {
                Process.Start(new ProcessStartInfo(browser.Program, browser.Arguments) { UseShellExecute = false });
                return;
            }
        }
        catch (Exception) { }
        Shell(address);
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
            case "dark" or "gray" or "midnight" or "light" or "blue" or "vs-dark" or "vs-light": settings.Theme = action; Themes.Apply(action); break;
            case "tasks": ShowTaskList(); break;
            case "report":
                File.AppendAllText(Environment.GetEnvironmentVariable("CF_REPORT") ?? "report.txt", $"{Current?.DisplayName}:{Current?.CaretPosition.Line}\n");
                break;
            case "stop": runner.Stop(); break;
            case "dump":
                // Status, open tabs and output, for checks in scripts/screenshots.ps1.
                File.AppendAllText(Environment.GetEnvironmentVariable("CF_REPORT") ?? "report.txt",
                    $"STATUS: {StatusText.Text}\nTABS: {string.Join(",", documents.Select(d => d.DisplayName))}\nOUTPUT:\n{OutputText.Text}\n");
                break;
            case var a when a.StartsWith("open:"): OpenPath(a[5..]); break;
            case "togglebookmark": ToggleBookmark(); break;
            case "nextbookmark": GoToBookmark(false); break;
            case "back": NavigateBackward(); break;
            case "forward": NavigateForward(); break;
            case "selectall": Current?.Editor.SelectAll(); break;
            case var a when a.StartsWith("find:"): FindWindow.Run(a[5..]); break;
            case var a when a.StartsWith("replace:"): FindWindow.RunReplace(a[8..].Split("=>")[0], a[8..].Split("=>")[1]); break;
            case var a when a.StartsWith("goto:"): Current?.Reveal(int.Parse(a[5..])); break;
            case var a when a.StartsWith("type:") && Current is { } typing:
                // "\t" presses Tab (expanding a snippet) and "\n" types a line break, anywhere in the text.
                var pieces = a[5..].Split("\\t");
                for (var i = 0; i < pieces.Length; i++)
                {
                    typing.Editor.Document.Insert(typing.Editor.CaretOffset, pieces[i].Replace("\\n", "\n"));
                    if (i < pieces.Length - 1) typing.ExpandSnippet();
                }
                break;
            case "newproject": Dispatcher.BeginInvoke(NewProject); break;
            case "options": Dispatcher.BeginInvoke(ShowOptions); break;
            case "find": ShowFind(); break;
            case "design": ViewDesigner(); break;
            case "code": ViewCode(); break;
            case "handler": if (CurrentDesigner is { } h) AddEventHandler(h, h.Surface.SelectedName); break;
            case "saveall": SaveAll(); break;
            case var a when a.StartsWith("add:") && CurrentDesigner is { } d1:
                var parts = a.Split(':');
                if (ControlKinds.Find(parts[1]) is { } kind) d1.Surface.Place(kind, new Point(double.Parse(parts[2]), double.Parse(parts[3])));
                break;
            case var a when a.StartsWith("select:") && CurrentDesigner is { } d2: d2.Surface.Select(a[7..] == "form" ? null : a[7..]); break;
            case var a when a.StartsWith("set:") && CurrentDesigner is { } d3:
                var equals = a.IndexOf('=');
                // Commas separate actions, so values with commas (sizes) are written with semicolons.
                if (d3.Surface.SetFromText(a[4..equals], a[(equals + 1)..].Replace(';', ',')) is { } problem) Console.Error.WriteLine("SET: " + problem);
                break;
        }
    }
}

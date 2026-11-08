using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodeForge.Core;

namespace CodeForge;

/// <summary>
/// Git Changes, beside Solution Explorer as in Visual Studio: the branch, changed files to stage and unstage, a commit
/// message, and Fetch, Pull and Push. Double-clicking a file shows its changes; its context menu can undo them.
/// </summary>
public sealed class GitPanel : DockPanel
{
    public Func<string?> Folder { get; init; } = () => null;
    public Action<string> OpenFile { get; init; } = _ => { };
    /// <summary>(title, text, language id): shows a diff or the history in a read-only tab.</summary>
    public Action<string, string, string> ShowText { get; init; } = (_, _, _) => { };
    public Action<string> Status { get; init; } = _ => { };
    /// <summary>The branch changed (or the folder isn't a repository: null), for the status bar.</summary>
    public Action<GitStatus?> StatusChanged { get; init; } = _ => { };
    public Func<Window> Owner { get; init; } = () => Application.Current.MainWindow;

    public GitStatus? Current { get; private set; }

    /// <summary>The commit message being written.</summary>
    public string Message { get => message.Text; set => message.Text = value; }

    private readonly ComboBox branches = new() { MinWidth = 120, Margin = new Thickness(8, 4, 4, 4), ToolTip = "Branch" };
    private readonly TextBlock sync = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), Opacity = 0.7 };
    private readonly TextBox message = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56, MaxHeight = 140, Margin = new Thickness(8, 4, 8, 4),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly TextBlock placeholder = new()
    {
        Text = "Enter a message", IsHitTestVisible = false, Opacity = 0.5, Margin = new Thickness(14, 10, 0, 0), VerticalAlignment = VerticalAlignment.Top,
    };
    private readonly Button commit = new() { Content = "Commit All", Margin = new Thickness(8, 0, 8, 6), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly DockPanel content = new();
    private readonly StackPanel notRepository = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16) };
    private readonly TextBlock notRepositoryText = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Opacity = 0.7 };
    private bool busy;
    private bool fillingBranches;
    private int generation;

    private const string NewBranch = "New Branch…";

    public GitPanel()
    {
        Background = Brushes.Transparent;
        var title = new DockPanel();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 4, 0) };
        DockPanel.SetDock(tools, Dock.Right);
        tools.Children.Add(Icon("", "Fetch", Fetch));
        tools.Children.Add(Icon("", "Pull", Pull));
        tools.Children.Add(Icon("", "Push", Push));
        tools.Children.Add(Icon("", "View Branch History", ShowHistory));
        tools.Children.Add(Icon("", "Refresh", Refresh));
        title.Children.Add(tools);
        title.Children.Add(new TextBlock { Text = "GIT CHANGES", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 8, 10, 6), Opacity = 0.7 });
        SetDock(title, Dock.Top);
        Children.Add(title);

        var branchRow = new DockPanel();
        DockPanel.SetDock(sync, Dock.Right);
        branchRow.Children.Add(sync);
        branchRow.Children.Add(branches);
        SetDock(branchRow, Dock.Top);
        content.Children.Add(branchRow);
        var messageHost = new Grid();
        messageHost.Children.Add(message);
        messageHost.Children.Add(placeholder);
        SetDock(messageHost, Dock.Top);
        content.Children.Add(messageHost);
        SetDock(commit, Dock.Top);
        content.Children.Add(commit);
        content.Children.Add(list);
        Children.Add(content);

        notRepository.Children.Add(notRepositoryText);
        var create = new Button { Content = "Create Git Repository", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        create.Click += (_, _) => CreateRepository();
        notRepository.Children.Add(create);
        Children.Add(notRepository);

        message.TextChanged += (_, _) => placeholder.Visibility = message.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        message.PreviewKeyDown += (_, e) =>
        {
            // Ctrl+Enter commits, as in Visual Studio.
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Commit(); }
        };
        commit.Click += (_, _) => Commit();
        branches.SelectionChanged += (_, _) => BranchChosen();
        list.MouseDoubleClick += (_, e) =>
        {
            if (Selected is { } change && e.OriginalSource is not Button) ShowDiff(change);
        };
        list.ContextMenuOpening += (_, e) => { if (Selected == null) e.Handled = true; };
        list.ContextMenu = new ContextMenu();
        list.ContextMenu.Items.Add(Item("Open", () => { if (Selected is { } c && Folder() is { } f) OpenFile(Path.Combine(f, c.Path)); }));
        list.ContextMenu.Items.Add(Item("Compare with Unmodified", () => { if (Selected is { } c) ShowDiff(c); }));
        list.ContextMenu.Items.Add(Item("Stage / Unstage", () => { if (Selected is { } c) ToggleStage(c); }));
        list.ContextMenu.Items.Add(new Separator());
        list.ContextMenu.Items.Add(Item("Undo Changes…", () => { if (Selected is { } c) UndoChanges(c); }));
        ShowState(null, isRepository: false);
    }

    private GitChange? Selected => (list.SelectedItem as ListBoxItem)?.Tag as GitChange;

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static Button Icon(string glyph, string tip, Action action)
    {
        var button = new Button
        {
            Content = glyph, ToolTip = tip, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12,
            Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(1, 0, 1, 0), MinWidth = 0, MinHeight = 0,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        };
        button.Click += (_, _) => action();
        return button;
    }

    // MARK: Status

    public async void Refresh()
    {
        var folder = Folder();
        var request = ++generation;
        if (folder == null)
        {
            ShowState(null, isRepository: false);
            return;
        }
        var result = await GitCommand.RunAsync(folder, "status", "--porcelain=v1", "--branch", "--untracked-files=all");
        var names = await GitCommand.RunAsync(folder, "branch", "--format=%(refname:short)");
        if (request != generation) return;
        var status = result.Succeeded ? GitStatus.Parse(result.Output) : null;
        ShowState(status, result.Succeeded, gitMissing: GitCommand.Program() == null);
        fillingBranches = true;
        branches.Items.Clear();
        if (status != null)
        {
            var all = names.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (all.Count == 0) all.Add(status.Branch);
            foreach (var name in all) branches.Items.Add(name);
            branches.Items.Add(new Separator());
            branches.Items.Add(NewBranch);
            branches.SelectedItem = status.Branch;
        }
        fillingBranches = false;
    }

    private void ShowState(GitStatus? status, bool isRepository, bool gitMissing = false)
    {
        Current = status;
        content.Visibility = isRepository ? Visibility.Visible : Visibility.Collapsed;
        notRepository.Visibility = isRepository || Folder() == null ? Visibility.Collapsed : Visibility.Visible;
        notRepositoryText.Text = gitMissing ? "Git isn't installed. Install it with: winget install Git.Git" : "This folder isn't a Git repository.";
        StatusChanged(status);
        list.Items.Clear();
        if (status == null) return;
        var staged = status.Changes.Where(c => c.IsStaged).ToList();
        var unstaged = status.Changes.Where(c => !c.IsStaged || c.Unstaged != ' ')
            .Select(c => c with { Staged = c.IsUntracked ? '?' : ' ', Unstaged = c.IsUntracked ? '?' : c.Unstaged }).ToList();
        if (staged.Count > 0)
        {
            list.Items.Add(Header($"Staged Changes ({staged.Count})"));
            foreach (var change in staged) list.Items.Add(Row(change));
        }
        if (unstaged.Count > 0)
        {
            list.Items.Add(Header($"Changes ({unstaged.Count})"));
            foreach (var change in unstaged) list.Items.Add(Row(change));
        }
        if (list.Items.Count == 0) list.Items.Add(Header("No changes"));
        commit.Content = staged.Count == 0 ? "Commit All" : $"Commit Staged ({staged.Count})";
        commit.IsEnabled = status.Changes.Count > 0;
        var counts = new List<string>();
        if (status.Ahead > 0) counts.Add($"↑{status.Ahead}");
        if (status.Behind > 0) counts.Add($"↓{status.Behind}");
        sync.Text = status.Upstream == null ? "no remote branch" : string.Join(" ", counts);
    }

    private static ListBoxItem Header(string text) => new()
    {
        Content = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = 0.65, Margin = new Thickness(2, 6, 0, 2) },
        IsEnabled = false, Focusable = false,
    };

    private ListBoxItem Row(GitChange change)
    {
        var color = change.Letter switch { "A" or "U" => "#2EA043", "D" or "!" => "#D73A49", "R" => "#2F80ED", _ => "#D29922" };
        var row = new DockPanel { LastChildFill = true };
        var letter = new TextBlock { Text = change.Letter, Width = 14, FontWeight = FontWeights.Bold, Foreground = Themes.Brush(color), FontFamily = new FontFamily("Cascadia Mono, Consolas") };
        DockPanel.SetDock(letter, Dock.Left);
        row.Children.Add(letter);
        var toggle = new Button
        {
            Content = change.IsStaged ? "" : "", ToolTip = change.IsStaged ? "Unstage" : "Stage",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, Padding = new Thickness(5, 2, 5, 2),
            MinWidth = 0, MinHeight = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        };
        toggle.Click += (_, _) => ToggleStage(change);
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(toggle);
        var names = new StackPanel { Orientation = Orientation.Horizontal };
        names.Children.Add(new TextBlock { Text = Path.GetFileName(change.Path) });
        var folder = Path.GetDirectoryName(change.Path.Replace('/', Path.DirectorySeparatorChar));
        if (!string.IsNullOrEmpty(folder)) names.Children.Add(new TextBlock { Text = folder, Opacity = 0.55, FontSize = 11, Margin = new Thickness(6, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(names);
        return new ListBoxItem { Content = row, Tag = change, ToolTip = change.OriginalPath != null ? $"{change.OriginalPath} → {change.Path}" : change.Path };
    }

    // MARK: Actions

    /// <summary>Runs git, shows its message if it failed, then refreshes.</summary>
    private async void Git(string[] arguments, string? success = null, Action? then = null)
    {
        if (Folder() is not { } folder || busy) return;
        busy = true;
        Status($"git {arguments[0]}…");
        var result = await GitCommand.RunAsync(folder, arguments);
        busy = false;
        if (result.Succeeded)
        {
            Status(success ?? "Ready");
            then?.Invoke();
        }
        else if (GitIdentity.IsMissing(result.Message))
        {
            // A first commit on this PC: ask who's committing, as Visual Studio does, then try again.
            Status("Git needs your name and email");
            if (AskIdentity(folder)) { Git(arguments, success, then); return; }
        }
        else
        {
            Status($"git {arguments[0]} failed");
            MessageBox.Show(Owner(), result.Message, $"git {arguments[0]} failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Refresh();
    }

    /// <summary>Asks for the name and email Git records with commits, and saves them for every repository.</summary>
    private bool AskIdentity(string folder)
    {
        var name = Prompt.Ask(Owner(), "Your name for Git",
            "Git records a name and an email address with each commit. They're saved for every repository on this PC (git config --global). Your name:",
            Environment.UserName)?.Trim();
        if (string.IsNullOrEmpty(name)) return false;
        var email = Prompt.Ask(Owner(), "Your email for Git", "Your email address (the one your GitHub account uses, if you have one):", "")?.Trim();
        if (string.IsNullOrEmpty(email)) return false;
        return GitCommand.Run(folder, "config", "--global", "user.name", name).Succeeded &&
               GitCommand.Run(folder, "config", "--global", "user.email", email).Succeeded;
    }

    private void ToggleStage(GitChange change)
    {
        if (change.IsStaged) Git(["restore", "--staged", "--", change.Path]);
        else Git(["add", "--", change.Path]);
    }

    public void Commit()
    {
        var text = message.Text.Trim();
        if (text.Length == 0)
        {
            message.Focus();
            Status("Enter a commit message first");
            return;
        }
        if (Current == null || Current.Changes.Count == 0) { Status("Nothing to commit"); return; }
        void Done() => Git(["commit", "-m", text], $"Committed: {text.Split('\n')[0]}", () => message.Clear());
        if (Current.Changes.Any(c => c.IsStaged)) Done();
        else Git(["add", "-A"], then: Done);
    }

    public void Fetch() => Git(["fetch", "--prune"], "Fetched");

    public void Pull() => Git(["pull", "--ff-only"], "Pulled");

    public void Push()
    {
        if (Current is not { } status) return;
        if (status.Upstream == null) Git(["push", "--set-upstream", "origin", status.Branch], $"Pushed {status.Branch} to origin");
        else Git(["push"], "Pushed");
    }

    public void CreateBranch()
    {
        if (Current is not { } status) return;
        var name = Prompt.Ask(Owner(), "New Branch", $"The new branch starts from {status.Branch} and becomes the current branch.", "")?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        Git(["switch", "-c", name], $"Created and switched to {name}");
    }

    private void BranchChosen()
    {
        if (fillingBranches || branches.SelectedItem is not string chosen || Current == null || chosen == Current.Branch) return;
        if (chosen == NewBranch)
        {
            fillingBranches = true;
            branches.SelectedItem = Current.Branch;
            fillingBranches = false;
            CreateBranch();
            return;
        }
        Git(["switch", chosen], $"Switched to {chosen}");
    }

    /// <summary>git init, with a .gitignore for build output (bin, obj, node_modules…) as Visual Studio adds.</summary>
    public void CreateRepository() => Git(["init"], "Created a Git repository", () =>
    {
        if (Folder() is not { } folder) return;
        var ignore = Path.Combine(folder, ".gitignore");
        if (!File.Exists(ignore)) File.WriteAllText(ignore, GitIdentity.DefaultIgnore);
    });

    private async void ShowDiff(GitChange change)
    {
        if (Folder() is not { } folder) return;
        if (change.IsUntracked) { OpenFile(Path.Combine(folder, change.Path)); return; }
        var result = await GitCommand.RunAsync(folder, change.IsStaged ? ["diff", "--cached", "--", change.Path] : ["diff", "--", change.Path]);
        ShowText($"{Path.GetFileName(change.Path)} (changes)", result.Output.Length == 0 ? "No differences." : result.Output, "diff");
    }

    private void UndoChanges(GitChange change)
    {
        var name = Path.GetFileName(change.Path);
        var answer = MessageBox.Show(Owner(), change.IsUntracked
                ? $"“{name}” is a new file. It will be moved to the Recycle Bin."
                : $"Your edits to “{name}” since the last commit will be lost.",
            $"Undo the changes to “{name}”?", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK || Folder() is not { } folder) return;
        if (change.IsUntracked)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(Path.Combine(folder, change.Path),
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            catch (Exception exception)
            {
                MessageBox.Show(Owner(), exception.Message, "Couldn't move the file", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Refresh();
        }
        else
        {
            Git(["restore", "--staged", "--worktree", "--", change.Path], $"Undid the changes to {name}");
        }
    }

    public async void ShowHistory()
    {
        if (Folder() is not { } folder) return;
        var result = await GitCommand.RunAsync(folder, "log", "--graph", "--date=relative", "-n", "300", "--pretty=format:%h  %ad  %an%d%n    %s");
        ShowText($"History — {Current?.Branch}", result.Succeeded ? result.Output : result.Message, "text");
    }

    /// <summary>Who last changed each line of a file (Annotate in Visual Studio).</summary>
    public async void ShowBlame(string path)
    {
        if (Folder() is not { } folder) return;
        var result = await GitCommand.RunAsync(folder, "blame", "--date=relative", "-w", "--", Path.GetRelativePath(folder, path));
        ShowText($"{Path.GetFileName(path)} (blame)", result.Succeeded ? result.Output : result.Message, "text");
    }
}


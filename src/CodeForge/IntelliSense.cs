using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodeForge.Core;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace CodeForge;

/// <summary>
/// Completion lists, Quick Info, Go To Definition, Find All References and live errors for one window. Files whose language
/// server is installed (clangd, Pyright or Jedi, TypeScript, csharp-ls, FsAutoComplete, gopls, rust-analyzer, JDT LS,
/// SourceKit-LSP) ask it; the others get keywords, snippets and the file's words.
/// </summary>
public sealed class IntelliSense
{
    /// <summary>The project folder, for starting servers and searching without one.</summary>
    public Func<string?> Workspace { get; init; } = () => null;
    public Func<IReadOnlyList<EditorDocument>> Documents { get; init; } = () => [];
    public Action<string, int, int?> OpenLocation { get; init; } = (_, _, _) => { };
    public Action<string, List<SearchMatch>> ShowReferences { get; init; } = (_, _) => { };
    public Action<string> Status { get; init; } = _ => { };
    /// <summary>Live errors changed: the window refreshes the Error List and squiggles.</summary>
    public Action DiagnosticsChanged { get; init; } = () => { };
    /// <summary>Runs an install command in the Output window, then the action if it succeeded.</summary>
    public Action<string, string, Action> RunInstall { get; init; } = (_, _, _) => { };
    public Func<Window> Owner { get; init; } = () => Application.Current.MainWindow;

    public LanguageServerLocator Locator { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeForge", "LanguageServers"));

    private readonly Dictionary<string, LanguageClient> clients = [];
    private readonly Dictionary<EditorDocument, LanguageDocument> sessions = [];
    private readonly Dictionary<EditorDocument, DispatcherTimer> pendingChanges = [];
    private readonly Dictionary<string, List<Diagnostic>> live = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Languages whose missing server was already mentioned in the status bar.</summary>
    private readonly HashSet<string> mentioned = [];
    private CompletionWindow? completion;
    private ToolTip? hoverTip;
    private int completionGeneration;
    private int hoverGeneration;

    /// <summary>Errors and warnings the language servers reported, by file.</summary>
    public IEnumerable<Diagnostic> LiveDiagnostics => live.Values.SelectMany(d => d);

    public bool HasServer(EditorDocument document) => sessions.ContainsKey(document);

    public string? ServerName(EditorDocument document) => sessions.TryGetValue(document, out var session)
        ? clients.FirstOrDefault(c => c.Value == session.Client).Key?.Split('|')[0] : null;

    // MARK: Documents

    public void Attach(EditorDocument document)
    {
        var area = document.Editor.TextArea;
        area.TextEntered += (_, e) => TextEntered(document, e.Text);
        area.PreviewMouseDown += (_, _) => CloseHover();
        area.PreviewKeyDown += (_, _) => CloseHover();
        area.TextEntering += (_, e) =>
        {
            // With the list open, . ( ; [ , accept the selected name first, as in Visual Studio; other punctuation closes it.
            if (completion == null || e.Text.Length == 0 || BasicIntelliSense.IsIdentifier(e.Text[0])) return;
            if (".(;[,".Contains(e.Text[0]) && completion.CompletionList.SelectedItem != null && completion.EndOffset > completion.StartOffset)
                completion.CompletionList.RequestInsertion(e);
            else
                completion.Close();
        };
        area.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            // Ctrl+Click goes to the definition, as in Visual Studio.
            if (document.Editor.GetPositionFromPoint(e.GetPosition(document.Editor)) is not { } position) return;
            e.Handled = true;
            GoToDefinition(document, document.Editor.Document.GetOffset(position.Location));
        };
        document.Editor.MouseHover += (_, e) =>
        {
            if (document.Editor.GetPositionFromPoint(e.GetPosition(document.Editor)) is not { } position) return;
            ShowQuickInfo(document, document.Editor.Document.GetOffset(position.Location), atCaret: false);
        };
        document.Editor.MouseHoverStopped += (_, _) => CloseHover();
        document.Editor.TextChanged += (_, _) => Changed(document);
        document.Saved += () =>
        {
            // Save As can give the file a new name or language.
            if (sessions.TryGetValue(document, out var session) && !SamePath(session.Path, document.Path)) Detach(document);
            if (sessions.TryGetValue(document, out session)) { Flush(document); session.Saved(); }
            else Open(document);
        };
        Open(document);
    }

    public void Detach(EditorDocument document)
    {
        if (pendingChanges.Remove(document, out var timer)) timer.Stop();
        if (!sessions.Remove(document, out var session)) return;
        session.Closed();
        if (live.Remove(session.Path)) DiagnosticsChanged();
    }

    /// <summary>Opens a file in its language server, starting the server for the project if it isn't running.</summary>
    private void Open(EditorDocument document)
    {
        if (document.Path is not { } path || sessions.ContainsKey(document)) return;
        if (Client(document.Language.Id, path) is not { } client) return;
        sessions[document] = new LanguageDocument(client, path, document.Language.Id, () => document.Editor.Text);
    }

    private LanguageClient? Client(string languageId, string path)
    {
        if (Locator.Find(languageId) is not { } found)
        {
            if (Locator.Installable(languageId) is { } spec && mentioned.Add(languageId))
                Status($"Edit ▸ IntelliSense ▸ Install {spec.Name} gives {Languages.ById(languageId).Name} files full IntelliSense");
            return null;
        }
        var root = RootFor(path);
        var key = found.Spec.Name + "|" + root;
        if (clients.TryGetValue(key, out var running) && running.IsRunning) return running;
        var client = new LanguageClient(action => Application.Current.Dispatcher.BeginInvoke(action));
        client.DiagnosticsPublished += (file, list) =>
        {
            if (list.Count == 0 && !live.ContainsKey(file)) return;
            if (list.Count == 0) live.Remove(file); else live[file] = list;
            DiagnosticsChanged();
        };
        client.Exited += () =>
        {
            foreach (var document in sessions.Where(s => s.Value.Client == client).Select(s => s.Key).ToList()) sessions.Remove(document);
            clients.Remove(key);
        };
        if (!client.Start(found.Launch, root))
        {
            Status($"{found.Spec.Name} didn't start");
            return null;
        }
        clients[key] = client;
        Status($"IntelliSense: {found.Spec.Name} is reading {Path.GetFileName(root.TrimEnd('\\'))}…");
        client.WhenReady(() => Status($"IntelliSense: {found.Spec.Name}"));
        return client;
    }

    /// <summary>The open folder if the file is in it, otherwise the file's own folder.</summary>
    private string RootFor(string path)
    {
        var folder = Workspace();
        if (folder != null && path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Path.TrimEndingDirectorySeparator(folder);
        return Path.GetDirectoryName(path)!;
    }

    /// <summary>The server gets the whole text a moment after typing stops.</summary>
    private void Changed(EditorDocument document)
    {
        if (!sessions.ContainsKey(document)) return;
        if (!pendingChanges.TryGetValue(document, out var timer))
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (_, _) => Flush(document);
            pendingChanges[document] = timer;
        }
        timer.Stop();
        timer.Start();
    }

    /// <summary>Sends a waiting edit now, so a question sees the latest text.</summary>
    private void Flush(EditorDocument document)
    {
        if (!pendingChanges.TryGetValue(document, out var timer) || !timer.IsEnabled) return;
        timer.Stop();
        if (sessions.TryGetValue(document, out var session)) session.Changed();
    }

    public void StopAll()
    {
        foreach (var client in clients.Values) client.Shutdown();
        clients.Clear();
        sessions.Clear();
        live.Clear();
    }

    /// <summary>Stops the servers for a project being closed.</summary>
    public void CloseFolder(string folder)
    {
        foreach (var (key, client) in clients.Where(c => SamePath(c.Key.Split('|')[1], Path.TrimEndingDirectorySeparator(folder))).ToList())
        {
            client.Shutdown();
            clients.Remove(key);
            foreach (var document in sessions.Where(s => s.Value.Client == client).Select(s => s.Key).ToList()) sessions.Remove(document);
        }
        foreach (var file in live.Keys.Where(f => f.StartsWith(folder, StringComparison.OrdinalIgnoreCase)).ToList()) live.Remove(file);
        DiagnosticsChanged();
    }

    // MARK: Install

    /// <summary>Installs the language server for the current file's language (asking first), then opens the file in it.</summary>
    public void Install(EditorDocument? document)
    {
        if (document == null) return;
        var language = document.Language;
        PathVariable.Refresh();
        if (Locator.Find(language.Id) is { } found)
        {
            MessageBox.Show(Owner(), $"{found.Spec.Name} is already installed and gives {language.Name} files IntelliSense.", "IntelliSense",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var spec = Locator.Installable(language.Id);
        if (spec == null || Locator.InstallCommand(spec) is not { } command)
        {
            var hint = LanguageServerSpecs.For(language.Id).FirstOrDefault()?.InstallHint;
            MessageBox.Show(Owner(), hint != null ? $"{language.Name} IntelliSense needs a language server. {hint}" :
                    $"{language.Name} files get keywords, snippets and words from the file; there's no language server for them.",
                "IntelliSense", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var size = spec.DownloadSize != null ? $" ({spec.DownloadSize})" : "";
        var answer = MessageBox.Show(Owner(),
            $"CodeForge gives {language.Name} files completion, Quick Info, Go To Definition and live errors with {spec.Name}. " +
            $"It will download it{size} into its own folder with {spec.Installer}:\n\n{command}\n\nInstall it now?",
            $"Install {spec.Name}?", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        Directory.CreateDirectory(Locator.InstallFolder);
        RunInstall(command, Locator.InstallFolder, () =>
        {
            mentioned.Remove(language.Id);
            foreach (var open in Documents().Where(d => d.Language.Id == language.Id)) Open(open);
            Status(Locator.Find(language.Id) != null ? $"{spec.Name} is installed" : $"{spec.Name} didn't install");
        });
    }

    // MARK: Completion

    private void TextEntered(EditorDocument document, string text)
    {
        CloseHover();
        if (completion != null || text.Length != 1 || document.IsReadOnly) return;
        var typed = text[0];
        var triggers = sessions.TryGetValue(document, out var session) ? session.Client.TriggerCharacters : new HashSet<string> { "." };
        var caret = document.Editor.CaretOffset;
        var start = BasicIntelliSense.WordStart(document.Editor.Text, caret);
        // A trigger character (. :: -> …), or the first letter of a new word.
        if (triggers.Contains(text) && (session != null || typed == '.')) RequestCompletion(document, explicitly: false);
        else if (BasicIntelliSense.IsIdentifier(typed) && !char.IsDigit(typed) && start == caret - 1) RequestCompletion(document, explicitly: false);
    }

    /// <summary>Ctrl+Space and Ctrl+J: the list at the caret, even with nothing typed yet.</summary>
    public void Complete(EditorDocument? document)
    {
        if (document == null || document.IsReadOnly) return;
        RequestCompletion(document, explicitly: true);
    }

    private void RequestCompletion(EditorDocument document, bool explicitly)
    {
        var request = ++completionGeneration;
        var caret = document.Editor.CaretOffset;
        var text = document.Editor.Text;
        if (!sessions.TryGetValue(document, out var session))
        {
            var (items, start) = BasicIntelliSense.Completions(text, caret, document.Language);
            // Opened by typing, words alone aren't worth a list.
            if (!explicitly && items.All(i => i.Kind == 1)) return;
            Show(document, items, start, explicitly);
            return;
        }
        Flush(document);
        session.Completion(caret, items =>
        {
            if (request != completionGeneration || document.Editor.CaretOffset < caret) return;
            var start = items.Select(i => i.ReplaceStart).Where(s => s != null).Min() ?? BasicIntelliSense.WordStart(document.Editor.Text, caret);
            // Snippets too, except after a dot.
            var wordStart = BasicIntelliSense.WordStart(text, caret);
            var afterDot = wordStart > 0 && text[wordStart - 1] == '.';
            if (!afterDot)
                items.AddRange(Snippets.ForLanguage(document.Language.Id)
                    .Select(s => new CompletionEntry(s.Shortcut, s.Title, 15, s.Shortcut, s.Shortcut, "~" + s.Shortcut, Snippet: s.Body)));
            Show(document, items, Math.Min(start, document.Editor.CaretOffset), explicitly);
        });
    }

    private void Show(EditorDocument document, List<CompletionEntry> items, int start, bool explicitly)
    {
        if (items.Count == 0) { if (explicitly) Status("No suggestions here"); return; }
        var editor = document.Editor;
        if (editor.TextArea.Document == null || start > editor.CaretOffset || !editor.TextArea.IsKeyboardFocusWithin && !explicitly) return;
        var typed = editor.Document.GetText(start, editor.CaretOffset - start);
        if (typed.Length > 0 && !items.Any(i => i.FilterText.Contains(typed, StringComparison.OrdinalIgnoreCase))) return;
        completion?.Close();
        var window = new CompletionWindow(editor.TextArea)
        {
            StartOffset = start,
            EndOffset = editor.CaretOffset,
            CloseWhenCaretAtBeginning = !explicitly,
            CloseAutomatically = true,
            MinWidth = 320,
            SizeToContent = SizeToContent.Height,
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
        };
        window.CompletionList.ListBox.Background = window.Background;
        window.CompletionList.ListBox.Foreground = window.Foreground;
        window.CompletionList.ListBox.BorderThickness = new Thickness(0);
        foreach (var item in items.OrderBy(i => i.SortText, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase))
            window.CompletionList.CompletionData.Add(new CompletionItem(item));
        window.Closed += (_, _) => { if (completion == window) completion = null; };
        completion = window;
        window.Show();
        if (typed.Length > 0) window.CompletionList.SelectItem(typed);
    }

    // MARK: Quick Info

    /// <summary>The problem under the mouse and what the language server says about the symbol there.</summary>
    public void ShowQuickInfo(EditorDocument? document, int? offset = null, bool atCaret = true)
    {
        if (document == null) return;
        CloseHover();
        var at = offset ?? document.Editor.CaretOffset;
        var request = ++hoverGeneration;
        var problem = document.DiagnosticAt(at);
        void Present(string? info)
        {
            if (request != hoverGeneration) return;
            var text = string.Join("\n\n", new[] { problem, info }.Where(t => !string.IsNullOrEmpty(t)));
            if (text.Length == 0) { if (atCaret) Status("No information here"); return; }
            var content = new TextBlock
            {
                Text = text.Length > 1500 ? text[..1500] + "…" : text, TextWrapping = TextWrapping.Wrap, MaxWidth = 560,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12,
            };
            // A free-standing tooltip (not the editor's ToolTip property), so it never takes the next click.
            hoverTip = new ToolTip
            {
                Content = content, Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
                PlacementTarget = document.Editor.TextArea.TextView, IsHitTestVisible = false,
            };
            if (atCaret)
            {
                var caret = document.Editor.TextArea.Caret.CalculateCaretRectangle();
                hoverTip.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
                hoverTip.PlacementTarget = document.Editor.TextArea.TextView;
                hoverTip.HorizontalOffset = caret.Left - document.Editor.TextArea.TextView.ScrollOffset.X;
                hoverTip.VerticalOffset = caret.Bottom - document.Editor.TextArea.TextView.ScrollOffset.Y + 2;
                hoverTip.StaysOpen = false;
            }
            hoverTip.IsOpen = true;
        }
        if (sessions.TryGetValue(document, out var session) && WordAt(document.Editor.Text, at) != null)
        {
            Flush(document);
            session.Hover(at, Present);
        }
        else
        {
            Present(null);
        }
    }

    private void CloseHover()
    {
        hoverGeneration++;
        if (hoverTip == null) return;
        hoverTip.IsOpen = false;
        hoverTip = null;
    }

    // MARK: Navigation

    public void GoToDefinition(EditorDocument? document, int? offset = null)
    {
        if (document == null) return;
        var at = offset ?? document.Editor.CaretOffset;
        if (WordAt(document.Editor.Text, at) is not { } word) { Status("Put the caret on a name first"); return; }
        if (sessions.TryGetValue(document, out var session))
        {
            Flush(document);
            session.Definition(at, locations =>
            {
                if (locations.FirstOrDefault() is { } location) OpenLocation(location.Path, location.Line, location.Column);
                else Status($"No definition found for “{word.Text}”");
            });
            return;
        }
        // Without a server: a declaration of the name in this file, then in the project's files of this language.
        if (BasicIntelliSense.Declaration(document.Editor.Text, word.Text, document.Language) is { } here && document.Path is { } path)
        {
            var location = document.Editor.Document.GetLocation(here);
            OpenLocation(path, location.Line, location.Column);
            return;
        }
        foreach (var file in ProjectFiles(document.Language))
        {
            if (SamePath(file, document.Path) || FileSearch.ReadText(file) is not { } text) continue;
            if (BasicIntelliSense.Declaration(text, word.Text, document.Language) is { } found)
            {
                var (line, character) = new TextLines(text).Position(found);
                OpenLocation(file, line + 1, character + 1);
                return;
            }
        }
        Status($"No definition found for “{word.Text}”");
    }

    public void FindAllReferences(EditorDocument? document)
    {
        if (document == null) return;
        var at = document.Editor.CaretOffset;
        if (WordAt(document.Editor.Text, at) is not { } word) { Status("Put the caret on a name first"); return; }
        if (sessions.TryGetValue(document, out var session))
        {
            Flush(document);
            Status($"Finding references to “{word.Text}”…");
            session.References(word.Start, locations =>
            {
                var matches = locations.Select(l => new SearchMatch(l.Path, l.Line, l.Column, word.Text.Length, LineText(l.Path, l.Line))).ToList();
                ShowReferences($"References to “{word.Text}”", matches);
                Status($"{matches.Count} reference{(matches.Count == 1 ? "" : "s")} to “{word.Text}”");
            });
            return;
        }
        // Without a server: every whole-word use in the project's files of this language, outside strings and comments.
        var options = new SearchOptions(MatchCase: !document.Language.CaseInsensitive, WholeWord: true);
        var expression = FileSearch.Expression(word.Text, options)!;
        var results = new List<SearchMatch>();
        var files = ProjectFiles(document.Language).ToList();
        if (document.Path != null && !files.Any(f => SamePath(f, document.Path))) files.Insert(0, document.Path);
        foreach (var file in files)
        {
            var open = Documents().FirstOrDefault(d => SamePath(d.Path, file));
            if ((open?.Editor.Text ?? FileSearch.ReadText(file)) is not { } text) continue;
            var lines = new TextLines(text);
            results.AddRange(FileSearch.Matches(text, file, expression)
                .Where(m => !BasicIntelliSense.InStringOrComment(text, lines.Offset(m.Line - 1, m.Column - 1), document.Language)));
        }
        ShowReferences($"References to “{word.Text}”", results);
        Status($"{results.Count} reference{(results.Count == 1 ? "" : "s")} to “{word.Text}”");
    }

    private IEnumerable<string> ProjectFiles(Language language)
    {
        if (Workspace() is not { } folder) return [];
        return FileSearch.Files(folder, string.Join(";", language.Extensions.Select(e => "*." + e))).Take(3000);
    }

    private string LineText(string path, int line)
    {
        var open = Documents().FirstOrDefault(d => SamePath(d.Path, path));
        if (open != null && line <= open.Editor.Document.LineCount) return open.Editor.Document.GetText(open.Editor.Document.GetLineByNumber(line)).Trim();
        try { return File.ReadLines(path).Skip(line - 1).FirstOrDefault()?.Trim() ?? ""; } catch (Exception) { return ""; }
    }

    private static (string Text, int Start)? WordAt(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var start = BasicIntelliSense.WordStart(text, offset);
        var end = offset;
        while (end < text.Length && BasicIntelliSense.IsIdentifier(text[end])) end++;
        return end > start ? (text[start..end], start) : null;
    }

    private static bool SamePath(string? a, string? b) =>
        a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}

/// <summary>A row in the completion list: a colored badge for the kind, the name, and its detail on the right.</summary>
public sealed class CompletionItem(CompletionEntry entry) : ICompletionData
{
    public System.Windows.Media.ImageSource? Image => null;
    public string Text => entry.FilterText;
    public double Priority => entry.Kind == 1 ? -1 : 0;
    public object Description => entry.Detail.Length > 0 ? entry.Detail : (entry.Snippet != null ? "Snippet (Tab)" : entry.Label);

    public object Content
    {
        get
        {
            var (glyph, color) = Badge(entry.Kind);
            var row = new DockPanel { LastChildFill = true };
            var badge = new TextBlock
            {
                Text = glyph, Width = 18, FontWeight = FontWeights.SemiBold, Foreground = Themes.Brush(color),
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
            };
            DockPanel.SetDock(badge, Dock.Left);
            row.Children.Add(badge);
            if (entry.Detail.Length > 0 && entry.Kind != 14)
            {
                var detail = new TextBlock
                {
                    Text = entry.Detail.Length > 48 ? entry.Detail[..48] + "…" : entry.Detail, Opacity = 0.6, Margin = new Thickness(16, 0, 4, 0),
                    FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                };
                DockPanel.SetDock(detail, Dock.Right);
                row.Children.Add(detail);
            }
            row.Children.Add(new TextBlock { Text = entry.Label });
            return row;
        }
    }

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var document = textArea.Document;
        if (entry.Snippet != null)
        {
            var line = document.GetLineByOffset(completionSegment.Offset);
            var indent = new string(document.GetText(line).TakeWhile(char.IsWhiteSpace).ToArray());
            var (text, cursor) = new ToolboxItem(entry.Label, "", entry.Snippet).Expanded(indent);
            document.Replace(completionSegment, text);
            textArea.Caret.Offset = completionSegment.Offset + cursor;
            return;
        }
        document.Replace(completionSegment, entry.Text);
    }

    /// <summary>LSP CompletionItemKind → a letter and color, like Visual Studio's icons.</summary>
    private static (string Glyph, string Color) Badge(int kind) => kind switch
    {
        2 or 3 or 4 => ("ƒ", "#8E44AD"),
        5 or 10 => ("◆", "#2F80ED"),
        6 or 12 or 21 => ("v", "#2F80ED"),
        7 or 22 => ("C", "#E67E22"),
        8 => ("I", "#16A085"),
        9 => ("{}", "#7F8C8D"),
        13 or 20 => ("E", "#C9A400"),
        14 => ("k", "#D6336C"),
        15 => ("⋯", "#7F8C8D"),
        25 => ("T", "#16A085"),
        1 => ("ab", "#95A5A6"),
        _ => ("•", "#95A5A6"),
    };
}

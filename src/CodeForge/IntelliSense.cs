using System.IO;
using System.Text.RegularExpressions;
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
    /// <summary>Opens a file in a tab (or finds its tab), for edits to files that aren't open.</summary>
    public Func<string, EditorDocument?> OpenDocument { get; init; } = _ => null;
    /// <summary>Brings a document's tab to the front.</summary>
    public Action<EditorDocument> SelectDocument { get; init; } = _ => { };

    public LanguageServerLocator Locator { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeForge", "LanguageServers"));

    private readonly Dictionary<string, LanguageClient> clients = [];
    private readonly Dictionary<EditorDocument, LanguageDocument> sessions = [];
    private readonly Dictionary<EditorDocument, DispatcherTimer> pendingChanges = [];
    private readonly Dictionary<string, List<Diagnostic>> live = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Languages whose missing server was already mentioned in the status bar.</summary>
    private readonly HashSet<string> mentioned = [];
    /// <summary>The project's declarations, for VB.NET IntelliSense (and C# without its server); read once, then on save.</summary>
    private List<CodeSymbol>? projectSymbols;
    private string? symbolsFolder;
    private bool readingSymbols;
    private CompletionWindow? completion;
    private ToolTip? hoverTip;
    private int completionGeneration;
    private int hoverGeneration;
    private OverloadInsightWindow? insight;
    /// <summary>The "(" of the call Parameter Info shows, so arrowing to another overload sticks while typing in it.</summary>
    private int insightParen = -1;
    private int signatureGeneration;
    private DispatcherTimer? signatureRefresh;
    private readonly CompletionHistory history = new();

    /// <summary>The signature Parameter Info shows, for scripts.</summary>
    public string? ParameterInfoText => insight?.Provider is SignatureProvider provider ? provider.Text : null;

    /// <summary>Errors and warnings the language servers reported, by file.</summary>
    public IEnumerable<Diagnostic> LiveDiagnostics => live.Values.SelectMany(d => d);

    public bool HasServer(EditorDocument document) => sessions.ContainsKey(document);

    /// <summary>The names in the open completion list, for scripts.</summary>
    public IEnumerable<string> CompletionLabels => completion?.CompletionList.CompletionData.Select(d => d.Text) ?? Enumerable.Empty<string>();

    public string? ServerName(EditorDocument document) => sessions.TryGetValue(document, out var session)
        ? clients.FirstOrDefault(c => c.Value == session.Client).Key?.Split('|')[0] : null;

    // MARK: Documents

    public void Attach(EditorDocument document)
    {
        var area = document.Editor.TextArea;
        area.TextEntered += (_, e) => TextEntered(document, e.Text);
        area.Caret.PositionChanged += (_, _) => { if (insight != null && insight.TextArea == area) RefreshParameterInfoSoon(document); };
        area.LostKeyboardFocus += (_, _) => { if (insight?.TextArea == area && !area.IsKeyboardFocusWithin) CloseParameterInfo(); };
        area.PreviewMouseDown += (_, _) => CloseHover();
        area.PreviewKeyDown += (_, _) => CloseHover();
        area.TextEntering += (_, e) =>
        {
            // With the list open, . ( ; [ , accept the selected name first, as in Visual Studio; other punctuation closes it.
            if (completion == null || e.Text.Length == 0 || BasicIntelliSense.IsIdentifier(e.Text[0])) return;
            // Only a name that starts with what was typed (Visual Studio's "soft" selection otherwise), and never a
            // snippet: typing "Main(" quickly mustn't turn into another name or a whole template.
            var typed = completion.EndOffset > completion.StartOffset
                ? completion.TextArea.Document.GetText(completion.StartOffset, completion.EndOffset - completion.StartOffset) : "";
            if (".(;[,".Contains(e.Text[0]) && completion.CompletionList.SelectedItem is CompletionItem { IsSnippet: false } selected &&
                typed.Length > 0 && selected.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
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
            if (DotNetIntelliSense.Supports(document.Language.Id)) ForgetSymbols();
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
        client.EditRequested = edits => Apply(edits, "Edit") > 0 || edits.Count == 0;
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
            $"Kryndev gives {language.Name} files completion, Quick Info, Go To Definition and live errors with {spec.Name}. " +
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
        if (text.Length != 1 || document.IsReadOnly) return;
        var typed = text[0];
        var triggers = sessions.TryGetValue(document, out var session) ? session.Client.TriggerCharacters : new HashSet<string> { "." };
        // Parameter Info opens on ( and , and follows the caret through the arguments.
        if (text is "(" or "," || session?.Client.SignatureTriggerCharacters?.Contains(text) == true) ShowParameterInfo(document, explicitly: false);
        else if (insight != null) RefreshParameterInfoSoon(document);
        // Typed fast ("g."), the list for the word can still be open when the dot comes: the dot's list replaces it.
        if (completion != null)
        {
            if (!triggers.Contains(text)) return;
            completion.Close();
            completion = null;
        }
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
            var (items, start) = BasicIntelliSense.Completions(text, caret, document.Language, SymbolsFor(document));
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
        var languageId = document.Language.Id;
        sessions.TryGetValue(document, out var session);
        // Visual Studio's order (the server's, else A–Z); typing selects the best match, and of equal matches the one used last.
        foreach (var item in items.OrderBy(i => i.SortText, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase))
            window.CompletionList.CompletionData.Add(new CompletionItem(item, languageId, history.Recency(languageId, item.Label))
            {
                Resolve = session != null ? new Action<CompletionEntry, Action<string?, string?>>(session.Resolve) : null,
                Picked = picked =>
                {
                    history.Used(languageId, picked.Label);
                    // A method accepted with its parentheses: show what goes in them.
                    if (document.Editor.CaretOffset > 0 && ParameterInfo.CallAt(document.Editor.Text, document.Editor.CaretOffset, document.Language) != null)
                        document.Editor.Dispatcher.BeginInvoke(new Action(() => ShowParameterInfo(document, explicitly: false)), DispatcherPriority.Background);
                },
            });
        window.Closed += (_, _) => { if (completion == window) completion = null; };
        completion = window;
        window.Show();
        if (typed.Length > 0) window.CompletionList.SelectItem(typed);
    }

    // MARK: Parameter Info

    /// <summary>Parameter Info (Ctrl+Shift+Space): the ways to call the function the caret is in, its current argument in bold.</summary>
    public void ShowParameterInfo(EditorDocument? document, bool explicitly = true)
    {
        if (document == null) return;
        var editor = document.Editor;
        var caret = editor.CaretOffset;
        var text = editor.Text;
        var call = ParameterInfo.CallAt(text, caret, document.Language);
        var request = ++signatureGeneration;
        void FromProject()
        {
            if (request != signatureGeneration) return;
            if (call == null) { CloseParameterInfo(); if (explicitly) Status("No parameter info here: put the caret inside a call's parentheses"); return; }
            var symbols = SymbolsFor(document) ?? SymbolParser.Symbols(text, document.Language.Id, document.Path);
            var signatures = ParameterInfo.Basic(text, call, document.Language, symbols);
            if (signatures.Count == 0) { CloseParameterInfo(); if (explicitly) Status($"No parameter info for “{call.Name}”"); return; }
            PresentSignatures(document, signatures, ParameterInfo.BestOverload(signatures, call.Argument), call.Argument, call.NameStart, call.OpenParen);
        }
        if (sessions.TryGetValue(document, out var session) && session.Client.SignatureTriggerCharacters != null)
        {
            Flush(document);
            session.SignatureHelp(caret, result =>
            {
                if (request != signatureGeneration || editor.CaretOffset != caret) return;
                if (result.Signatures.Count == 0) { FromProject(); return; }
                PresentSignatures(document, result.Signatures, result.Active, result.Parameter, call?.NameStart ?? caret, call?.OpenParen ?? -1);
            });
            return;
        }
        FromProject();
    }

    private void PresentSignatures(EditorDocument document, List<SignatureInfo> signatures, int active, int argument, int start, int paren)
    {
        var area = document.Editor.TextArea;
        if (insight != null && insight.TextArea == area && insight.Provider is SignatureProvider shown)
        {
            // Same call and overloads: keep the one the user arrowed to.
            var keep = paren == insightParen && shown.Count == signatures.Count ? shown.SelectedIndex : active;
            shown.Update(signatures, keep, argument);
            insightParen = paren;
            return;
        }
        insight?.Close();
        var window = new OverloadInsightWindow(area)
        {
            Provider = new SignatureProvider(signatures, active, argument),
            CloseAutomatically = false,
            StartOffset = Math.Min(start, area.Document.TextLength),
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            MaxWidth = 640,
        };
        window.Closed += (_, _) => { if (insight == window) { insight = null; insightParen = -1; } };
        insight = window;
        insightParen = paren;
        window.Show();
    }

    private void RefreshParameterInfoSoon(EditorDocument document)
    {
        if (signatureRefresh == null)
        {
            signatureRefresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            signatureRefresh.Tick += (_, _) =>
            {
                signatureRefresh.Stop();
                if (insight != null && signatureRefresh.Tag is EditorDocument target) ShowParameterInfo(target, explicitly: false);
            };
        }
        signatureRefresh.Tag = document;
        signatureRefresh.Stop();
        signatureRefresh.Start();
    }

    public void CloseParameterInfo()
    {
        signatureGeneration++;
        insight?.Close();
        insight = null;
        insightParen = -1;
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
        // For a keyword (void, let, padding…) the servers have nothing to say: one line on what it means.
        string? Guide()
        {
            if (WordAt(document.Editor.Text, at) is not { } word || BasicIntelliSense.InStringOrComment(document.Editor.Text, word.Start, document.Language)) return null;
            var keywords = BasicIntelliSense.Keywords(document.Language);
            var isKeyword = keywords.Contains(word.Text, document.Language.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            if (!isKeyword && document.Language.Id is not ("css" or "scss" or "less")) return null;
            return KeywordGuide.Explain(word.Text, document.Language.Id) is { } meaning ? $"{word.Text}: {meaning}" : null;
        }
        if (sessions.TryGetValue(document, out var session) && WordAt(document.Editor.Text, at) != null)
        {
            Flush(document);
            session.Hover(at, info => Present(info ?? Guide()));
        }
        else
        {
            Present((DotNetIntelliSense.Supports(document.Language.Id) && SymbolsFor(document) is { } symbols
                ? DotNetIntelliSense.Hover(document.Editor.Text, at, document.Language.Id, symbols) : null) ?? Guide());
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
        // VB.NET: members of .NET and project types, locals, then declarations anywhere in the project.
        if (DotNetIntelliSense.Supports(document.Language.Id) && SymbolsFor(document) is { } known &&
            DotNetIntelliSense.Definition(document.Editor.Text, at, document.Language.Id, known) is { } definition)
        {
            if (definition.Path == null && document.Path is { } here2) { OpenLocation(here2, definition.Line, null); return; }
            if (definition.Path != null) { OpenLocation(definition.Path, definition.Line, null); return; }
        }
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

    /// <summary>
    /// The project's declarations with this file's current ones (unsaved edits included), or null for languages that
    /// don't use them. The project is read in the background the first time; until then, only this file's.
    /// </summary>
    private List<CodeSymbol>? SymbolsFor(EditorDocument document)
    {
        if (!DotNetIntelliSense.Supports(document.Language.Id)) return null;
        var own = SymbolParser.Symbols(document.Editor.Text, document.Language.Id, document.Path);
        var folder = Workspace();
        if (folder != symbolsFolder) { projectSymbols = null; symbolsFolder = folder; }
        if (projectSymbols == null && folder != null) ReadProjectSymbols(folder);
        var others = (projectSymbols ?? []).Where(s => !SamePath(s.Path, document.Path));
        return [.. own, .. others];
    }

    private async void ReadProjectSymbols(string folder)
    {
        if (readingSymbols) return;
        readingSymbols = true;
        try
        {
            var found = await Task.Run(() => SymbolParser.Index(folder).Where(s => DotNetIntelliSense.Supports(Languages.ForFile(s.Path ?? "").Id)).ToList());
            if (symbolsFolder == folder) projectSymbols = found;
        }
        finally
        {
            readingSymbols = false;
        }
    }

    /// <summary>A file was saved: read the project's declarations again next time they're needed.</summary>
    public void ForgetSymbols() => projectSymbols = null;

    // MARK: Refactoring

    /// <summary>
    /// Rename (Ctrl+Shift+R; Ctrl+R, Ctrl+R in Visual Studio): the language server renames the symbol everywhere it's used; without one,
    /// every whole-word use in the project's files of this language, outside strings and comments.
    /// </summary>
    public void Rename(EditorDocument? document)
    {
        if (document == null || document.IsReadOnly) return;
        if (WordAt(document.Editor.Text, document.Editor.CaretOffset) is not { } word) { Status("Put the caret on a name first"); return; }
        var hasServer = sessions.TryGetValue(document, out var session);
        var message = hasServer ? $"Every use of “{word.Text}” that {ServerName(document)} knows about is renamed."
            : $"Every “{word.Text}” in this project's {document.Language.Name} files is renamed (whole words, not in strings or comments).";
        if (Prompt.Ask(Owner(), "Rename", message + " Changed files are left open, unsaved, so you can check them.", word.Text)?.Trim() is not { Length: > 0 } newName
            || newName == word.Text) return;
        if (!Regex.IsMatch(newName, @"^[\p{L}_][\p{L}\p{N}_]*$")) { Status($"“{newName}” isn't a valid name"); return; }
        if (session != null)
        {
            Flush(document);
            session.Rename(word.Start, newName, edits =>
            {
                if (edits.Count == 0)
                {
                    // Jedi, for one, won't rename in a file with syntax errors.
                    var errors = live.TryGetValue(document.Path ?? "", out var list) && list.Any(d => d.Severity == Severity.Error);
                    Status($"{ServerName(document)} couldn't rename “{word.Text}”" + (errors ? ": fix the errors in the file first" : ""));
                    return;
                }
                var count = Apply(edits, "Rename");
                SelectDocument(document);
                Status($"Renamed “{word.Text}” to “{newName}”: {count} change{(count == 1 ? "" : "s")}");
            });
            return;
        }
        var options = new SearchOptions(MatchCase: !document.Language.CaseInsensitive, WholeWord: true);
        var expression = FileSearch.Expression(word.Text, options)!;
        var files = ProjectFiles(document.Language).ToList();
        if (document.Path != null && !files.Any(f => SamePath(f, document.Path))) files.Insert(0, document.Path);
        var changes = 0;
        foreach (var file in document.Path == null ? Enumerable.Empty<string>() : files)
        {
            var open = Documents().FirstOrDefault(d => SamePath(d.Path, file));
            if ((open?.Editor.Text ?? FileSearch.ReadText(file)) is not { } text) continue;
            var matches = expression.Matches(text).Where(m => !BasicIntelliSense.InStringOrComment(text, m.Index, document.Language)).ToList();
            if (matches.Count == 0) continue;
            var target = open ?? OpenDocument(file);
            if (target == null) continue;
            ReplaceAll(target, matches.Select(m => (m.Index, m.Length, newName)));
            changes += matches.Count;
        }
        if (document.Path == null) ReplaceAll(document, expression.Matches(document.Editor.Text).Select(m => (m.Index, m.Length, newName)));
        SelectDocument(document);
        Status($"Renamed “{word.Text}” to “{newName}”: {changes} change{(changes == 1 ? "" : "s")}");
    }

    /// <summary>Extract Method (Ctrl+Shift+M; Ctrl+R, Ctrl+M in Visual Studio) on the selected lines, then Rename on the new method.</summary>
    public void ExtractMethodFromSelection(EditorDocument? document)
    {
        if (document == null || document.IsReadOnly) return;
        var editor = document.Editor;
        try
        {
            var result = CodeForge.Core.ExtractMethod.Extract(editor.Text, editor.SelectionStart, editor.SelectionLength, document.Language.Id);
            // One change, so one Undo puts it back. AvalonEdit keeps \r\n files' line endings when given \n.
            var newText = editor.Text.Contains("\r\n") ? result.Text.Replace("\n", "\r\n") : result.Text;
            var nameStart = editor.Text.Contains("\r\n") ? result.Text[..result.NameStart].Count(c => c == '\n') + result.NameStart : result.NameStart;
            editor.Document.Replace(0, editor.Document.TextLength, newText);
            editor.Select(nameStart, result.NameLength);
            editor.ScrollTo(editor.Document.GetLineByOffset(nameStart).LineNumber, 0);
            Status("Extracted the method: rename it with Ctrl+Shift+R");
        }
        catch (CodeForge.Core.ExtractMethod.Failure failure)
        {
            MessageBox.Show(Owner(), failure.Message, "Extract Method", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>Quick Actions and Refactorings (Ctrl+.): the server's fixes for the line, then CodeForge's own refactorings.</summary>
    public void ShowQuickActions(EditorDocument? document)
    {
        if (document == null) return;
        var editor = document.Editor;
        var menu = new ContextMenu { PlacementTarget = editor.TextArea.TextView, Placement = System.Windows.Controls.Primitives.PlacementMode.Relative };
        var caret = editor.TextArea.Caret.CalculateCaretRectangle();
        menu.HorizontalOffset = caret.Left - editor.TextArea.TextView.ScrollOffset.X;
        menu.VerticalOffset = caret.Bottom - editor.TextArea.TextView.ScrollOffset.Y + 2;
        void Show(List<CodeAction> actions)
        {
            foreach (var action in actions.Take(20))
            {
                var item = new MenuItem { Header = action.Title, FontWeight = action.IsPreferred ? FontWeights.SemiBold : FontWeights.Normal };
                item.Click += (_, _) =>
                {
                    if (sessions.TryGetValue(document, out var current)) current.Run(action, edits => Apply(edits, action.Title) >= 0);
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            var rename = new MenuItem { Header = "Rename…", InputGestureText = "Ctrl+Shift+R" };
            rename.Click += (_, _) => Rename(document);
            menu.Items.Add(rename);
            if (CodeForge.Core.ExtractMethod.Supports(document.Language.Id))
            {
                var extract = new MenuItem { Header = "Extract Method", InputGestureText = "Ctrl+Shift+M", IsEnabled = editor.SelectionLength > 0 };
                extract.Click += (_, _) => ExtractMethodFromSelection(document);
                menu.Items.Add(extract);
            }
            menu.IsOpen = true;
        }
        if (sessions.TryGetValue(document, out var session))
        {
            Flush(document);
            var start = editor.SelectionLength > 0 ? editor.SelectionStart : editor.CaretOffset;
            session.CodeActions(start, start + editor.SelectionLength, Show);
        }
        else
        {
            Show([]);
        }
    }

    /// <summary>Makes a WorkspaceEdit's changes in tabs (opening files that aren't open); returns how many edits it made.</summary>
    private int Apply(Dictionary<string, List<TextEdit>> edits, string what)
    {
        var count = 0;
        foreach (var (path, list) in edits)
        {
            if (list.Count == 0) continue;
            var document = Documents().FirstOrDefault(d => SamePath(d.Path, path)) ?? OpenDocument(path);
            if (document == null) continue;
            var lines = new TextLines(document.Editor.Text);
            ReplaceAll(document, list.Select(e =>
            {
                var start = lines.Offset(e.StartLine, e.StartCharacter);
                return (start, Math.Max(lines.Offset(e.EndLine, e.EndCharacter) - start, 0), e.NewText);
            }));
            count += list.Count;
        }
        if (count > 0) Status($"{what}: {count} change{(count == 1 ? "" : "s")}");
        return count;
    }

    /// <summary>Replaces ranges in one Undo step, last first so earlier offsets stay right.</summary>
    private static void ReplaceAll(EditorDocument document, IEnumerable<(int Start, int Length, string Text)> replacements)
    {
        var editor = document.Editor.Document;
        editor.BeginUpdate();
        try
        {
            foreach (var (start, length, text) in replacements.OrderByDescending(r => r.Start))
                editor.Replace(Math.Min(start, editor.TextLength), Math.Min(length, Math.Max(editor.TextLength - start, 0)), text);
        }
        finally
        {
            editor.EndUpdate();
        }
    }
}

/// <summary>
/// A row in the completion list: a colored badge for the kind, the name, and its detail on the right. Selected, it explains
/// itself beside the list: the server's documentation, what a keyword or built-in function means, or a snippet's text.
/// </summary>
public sealed class CompletionItem(CompletionEntry entry, string languageId, int recency) : ICompletionData
{
    /// <summary>Asks the language server for the item's documentation (completionItem/resolve).</summary>
    public Action<CompletionEntry, Action<string?, string?>>? Resolve { get; init; }
    /// <summary>Called after the item is inserted.</summary>
    public Action<CompletionEntry>? Picked { get; init; }

    private string? resolvedDocumentation;
    private string? resolvedDetail;
    private bool resolving;
    private TextBlock? shownDocumentation;

    /// <summary>A code snippet from the snippet library (not a method a server offers with its arguments).</summary>
    public bool IsSnippet => entry.Kind == 15 && entry.Snippet != null;

    public System.Windows.Media.ImageSource? Image => null;
    public string Text => entry.FilterText;
    /// <summary>Of equally good matches, the list selects the one used most recently.</summary>
    public double Priority => (entry.Kind == 1 ? -1 : 0) + recency / 1000.0;

    public object Description
    {
        get
        {
            var detail = resolvedDetail ?? entry.Detail;
            var panel = new StackPanel { MaxWidth = 420, Margin = new Thickness(2) };
            var title = detail.Length > 0 && entry.Kind != 14 && detail != "Keyword" ? detail : entry.Label;
            panel.Children.Add(new TextBlock
            {
                Text = title, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12,
                FontWeight = FontWeights.SemiBold,
            });
            shownDocumentation = new TextBlock { Text = Explanation() ?? "", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), FontSize = 12 };
            if (shownDocumentation.Text.Length == 0) shownDocumentation.Visibility = Visibility.Collapsed;
            panel.Children.Add(shownDocumentation);
            if (entry.Documentation == null && resolvedDocumentation == null && !resolving && Resolve != null && entry.Raw != null)
            {
                resolving = true;
                Resolve(entry, (documentation, fuller) =>
                {
                    resolvedDocumentation = documentation ?? "";
                    resolvedDetail = fuller;
                    if (shownDocumentation is { } shown && documentation is { Length: > 0 })
                    {
                        shown.Text = Trim(documentation);
                        shown.Visibility = Visibility.Visible;
                    }
                });
            }
            return panel;
        }
    }

    /// <summary>What to say about the item beyond its signature.</summary>
    private string? Explanation()
    {
        if (resolvedDocumentation is { Length: > 0 } resolved) return Trim(resolved);
        if (entry.Documentation is { Length: > 0 } documentation) return Trim(documentation);
        if (entry.Kind == 14 || entry.Detail == "Keyword") return KeywordGuide.Explain(entry.Label, languageId);
        if (IsSnippet) return "Snippet: press Tab to insert it, then Tab to move between the parts to fill in.\n\n" + Trim(SnippetTemplate.PlainText(entry.Snippet!));
        if (entry.Kind is 2 or 3 && BuiltInFunctions.Explain(languageId, entry.Label) is { } builtIn) return builtIn;
        return null;
    }

    private static string Trim(string text) => text.Length > 900 ? text[..900].TrimEnd() + "…" : text;

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
        // Typing ( or . to accept keeps just the name: the ( that was typed follows it.
        var typed = insertionRequestEventArgs is TextCompositionEventArgs;
        if (entry.Snippet != null && !typed)
        {
            SnippetInsertion.Insert(textArea, completionSegment.Offset, completionSegment.Length, entry.Snippet);
        }
        else
        {
            var text = entry.Text;
            if (typed && entry.Snippet != null && text.IndexOf('(') is > 0 and var paren) text = text[..paren];
            document.Replace(completionSegment, text);
        }
        Picked?.Invoke(entry);
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

/// <summary>Parameter Info's contents: the overloads (Up and Down move between them), the current argument in bold, and what
/// the function and that parameter are for.</summary>
public sealed class SignatureProvider : IOverloadProvider
{
    private List<SignatureInfo> signatures;
    private int selected;
    private int argument;

    public SignatureProvider(List<SignatureInfo> signatures, int active, int argument)
    {
        this.signatures = signatures;
        this.argument = argument;
        selected = Math.Clamp(active, 0, Math.Max(signatures.Count - 1, 0));
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public void Update(List<SignatureInfo> newSignatures, int active, int newArgument)
    {
        signatures = newSignatures;
        argument = newArgument;
        selected = Math.Clamp(active, 0, Math.Max(signatures.Count - 1, 0));
        Changed();
    }

    public int Count => signatures.Count;

    public int SelectedIndex
    {
        get => selected;
        set
        {
            selected = Math.Clamp(value, 0, Math.Max(signatures.Count - 1, 0));
            Changed();
        }
    }

    private void Changed()
    {
        foreach (var name in new[] { nameof(SelectedIndex), nameof(Count), nameof(CurrentIndexText), nameof(CurrentHeader), nameof(CurrentContent) })
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    public string? CurrentIndexText => signatures.Count > 1 ? $"{selected + 1} of {signatures.Count}" : null;

    private SignatureInfo Current => signatures[selected];

    private int Parameter => ParameterInfo.ActiveParameter(Current, argument);

    /// <summary>The signature as plain text, the current parameter in [brackets] (for scripts and screenshots).</summary>
    public string Text
    {
        get
        {
            if (signatures.Count == 0) return "";
            var label = Current.Label;
            if (Parameter < 0) return label;
            var (start, length) = Current.Parameters[Parameter];
            return label[..start] + "[" + label.Substring(start, length) + "]" + label[(start + length)..];
        }
    }

    public object CurrentHeader
    {
        get
        {
            var block = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            if (signatures.Count == 0) return block;
            var label = Current.Label;
            var (start, length) = Parameter >= 0 ? Current.Parameters[Parameter] : (0, 0);
            if (length == 0 || start + length > label.Length)
            {
                block.Text = label;
                return block;
            }
            block.Inlines.Add(new System.Windows.Documents.Run(label[..start]));
            block.Inlines.Add(new System.Windows.Documents.Run(label.Substring(start, length)) { FontWeight = FontWeights.Bold });
            block.Inlines.Add(new System.Windows.Documents.Run(label[(start + length)..]));
            return block;
        }
    }

    public object CurrentContent
    {
        get
        {
            if (signatures.Count == 0) return "";
            var lines = new List<string>();
            if (Current.Documentation.Length > 0) lines.Add(Current.Documentation.Length > 600 ? Current.Documentation[..600] + "…" : Current.Documentation);
            if (Parameter >= 0)
            {
                var (start, length) = Current.Parameters[Parameter];
                var name = Current.Label.Substring(start, length);
                var doc = Current.ParameterDocs is { } docs && Parameter < docs.Count ? docs[Parameter] : "";
                if (doc.Length > 0) lines.Add(name + ": " + doc);
            }
            return lines.Count == 0 ? "" : new TextBlock { Text = string.Join("\n\n", lines), TextWrapping = TextWrapping.Wrap, MaxWidth = 600, FontSize = 12 };
        }
    }
}

using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CodeForge.Core;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Search;

namespace CodeForge;

/// <summary>One open file: its editor, language and saved state.</summary>
public sealed class EditorDocument
{
    public TextEditor Editor { get; }
    public string? Path { get; private set; }
    public Language Language { get; private set; }
    public string UntitledName { get; }
    public bool IsReadOnly => Editor.IsReadOnly;
    public bool IsDirty => Editor.IsModified;
    public string DisplayName => Path != null ? System.IO.Path.GetFileName(Path) : UntitledName;
    public event Action? StateChanged;
    public event Action? CaretMoved;

    private readonly DiagnosticRenderer diagnostics;
    private readonly List<TextAnchor> bookmarks = [];
    private readonly BookmarkRenderer bookmarkRenderer;
    private Encoding encoding = new UTF8Encoding(false);

    public EditorDocument(string? path, string text, string untitledName = "Untitled")
    {
        Path = path;
        UntitledName = untitledName;
        Language = path != null ? Languages.ForFile(path) : Languages.PlainText;
        Editor = new TextEditor
        {
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Mono, Cascadia Code, Consolas, Courier New"),
            FontSize = Settings.Current.FontSize,
            WordWrap = false,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            Padding = new Thickness(4, 2, 0, 0),
        };
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Text = text;
        Editor.IsModified = false;
        Editor.Document.UndoStack.ClearAll();
        SearchPanel.Install(Editor.TextArea);
        diagnostics = new DiagnosticRenderer(Editor);
        Editor.TextArea.TextView.BackgroundRenderers.Add(diagnostics);
        bookmarkRenderer = new BookmarkRenderer(bookmarks);
        Editor.TextArea.TextView.BackgroundRenderers.Add(bookmarkRenderer);
        Editor.TextArea.PreviewKeyDown += ExpandSnippetOnTab;
        Editor.TextChanged += (_, _) =>
        {
            diagnostics.Clear();   // positions are stale after an edit
            StateChanged?.Invoke();
        };
        Editor.Document.UndoStack.PropertyChanged += (_, e) => { if (e.PropertyName == "IsOriginalFile") StateChanged?.Invoke(); };
        Editor.TextArea.Caret.PositionChanged += (_, _) => CaretMoved?.Invoke();
        ApplyTheme();
        Themes.Changed += ApplyTheme;
    }

    public static EditorDocument Open(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 30_000_000) throw new IOException("The file is larger than 30 MB.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0)) throw new IOException("This looks like a binary file, so CodeForge won't open it as text.");
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var document = new EditorDocument(System.IO.Path.GetFullPath(path), text) { encoding = reader.CurrentEncoding };
        return document;
    }

    public void SetLanguage(Language language)
    {
        Language = language;
        ApplyTheme();
        StateChanged?.Invoke();
    }

    public void Save(string? newPath = null)
    {
        if (newPath != null)
        {
            Path = System.IO.Path.GetFullPath(newPath);
            if (Language == Languages.PlainText || Language.Id != Languages.ForFile(Path).Id) Language = Languages.ForFile(Path);
            ApplyTheme();
        }
        if (Path == null) throw new InvalidOperationException("Choose a file name first.");
        File.WriteAllText(Path, Editor.Text, encoding);
        Editor.IsModified = false;
        StateChanged?.Invoke();
    }

    public void ApplyTheme()
    {
        var palette = Themes.Current;
        Editor.Background = Themes.Brush(palette.Editor);
        Editor.Foreground = Themes.Brush(palette.EditorText);
        Editor.LineNumbersForeground = Themes.Brush(palette.LineNumbers);
        Editor.TextArea.TextView.CurrentLineBackground = Themes.Brush(palette.CurrentLine);
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        Editor.TextArea.SelectionBrush = Themes.Brush(palette.Selection);
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.SelectionBorder = null;
        Editor.SyntaxHighlighting = Highlighting.For(Language, palette);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    public void SetFontSize(double size) => Editor.FontSize = size;

    /// <summary>1-based line and column of the caret.</summary>
    public (int Line, int Column) CaretPosition => (Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column);

    /// <summary>Moves the caret to a 1-based line (and column) and scrolls there.</summary>
    public void Reveal(int line, int? column = null)
    {
        line = Math.Clamp(line, 1, Editor.Document.LineCount);
        var documentLine = Editor.Document.GetLineByNumber(line);
        var col = Math.Clamp(column ?? 1, 1, documentLine.Length + 1);
        Editor.TextArea.Caret.Location = new TextLocation(line, col);
        Editor.ScrollTo(line, col);
        Editor.TextArea.Focus();
        Editor.Select(documentLine.Offset, 0);
    }

    public void SetDiagnostics(IEnumerable<Diagnostic> list) => diagnostics.Set(list);

    public string? DiagnosticAt(int offset) => diagnostics.MessageAt(offset);

    public void Detach() => Themes.Changed -= ApplyTheme;

    // MARK: Bookmarks

    /// <summary>The bookmarked lines (1-based). Anchors keep them on their code as text is edited above them.</summary>
    public List<int> BookmarkLines => bookmarks.Select(a => a.Line).Distinct().Order().ToList();

    public void SetBookmarks(IEnumerable<int> lines)
    {
        bookmarks.Clear();
        foreach (var line in lines.Where(l => l >= 1 && l <= Editor.Document.LineCount)) AddBookmark(line);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    public void ToggleBookmark(int line)
    {
        if (bookmarks.RemoveAll(a => a.Line == line) == 0) AddBookmark(line);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void AddBookmark(int line)
    {
        var anchor = Editor.Document.CreateAnchor(Editor.Document.GetLineByNumber(line).Offset);
        anchor.MovementType = AnchorMovementType.AfterInsertion;   // Enter at the start of the line moves the bookmark with the text
        anchor.SurviveDeletion = true;
        bookmarks.Add(anchor);
    }

    // MARK: Snippets

    /// <summary>Tab right after a snippet shortcut (prop, cw, For…) expands it, as in Visual Studio.</summary>
    private void ExpandSnippetOnTab(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Tab && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None && ExpandSnippet()) e.Handled = true;
    }

    public bool ExpandSnippet()
    {
        if (Editor.SelectionLength > 0 || Editor.IsReadOnly) return false;
        var document = Editor.Document;
        var end = Editor.CaretOffset;
        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(document.GetCharAt(start - 1)) || document.GetCharAt(start - 1) == '_')) start--;
        if (start == end || Snippets.Find(Language.Id, document.GetText(start, end - start)) is not { } snippet) return false;
        var line = document.GetLineByOffset(start);
        var indent = new string(document.GetText(line).TakeWhile(char.IsWhiteSpace).ToArray());
        var (text, cursor) = snippet.ToolboxItem.Expanded(indent);
        document.Replace(start, end - start, text);
        Editor.CaretOffset = start + cursor;
        return true;
    }
}

/// <summary>A bar and a soft band on bookmarked lines.</summary>
public sealed class BookmarkRenderer(List<TextAnchor> bookmarks) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (bookmarks.Count == 0 || !textView.VisualLinesValid) return;
        var lines = bookmarks.Select(a => a.Line).ToHashSet();
        var accent = (Color)ColorConverter.ConvertFromString(Themes.Current.Accent);
        var bar = new SolidColorBrush(accent);
        var band = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B));
        bar.Freeze();
        band.Freeze();
        foreach (var line in textView.VisualLines)
        {
            if (!lines.Contains(line.FirstDocumentLine.LineNumber)) continue;
            var top = line.VisualTop - textView.ScrollOffset.Y;
            drawingContext.DrawRectangle(band, null, new Rect(0, top, textView.ActualWidth, line.Height));
            drawingContext.DrawRectangle(bar, null, new Rect(0, top, 3, line.Height));
        }
    }
}

/// <summary>Draws a wavy underline (red for errors, yellow for warnings) under the problems from the last build.</summary>
public sealed class DiagnosticRenderer(TextEditor editor) : IBackgroundRenderer
{
    private readonly List<(TextSegment Segment, Severity Severity, string Message)> marks = [];

    public KnownLayer Layer => KnownLayer.Selection;

    public void Set(IEnumerable<Diagnostic> diagnostics)
    {
        marks.Clear();
        var document = editor.Document;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Line < 1 || diagnostic.Line > document.LineCount) continue;
            var line = document.GetLineByNumber(diagnostic.Line);
            int start = line.Offset, end = line.EndOffset;
            if (diagnostic.Column is { } column && column - 1 < line.Length)
            {
                // From the column to the end of the word there.
                start = line.Offset + column - 1;
                end = start;
                while (end < line.EndOffset && (char.IsLetterOrDigit(document.GetCharAt(end)) || document.GetCharAt(end) == '_')) end++;
                if (end == start) end = Math.Min(start + 1, line.EndOffset);
            }
            else
            {
                while (start < end && char.IsWhiteSpace(document.GetCharAt(start))) start++;
            }
            if (end > start) marks.Add((new TextSegment { StartOffset = start, EndOffset = end }, diagnostic.Severity, diagnostic.Message));
        }
        editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void Clear()
    {
        if (marks.Count == 0) return;
        marks.Clear();
        editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public string? MessageAt(int offset)
    {
        var found = marks.Where(m => offset >= m.Segment.StartOffset && offset <= m.Segment.EndOffset)
                         .Select(m => (m.Severity == Severity.Error ? "Error: " : "Warning: ") + m.Message).ToList();
        return found.Count == 0 ? null : string.Join("\n", found);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (marks.Count == 0 || !textView.VisualLinesValid) return;
        foreach (var (segment, severity, _) in marks)
        {
            var color = severity == Severity.Error ? Color.FromRgb(0xE5, 0x14, 0x00) : Color.FromRgb(0xCC, 0xA7, 0x00);
            var pen = new Pen(new SolidColorBrush(color), 1);
            pen.Freeze();
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                // A zigzag along the bottom of the text.
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    double x = rect.Left, y = rect.Bottom - 1;
                    context.BeginFigure(new Point(x, y), false, false);
                    var up = true;
                    while (x < rect.Right)
                    {
                        x += 2;
                        context.LineTo(new Point(Math.Min(x, rect.Right), up ? y - 2 : y), true, false);
                        up = !up;
                    }
                }
                geometry.Freeze();
                drawingContext.DrawGeometry(null, pen, geometry);
            }
        }
    }
}

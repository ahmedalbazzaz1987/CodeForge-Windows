using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CodeForge.Core;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
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
    private readonly List<TextAnchor> breakpoints = [];
    private readonly BreakpointMargin breakpointMargin;
    private readonly ExecutionLineRenderer executionRenderer = new();

    /// <summary>A breakpoint was set or cleared by clicking the margin: the window stores it and tells the debugger.</summary>
    public event Action? BreakpointsChanged;
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
        Editor.TextArea.TextView.BackgroundRenderers.Add(executionRenderer);
        // Breakpoints go in their own margin left of the line numbers, as in Visual Studio; a click there sets one.
        breakpointMargin = new BreakpointMargin(this);
        Editor.TextArea.LeftMargins.Insert(0, breakpointMargin);
        Editor.TextArea.PreviewKeyDown += ExpandSnippetOnTab;
        GroupTypingForUndo();
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

    /// <summary>"UTF-8", "UTF-8 with BOM", "UTF-16 LE"…, for the Properties window.</summary>
    public string EncodingName => encoding switch
    {
        UTF8Encoding utf8 => utf8.GetPreamble().Length > 0 ? "UTF-8 with BOM" : "UTF-8",
        UnicodeEncoding { CodePage: 1201 } => "UTF-16 BE",
        UnicodeEncoding => "UTF-16 LE",
        var other => other.WebName.ToUpperInvariant(),
    };

    /// <summary>
    /// Ctrl+Z undoes typing a word at a time, like Visual Studio, instead of one character at a time: characters typed one
    /// after another (with nothing else changing the text in between) join the previous undo step. A space or line break
    /// starts a new step.
    /// </summary>
    private void GroupTypingForUndo()
    {
        var changes = 0;
        var typedAfter = -1;     // the change count right after the last typed character
        var typedEnd = -1;       // the caret right after it
        var typedAt = DateTime.MinValue;
        var continuing = false;
        Editor.Document.Changed += (_, _) => changes++;
        // If another handler (IntelliSense) took the keystroke, TextEntered never comes: close the group here instead.
        void CloseDangling()
        {
            if (!continuing) return;
            continuing = false;
            Editor.Document.UndoStack.EndUndoGroup();
        }
        Editor.TextArea.PreviewKeyDown += (_, _) => CloseDangling();
        Editor.TextArea.LostKeyboardFocus += (_, _) => CloseDangling();
        Editor.TextArea.TextEntering += (_, e) =>
        {
            CloseDangling();
            var stack = Editor.Document.UndoStack;
            continuing = e.Text.Length == 1 && !char.IsWhiteSpace(e.Text[0]) && stack.CanUndo && changes == typedAfter &&
                         Editor.CaretOffset == typedEnd && Editor.SelectionLength == 0 && (DateTime.Now - typedAt).TotalSeconds < 3;
            if (continuing) stack.StartContinuedUndoGroup();
        };
        Editor.TextArea.TextEntered += (_, _) =>
        {
            if (continuing) Editor.Document.UndoStack.EndUndoGroup();
            continuing = false;
            typedAfter = changes;
            typedEnd = Editor.CaretOffset;
            typedAt = DateTime.Now;
        };
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

    // MARK: Breakpoints and the line the debugger stopped at

    /// <summary>The lines with a breakpoint (1-based). Anchors keep them on their code as text is edited above them.</summary>
    public List<int> BreakpointLines => breakpoints.Select(a => a.Line).Distinct().Order().ToList();

    public void SetBreakpoints(IEnumerable<int> lines)
    {
        breakpoints.Clear();
        foreach (var line in lines.Where(l => l >= 1 && l <= Editor.Document.LineCount)) AddBreakpoint(line);
        breakpointMargin.InvalidateVisual();
    }

    public void ToggleBreakpoint(int line)
    {
        if (line < 1 || line > Editor.Document.LineCount) return;
        if (breakpoints.RemoveAll(a => a.Line == line) == 0) AddBreakpoint(line);
        breakpointMargin.InvalidateVisual();
        BreakpointsChanged?.Invoke();
    }

    private void AddBreakpoint(int line)
    {
        var anchor = Editor.Document.CreateAnchor(Editor.Document.GetLineByNumber(line).Offset);
        anchor.MovementType = AnchorMovementType.AfterInsertion;
        anchor.SurviveDeletion = true;
        breakpoints.Add(anchor);
    }

    /// <summary>The line where the debugger is stopped (0 for none): a yellow band and arrow, scrolled into view.</summary>
    public int ExecutionLine { get; private set; }

    public void SetExecutionLine(int line)
    {
        ExecutionLine = line;
        executionRenderer.Line = line;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        breakpointMargin.InvalidateVisual();
        if (line >= 1 && line <= Editor.Document.LineCount)
        {
            Editor.ScrollTo(line, 1);
            Editor.TextArea.Caret.Location = new TextLocation(line, 1);
        }
    }

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

/// <summary>The gutter for breakpoints (red dots) and the current statement while debugging (a yellow arrow).</summary>
public sealed class BreakpointMargin(EditorDocument document) : AbstractMargin
{
    private const double MarginWidth = 16;

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView != null) oldTextView.VisualLinesChanged -= Redraw;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null) newTextView.VisualLinesChanged += Redraw;
        InvalidateVisual();
    }

    private void Redraw(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        var view = TextView;
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));   // takes clicks
        if (view is not { VisualLinesValid: true }) return;
        var lines = document.BreakpointLines.ToHashSet();
        var red = new SolidColorBrush(Color.FromRgb(0xE5, 0x14, 0x00));
        var yellow = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
        red.Freeze();
        yellow.Freeze();
        foreach (var line in view.VisualLines)
        {
            var number = line.FirstDocumentLine.LineNumber;
            var top = line.VisualTop - view.ScrollOffset.Y;
            var center = new Point(MarginWidth / 2, top + line.Height / 2);
            if (lines.Contains(number)) drawingContext.DrawEllipse(red, null, center, 5.5, 5.5);
            if (number == document.ExecutionLine)
            {
                var arrow = new StreamGeometry();
                using (var context = arrow.Open())
                {
                    context.BeginFigure(new Point(3, center.Y - 4), true, true);
                    context.LineTo(new Point(9, center.Y - 4), false, false);
                    context.LineTo(new Point(14, center.Y), false, false);
                    context.LineTo(new Point(9, center.Y + 4), false, false);
                    context.LineTo(new Point(3, center.Y + 4), false, false);
                }
                arrow.Freeze();
                drawingContext.DrawGeometry(yellow, new Pen(Brushes.Black, 0.5), arrow);
            }
        }
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (TextView is not { } view || !view.VisualLinesValid) return;
        var line = view.GetVisualLineFromVisualTop(e.GetPosition(view).Y + view.ScrollOffset.Y);
        if (line == null) return;
        document.ToggleBreakpoint(line.FirstDocumentLine.LineNumber);
        e.Handled = true;
    }
}

/// <summary>A yellow band on the line where the debugger is stopped.</summary>
public sealed class ExecutionLineRenderer : IBackgroundRenderer
{
    public int Line { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Line < 1 || !textView.VisualLinesValid) return;
        var band = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xD7, 0x00));
        band.Freeze();
        foreach (var line in textView.VisualLines)
        {
            if (line.FirstDocumentLine.LineNumber != Line) continue;
            drawingContext.DrawRectangle(band, null, new Rect(0, line.VisualTop - textView.ScrollOffset.Y, textView.ActualWidth, line.Height));
        }
    }
}

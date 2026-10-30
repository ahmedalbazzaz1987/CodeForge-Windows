using System.IO;
using System.Text;
using CodeForge.Core;

namespace CodeForge.Designer;

/// <summary>A form open in the Windows Forms designer ("Form1.vb [Design]"): its design, undo history and saved state.</summary>
public sealed class DesignerDocument
{
    public string DesignerPath { get; }
    public string CodePath { get; }
    public string Language { get; }
    public FormDesign Design { get; private set; }
    public DesignSurface Surface { get; }
    public bool IsDirty { get; private set; }
    public string DisplayName => Path.GetFileName(CodePath) + " [Design]";
    public event Action? StateChanged;

    private readonly Stack<string> undo = new();
    private readonly Stack<string> redo = new();
    private string lastSaved;

    public bool CanUndo => undo.Count > 0;

    public DesignerDocument(string codeOrDesignerPath)
    {
        DesignerPath = FormDesign.DesignerFileFor(codeOrDesignerPath) ?? throw new FileNotFoundException("This form has no .Designer file.");
        CodePath = FormDesign.CodeFileFor(DesignerPath);
        Language = DesignerPath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase) ? "vb" : "csharp";
        var text = File.ReadAllText(DesignerPath);
        if (!FormDesign.IsFormDesigner(text)) throw new InvalidDataException("This file isn't a Windows Forms form.");
        Design = FormDesign.Parse(text, Language);
        _ = Design.Generate();   // fails early (with a message) if InitializeComponent can't be found
        lastSaved = text;
        Surface = new DesignSurface(this);
    }

    /// <summary>Remembers the design before a change, for Undo.</summary>
    public void Snapshot()
    {
        undo.Push(Design.Generate());
        redo.Clear();
    }

    public void DropSnapshot() { if (undo.Count > 0) undo.Pop(); }

    public void Changed()
    {
        IsDirty = true;
        StateChanged?.Invoke();
    }

    public void Undo()
    {
        if (undo.Count == 0) return;
        redo.Push(Design.Generate());
        Restore(undo.Pop());
    }

    public void Redo()
    {
        if (redo.Count == 0) return;
        undo.Push(Design.Generate());
        Restore(redo.Pop());
    }

    private void Restore(string text)
    {
        Design = FormDesign.Parse(text, Language);
        IsDirty = text != lastSaved;
        Surface.Rebuild();
        StateChanged?.Invoke();
    }

    public void Save()
    {
        var text = Design.Generate();
        File.WriteAllText(DesignerPath, text, new UTF8Encoding(true));
        lastSaved = text;
        IsDirty = false;
        StateChanged?.Invoke();
    }
}

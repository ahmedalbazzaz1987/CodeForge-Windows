using System.Text;
using System.Text.RegularExpressions;

namespace CodeForge.Core;

/// <summary>What the designer knows about a Windows Forms control type.</summary>
/// <param name="HasText">New controls get Text = their name (Button1).</param>
/// <param name="SupportsInitialize">The type implements ISupportInitialize, so InitializeComponent wraps it in BeginInit/EndInit.</param>
public sealed record ControlKind(string Name, string Glyph, int Width, int Height, bool AutoSize = false, bool HasText = false,
                                 bool IsContainer = false, bool SupportsInitialize = false, bool VisualStyle = false,
                                 string DefaultEvent = "Click", string EventArgs = "EventArgs", bool TabStop = true)
{
    public string FullName => "System.Windows.Forms." + Name;
}

public static class ControlKinds
{
    /// <summary>The Toolbox's "Common Controls" and "Containers", in Visual Studio's order.</summary>
    public static readonly IReadOnlyList<ControlKind> All =
    [
        new("Button", "\uE815", 94, 29, HasText: true, VisualStyle: true),
        new("CheckBox", "\uE73A", 101, 24, AutoSize: true, HasText: true, VisualStyle: true, DefaultEvent: "CheckedChanged"),
        new("CheckedListBox", "\uE762", 150, 114, DefaultEvent: "SelectedIndexChanged"),
        new("ComboBox", "\uE70D", 151, 28, DefaultEvent: "SelectedIndexChanged"),
        new("DateTimePicker", "\uE787", 250, 27, DefaultEvent: "ValueChanged"),
        new("Label", "\uE8E9", 50, 20, AutoSize: true, HasText: true, TabStop: false),
        new("LinkLabel", "\uE71B", 76, 20, AutoSize: true, HasText: true, DefaultEvent: "LinkClicked", EventArgs: "LinkLabelLinkClickedEventArgs"),
        new("ListBox", "\uEA37", 150, 104, DefaultEvent: "SelectedIndexChanged"),
        new("MaskedTextBox", "\uE8D2", 125, 27, DefaultEvent: "MaskInputRejected", EventArgs: "MaskInputRejectedEventArgs"),
        new("MonthCalendar", "\uE787", 251, 162, DefaultEvent: "DateChanged", EventArgs: "DateRangeEventArgs"),
        new("NumericUpDown", "\uEC8F", 150, 27, SupportsInitialize: true, DefaultEvent: "ValueChanged"),
        new("PictureBox", "\uEB9F", 125, 62, SupportsInitialize: true, TabStop: false),
        new("ProgressBar", "\uE9D9", 125, 29, TabStop: false),
        new("RadioButton", "\uECCB", 117, 24, AutoSize: true, HasText: true, VisualStyle: true, DefaultEvent: "CheckedChanged"),
        new("RichTextBox", "\uE8D2", 125, 120, DefaultEvent: "TextChanged"),
        new("TextBox", "\uE8D2", 125, 27, DefaultEvent: "TextChanged"),
        new("TrackBar", "\uE9E9", 130, 56, SupportsInitialize: true, DefaultEvent: "Scroll"),
        new("DataGridView", "\uE80A", 300, 188, SupportsInitialize: true, DefaultEvent: "CellContentClick", EventArgs: "DataGridViewCellEventArgs"),
        new("GroupBox", "\uE8FD", 250, 125, HasText: true, IsContainer: true, DefaultEvent: "Enter", TabStop: true),
        new("Panel", "\uE91B", 200, 100, IsContainer: true, DefaultEvent: "Paint", EventArgs: "PaintEventArgs"),
        new("FlowLayoutPanel", "\uE8A9", 200, 100, IsContainer: true, DefaultEvent: "Paint", EventArgs: "PaintEventArgs"),
    ];

    public static ControlKind? Find(string type)
    {
        var name = type.Split('.')[^1];
        return All.FirstOrDefault(k => k.Name == name);
    }
}

/// <summary>A control on the designed form: its InitializeComponent statements, kept as code.</summary>
public sealed class DesignedControl(string name, string type)
{
    public string Name { get; set; } = name;
    /// <summary>The full type name, like System.Windows.Forms.Button.</summary>
    public string Type { get; set; } = type;
    /// <summary>The container it's in (a Panel or GroupBox), or null for the form itself.</summary>
    public string? Parent { get; set; }
    /// <summary>Constructor argument as written (Timer and others take "components").</summary>
    public string ConstructorArgument { get; set; } = "";
    /// <summary>Property assignments in order: (property, code expression).</summary>
    public List<(string Property, string Value)> Properties { get; } = [];
    /// <summary>C# event wiring: (event, handler method).</summary>
    public List<(string Event, string Handler)> Events { get; } = [];
    /// <summary>Other statements about this control, kept as written (Items.AddRange…).</summary>
    public List<string> Extra { get; } = [];

    public ControlKind? Kind => ControlKinds.Find(Type);

    public string? Get(string property) => Properties.FirstOrDefault(p => p.Property == property).Value;

    public void Set(string property, string? value)
    {
        var index = Properties.FindIndex(p => p.Property == property);
        if (value == null) { if (index >= 0) Properties.RemoveAt(index); return; }
        if (index >= 0) Properties[index] = (property, value); else Properties.Add((property, value));
    }
}

/// <summary>
/// A Windows Forms designer file (Form1.Designer.vb or .cs): reads InitializeComponent into controls and properties,
/// and writes it back in Visual Studio's layout. Property values stay as code expressions; the app turns them into
/// real values to draw the form. Everything outside InitializeComponent and the control fields is kept as it was.
/// </summary>
public sealed partial class FormDesign
{
    public string Language { get; }
    public string ClassName { get; private set; } = "Form1";
    public List<DesignedControl> Controls { get; } = [];
    public List<(string Property, string Value)> FormProperties { get; } = [];
    public List<(string Event, string Handler)> FormEvents { get; } = [];
    public List<string> FormExtra { get; } = [];
    public bool HasComponents { get; private set; }
    private readonly string source;
    private readonly HashSet<string> originalFields = new(StringComparer.OrdinalIgnoreCase);

    private bool IsVB => Language == "vb";
    private StringComparison NameComparison => IsVB ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private FormDesign(string source, string language)
    {
        this.source = source;
        Language = language;
    }

    /// <summary>True when a designer file belongs to a form CodeForge can design.</summary>
    public static bool IsFormDesigner(string text) =>
        text.Contains("InitializeComponent") && (text.Contains("Windows.Forms.Form") || Regex.IsMatch(text, @"Inherits\s+Form\b|class\s+\w+\s*:\s*Form\b") || text.Contains("partial class", StringComparison.OrdinalIgnoreCase));

    /// <summary>The designer file next to a form's code file (Form1.vb → Form1.Designer.vb), if there is one.</summary>
    public static string? DesignerFileFor(string codeFile)
    {
        if (codeFile.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase) || codeFile.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)) return codeFile;
        var designer = Path.Combine(Path.GetDirectoryName(codeFile) ?? "", Path.GetFileNameWithoutExtension(codeFile) + ".Designer" + Path.GetExtension(codeFile));
        return File.Exists(designer) ? designer : null;
    }

    /// <summary>The code file for a designer file (Form1.Designer.vb → Form1.vb).</summary>
    public static string CodeFileFor(string designerFile) =>
        Path.Combine(Path.GetDirectoryName(designerFile) ?? "", Path.GetFileNameWithoutExtension(designerFile).Replace(".Designer", "", StringComparison.OrdinalIgnoreCase) + Path.GetExtension(designerFile));

    // MARK: Reading

    public static FormDesign Parse(string text, string language)
    {
        var design = new FormDesign(text, language);
        design.Read();
        return design;
    }

    private (int Start, int End)? MethodBody()
    {
        if (IsVB)
        {
            var header = Regex.Match(source, @"Sub\s+InitializeComponent\s*\(\s*\)[^\n]*\n", RegexOptions.IgnoreCase);
            if (!header.Success) return null;
            var end = Regex.Match(source[header.Index..], @"^[ \t]*End\s+Sub\b", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return end.Success ? (header.Index + header.Length, header.Index + end.Index) : null;
        }
        var signature = Regex.Match(source, @"void\s+InitializeComponent\s*\(\s*\)\s*\{");
        if (!signature.Success) return null;
        var depth = 0;
        for (var i = signature.Index + signature.Length - 1; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            if (source[i] == '}' && --depth == 0) return (signature.Index + signature.Length, i);
        }
        return null;
    }

    /// <summary>InitializeComponent's statements, joined across lines, without comments and Me./this.</summary>
    private List<string> Statements(string body)
    {
        var statements = new List<string>();
        var current = new StringBuilder();
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (current.Length == 0 && (line.Length == 0 || line.StartsWith('\'') || line.StartsWith("//") || line.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))) continue;
            if (current.Length > 0) current.Append(' ');
            current.Append(line);
            var text = current.ToString();
            var complete = IsVB
                ? !(text.EndsWith(" _") || text.EndsWith(',') || text.EndsWith('(') || text.EndsWith('{'))
                : text.EndsWith(';') || text.EndsWith('}');
            if (!complete) continue;
            if (IsVB && text.EndsWith(" _")) text = text[..^2];
            statements.Add(text.TrimEnd(';').Trim());
            current.Clear();
        }
        if (current.Length > 0) statements.Add(current.ToString().TrimEnd(';').Trim());
        return statements.Select(s => StripReceiver().Replace(s, "")).ToList();
    }

    [GeneratedRegex(@"\b(?:Me|this)\.(?=\w)", RegexOptions.IgnoreCase)]
    private static partial Regex StripReceiver();

    [GeneratedRegex(@"^(\w+)\s*=\s*(?:New|new)\s+([\w.]+)\s*\(\s*([\w.]*)\s*\)$")]
    private static partial Regex Creation();

    [GeneratedRegex(@"^(?:(\w+)\.)?Controls\.Add\(\s*(\w+)\s*\)$")]
    private static partial Regex ControlsAdd();

    [GeneratedRegex(@"^(?:(\w+)\.)?(\w+)\s*\+=\s*(?:new\s+[\w.]+\(\s*)?(\w+)\s*\)?$")]
    private static partial Regex EventWiring();

    [GeneratedRegex(@"^(\w+)\.(\w+)\s*=\s*(.+)$")]
    private static partial Regex ControlProperty();

    [GeneratedRegex(@"^(\w+)\s*=\s*(.+)$")]
    private static partial Regex FormProperty();

    [GeneratedRegex(@"(?:Suspend|Resume|Perform)Layout\(|\.(?:Begin|End)Init\(\)", RegexOptions.IgnoreCase)]
    private static partial Regex Bookkeeping();

    private void Read()
    {
        var name = IsVB ? Regex.Match(source, @"Partial\s+(?:Public\s+|Friend\s+)?Class\s+(\w+)", RegexOptions.IgnoreCase)
                        : Regex.Match(source, @"partial\s+class\s+(\w+)");
        if (name.Success) ClassName = name.Groups[1].Value;
        if (MethodBody() is not { } body) return;

        foreach (var statement in Statements(source[body.Start..body.End]))
        {
            Match match;
            if ((match = Creation().Match(statement)).Success)
            {
                if (match.Groups[1].Value.Equals("components", StringComparison.OrdinalIgnoreCase)) { HasComponents = true; continue; }
                var type = match.Groups[2].Value;
                if (!type.Contains('.')) type = (type == "Container" ? "System.ComponentModel." : "System.Windows.Forms.") + type;
                Controls.Add(new DesignedControl(match.Groups[1].Value, type) { ConstructorArgument = match.Groups[3].Value });
                originalFields.Add(match.Groups[1].Value);
                continue;
            }
            if (Bookkeeping().IsMatch(statement)) continue;
            if ((match = ControlsAdd().Match(statement)).Success)
            {
                if (Control(match.Groups[2].Value) is { } child)
                    child.Parent = match.Groups[1].Success ? Control(match.Groups[1].Value)?.Name : null;
                continue;
            }
            if (!IsVB && (match = EventWiring().Match(statement)).Success)
            {
                if (match.Groups[1].Success && Control(match.Groups[1].Value) is { } target) target.Events.Add((match.Groups[2].Value, match.Groups[3].Value));
                else if (!match.Groups[1].Success) FormEvents.Add((match.Groups[2].Value, match.Groups[3].Value));
                else FormExtra.Add(statement);
                continue;
            }
            if ((match = ControlProperty().Match(statement)).Success && Control(match.Groups[1].Value) is { } owner)
            {
                owner.Properties.Add((match.Groups[2].Value, match.Groups[3].Value.Trim()));
                continue;
            }
            if ((match = FormProperty().Match(statement)).Success && Control(match.Groups[1].Value) == null)
            {
                FormProperties.Add((match.Groups[1].Value, match.Groups[2].Value.Trim()));
                continue;
            }
            var dot = statement.IndexOf('.');
            if (dot > 0 && Control(statement[..dot]) is { } subject) subject.Extra.Add(statement);
            else FormExtra.Add(statement);
        }
    }

    public DesignedControl? Control(string name) => Controls.FirstOrDefault(c => c.Name.Equals(name, NameComparison));

    public string? FormProperty(string property) => FormProperties.FirstOrDefault(p => p.Property == property).Value;

    public void SetFormProperty(string property, string? value)
    {
        var index = FormProperties.FindIndex(p => p.Property == property);
        if (value == null) { if (index >= 0) FormProperties.RemoveAt(index); return; }
        if (index >= 0) FormProperties[index] = (property, value); else FormProperties.Add((property, value));
    }

    // MARK: Editing

    /// <summary>Button1 for VB.NET, button1 for C#, numbered past the names in use.</summary>
    public string UniqueName(string typeName)
    {
        var stem = IsVB ? typeName : char.ToLowerInvariant(typeName[0]) + typeName[1..];
        for (var number = 1; ; number++)
        {
            var candidate = stem + number;
            if (Control(candidate) == null && !candidate.Equals(ClassName, NameComparison)) return candidate;
        }
    }

    /// <summary>Adds a control the way Visual Studio's Toolbox does: a new name, default size and text, the next tab index.</summary>
    public DesignedControl Add(ControlKind kind, int x, int y, string? parent = null)
    {
        var name = UniqueName(kind.Name);
        var control = new DesignedControl(name, kind.FullName) { Parent = parent };
        if (kind.Name == "DataGridView") control.Set("ColumnHeadersHeightSizeMode", "System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize");
        if (kind.AutoSize) control.Set("AutoSize", Bool(true));
        control.Set("Location", Point(x, y));
        control.Set("Name", Quote(name));
        if (kind.Name == "DataGridView") control.Set("RowHeadersWidth", "51");
        control.Set("Size", SizeValue(kind.Width, kind.Height));
        if (kind.TabStop) control.Set("TabIndex", Controls.Count(c => c.Kind?.TabStop == true).ToString());
        else if (kind.Name == "PictureBox") control.Set("TabStop", Bool(false));
        if (kind.HasText) control.Set("Text", Quote(name));
        if (kind.VisualStyle) control.Set("UseVisualStyleBackColor", Bool(true));
        Controls.Add(control);
        return control;
    }

    /// <summary>Removes a control, and everything inside it if it's a container.</summary>
    public void Remove(string name)
    {
        foreach (var child in Controls.Where(c => name.Equals(c.Parent, NameComparison)).Select(c => c.Name).ToList()) Remove(child);
        Controls.RemoveAll(c => c.Name.Equals(name, NameComparison));
    }

    /// <summary>Renames a control everywhere in InitializeComponent. Returns false if the name is taken or isn't an identifier.</summary>
    public bool Rename(string oldName, string newName)
    {
        if (!Regex.IsMatch(newName, @"^[A-Za-z_]\w*$") || (Control(newName) != null && !oldName.Equals(newName, NameComparison))) return false;
        if (Control(oldName) is not { } control) return false;
        var word = new Regex(@"(?<![\w.])" + Regex.Escape(oldName) + @"\b", IsVB ? RegexOptions.IgnoreCase : RegexOptions.None);
        control.Name = newName;
        control.Set("Name", Quote(newName));
        foreach (var other in Controls)
        {
            if (oldName.Equals(other.Parent, NameComparison)) other.Parent = newName;
            for (var i = 0; i < other.Extra.Count; i++) other.Extra[i] = word.Replace(other.Extra[i], newName);
        }
        return true;
    }

    /// <summary>Brings a control to the front (the end of its container's control list) or sends it to the back.</summary>
    public void Reorder(string name, bool toFront)
    {
        if (Control(name) is not { } control) return;
        Controls.Remove(control);
        if (toFront) Controls.Add(control); else Controls.Insert(0, control);
    }

    // MARK: Code expressions

    public string Quote(string text) => IsVB
        ? "\"" + text.Replace("\"", "\"\"") + "\""
        : "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";

    public string Bool(bool value) => IsVB ? (value ? "True" : "False") : (value ? "true" : "false");
    public string Point(int x, int y) => (IsVB ? "New" : "new") + $" System.Drawing.Point({x}, {y})";
    public string SizeValue(int width, int height) => (IsVB ? "New" : "new") + $" System.Drawing.Size({width}, {height})";
    public string Float(double value) => value.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture) + "F";

    /// <summary>(x, y) from a Point or Size expression like <c>New Point(12, 34)</c>.</summary>
    public static (int A, int B)? Pair(string? expression)
    {
        if (expression == null) return null;
        var match = Regex.Match(expression, @"\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)");
        return match.Success ? (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)) : null;
    }

    /// <summary>The text of a string literal, or null when the expression isn't one.</summary>
    public string? Unquote(string? expression)
    {
        if (expression == null) return null;
        var text = expression.Trim();
        if (text.StartsWith('@') && !IsVB) return text.Length >= 3 && text.EndsWith('"') ? text[2..^1].Replace("\"\"", "\"") : null;
        if (text.Length < 2 || text[0] != '"' || text[^1] != '"') return null;
        var inner = text[1..^1];
        return IsVB ? inner.Replace("\"\"", "\"") : Regex.Unescape(inner);
    }

    // MARK: Writing

    private string Indent => IsVB ? "        " : "        ";
    private string End => IsVB ? "" : ";";
    private string Comment => IsVB ? "' " : "// ";
    private string NewKeyword => IsVB ? "New" : "new";

    private IEnumerable<DesignedControl> ChildrenOf(string? parent) =>
        Controls.Where(c => string.Equals(c.Parent, parent, IsVB ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

    /// <summary>The InitializeComponent body, in Visual Studio's layout.</summary>
    public string GenerateBody()
    {
        var lines = new List<string>();
        void Line(string text) => lines.Add(Indent + text + (text.Length == 0 || text.StartsWith(Comment.Trim()) ? "" : End));
        void Header(string name)
        {
            lines.Add(Indent + Comment);
            lines.Add(Indent + Comment + name);
            lines.Add(Indent + Comment);
        }
        string Init(DesignedControl control, string method) => IsVB
            ? $"CType({control.Name}, System.ComponentModel.ISupportInitialize).{method}()"
            : $"((System.ComponentModel.ISupportInitialize){control.Name}).{method}()";

        if (HasComponents) Line($"components = {NewKeyword} System.ComponentModel.Container()");
        foreach (var control in Controls) Line($"{control.Name} = {NewKeyword} {control.Type}({control.ConstructorArgument})");
        var containers = Controls.Where(c => ChildrenOf(c.Name).Any()).ToList();
        var initializing = Controls.Where(c => c.Kind?.SupportsInitialize == true).ToList();
        foreach (var container in containers) Line($"{container.Name}.SuspendLayout()");
        foreach (var control in initializing) Line(Init(control, "BeginInit"));
        Line("SuspendLayout()");
        foreach (var control in Controls)
        {
            Header(control.Name);
            // Visual Studio lists children in reverse order of adding (the last added is drawn on top).
            foreach (var child in ChildrenOf(control.Name).Reverse()) Line($"{control.Name}.Controls.Add({child.Name})");
            if (control.Get("Name") == null) control.Set("Name", Quote(control.Name));
            foreach (var (property, value) in control.Properties) Line($"{control.Name}.{property} = {value}");
            foreach (var (name, handler) in control.Events) Line($"{control.Name}.{name} += {handler}");
            foreach (var extra in control.Extra) Line(extra);
        }
        Header(ClassName);
        var order = new[] { "AutoScaleDimensions", "AutoScaleMode", "ClientSize" };
        foreach (var property in order)
            if (FormProperty(property) is { } value) Line($"{property} = {value}");
        foreach (var child in ChildrenOf(null).Reverse()) Line($"Controls.Add({child.Name})");
        if (FormProperty("Name") == null) SetFormProperty("Name", Quote(ClassName));
        foreach (var (property, value) in FormProperties.Where(p => !order.Contains(p.Property))) Line($"{property} = {value}");
        foreach (var (name, handler) in FormEvents) Line($"{name} += {handler}");
        foreach (var extra in FormExtra) Line(extra);
        foreach (var control in initializing) Line(Init(control, "EndInit"));
        foreach (var container in containers)
        {
            Line($"{container.Name}.ResumeLayout({Bool(false)})");
            if (container.Kind?.Name != "Panel") Line($"{container.Name}.PerformLayout()");
        }
        Line($"ResumeLayout({Bool(false)})");
        if (Controls.Count > 0) Line("PerformLayout()");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    /// <summary>The whole designer file with the new InitializeComponent and control fields.</summary>
    public string Generate()
    {
        if (MethodBody() is not { } body) throw new InvalidOperationException("The designer file has no InitializeComponent method.");
        var newline = source.Contains("\r\n") ? "\r\n" : "\n";
        // In C# the body ends at the method's "}", whose indentation was part of the old body.
        var text = source[..body.Start] + GenerateBody().Replace(Environment.NewLine, newline) + (IsVB ? "" : "    ") + source[body.End..];

        // Control fields: drop the old ones and write one per control.
        var fieldPattern = IsVB
            ? @"^[ \t]*(?:Friend|Private|Public|Protected)\s+WithEvents\s+(\w+)\s+As\s+[\w.]+[ \t]*\r?\n"
            : @"^[ \t]*(?:private|internal|public|protected)\s+(?:[\w.]+)\s+(\w+)\s*;[ \t]*\r?\n";
        text = Regex.Replace(text, fieldPattern, match =>
            originalFields.Contains(match.Groups[1].Value) || Control(match.Groups[1].Value) != null ? "" : match.Value, RegexOptions.Multiline);
        var fields = string.Concat(Controls.Select(c => IsVB
            ? $"    Friend WithEvents {c.Name} As {c.Type}{newline}"
            : $"    private {c.Type} {c.Name};{newline}"));
        if (IsVB)
        {
            var endSub = Regex.Match(text[text.IndexOf("InitializeComponent", StringComparison.OrdinalIgnoreCase)..], @"End\s+Sub[ \t]*\r?\n", RegexOptions.IgnoreCase);
            var at = text.IndexOf("InitializeComponent", StringComparison.OrdinalIgnoreCase) + endSub.Index + endSub.Length;
            // Collapse the blank lines left where the old fields were.
            var rest = Regex.Replace(text[at..], @"^(\s*\r?\n)+", "");
            text = text[..at] + (fields.Length > 0 ? newline + fields : "") + newline + rest;
        }
        else
        {
            var region = text.IndexOf("#endregion", StringComparison.Ordinal);
            if (region >= 0)
            {
                var lineEnd = text.IndexOf('\n', region) + 1;
                var rest = Regex.Replace(text[lineEnd..], @"^(\s*\r?\n)+", "");
                text = text[..lineEnd] + (fields.Length > 0 ? newline + fields : "") + rest;
            }
        }
        return text;
    }

    // MARK: Event handlers

    public string HandlerName(string? controlName, string eventName) => $"{controlName ?? ClassName}_{eventName}";

    /// <summary>
    /// Makes sure the form's code file has a handler for an event (Visual Studio's double-click on a control), and returns
    /// the new text and the 1-based line to put the cursor on. In C#, also wires the event in InitializeComponent.
    /// </summary>
    public (string Code, int Line) AddHandler(string code, string? controlName, string eventName, string eventArgs)
    {
        var handler = HandlerName(controlName, eventName);
        var newline = code.Contains("\r\n") ? "\r\n" : "\n";
        int LineOf(int offset) => code[..offset].Count(c => c == '\n') + 1;
        if (IsVB)
        {
            var source = controlName ?? "MyBase";
            var existing = Regex.Match(code, $@"Handles\s+(?:[\w.]+\s*,\s*)*{Regex.Escape(source)}\.{Regex.Escape(eventName)}\b", RegexOptions.IgnoreCase);
            if (existing.Success) return (code, LineOf(existing.Index) + 1);
            var end = Regex.Matches(code, @"^[ \t]*End\s+Class\b", RegexOptions.Multiline | RegexOptions.IgnoreCase).LastOrDefault();
            if (end == null) return (code, 1);
            var method = $"    Private Sub {handler}(sender As Object, e As {eventArgs}) Handles {source}.{eventName}{newline}{newline}    End Sub{newline}";
            var before = code[..end.Index];
            var spacer = Regex.IsMatch(before, @"(\r?\n)\s*(\r?\n)\s*$") || before.TrimEnd().EndsWith("Class " + ClassName, StringComparison.OrdinalIgnoreCase) ? "" : newline;
            var updated = before + spacer + method + code[end.Index..];
            return (updated, LineOf(end.Index) + (spacer.Length > 0 ? 2 : 1));
        }
        else
        {
            var wired = controlName == null ? FormEvents : Control(controlName)?.Events;
            if (wired != null && !wired.Any(e => e.Event == eventName)) wired.Add((eventName, handler));
            var existing = Regex.Match(code, $@"void\s+{Regex.Escape(handler)}\s*\(");
            if (existing.Success) return (code, LineOf(existing.Index) + 2);
            // The class's closing brace: the last one, or the one before it when the namespace has braces.
            var braces = Regex.Matches(code, @"^\s*\}", RegexOptions.Multiline);
            var blockNamespace = Regex.IsMatch(code, @"namespace\s+[\w.]+\s*\{|namespace\s+[\w.]+\s*\r?\n\s*\{");
            var closing = braces.Count >= (blockNamespace ? 2 : 1) ? braces[braces.Count - (blockNamespace ? 2 : 1)] : null;
            if (closing == null) return (code, 1);
            var indent = blockNamespace ? "        " : "    ";
            var at = code.LastIndexOf('\n', Math.Max(closing.Index + closing.Value.IndexOf('}') - 1, 0)) + 1;   // start of the brace's line
            var method = $"{newline}{indent}private void {handler}(object sender, {eventArgs} e){newline}{indent}{{{newline}{newline}{indent}}}{newline}";
            var updated = code[..at].TrimEnd('\r', '\n', ' ', '\t') + newline + method + code[at..];
            return (updated, updated[..updated.IndexOf($"void {handler}(", StringComparison.Ordinal)].Count(c => c == '\n') + 3);
        }
    }

    /// <summary>VB.NET's Handles clauses name controls; renaming a control renames them too.</summary>
    public static string RenameInCode(string code, string language, string oldName, string newName) => language == "vb"
        ? Regex.Replace(code, $@"(Handles\s+(?:[\w.]+\s*,\s*)*){Regex.Escape(oldName)}\.", $"$1{newName}.", RegexOptions.IgnoreCase)
        : code;
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CodeForge.Core;
using System.Windows.Forms.Integration;
using WinForms = System.Windows.Forms;

namespace CodeForge.Designer;

/// <summary>
/// The Properties window (F4), under Solution Explorer: Windows Forms' own PropertyGrid, as in Visual Studio, showing
/// the control selected in the designer. Edits go back to the designer, which writes them into the form's code.
/// </summary>
public sealed class PropertiesPane : DockPanel
{
    private readonly WinForms.PropertyGrid grid = new() { ToolbarVisible = true, HelpVisible = true, PropertySort = WinForms.PropertySort.CategorizedAlphabetical };
    private readonly WindowsFormsHost host = new();
    private readonly TextBlock heading = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 10, 6), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock empty = new() { Text = "Select a control in the Windows Forms designer to see its properties.", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(16), VerticalAlignment = VerticalAlignment.Center };
    private DesignSurface? surface;
    private bool reverting;

    public PropertiesPane()
    {
        var title = new TextBlock { Text = "PROPERTIES", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 8, 10, 4) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        empty.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        SetDock(title, Dock.Top);
        SetDock(heading, Dock.Top);
        Children.Add(title);
        Children.Add(heading);
        host.Child = grid;
        var body = new Grid();
        body.Children.Add(host);
        body.Children.Add(empty);
        Children.Add(body);
        grid.PropertyValueChanged += OnValueChanged;
        ApplyTheme();
        Themes.Changed += ApplyTheme;
        Show(null);
    }

    public string Heading => heading.Text;

    /// <summary>Follows a designer's selection, or shows nothing (null).</summary>
    public void Show(DesignSurface? designer)
    {
        if (surface != null) surface.SelectionChanged -= OnSelection;
        surface = designer;
        if (surface != null) surface.SelectionChanged += OnSelection;
        host.Visibility = designer == null ? Visibility.Collapsed : Visibility.Visible;
        empty.Visibility = designer == null ? Visibility.Visible : Visibility.Collapsed;
        heading.Text = "";
        if (designer == null) grid.SelectedObject = null;
    }

    /// <summary>With no designer in front, the current file's properties (read-only), as Visual Studio shows them.</summary>
    public void ShowFile(string? path, string? language, int? lines, string? encoding)
    {
        if (surface != null) return;
        if (path == null || !System.IO.File.Exists(path))
        {
            empty.Text = path == null ? "Open a file to see its properties, or a form in the designer to see its controls." : "Save the file to see its properties.";
            return;
        }
        var info = new System.IO.FileInfo(path);
        grid.SelectedObject = new FileProperties(info, language ?? "", lines ?? 0, encoding ?? "");
        heading.Text = info.Name + "  File Properties";
        host.Visibility = Visibility.Visible;
        empty.Visibility = Visibility.Collapsed;
    }

    /// <summary>An HTML element's attributes at the caret, editable as in Visual Studio's web designer.</summary>
    /// <param name="change">(index, name, value): a new value; a null name removes the attribute; an index past the end adds one.</param>
    public void ShowHtml(HtmlTag tag, Action<int, string?, string?> change)
    {
        if (surface != null) return;
        grid.SelectedObject = new HtmlElementProperties(tag, change);
        heading.Text = $"<{tag.Name}>  Element";
        host.Visibility = Visibility.Visible;
        empty.Visibility = Visibility.Collapsed;
    }

    private void OnSelection(object target, string title)
    {
        heading.Text = title;
        if (!ReferenceEquals(grid.SelectedObject, target)) grid.SelectedObject = target; else grid.Refresh();
    }

    private void OnValueChanged(object? sender, WinForms.PropertyValueChangedEventArgs e)
    {
        if (reverting || surface == null || e.ChangedItem?.PropertyDescriptor is not { } descriptor) return;
        var problem = surface.PropertyEdited(descriptor.Name, e.ChangedItem.Value);
        if (problem == null) return;
        reverting = true;
        try { descriptor.SetValue(grid.SelectedObject, e.OldValue); } catch (Exception) { }
        reverting = false;
        grid.Refresh();
        MessageBox.Show(Window.GetWindow(this), problem, "Properties", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ApplyTheme()
    {
        var p = Themes.Current;
        System.Drawing.Color C(string hex) => System.Drawing.ColorTranslator.FromHtml(hex.Length == 9 ? "#" + hex[3..] : hex);
        grid.BackColor = C(p.Panel);
        grid.ViewBackColor = C(p.Editor);
        grid.ViewForeColor = C(p.EditorText);
        grid.LineColor = C(p.Panel);
        grid.CategoryForeColor = C(p.Text);
        grid.CategorySplitterColor = C(p.Panel);
        grid.HelpBackColor = C(p.Panel);
        grid.HelpForeColor = C(p.Text);
        grid.HelpBorderColor = C(p.Border);
        grid.ViewBorderColor = C(p.Border);
        grid.CommandsBackColor = C(p.Panel);
        grid.SelectedItemWithFocusBackColor = C(p.Accent);
        grid.DisabledItemForeColor = C(p.SubtleText);
    }
}

/// <summary>What the Properties window shows for a file.</summary>
public sealed class FileProperties(System.IO.FileInfo info, string language, int lines, string encoding)
{
    [System.ComponentModel.Category("File"), System.ComponentModel.ReadOnly(true), System.ComponentModel.Description("The file's name.")]
    public string Name => info.Name;
    [System.ComponentModel.Category("File"), System.ComponentModel.ReadOnly(true), System.ComponentModel.DisplayName("Full Path"), System.ComponentModel.Description("Where the file is on disk.")]
    public string FullPath => info.FullName;
    [System.ComponentModel.Category("File"), System.ComponentModel.ReadOnly(true), System.ComponentModel.Description("The language Kryndev uses for the file.")]
    public string Language => language;
    [System.ComponentModel.Category("File"), System.ComponentModel.ReadOnly(true), System.ComponentModel.Description("The size of the saved file.")]
    public string Size => info.Length < 1024 ? $"{info.Length} bytes" : info.Length < 1024 * 1024 ? $"{info.Length / 1024.0:0.#} KB" : $"{info.Length / 1048576.0:0.#} MB";
    [System.ComponentModel.Category("File"), System.ComponentModel.ReadOnly(true), System.ComponentModel.Description("When the file was last saved.")]
    public DateTime Modified => info.LastWriteTime;
    [System.ComponentModel.Category("Text"), System.ComponentModel.ReadOnly(true), System.ComponentModel.Description("Lines in the editor.")]
    public int Lines => lines;
    [System.ComponentModel.Category("Text"), System.ComponentModel.ReadOnly(true), System.ComponentModel.Description("The character encoding the file is saved in.")]
    public string Encoding => encoding;
}

/// <summary>
/// An HTML element for the PropertyGrid: one row per attribute, and "(New attribute)" where typing name=value adds one.
/// Clearing a value removes its attribute.
/// </summary>
public sealed class HtmlElementProperties(HtmlTag tag, Action<int, string?, string?> change) : CustomTypeDescriptor
{
    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();

    public override PropertyDescriptorCollection GetProperties()
    {
        var rows = tag.Attributes.Select((a, i) => (PropertyDescriptor)new AttributeRow(a.Name, a.Value ?? "", i, change)).ToList();
        rows.Add(new AttributeRow("(New attribute)", "", -1, (_, name, value) =>
        {
            var text = value ?? "";
            var equals = text.IndexOf('=');
            var key = (equals < 0 ? text : text[..equals]).Trim();
            if (key.Length > 0) change(tag.Attributes.Count, key, equals < 0 ? null : text[(equals + 1)..].Trim().Trim('"', '\''));
        }));
        return new PropertyDescriptorCollection(rows.ToArray());
    }

    public override object? GetPropertyOwner(PropertyDescriptor? pd) => this;

    private sealed class AttributeRow(string name, string value, int index, Action<int, string?, string?> change)
        : PropertyDescriptor(name, [new CategoryAttribute(index < 0 ? "Add" : "Attributes"),
                                    new DescriptionAttribute(index < 0 ? "Type name=value to add an attribute." : "Clear the value to remove the attribute.")])
    {
        public override Type ComponentType => typeof(HtmlElementProperties);
        public override bool IsReadOnly => false;
        public override Type PropertyType => typeof(string);
        public override bool CanResetValue(object component) => false;
        public override object GetValue(object? component) => value;
        public override void ResetValue(object component) { }
        public override bool ShouldSerializeValue(object component) => false;

        public override void SetValue(object? component, object? newValue)
        {
            var text = newValue as string ?? "";
            if (index < 0) { change(-1, null, text); return; }
            if (text.Length == 0 && value.Length > 0) change(index, null, null);
            else change(index, Name, text);
        }
    }
}

using System.Windows;
using System.Windows.Controls;
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

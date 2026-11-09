using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CodeForge.Core;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace CodeForge.Designer;

/// <summary>
/// The Windows Forms designer surface: the form with its title bar and controls, which you select, move and resize
/// with the mouse or the arrow keys, and add to from the Toolbox. Controls are real Windows Forms controls drawn into
/// pictures (DrawToBitmap), so they look exactly as they will when the program runs.
/// </summary>
public sealed class DesignSurface : UserControl
{
    private const double Margin0 = 24;
    private const double TitleHeight = 31;
    private const int Grid = 4;            // moves and resizes snap to this many pixels

    private readonly DesignerDocument document;
    private readonly Canvas root = new() { Background = Brushes.Transparent };
    private readonly Border frame = new() { BorderThickness = new Thickness(1), SnapsToDevicePixels = true };
    private readonly Canvas client = new() { ClipToBounds = true };
    private readonly TextBlock title = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontFamily = new FontFamily("Segoe UI") };
    private readonly Canvas overlay = new() { IsHitTestVisible = false };
    private readonly Dictionary<string, WinForms.Control> instances = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image> pictures = new(StringComparer.OrdinalIgnoreCase);
    private WinForms.Form sandbox = new();
    private string? selected;            // null: the form itself
    private Drag? drag;

    /// <summary>The control the Toolbox has armed: the next click on the form places one.</summary>
    public ControlKind? ArmedTool { get; set; }

    /// <summary>(object for the Properties window, its title) whenever the selection or its values change.</summary>
    public event Action<object, string>? SelectionChanged;
    /// <summary>Double-click: (control name or null for the form) wants its default event handler.</summary>
    public event Action<string?>? HandlerRequested;
    public event Action? ToolUsed;
    public event Action? ViewCodeRequested;
    public event Action<string, string>? Renamed;

    private FormDesign Design => document.Design;
    private Point ClientOrigin => new(Margin0 + 1, Margin0 + 1 + TitleHeight);

    private sealed record Drag(string? Name, string Mode, Point Start, Rect Original, bool Snapshotted)
    {
        public bool Moved { get; set; }
    }

    public DesignSurface(DesignerDocument document)
    {
        this.document = document;
        Focusable = true;
        FocusVisualStyle = null;
        AllowDrop = true;
        var titleBar = new DockPanel { Height = TitleHeight, Background = Brushes.White };
        var buttons = new TextBlock { Text = "          ", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Foreground = Brushes.Black };
        DockPanel.SetDock(buttons, Dock.Right);
        titleBar.Children.Add(buttons);
        var icon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = Brushes.SteelBlue };
        DockPanel.SetDock(icon, Dock.Left);
        titleBar.Children.Add(icon);
        title.Foreground = Brushes.Black;
        titleBar.Children.Add(title);
        var stack = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);
        stack.Children.Add(titleBar);
        stack.Children.Add(client);
        frame.Child = stack;
        frame.BorderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        frame.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.25 };
        Canvas.SetLeft(frame, Margin0);
        Canvas.SetTop(frame, Margin0);
        root.Children.Add(frame);
        root.Children.Add(overlay);
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        scroll.SetResourceReference(BackgroundProperty, "PanelBrush");
        Content = scroll;

        root.MouseLeftButtonDown += OnMouseDown;
        root.MouseMove += OnMouseMove;
        root.MouseLeftButtonUp += OnMouseUp;
        root.MouseRightButtonUp += OnRightClick;
        KeyDown += OnKey;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(ToolboxFormat) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += OnDrop;
        Loaded += (_, _) => Focus();
        Unloaded += (_, _) => { };
        Rebuild();
    }

    public const string ToolboxFormat = "CodeForge.WinFormsControl";

    public string? SelectedName => selected;

    // MARK: Building the picture

    /// <summary>Creates fresh controls from the design and draws everything (after loading, undo or adding and removing).</summary>
    public void Rebuild()
    {
        foreach (var instance in instances.Values) instance.Dispose();
        instances.Clear();
        sandbox.Dispose();
        sandbox = new WinForms.Form { ShowInTaskbar = false, StartPosition = WinForms.FormStartPosition.Manual, Location = new Drawing.Point(-32000, -32000) };
        Apply(sandbox, Design.FormProperties);
        if (Design.FormProperty("ClientSize") is null && FormDesign.Pair(Design.FormProperty("Size")) is { } size) sandbox.Size = new Drawing.Size(size.A, size.B);
        _ = sandbox.Handle;   // children get handles (and can be drawn) once the form has one

        foreach (var control in Design.Controls)
        {
            var type = typeof(WinForms.Form).Assembly.GetType(control.Type);
            WinForms.Control instance;
            try
            {
                instance = type != null && typeof(WinForms.Control).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null
                    ? (WinForms.Control)Activator.CreateInstance(type)!
                    : new WinForms.Label { Text = control.Name, BorderStyle = WinForms.BorderStyle.FixedSingle, BackColor = Drawing.Color.Gainsboro };
            }
            catch (Exception) { instance = new WinForms.Label { Text = control.Name, BorderStyle = WinForms.BorderStyle.FixedSingle }; }
            sandbox.Controls.Add(instance);
            Apply(instance, control.Properties);
            instance.Name = control.Name;
            instances[control.Name] = instance;
        }
        if (selected != null && !instances.ContainsKey(selected)) selected = null;
        Redraw();
        NotifySelection();
    }

    private void Apply(object target, IEnumerable<(string Property, string Value)> properties)
    {
        foreach (var (property, expression) in properties)
        {
            var info = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            if (info is not { CanWrite: true }) continue;
            if (WinFormsValues.Evaluate(expression, info.PropertyType, Design) is { } value)
            {
                try { info.SetValue(target, value); } catch (Exception) { }
            }
        }
    }

    /// <summary>Draws the frame and every control from their current state.</summary>
    public void Redraw()
    {
        client.Children.Clear();
        pictures.Clear();
        var clientSize = sandbox.ClientSize;
        client.Width = clientSize.Width;
        client.Height = clientSize.Height;
        client.Background = new SolidColorBrush(ToMedia(sandbox.BackColor));
        title.Text = sandbox.Text;
        root.Width = Margin0 * 2 + clientSize.Width + 40;
        root.Height = Margin0 * 2 + clientSize.Height + TitleHeight + 40;
        foreach (var control in DrawOrder()) Draw(control.Name);
        UpdateOverlay();
    }

    /// <summary>Containers before their contents; later controls on top, like Windows Forms' z-order.</summary>
    private List<DesignedControl> DrawOrder()
    {
        var order = new List<DesignedControl>();
        void Visit(string? parent)
        {
            foreach (var control in Design.Controls.Where(c => string.Equals(c.Parent, parent, StringComparison.OrdinalIgnoreCase)))
            {
                order.Add(control);
                Visit(control.Name);
            }
        }
        Visit(null);
        return order;
    }

    private void Draw(string name)
    {
        if (!instances.TryGetValue(name, out var instance)) return;
        var bounds = Bounds(name);
        if (pictures.TryGetValue(name, out var existing)) client.Children.Remove(existing);
        var picture = new Image { Width = Math.Max(bounds.Width, 1), Height = Math.Max(bounds.Height, 1), IsHitTestVisible = false, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
        picture.Source = Picture(instance);
        Canvas.SetLeft(picture, bounds.X);
        Canvas.SetTop(picture, bounds.Y);
        pictures[name] = picture;
        // Keep the z-order: insert after its container and the controls drawn before it.
        var order = DrawOrder().Select(c => c.Name).ToList();
        var position = order.IndexOf(name);
        var index = client.Children.OfType<Image>().Count(i => order.IndexOf(pictures.FirstOrDefault(p => p.Value == i).Key ?? "") < position);
        client.Children.Insert(Math.Min(index, client.Children.Count), picture);
    }

    private static BitmapSource? Picture(WinForms.Control control)
    {
        if (control.Width < 1 || control.Height < 1) return null;
        try
        {
            using var bitmap = new Drawing.Bitmap(control.Width, control.Height);
            control.DrawToBitmap(bitmap, new Drawing.Rectangle(0, 0, control.Width, control.Height));
            using var stream = new MemoryStream();
            bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
    }

    private static Color ToMedia(Drawing.Color color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>A control's rectangle in client coordinates (its containers' positions added up).</summary>
    private Rect Bounds(string name)
    {
        if (!instances.TryGetValue(name, out var instance)) return Rect.Empty;
        double x = instance.Left, y = instance.Top;
        var parent = Design.Control(name)?.Parent;
        while (parent != null && instances.TryGetValue(parent, out var container))
        {
            x += container.Left;   // a child's Location is measured from its container's top-left corner
            y += container.Top;
            parent = Design.Control(parent)?.Parent;
        }
        return new Rect(x, y, instance.Width, instance.Height);
    }

    // MARK: Selection

    public void Select(string? name)
    {
        selected = name != null && instances.ContainsKey(name) ? Design.Control(name)!.Name : null;
        UpdateOverlay();
        NotifySelection();
    }

    private void NotifySelection()
    {
        if (selected != null && instances.TryGetValue(selected, out var instance))
            SelectionChanged?.Invoke(instance, $"{selected}  {Design.Control(selected)!.Type}");
        else
            SelectionChanged?.Invoke(sandbox, $"{Design.ClassName}  System.Windows.Forms.Form");
    }

    private Rect SelectionRect()
    {
        var origin = ClientOrigin;
        if (selected == null) return new Rect(origin.X, origin.Y, client.Width, client.Height);
        var bounds = Bounds(selected);
        bounds.Offset(origin.X, origin.Y);
        return bounds;
    }

    private IEnumerable<(string Mode, Point Point)> Handles(Rect rect)
    {
        double l = rect.Left, t = rect.Top, r = rect.Right, b = rect.Bottom, cx = (l + r) / 2, cy = (t + b) / 2;
        if (selected == null)
        {
            yield return ("E", new Point(r, cy));
            yield return ("S", new Point(cx, b));
            yield return ("SE", new Point(r, b));
            yield break;
        }
        if (Design.Control(selected)?.Kind?.AutoSize == true && Design.Control(selected)!.Get("AutoSize") is { } auto && auto.Contains("rue")) yield break;
        foreach (var handle in new[] { ("NW", l, t), ("N", cx, t), ("NE", r, t), ("E", r, cy), ("SE", r, b), ("S", cx, b), ("SW", l, b), ("W", l, cy) })
            yield return (handle.Item1, new Point(handle.Item2, handle.Item3));
    }

    private void UpdateOverlay()
    {
        overlay.Children.Clear();
        var rect = SelectionRect();
        if (selected != null)
        {
            var outline = new Rectangle { Width = rect.Width + 2, Height = rect.Height + 2, Stroke = Brushes.DimGray, StrokeThickness = 1, StrokeDashArray = [3, 2] };
            Canvas.SetLeft(outline, rect.Left - 1);
            Canvas.SetTop(outline, rect.Top - 1);
            overlay.Children.Add(outline);
        }
        foreach (var (_, point) in Handles(rect))
        {
            var handle = new Rectangle { Width = 7, Height = 7, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1 };
            Canvas.SetLeft(handle, point.X - 3.5);
            Canvas.SetTop(handle, point.Y - 3.5);
            overlay.Children.Add(handle);
        }
    }

    // MARK: Mouse

    private string? HitTest(Point inClient)
    {
        foreach (var control in Enumerable.Reverse(DrawOrder()))
            if (Bounds(control.Name).Contains(inClient)) return control.Name;
        return null;
    }

    /// <summary>The innermost container under a point (excluding a control being moved and its contents).</summary>
    private string? ContainerAt(Point inClient, string? excluding = null)
    {
        foreach (var control in Enumerable.Reverse(DrawOrder()))
        {
            if (control.Kind?.IsContainer != true || IsWithin(control.Name, excluding)) continue;
            if (Bounds(control.Name).Contains(inClient)) return control.Name;
        }
        return null;
    }

    private bool IsWithin(string name, string? ancestor)
    {
        for (var current = name; current != null; current = Design.Control(current)?.Parent)
            if (ancestor != null && current.Equals(ancestor, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private Point ToClient(Point inRoot) => new(inRoot.X - ClientOrigin.X, inRoot.Y - ClientOrigin.Y);

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        var point = e.GetPosition(root);
        var inClient = ToClient(point);
        if (e.ClickCount == 2)
        {
            var target = HitTest(inClient);
            if (target != null || new Rect(0, 0, client.Width, client.Height).Contains(inClient)) HandlerRequested?.Invoke(target);
            return;
        }
        if (ArmedTool is { } tool && new Rect(0, 0, client.Width, client.Height).Contains(inClient))
        {
            Place(tool, inClient);
            return;
        }
        foreach (var (mode, handle) in Handles(SelectionRect()))
        {
            if (Math.Abs(point.X - handle.X) <= 5 && Math.Abs(point.Y - handle.Y) <= 5)
            {
                drag = new Drag(selected, mode, point, selected == null ? new Rect(0, 0, client.Width, client.Height) : CurrentRect(selected), false);
                root.CaptureMouse();
                return;
            }
        }
        var hit = HitTest(inClient);
        Select(hit);
        if (hit != null)
        {
            drag = new Drag(hit, "move", point, CurrentRect(hit), false);
            root.CaptureMouse();
        }
    }

    /// <summary>Location and size in the control's own container.</summary>
    private Rect CurrentRect(string name) => instances.TryGetValue(name, out var c) ? new Rect(c.Left, c.Top, c.Width, c.Height) : Rect.Empty;

    private static int Snap(double value) => (int)(Math.Round(value / Grid) * Grid);

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (drag == null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(root);
        double dx = point.X - drag.Start.X, dy = point.Y - drag.Start.Y;
        if (!drag.Moved && Math.Abs(dx) < 3 && Math.Abs(dy) < 3) return;
        if (!drag.Moved)
        {
            document.Snapshot();
            drag.Moved = true;
        }
        var r = drag.Original;
        double left = r.Left, top = r.Top, width = r.Width, height = r.Height;
        if (drag.Mode == "move")
        {
            left = Snap(r.Left + dx);
            top = Snap(r.Top + dy);
        }
        else
        {
            if (drag.Mode.Contains('E')) width = Math.Max(Snap(r.Width + dx), 8);
            if (drag.Mode.Contains('S')) height = Math.Max(Snap(r.Height + dy), 8);
            if (drag.Mode.Contains('W')) { var right = r.Right; left = Math.Min(Snap(r.Left + dx), right - 8); width = right - left; }
            if (drag.Mode.Contains('N')) { var bottom = r.Bottom; top = Math.Min(Snap(r.Top + dy), bottom - 8); height = bottom - top; }
        }
        if (drag.Name == null)
        {
            sandbox.ClientSize = new Drawing.Size((int)Math.Max(width, 120), (int)Math.Max(height, 60));
            client.Width = sandbox.ClientSize.Width;
            client.Height = sandbox.ClientSize.Height;
            root.Width = Margin0 * 2 + client.Width + 40;
            root.Height = Margin0 * 2 + client.Height + TitleHeight + 40;
        }
        else if (instances.TryGetValue(drag.Name, out var instance))
        {
            instance.Bounds = new Drawing.Rectangle((int)left, (int)top, (int)width, (int)height);
            foreach (var control in DrawOrder().Where(c => IsWithin(c.Name, drag.Name)))
            {
                if (pictures.TryGetValue(control.Name, out var picture))
                {
                    var bounds = Bounds(control.Name);
                    Canvas.SetLeft(picture, bounds.X);
                    Canvas.SetTop(picture, bounds.Y);
                    if (control.Name.Equals(drag.Name, StringComparison.OrdinalIgnoreCase) && drag.Mode != "move") Draw(control.Name);
                }
            }
        }
        UpdateOverlay();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        root.ReleaseMouseCapture();
        var finished = drag;
        drag = null;
        if (finished is not { Moved: true }) return;
        if (finished.Name == null)
        {
            Design.SetFormProperty("ClientSize", Design.SizeValue(sandbox.ClientSize.Width, sandbox.ClientSize.Height));
            document.Changed();
            NotifySelection();
            return;
        }
        var control = Design.Control(finished.Name)!;
        var instance = instances[finished.Name];
        // Dropped into a different container (or out of one): it moves there, keeping its place on screen.
        if (finished.Mode == "move")
        {
            var absolute = Bounds(control.Name);
            var center = new Point(absolute.X + 4, absolute.Y + 4);
            var target = ContainerAt(center, excluding: control.Name);
            if (!string.Equals(target, control.Parent, StringComparison.OrdinalIgnoreCase))
            {
                var offset = target != null ? Bounds(target) : new Rect(0, 0, 0, 0);
                control.Parent = target;
                control.Set("Location", Design.Point((int)(absolute.X - offset.X), (int)(absolute.Y - offset.Y)));
                document.Changed();
                Rebuild();
                Select(control.Name);
                return;
            }
        }
        control.Set("Location", Design.Point(instance.Left, instance.Top));
        if (finished.Mode != "move") control.Set("Size", Design.SizeValue(instance.Width, instance.Height));
        document.Changed();
        Draw(control.Name);
        UpdateOverlay();
        NotifySelection();
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        var hit = HitTest(ToClient(e.GetPosition(root)));
        Select(hit);
        var menu = new ContextMenu();
        void Add(string header, Action action, bool enabled = true) => menu.Items.Add(new MenuItem { Header = header, IsEnabled = enabled, Command = new RelayCommand(action) });
        Add("View Code", () => ViewCodeRequested?.Invoke());
        menu.Items.Add(new Separator());
        Add("Bring to Front", () => Reorder(true), hit != null);
        Add("Send to Back", () => Reorder(false), hit != null);
        Add("Delete", DeleteSelected, hit != null);
        menu.Items.Add(new Separator());
        Add("Undo", () => document.Undo(), document.CanUndo);
        menu.IsOpen = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(ToolboxFormat) is string name && ControlKinds.Find(name) is { } kind)
            Place(kind, ToClient(e.GetPosition(root)));
    }

    // MARK: Editing

    /// <summary>Adds a control from the Toolbox at a point (inside the container there, if any).</summary>
    public void Place(ControlKind kind, Point inClient)
    {
        document.Snapshot();
        var container = ContainerAt(inClient);
        var x = inClient.X;
        var y = inClient.Y;
        if (container != null)
        {
            var bounds = Bounds(container);
            x -= bounds.X;
            y -= bounds.Y;
        }
        var control = Design.Add(kind, Snap(Math.Max(x, 0)), Snap(Math.Max(y, 0)), container);
        ArmedTool = null;
        ToolUsed?.Invoke();
        document.Changed();
        Rebuild();
        // Auto-sized controls (Label, CheckBox…) know their real size only now; write it like Visual Studio does.
        if (control.Kind?.AutoSize == true && instances.TryGetValue(control.Name, out var instance))
            control.Set("Size", Design.SizeValue(instance.Width, instance.Height));
        Select(control.Name);
    }

    /// <summary>Adds a control at the next free spot (Toolbox double-click).</summary>
    public void PlaceDefault(ControlKind kind)
    {
        var offset = 12 + 8 * (Design.Controls.Count % 10);
        Place(kind, new Point(offset, offset));
    }

    public void DeleteSelected()
    {
        if (selected == null) return;
        document.Snapshot();
        Design.Remove(selected);
        selected = null;
        document.Changed();
        Rebuild();
    }

    private void Reorder(bool toFront)
    {
        if (selected == null) return;
        document.Snapshot();
        Design.Reorder(selected, toFront);
        document.Changed();
        Rebuild();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; return; }
        if (e.Key == Key.Escape)
        {
            ArmedTool = null;
            ToolUsed?.Invoke();
            Select(selected != null ? Design.Control(selected)?.Parent : null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Z && modifiers == ModifierKeys.Control) { document.Undo(); e.Handled = true; return; }
        if (e.Key == Key.Y && modifiers == ModifierKeys.Control) { document.Redo(); e.Handled = true; return; }
        if (e.Key == Key.F7) { ViewCodeRequested?.Invoke(); e.Handled = true; return; }
        if (e.Key == Key.Enter && selected != null) { HandlerRequested?.Invoke(selected); e.Handled = true; return; }
        var (dx, dy) = e.Key switch { Key.Left => (-1, 0), Key.Right => (1, 0), Key.Up => (0, -1), Key.Down => (0, 1), _ => (0, 0) };
        if ((dx, dy) == (0, 0) || selected == null || !instances.TryGetValue(selected, out var instance)) return;
        document.Snapshot();
        var control = Design.Control(selected)!;
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            instance.Size = new Drawing.Size(Math.Max(instance.Width + dx, 4), Math.Max(instance.Height + dy, 4));
            control.Set("Size", Design.SizeValue(instance.Width, instance.Height));
        }
        else
        {
            instance.Location = new Drawing.Point(instance.Left + dx, instance.Top + dy);
            control.Set("Location", Design.Point(instance.Left, instance.Top));
        }
        document.Changed();
        foreach (var name in DrawOrder().Where(c => IsWithin(c.Name, selected)).Select(c => c.Name)) Draw(name);
        UpdateOverlay();
        NotifySelection();
        e.Handled = true;
    }

    /// <summary>
    /// A value changed in the Properties window (it already changed the control). Writes it into the design, or returns
    /// why it couldn't (the Properties window then puts the old value back).
    /// </summary>
    public string? PropertyEdited(string property, object? value)
    {
        var isForm = selected == null;
        if (property == "Name")
        {
            if (isForm) return "The form's name is its class name; rename the class in the code to change it.";
            var newName = value as string ?? "";
            var oldName = selected!;
            document.Snapshot();
            if (!Design.Rename(oldName, newName)) { document.DropSnapshot(); return $"“{newName}” isn't a valid name, or another control already uses it."; }
            instances[newName] = instances[oldName];
            if (!newName.Equals(oldName, StringComparison.OrdinalIgnoreCase)) instances.Remove(oldName);
            pictures.Remove(oldName);
            selected = newName;
            document.Changed();
            Renamed?.Invoke(oldName, newName);
            Redraw();
            NotifySelection();
            return null;
        }
        // The form's outer Size is written as ClientSize, like Visual Studio does.
        if (isForm && property == "Size") { property = "ClientSize"; value = sandbox.ClientSize; }
        var code = WinFormsValues.Serialize(value, Design);
        if (code == null) return $"Kryndev can't save the {property} property yet. Set it in code instead (in the form's Load event, for example).";
        document.Snapshot();
        if (isForm) Design.SetFormProperty(property, code); else Design.Control(selected!)!.Set(property, code);
        // Auto-sized controls change size when their text or font changes; keep the written Size in step.
        if (!isForm && instances.TryGetValue(selected!, out var instance) && instance.AutoSize)
            Design.Control(selected!)!.Set("Size", Design.SizeValue(instance.Width, instance.Height));
        document.Changed();
        if (isForm) Redraw(); else { foreach (var name in DrawOrder().Where(c => IsWithin(c.Name, selected)).Select(c => c.Name)) Draw(name); UpdateOverlay(); }
        return null;
    }

    /// <summary>For tests: set a property from text, as if typed in the Properties window.</summary>
    public string? SetFromText(string property, string text)
    {
        object target = selected != null ? instances[selected] : sandbox;
        var descriptor = System.ComponentModel.TypeDescriptor.GetProperties(target)[property];
        if (descriptor == null) return $"No property {property}";
        var value = descriptor.Converter.ConvertFromInvariantString(text);
        descriptor.SetValue(target, value);
        return PropertyEdited(property, value);
    }

    public void Close()
    {
        foreach (var instance in instances.Values) instance.Dispose();
        sandbox.Dispose();
    }
}

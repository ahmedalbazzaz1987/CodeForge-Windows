using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodeForge.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CodeForge.Designer;

/// <summary>
/// Design, Split and Source views for HTML pages, like Visual Studio's web designer and the Mac version's: the page as a
/// browser shows it (Microsoft Edge WebView2), the source, or both. Clicking an element puts the caret in its tag, so
/// the Properties window shows its attributes.
/// </summary>
public sealed class HtmlDesignHost : DockPanel
{
    public enum Mode { Design, Split, Source }

    /// <summary>Remembered for the next page (Settings saves it).</summary>
    public static Mode LastMode { get; set; } = Mode.Split;

    /// <summary>The element clicked in the design view: its tag name and its index among the page's elements with that name.</summary>
    public event Action<string, int>? ElementClicked;

    public Mode Current { get; private set; }

    private const string Host = "codeforge.page";
    private const string ClickScript = """
        document.addEventListener('click', function (event) {
          event.preventDefault(); event.stopPropagation();
          if (event.detail === 0) { return; }
          var element = event.target, tag = element.tagName.toLowerCase();
          if (tag === 'html' || tag === 'head') { return; }
          var index = Array.prototype.indexOf.call(document.getElementsByTagName(tag), element);
          document.querySelectorAll('[data-codeforge-selected]').forEach(function (e) { e.removeAttribute('data-codeforge-selected'); e.style.outline = ''; });
          element.setAttribute('data-codeforge-selected', '1');
          element.style.outline = '2px solid #007ACC';
          window.chrome.webview.postMessage({ tag: tag, index: index });
        }, true);
        """;

    private readonly EditorDocument document;
    private readonly WebView2 web = new();
    private readonly TextBlock unavailable = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16), Visibility = Visibility.Collapsed };
    private readonly Grid area = new();
    private readonly GridSplitter splitter = new() { Height = 4, HorizontalAlignment = HorizontalAlignment.Stretch, ShowsPreview = false };
    private readonly List<Button> buttons = [];
    private readonly DispatcherTimer reloadTimer;
    private bool ready;
    private double scrollY;

    public HtmlDesignHost(EditorDocument document)
    {
        this.document = document;
        reloadTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        reloadTimer.Tick += (_, _) => { reloadTimer.Stop(); Reload(); };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Height = 26, Margin = new Thickness(6, 0, 6, 0) };
        bar.SetResourceReference(BackgroundProperty, "ChromeBrush");
        foreach (var mode in Enum.GetValues<Mode>())
        {
            var button = new Button { Content = mode.ToString(), Tag = mode, Padding = new Thickness(10, 1, 10, 1), Margin = new Thickness(0, 2, 4, 2), BorderThickness = new Thickness(0) };
            button.SetResourceReference(BackgroundProperty, "ChromeBrush");
            button.Click += (_, _) => SetMode(mode);
            buttons.Add(button);
            bar.Children.Add(button);
        }
        var barBorder = new Border { Child = bar, BorderThickness = new Thickness(0, 1, 0, 0) };
        barBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        barBorder.SetResourceReference(Border.BackgroundProperty, "ChromeBrush");
        SetDock(barBorder, Dock.Bottom);
        Children.Add(barBorder);

        area.RowDefinitions.Add(new RowDefinition());
        area.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        area.RowDefinitions.Add(new RowDefinition());
        var design = new Grid();
        design.Children.Add(web);
        design.Children.Add(unavailable);
        unavailable.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        Grid.SetRow(design, 0);
        Grid.SetRow(splitter, 1);
        Grid.SetRow(document.Editor, 2);
        splitter.SetResourceReference(BackgroundProperty, "BorderBrush");
        area.Children.Add(design);
        area.Children.Add(splitter);
        area.Children.Add(document.Editor);
        Children.Add(area);

        document.Editor.TextChanged += (_, _) => { if (Current != Mode.Source) { reloadTimer.Stop(); reloadTimer.Start(); } };
        SetMode(LastMode);
        Loaded += async (_, _) => await StartBrowser();
    }

    public void SetMode(Mode mode)
    {
        Current = mode;
        LastMode = mode;
        var design = mode != Mode.Source;
        var source = mode != Mode.Design;
        area.RowDefinitions[0].Height = design ? new GridLength(source ? 55 : 100, GridUnitType.Star) : new GridLength(0);
        area.RowDefinitions[2].Height = source ? new GridLength(design ? 45 : 100, GridUnitType.Star) : new GridLength(0);
        splitter.Visibility = design && source ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in buttons)
        {
            var selected = (Mode)button.Tag == mode;
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            button.SetResourceReference(ForegroundProperty, selected ? "AccentBrush" : "SubtleTextBrush");
        }
        if (design) Reload();
        if (mode == Mode.Source) Dispatcher.BeginInvoke(() => document.Editor.TextArea.Focus(), DispatcherPriority.Input);
    }

    private async Task StartBrowser()
    {
        if (ready || web.CoreWebView2 != null) return;
        try
        {
            // The profile goes in CodeForge's own folder: next to the program it may not be writable.
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeForge", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            await web.EnsureCoreWebView2Async(environment);
            var core = web.CoreWebView2!;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(ClickScript);
            core.WebMessageReceived += (_, e) =>
            {
                try
                {
                    using var json = JsonDocument.Parse(e.WebMessageAsJson);
                    ElementClicked?.Invoke(json.RootElement.GetProperty("tag").GetString() ?? "", json.RootElement.GetProperty("index").GetInt32());
                }
                catch (Exception) { }
            };
            core.NavigationCompleted += async (_, _) =>
            {
                if (scrollY > 0) await core.ExecuteScriptAsync($"window.scrollTo(0, {scrollY.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
            };
            ready = true;
            Reload();
        }
        catch (Exception error)
        {
            web.Visibility = Visibility.Collapsed;
            unavailable.Visibility = Visibility.Visible;
            unavailable.Text = "The design view needs the Microsoft Edge WebView2 Runtime, which comes with Windows 10 and 11. " +
                               "Install it from https://developer.microsoft.com/microsoft-edge/webview2/ if it's missing.\n\n" + error.Message;
        }
    }

    /// <summary>Shows the page as it is in the editor, keeping the scroll position. Relative links load from its folder.</summary>
    private async void Reload()
    {
        if (!ready || web.CoreWebView2 is not { } core || Current == Mode.Source) return;
        try
        {
            if (double.TryParse(await core.ExecuteScriptAsync("window.scrollY"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
                scrollY = y;
            if (document.Path is { } path && Path.GetDirectoryName(path) is { } folder)
                core.SetVirtualHostNameToFolderMapping(Host, folder, CoreWebView2HostResourceAccessKind.Allow);
            core.NavigateToString(HtmlTag.WithBase(document.Editor.Text, $"https://{Host}/"));
        }
        catch (Exception) { }
    }
}

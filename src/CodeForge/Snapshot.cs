using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CodeForge;

/// <summary>
/// For testing on machines nobody watches (GitHub's Windows runners): opens files, performs actions, saves PNG images
/// of every window and quits.
///   CF_SNAPSHOT=out.png   where to write the image (required)
///   CF_OPEN=path;path     folders or files to open first
///   CF_ACTIONS=a,b        start, build, errors, terminal, dark, blue, newproject, options, find (2 s apart)
///   CF_ACTION_GAP=seconds time between actions (default 2)
///   CF_DELAY=seconds      how long to wait before the picture (default 4)
/// </summary>
public static class Snapshot
{
    public static void RunIfRequested(MainWindow window)
    {
        var path = Environment.GetEnvironmentVariable("CF_SNAPSHOT");
        if (path == null) return;
        foreach (var target in (Environment.GetEnvironmentVariable("CF_OPEN") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            window.OpenPath(Path.GetFullPath(target));
        var actions = (Environment.GetEnvironmentVariable("CF_ACTIONS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var gap = double.TryParse(Environment.GetEnvironmentVariable("CF_ACTION_GAP"), out var g) ? g : 2;
        for (var i = 0; i < actions.Length; i++)
        {
            var action = actions[i].Trim();
            After(1 + i * gap, () => window.Perform(action));
        }
        var delay = double.TryParse(Environment.GetEnvironmentVariable("CF_DELAY"), out var seconds) ? seconds : 4;
        After(Math.Max(delay, (actions.Length - 1) * gap + 2), () =>
        {
            var index = 0;
            foreach (Window open in Application.Current.Windows)
            {
                if (!open.IsVisible) continue;
                Save(open, open == window ? path : $"{path}.window{++index}.png");
            }
            // A real screen capture too: it includes Windows Forms parts (the designer's Properties grid) that WPF can't draw.
            try
            {
                window.Activate();
                var dpi = VisualTreeHelper.GetDpi(window);
                var topLeft = window.PointToScreen(new Point(0, 0));
                var size = new System.Drawing.Size((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY));
                using var bitmap = new System.Drawing.Bitmap(size.Width, size.Height);
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen((int)topLeft.X, (int)topLeft.Y, 0, 0, size);
                bitmap.Save(path.Replace(".png", ".screen.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception error) { Console.Error.WriteLine("Screen capture failed: " + error.Message); }
            Console.WriteLine($"SNAPSHOT saved {path}");
            Environment.Exit(0);
        });
    }

    private static void After(double seconds, Action action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) => { timer.Stop(); action(); };
        timer.Start();
    }

    private static void Save(Window window, string path)
    {
        if (window.Content is not FrameworkElement content || content.ActualWidth < 1) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
                                            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
    }
}

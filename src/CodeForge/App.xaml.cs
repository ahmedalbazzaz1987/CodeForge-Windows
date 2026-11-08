using System.IO;
using System.Windows;
using System.Windows.Threading;
using CodeForge.Core;

namespace CodeForge;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        PathVariable.Refresh();
        // The form designer and Properties window use Windows Forms; give them the current Windows look.
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        Themes.Apply(Settings.Current.Theme);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        window.Restore(e.Args);
        Snapshot.RunIfRequested(window);
    }

    /// <summary>Shows unexpected errors instead of closing, and logs them for bug reports.</summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            File.AppendAllText(Path.Combine(Settings.Folder, "errors.log"), $"{DateTime.Now:u}\n{e.Exception}\n\n");
        }
        catch (IOException) { }
        if (Environment.GetEnvironmentVariable("CF_SNAPSHOT") != null)
        {
            Console.Error.WriteLine(e.Exception);
            Environment.Exit(3);
        }
        MessageBox.Show(e.Exception.Message, "CodeForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}

using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace CodeForge;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
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

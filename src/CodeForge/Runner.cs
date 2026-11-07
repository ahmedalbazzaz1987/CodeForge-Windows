using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Threading;

namespace CodeForge;

/// <summary>
/// Runs one command at a time through cmd.exe and streams its output (both streams) as it arrives, so prompts that
/// don't end with a line break still show. Input typed in the Output window goes to the program's stdin.
/// </summary>
public sealed class Runner
{
    private Process? process;
    private readonly Stopwatch clock = new();
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

    public bool IsRunning => process is { HasExited: false };
    /// <summary>The last run ended because Stop was pressed, not because it failed.</summary>
    public bool WasStopped { get; private set; }
    /// <summary>(text, isError)</summary>
    public event Action<string, bool>? Output;
    /// <summary>(exit code, seconds)</summary>
    public event Action<int, double>? Exited;

    public void Run(string command, string folder, IDictionary<string, string>? environment = null)
    {
        Stop();
        WasStopped = false;
        var info = new ProcessStartInfo("cmd.exe")
        {
            // /s keeps the quotes in the command as they are; /d skips AutoRun scripts.
            Arguments = "/d /s /c \"" + command + "\"",
            WorkingDirectory = folder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.Environment["PYTHONUNBUFFERED"] = "1";
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        info.Environment["DOTNET_NOLOGO"] = "1";
        if (environment != null) foreach (var (key, value) in environment) info.Environment[key] = value;

        var started = new Process { StartInfo = info, EnableRaisingEvents = true };
        try
        {
            started.Start();
        }
        catch (Exception error)
        {
            Output?.Invoke($"Couldn't start the command: {error.Message}\n", true);
            Exited?.Invoke(-1, 0);
            return;
        }
        process = started;
        clock.Restart();
        var pumps = new[] { Pump(started.StandardOutput, false), Pump(started.StandardError, true) };
        Task.Run(async () =>
        {
            await Task.WhenAll(pumps);
            await started.WaitForExitAsync();
            var seconds = clock.Elapsed.TotalSeconds;
            var code = started.ExitCode;
            await dispatcher.InvokeAsync(() =>
            {
                if (process == started) process = null;
                Exited?.Invoke(code, seconds);
            });
        });
    }

    private Task Pump(StreamReader reader, bool isError) => Task.Run(async () =>
    {
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            var text = new string(buffer, 0, count).Replace("\r\n", "\n");
            await dispatcher.InvokeAsync(() => Output?.Invoke(text, isError));
        }
    });

    public void Send(string line)
    {
        if (process is not { HasExited: false } running) return;
        try
        {
            running.StandardInput.WriteLine(line);
            running.StandardInput.Flush();
        }
        catch (IOException) { }
    }

    /// <summary>Stops the program and everything it started (like a web server under dotnet run).</summary>
    public void Stop()
    {
        if (process is not { HasExited: false } running) return;
        WasStopped = true;
        try { running.Kill(entireProcessTree: true); } catch (Exception) { }
    }
}

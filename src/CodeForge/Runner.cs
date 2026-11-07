using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using CodeForge.Core;

namespace CodeForge;

/// <summary>
/// Runs one command at a time through cmd.exe and streams its output (both streams) line by line, so stdout and stderr
/// don't cut into each other's lines; a prompt without a line break still shows after a short pause. Input typed in the
/// Output window goes to the program's stdin. Everything is UTF-8: the console's code page is switched to 65001 before
/// the command runs, so Arabic input, output and paths (dotnet build's file names too) come through intact.
/// </summary>
public sealed class Runner
{
    /// <summary>How long a partial line (a prompt such as "Name: ") waits for the rest before it's shown.</summary>
    private static readonly TimeSpan PartialLineDelay = TimeSpan.FromMilliseconds(120);

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);
    private const uint FailCriticalErrors = 0x0001, NoGpFaultErrorBox = 0x0002;

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
            Arguments = "/d /s /c \"chcp 65001 >nul & " + command + "\"",
            WorkingDirectory = folder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            // No byte order mark: it would reach the program as the first character it reads.
            StandardInputEncoding = new UTF8Encoding(false),
        };
        info.Environment["PYTHONUNBUFFERED"] = "1";
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        info.Environment["DOTNET_NOLOGO"] = "1";
        // MSBuild reads environment variables as properties: the F# compiler then reports in UTF-8 too (C# already does),
        // so file names in its errors keep their Arabic letters and the Error List can open them.
        info.Environment["Utf8Output"] = "true";
        if (environment != null) foreach (var (key, value) in environment) info.Environment[key] = value;

        var started = new Process { StartInfo = info, EnableRaisingEvents = true };
        // Child processes inherit the error mode. Without the crash dialog, Windows Error Reporting doesn't hold a crashed
        // program (an unhandled .NET exception) for half a minute before it ends.
        var previousMode = SetErrorMode(FailCriticalErrors | NoGpFaultErrorBox);
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
        finally
        {
            SetErrorMode(previousMode);
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
        var lines = new StreamLines();
        async Task Show(string text)
        {
            text = ConsoleText.StripAnsi(text);
            if (text.Length > 0) await dispatcher.InvokeAsync(() => Output?.Invoke(text, isError));
        }
        while (true)
        {
            var read = reader.ReadAsync(buffer, 0, buffer.Length);
            // A partial line (a prompt) is shown once the program pauses.
            if (lines.Pending.Length > 0 && await Task.WhenAny(read, Task.Delay(PartialLineDelay)) != read)
                await Show(lines.Flush());
            var count = await read;
            if (count <= 0) break;
            await Show(lines.Add(new string(buffer, 0, count)));
        }
        await Show(lines.Flush());
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

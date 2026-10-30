using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;

namespace CodeForge;

/// <summary>
/// The Terminal tab: a PowerShell session in the project folder for one-line commands (git, dotnet, npm, dir…).
/// It has no console window behind it, so full-screen programs don't work here. After each command a marker line
/// reports the current folder, which becomes the prompt.
/// </summary>
public sealed class Terminal
{
    private const string Marker = "\u001ECWD:";
    private readonly TextBox output;
    private readonly TextBox input;
    private readonly TextBlock prompt;
    private readonly List<string> history = [];
    private int historyIndex;
    private Process? shell;
    private string folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string pending = "";

    public Terminal(TextBox output, TextBox input, TextBlock prompt)
    {
        this.output = output;
        this.input = input;
        this.prompt = prompt;
        input.PreviewKeyDown += OnKey;
    }

    /// <summary>pwsh (PowerShell 7) when it's installed, otherwise Windows PowerShell.</summary>
    private static string ShellPath()
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try { if (File.Exists(Path.Combine(folder, "pwsh.exe"))) return Path.Combine(folder, "pwsh.exe"); } catch (ArgumentException) { }
        }
        return "powershell.exe";
    }

    public void Start(string startFolder)
    {
        input.Focus();
        if (shell is { HasExited: false }) return;
        folder = startFolder;
        var info = new ProcessStartInfo(ShellPath(), "-NoLogo -NoProfile -NoExit -Command -")
        {
            WorkingDirectory = Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        try
        {
            shell = Process.Start(info);
        }
        catch (Exception error)
        {
            Append($"Couldn't start PowerShell: {error.Message}\n");
            return;
        }
        if (shell == null) return;
        shell.StandardInput.AutoFlush = true;
        Pump(shell.StandardOutput);
        Pump(shell.StandardError);
        // UTF-8 output, and no progress bars (they need a real console).
        Send("[Console]::OutputEncoding = [Text.Encoding]::UTF8; $ProgressPreference = 'SilentlyContinue'", echo: false);
    }

    private void Pump(StreamReader reader) => Task.Run(async () =>
    {
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            var text = new string(buffer, 0, count).Replace("\r\n", "\n");
            await output.Dispatcher.InvokeAsync(() => Receive(text));
        }
    });

    public void SetFolder(string? path)
    {
        if (path == null || !Directory.Exists(path)) return;
        folder = path;
        if (shell is { HasExited: false }) Send($"Set-Location -LiteralPath '{path.Replace("'", "''")}'", echo: false);
    }

    private void Send(string command, bool echo)
    {
        if (echo) Append($"{prompt.Text} {command}\n");
        try
        {
            shell?.StandardInput.WriteLine(command);
            shell?.StandardInput.WriteLine($"Write-Output \"`n{Marker}$((Get-Location).Path)\"");
        }
        catch (IOException) { }
    }

    private void Receive(string text)
    {
        pending += text;
        int index;
        while ((index = pending.IndexOf(Marker, StringComparison.Ordinal)) >= 0)
        {
            var end = pending.IndexOf('\n', index);
            if (end < 0) break;
            var before = pending[..index];
            if (before.EndsWith('\n')) before = before[..^1];
            Append(before);
            var cwd = pending[(index + Marker.Length)..end].Trim();
            folder = cwd;
            prompt.Text = "PS " + (cwd.Length > 40 ? "…" + cwd[^38..] : cwd) + ">";
            pending = pending[(end + 1)..];
        }
        if (!pending.Contains('\u001E'))
        {
            Append(pending);
            pending = "";
        }
    }

    private void Append(string text)
    {
        if (text.Length == 0) return;
        output.AppendText(text);
        if (output.Text.Length > 1_000_000) output.Text = output.Text[^800_000..];
        output.ScrollToEnd();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                var command = input.Text;
                input.Clear();
                if (shell is not { HasExited: false }) Start(folder);
                if (command.Trim() is "cls" or "clear") { output.Clear(); e.Handled = true; return; }
                if (command.Length > 0 && (history.Count == 0 || history[^1] != command)) history.Add(command);
                historyIndex = history.Count;
                Send(command, echo: true);
                e.Handled = true;
                break;
            case Key.Up when history.Count > 0:
                historyIndex = Math.Max(historyIndex - 1, 0);
                input.Text = history[historyIndex];
                input.CaretIndex = input.Text.Length;
                e.Handled = true;
                break;
            case Key.Down when history.Count > 0:
                historyIndex = Math.Min(historyIndex + 1, history.Count);
                input.Text = historyIndex < history.Count ? history[historyIndex] : "";
                input.CaretIndex = input.Text.Length;
                e.Handled = true;
                break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control && input.SelectionLength == 0:
                Interrupt();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Ctrl+C: stops what PowerShell is running by ending its child processes, not the shell itself.</summary>
    private void Interrupt()
    {
        if (shell is not { HasExited: false }) return;
        try
        {
            var kill = new ProcessStartInfo("taskkill", $"/F /T /FI \"PID ne {shell.Id}\" /FI \"PARENTPID eq {shell.Id}\"") { CreateNoWindow = true, UseShellExecute = false };
            Process.Start(kill);
            Append("^C\n");
        }
        catch (Exception) { }
    }

    public void Stop()
    {
        try { shell?.Kill(entireProcessTree: true); } catch (Exception) { }
    }
}

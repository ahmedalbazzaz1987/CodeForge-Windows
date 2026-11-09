using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using CodeForge.Core;

namespace CodeForge;

/// <summary>
/// Checks GitHub Releases for a newer CodeForge, downloads the zip, checks its SHA-256, and replaces the app's folder
/// after CodeForge quits (a small PowerShell script waits, copies the new files over, and starts CodeForge again).
/// </summary>
public static class Updater
{
    public const string Repository = "ahmedalbazzaz1987/CodeForge-Windows";
    public static string ProjectPage => $"https://github.com/{Repository}";

    public static AppVersion CurrentVersion { get; } =
        AppVersion.Parse(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.0.0")
        ?? new AppVersion([0, 0, 0]);

    private static readonly HttpClient Http = new() { DefaultRequestHeaders = { { "User-Agent", "CodeForge-Windows" } }, Timeout = TimeSpan.FromMinutes(5) };

    public static async Task CheckAsync(Window owner, bool userInitiated)
    {
        Settings.Current.LastUpdateCheck = DateTime.UtcNow;
        Settings.Current.Save();
        ReleaseInfo? release;
        try
        {
            var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest");
            release = ReleaseInfo.Parse(json);
        }
        catch (Exception error)
        {
            if (userInitiated) MessageBox.Show(owner, $"Couldn't check for updates: {error.Message}", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (release == null || !(release.Version > CurrentVersion))
        {
            if (userInitiated) MessageBox.Show(owner, $"Kryndev {CurrentVersion} is the latest version.", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var notes = ReleaseNotes.Unwrapped(release.Notes).Replace("**", "").Replace("`", "");
        if (notes.Length > 1200) notes = notes[..1200] + "…";
        var answer = MessageBox.Show(owner, $"Kryndev {release.Version} is available (you have {CurrentVersion}).\n\n{notes}\n\nInstall it now? Kryndev will restart.",
                                     "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Yes) await InstallAsync(owner, release);
    }

    /// <summary>Installed with the setup program (it leaves its uninstaller next to CodeForge.exe), rather than unzipped.</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    private static async Task<string> DownloadVerified(string url, string checksumUrl, string file)
    {
        await File.WriteAllBytesAsync(file, await Http.GetByteArrayAsync(url));
        var expected = ReleaseInfo.ChecksumValue(await Http.GetStringAsync(checksumUrl))
            ?? throw new InvalidDataException("The checksum file is damaged.");
        await using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
        if (actual != expected) throw new InvalidDataException("The download doesn't match its checksum, so it wasn't installed.");
        return file;
    }

    /// <summary>
    /// Installed copies update by running the new setup quietly: it closes CodeForge, replaces it (keeping the user's
    /// choices, like shortcuts), and a small script starts CodeForge again afterwards.
    /// </summary>
    private static async Task RunSetupAsync(Window owner, ReleaseInfo release)
    {
        try
        {
            var work = Path.Combine(Path.GetTempPath(), "CodeForge-update-" + release.Version);
            Directory.CreateDirectory(work);
            var setup = await DownloadVerified(release.SetupUrl!, release.SetupChecksumUrl!, Path.Combine(work, $"Kryndev-{release.Version}-setup.exe"));
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            // Installed for all users (in Program Files): the setup has to ask for administrator rights again.
            var scope = AppContext.BaseDirectory.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) ? "/ALLUSERS" : "/CURRENTUSER";
            var exe = Path.Combine(AppContext.BaseDirectory, "CodeForge.exe");
            var script = Path.Combine(work, "update.ps1");
            File.WriteAllText(script, $"""
                Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue
                Start-Process -FilePath '{setup.Replace("'", "''")}' -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','{scope}' -Wait
                Start-Process -FilePath '{exe.Replace("'", "''")}'
                """);
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"") { UseShellExecute = false, CreateNoWindow = true });
            Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            MessageBox.Show(owner, $"The update couldn't be installed: {error.Message}\n\nYou can download it from {release.PageUrl}", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static async Task InstallAsync(Window owner, ReleaseInfo release)
    {
        if (IsInstalled && release.SetupUrl != null && release.SetupChecksumUrl != null)
        {
            await RunSetupAsync(owner, release);
            return;
        }
        try
        {
            var work = Path.Combine(Path.GetTempPath(), "CodeForge-update-" + release.Version);
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            var zip = await DownloadVerified(release.ZipUrl, release.ChecksumUrl, Path.Combine(work, "CodeForge.zip"));
            var unpacked = Path.Combine(work, "files");
            ZipFile.ExtractToDirectory(zip, unpacked);
            // The zip holds a CodeForge folder; copy its contents over this installation.
            var source = Directory.Exists(Path.Combine(unpacked, "CodeForge")) ? Path.Combine(unpacked, "CodeForge") : unpacked;
            if (!File.Exists(Path.Combine(source, "CodeForge.exe"))) throw new InvalidDataException("The download doesn't contain CodeForge.exe.");
            var target = AppContext.BaseDirectory.TrimEnd('\\');
            var script = Path.Combine(work, "update.ps1");
            File.WriteAllText(script, $"""
                $ErrorActionPreference = 'Stop'
                Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 300
                Copy-Item -Path '{source.Replace("'", "''")}\*' -Destination '{target.Replace("'", "''")}' -Recurse -Force
                Start-Process -FilePath '{Path.Combine(target, "CodeForge.exe").Replace("'", "''")}'
                """);
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"") { UseShellExecute = false, CreateNoWindow = true });
            Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            MessageBox.Show(owner, $"The update couldn't be installed: {error.Message}\n\nYou can download it from {release.PageUrl}", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

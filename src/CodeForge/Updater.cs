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
            if (userInitiated) MessageBox.Show(owner, $"CodeForge {CurrentVersion} is the latest version.", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var notes = release.Notes.Length > 1200 ? release.Notes[..1200] + "…" : release.Notes;
        var answer = MessageBox.Show(owner, $"CodeForge {release.Version} is available (you have {CurrentVersion}).\n\n{notes}\n\nInstall it now? CodeForge will restart.",
                                     "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Yes) await InstallAsync(owner, release);
    }

    private static async Task InstallAsync(Window owner, ReleaseInfo release)
    {
        try
        {
            var work = Path.Combine(Path.GetTempPath(), "CodeForge-update-" + release.Version);
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            var zip = Path.Combine(work, "CodeForge.zip");
            await File.WriteAllBytesAsync(zip, await Http.GetByteArrayAsync(release.ZipUrl));
            var expected = ReleaseInfo.ChecksumValue(await Http.GetStringAsync(release.ChecksumUrl))
                ?? throw new InvalidDataException("The checksum file is damaged.");
            await using (var stream = File.OpenRead(zip))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
                if (actual != expected) throw new InvalidDataException("The download doesn't match its checksum, so it wasn't installed.");
            }
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

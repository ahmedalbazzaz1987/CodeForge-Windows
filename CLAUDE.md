# Kryndev for Windows (formerly CodeForge) — notes for Claude

The product is called **Kryndev** (renamed from CodeForge in 0.10.0). Only user-visible text says Kryndev: the
repository, projects, namespaces, `CodeForge.exe`, the installer's AppId, registry keys and the settings folders
(`%APPDATA%\CodeForge`, `%LOCALAPPDATA%\CodeForge`) keep "CodeForge" so installed copies keep updating and keep their
settings. Don't rename those. Release assets are `Kryndev-x.y.z-setup.exe` / `-windows.zip` (the updater matches the
suffix, so older copies find them).

The Windows version of CodeForge (the Mac version: github.com/ahmedalbazzaz1987/CodeForge, Swift/AppKit). The owner
wants it to feel like Visual Studio Professional and keeps asking for features; `CHANGELOG.md` has the history.

- C# / WPF on .NET 10. `src/CodeForge.Core` is the engine (no UI, unit-tested in `tests/`, builds on any OS);
  `src/CodeForge` is the WPF app. The editor is AvalonEdit; controls use WPF's Fluent theme (`ThemeMode`), and
  CodeForge's own surfaces use the brushes in `Themes.cs` through DynamicResource.
- Put logic that doesn't need UI in Core with a test. Code reads like the surrounding code.
- The app builds on macOS (`EnableWindowsTargeting`) but runs only on Windows. GitHub Actions
  (`.github/workflows/windows.yml`) builds, tests and runs `scripts/screenshots.ps1`, which drives the app through
  `Snapshot.cs` (`CF_SNAPSHOT`, `CF_OPEN`, `CF_ACTIONS`, `CF_DELAY`, `CF_SETTINGS_DIR`, `CF_FRESH`) and uploads PNGs.
- Release: bump `VERSION`, add a CHANGELOG section, commit, tag `v<version>` and push the tag; CI publishes the zip and
  checksum that `Updater.cs` installs.
- Windows-only features planned beyond the Mac version: a Windows Forms designer (with PropertyGrid), a WPF/XAML live
  preview, SQL Server LocalDB in Server Explorer. .NET debugging uses netcoredbg (Microsoft's vsdbg is licensed only for
  Microsoft's own tools, on Windows too).

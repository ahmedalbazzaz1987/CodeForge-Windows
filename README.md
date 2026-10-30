# CodeForge for Windows

A small, self-updating, Visual Studio-style IDE for Windows, written in C# with WPF. It's the Windows version of
[CodeForge for Mac](https://github.com/ahmedalbazzaz1987/CodeForge).

- **Visual Studio layout**: Toolbox, Solution Explorer, editor tabs, Output / Error List / Terminal, menus in Visual
  Studio's order, a Debug / Release box and a colored status bar. Light, Dark and Blue themes.
- **Windows Forms designer**: design forms by dragging controls from the Toolbox, set their properties in the
  Properties window, and double-click a control to write its event handler, like Visual Studio.
- **Windows desktop projects**: Windows Forms and WPF apps in VB.NET and C#, which only run on Windows, plus console
  apps, class libraries, ASP.NET Core, Blazor, gRPC, Worker services, .NET MAUI and test projects.
- **Run and fix**: F5 runs the project or the current file; build errors from VB.NET, C#, gcc, Python and Node.js go to
  the Error List and are underlined in the code.
- **Many languages**: C#, VB.NET, F#, XAML, Python, JavaScript, TypeScript, C, C++, Java, Kotlin, Go, Rust, Swift,
  Ruby, PHP, Lua, PowerShell, Batch, Shell, SQL, HTML, XML, CSS, JSON, YAML, Markdown and more.
- **PowerShell terminal** in the project folder, and **self-updating** from GitHub Releases.

## Install

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) (it includes the Desktop Runtime CodeForge needs,
   and builds your .NET projects): `winget install Microsoft.DotNet.SDK.10`
2. Download **`CodeForge-<version>-setup.exe`** from the [latest release](https://github.com/ahmedalbazzaz1987/CodeForge-Windows/releases/latest)
   and run it. It installs for your user account (no administrator rights needed), adds CodeForge to the Start menu,
   can add a desktop shortcut and an **Open with CodeForge** item when you right-click a folder, and appears in
   **Settings ▸ Apps** so you can uninstall it like any other program.
3. Windows SmartScreen may warn the first time because the installer isn't signed yet: click **More info ▸ Run anyway**.

CodeForge updates itself: **Help ▸ Check for Updates** (or the daily check) downloads the new setup, checks its SHA-256
and installs it quietly, then starts CodeForge again.

A portable version (`CodeForge-<version>-windows.zip`, no installation) is in each release too.

## Shortcuts

| Action | Shortcut |
| --- | --- |
| New Project / New File | Ctrl+Shift+N / Ctrl+N |
| Open File / Open Folder | Ctrl+O / Ctrl+Shift+O |
| Save / Save All | Ctrl+S / Ctrl+Shift+S |
| Start / Stop | F5 (or Ctrl+F5) / Shift+F5 |
| Build | Ctrl+Shift+B |
| Run All Tests | Ctrl+R |
| Find / Go To Line / Go To File | Ctrl+F / Ctrl+G / Ctrl+T |
| Toggle comment / Duplicate line | Ctrl+K / Ctrl+D |
| View Code / View Designer (forms) | F7 / Shift+F7 |
| Properties window | F4 |
| Solution Explorer / Toolbox / Output / Error List / Terminal | Ctrl+Alt+L / Ctrl+Alt+X / Ctrl+Alt+O / Ctrl+Alt+E / Ctrl+` |
| Next / previous document | Ctrl+Tab / Ctrl+Shift+Tab |

## Build from source

```powershell
git clone https://github.com/ahmedalbazzaz1987/CodeForge-Windows.git
cd CodeForge-Windows
dotnet build
dotnet run --project src/CodeForge
```

`dotnet test` runs the engine's unit tests (they also run on macOS and Linux). The app builds on any OS
(`EnableWindowsTargeting`) but runs only on Windows. `scripts/screenshots.ps1` opens the app in several situations and
saves screenshots; GitHub Actions runs it on every push.

## Releasing

Bump `VERSION`, add a `## <version>` section to `CHANGELOG.md`, commit, then tag and push (`git tag v0.2.0 && git push
origin v0.2.0`). The workflow builds, tests, zips the app with a SHA-256 checksum and creates the GitHub release that
installed copies update from.

## Layout

```
src/CodeForge.Core/   Engine: languages, error parsing, build detection, templates, Toolbox, updates (no UI; tested)
src/CodeForge/        The WPF app: main window, editor tabs, runner, terminal, dialogs, themes, updater
tests/                Unit tests for the engine
installer/            CodeForge.iss (Inno Setup: the setup program)
scripts/              screenshots.ps1
```

## License

MIT

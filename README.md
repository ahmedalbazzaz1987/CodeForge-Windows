# CodeForge for Windows

A small, self-updating, Visual Studio-style IDE for Windows, written in C# with WPF. It's the Windows version of
[CodeForge for Mac](https://github.com/ahmedalbazzaz1987/CodeForge).

- **Visual Studio layout**: Toolbox, Solution Explorer, editor tabs, Output / Error List / Task List / Terminal, menus in
  Visual Studio's order, a Debug / Release box and a colored status bar.
- **Windows Forms designer**: design forms by dragging controls from the Toolbox, set their properties in the
  Properties window, and double-click a control to write its event handler, like Visual Studio.
- **Windows desktop projects**: Windows Forms and WPF apps in VB.NET and C#, which only run on Windows, plus console
  apps, class libraries, ASP.NET Core, Blazor, gRPC, Worker services, .NET MAUI and test projects.
- **Find and navigate**: Find and Replace in Files, a Task List of TODO comments, bookmarks, Navigate Backward/Forward
  and Tab-expanding code snippets.
- **Modern themes**: Modern Light, Modern Dark, Gray and Midnight, plus Visual Studio's Light, Dark and Blue.
- **Debugger**: F5 debugs C#, VB.NET, F# (netcoredbg) and Python (debugpy) with breakpoints (F9 or a click in the margin),
  Step Over/Into/Out (F10/F11/Shift+F11), Locals, Call Stack and expression evaluation; Ctrl+F5 runs without debugging.
- **IntelliSense**: completion lists, Quick Info, Go To Definition (F12), Find All References (Shift+F12) and live
  errors from language servers (clangd, Pyright/Jedi, TypeScript, csharp-ls, FsAutoComplete, gopls, rust-analyzer,
  JDT LS, SourceKit-LSP), which CodeForge can install for you; keywords, snippets and words for the rest.
- **Git Changes and refactoring**: stage, commit, fetch, pull, push, branches, history and blame; Rename, Extract
  Method and Quick Actions (Ctrl+.).
- **Class View, Object Browser and Server Explorer**: browse the project's types and members; connect to SQLite,
  SQL Server (LocalDB), PostgreSQL and MySQL, see tables and run queries. A Code Snippets Manager for your own snippets.
- **VB.NET IntelliSense, CodeLens and Test Explorer**: member lists for .NET and your own classes without a language
  server; references and the last change above each member; every test's result.
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
| Find in Files / Replace in Files | Ctrl+Shift+F / Ctrl+Shift+H |
| Task List | Ctrl+Alt+K |
| Toggle / next / previous bookmark | Ctrl+F2 / F2 / Shift+F2 |
| Navigate Backward / Forward | Ctrl+- / Ctrl+Shift+- |
| Expand a code snippet | type its shortcut, then Tab |
| Zoom in / out | Ctrl+Shift+. / Ctrl+Shift+, |
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

MIT. Copyright © 2026 Ahmed Al-Bazzaz.

# Changelog

## 0.1.0

The first Windows version of CodeForge, a small Visual Studio-style IDE (the Mac version lives at
github.com/ahmedalbazzaz1987/CodeForge).

- Visual Studio layout: Toolbox on the left, Solution Explorer on the right, editor tabs, and an Output / Error List /
  Terminal panel, with menus in Visual Studio's order and a colored status bar.
- Code editor (AvalonEdit) with line numbers, syntax highlighting for 30 languages, Find (Ctrl+F), Go To Line (Ctrl+G),
  Go To File (Ctrl+T), toggle comment and duplicate line.
- New Project with Windows-only templates (Windows Forms and WPF apps in VB.NET and C#), .NET templates (console, class
  library, ASP.NET Core, Blazor, gRPC, Worker, MAUI, tests) and Python, Node.js and web pages.
- Start (F5) runs the project or the current file; Build (Ctrl+Shift+B), Rebuild, Clean, Run All Tests (Ctrl+R) and a
  Debug / Release box. Errors from MSBuild (VB.NET and C#), gcc, Python and Node.js go to the Error List and are
  underlined in the editor. ASP.NET sites open in your browser.
- Toolbox with WPF controls and layouts for XAML files, HTML, ASP.NET Tag Helpers, and VB.NET, C#, Python and
  JavaScript snippets.
- PowerShell terminal in the project folder.
- Light, Dark and Blue themes (or follow Windows), font size, and session restore in Tools ▸ Options.
- Updates itself from GitHub Releases (Help ▸ Check for Updates).

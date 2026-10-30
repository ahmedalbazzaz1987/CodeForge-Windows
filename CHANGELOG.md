# Changelog

## 0.2.1

- **CodeForge now has an installer** (`CodeForge-0.2.1-setup.exe`): a setup wizard that installs for your user account
  without administrator rights (or for all users, if you choose), adds Start menu and optional desktop shortcuts, an
  optional **Open with CodeForge** item when you right-click a folder, and optional "Open with" for .vbproj, .csproj
  and .sln files. It checks that .NET 10 is installed, and CodeForge appears in Settings ▸ Apps to uninstall.
- Installed copies update by running the new setup quietly and restarting. The portable zip is still available.
- The Windows Forms designer saves PasswordChar and other single-character properties.

## 0.2.0

The Windows Forms designer, a feature only the Windows version has.

- **Design forms visually**: double-click a form (Form1.vb or Form1.cs) in Solution Explorer to open it in Design view.
  Controls are real Windows Forms controls, drawn exactly as they look when the program runs.
- **Toolbox** with Common Controls (Button, Label, TextBox, CheckBox, RadioButton, ComboBox, ListBox, CheckedListBox,
  DateTimePicker, MonthCalendar, NumericUpDown, PictureBox, ProgressBar, TrackBar, LinkLabel, MaskedTextBox,
  RichTextBox, DataGridView) and Containers (GroupBox, Panel, FlowLayoutPanel): click a control and then the form, drag
  it onto the form, or double-click it.
- **Move and resize** with the mouse (snapping to a 4-pixel grid) or the arrow keys (Shift+arrows resize); drop a control
  into a GroupBox or Panel to put it inside; resize the form from its edges; Delete, Bring to Front, Send to Back, and
  Undo/Redo (Ctrl+Z / Ctrl+Y).
- **Properties window** (under Solution Explorer, F4): Visual Studio's own property grid for the selected control or the
  form, including colors, fonts, anchors and docking. Renaming a control updates its VB.NET Handles clauses.
- **Double-click a control** to create its default event handler (Button → Click, TextBox → TextChanged, form → Load…)
  and jump to it in the code, as in Visual Studio.
- View Code (F7) and View Designer (Shift+F7). Saving writes Form1.Designer.vb / .cs in Visual Studio's format; existing
  forms made in Visual Studio open too, and code the designer doesn't understand is kept as it is.

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

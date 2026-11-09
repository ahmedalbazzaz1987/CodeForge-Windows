# Changelog

## 0.9.2

- Debugging sends only the breakpoints of files that still exist and that the debugger reads (GDB no longer gets
  Swift files), so Output has no "No source file named…" or "pending" lines left from other projects.

## 0.9.1

From testing 0.9.0 on a PC.

- **Debugging C and C++ in folders with Arabic (or other non-ASCII) names**: GDB couldn't open them; CodeForge now
  hands it the folder's short name and finds the files again by name.
- The debugger is only sent breakpoints for its own kind of file (GDB no longer lists the C# breakpoints of other
  projects as pending).
- A compiler or debugger installed while CodeForge is open (winget, MSYS2, `go install`) is found without restarting.
- The terminal stays at its last line when the color theme changes.

## 0.9.0

The rest of the Mac version's features.

- **Debugging C, C++, Rust, Go and Swift** (F5): CodeForge builds the program with debug information, then stops at
  breakpoints, steps and shows the locals and call stack. C and C++ use GDB (MSYS2: `pacman -S
  mingw-w64-ucrt-x86_64-gdb`), Rust and Swift LLVM's lldb-dap (`winget install LLVM.LLVM`), and Go Delve (`go install
  github.com/go-delve/delve/cmd/dlv@latest`). Without a debugger, F5 runs the program and says what to install.
- **HTML designer**: HTML pages open with Design, Split and Source views (the buttons under the page, or View ▸
  Designer and View ▸ Code). Clicking an element in the design view puts the caret in its tag.
- **Properties for HTML elements**: in HTML and Razor pages, the Properties window shows the attributes of the tag at
  the caret. Change a value, clear it to remove the attribute, or type `name=value` under (New attribute) to add one.
- **More color themes** in Tools ▸ Options: High Contrast, Monokai, Dracula, Solarized Dark and Solarized Light.
- **Toolbar**: View ▸ Show Toolbar (Ctrl+Alt+T) and View ▸ Customize Toolbar to choose its buttons.
- Highlighting for **Objective-C** and **Makefiles** (Start runs `make`).
- **Opening a project closes every tab**, as closing a solution in Visual Studio does, including files opened on their
  own; they used to stay open, with their errors, from project to project.
- Update messages show the release notes as flowing text.

## 0.8.1

Fixes from testing 0.8.0 on Windows.

- Typing quickly no longer lets `(`, `.` or `;` accept a completion that doesn't start with what you typed, or a
  snippet (typing `Sub Main(` could become another name or a whole template).
- Add New Item shows its Add and Cancel buttons (they were cut off).

## 0.8.0

Closes the remaining gap with the Mac version.

- **VB.NET IntelliSense** without a language server, from the Mac version's .NET catalog and the project's own
  classes: member lists after a dot (`Console.`, `names.` for a `List(Of String)`, `calc.` for your class — Shared
  members only on the class), namespaces after `Imports`, types after `As`, local variables, Quick Info and Go To
  Definition. C# gets the same when csharp-ls isn't installed.
- **CodeLens**: after each class, method and property, how many times it's used in the project and who changed it
  last (git blame). View ▸ Show CodeLens turns it off and on.
- **Add New Item** (Project menu) lists item templates by language: VB.NET and C# Class, Module, Interface, Record
  and Windows Form (with its .Designer file), Razor pages and components, web, Python, C/C++, F#, SQL and text files.
- **Test Explorer** (Test menu, and a tab beside Output): every test of the last Run All Tests, passed, failed or
  skipped, with its duration, for dotnet test, pytest, go test and cargo test. Double-click a test to go to it.
- **Colored output**: error lines are red and warnings amber in the Output window, as on the Mac.
- **Avalonia** project templates (desktop and MVVM), installing Avalonia's templates the first time.
- Escape closes the Object Browser.

## 0.7.0

**Class View, Object Browser, Server Explorer, more templates and a Code Snippets Manager**, like the Mac version.

- **Class View** (View ▸ Class View, Ctrl+Shift+C), a tab beside Solution Explorer: the project's namespaces, classes,
  modules, structures, interfaces and enums with their members, read from the source files of VB.NET, C#, F#, Python,
  JavaScript, TypeScript, Java, Kotlin, Go, Rust, C, C++, Swift and PHP. Search it; double-click to go to a member.
- **Object Browser** (Ctrl+Alt+J): every type, its members, and the selected one's declaration.
- **Server Explorer** (Ctrl+Alt+S): connect to a SQLite file (opened with the SQLite that comes with Windows, nothing to
  install), SQL Server including LocalDB (through sqlcmd), PostgreSQL or MySQL. See tables, views and columns;
  double-click a table for its rows in the new **Query Results** tab; Tools ▸ New Query and Execute Query
  (Ctrl+Shift+E) run SQL. Passwords are asked when you connect and never saved.
- **New project templates**: C, C++, Go, Rust, Java, TypeScript, Empty Project, NUnit Test Project and Razor Class
  Library.
- **Code Snippets Manager** (Tools, Ctrl+Alt+B): add, change and delete the snippets of each language; your changes
  are saved and used by Tab, the Toolbox and the completion list. Restore Built-in Snippets brings the originals back.
- Git ▸ Commit All without a message now puts the caret in the commit message box (it stayed in the editor).

## 0.6.1

Fixes from testing 0.6.0 on Windows.

- **First commit on a PC**: when Git doesn't know who's committing, CodeForge asks for your name and email (saved with
  `git config --global`) and commits, instead of showing Git's error.
- **Create Git Repository** adds a .gitignore for build output (bin, obj, node_modules, __pycache__…), as Visual Studio does.
- **TypeScript Quick Info** keeps the signature and the documentation on separate lines (CodeForge now asks servers for
  Markdown first).
- Typing **`g.`** quickly opens the member list (the dot used to be lost while the list for `g` was still opening).

## 0.6.0

**Git Changes and refactoring**, like Visual Studio's and the Mac version's.

- **Git Changes**, a tab beside Solution Explorer (Git ▸ Git Changes, Ctrl+Alt+F7, or click the branch in the status
  bar): staged and unstaged files with + and − to stage them, a commit message (Ctrl+Enter commits), Commit All or
  Commit Staged, Fetch, Pull and Push, and a branch list to switch branches or create one. Double-click a file to see
  its changes; right-click to open it or undo its changes. Folders that aren't repositories get Create Git Repository.
- A **Git menu**: Git Changes, Commit All, Fetch, Pull, Push, New Branch, View Branch History, Blame (Annotate), Create
  Git Repository and Clone Repository. The status bar shows the branch, commits to push and pull, and changed files.
- **Rename** (Ctrl+Shift+R): the language server renames a symbol everywhere it's used; without one, every whole-word
  use in the project's files of that language, outside strings and comments. Changed files stay open, unsaved.
- **Extract Method** (Ctrl+Shift+M) for VB.NET, C#, Python, JavaScript, TypeScript and Swift: variables from before the
  selection become parameters, and one set in it and used after becomes the return value.
- **Quick Actions and Refactorings** (Ctrl+.): the language server's fixes for the line (add a using, fix a typo…),
  then Rename and Extract Method.

## 0.5.2

- **Newly installed tools are found right away.** CodeForge reads the saved PATH when it starts and before installing a
  language server, so Node.js, Go or LLVM installed with winget work in IntelliSense, Run and the Terminal without
  signing out of Windows first.

## 0.5.1

Fixes from testing IntelliSense on a PC whose Documents folder has an Arabic name.

- **Python errors as you type** with Jedi in folders whose names have Arabic letters or spaces (Jedi never reported them).
- **F# IntelliSense** loads the project by itself (FsAutoComplete waited to be told which one).
- **Quick Info** no longer swallows the next click in the editor, and closes when you type or click.
- **Go To Definition** and Error List jumps put the caret on the name, not at the start of its line.

## 0.5.0

**IntelliSense**, like Visual Studio's and the Mac version's, from language servers. By Ahmed Al-Bazzaz.

- **Completion lists** open as you type a name or a `.`, and with Ctrl+Space or Ctrl+J. Tab, Enter, `.` or `(` accept the
  selected name; snippets are in the list too.
- **Quick Info**: hover over a name (or press Ctrl+Shift+I) to see its type and documentation, with the error there if
  there is one.
- **Go To Definition** (F12, or Ctrl+Click) and **Find All References** (Shift+F12, into Find Results).
- **Live errors**: red and yellow squiggles and Error List entries as you type, before you build.
- The servers: clangd (C, C++), Pyright or Jedi (Python), TypeScript 7 (JavaScript, TypeScript), csharp-ls (C#),
  FsAutoComplete (F#), gopls (Go), rust-analyzer (Rust), Eclipse JDT LS (Java) and SourceKit-LSP (Swift). CodeForge
  installs Pyright, Jedi, TypeScript, csharp-ls and FsAutoComplete into its own folder when you choose Edit ▸
  IntelliSense ▸ Install Language Server (it asks first); the others are found where their installers put them.
- Without a server (VB.NET, and languages whose server isn't installed), completion offers the language's keywords,
  snippets and the file's words, F12 finds declarations in the project, and Shift+F12 lists every use outside strings
  and comments. The status bar shows which server a file uses.
- The debugger says "[Program finished]" when a program ends without an exit code, and shows the Output at the end.

## 0.4.0

**A debugger**, like Visual Studio's, for C#, VB.NET, F# and Python. By Ahmed Al-Bazzaz.

- **F5 starts debugging** (Ctrl+F5 still runs without it). .NET programs (console, Windows Forms, WPF, ASP.NET Core) are
  built in Debug and run under netcoredbg, Samsung's open-source .NET debugger, which CodeForge downloads the first time
  (with your OK). Python files run under debugpy, installed with pip into CodeForge's own folder the first time.
- **Breakpoints**: click in the margin left of the line numbers, or press F9. They stay on their line as you edit, are
  remembered between sessions, and can be set while the program runs. Debug ▸ Delete All Breakpoints (Ctrl+Shift+F9).
- **Stepping**: Continue (F5), Step Over (F10), Step Into (F11), Step Out (Shift+F11), Break All and Stop (Shift+F5), from
  the Debug menu or the debug toolbar above the editor. The current line is marked with a yellow arrow and band.
- **The Debug tab**: Locals (expand objects, lists and arrays), the Call Stack (click a frame to see its code and
  variables), and an expression box that evaluates any expression where the program is paused.
- Test projects still run their tests with F5, and MAUI apps run without debugging.

## 0.3.3

Two more fixes found while re-testing 0.3.2 on a PC whose Documents folder has an Arabic name.

- **F# errors open at their line.** The F# compiler wrote file names in the old code page ("?????"), so the Error List
  couldn't find the file. It now reports in UTF-8, like C#.
- **HTML pages open in the default web browser** even on a PC where no program is set for .html files (Windows showed
  its "Open with" box instead).

## 0.3.2

Fixes from testing every language on Windows, mostly for Arabic and other non-English text. By Ahmed Al-Bazzaz.

- **Arabic folder names work everywhere.** Programs, builds and the Terminal now run with the UTF-8 code page, so a
  project under a folder such as "المستندات" builds and runs, the Terminal follows the project into it, and file names in
  build errors are no longer "?????" (so double-clicking an F# or MSBuild error opens the right line again).
- **Typing Arabic into a running program works**: input sent from the Output window arrives as UTF-8, not "????".
- **Output and errors no longer cut into each other's lines.** stdout and stderr are shown a whole line at a time; a
  prompt without a line break ("Name: ") still appears after a moment.
- **Terminal**: shows Arabic output correctly, and Ctrl+C stops the running command, not the shell.
- **A crashing program ends at once** instead of waiting half a minute for Windows Error Reporting, and its exit code is
  explained: `-532462766 (0xE0434352: an unhandled .NET exception)`.
- **.NET exceptions and failed tests go to the Error List**, at the first line of your code in the stack trace.
- **Color codes are removed** from program output instead of showing as `[31m`.
- **Undo works a word at a time** while typing, like Visual Studio, instead of one character at a time.
- **HTML pages open in your browser** directly (Start no longer shows Windows' "Open with" box).
- **Toolbox**: inserting two HTML items in a row puts the second after the first instead of inside it.
- **Properties window** shows the current file's name, path, language, size, date, lines and encoding when no form
  designer is open, and updates when you save.
- **Double-clicking a control in the form designer** switches to the code at its event handler.
- **Menus show real keys** (Ctrl+\`, Ctrl+-, Ctrl+Shift+.) instead of "OemTilde" and "OemMinus".
- **Error List**: the File and Line columns fit without scrolling sideways.
- **F# snippets** in the Toolbox and with Tab (`let`, `rec`, `type`, `union`, `match`, `for`, `pf`, `async`, `try`).

## 0.3.1

The fixes from testing the Mac version (0.11.1 to 0.12.0), wherever the Windows version has the same feature.

- **Stop says "Stopped"** instead of reporting a failure.
- **Progress that redraws its line** (a carriage return, like git's percentages) shows only its last version.
- **Missing tools are named before running**: if Go, Rust, Java, Node.js, Python, a C compiler or another tool isn't
  installed, Start and Build say what to install (with the winget command) instead of failing with "is not recognized".
- **Start on a test project runs its tests** (xUnit, NUnit, MSTest), and **Start on a class library** explains that a
  library can't be started.
- **.NET MAUI apps build and start as Windows apps**, so Build no longer needs the Android SDK or a Mac.
- **Opening another project closes the old one's tabs**, output, errors, Find Results and Task List, like closing a
  solution in Visual Studio. It asks about unsaved files first.
- Test runner lines such as `[xUnit.net 00:00:01.23]` no longer add false entries to the Error List.
- **Snippets and Toolbox groups for C, C++, Java, Go, Rust and TypeScript.**

## 0.3.0

A modern look, and more of Visual Studio's everyday tools. By Ahmed Al-Bazzaz.

- **New color themes** in Tools ▸ Options: **Modern Light** and **Modern Dark** (the new defaults, with calm neutrals, an
  indigo accent and softer syntax colors), **Gray** (a second dark option in neutral graphite with a teal accent) and
  **Midnight** (deep navy). Visual Studio's own Light, Dark and Blue are still there.
- **Find in Files and Replace in Files** (Ctrl+Shift+F, Ctrl+Shift+H): the whole project, the current folder or the open
  documents, with match case, whole word, regular expressions and file types. Results go to a new **Find Results** tab;
  replacing in open files can be undone with Ctrl+Z.
- **Task List** (Ctrl+Alt+K): every TODO, FIXME, HACK, UNDONE, BUG and XXX comment in the project.
- **Bookmarks** (Edit menu): Toggle (Ctrl+F2), Next (F2), Previous (Shift+F2) and Clear All. They stay on their code as you
  edit, and are remembered between sessions.
- **Navigate Backward and Forward** (Ctrl+- and Ctrl+Shift+-, with toolbar buttons), like Visual Studio. Zoom moved to
  Ctrl+Shift+. and Ctrl+Shift+, as in Visual Studio.
- **Code snippets**: type a shortcut (`prop`, `ctor`, `foreach`, `cw`, `Sub`, `For`, `Try`…) and press Tab. 75 snippets for
  VB.NET, C#, Python, JavaScript, Swift and HTML, also listed in the Toolbox.
- The app, the installer and the license now credit **Ahmed Al-Bazzaz**.

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

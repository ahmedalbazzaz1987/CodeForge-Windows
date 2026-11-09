using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodeForge.Core;

/// <summary>Converts between string offsets (UTF-16, like AvalonEdit's) and LSP positions (0-based line, UTF-16 character).</summary>
public sealed class TextLines
{
    private readonly List<int> starts = [0];
    public int Length { get; }

    public TextLines(string text)
    {
        for (var i = 0; i < text.Length; i++) if (text[i] == '\n') starts.Add(i + 1);
        Length = text.Length;
    }

    public (int Line, int Character) Position(int offset)
    {
        offset = Math.Clamp(offset, 0, Length);
        int low = 0, high = starts.Count - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (starts[mid] <= offset) low = mid; else high = mid - 1;
        }
        return (low, offset - starts[low]);
    }

    public int Offset(int line, int character)
    {
        if (line >= starts.Count) return Length;
        var start = starts[Math.Max(line, 0)];
        var end = line + 1 < starts.Count ? starts[line + 1] - 1 : Length;
        return Math.Min(start + Math.Max(character, 0), end);
    }
}

/// <summary>file:// URIs for language servers. Windows paths become <c>file:///C:/…</c>; servers may send the colon as %3A.</summary>
public static class LspUri
{
    /// <param name="plain">Leave the path unescaped (<c>file:///C:/My Files/ملف.py</c>), for servers that compare URIs
    /// without decoding them.</param>
    public static string FromPath(string path, bool plain = false)
    {
        var parts = path.Replace('\\', '/').Split('/');
        var encoded = parts.Select((part, i) => plain || i == 0 && Regex.IsMatch(part, "^[A-Za-z]:$") ? part : Uri.EscapeDataString(part));
        var joined = string.Join("/", encoded);
        return "file://" + (joined.StartsWith('/') ? "" : "/") + joined;
    }

    public static string? ToPath(string uri)
    {
        if (!uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return null;
        var path = Uri.UnescapeDataString(uri[7..]);
        if (path.StartsWith("localhost/", StringComparison.OrdinalIgnoreCase)) path = path[9..];
        // /C:/Users/… → C:\Users\…
        if (Regex.IsMatch(path, "^/[A-Za-z]:")) return char.ToUpperInvariant(path[1]) + path[2..].Replace('/', '\\');
        return path;
    }
}

/// <summary>How a language server is started.</summary>
public sealed record ServerLaunch(string Executable, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment,
    bool PlainUris = false, string? InitializationOptions = null);

/// <summary>A language server CodeForge knows how to start (and, for some, install).</summary>
/// <param name="Executables">Program names to look for, in order.</param>
/// <param name="InstalledCopyOnly">Only use the copy CodeForge installed (another copy may be too old).</param>
/// <param name="NpmPackages">Packages CodeForge can install with npm (needs Node.js).</param>
/// <param name="DotNetTool">A .NET tool CodeForge can install (<c>dotnet tool</c>).</param>
/// <param name="PipPackage">A Python package CodeForge can install with pip into its own folder; the server runs with
/// <see cref="PythonStart"/> under that Python.</param>
public sealed record LanguageServerSpec(
    string Id, string Name, string[] Languages, string[] Executables, string InstallHint,
    string[]? Arguments = null, bool InstalledCopyOnly = false,
    string[]? NpmPackages = null, string? DotNetTool = null, string? PipPackage = null, string? PythonStart = null,
    string? DownloadSize = null, bool PlainUris = false, string? InitializationOptions = null)
{
    public IReadOnlyList<string> Args => Arguments ?? [];
    public bool CanInstall => NpmPackages != null || DotNetTool != null || PipPackage != null;

    /// <summary>The program installing needs: npm, dotnet or python.</summary>
    public string? Installer => NpmPackages != null ? "npm" : DotNetTool != null ? "dotnet" : PipPackage != null ? "python" : null;
}

public static class LanguageServerSpecs
{
    private const string Llvm = "Install LLVM (it includes clangd): winget install LLVM.LLVM";

    public static readonly IReadOnlyList<LanguageServerSpec> All =
    [
        new("clangd", "clangd", ["c", "cpp"], ["clangd"], Llvm, Arguments: ["--background-index=false"]),
        new("sourcekit-lsp", "SourceKit-LSP", ["swift"], ["sourcekit-lsp"], "Install Swift for Windows from swift.org (it includes SourceKit-LSP)."),
        new("pyright", "Pyright", ["python"], ["pyright-langserver"], "Kryndev can install it for you (needs Node.js: winget install OpenJS.NodeJS.LTS).",
            Arguments: ["--stdio"], NpmPackages: ["pyright"], DownloadSize: "about 20 MB"),
        // Without Node.js, Python files get Jedi's server, installed with pip like the debugger.
        new("jedi", "Jedi", ["python"], [], "Kryndev can install it for you with pip.",
            PipPackage: "jedi-language-server", PythonStart: "import sys; from jedi_language_server.cli import cli; sys.exit(cli())",
            // Jedi stores files by their decoded URI but looks them up undecoded: escaped ones (Arabic, spaces) never get errors.
            DownloadSize: "about 10 MB", PlainUris: true),
        // TypeScript 7 has a built-in language server for JavaScript and TypeScript.
        new("typescript", "TypeScript", ["javascript", "typescript"], ["tsc"], "Kryndev can install it for you (needs Node.js: winget install OpenJS.NodeJS.LTS).",
            Arguments: ["--lsp", "--stdio"], InstalledCopyOnly: true, NpmPackages: ["typescript@7"], DownloadSize: "about 30 MB"),
        new("csharp-ls", "csharp-ls", ["csharp"], ["csharp-ls"], "Kryndev can install it for you (needs the .NET SDK).",
            DotNetTool: "csharp-ls", DownloadSize: "about 40 MB"),
        new("fsautocomplete", "FsAutoComplete", ["fsharp"], ["fsautocomplete"], "Kryndev can install it for you (needs the .NET SDK).",
            DotNetTool: "fsautocomplete", DownloadSize: "about 60 MB",
            // Otherwise it waits for the client to pick a project (fsharp/workspaceLoad), as Ionide does.
            InitializationOptions: """{ "AutomaticWorkspaceInit": true }"""),
        new("gopls", "gopls", ["go"], ["gopls"], "Install it with: go install golang.org/x/tools/gopls@latest"),
        new("rust-analyzer", "rust-analyzer", ["rust"], ["rust-analyzer"], "Install it with: rustup component add rust-analyzer"),
        new("jdtls", "Eclipse JDT LS", ["java"], ["jdtls"], "Install Eclipse JDT LS from github.com/eclipse-jdtls/eclipse.jdt.ls and put jdtls on PATH."),
    ];

    /// <summary>The servers for a language, best first.</summary>
    public static IEnumerable<LanguageServerSpec> For(string languageId) => All.Where(s => s.Languages.Contains(languageId));

    /// <summary>The LSP <c>languageId</c> for a file.</summary>
    public static string LanguageId(string languageId, string path) =>
        (languageId, Path.GetExtension(path).TrimStart('.').ToLowerInvariant()) switch
        {
            ("typescript", "tsx") => "typescriptreact",
            ("javascript", "jsx") => "javascriptreact",
            _ => languageId,
        };
}

/// <summary>Finds installed language servers and says how to start and install them.</summary>
/// <param name="installFolder">CodeForge's own folder for servers (<c>%LOCALAPPDATA%\CodeForge\LanguageServers</c>).</param>
public sealed class LanguageServerLocator(string installFolder, string? path = null, string? home = null, string? localAppData = null)
{
    public string InstallFolder => installFolder;
    private string Home => home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string LocalAppData => localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public string DotNetToolsFolder => Path.Combine(installFolder, "dotnet-tools");
    public string NodeModulesBin => Path.Combine(installFolder, "node_modules", ".bin");
    public string PipFolder(LanguageServerSpec spec) => Path.Combine(installFolder, spec.Id);

    /// <summary>Where servers usually live besides PATH: CodeForge's folder, .NET tools, Go, Cargo, LLVM and Swift.</summary>
    public IEnumerable<string> SearchFolders()
    {
        yield return DotNetToolsFolder;
        yield return NodeModulesBin;
        yield return Path.Combine(Home, ".dotnet", "tools");
        yield return Path.Combine(Home, "go", "bin");
        yield return Path.Combine(Home, ".cargo", "bin");
        yield return @"C:\Program Files\LLVM\bin";
        var toolchains = Path.Combine(LocalAppData, "Programs", "Swift", "Toolchains");
        if (Directory.Exists(toolchains))
            foreach (var toolchain in Directory.GetDirectories(toolchains).OrderDescending()) yield return Path.Combine(toolchain, "usr", "bin");
        foreach (var folder in (path ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return folder;
    }

    /// <summary>A real Python, not the Microsoft Store's stub that only opens the Store.</summary>
    public string? Python()
    {
        var python = ToolCheck.Find("python", path);
        return python == null || python.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) ? null : python;
    }

    /// <summary>How to start a server, or null if it isn't installed.</summary>
    public ServerLaunch? Launch(LanguageServerSpec spec)
    {
        var environment = new Dictionary<string, string>();
        if (spec.PythonStart != null)
        {
            var folder = PipFolder(spec);
            if (!Directory.Exists(Path.Combine(folder, spec.PipPackage!.Replace('-', '_'))) || Python() is not { } python) return null;
            environment["PYTHONPATH"] = folder;
            environment["PYTHONUTF8"] = "1";
            return new ServerLaunch(python, ["-c", spec.PythonStart], environment, spec.PlainUris, spec.InitializationOptions);
        }
        // .NET tools built for an older .NET still run on the SDK that's installed.
        if (spec.DotNetTool != null) environment["DOTNET_ROLL_FORWARD"] = "Major";
        var folders = spec.InstalledCopyOnly ? [NodeModulesBin] : SearchFolders().ToArray();
        foreach (var name in spec.Executables)
        {
            if (FindProgram(name, folders) is { } executable) return new ServerLaunch(executable, spec.Args, environment, spec.PlainUris, spec.InitializationOptions);
        }
        return null;
    }

    /// <summary>
    /// A program in one of the folders. On Windows only .exe, .cmd and .bat count: npm puts a shell script without an
    /// extension next to each .cmd, and Windows can't start it.
    /// </summary>
    private static string? FindProgram(string name, IEnumerable<string> folders)
    {
        string[] extensions = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat"] : ["", ".exe"];
        foreach (var folder in folders)
        {
            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(folder.Trim('"'), name + extension);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
        }
        return null;
    }

    /// <summary>The first installed server for a language.</summary>
    public (LanguageServerSpec Spec, ServerLaunch Launch)? Find(string languageId)
    {
        foreach (var spec in LanguageServerSpecs.For(languageId))
            if (Launch(spec) is { } launch) return (spec, launch);
        return null;
    }

    /// <summary>The server CodeForge would install for a language: the first whose installer (npm, dotnet, python) is here.</summary>
    public LanguageServerSpec? Installable(string languageId) =>
        LanguageServerSpecs.For(languageId).FirstOrDefault(s => s.Installer switch
        {
            "npm" => ToolCheck.Find("npm", path) != null,
            "dotnet" => ToolCheck.Find("dotnet", path) != null,
            "python" => Python() != null,
            _ => false,
        });

    /// <summary>The cmd.exe command that installs a server into CodeForge's folder.</summary>
    public string? InstallCommand(LanguageServerSpec spec)
    {
        var folder = CommandTemplate.Quote(installFolder);
        if (spec.DotNetTool != null)
            return $"dotnet tool update --tool-path {CommandTemplate.Quote(DotNetToolsFolder)} {spec.DotNetTool}";
        if (spec.NpmPackages != null)
            return $"(if not exist {folder} mkdir {folder}) && cd /d {folder} && npm install --no-fund --no-audit --loglevel=error {string.Join(" ", spec.NpmPackages)}";
        if (spec.PipPackage != null && Python() is { } python)
            return $"{CommandTemplate.Quote(python)} -m pip install --disable-pip-version-check --upgrade --target {CommandTemplate.Quote(PipFolder(spec))} {spec.PipPackage}";
        return null;
    }
}

/// <summary>Turns hover Markdown into plain text: code fences, emphasis markers and link targets are removed.</summary>
public static partial class HoverText
{
    [GeneratedRegex(@"\[([^\]\n]*)\]\([^)\n]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?<![*\w])\*(?=\S)([^*\n]+?)(?<=\S)\*(?![*\w])")]
    private static partial Regex Emphasis();

    public static string Plain(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n')
            .Where(line => !line.StartsWith("```"))
            .Select(line => line.Replace("**", "").Replace("`", ""));
        var text = string.Join("\n", lines).Trim();
        text = Link().Replace(text, "$1");
        text = Emphasis().Replace(text, "$1");
        text = text.Replace("&nbsp;", " ").Replace("\\_", "_");
        while (text.Contains("\n\n\n")) text = text.Replace("\n\n\n", "\n\n");
        return text;
    }
}

/// <summary>An entry in the completion list. <see cref="Kind"/> is LSP's CompletionItemKind.</summary>
/// <param name="ReplaceStart">Where the server wants the text replaced from, if it said.</param>
/// <param name="Snippet">A code snippet's body (<c>$0</c> marks the caret): accepting the item expands it.</param>
public sealed record CompletionEntry(string Label, string Detail, int Kind, string Text, string FilterText, string SortText,
    int? ReplaceStart = null, string? Snippet = null);

/// <summary>A place in a file: 1-based line and column.</summary>
public sealed record CodeLocation(string Path, int Line, int Column);

/// <summary>Reads language servers' answers.</summary>
public static partial class LspResults
{
    public static List<CompletionEntry> Completions(JsonNode? result, TextLines lines)
    {
        var items = result is JsonObject list ? list["items"] as JsonArray : result as JsonArray;
        var entries = new List<CompletionEntry>();
        foreach (var item in items?.OfType<JsonObject>().Take(3000) ?? [])
        {
            if (Text(item["label"]) is not { } label) continue;
            var text = Text(item["insertText"]) ?? label;
            int? replaceStart = null;
            if (item["textEdit"] is JsonObject edit)
            {
                text = Text(edit["newText"]) ?? text;
                var range = edit["range"] ?? edit["replace"] ?? edit["insert"];
                if (range?["start"] is JsonObject start && Int(start["line"]) is { } line && Int(start["character"]) is { } character)
                    replaceStart = lines.Offset(line, character);
            }
            if (Int(item["insertTextFormat"]) == 2) text = StripSnippet(text);
            label = label.Trim();
            entries.Add(new CompletionEntry(label, Text(item["detail"]) ?? Text(item["labelDetails"]?["description"]) ?? "", Int(item["kind"]) ?? 0,
                text, Text(item["filterText"]) ?? label, Text(item["sortText"]) ?? label, replaceStart));
        }
        return entries;
    }

    public static string? Hover(JsonNode? result)
    {
        var contents = result?["contents"];
        string? text = contents switch
        {
            JsonObject markup => Text(markup["value"]),
            JsonValue value => Text(value),
            JsonArray parts => string.Join("\n\n", parts.Select(p => p is JsonObject o ? Text(o["value"]) : Text(p)).Where(p => !string.IsNullOrEmpty(p))),
            _ => null,
        };
        if (text == null) return null;
        var plain = HoverText.Plain(text);
        return plain.Length == 0 ? null : plain;
    }

    /// <summary>Location, Location[] or LocationLink[] (definition, references).</summary>
    public static List<CodeLocation> Locations(JsonNode? result)
    {
        var list = result switch { JsonArray array => array.OfType<JsonObject>(), JsonObject one => [one], _ => [] };
        var locations = new List<CodeLocation>();
        foreach (var item in list)
        {
            if ((Text(item["uri"]) ?? Text(item["targetUri"])) is not { } uri || LspUri.ToPath(uri) is not { } path) continue;
            var start = (item["range"] ?? item["targetSelectionRange"])?["start"];
            if (Int(start?["line"]) is { } line && Int(start?["character"]) is { } character)
                locations.Add(new CodeLocation(path, line + 1, character + 1));
        }
        return locations;
    }

    /// <summary>Errors and warnings (information and hints are left out, as in Visual Studio's default Error List).</summary>
    public static List<Diagnostic> Diagnostics(JsonArray? items, string path)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var item in items?.OfType<JsonObject>() ?? [])
        {
            var severity = Int(item["severity"]) ?? 1;
            var start = item["range"]?["start"];
            var end = item["range"]?["end"];
            if (severity > 2 || Int(start?["line"]) is not { } line || Int(start?["character"]) is not { } character) continue;
            var message = (Text(item["message"]) ?? "").Replace("\r\n", "\n").Split('\n')[0].Trim();
            var code = item["code"] switch { JsonValue value => value.ToString(), _ => null };
            diagnostics.Add(new Diagnostic(severity == 1 ? Severity.Error : Severity.Warning, message, path, line + 1, character + 1, code,
                Int(end?["line"]) + 1, Int(end?["character"]) + 1));
        }
        return diagnostics;
    }

    /// <summary>A WorkspaceEdit's text edits by file, from <c>changes</c> or <c>documentChanges</c>.</summary>
    public static Dictionary<string, List<TextEdit>> WorkspaceEdits(JsonNode? edit)
    {
        var result = new Dictionary<string, List<TextEdit>>(StringComparer.OrdinalIgnoreCase);
        void Add(string? uri, JsonArray? edits)
        {
            if (uri == null || LspUri.ToPath(uri) is not { } path || edits == null) return;
            if (!result.TryGetValue(path, out var list)) result[path] = list = [];
            foreach (var item in edits.OfType<JsonObject>())
            {
                var range = item["range"];
                if (Int(range?["start"]?["line"]) is not { } startLine || Int(range?["start"]?["character"]) is not { } startCharacter ||
                    Int(range?["end"]?["line"]) is not { } endLine || Int(range?["end"]?["character"]) is not { } endCharacter) continue;
                list.Add(new TextEdit(startLine, startCharacter, endLine, endCharacter, Text(item["newText"]) ?? ""));
            }
        }
        if (edit?["changes"] is JsonObject changes)
            foreach (var (uri, edits) in changes) Add(uri, edits as JsonArray);
        foreach (var change in (edit?["documentChanges"] as JsonArray)?.OfType<JsonObject>() ?? [])
            Add(Text(change["textDocument"]?["uri"]), change["edits"] as JsonArray);
        return result;
    }

    /// <summary>Code actions and bare commands, preferred ones first.</summary>
    public static List<CodeAction> CodeActions(JsonNode? result) =>
        (result as JsonArray)?.OfType<JsonObject>()
            .Where(a => Text(a["title"]) is { Length: > 0 } && a["disabled"] == null)
            .Select(a => new CodeAction(Text(a["title"])!, Text(a["kind"]) ?? "", a["isPreferred"] is JsonValue p && p.TryGetValue<bool>(out var b) && b, a))
            .OrderByDescending(a => a.IsPreferred).ThenBy(a => a.Kind.StartsWith("quickfix") ? 0 : 1)
            .ToList() ?? [];

    [GeneratedRegex(@"\$\{\d+:([^}]*)\}|\$\{\d+\}|\$\d+")]
    private static partial Regex SnippetField();

    /// <summary>"print(${1:value})$0" → "print(value)"</summary>
    public static string StripSnippet(string text) => SnippetField().Replace(text, "$1");

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;
}

/// <summary>
/// A running language server, spoken to with JSON-RPC over stdin and stdout (framed like the debug adapter protocol).
/// Answers and notifications arrive through <c>post</c>, on the UI thread.
/// </summary>
public sealed class LanguageClient(Action<Action> post)
{
    /// <summary>(file path, errors and warnings) whenever the server publishes diagnostics.</summary>
    public event Action<string, List<Diagnostic>>? DiagnosticsPublished;
    public event Action? Exited;
    /// <summary>The server finished work it announced (indexing the project): answers it gave before may be better now.</summary>
    public event Action? Idle;
    /// <summary>The server asks for files to be changed (workspace/applyEdit); return whether they were.</summary>
    public Func<Dictionary<string, List<TextEdit>>, bool>? EditRequested;
    /// <summary>The diagnostics as the server sent them, by file: code actions are asked for with them.</summary>
    public Dictionary<string, JsonArray> RawDiagnostics { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> TriggerCharacters { get; private set; } = ["."];
    public bool PullsDiagnostics { get; private set; }
    public bool IsRunning { get; private set; }
    /// <summary>Send file URIs unescaped (see <see cref="LanguageServerSpec.PlainUris"/>).</summary>
    public bool PlainUris { get; private set; }
    public string Uri(string path) => LspUri.FromPath(path, PlainUris);
    public bool IsBusy => activeWork.Count > 0;

    private Process? process;
    private Stream? input;
    private readonly DapFraming framing = new();
    private readonly Dictionary<int, Action<JsonNode?>> pending = [];
    private readonly List<JsonObject> queued = [];
    private readonly List<Action> readyActions = [];
    private readonly HashSet<string> activeWork = [];
    private readonly object gate = new();
    private bool ready;
    private int nextId = 1;
    private int idleGeneration;

    public bool Start(ServerLaunch launch, string root)
    {
        var info = new ProcessStartInfo(launch.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root,
        };
        foreach (var argument in launch.Arguments) info.ArgumentList.Add(argument);
        foreach (var (key, value) in launch.Environment) info.Environment[key] = value;
        try
        {
            process = Process.Start(info);
        }
        catch (Exception)
        {
            return false;
        }
        if (process == null) return false;
        IsRunning = true;
        PlainUris = launch.PlainUris;
        input = process.StandardInput.BaseStream;
        var output = process.StandardOutput.BaseStream;
        Task.Run(async () =>
        {
            var chunk = new byte[65536];
            int count;
            try
            {
                while ((count = await output.ReadAsync(chunk)) > 0)
                {
                    List<string> messages;
                    lock (gate) messages = framing.Append(chunk.AsSpan(0, count));
                    foreach (var message in messages) post(() => Receive(message));
                }
            }
            catch (Exception) { }
            post(() =>
            {
                IsRunning = false;
                Exited?.Invoke();
            });
        });
        // The server's log: read so its pipe never fills, but don't show it.
        Task.Run(async () => { try { await process.StandardError.ReadToEndAsync(); } catch (Exception) { } });

        var rootUri = Uri(root);
        var capabilities = JsonNode.Parse("""
            {
              "general": { "positionEncodings": ["utf-16"] },
              "textDocument": {
                "synchronization": { "didSave": true, "dynamicRegistration": false },
                "completion": { "completionItem": { "snippetSupport": false, "documentationFormat": ["markdown", "plaintext"] } },
                "hover": { "contentFormat": ["markdown", "plaintext"] },
                "definition": { "linkSupport": true },
                "references": {},
                "rename": { "prepareSupport": false },
                "codeAction": {
                  "codeActionLiteralSupport": { "codeActionKind": { "valueSet": ["", "quickfix", "refactor", "refactor.extract", "refactor.inline", "refactor.rewrite", "source", "source.organizeImports"] } },
                  "resolveSupport": { "properties": ["edit"] }, "dataSupport": true
                },
                "publishDiagnostics": { "relatedInformation": false },
                "diagnostic": { "dynamicRegistration": false }
              },
              "window": { "workDoneProgress": true },
              "workspace": { "workspaceFolders": true, "configuration": true, "applyEdit": true, "workspaceEdit": { "documentChanges": true } }
            }
            """);
        var parameters = new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = rootUri,
            ["rootPath"] = root,
            ["workspaceFolders"] = new JsonArray(new JsonObject { ["uri"] = rootUri, ["name"] = Path.GetFileName(root.TrimEnd('\\', '/')) }),
            ["clientInfo"] = new JsonObject { ["name"] = "Kryndev" },
            ["capabilities"] = capabilities,
        };
        if (launch.InitializationOptions != null) parameters["initializationOptions"] = JsonNode.Parse(launch.InitializationOptions);
        var id = nextId++;
        pending[id] = result =>
        {
            var serverCapabilities = result?["capabilities"];
            if (serverCapabilities?["completionProvider"]?["triggerCharacters"] is JsonArray characters)
                TriggerCharacters = characters.Select(c => c?.ToString() ?? "").Where(c => c.Trim().Length > 0).ToHashSet();
            PullsDiagnostics = serverCapabilities?["diagnosticProvider"] != null;
            Write(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "initialized", ["params"] = new JsonObject() });
            ready = true;
            foreach (var message in queued) Write(message);
            queued.Clear();
            foreach (var action in readyActions) action();
            readyActions.Clear();
        };
        Write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = "initialize", ["params"] = parameters });
        return true;
    }

    /// <summary>Asks the server something; the completion gets the result (null for an error or no answer).</summary>
    public void Request(string method, JsonObject parameters, Action<JsonNode?> completion)
    {
        var id = nextId++;
        pending[id] = completion;
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
    }

    public void Notify(string method, JsonObject parameters) =>
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

    /// <summary>Runs once the server has answered <c>initialize</c> (right away if it has).</summary>
    public void WhenReady(Action action)
    {
        if (ready) action(); else readyActions.Add(action);
    }

    /// <summary>Asks a pull-diagnostics server for a file's problems; they arrive like published ones.</summary>
    public void PullDiagnostics(string path)
    {
        if (!PullsDiagnostics) return;
        Request("textDocument/diagnostic", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = Uri(path) } }, result =>
        {
            if (result?["items"] is JsonArray items)
            {
                RawDiagnostics[path] = (JsonArray)items.DeepClone();
                DiagnosticsPublished?.Invoke(path, LspResults.Diagnostics(items, path));
            }
        });
    }

    public void Shutdown()
    {
        if (!IsRunning) return;
        var running = process;
        Request("shutdown", new JsonObject(), _ =>
        {
            Notify("exit", new JsonObject());
            Task.Delay(1000).ContinueWith(_ => Kill(running));
        });
        // A server that never answers is stopped anyway.
        Task.Delay(3000).ContinueWith(_ => Kill(running));
    }

    private static void Kill(Process? process)
    {
        try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); } catch (Exception) { }
    }

    private void Send(JsonObject message)
    {
        if (ready) Write(message); else queued.Add(message);
    }

    private void Write(JsonObject message)
    {
        if (input == null) return;
        var bytes = DapFraming.Frame(message.ToJsonString());
        lock (gate)
        {
            try
            {
                input.Write(bytes);
                input.Flush();
            }
            catch (Exception) { }
        }
    }

    internal void Receive(string text)
    {
        JsonObject? message;
        try { message = JsonNode.Parse(text) as JsonObject; } catch (Exception) { return; }
        if (message == null) return;
        var method = message["method"]?.GetValue<string>();
        var id = message["id"];
        if (method == null && id is JsonValue value && value.TryGetValue<int>(out var number))
        {
            if (pending.Remove(number, out var completion)) completion(message["error"] != null ? null : message["result"]);
        }
        else if (method != null && id != null)
        {
            // A request from the server: answer it, or it may wait for ever.
            JsonNode? result = null;
            if (method == "workspace/configuration" && message["params"]?["items"] is JsonArray items)
                result = new JsonArray(items.Select(_ => (JsonNode?)null).ToArray());
            else if (method == "workspace/workspaceFolders")
                result = null;
            else if (method == "workspace/applyEdit")
                // A command the server ran (a code action's) wants files changed.
                result = new JsonObject { ["applied"] = EditRequested?.Invoke(LspResults.WorkspaceEdits(message["params"]?["edit"])) ?? false };
            Write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result });
        }
        else if (method == "textDocument/publishDiagnostics" && message["params"] is JsonObject parameters)
        {
            if (parameters["uri"]?.GetValue<string>() is { } uri && LspUri.ToPath(uri) is { } path)
            {
                RawDiagnostics[path] = parameters["diagnostics"] as JsonArray ?? [];
                DiagnosticsPublished?.Invoke(path, LspResults.Diagnostics(parameters["diagnostics"] as JsonArray, path));
            }
        }
        else if (method == "$/progress" && message["params"] is JsonObject progress)
        {
            var token = progress["token"]?.ToString();
            var kind = progress["value"]?["kind"]?.ToString();
            if (token == null) return;
            if (kind == "begin") { activeWork.Add(token); idleGeneration++; }
            if (kind != "end") return;
            activeWork.Remove(token);
            if (activeWork.Count > 0) return;
            // Servers often start the next stage right away, so wait for a pause.
            var generation = ++idleGeneration;
            Task.Delay(1000).ContinueWith(_ => post(() => { if (generation == idleGeneration && activeWork.Count == 0) Idle?.Invoke(); }));
        }
    }
}

/// <summary>One open file's copy in a language server: opened, changed (whole text), saved and closed.</summary>
public sealed class LanguageDocument
{
    public LanguageClient Client { get; }
    public string Path { get; }
    private readonly Func<string> text;
    private int version = 1;
    private string Uri => Client.Uri(Path);

    public LanguageDocument(LanguageClient client, string path, string languageId, Func<string> text)
    {
        Client = client;
        Path = path;
        this.text = text;
        client.Notify("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = Uri, ["languageId"] = LanguageServerSpecs.LanguageId(languageId, path), ["version"] = version, ["text"] = text(),
            },
        });
        client.WhenReady(() => client.PullDiagnostics(path));
    }

    public void Changed()
    {
        version++;
        Client.Notify("textDocument/didChange", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = Uri, ["version"] = version },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text() }),
        });
        Client.PullDiagnostics(Path);
    }

    public void Saved() => Client.Notify("textDocument/didSave", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = Uri } });

    public void Closed() => Client.Notify("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = Uri } });

    private JsonObject At(int offset)
    {
        var (line, character) = new TextLines(text()).Position(offset);
        return new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = Uri },
            ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
        };
    }

    public void Completion(int offset, Action<List<CompletionEntry>> handler)
    {
        var lines = new TextLines(text());
        Client.Request("textDocument/completion", At(offset), result => handler(LspResults.Completions(result, lines)));
    }

    public void Hover(int offset, Action<string?> handler) =>
        Client.Request("textDocument/hover", At(offset), result => handler(LspResults.Hover(result)));

    public void Definition(int offset, Action<List<CodeLocation>> handler) =>
        Client.Request("textDocument/definition", At(offset), result => handler(LspResults.Locations(result)));

    public void References(int offset, Action<List<CodeLocation>> handler)
    {
        var parameters = At(offset);
        parameters["context"] = new JsonObject { ["includeDeclaration"] = true };
        Client.Request("textDocument/references", parameters, result => handler(LspResults.Locations(result)));
    }

    /// <summary>The edits, by file, that rename the symbol at an offset (empty if the server can't).</summary>
    public void Rename(int offset, string newName, Action<Dictionary<string, List<TextEdit>>> handler)
    {
        var parameters = At(offset);
        parameters["newName"] = newName;
        Client.Request("textDocument/rename", parameters, result => handler(LspResults.WorkspaceEdits(result)));
    }

    /// <summary>Quick fixes and refactorings for a range, with the server's diagnostics that touch it.</summary>
    public void CodeActions(int start, int end, Action<List<CodeAction>> handler)
    {
        var lines = new TextLines(text());
        var (startLine, startCharacter) = lines.Position(start);
        var (endLine, endCharacter) = lines.Position(end);
        var touching = new JsonArray();
        foreach (var diagnostic in Client.RawDiagnostics.GetValueOrDefault(Path)?.OfType<JsonObject>() ?? [])
        {
            var from = diagnostic["range"]?["start"]?["line"]?.GetValue<int>() ?? -1;
            var to = diagnostic["range"]?["end"]?["line"]?.GetValue<int>() ?? -1;
            if (from <= endLine && to >= startLine) touching.Add(diagnostic.DeepClone());
        }
        var parameters = new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = Uri },
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = startLine, ["character"] = startCharacter },
                ["end"] = new JsonObject { ["line"] = endLine, ["character"] = endCharacter },
            },
            ["context"] = new JsonObject { ["diagnostics"] = touching },
        };
        Client.Request("textDocument/codeAction", parameters, result => handler(LspResults.CodeActions(result)));
    }

    /// <summary>
    /// Carries out a code action: its edit (asking the server to fill it in first if it left it out), then its command,
    /// whose edits arrive through <see cref="LanguageClient.EditRequested"/>.
    /// </summary>
    public void Run(CodeAction action, Func<Dictionary<string, List<TextEdit>>, bool> apply)
    {
        void Finish(JsonObject raw)
        {
            if (raw["edit"] != null) apply(LspResults.WorkspaceEdits(raw["edit"]));
            var command = raw["command"] is JsonObject inner ? inner : raw["command"] is JsonValue ? raw : null;
            if (command?["command"] is JsonValue name)
                Client.Request("workspace/executeCommand", new JsonObject
                {
                    ["command"] = name.DeepClone(), ["arguments"] = command["arguments"]?.DeepClone() ?? new JsonArray(),
                }, _ => { });
        }
        if (action.Raw["edit"] == null && action.Raw["command"] == null && action.Raw["data"] != null)
            Client.Request("codeAction/resolve", (JsonObject)action.Raw.DeepClone(), result => Finish(result as JsonObject ?? action.Raw));
        else
            Finish(action.Raw);
    }
}

/// <summary>A change to a file's text: 0-based LSP positions.</summary>
public sealed record TextEdit(int StartLine, int StartCharacter, int EndLine, int EndCharacter, string NewText)
{
    /// <summary>The text with the edits made, last first so earlier positions stay right.</summary>
    public static string Apply(string text, IEnumerable<TextEdit> edits)
    {
        var lines = new TextLines(text);
        var builder = new System.Text.StringBuilder(text);
        foreach (var (edit, start, end) in edits
                     .Select(e => (e, lines.Offset(e.StartLine, e.StartCharacter), lines.Offset(e.EndLine, e.EndCharacter)))
                     .OrderByDescending(e => e.Item2).ThenByDescending(e => e.Item3))
        {
            builder.Remove(start, Math.Max(end - start, 0));
            builder.Insert(start, edit.NewText);
        }
        return builder.ToString();
    }
}

/// <summary>A quick fix or refactoring a language server offers (Ctrl+.).</summary>
public sealed record CodeAction(string Title, string Kind, bool IsPreferred, JsonObject Raw);

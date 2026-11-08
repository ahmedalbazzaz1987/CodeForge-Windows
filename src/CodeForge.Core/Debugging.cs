using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodeForge.Core;

/// <summary>
/// The Debug Adapter Protocol's framing: each message is <c>Content-Length: n</c>, a blank line, then n bytes of JSON.
/// Bytes arrive in arbitrary pieces; <see cref="Append"/> returns the messages they complete.
/// </summary>
public sealed class DapFraming
{
    private byte[] buffer = [];

    public List<string> Append(ReadOnlySpan<byte> data)
    {
        var combined = new byte[buffer.Length + data.Length];
        buffer.CopyTo(combined, 0);
        data.CopyTo(combined.AsSpan(buffer.Length));
        buffer = combined;
        var messages = new List<string>();
        while (true)
        {
            var headerEnd = IndexOf(buffer, "\r\n\r\n"u8);
            if (headerEnd < 0) break;
            var header = Encoding.ASCII.GetString(buffer, 0, headerEnd);
            var match = Regex.Match(header, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                // Not a header (an adapter printing text): skip past it.
                buffer = buffer[(headerEnd + 4)..];
                continue;
            }
            var length = int.Parse(match.Groups[1].Value);
            var start = headerEnd + 4;
            if (buffer.Length < start + length) break;
            messages.Add(Encoding.UTF8.GetString(buffer, start, length));
            buffer = buffer[(start + length)..];
        }
        return messages;
    }

    public static byte[] Frame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        return [.. header, .. body];
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> pattern) => data.AsSpan().IndexOf(pattern);
}

/// <summary>Which debug adapter to start and how to launch the program with it.</summary>
public sealed record DebugConfiguration(string Adapter, string[] AdapterArguments, string AdapterId, JsonObject Launch)
{
    public Dictionary<string, string> Environment { get; init; } = [];
    /// <summary>Delve speaks the protocol over TCP: it prints "DAP server listening at: host:port" first.</summary>
    public bool UsesSocket { get; init; }
}

/// <summary>A frame of the call stack where the program is stopped.</summary>
public sealed record StackFrameInfo(int Id, string Name, string? File, int Line);

/// <summary>A variable in the Locals tree. <see cref="Reference"/> above 0 means it has children to ask for.</summary>
public sealed class DebugVariable(string name, string value, string type, int reference)
{
    public string Name { get; } = name;
    public string Value { get; } = value;
    public string Type { get; } = type;
    public int Reference { get; } = reference;
    public List<DebugVariable>? Children { get; set; }
}

public enum DebugState { Starting, Running, Paused, Ended }

/// <summary>Where a debugger message comes from, for coloring in the Output window.</summary>
public enum DebugOutputKind { Program, Error, Info }

/// <summary>
/// Talks to a debug adapter (netcoredbg for .NET, debugpy for Python) over its stdin and stdout. Callbacks run through
/// <c>post</c>, which the app sets to its UI thread's dispatcher.
/// </summary>
public sealed class DebugAdapter(Action<Action> post)
{
    public event Action<string, JsonObject>? Event;
    public event Action? Exited;

    private Process? process;
    private Stream? input;
    private readonly DapFraming framing = new();
    private readonly Dictionary<int, Action<JsonObject>> pending = [];
    private readonly object gate = new();
    private int sequence = 1;

    /// <summary>What the adapter printed on stderr (the last few KB), to explain why it ended before the program started.</summary>
    public string ErrorText { get { lock (gate) return errorText.ToString(); } }
    private readonly StringBuilder errorText = new();

    public bool Start(string executable, IEnumerable<string> arguments, IDictionary<string, string>? environment, bool socket = false)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (environment != null) foreach (var (key, value) in environment) info.Environment[key] = value;
        try
        {
            process = Process.Start(info);
        }
        catch (Exception)
        {
            return false;
        }
        if (process == null) return false;
        Task.Run(async () =>
        {
            var buffer = new char[4096];
            int count;
            try
            {
                while ((count = await process.StandardError.ReadAsync(buffer)) > 0)
                    lock (gate)
                    {
                        errorText.Append(buffer, 0, count);
                        if (errorText.Length > 8000) errorText.Remove(0, errorText.Length - 8000);
                    }
            }
            catch (Exception) { }
        });
        if (!socket)
        {
            input = process.StandardInput.BaseStream;
            Read(process.StandardOutput.BaseStream);
            return true;
        }
        // A socket adapter prints its address, then waits for the connection; requests made meanwhile are queued.
        Task.Run(async () =>
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    if (ListeningAddress(line) is not { } address) continue;
                    var client = new System.Net.Sockets.TcpClient();
                    await client.ConnectAsync(address.Host, address.Port);
                    var stream = client.GetStream();
                    List<byte[]> waiting;
                    lock (gate)
                    {
                        input = stream;
                        waiting = [.. queued];
                        queued.Clear();
                    }
                    foreach (var bytes in waiting) Send(bytes);
                    Read(stream);
                    // Keep reading stdout so the adapter never blocks on a full pipe.
                    _ = process.StandardOutput.ReadToEndAsync();
                    return;
                }
            }
            catch (Exception) { }
            post(() => Exited?.Invoke());
        });
        return true;
    }

    private readonly List<byte[]> queued = [];

    /// <summary>"DAP server listening at: 127.0.0.1:52123" (Delve) → the host and port.</summary>
    public static (string Host, int Port)? ListeningAddress(string line)
    {
        var match = Regex.Match(line, @"listening at:?\s*\[?([\w.:]+?)\]?:(\d+)\s*$", RegexOptions.IgnoreCase);
        return match.Success ? (match.Groups[1].Value, int.Parse(match.Groups[2].Value)) : null;
    }

    private void Read(Stream output)
    {
        Task.Run(async () =>
        {
            var chunk = new byte[16384];
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
            post(() => Exited?.Invoke());
        });
    }

    public void Request(string command, JsonObject? arguments = null, Action<JsonObject>? completion = null)
    {
        int seq;
        lock (gate)
        {
            seq = sequence++;
            if (completion != null) pending[seq] = completion;
        }
        Write(new JsonObject { ["seq"] = seq, ["type"] = "request", ["command"] = command, ["arguments"] = arguments ?? new JsonObject() });
    }

    public void Terminate()
    {
        try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); } catch (Exception) { }
    }

    private void Write(JsonObject message)
    {
        var bytes = DapFraming.Frame(message.ToJsonString());
        lock (gate)
        {
            if (input == null) { queued.Add(bytes); return; }
        }
        Send(bytes);
    }

    private void Send(byte[] bytes)
    {
        lock (gate)
        {
            try
            {
                input?.Write(bytes);
                input?.Flush();
            }
            catch (Exception) { }
        }
    }

    private void Receive(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject message) return;
        switch ((string?)message["type"])
        {
            case "response":
                Action<JsonObject>? completion = null;
                lock (gate)
                {
                    if (message["request_seq"]?.GetValue<int>() is { } seq && pending.Remove(seq, out var found)) completion = found;
                }
                completion?.Invoke(message);
                break;
            case "event":
                Event?.Invoke((string?)message["event"] ?? "", message["body"] as JsonObject ?? []);
                break;
            case "request":
                // Reverse requests (runInTerminal) aren't supported: the program's output comes through output events.
                int next;
                lock (gate) next = sequence++;
                Write(new JsonObject
                {
                    ["seq"] = next, ["type"] = "response", ["request_seq"] = message["seq"]?.GetValue<int>() ?? 0,
                    ["success"] = false, ["command"] = (string?)message["command"] ?? "", ["message"] = "not supported",
                });
                break;
        }
    }
}

/// <summary>One debugging run: launches the program under a debug adapter and tracks where it stopped.</summary>
public sealed class DebugSession
{
    private readonly DebugAdapter adapter;
    private readonly Action<Action> post;

    public DebugConfiguration Configuration { get; }
    public DebugState State { get; private set; } = DebugState.Starting;
    public int ThreadId { get; private set; }
    public IReadOnlyList<StackFrameInfo> Frames { get; private set; } = [];
    public string StopReason { get; private set; } = "";
    private bool exitReported;

    /// <summary>The breakpoints to send when the adapter is ready: file → lines (1-based).</summary>
    public Func<IReadOnlyDictionary<string, IReadOnlyCollection<int>>> Breakpoints { get; set; } = () => new Dictionary<string, IReadOnlyCollection<int>>();

    public event Action? StateChanged;
    public event Action<string, DebugOutputKind>? Output;
    public event Action? Stopped;

    public DebugSession(DebugConfiguration configuration, Action<Action>? post = null)
    {
        Configuration = configuration;
        this.post = post ?? (action => action());
        adapter = new DebugAdapter(this.post);
    }

    private void SetState(DebugState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke();
    }

    public bool Start()
    {
        adapter.Event += Handle;
        adapter.Exited += () =>
        {
            // Ended before the program ran: say why (an old GDB without DAP, a missing DLL…).
            if (State == DebugState.Starting)
            {
                var reason = adapter.ErrorText.Trim();
                Output?.Invoke($"The debugger ({Path.GetFileNameWithoutExtension(Configuration.Adapter)}) ended before the program started." +
                               (reason.Length > 0 ? "\n" + reason : "") + "\n", DebugOutputKind.Error);
            }
            SetState(DebugState.Ended);
        };
        if (!adapter.Start(Configuration.Adapter, Configuration.AdapterArguments, Configuration.Environment, Configuration.UsesSocket)) return false;
        adapter.Request("initialize", new JsonObject
        {
            ["clientID"] = "codeforge", ["clientName"] = "CodeForge", ["adapterID"] = Configuration.AdapterId,
            ["linesStartAt1"] = true, ["columnsStartAt1"] = true, ["pathFormat"] = "path",
            ["supportsVariableType"] = true, ["supportsRunInTerminalRequest"] = false,
        });
        adapter.Request("launch", (JsonObject)Configuration.Launch.DeepClone(), response =>
        {
            if (response["success"]?.GetValue<bool>() == false)
            {
                Output?.Invoke($"Couldn't start debugging: {(string?)response["message"] ?? "unknown error"}\n", DebugOutputKind.Error);
                Stop();
            }
        });
        return true;
    }

    private void Handle(string name, JsonObject body)
    {
        switch (name)
        {
            case "initialized":
                foreach (var (file, lines) in Breakpoints()) SendBreakpoints(file, lines);
                adapter.Request("configurationDone");
                SetState(DebugState.Running);
                break;
            case "stopped":
                ThreadId = body["threadId"]?.GetValue<int>() ?? ThreadId;
                StopReason = (string?)body["description"] ?? (string?)body["reason"] ?? "paused";
                LoadFrames();
                break;
            case "continued":
                Frames = [];
                SetState(DebugState.Running);
                break;
            case "output":
                if ((string?)body["output"] is not { } text) return;
                var category = (string?)body["category"] ?? "console";
                if (category == "telemetry") return;
                var isProgram = category is "stdout" or "stderr";
                // The debugger's own messages can lack a line ending; the program's next line would join on to them.
                if (!isProgram && !text.EndsWith('\n')) text += "\n";
                Output?.Invoke(text, category == "stderr" ? DebugOutputKind.Error : isProgram ? DebugOutputKind.Program : DebugOutputKind.Info);
                break;
            case "exited":
                exitReported = true;
                var code = body["exitCode"]?.GetValue<int>() ?? 0;
                Output?.Invoke($"\n[Program exited with code {ExitCodes.Describe(code)}]\n", code == 0 ? DebugOutputKind.Info : DebugOutputKind.Error);
                break;
            case "terminated":
                // netcoredbg on Windows can end without an "exited" event.
                if (!exitReported) Output?.Invoke("\n[Program finished]\n", DebugOutputKind.Info);
                exitReported = true;
                Stop();
                break;
        }
    }

    private void LoadFrames()
    {
        adapter.Request("stackTrace", new JsonObject { ["threadId"] = ThreadId, ["startFrame"] = 0, ["levels"] = 60 }, response =>
        {
            var list = response["body"]?["stackFrames"] as JsonArray ?? [];
            Frames = list.OfType<JsonObject>().Select(frame => new StackFrameInfo(
                frame["id"]?.GetValue<int>() ?? 0, (string?)frame["name"] ?? "?", (string?)frame["source"]?["path"],
                frame["line"]?.GetValue<int>() ?? 0)).ToList();
            SetState(DebugState.Paused);
            Stopped?.Invoke();
        });
    }

    public void SendBreakpoints(string file, IEnumerable<int> lines)
    {
        if (State == DebugState.Ended) return;
        adapter.Request("setBreakpoints", new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = file, ["name"] = Path.GetFileName(file) },
            ["breakpoints"] = new JsonArray(lines.Order().Select(line => (JsonNode)new JsonObject { ["line"] = line }).ToArray()),
        });
    }

    /// <summary>The variables in a frame's first scope (usually "Locals").</summary>
    public void Locals(int frame, Action<List<DebugVariable>> completion)
    {
        adapter.Request("scopes", new JsonObject { ["frameId"] = frame }, response =>
        {
            var reference = (response["body"]?["scopes"] as JsonArray)?.FirstOrDefault()?["variablesReference"]?.GetValue<int>() ?? 0;
            if (reference <= 0) { completion([]); return; }
            Variables(reference, completion);
        });
    }

    public void Variables(int reference, Action<List<DebugVariable>> completion)
    {
        adapter.Request("variables", new JsonObject { ["variablesReference"] = reference }, response =>
        {
            var list = response["body"]?["variables"] as JsonArray ?? [];
            completion(list.OfType<JsonObject>().Select(v => new DebugVariable(
                (string?)v["name"] ?? "", (string?)v["value"] ?? "", (string?)v["type"] ?? "", v["variablesReference"]?.GetValue<int>() ?? 0)).ToList());
        });
    }

    public void Evaluate(string expression, int? frame, Action<string> completion)
    {
        // "watch" evaluates the text as an expression and returns just the value.
        var arguments = new JsonObject { ["expression"] = expression, ["context"] = "watch" };
        if (frame is { } id) arguments["frameId"] = id;
        adapter.Request("evaluate", arguments, response =>
            completion(response["success"]?.GetValue<bool>() == false
                ? "error: " + ((string?)response["message"] ?? "can't evaluate")
                : (string?)response["body"]?["result"] ?? ""));
    }

    public void Resume() => Command("continue");
    public void StepOver() => Command("next");
    public void StepInto() => Command("stepIn");
    public void StepOut() => Command("stepOut");
    public void Pause() { if (State == DebugState.Running) adapter.Request("pause", new JsonObject { ["threadId"] = ThreadId }); }

    private void Command(string name)
    {
        if (State != DebugState.Paused) return;
        Frames = [];
        SetState(DebugState.Running);
        adapter.Request(name, new JsonObject { ["threadId"] = ThreadId });
    }

    public void Stop()
    {
        if (State == DebugState.Ended) return;
        adapter.Request("disconnect", new JsonObject { ["terminateDebuggee"] = true });
        Task.Delay(1000).ContinueWith(_ => adapter.Terminate());
        SetState(DebugState.Ended);
    }
}

/// <summary>Where the debuggers live and how to launch a program under each.</summary>
public static class DebugTools
{
    /// <summary>netcoredbg, Samsung's open-source (MIT) .NET debugger. Microsoft's vsdbg is licensed only for its own tools.</summary>
    public static string NetcoredbgDownloadUrl => OperatingSystem.IsWindows()
        ? "https://github.com/Samsung/netcoredbg/releases/latest/download/netcoredbg-win64.zip"
        : "https://github.com/Samsung/netcoredbg/releases/latest/download/netcoredbg-linux-amd64.tar.gz";

    /// <summary>The executable inside the unpacked archive (which holds a "netcoredbg" folder).</summary>
    public static string NetcoredbgPath(string installFolder) =>
        Path.Combine(installFolder, "netcoredbg", OperatingSystem.IsWindows() ? "netcoredbg.exe" : "netcoredbg");

    /// <summary>The project file (.csproj, .vbproj, .fsproj) at the top of a folder.</summary>
    public static string? DotNetProject(string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder).Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                                                    f.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                                                    f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            : null;

    /// <summary>The newest built program (its .dll) under bin\Debug of a .NET project, whatever the target framework.</summary>
    public static string? DotNetAssembly(string project, BuildConfiguration configuration = BuildConfiguration.Debug)
    {
        var text = File.Exists(project) ? File.ReadAllText(project) : "";
        var name = Regex.Match(text, @"<AssemblyName>\s*([^<]+?)\s*</AssemblyName>") is { Success: true } m ? m.Groups[1].Value : Path.GetFileNameWithoutExtension(project);
        var bin = Path.Combine(Path.GetDirectoryName(project)!, "bin", configuration.ToString());
        if (!Directory.Exists(bin)) return null;
        return Directory.GetDirectories(bin)
            .Select(framework => Path.Combine(framework, name + ".dll"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>A built .NET program under netcoredbg. ASP.NET apps get their launch profile's http address.</summary>
    public static DebugConfiguration DotNet(string netcoredbg, string dotnet, string assembly, string projectFolder)
    {
        var env = new JsonObject { ["DOTNET_ENVIRONMENT"] = "Development", ["ASPNETCORE_ENVIRONMENT"] = "Development" };
        if (LaunchUrl(projectFolder) is { } url) env["ASPNETCORE_URLS"] = url;
        var launch = new JsonObject
        {
            ["type"] = "coreclr", ["request"] = "launch", ["program"] = dotnet, ["args"] = new JsonArray(assembly),
            ["cwd"] = projectFolder, ["env"] = env, ["stopAtEntry"] = false, ["justMyCode"] = true, ["console"] = "internalConsole",
        };
        return new DebugConfiguration(netcoredbg, ["--interpreter=vscode"], "coreclr", launch);
    }

    /// <summary>The first http:// address in Properties\launchSettings.json (the HTTPS certificate may not be trusted).</summary>
    public static string? LaunchUrl(string projectFolder)
    {
        var file = Path.Combine(projectFolder, "Properties", "launchSettings.json");
        if (!File.Exists(file)) return null;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["profiles"] is not JsonObject profiles)
                return null;
            var urls = profiles.Select(p => (string?)p.Value?["applicationUrl"]).FirstOrDefault(u => u != null);
            return urls?.Split(';').FirstOrDefault(u => u.StartsWith("http://")) ?? urls?.Split(';').FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A Python script under debugpy, installed (with pip --target) into <paramref name="debugpyFolder"/>.</summary>
    public static DebugConfiguration Python(string python, string debugpyFolder, string file, string folder)
    {
        var launch = new JsonObject
        {
            ["type"] = "python", ["request"] = "launch", ["program"] = file, ["cwd"] = folder, ["python"] = python,
            ["console"] = "internalConsole", ["justMyCode"] = true, ["redirectOutput"] = true, ["stopOnEntry"] = false,
            ["env"] = new JsonObject { ["PYTHONUNBUFFERED"] = "1", ["PYTHONIOENCODING"] = "utf-8" },
        };
        return new DebugConfiguration(python, ["-m", "debugpy.adapter"], "debugpy", launch)
        {
            Environment = new() { ["PYTHONPATH"] = debugpyFolder, ["PYTHONIOENCODING"] = "utf-8" },
        };
    }

    public static bool DebugpyInstalled(string debugpyFolder) => File.Exists(Path.Combine(debugpyFolder, "debugpy", "__init__.py"));

    /// <summary>A compiled program (C, C++, Rust, Swift) under LLVM's lldb-dap, as on the Mac.</summary>
    public static DebugConfiguration Lldb(string lldbDap, string program, string folder) =>
        new(lldbDap, [], "lldb-dap", new JsonObject
        {
            ["type"] = "lldb-dap", ["request"] = "launch", ["program"] = program, ["args"] = new JsonArray(), ["cwd"] = folder,
            ["stopOnEntry"] = false,
        });

    /// <summary>A program built with GCC (MSYS2's MinGW) under GDB's own adapter (GDB 14 and newer: <c>gdb -i dap</c>).</summary>
    public static DebugConfiguration Gdb(string gdb, string program, string folder) =>
        new(gdb, ["-i", "dap", "-q"], "gdb", new JsonObject
        {
            ["type"] = "gdb", ["request"] = "launch", ["program"] = program, ["args"] = new JsonArray(), ["cwd"] = folder,
        });

    /// <summary>A Go program under Delve, which shows Go's variables. CodeForge builds the program; Delve runs it.</summary>
    public static DebugConfiguration Delve(string dlv, string program, string folder) =>
        new(dlv, ["dap", "--listen=127.0.0.1:0"], "go", new JsonObject
        {
            ["type"] = "go", ["request"] = "launch", ["mode"] = "exec", ["program"] = program, ["cwd"] = folder,
            ["stopOnEntry"] = false, ["outputMode"] = "remote",
        })
        { UsesSocket = true };
}

/// <summary>How to build a compiled program with debug information, and where the program ends up.</summary>
public sealed record NativeDebugBuild(string Command, string Program, string Folder, string LanguageId);

/// <summary>
/// Debugging compiled languages, as on the Mac: C and C++ (GDB, or lldb-dap), Rust and Swift (lldb-dap, or GDB) and Go
/// (Delve). The program is built without optimizations and with debug information first.
/// </summary>
public static class NativeDebugging
{
    public static readonly HashSet<string> Languages = ["c", "cpp", "rust", "go", "swift"];

    /// <summary>The debug build for a single file: the program goes to <paramref name="outputDirectory"/>.</summary>
    public static NativeDebugBuild? ForFile(string file, string languageId, string outputDirectory)
    {
        var program = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(file) + "-debug.exe");
        string Q(string value) => CommandTemplate.Quote(value);
        var command = languageId switch
        {
            "c" => $"gcc -g -O0 -Wall {Q(file)} -o {Q(program)}",
            "cpp" => $"g++ -g -O0 -std=c++20 -Wall {Q(file)} -o {Q(program)}",
            "rust" => $"rustc -g {Q(file)} -o {Q(program)}",
            "go" => $"go build -gcflags \"all=-N -l\" -o {Q(program)} {Q(file)}",
            "swift" => $"swiftc -g -Onone {Q(file)} -o {Q(program)}",
            _ => null,
        };
        return command == null ? null : new NativeDebugBuild(command, program, Path.GetDirectoryName(file)!, languageId);
    }

    /// <summary>The debug build for a Cargo project or a Go module; null for other projects.</summary>
    public static NativeDebugBuild? ForProject(string folder, string outputDirectory)
    {
        var cargo = Path.Combine(folder, "Cargo.toml");
        if (File.Exists(cargo))
        {
            var name = CargoPackageName(File.ReadAllText(cargo)) ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
            return new NativeDebugBuild("cargo build", Path.Combine(folder, "target", "debug", name + ".exe"), folder, "rust");
        }
        if (File.Exists(Path.Combine(folder, "go.mod")))
        {
            var program = Path.Combine(outputDirectory, Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) + "-debug.exe");
            return new NativeDebugBuild($"go build -gcflags \"all=-N -l\" -o {CommandTemplate.Quote(program)} .", program, folder, "go");
        }
        return null;
    }

    /// <summary>The <c>name</c> in Cargo.toml's [package] section.</summary>
    public static string? CargoPackageName(string toml)
    {
        var inPackage = false;
        foreach (var raw in toml.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inPackage = line == "[package]"; continue; }
            if (inPackage && Regex.Match(line, @"^name\s*=\s*""([^""]+)""") is { Success: true } match) return match.Groups[1].Value;
        }
        return null;
    }

    /// <summary>Which debugger to use, in order of preference; <paramref name="find"/> returns a program's path or null.</summary>
    public static DebugConfiguration? Configuration(NativeDebugBuild build, Func<string, string?> find)
    {
        var program = build.Program;
        var folder = build.Folder;
        string? Lldb() => find("lldb-dap");
        string? Gdb() => find("gdb");
        return build.LanguageId switch
        {
            "go" => find("dlv") is { } dlv ? DebugTools.Delve(dlv, program, folder) : Gdb() is { } gdb ? DebugTools.Gdb(gdb, program, folder) : null,
            "c" or "cpp" => Gdb() is { } gdb ? DebugTools.Gdb(gdb, program, folder) : Lldb() is { } lldb ? DebugTools.Lldb(lldb, program, folder) : null,
            _ => Lldb() is { } lldb ? DebugTools.Lldb(lldb, program, folder) : Gdb() is { } gdb ? DebugTools.Gdb(gdb, program, folder) : null,
        };
    }

    /// <summary>A debugger on PATH, or where its installer puts it (LLVM, Go's bin folder, MSYS2).</summary>
    public static string? Find(string name, Func<string, string?> onPath, string? home = null, string? programFiles = null)
    {
        if (onPath(name) is { } found) return found;
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        programFiles ??= Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] places = name switch
        {
            "lldb-dap" => [Path.Combine(programFiles, "LLVM", "bin", "lldb-dap.exe")],
            "dlv" => [Path.Combine(home, "go", "bin", "dlv.exe")],
            "gdb" => [@"C:\msys64\ucrt64\bin\gdb.exe", @"C:\msys64\mingw64\bin\gdb.exe", @"C:\msys64\clang64\bin\gdb.exe"],
            _ => [],
        };
        return places.FirstOrDefault(File.Exists);
    }

    /// <summary>What to install when no debugger for the language was found.</summary>
    public static string InstallHint(string languageId) => languageId switch
    {
        "go" => "Install Delve: go install github.com/go-delve/delve/cmd/dlv@latest",
        "c" or "cpp" => "Install GDB with MSYS2: pacman -S mingw-w64-ucrt-x86_64-gdb (or LLVM, which includes lldb-dap: winget install LLVM.LLVM)",
        _ => "Install LLVM, which includes lldb-dap: winget install LLVM.LLVM",
    };
}

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

    public bool Start(string executable, IEnumerable<string> arguments, IDictionary<string, string>? environment)
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
        input = process.StandardInput.BaseStream;
        var output = process.StandardOutput.BaseStream;
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
        // The adapter's own diagnostics: read so its pipe never fills, but don't show them.
        Task.Run(() => process.StandardError.ReadToEndAsync());
        return true;
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
        adapter.Exited += () => SetState(DebugState.Ended);
        if (!adapter.Start(Configuration.Adapter, Configuration.AdapterArguments, Configuration.Environment)) return false;
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
}

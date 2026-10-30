using System.Text.RegularExpressions;

namespace CodeForge.Core;

/// <summary>Visual Studio's solution configurations.</summary>
public enum BuildConfiguration { Debug, Release }

/// <summary>How to build, run and test a project folder.</summary>
/// <param name="Languages">Files in these languages belong to the project: Start runs the project for them, and the file itself otherwise.</param>
public sealed record BuildPlan(string Name, string Build, string? Clean, string Folder, string? Run, string[] Languages);

public static partial class BuildSystem
{
    private static readonly string[] DotNetProjectExtensions = [".csproj", ".vbproj", ".fsproj"];

    /// <summary>Recognizes the project in a folder from the files at its top level.</summary>
    public static BuildPlan? Detect(string folder, BuildConfiguration configuration = BuildConfiguration.Debug)
    {
        if (!Directory.Exists(folder)) return null;
        var names = Directory.GetFiles(folder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList();
        var config = configuration == BuildConfiguration.Release ? " -c Release" : "";

        var project = names.FirstOrDefault(n => DotNetProjectExtensions.Any(e => n.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
        var solution = names.FirstOrDefault(n => n.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));
        if (project != null || solution != null)
        {
            // `dotnet run` needs a project; with only a solution here, run the first project in a subfolder.
            var runTarget = project == null && FirstProject(folder) is { } sub ? " --project " + CommandTemplate.Quote(sub) : "";
            var name = Path.GetFileNameWithoutExtension(project ?? solution)!;
            return new BuildPlan($".NET ({name})", "dotnet build" + config, "dotnet clean", folder, "dotnet run" + config + runTarget,
                                 ["csharp", "vb", "fsharp", "razor", "xaml", "html", "css", "javascript", "json", "xml"]);
        }
        if (names.Contains("package.json"))
        {
            var manifest = File.ReadAllText(Path.Combine(folder, "package.json"));
            bool hasBuild = manifest.Contains("\"build\""), hasStart = manifest.Contains("\"start\"");
            if (hasBuild || hasStart)
                return new BuildPlan("npm", hasBuild ? "npm run build" : "npm install", null, folder, hasStart ? "npm start" : null,
                                     ["javascript", "typescript", "json", "html", "css"]);
        }
        if (names.Contains("Cargo.toml"))
            return new BuildPlan("Cargo", "cargo build" + (configuration == BuildConfiguration.Release ? " --release" : ""), "cargo clean", folder,
                                 "cargo run" + (configuration == BuildConfiguration.Release ? " --release" : ""), ["rust"]);
        if (names.Contains("go.mod"))
            return new BuildPlan("Go module", "go build ./...", "go clean", folder, "go run .", ["go"]);
        if (names.Contains("CMakeLists.txt"))
            return new BuildPlan("CMake", $"cmake -S . -B build && cmake --build build --config {configuration}", "rmdir /s /q build", folder, null, ["c", "cpp"]);
        if (names.Contains("tsconfig.json"))
            return new BuildPlan("TypeScript", "npx --yes tsc", null, folder, null, ["typescript"]);
        return null;
    }

    /// <summary>The command that runs a project's tests (Test ▸ Run All Tests), if the project type has one.</summary>
    public static string? TestCommand(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        var names = Directory.GetFiles(folder).Select(Path.GetFileName).OfType<string>().ToList();
        if (names.Any(n => n.EndsWith(".sln") || n.EndsWith(".slnx") || DotNetProjectExtensions.Any(n.EndsWith))) return "dotnet test";
        if (names.Contains("Cargo.toml")) return "cargo test";
        if (names.Contains("go.mod")) return "go test ./...";
        if (names.Contains("package.json") && File.ReadAllText(Path.Combine(folder, "package.json")).Contains("\"test\"")) return "npm test";
        if (Directory.Exists(Path.Combine(folder, "tests")) || names.Any(n => n.StartsWith("test_") && n.EndsWith(".py"))) return "python -m pytest";
        return null;
    }

    /// <summary>The first .NET project file in a subfolder (relative path), for a solution folder.</summary>
    public static string? FirstProject(string folder)
    {
        foreach (var sub in Directory.GetDirectories(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(sub).StartsWith('.')) continue;
            var file = Directory.GetFiles(sub).Order(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(f => DotNetProjectExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
            if (file != null) return Path.GetRelativePath(folder, file);
        }
        return null;
    }

    /// <summary>ASP.NET apps print the address they listen on; Start opens it in the browser once.</summary>
    public static string? ListeningAddress(string output)
    {
        var match = ListeningRegex().Match(output);
        if (!match.Success) return null;
        return match.Groups[1].Value.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost").Replace("+", "localhost").Replace("*", "localhost");
    }

    [GeneratedRegex(@"Now listening on: (https?://\S+)")]
    private static partial Regex ListeningRegex();
}

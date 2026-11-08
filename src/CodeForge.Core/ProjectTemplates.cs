using System.Text;

namespace CodeForge.Core;

/// <summary>A starter project for File ▸ New Project.</summary>
/// <param name="Command">A command run in the parent folder that creates the project (<c>dotnet new …</c>);
/// {folder} is the new folder's name and {ident} the name as an identifier. Null for projects made from <paramref name="Files"/>.</param>
/// <param name="MainFile">The file opened after the project is created.</param>
public sealed record ProjectTemplate(string Id, string Name, string Summary, string Tags, string Group, string MainFile,
                                     string? Command = null, IReadOnlyDictionary<string, string>? Files = null, string? RunCommand = null);

public static class ProjectTemplates
{
    private static ProjectTemplate DotNet(string id, string name, string summary, string tags, string mainFile, string command) =>
        new(id, name, summary, tags, ".NET", mainFile, "dotnet new " + command + " -o \"{folder}\" -n {ident}");

    /// <summary>Windows desktop templates: these only run on Windows, so they're new compared with the Mac version.</summary>
    public static readonly IReadOnlyList<ProjectTemplate> Windows =
    [
        new("winforms-vb", "Windows Forms App (VB.NET)", "A Windows desktop app with forms you design by dragging controls.", "VB.NET  Windows  Desktop",
            "Windows Desktop", "Form1.vb", "dotnet new winforms -lang VB -o \"{folder}\" -n {ident}"),
        new("winforms-cs", "Windows Forms App (C#)", "A Windows desktop app with forms you design by dragging controls.", "C#  Windows  Desktop",
            "Windows Desktop", "Form1.cs", "dotnet new winforms -o \"{folder}\" -n {ident}"),
        new("wpf-vb", "WPF Application (VB.NET)", "A Windows desktop app whose windows are written in XAML.", "VB.NET  WPF  XAML  Desktop",
            "Windows Desktop", "MainWindow.xaml", "dotnet new wpf -lang VB -o \"{folder}\" -n {ident}"),
        new("wpf-cs", "WPF Application (C#)", "A Windows desktop app whose windows are written in XAML.", "C#  WPF  XAML  Desktop",
            "Windows Desktop", "MainWindow.xaml", "dotnet new wpf -o \"{folder}\" -n {ident}"),
        new("winforms-lib-vb", "Windows Forms Class Library (VB.NET)", "Forms and controls to share between Windows apps.", "VB.NET  Windows  Library",
            "Windows Desktop", "Class1.vb", "dotnet new winformslib -lang VB -o \"{folder}\" -n {ident}"),
        new("wpf-lib-cs", "WPF Class Library (C#)", "XAML windows and controls to share between WPF apps.", "C#  WPF  Library",
            "Windows Desktop", "Class1.cs", "dotnet new wpflib -o \"{folder}\" -n {ident}"),
    ];

    public static readonly IReadOnlyList<ProjectTemplate> DotNetTemplates =
    [
        DotNet("vb-console", "Console App (VB.NET)", "A Visual Basic .NET command-line program.", "VB.NET  Console", "Program.vb", "console -lang VB"),
        DotNet("vb-library", "Class Library (VB.NET)", "A Visual Basic .NET library of classes.", "VB.NET  Library", "Class1.vb", "classlib -lang VB"),
        DotNet("cs-console", "Console App (C#)", "A C# command-line program.", "C#  Console", "Program.cs", "console"),
        DotNet("cs-library", "Class Library (C#)", "A C# library of classes.", "C#  Library", "Class1.cs", "classlib"),
        DotNet("aspnet-razor", "ASP.NET Core Web App (Razor Pages)", "A web site with Razor Pages; Start opens it in your browser.", "C#  ASP.NET  Web", "Pages/Index.cshtml", "webapp"),
        DotNet("aspnet-mvc", "ASP.NET Core Web App (Model-View-Controller)", "A web site with controllers and views.", "C#  ASP.NET  Web", "Controllers/HomeController.cs", "mvc"),
        DotNet("aspnet-api", "ASP.NET Core Web API", "An HTTP API with OpenAPI support.", "C#  ASP.NET  API", "Program.cs", "webapi"),
        DotNet("aspnet-empty", "ASP.NET Core Empty", "One Program.cs that answers “Hello World!”.", "C#  ASP.NET  Web", "Program.cs", "web"),
        DotNet("blazor", "Blazor Web App", "Interactive web UI with C# and Razor components.", "C#  Blazor  Web", "Components/Pages/Home.razor", "blazor"),
        DotNet("blazorwasm", "Blazor WebAssembly Standalone App", "A Blazor app that runs in the browser.", "C#  Blazor  WebAssembly", "Pages/Home.razor", "blazorwasm"),
        DotNet("grpc", "ASP.NET Core gRPC Service", "A gRPC service with a sample Greeter.", "C#  gRPC  Service", "Services/GreeterService.cs", "grpc"),
        DotNet("worker", "Worker Service", "A long-running background service.", "C#  Service", "Worker.cs", "worker"),
        DotNet("maui", ".NET MAUI App", "A cross-platform app with XAML pages; runs on Windows. Needs: dotnet workload install maui", "C#  MAUI  XAML", "MainPage.xaml", "maui"),
        DotNet("xunit-cs", "xUnit Test Project (C#)", "Unit tests; run them with Test ▸ Run All Tests.", "C#  Test", "UnitTest1.cs", "xunit"),
        DotNet("xunit-vb", "xUnit Test Project (VB.NET)", "Visual Basic unit tests.", "VB.NET  Test", "UnitTest1.vb", "xunit -lang VB"),
        DotNet("mstest", "MSTest Test Project", "Unit tests with MSTest.", "C#  Test", "Test1.cs", "mstest"),
        DotNet("nunit", "NUnit Test Project", "Unit tests with NUnit.", "C#  Test", "UnitTest1.cs", "nunit"),
        DotNet("razorclasslib", "Razor Class Library", "Razor components and pages to share between web apps.", "C#  Razor  Library", "Component1.razor", "razorclasslib"),
        DotNet("fs-console", "Console App (F#)", "An F# command-line program.", "F#  Console", "Program.fs", "console -lang F#"),
    ];

    public static readonly IReadOnlyList<ProjectTemplate> General =
    [
        new("python", "Python Console App", "A Python program that runs in the Output window.", "Python", "General", "main.py",
            Files: new Dictionary<string, string>
            {
                ["main.py"] = "def main():\n    name = input(\"What's your name? \")\n    print(f\"Hello, {name}! Welcome to {{name}}.\")\n\n\nif __name__ == \"__main__\":\n    main()\n",
                [".gitignore"] = "__pycache__/\n.venv/\n",
            }),
        new("node", "Node.js Console App", "A JavaScript program for Node.js.", "JavaScript  Node.js", "General", "index.js",
            Files: new Dictionary<string, string>
            {
                ["index.js"] = "console.log(\"Hello from {{name}}!\");\n",
                ["package.json"] = "{\n  \"name\": \"{{ident}}\",\n  \"version\": \"1.0.0\",\n  \"main\": \"index.js\",\n  \"scripts\": { \"start\": \"node index.js\" }\n}\n",
                [".gitignore"] = "node_modules/\n",
            }),
        new("web", "Web Page (HTML, CSS, JavaScript)", "A static page that opens in your browser.", "HTML  CSS  JavaScript", "General", "index.html",
            Files: new Dictionary<string, string>
            {
                ["index.html"] = "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n    <meta charset=\"utf-8\">\n    <title>{{name}}</title>\n    <link rel=\"stylesheet\" href=\"style.css\">\n</head>\n<body>\n    <h1>{{name}}</h1>\n    <script src=\"script.js\"></script>\n</body>\n</html>\n",
                ["style.css"] = "body {\n    font-family: \"Segoe UI\", sans-serif;\n    margin: 40px;\n}\n",
                ["script.js"] = "console.log(\"Ready\");\n",
            }),
        // The same as the Mac version's. compile_flags.txt tells clangd (IntelliSense) the language standard.
        new("c", "C Console App", "A C17 program built with clang or gcc.", "C", "General", "main.c",
            Files: new Dictionary<string, string>
            {
                ["compile_flags.txt"] = "-std=c17\n-Wall\n",
                ["main.c"] = "#include <stdio.h>\n\nint main(void) {\n    printf(\"Hello from {{name}}!\\n\");\n    return 0;\n}\n",
            }),
        new("cpp", "C++ Console App", "A C++20 program built with clang++ or g++.", "C++", "General", "main.cpp",
            Files: new Dictionary<string, string>
            {
                ["compile_flags.txt"] = "-std=c++20\n-Wall\n",
                ["main.cpp"] = "#include <iostream>\n#include <string>\n\nint main() {\n    std::string name = \"{{name}}\";\n    std::cout << \"Hello from \" << name << \"!\" << std::endl;\n    return 0;\n}\n",
            }),
        new("typescript", "TypeScript Console App", "A TypeScript program for Node.js.", "TypeScript  Node.js", "General", "index.ts",
            Files: new Dictionary<string, string>
            {
                ["index.ts"] = "function greet(name: string): string {\n    return `Hello from ${name}!`;\n}\n\nconsole.log(greet(\"{{name}}\"));\n",
                [".gitignore"] = "node_modules/\n",
            }),
        new("go", "Go Console App", "A Go module, run with go run.", "Go", "General", "main.go",
            Files: new Dictionary<string, string>
            {
                ["go.mod"] = "module {{ident}}\n\ngo 1.22\n",
                ["main.go"] = "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tfmt.Println(\"Hello from {{name}}!\")\n}\n",
            }),
        new("rust", "Rust Console App", "A Cargo project, run with cargo run.", "Rust", "General", "src/main.rs",
            Files: new Dictionary<string, string>
            {
                ["Cargo.toml"] = "[package]\nname = \"{{crate}}\"\nversion = \"0.1.0\"\nedition = \"2021\"\n",
                ["src/main.rs"] = "fn main() {\n    println!(\"Hello from {{name}}!\");\n}\n",
                [".gitignore"] = "target/\n",
            }),
        new("java", "Java Console App", "A single-file Java program.", "Java", "General", "Main.java",
            Files: new Dictionary<string, string>
            {
                ["Main.java"] = "public class Main {\n    public static void main(String[] args) {\n        System.out.println(\"Hello from {{name}}!\");\n    }\n}\n",
            }),
        new("empty", "Empty Project", "A folder with a README, ready for any language.", "Any", "General", "README.md",
            Files: new Dictionary<string, string> { ["README.md"] = "# {{name}}\n" }),
    ];

    public static IReadOnlyList<ProjectTemplate> All { get; } = [.. Windows, .. DotNetTemplates, .. General];

    /// <summary>"My App 2" → "MyApp2"; a leading digit gets an underscore.</summary>
    public static string Identifier(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name) if (char.IsLetterOrDigit(c) || c == '_') builder.Append(c);
        var result = builder.ToString();
        if (result.Length == 0) return "App";
        return char.IsDigit(result[0]) ? "_" + result : result;
    }

    /// <summary>Cargo wants lowercase snake_case ASCII names: "My App" → "my_app".</summary>
    public static string CrateName(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
            builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        var result = builder.ToString().Trim('_');
        while (result.Contains("__")) result = result.Replace("__", "_");
        return result.Length == 0 || char.IsDigit(result[0]) ? "app" + (result.Length > 0 ? "_" + result : "") : result;
    }

    public static string? CreationCommand(ProjectTemplate template, string name) =>
        template.Command?.Replace("{folder}", name).Replace("{ident}", Identifier(name));

    /// <summary>Writes a file-based template into location/name and returns the new folder.</summary>
    public static string Create(ProjectTemplate template, string name, string location)
    {
        var folder = Path.Combine(location, name);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new IOException($"“{name}” already exists in {location}.");
        foreach (var (path, contents) in template.Files ?? new Dictionary<string, string>())
        {
            var target = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, contents.Replace("{{name}}", name).Replace("{{ident}}", Identifier(name)).Replace("{{crate}}", CrateName(name)));
        }
        return folder;
    }
}

namespace CodeForge.Core;

/// <summary>A language CodeForge can highlight and (optionally) run.</summary>
/// <param name="Highlighting">AvalonEdit's built-in definition, when it has one; otherwise one is made from the keywords.</param>
/// <param name="RunCommand">Command that runs a file. Placeholders: {file} {dir} {name} {out} (see <see cref="CommandTemplate"/>).</param>
public sealed record Language(
    string Id, string Name, string[] Extensions,
    string? Highlighting = null,
    string Keywords = "", string Types = "",
    string? LineComment = null, string? BlockStart = null, string? BlockEnd = null,
    string Strings = "\"'",
    bool CaseInsensitive = false,
    string? RunCommand = null, string? InstallHint = null,
    string[]? FileNames = null)
{
    public IEnumerable<string> KeywordList => Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    public IEnumerable<string> TypeList => Types.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

public static class Languages
{
    public static readonly Language PlainText = new("text", "Plain Text", ["txt", "log"]);

    private const string CKeywords = "auto break case char const continue default do double else enum extern float for goto if inline int long " +
        "register restrict return short signed sizeof static struct switch typedef union unsigned void volatile while bool true false NULL";
    private const string JsKeywords = "break case catch class const continue debugger default delete do else export extends finally for function if " +
        "import in instanceof let new return super switch this throw try typeof var void while with yield async await of static get set null undefined true false";

    public static readonly IReadOnlyList<Language> All =
    [
        new("csharp", "C#", ["cs", "csx"], Highlighting: "C#", LineComment: "//", BlockStart: "/*", BlockEnd: "*/",
            RunCommand: "dotnet run \"{file}\"", InstallHint: "Install the .NET SDK: winget install Microsoft.DotNet.SDK.10"),
        new("vb", "VB.NET", ["vb", "vbs"], Highlighting: "VB", LineComment: "'", CaseInsensitive: true,
            InstallHint: "Install the .NET SDK: winget install Microsoft.DotNet.SDK.10"),
        new("fsharp", "F#", ["fs", "fsx", "fsi"], LineComment: "//", BlockStart: "(*", BlockEnd: "*)",
            Keywords: "abstract and as assert base begin class default delegate do done downcast downto elif else end exception extern false finally " +
                      "for fun function global if in inherit inline interface internal lazy let match member module mutable namespace new not null of open " +
                      "or override private public rec return sig static struct then to true try type upcast use val void when while with yield async task",
            RunCommand: "dotnet fsi \"{file}\"", InstallHint: "Install the .NET SDK: winget install Microsoft.DotNet.SDK.10"),
        new("razor", "Razor (ASP.NET)", ["cshtml", "razor", "vbhtml"], Highlighting: "ASP/XHTML"),
        new("xaml", "XAML", ["xaml", "axaml"], Highlighting: "XML"),
        new("python", "Python", ["py", "pyw"], Highlighting: "Python", LineComment: "#",
            RunCommand: "python -u \"{file}\"", InstallHint: "Install Python: winget install Python.Python.3.13"),
        new("javascript", "JavaScript", ["js", "mjs", "cjs", "jsx"], Highlighting: "JavaScript", LineComment: "//", BlockStart: "/*", BlockEnd: "*/",
            RunCommand: "node \"{file}\"", InstallHint: "Install Node.js: winget install OpenJS.NodeJS.LTS"),
        new("typescript", "TypeScript", ["ts", "tsx", "mts", "cts"], LineComment: "//", BlockStart: "/*", BlockEnd: "*/", Strings: "\"'`",
            Keywords: JsKeywords + " interface type enum implements namespace declare abstract readonly private protected public keyof infer is as satisfies",
            Types: "any unknown never string number boolean symbol bigint object",
            RunCommand: "npx --yes tsx \"{file}\"", InstallHint: "Install Node.js: winget install OpenJS.NodeJS.LTS"),
        new("c", "C", ["c", "h"], Highlighting: "C++", LineComment: "//", BlockStart: "/*", BlockEnd: "*/",
            RunCommand: "gcc -Wall \"{file}\" -o \"{out}.exe\" && \"{out}.exe\"", InstallHint: "Install a C compiler, for example: winget install MSYS2.MSYS2 (then pacman -S mingw-w64-ucrt-x86_64-gcc)"),
        new("cpp", "C++", ["cpp", "cc", "cxx", "hpp", "hh", "hxx"], Highlighting: "C++", LineComment: "//", BlockStart: "/*", BlockEnd: "*/",
            RunCommand: "g++ -std=c++20 -Wall \"{file}\" -o \"{out}.exe\" && \"{out}.exe\"", InstallHint: "Install a C++ compiler, for example: winget install MSYS2.MSYS2 (then pacman -S mingw-w64-ucrt-x86_64-gcc)"),
        new("java", "Java", ["java"], Highlighting: "Java", LineComment: "//", BlockStart: "/*", BlockEnd: "*/",
            RunCommand: "java \"{file}\"", InstallHint: "Install a JDK: winget install Microsoft.OpenJDK.21"),
        new("kotlin", "Kotlin", ["kt", "kts"], LineComment: "//", BlockStart: "/*", BlockEnd: "*/",
            Keywords: "as break class continue do else false for fun if in interface is null object package return super this throw true try typealias " +
                      "val var when while by catch constructor finally get import init set where abstract annotation companion const data enum final " +
                      "inline inner internal lateinit open operator out override private protected public sealed suspend vararg"),
        new("go", "Go", ["go"], LineComment: "//", BlockStart: "/*", BlockEnd: "*/", Strings: "\"'`",
            Keywords: "break case chan const continue default defer else fallthrough for func go goto if import interface map package range return " +
                      "select struct switch type var true false nil iota",
            Types: "bool byte complex64 complex128 error float32 float64 int int8 int16 int32 int64 rune string uint uint8 uint16 uint32 uint64 uintptr any",
            RunCommand: "go run \"{file}\"", InstallHint: "Install Go: winget install GoLang.Go"),
        new("rust", "Rust", ["rs"], LineComment: "//", BlockStart: "/*", BlockEnd: "*/",
            Keywords: "as async await break const continue crate dyn else enum extern false fn for if impl in let loop match mod move mut pub ref " +
                      "return self Self static struct super trait true type unsafe use where while",
            Types: "i8 i16 i32 i64 i128 isize u8 u16 u32 u64 u128 usize f32 f64 bool char str String Vec Option Result Box",
            RunCommand: "rustc \"{file}\" -o \"{out}.exe\" && \"{out}.exe\"", InstallHint: "Install Rust: winget install Rustlang.Rustup"),
        new("swift", "Swift", ["swift"], LineComment: "//", BlockStart: "/*", BlockEnd: "*/", Strings: "\"",
            Keywords: "associatedtype class deinit enum extension fileprivate func import init inout internal let open operator private protocol public " +
                      "static struct subscript typealias var break case catch continue default defer do else fallthrough for guard if in repeat return " +
                      "throw switch where while as false is nil self Self super throws true try async await actor some any",
            RunCommand: "swift \"{file}\"", InstallHint: "Install Swift for Windows from swift.org."),
        new("ruby", "Ruby", ["rb", "rake", "gemspec"], LineComment: "#",
            Keywords: "BEGIN END alias and begin break case class def defined? do else elsif end ensure false for if in module next nil not or redo " +
                      "rescue retry return self super then true undef unless until when while yield require attr_accessor puts",
            RunCommand: "ruby \"{file}\"", InstallHint: "Install Ruby: winget install RubyInstallerTeam.Ruby.3.3", FileNames: ["Gemfile", "Rakefile"]),
        new("php", "PHP", ["php"], Highlighting: "PHP", RunCommand: "php \"{file}\"", InstallHint: "Install PHP from windows.php.net."),
        new("lua", "Lua", ["lua"], LineComment: "--",
            Keywords: "and break do else elseif end false for function goto if in local nil not or repeat return then true until while",
            RunCommand: "lua \"{file}\""),
        new("powershell", "PowerShell", ["ps1", "psm1", "psd1"], Highlighting: "PowerShell", LineComment: "#",
            RunCommand: "powershell -NoProfile -ExecutionPolicy Bypass -File \"{file}\""),
        new("batch", "Batch", ["bat", "cmd"], LineComment: "REM ", CaseInsensitive: true,
            Keywords: "echo set if else for in do goto call exit not exist defined errorlevel pause cls cd setlocal endlocal shift start rem off on",
            RunCommand: "cmd /c \"{file}\""),
        new("shell", "Shell", ["sh", "bash", "zsh"], LineComment: "#",
            Keywords: "if then else elif fi case esac for while until do done in function select return local export readonly echo exit",
            RunCommand: "bash \"{file}\"", InstallHint: "Install Git for Windows (it includes bash): winget install Git.Git"),
        new("sql", "SQL", ["sql"], Highlighting: "TSQL", LineComment: "--", BlockStart: "/*", BlockEnd: "*/"),
        new("html", "HTML", ["html", "htm", "xhtml"], Highlighting: "HTML", RunCommand: "start \"\" \"{file}\""),
        new("xml", "XML", ["xml", "csproj", "vbproj", "fsproj", "props", "targets", "slnx", "config", "resx", "nuspec", "svg", "manifest", "xsd"], Highlighting: "XML"),
        new("css", "CSS", ["css", "scss", "less"], Highlighting: "CSS"),
        new("json", "JSON", ["json", "jsonc"], Strings: "\"", Keywords: "true false null"),
        new("yaml", "YAML", ["yml", "yaml"], LineComment: "#", Keywords: "true false null yes no on off"),
        new("toml", "TOML / INI", ["toml", "ini", "cfg", "conf", "editorconfig"], LineComment: "#", Keywords: "true false"),
        new("markdown", "Markdown", ["md", "markdown"], Highlighting: "MarkDown", RunCommand: "start \"\" \"{file}\""),
        new("diff", "Diff", ["diff", "patch"], Highlighting: "Patch"),
        new("dockerfile", "Dockerfile", ["dockerfile"], LineComment: "#", CaseInsensitive: true,
            Keywords: "FROM RUN CMD LABEL EXPOSE ENV ADD COPY ENTRYPOINT VOLUME USER WORKDIR ARG ONBUILD STOPSIGNAL HEALTHCHECK SHELL AS",
            FileNames: ["Dockerfile", "Containerfile"]),
    ];

    public static Language ById(string id) => All.FirstOrDefault(l => l.Id == id) ?? PlainText;

    public static Language ForFile(string path)
    {
        var name = Path.GetFileName(path);
        var byName = All.FirstOrDefault(l => l.FileNames?.Contains(name, StringComparer.OrdinalIgnoreCase) == true);
        if (byName != null) return byName;
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (extension.Length == 0) return PlainText;
        return All.FirstOrDefault(l => l.Extensions.Contains(extension)) ?? PlainText;
    }
}

/// <summary>Fills in run command placeholders: {file}, {dir}, {name} (file name without extension) and {out}.</summary>
public static class CommandTemplate
{
    public static string Expand(string template, string file, string outputDirectory) =>
        template.Replace("{file}", file)
                .Replace("{dir}", Path.GetDirectoryName(file) ?? "")
                .Replace("{name}", Path.GetFileNameWithoutExtension(file))
                .Replace("{out}", Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(file)));

    /// <summary>Quotes an argument for cmd.exe.</summary>
    public static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}

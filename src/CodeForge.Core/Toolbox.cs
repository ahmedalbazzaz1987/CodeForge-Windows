namespace CodeForge.Core;

/// <summary>Something you can double-click or drag from the Toolbox into the editor. <c>$0</c> marks where the cursor goes.</summary>
public sealed record ToolboxItem(string Name, string Glyph, string Snippet)
{
    /// <summary>The snippet as inserted on a line indented with <paramref name="indent"/>, and the cursor offset in it.</summary>
    public (string Text, int Cursor) Expanded(string indent)
    {
        var text = Snippet.Replace("\n", "\n" + indent);
        var marker = text.IndexOf("$0", StringComparison.Ordinal);
        return marker < 0 ? (text, text.Length) : (text.Remove(marker, 2), marker);
    }
}

public sealed record ToolboxGroup(string Name, IReadOnlyList<ToolboxItem> Items);

/// <summary>The Toolbox for each kind of file, like Visual Studio's Toolbox tabs. Glyphs are Segoe Fluent Icons.</summary>
public static class ToolboxCatalog
{
    public static IReadOnlyList<ToolboxGroup> Groups(string languageId) => languageId switch
    {
        "xaml" => [Wpf, WpfLayout],
        "html" => [Html],
        "razor" => [TagHelpers, Html],
        "csharp" => [CSharp],
        "vb" => [VisualBasic],
        "python" => [Python],
        "javascript" => [JavaScript],
        "typescript" => [TypeScript, JavaScript],
        "c" => [C],
        "cpp" => [Cpp, C],
        "java" => [Java],
        "go" => [Go],
        "rust" => [Rust],
        _ => [],
    };

    static readonly ToolboxGroup Wpf = new("WPF Controls",
    [
        new("Button", "", "<Button Content=\"$0Button\" Width=\"100\" Height=\"30\" Click=\"Button_Click\"/>"),
        new("TextBox", "", "<TextBox x:Name=\"$0textBox\" Width=\"200\"/>"),
        new("PasswordBox", "", "<PasswordBox x:Name=\"$0passwordBox\" Width=\"200\"/>"),
        new("Label", "", "<Label Content=\"$0Label\"/>"),
        new("TextBlock", "", "<TextBlock Text=\"$0Text\"/>"),
        new("CheckBox", "", "<CheckBox Content=\"$0CheckBox\"/>"),
        new("RadioButton", "", "<RadioButton Content=\"$0Option\" GroupName=\"Group1\"/>"),
        new("ComboBox", "", "<ComboBox x:Name=\"$0comboBox\" Width=\"150\">\n    <ComboBoxItem Content=\"Item 1\"/>\n    <ComboBoxItem Content=\"Item 2\"/>\n</ComboBox>"),
        new("ListBox", "", "<ListBox x:Name=\"$0listBox\" Height=\"100\">\n    <ListBoxItem Content=\"Item 1\"/>\n</ListBox>"),
        new("DataGrid", "", "<DataGrid x:Name=\"$0dataGrid\" AutoGenerateColumns=\"True\"/>"),
        new("Image", "", "<Image Source=\"$0\" Width=\"100\"/>"),
        new("Slider", "", "<Slider Minimum=\"0\" Maximum=\"100\" Width=\"150\"/>"),
        new("ProgressBar", "", "<ProgressBar Value=\"$050\" Height=\"10\" Width=\"200\"/>"),
        new("DatePicker", "", "<DatePicker x:Name=\"$0datePicker\"/>"),
        new("Menu", "", "<Menu>\n    <MenuItem Header=\"_File\">\n        <MenuItem Header=\"_Exit\"/>\n    </MenuItem>\n</Menu>$0"),
    ]);

    static readonly ToolboxGroup WpfLayout = new("WPF Layout",
    [
        new("Grid", "", "<Grid>\n    <Grid.RowDefinitions>\n        <RowDefinition Height=\"Auto\"/>\n        <RowDefinition Height=\"*\"/>\n    </Grid.RowDefinitions>\n    $0\n</Grid>"),
        new("StackPanel", "", "<StackPanel Margin=\"10\">\n    $0\n</StackPanel>"),
        new("DockPanel", "", "<DockPanel>\n    $0\n</DockPanel>"),
        new("WrapPanel", "", "<WrapPanel>\n    $0\n</WrapPanel>"),
        new("Border", "", "<Border BorderBrush=\"Gray\" BorderThickness=\"1\" CornerRadius=\"4\" Padding=\"8\">\n    $0\n</Border>"),
        new("ScrollViewer", "", "<ScrollViewer>\n    $0\n</ScrollViewer>"),
        new("TabControl", "", "<TabControl>\n    <TabItem Header=\"Tab 1\">$0</TabItem>\n    <TabItem Header=\"Tab 2\"/>\n</TabControl>"),
        new("GroupBox", "", "<GroupBox Header=\"$0Group\">\n    \n</GroupBox>"),
    ]);

    static readonly ToolboxGroup Html = new("HTML",
    [
        new("Button", "", "<button type=\"button\">$0Button</button>"),
        new("Text Box", "", "<input type=\"text\" name=\"$0\" placeholder=\"\">"),
        new("Password", "", "<input type=\"password\" name=\"$0\">"),
        new("Check Box", "", "<label><input type=\"checkbox\" name=\"$0\"> Check box</label>"),
        new("Drop-Down List", "", "<select name=\"$0\">\n    <option>Item 1</option>\n    <option>Item 2</option>\n</select>"),
        new("Text Area", "", "<textarea name=\"$0\" rows=\"4\" cols=\"40\"></textarea>"),
        new("Form", "", "<form method=\"post\" action=\"$0\">\n    \n    <button type=\"submit\">Submit</button>\n</form>"),
        new("Table", "", "<table>\n    <tr><th>Header</th></tr>\n    <tr><td>$0</td></tr>\n</table>"),
        new("Image", "", "<img src=\"$0\" alt=\"\">"),
        new("Hyperlink", "", "<a href=\"$0\">Link</a>"),
        new("Div", "", "<div>\n    $0\n</div>"),
    ]);

    static readonly ToolboxGroup TagHelpers = new("ASP.NET Tag Helpers",
    [
        new("Form", "", "<form asp-action=\"$0\" method=\"post\">\n    \n</form>"),
        new("Input", "", "<input asp-for=\"$0\" class=\"form-control\" />"),
        new("Label", "", "<label asp-for=\"$0\"></label>"),
        new("Validation Message", "", "<span asp-validation-for=\"$0\" class=\"text-danger\"></span>"),
        new("Anchor", "", "<a asp-page=\"$0\">Link</a>"),
    ]);

    static readonly ToolboxGroup CSharp = new("C# Snippets",
    [
        new("Class", "", "public class $0MyClass\n{\n    \n}"),
        new("Property", "", "public $0string Name { get; set; }"),
        new("Method", "", "public void $0Method()\n{\n    \n}"),
        new("if", "", "if ($0condition)\n{\n    \n}"),
        new("foreach", "", "foreach (var item in $0items)\n{\n    \n}"),
        new("try…catch", "", "try\n{\n    $0\n}\ncatch (Exception ex)\n{\n    Console.WriteLine(ex.Message);\n}"),
        new("Console.WriteLine", "", "Console.WriteLine($0);"),
        new("MessageBox.Show", "", "MessageBox.Show(\"$0\");"),
    ]);

    static readonly ToolboxGroup VisualBasic = new("VB.NET Snippets",
    [
        new("Class", "", "Public Class $0ClassName\n    \nEnd Class"),
        new("Sub", "", "Public Sub $0Name()\n    \nEnd Sub"),
        new("Function", "", "Public Function $0Name() As Integer\n    Return 0\nEnd Function"),
        new("Property", "", "Public Property $0Name As String"),
        new("If…Then", "", "If $0condition Then\n    \nEnd If"),
        new("For Each", "", "For Each item In $0items\n    \nNext"),
        new("Try…Catch", "", "Try\n    $0\nCatch ex As Exception\n    MessageBox.Show(ex.Message)\nEnd Try"),
        new("MessageBox.Show", "", "MessageBox.Show(\"$0\")"),
        new("Console.WriteLine", "", "Console.WriteLine($0)"),
    ]);

    static readonly ToolboxGroup Python = new("Python Snippets",
    [
        new("Function", "", "def $0name():\n    pass"),
        new("Class", "", "class $0Name:\n    def __init__(self):\n        pass"),
        new("Main guard", "", "if __name__ == \"__main__\":\n    $0main()"),
        new("for loop", "", "for item in $0items:\n    pass"),
        new("try…except", "", "try:\n    $0\nexcept Exception as error:\n    print(error)"),
    ]);

    static readonly ToolboxGroup JavaScript = new("JavaScript Snippets",
    [
        new("Function", "", "function $0name() {\n    \n}"),
        new("Arrow function", "", "const $0name = () => {\n    \n};"),
        new("Event listener", "", "document.querySelector(\"$0\").addEventListener(\"click\", (event) => {\n    \n});"),
        new("fetch JSON", "", "const response = await fetch($0url);\nconst data = await response.json();"),
        new("console.log", "", "console.log($0);"),
    ]);

    static readonly ToolboxGroup C = new("C Snippets",
    [
        new("main", "", "#include <stdio.h>\n\nint main(void) {\n    $0\n    return 0;\n}"),
        new("Function", "", "int $0name(void) {\n    return 0;\n}"),
        new("Struct", "", "typedef struct {\n    int $0value;\n} Name;"),
        new("if", "", "if ($0condition) {\n    \n}"),
        new("for loop", "", "for (int i = 0; i < $0count; i++) {\n    \n}"),
        new("while loop", "", "while ($0condition) {\n    \n}"),
        new("switch", "", "switch ($0value) {\ncase 1:\n    break;\ndefault:\n    break;\n}"),
        new("printf", "", "printf(\"$0\\n\");"),
        new("malloc", "", "int *items = malloc($0count * sizeof *items);\nif (items == NULL) {\n    return 1;\n}\nfree(items);"),
    ]);

    static readonly ToolboxGroup Cpp = new("C++ Snippets",
    [
        new("main", "", "#include <iostream>\n\nint main() {\n    $0\n    return 0;\n}"),
        new("Class", "", "class $0Name {\npublic:\n    Name() = default;\n\nprivate:\n    int value_ = 0;\n};"),
        new("Range for", "", "for (const auto& item : $0items) {\n    \n}"),
        new("cout", "", "std::cout << $0 << std::endl;"),
        new("vector", "", "std::vector<int> $0values;"),
        new("unique_ptr", "", "auto $0item = std::make_unique<Type>();"),
        new("try…catch", "", "try {\n    $0\n} catch (const std::exception& error) {\n    std::cerr << error.what() << std::endl;\n}"),
        new("Lambda", "", "auto $0name = [&](int value) {\n    return value;\n};"),
    ]);

    static readonly ToolboxGroup Java = new("Java Snippets",
    [
        new("main", "", "public static void main(String[] args) {\n    $0\n}"),
        new("Class", "", "public class $0Name {\n    \n}"),
        new("Method", "", "public void $0name() {\n    \n}"),
        new("Record", "", "public record $0Name(String value) {}"),
        new("if", "", "if ($0condition) {\n    \n}"),
        new("for each", "", "for (var item : $0items) {\n    \n}"),
        new("try…catch", "", "try {\n    $0\n} catch (Exception error) {\n    error.printStackTrace();\n}"),
        new("println", "", "System.out.println($0);"),
        new("ArrayList", "", "var $0items = new java.util.ArrayList<String>();"),
    ]);

    static readonly ToolboxGroup TypeScript = new("TypeScript Snippets",
    [
        new("Interface", "", "interface $0Name {\n    id: number;\n}"),
        new("Type", "", "type $0Name = {\n    id: number;\n};"),
        new("Enum", "", "enum $0Name {\n    First,\n    Second,\n}"),
        new("Class", "", "class $0Name {\n    constructor(private readonly id: number) {}\n}"),
        new("Typed function", "", "function $0name(value: string): string {\n    return value;\n}"),
        new("Generic function", "", "function $0name<T>(value: T): T {\n    return value;\n}"),
        new("Async function", "", "async function $0name(): Promise<void> {\n    \n}"),
    ]);

    static readonly ToolboxGroup Go = new("Go Snippets",
    [
        new("main", "", "func main() {\n\t$0\n}"),
        new("Function", "", "func $0name() error {\n\treturn nil\n}"),
        new("Struct", "", "type $0Name struct {\n\tID int\n}"),
        new("if err", "", "if err != nil {\n\treturn $0err\n}"),
        new("for range", "", "for _, item := range $0items {\n\t\n}"),
        new("Println", "", "fmt.Println($0)"),
    ]);

    static readonly ToolboxGroup Rust = new("Rust Snippets",
    [
        new("main", "", "fn main() {\n    $0\n}"),
        new("Function", "", "fn $0name() -> i32 {\n    0\n}"),
        new("Struct", "", "struct $0Name {\n    id: u32,\n}"),
        new("impl", "", "impl $0Name {\n    fn new() -> Self {\n        Self {}\n    }\n}"),
        new("match", "", "match $0value {\n    Some(item) => {}\n    None => {}\n}"),
        new("for", "", "for item in $0items.iter() {\n    \n}"),
        new("println!", "", "println!(\"{}\", $0);"),
    ]);
}

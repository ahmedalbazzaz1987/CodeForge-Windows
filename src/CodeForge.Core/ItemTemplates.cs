namespace CodeForge.Core;

/// <summary>A new file from Project ▸ Add New Item, like Visual Studio's item templates.</summary>
/// <param name="Contents">{{name}} is the file name without its extension, {{namespace}} the project's root namespace.</param>
/// <param name="Companion">A second file made next to it (a form's .Designer file): its suffix and contents.</param>
public sealed record ItemTemplate(string Name, string Group, string Extension, string DefaultName, string Contents,
    (string Suffix, string Contents)? Companion = null);

public static class ItemTemplates
{
    private const string VbFormDesigner = """
        <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
        Partial Class {{name}}
            Inherits System.Windows.Forms.Form

            Private components As System.ComponentModel.IContainer

            <System.Diagnostics.DebuggerStepThrough()>
            Private Sub InitializeComponent()
                Me.SuspendLayout()
                Me.AutoScaleDimensions = New System.Drawing.SizeF(7.0!, 15.0!)
                Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
                Me.ClientSize = New System.Drawing.Size(800, 450)
                Me.Name = "{{name}}"
                Me.Text = "{{name}}"
                Me.ResumeLayout(False)
            End Sub
        End Class

        """;

    private const string CsFormDesigner = """
        namespace {{namespace}};

        partial class {{name}}
        {
            private System.ComponentModel.IContainer components = null;

            protected override void Dispose(bool disposing)
            {
                if (disposing && (components != null)) components.Dispose();
                base.Dispose(disposing);
            }

            private void InitializeComponent()
            {
                this.SuspendLayout();
                this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
                this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
                this.ClientSize = new System.Drawing.Size(800, 450);
                this.Name = "{{name}}";
                this.Text = "{{name}}";
                this.ResumeLayout(false);
            }
        }

        """;

    public static readonly IReadOnlyList<ItemTemplate> All =
    [
        new("Class", "VB.NET", "vb", "Class1", "Public Class {{name}}\n\nEnd Class\n"),
        new("Module", "VB.NET", "vb", "Module1", "Module {{name}}\n\nEnd Module\n"),
        new("Interface", "VB.NET", "vb", "IInterface1", "Public Interface {{name}}\n\nEnd Interface\n"),
        new("Windows Form", "VB.NET", "vb", "Form2", "Public Class {{name}}\n\nEnd Class\n", (".Designer.vb", VbFormDesigner)),
        new("Class", "C#", "cs", "Class1", "namespace {{namespace}};\n\npublic class {{name}}\n{\n}\n"),
        new("Interface", "C#", "cs", "IInterface1", "namespace {{namespace}};\n\npublic interface {{name}}\n{\n}\n"),
        new("Record", "C#", "cs", "Record1", "namespace {{namespace}};\n\npublic record {{name}}();\n"),
        new("Windows Form", "C#", "cs", "Form2",
            "namespace {{namespace}};\n\npublic partial class {{name}} : Form\n{\n    public {{name}}()\n    {\n        InitializeComponent();\n    }\n}\n",
            (".Designer.cs", CsFormDesigner)),
        new("Razor Page", "ASP.NET", "cshtml", "Page1", "@page\n\n<h1>{{name}}</h1>\n"),
        new("Razor Component", "ASP.NET", "razor", "Component1", "<h3>{{name}}</h3>\n\n@code {\n}\n"),
        new("HTML Page", "Web", "html", "page1",
            "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n    <meta charset=\"utf-8\">\n    <title>{{name}}</title>\n</head>\n<body>\n\n</body>\n</html>\n"),
        new("Style Sheet", "Web", "css", "styles", "body {\n}\n"),
        new("JavaScript File", "Web", "js", "script", ""),
        new("TypeScript File", "Web", "ts", "module1", "export {};\n"),
        new("Python File", "Python", "py", "module1", ""),
        new("C Source File", "C/C++", "c", "source1", "#include <stdio.h>\n"),
        new("C++ Source File", "C/C++", "cpp", "source1", "#include <iostream>\n"),
        new("Header File", "C/C++", "h", "header1", "#pragma once\n"),
        new("F# Source File", "F#", "fs", "Module1", "module {{name}}\n\n"),
        new("SQL Script", "Data", "sql", "Script1", "-- {{name}}\n"),
        new("JSON File", "General", "json", "settings", "{\n}\n"),
        new("Text File", "General", "txt", "TextFile1", ""),
        new("Markdown File", "General", "md", "README", "# {{name}}\n"),
    ];

    /// <summary>The file name, with the template's extension added when none was typed.</summary>
    public static string FileName(string typed, ItemTemplate template)
    {
        var name = typed.Trim();
        var baseName = name.Length == 0 ? template.DefaultName : name;
        return Path.HasExtension(baseName) ? baseName : baseName + "." + template.Extension;
    }

    /// <summary>The files to write, Windows line endings: the item and its companion, if any.</summary>
    public static List<(string FileName, string Contents)> Files(ItemTemplate template, string fileName, string projectNamespace)
    {
        var name = ProjectTemplates.Identifier(Path.GetFileNameWithoutExtension(fileName));
        string Fill(string text) => text.Replace("{{name}}", name).Replace("{{namespace}}", projectNamespace).Replace("\r\n", "\n").Replace("\n", "\r\n");
        var files = new List<(string, string)> { (fileName, Fill(template.Contents)) };
        if (template.Companion is { } companion) files.Add((Path.GetFileNameWithoutExtension(fileName) + companion.Suffix, Fill(companion.Contents)));
        return files;
    }
}

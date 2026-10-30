using CodeForge.Core;

namespace CodeForge.Core.Tests;

public class FormDesignTests
{
    // Exactly what `dotnet new winforms -lang VB` and `dotnet new winforms` create.
    private const string VbTemplate = """
        <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            'Form overrides dispose to clean up the component list.
            <System.Diagnostics.DebuggerNonUserCode()>
            Protected Overrides Sub Dispose(disposing As Boolean)
                Try
                    If disposing AndAlso components IsNot Nothing Then
                        components.Dispose()
                    End If
                Finally
                    MyBase.Dispose(disposing)
                End Try
            End Sub

            'Required by the Windows Form Designer
            Private components As System.ComponentModel.IContainer

            'NOTE: The following procedure is required by the Windows Form Designer
            <System.Diagnostics.DebuggerStepThrough()>
            Private Sub InitializeComponent()
                components = New System.ComponentModel.Container()
                AutoScaleMode = AutoScaleMode.Font
                ClientSize = New Size(800, 450)
                Text = "Form1"
            End Sub

        End Class
        """;

    private const string CsTemplate = """
        namespace WinFormsApp1;

        partial class Form1
        {
            private System.ComponentModel.IContainer components = null;

            protected override void Dispose(bool disposing)
            {
                if (disposing && (components != null))
                {
                    components.Dispose();
                }
                base.Dispose(disposing);
            }

            #region Windows Form Designer generated code

            private void InitializeComponent()
            {
                components = new System.ComponentModel.Container();
                AutoScaleMode = AutoScaleMode.Font;
                ClientSize = new Size(800, 450);
                Text = "Form1";
            }

            #endregion
        }
        """;

    [Fact]
    public void ReadsTheTemplates()
    {
        var vb = FormDesign.Parse(VbTemplate, "vb");
        Assert.Equal("Form1", vb.ClassName);
        Assert.True(vb.HasComponents);
        Assert.Empty(vb.Controls);
        Assert.Equal("New Size(800, 450)", vb.FormProperty("ClientSize"));
        Assert.Equal("Form1", vb.Unquote(vb.FormProperty("Text")));
        Assert.True(FormDesign.IsFormDesigner(VbTemplate));
        Assert.True(FormDesign.IsFormDesigner(CsTemplate));
    }

    [Fact]
    public void AddsControlsAndWritesVisualBasic()
    {
        var design = FormDesign.Parse(VbTemplate, "vb");
        var button = design.Add(ControlKinds.Find("Button")!, 20, 30);
        var label = design.Add(ControlKinds.Find("Label")!, 20, 80);
        Assert.Equal(("Button1", "Label1"), (button.Name, label.Name));
        design.SetFormProperty("Text", design.Quote("Sign in"));
        var text = design.Generate();

        Assert.Contains("        Button1 = New System.Windows.Forms.Button()\n", text);
        Assert.Contains("        Button1.Location = New System.Drawing.Point(20, 30)\n", text);
        Assert.Contains("        Button1.Text = \"Button1\"\n", text);
        Assert.Contains("        Label1.AutoSize = True\n", text);
        Assert.Contains("        Controls.Add(Label1)\n        Controls.Add(Button1)\n", text);
        Assert.Contains("        Text = \"Sign in\"\n", text);
        Assert.Contains("        ResumeLayout(False)\n        PerformLayout()\n    End Sub\n\n    Friend WithEvents Button1 As System.Windows.Forms.Button\n    Friend WithEvents Label1 As System.Windows.Forms.Label\n\nEnd Class", text);
        Assert.Contains("Protected Overrides Sub Dispose", text);   // the rest of the file is kept

        // Reading it back gives the same form, and writing it again changes nothing.
        var again = FormDesign.Parse(text, "vb");
        Assert.Equal(["Button1", "Label1"], again.Controls.Select(c => c.Name));
        Assert.Equal((20, 30), FormDesign.Pair(again.Control("button1")!.Get("Location")));
        Assert.Equal(text, again.Generate());
    }

    [Fact]
    public void WritesCSharpWithContainersAndEvents()
    {
        var design = FormDesign.Parse(CsTemplate, "csharp");
        var panel = design.Add(ControlKinds.Find("Panel")!, 10, 10);
        var box = design.Add(ControlKinds.Find("TextBox")!, 5, 5, parent: panel.Name);
        var grid = design.Add(ControlKinds.Find("DataGridView")!, 10, 150);
        Assert.Equal(("panel1", "textBox1"), (panel.Name, box.Name));
        var (code, line) = design.AddHandler("namespace WinFormsApp1;\n\npublic partial class Form1 : Form\n{\n    public Form1()\n    {\n        InitializeComponent();\n    }\n}\n", "textBox1", "TextChanged", "EventArgs");
        var text = design.Generate();

        Assert.Contains("        panel1.SuspendLayout();\n", text);
        Assert.Contains("        ((System.ComponentModel.ISupportInitialize)dataGridView1).BeginInit();\n", text);
        Assert.Contains("        panel1.Controls.Add(textBox1);\n", text);
        Assert.Contains("        textBox1.TextChanged += textBox1_TextChanged;\n", text);
        Assert.Contains("        panel1.ResumeLayout(false);\n", text);
        Assert.Contains("    #endregion\n\n    private System.Windows.Forms.Panel panel1;\n    private System.Windows.Forms.TextBox textBox1;\n    private System.Windows.Forms.DataGridView dataGridView1;\n}", text);
        Assert.DoesNotContain("Controls.Add(textBox1);\n        Controls", text);   // inside the panel, not on the form

        Assert.Contains("    private void textBox1_TextChanged(object sender, EventArgs e)\n    {\n\n    }\n}", code);
        Assert.Equal("", code.Split('\n')[line - 1].Trim());
        Assert.Equal("{", code.Split('\n')[line - 2].Trim());

        var again = FormDesign.Parse(text, "csharp");
        Assert.Equal("panel1", again.Control("textBox1")!.Parent);
        Assert.Equal([("TextChanged", "textBox1_TextChanged")], again.Control("textBox1")!.Events);
        Assert.Equal(text, again.Generate());
        _ = grid;
    }

    [Fact]
    public void ReadsOlderVisualStudioFiles()
    {
        // Me., full type names, line continuations and statements the designer doesn't model.
        const string old = """
            Partial Class LoginForm
                Inherits System.Windows.Forms.Form
                Private Sub InitializeComponent()
                    Me.OkButton = New System.Windows.Forms.Button()
                    Me.UserBox = New System.Windows.Forms.ComboBox()
                    Me.SuspendLayout()
                    '
                    'OkButton
                    '
                    Me.OkButton.Location = New System.Drawing.Point(100, 200)
                    Me.OkButton.Name = "OkButton"
                    Me.OkButton.Text = "&OK"
                    '
                    'UserBox
                    '
                    Me.UserBox.Items.AddRange(New Object() {"Ahmed", _
                        "Guest"})
                    Me.UserBox.Location = New System.Drawing.Point(10, 10)
                    '
                    'LoginForm
                    '
                    Me.ClientSize = New System.Drawing.Size(300, 260)
                    Me.Controls.Add(Me.UserBox)
                    Me.Controls.Add(Me.OkButton)
                    Me.Text = "Sign in"
                    Me.ResumeLayout(False)
                End Sub
                Friend WithEvents OkButton As System.Windows.Forms.Button
                Friend WithEvents UserBox As System.Windows.Forms.ComboBox
            End Class
            """;
        var design = FormDesign.Parse(old, "vb");
        Assert.Equal("LoginForm", design.ClassName);
        Assert.Equal(["OkButton", "UserBox"], design.Controls.Select(c => c.Name));
        Assert.Equal("&OK", design.Unquote(design.Control("OkButton")!.Get("Text")));
        Assert.Single(design.Control("UserBox")!.Extra);
        Assert.StartsWith("UserBox.Items.AddRange(New Object() {\"Ahmed\",", design.Control("UserBox")!.Extra[0]);

        Assert.True(design.Rename("OkButton", "SignInButton"));
        Assert.False(design.Rename("UserBox", "SignInButton"));   // taken
        var text = design.Generate();
        Assert.Contains("SignInButton.Name = \"SignInButton\"", text);
        Assert.Contains("Friend WithEvents SignInButton As System.Windows.Forms.Button", text);
        Assert.DoesNotContain("OkButton", text);
        Assert.Equal(1, text.Split("Friend WithEvents UserBox").Length - 1);
        Assert.Equal("Private Sub X() Handles SignInButton.Click", FormDesign.RenameInCode("Private Sub X() Handles OkButton.Click", "vb", "OkButton", "SignInButton"));
    }

    [Fact]
    public void AddsVisualBasicHandlersOnce()
    {
        var design = FormDesign.Parse(VbTemplate, "vb");
        var code = "Public Class Form1\n\nEnd Class\n";
        var (withClick, line) = design.AddHandler(code, "Button1", "Click", "EventArgs");
        Assert.Equal("Public Class Form1\n\n    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click\n\n    End Sub\nEnd Class\n", withClick);
        Assert.Equal("", withClick.Split('\n')[line - 1]);
        Assert.Equal(withClick, design.AddHandler(withClick, "Button1", "Click", "EventArgs").Code);   // already there
        var (withLoad, _) = design.AddHandler(withClick, null, "Load", "EventArgs");
        Assert.Contains("Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load", withLoad);
    }

    [Fact]
    public void RemovesContainersWithTheirChildren()
    {
        var design = FormDesign.Parse(CsTemplate, "csharp");
        var group = design.Add(ControlKinds.Find("GroupBox")!, 0, 0);
        design.Add(ControlKinds.Find("RadioButton")!, 5, 20, group.Name);
        design.Add(ControlKinds.Find("Button")!, 300, 0);
        design.Remove(group.Name);
        Assert.Equal(["button1"], design.Controls.Select(c => c.Name));
        Assert.Equal("button1", design.UniqueName("Button")[..^1] + "1");
    }
}

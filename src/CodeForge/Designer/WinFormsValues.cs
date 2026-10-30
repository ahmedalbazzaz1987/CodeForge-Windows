using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using CodeForge.Core;
using WinForms = System.Windows.Forms;

namespace CodeForge.Designer;

/// <summary>
/// Turns the code expressions in InitializeComponent into real values to apply to controls (to draw them), and values
/// changed in the Properties window back into code. Handles the types the designer writes: strings, numbers, booleans,
/// enums (and flags), Point, Size, SizeF, Padding, Color and Font.
/// </summary>
public static partial class WinFormsValues
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static object? Evaluate(string expression, Type type, FormDesign design)
    {
        var text = expression.Trim();
        try
        {
            if (type == typeof(string)) return design.Unquote(text);
            if (type == typeof(bool)) return text.Equals("true", StringComparison.OrdinalIgnoreCase) ? true : text.Equals("false", StringComparison.OrdinalIgnoreCase) ? false : null;
            if (type.IsEnum) return EvaluateEnum(text, type);
            if (Number(text) is { } number)
            {
                if (type == typeof(int)) return (int)number;
                if (type == typeof(float)) return (float)number;
                if (type == typeof(double)) return number;
                if (type == typeof(decimal)) return (decimal)number;
                if (type == typeof(long)) return (long)number;
                if (type == typeof(short)) return (short)number;
                if (type == typeof(byte)) return (byte)number;
            }
            var numbers = Numbers(text);
            if (type == typeof(Point) && numbers.Count == 2) return new Point((int)numbers[0], (int)numbers[1]);
            if (type == typeof(Size) && numbers.Count == 2) return new Size((int)numbers[0], (int)numbers[1]);
            if (type == typeof(SizeF) && numbers.Count == 2) return new SizeF((float)numbers[0], (float)numbers[1]);
            if (type == typeof(WinForms.Padding))
                return numbers.Count switch { 1 => new WinForms.Padding((int)numbers[0]), 4 => new WinForms.Padding((int)numbers[0], (int)numbers[1], (int)numbers[2], (int)numbers[3]), _ => null };
            if (type == typeof(Color)) return EvaluateColor(text, numbers);
            if (type == typeof(Font)) return EvaluateFont(text, design);
        }
        catch (Exception) { }   // an expression CodeForge can't read is just not drawn
        return null;
    }

    /// <summary>Code for a value set in the Properties window, or null when the designer can't write that type yet.</summary>
    public static string? Serialize(object? value, FormDesign design)
    {
        var vb = design.Language == "vb";
        string New(string type, string arguments) => $"{(vb ? "New" : "new")} {type}({arguments})";
        string F(double v) => v.ToString("0.0###", Invariant) + "F";
        switch (value)
        {
            case null: return null;
            case string s: return design.Quote(s);
            case bool b: return design.Bool(b);
            case Enum e: return SerializeEnum(e, vb);
            case int or long or short or byte: return Convert.ToString(value, Invariant);
            case float f: return F(f);
            case double d: return d.ToString("0.0###", Invariant) + (vb ? "R" : "D");
            case decimal m: return m.ToString(Invariant) + (vb ? "D" : "M");
            case Point p: return New("System.Drawing.Point", $"{p.X}, {p.Y}");
            case Size z: return New("System.Drawing.Size", $"{z.Width}, {z.Height}");
            case SizeF z: return New("System.Drawing.SizeF", $"{F(z.Width)}, {F(z.Height)}");
            case WinForms.Padding p:
                return p.All >= 0 ? New("System.Windows.Forms.Padding", p.All.ToString()) : New("System.Windows.Forms.Padding", $"{p.Left}, {p.Top}, {p.Right}, {p.Bottom}");
            case Color c:
                if (c.IsSystemColor) return "System.Drawing.SystemColors." + c.Name;
                if (c.IsNamedColor) return "System.Drawing.Color." + c.Name;
                return $"System.Drawing.Color.FromArgb({(c.A != 255 ? c.A + ", " : "")}{c.R}, {c.G}, {c.B})";
            case Font font:
                var style = font.Style == FontStyle.Regular ? "" : ", " + SerializeEnum(font.Style, vb);
                return New("System.Drawing.Font", $"{design.Quote(font.Name)}, {F(font.Size)}{style}");
            default: return null;
        }
    }

    private static string SerializeEnum(Enum value, bool vb)
    {
        var type = value.GetType();
        var typeName = type.FullName!.Replace('+', '.');
        if (type.IsDefined(typeof(FlagsAttribute), false) && !Enum.IsDefined(type, value))
        {
            var parts = Enum.GetValues(type).Cast<Enum>().Where(v => Convert.ToInt64(v) != 0 && value.HasFlag(v)).Select(v => $"{typeName}.{v}");
            var joined = string.Join(vb ? " Or " : " | ", parts);
            return parts.Count() > 1 ? $"({joined})" : joined;
        }
        return $"{typeName}.{value}";
    }

    private static object? EvaluateEnum(string text, Type type)
    {
        long result = 0;
        var found = false;
        foreach (var part in OrSplit().Split(text.Trim('(', ')', ' ')))
        {
            var name = part.Trim().Trim('(', ')').Split('.')[^1];
            if (name.Length == 0) continue;
            if (!Enum.TryParse(type, name, ignoreCase: true, out var value) || value == null) return null;
            result |= Convert.ToInt64(value);
            found = true;
        }
        return found ? Enum.ToObject(type, result) : null;
    }

    private static Color? EvaluateColor(string text, List<double> numbers)
    {
        if (text.Contains("FromArgb", StringComparison.OrdinalIgnoreCase))
        {
            var ints = numbers.Select(n => (int)n).ToList();
            return ints.Count switch { 3 => Color.FromArgb(ints[0], ints[1], ints[2]), 4 => Color.FromArgb(ints[0], ints[1], ints[2], ints[3]), 1 => Color.FromArgb(ints[0]), _ => null };
        }
        var name = text.Split('.')[^1];
        var color = Color.FromName(name);
        return color.IsKnownColor ? color : null;
    }

    private static Font? EvaluateFont(string text, FormDesign design)
    {
        var family = Regex.Match(text, "\"((?:[^\"]|\"\")*)\"");
        if (!family.Success) return null;
        var afterName = text[(family.Index + family.Length)..];
        var size = Regex.Match(afterName, @",\s*(-?\d+(?:\.\d+)?)");
        if (!size.Success) return null;
        var style = FontStyle.Regular;
        var styleText = Regex.Match(afterName, @"FontStyle\.\w+(?:\s*(?:Or|\|)\s*(?:[\w.]+\.)?FontStyle\.\w+)*");
        if (styleText.Success && EvaluateEnum(styleText.Value, typeof(FontStyle)) is FontStyle parsed) style = parsed;
        return new Font(design.Unquote(family.Value) ?? "Segoe UI", float.Parse(size.Groups[1].Value, Invariant), style);
    }

    /// <summary>A plain number literal, with VB.NET or C# type suffixes (8.0!, 12F, 1.5D, 3M…).</summary>
    private static double? Number(string text)
    {
        var match = Regex.Match(text, @"^-?\d+(?:\.\d+)?(?=[!#@%&FfDdMmRrLlSsUuIi]*$)");
        return match.Success ? double.Parse(match.Value, Invariant) : null;
    }

    /// <summary>The numbers in an expression like New Point(12, 34) or Color.FromArgb(CType(255, Byte), 0, 0).</summary>
    private static List<double> Numbers(string text) =>
        NumberPattern().Matches(text).Select(m => double.Parse(m.Value.TrimEnd('!', 'F', 'f', 'D', 'd', 'R', 'r'), Invariant)).ToList();

    [GeneratedRegex(@"(?<![\w.])-?\d+(?:\.\d+)?[!FfDdRr]?(?![\w])")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\s+Or\s+|\s*\|\s*", RegexOptions.IgnoreCase)]
    private static partial Regex OrSplit();
}

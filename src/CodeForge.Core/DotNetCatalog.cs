using System.Text.RegularExpressions;

namespace CodeForge.Core;

/// <summary>A .NET member with its VB.NET signature: <c>WriteLine(value As String)</c> or <c>Length As Integer</c>.</summary>
public sealed record DotNetMember(string Name, bool IsShared, bool IsMethod, string Signature, string? ReturnType);

public sealed record DotNetType(string Name, string Namespace, SymbolKind Kind, List<DotNetMember> Members)
{
    public string QualifiedName => Namespace + "." + Name;
}

/// <summary>
/// The .NET classes programs use most, with VB.NET signatures (the same catalog as the Mac version). It gives VB.NET,
/// which has no language server, member lists, Quick Info and Go To Definition.
/// </summary>
public static class DotNetCatalog
{
    // `+` marks Shared members, `.` marks properties; a member without `(` is a property or field.
    private static readonly (string Namespace, string Name, SymbolKind Kind, string Members)[] Source =
    [
        ("System", "Console", SymbolKind.Class, "+WriteLine(value As String) | +Write(value As String) | +ReadLine() As String | +ReadKey() As ConsoleKeyInfo | +Clear() | +Beep() | +ResetColor() | +SetCursorPosition(left As Integer, top As Integer) | +.ForegroundColor As ConsoleColor | +.BackgroundColor As ConsoleColor | +.Title As String | +.CursorLeft As Integer | +.CursorTop As Integer | +.KeyAvailable As Boolean | +.WindowWidth As Integer | +.Out As TextWriter | +.Error As TextWriter"),
        ("System", "String", SymbolKind.Class, "Length As Integer | ToUpper() As String | ToLower() As String | Trim() As String | TrimStart() As String | TrimEnd() As String | Substring(startIndex As Integer, length As Integer) As String | Replace(oldValue As String, newValue As String) As String | Split(separator As Char) As String() | Contains(value As String) As Boolean | StartsWith(value As String) As Boolean | EndsWith(value As String) As Boolean | IndexOf(value As String) As Integer | LastIndexOf(value As String) As Integer | PadLeft(totalWidth As Integer) As String | PadRight(totalWidth As Integer) As String | Insert(startIndex As Integer, value As String) As String | Remove(startIndex As Integer) As String | ToCharArray() As Char() | CompareTo(value As String) As Integer | Chars(index As Integer) As Char | +Format(format As String, ParamArray args As Object()) As String | +Join(separator As String, values As IEnumerable(Of String)) As String | +Concat(ParamArray values As String()) As String | +IsNullOrEmpty(value As String) As Boolean | +IsNullOrWhiteSpace(value As String) As Boolean | +Compare(strA As String, strB As String) As Integer | +.Empty As String"),
        ("System", "Math", SymbolKind.Class, "+Abs(value As Double) As Double | +Max(val1 As Double, val2 As Double) As Double | +Min(val1 As Double, val2 As Double) As Double | +Pow(x As Double, y As Double) As Double | +Sqrt(d As Double) As Double | +Round(value As Double, digits As Integer) As Double | +Floor(d As Double) As Double | +Ceiling(d As Double) As Double | +Truncate(d As Double) As Double | +Sign(value As Double) As Integer | +Clamp(value As Double, min As Double, max As Double) As Double | +Sin(a As Double) As Double | +Cos(d As Double) As Double | +Tan(a As Double) As Double | +Log(d As Double) As Double | +Log10(d As Double) As Double | +Exp(d As Double) As Double | +.PI As Double | +.E As Double"),
        ("System", "DateTime", SymbolKind.Structure, "+.Now As DateTime | +.Today As DateTime | +.UtcNow As DateTime | +Parse(s As String) As DateTime | +TryParse(s As String, ByRef result As DateTime) As Boolean | +DaysInMonth(year As Integer, month As Integer) As Integer | +IsLeapYear(year As Integer) As Boolean | AddDays(value As Double) As DateTime | AddHours(value As Double) As DateTime | AddMinutes(value As Double) As DateTime | AddSeconds(value As Double) As DateTime | AddMonths(months As Integer) As DateTime | AddYears(value As Integer) As DateTime | Subtract(value As DateTime) As TimeSpan | ToShortDateString() As String | ToLongDateString() As String | ToShortTimeString() As String | Year As Integer | Month As Integer | Day As Integer | Hour As Integer | Minute As Integer | Second As Integer | Millisecond As Integer | DayOfWeek As DayOfWeek | DayOfYear As Integer | Date As DateTime | TimeOfDay As TimeSpan"),
        ("System", "TimeSpan", SymbolKind.Structure, "+FromSeconds(value As Double) As TimeSpan | +FromMinutes(value As Double) As TimeSpan | +FromHours(value As Double) As TimeSpan | +FromDays(value As Double) As TimeSpan | +FromMilliseconds(value As Double) As TimeSpan | +.Zero As TimeSpan | Days As Integer | Hours As Integer | Minutes As Integer | Seconds As Integer | Milliseconds As Integer | TotalDays As Double | TotalHours As Double | TotalMinutes As Double | TotalSeconds As Double | TotalMilliseconds As Double | Add(ts As TimeSpan) As TimeSpan"),
        ("System", "Int32", SymbolKind.Structure, "+Parse(s As String) As Integer | +TryParse(s As String, ByRef result As Integer) As Boolean | +.MaxValue As Integer | +.MinValue As Integer | CompareTo(value As Integer) As Integer"),
        ("System", "Int64", SymbolKind.Structure, "+Parse(s As String) As Long | +TryParse(s As String, ByRef result As Long) As Boolean | +.MaxValue As Long | +.MinValue As Long"),
        ("System", "Double", SymbolKind.Structure, "+Parse(s As String) As Double | +TryParse(s As String, ByRef result As Double) As Boolean | +IsNaN(d As Double) As Boolean | +IsInfinity(d As Double) As Boolean | +.MaxValue As Double | +.MinValue As Double | +.NaN As Double | +.Epsilon As Double"),
        ("System", "Decimal", SymbolKind.Structure, "+Parse(s As String) As Decimal | +TryParse(s As String, ByRef result As Decimal) As Boolean | +Round(d As Decimal, decimals As Integer) As Decimal | +.MaxValue As Decimal | +.Zero As Decimal"),
        ("System", "Boolean", SymbolKind.Structure, "+Parse(value As String) As Boolean | +TryParse(value As String, ByRef result As Boolean) As Boolean | +.TrueString As String | +.FalseString As String"),
        ("System", "Char", SymbolKind.Structure, "+IsDigit(c As Char) As Boolean | +IsLetter(c As Char) As Boolean | +IsLetterOrDigit(c As Char) As Boolean | +IsWhiteSpace(c As Char) As Boolean | +IsUpper(c As Char) As Boolean | +IsLower(c As Char) As Boolean | +IsPunctuation(c As Char) As Boolean | +ToUpper(c As Char) As Char | +ToLower(c As Char) As Char"),
        ("System", "Convert", SymbolKind.Class, "+ToInt32(value As Object) As Integer | +ToInt64(value As Object) As Long | +ToDouble(value As Object) As Double | +ToDecimal(value As Object) As Decimal | +ToString(value As Object) As String | +ToBoolean(value As Object) As Boolean | +ToDateTime(value As Object) As DateTime | +ToChar(value As Object) As Char | +ToBase64String(inArray As Byte()) As String | +FromBase64String(s As String) As Byte()"),
        ("System", "Random", SymbolKind.Class, "Next() As Integer | Next(maxValue As Integer) As Integer | Next(minValue As Integer, maxValue As Integer) As Integer | NextDouble() As Double | NextBytes(buffer As Byte()) | +.Shared As Random"),
        ("System", "Guid", SymbolKind.Structure, "+NewGuid() As Guid | +Parse(input As String) As Guid | +TryParse(input As String, ByRef result As Guid) As Boolean | +.Empty As Guid"),
        ("System", "Environment", SymbolKind.Class, "+.NewLine As String | +.CurrentDirectory As String | +.MachineName As String | +.UserName As String | +.OSVersion As OperatingSystem | +.ProcessorCount As Integer | +.TickCount As Integer | +.Version As Version | +Exit(exitCode As Integer) | +GetEnvironmentVariable(variable As String) As String | +GetCommandLineArgs() As String() | +GetFolderPath(folder As SpecialFolder) As String"),
        ("System", "Array", SymbolKind.Class, "Length As Integer | Rank As Integer | GetValue(index As Integer) As Object | SetValue(value As Object, index As Integer) | +Sort(array As Array) | +Reverse(array As Array) | +IndexOf(array As Array, value As Object) As Integer | +Resize(ByRef array As T(), newSize As Integer) | +Copy(sourceArray As Array, destinationArray As Array, length As Integer) | +Clear(array As Array) | +Empty() As T() | +Exists(array As T(), match As Predicate(Of T)) As Boolean | +Find(array As T(), match As Predicate(Of T)) As T"),
        ("System", "Exception", SymbolKind.Class, "Message As String | StackTrace As String | InnerException As Exception | Source As String | Data As IDictionary | HResult As Integer | GetBaseException() As Exception"),
        ("System", "Object", SymbolKind.Class, "ToString() As String | Equals(obj As Object) As Boolean | GetHashCode() As Integer | GetType() As Type"),
        ("System.Collections.Generic", "List", SymbolKind.Class, "Add(item As T) | AddRange(collection As IEnumerable(Of T)) | Remove(item As T) As Boolean | RemoveAt(index As Integer) | RemoveAll(match As Predicate(Of T)) As Integer | Clear() | Contains(item As T) As Boolean | IndexOf(item As T) As Integer | Insert(index As Integer, item As T) | Sort() | Reverse() | ToArray() As T() | Find(match As Predicate(Of T)) As T | FindAll(match As Predicate(Of T)) As List(Of T) | FindIndex(match As Predicate(Of T)) As Integer | ForEach(action As Action(Of T)) | Exists(match As Predicate(Of T)) As Boolean | Count As Integer | Capacity As Integer | Item(index As Integer) As T"),
        ("System.Collections.Generic", "Dictionary", SymbolKind.Class, "Add(key As TKey, value As TValue) | Remove(key As TKey) As Boolean | ContainsKey(key As TKey) As Boolean | ContainsValue(value As TValue) As Boolean | TryGetValue(key As TKey, ByRef value As TValue) As Boolean | TryAdd(key As TKey, value As TValue) As Boolean | Clear() | Count As Integer | Keys As KeyCollection | Values As ValueCollection | Item(key As TKey) As TValue"),
        ("System.Collections.Generic", "HashSet", SymbolKind.Class, "Add(item As T) As Boolean | Remove(item As T) As Boolean | Contains(item As T) As Boolean | Clear() | UnionWith(other As IEnumerable(Of T)) | IntersectWith(other As IEnumerable(Of T)) | ExceptWith(other As IEnumerable(Of T)) | Count As Integer"),
        ("System.Collections.Generic", "Queue", SymbolKind.Class, "Enqueue(item As T) | Dequeue() As T | Peek() As T | TryDequeue(ByRef result As T) As Boolean | Contains(item As T) As Boolean | Clear() | Count As Integer"),
        ("System.Collections.Generic", "Stack", SymbolKind.Class, "Push(item As T) | Pop() As T | Peek() As T | TryPop(ByRef result As T) As Boolean | Contains(item As T) As Boolean | Clear() | Count As Integer"),
        ("System.Text", "StringBuilder", SymbolKind.Class, "Append(value As String) As StringBuilder | AppendLine(value As String) As StringBuilder | AppendFormat(format As String, ParamArray args As Object()) As StringBuilder | Insert(index As Integer, value As String) As StringBuilder | Remove(startIndex As Integer, length As Integer) As StringBuilder | Replace(oldValue As String, newValue As String) As StringBuilder | Clear() As StringBuilder | ToString() As String | Length As Integer | Capacity As Integer"),
        ("System.Text", "Encoding", SymbolKind.Class, "+.UTF8 As Encoding | +.ASCII As Encoding | +.Unicode As Encoding | GetBytes(s As String) As Byte() | GetString(bytes As Byte()) As String"),
        ("System.IO", "File", SymbolKind.Class, "+ReadAllText(path As String) As String | +WriteAllText(path As String, contents As String) | +AppendAllText(path As String, contents As String) | +ReadAllLines(path As String) As String() | +WriteAllLines(path As String, contents As String()) | +ReadAllBytes(path As String) As Byte() | +WriteAllBytes(path As String, bytes As Byte()) | +Exists(path As String) As Boolean | +Delete(path As String) | +Copy(sourceFileName As String, destFileName As String) | +Move(sourceFileName As String, destFileName As String) | +ReadLines(path As String) As IEnumerable(Of String) | +OpenText(path As String) As StreamReader | +CreateText(path As String) As StreamWriter | +GetLastWriteTime(path As String) As DateTime"),
        ("System.IO", "Directory", SymbolKind.Class, "+Exists(path As String) As Boolean | +CreateDirectory(path As String) As DirectoryInfo | +Delete(path As String, recursive As Boolean) | +GetFiles(path As String) As String() | +GetDirectories(path As String) As String() | +EnumerateFiles(path As String) As IEnumerable(Of String) | +GetCurrentDirectory() As String | +SetCurrentDirectory(path As String) | +Move(sourceDirName As String, destDirName As String)"),
        ("System.IO", "Path", SymbolKind.Class, "+Combine(ParamArray paths As String()) As String | +GetFileName(path As String) As String | +GetFileNameWithoutExtension(path As String) As String | +GetExtension(path As String) As String | +GetDirectoryName(path As String) As String | +GetFullPath(path As String) As String | +GetTempPath() As String | +GetTempFileName() As String | +ChangeExtension(path As String, extension As String) As String | +.DirectorySeparatorChar As Char"),
        ("System.IO", "StreamReader", SymbolKind.Class, "ReadLine() As String | ReadToEnd() As String | Read() As Integer | Peek() As Integer | Close() | Dispose() | EndOfStream As Boolean"),
        ("System.IO", "StreamWriter", SymbolKind.Class, "Write(value As String) | WriteLine(value As String) | Flush() | Close() | Dispose() | AutoFlush As Boolean"),
        ("System.Threading", "Thread", SymbolKind.Class, "+Sleep(millisecondsTimeout As Integer) | +.CurrentThread As Thread | Start() | Join() | Name As String | IsAlive As Boolean | IsBackground As Boolean | ManagedThreadId As Integer"),
        ("System.Threading.Tasks", "Task", SymbolKind.Class, "+Run(action As Action) As Task | +Delay(millisecondsDelay As Integer) As Task | +WhenAll(ParamArray tasks As Task()) As Task | +WhenAny(ParamArray tasks As Task()) As Task(Of Task) | +FromResult(result As T) As Task(Of T) | +.CompletedTask As Task | Wait() | ContinueWith(continuationAction As Action(Of Task)) As Task | IsCompleted As Boolean | IsFaulted As Boolean | Status As TaskStatus | Result As T"),
        ("System.Diagnostics", "Stopwatch", SymbolKind.Class, "+StartNew() As Stopwatch | Start() | Stop() | Reset() | Restart() | Elapsed As TimeSpan | ElapsedMilliseconds As Long | IsRunning As Boolean"),
        ("System.Diagnostics", "Debug", SymbolKind.Class, "+WriteLine(message As String) | +Write(message As String) | +Assert(condition As Boolean) | +Fail(message As String)"),
        ("System.Diagnostics", "Process", SymbolKind.Class, "+Start(fileName As String) As Process | +GetCurrentProcess() As Process | +GetProcesses() As Process() | Kill() | WaitForExit() | Id As Integer | ProcessName As String | HasExited As Boolean | ExitCode As Integer | StandardOutput As StreamReader"),
        ("System.Net.Http", "HttpClient", SymbolKind.Class, "GetAsync(requestUri As String) As Task(Of HttpResponseMessage) | GetStringAsync(requestUri As String) As Task(Of String) | PostAsync(requestUri As String, content As HttpContent) As Task(Of HttpResponseMessage) | PutAsync(requestUri As String, content As HttpContent) As Task(Of HttpResponseMessage) | DeleteAsync(requestUri As String) As Task(Of HttpResponseMessage) | SendAsync(request As HttpRequestMessage) As Task(Of HttpResponseMessage) | BaseAddress As Uri | DefaultRequestHeaders As HttpRequestHeaders | Timeout As TimeSpan | Dispose()"),
        ("System.Net.Http", "HttpResponseMessage", SymbolKind.Class, "EnsureSuccessStatusCode() As HttpResponseMessage | IsSuccessStatusCode As Boolean | StatusCode As HttpStatusCode | Content As HttpContent | Headers As HttpResponseHeaders | ReasonPhrase As String"),
        ("System.Linq", "Enumerable", SymbolKind.Class, "+Range(start As Integer, count As Integer) As IEnumerable(Of Integer) | +Repeat(element As T, count As Integer) As IEnumerable(Of T) | +Empty() As IEnumerable(Of T)"),
        ("System.Text.RegularExpressions", "Regex", SymbolKind.Class, "+IsMatch(input As String, pattern As String) As Boolean | +Match(input As String, pattern As String) As Match | +Matches(input As String, pattern As String) As MatchCollection | +Replace(input As String, pattern As String, replacement As String) As String | +Split(input As String, pattern As String) As String() | +Escape(str As String) As String"),
        ("System.Text.Json", "JsonSerializer", SymbolKind.Class, "+Serialize(value As Object) As String | +Deserialize(Of T)(json As String) As T"),
        ("System.Windows.Forms", "MessageBox", SymbolKind.Class, "+Show(text As String) As DialogResult | +Show(text As String, caption As String) As DialogResult | +Show(text As String, caption As String, buttons As MessageBoxButtons, icon As MessageBoxIcon) As DialogResult"),
        ("System.Windows.Forms", "Application", SymbolKind.Class, "+Run(mainForm As Form) | +Exit() | +EnableVisualStyles() | +DoEvents() | +.StartupPath As String | +.ProductName As String"),
        ("Microsoft.VisualBasic", "Strings", SymbolKind.Module, "+Len(str As String) As Integer | +Left(str As String, length As Integer) As String | +Right(str As String, length As Integer) As String | +Mid(str As String, start As Integer, length As Integer) As String | +UCase(str As String) As String | +LCase(str As String) As String | +Trim(str As String) As String | +InStr(string1 As String, string2 As String) As Integer | +Replace(expression As String, find As String, replacement As String) As String | +Split(expression As String, delimiter As String) As String() | +Join(sourceArray As String(), delimiter As String) As String | +Format(expression As Object, style As String) As String | +StrReverse(expression As String) As String | +Asc(c As Char) As Integer | +Chr(charCode As Integer) As Char"),
        ("Microsoft.VisualBasic", "Interaction", SymbolKind.Module, "+MsgBox(prompt As Object) As MsgBoxResult | +InputBox(prompt As String) As String | +IIf(expression As Boolean, truePart As Object, falsePart As Object) As Object | +Beep() | +Shell(pathName As String) As Integer"),
        ("Microsoft.VisualBasic", "Conversion", SymbolKind.Module, "+Val(expression As String) As Double | +Str(number As Object) As String | +Int(number As Double) As Double | +Fix(number As Double) As Double | +Hex(number As Object) As String | +Oct(number As Object) As String"),
        ("Microsoft.VisualBasic", "DateAndTime", SymbolKind.Module, "+.Now As DateTime | +.Today As DateTime | +.TimeOfDay As DateTime | +DateAdd(interval As DateInterval, number As Double, dateValue As DateTime) As DateTime | +DateDiff(interval As DateInterval, date1 As DateTime, date2 As DateTime) As Long | +Year(dateValue As DateTime) As Integer | +Month(dateValue As DateTime) As Integer | +Day(dateValue As DateTime) As Integer"),
    ];

    /// <summary>LINQ, available on every collection.</summary>
    private const string Linq = "Where(predicate As Func(Of T, Boolean)) As IEnumerable(Of T) | Select(selector As Func(Of T, TResult)) As IEnumerable(Of TResult) | OrderBy(keySelector As Func(Of T, TKey)) As IOrderedEnumerable(Of T) | OrderByDescending(keySelector As Func(Of T, TKey)) As IOrderedEnumerable(Of T) | First() As T | FirstOrDefault() As T | Last() As T | LastOrDefault() As T | Single() As T | Any() As Boolean | All(predicate As Func(Of T, Boolean)) As Boolean | Count() As Integer | Sum() As Double | Average() As Double | Max() As T | Min() As T | ToList() As List(Of T) | ToArray() As T() | ToDictionary(keySelector As Func(Of T, TKey)) As Dictionary(Of TKey, T) | Distinct() As IEnumerable(Of T) | Take(count As Integer) As IEnumerable(Of T) | Skip(count As Integer) As IEnumerable(Of T) | GroupBy(keySelector As Func(Of T, TKey)) As IEnumerable(Of IGrouping(Of TKey, T)) | Aggregate(func As Func(Of T, T, T)) As T | Concat(second As IEnumerable(Of T)) As IEnumerable(Of T) | Reverse() As IEnumerable(Of T) | SequenceEqual(second As IEnumerable(Of T)) As Boolean";

    private const string ObjectMembers = "ToString() As String | Equals(obj As Object) As Boolean | GetHashCode() As Integer | GetType() As Type";

    private static readonly HashSet<string> Collections = ["List", "Dictionary", "HashSet", "Queue", "Stack", "Array", "String"];

    public static readonly IReadOnlyList<DotNetType> Types = Source.Select(entry =>
    {
        var list = Parse(entry.Members);
        if (entry.Kind is SymbolKind.Class or SymbolKind.Structure && entry.Name != "Object") list.AddRange(Parse(ObjectMembers));
        if (Collections.Contains(entry.Name)) list.AddRange(Parse(Linq).Where(member => !list.Any(m => m.Name == member.Name)));
        return new DotNetType(entry.Name, entry.Namespace, entry.Kind, list);
    }).ToList();

    public static readonly IReadOnlyList<string> Namespaces = Types
        .SelectMany(t => Enumerable.Range(1, t.Namespace.Split('.').Length).Select(n => string.Join(".", t.Namespace.Split('.').Take(n))))
        .Distinct().Order().ToList();

    /// <summary>VB.NET and C# type keywords, and the .NET types they stand for.</summary>
    public static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>
    {
        ["integer"] = "Int32", ["int"] = "Int32", ["long"] = "Int64", ["short"] = "Int16", ["double"] = "Double", ["single"] = "Single",
        ["float"] = "Single", ["decimal"] = "Decimal", ["boolean"] = "Boolean", ["bool"] = "Boolean", ["char"] = "Char", ["string"] = "String",
        ["date"] = "DateTime", ["object"] = "Object", ["byte"] = "Byte", ["int32"] = "Int32", ["int64"] = "Int64",
    };

    /// <summary>A type by name, ignoring case, generic arguments and array brackets: "List(Of String)", "list&lt;int&gt;", "Integer".</summary>
    public static DotNetType? Type(string raw)
    {
        var name = BaseName(raw);
        if (name.EndsWith("()") || name.EndsWith("[]")) return Types.FirstOrDefault(t => t.Name == "Array");
        var resolved = Aliases.GetValueOrDefault(name.ToLowerInvariant()) ?? name;
        var shortName = resolved.Split('.')[^1];
        return Types.FirstOrDefault(t => t.Name.Equals(shortName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"List(Of String)" → "List", "Dictionary&lt;string, int&gt;" → "Dictionary"; "String()" stays an array.</summary>
    public static string BaseName(string raw)
    {
        var name = raw.Trim();
        if (name.StartsWith("New ", StringComparison.OrdinalIgnoreCase)) name = name[4..];
        var of = name.IndexOf("(Of ", StringComparison.OrdinalIgnoreCase);
        if (of >= 0) name = name[..of];
        var angle = name.IndexOf('<');
        if (angle >= 0) name = name[..angle];
        return name.Trim();
    }

    private static List<DotNetMember> Parse(string text)
    {
        var result = new List<DotNetMember>();
        foreach (var raw in text.Split(" | "))
        {
            var entry = raw.Trim();
            if (entry.Length == 0) continue;
            var isShared = entry.StartsWith('+');
            if (isShared) entry = entry[1..];
            if (entry.StartsWith('.')) entry = entry[1..];
            var paren = entry.IndexOf('(');
            var asIndex = entry.IndexOf(" As ", StringComparison.Ordinal);
            var isMethod = paren >= 0 && (asIndex < 0 || paren < asIndex);
            var name = new string(entry.TakeWhile(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            string? returns = null;
            if (isMethod)
            {
                // Skip the parameter list (and a leading `(Of T)`); what follows " As " is the return type.
                var rest = entry[paren..];
                while (rest.StartsWith('('))
                {
                    var depth = 0;
                    var end = 0;
                    for (var i = 0; i < rest.Length; i++)
                    {
                        if (rest[i] == '(') depth++;
                        if (rest[i] == ')' && --depth == 0) { end = i; break; }
                    }
                    rest = rest[(end + 1)..];
                }
                if (rest.StartsWith(" As ")) returns = rest[4..];
            }
            else if (asIndex >= 0)
            {
                returns = entry[(asIndex + 4)..];
            }
            result.Add(new DotNetMember(name, isShared, isMethod, entry, returns));
        }
        return result;
    }
}

/// <summary>
/// IntelliSense for VB.NET (and C# without its server) from the project's symbols and the .NET catalog: what follows
/// a dot, Quick Info and Go To Definition. Ported from the Mac version's BasicIntelliSense.
/// </summary>
public static class DotNetIntelliSense
{
    private abstract record Target;
    private sealed record Instance(string Type) : Target;
    private sealed record TypeTarget(string Type) : Target;
    private sealed record NamespaceTarget(string Name) : Target;

    public sealed record Local(string Name, string? Type, int Line);

    public static bool Supports(string languageId) => languageId is "vb" or "csharp";

    /// <summary>Completion entries at an offset: members after a dot, namespaces after Imports, types after As, or names.</summary>
    public static List<CompletionEntry>? Completions(string text, int offset, string languageId, IReadOnlyList<CodeSymbol> symbols)
    {
        offset = Math.Min(offset, text.Length);
        var start = BasicIntelliSense.WordStart(text, offset);
        var lineStart = start > 0 ? text.LastIndexOf('\n', start - 1) + 1 : 0;
        var before = text[lineStart..start];
        var items = new List<CompletionEntry>();
        CompletionEntry Entry(string label, string detail, int kind) => new(label, detail, kind, label, label, label.ToLowerInvariant());
        if (before.EndsWith('.'))
        {
            var expression = MemberExpression(before[..^1]);
            return Members(expression, text, start, languageId, symbols).Select(i => Entry(i.Label, i.Detail, i.Kind)).ToList();
        }
        if (Regex.IsMatch(before, @"^\s*(?:Imports|using)\s+[\w.]*$", RegexOptions.IgnoreCase))
            return DotNetCatalog.Namespaces.Select(n => Entry(n, "Namespace", 9)).ToList();
        if (languageId == "vb" && Regex.IsMatch(before, @"\b(?:As|Of|Inherits|Implements)\s+(?:New\s+)?$", RegexOptions.IgnoreCase))
        {
            items.AddRange(new[] { "Boolean", "Byte", "Char", "Date", "Decimal", "Double", "Integer", "Long", "Object", "Short", "Single", "String" }.Select(k => Entry(k, "Type", 7)));
            items.AddRange(symbols.Where(s => s.IsType).Select(s => Entry(s.Name, $"{s.KindTitle} {s.QualifiedName}", s.CompletionKind)));
            items.AddRange(DotNetCatalog.Types.Select(t => Entry(t.Name, $"{t.Kind} {t.QualifiedName}", KindNumber(t.Kind))));
            return items;
        }
        // Not after a dot: locals, members of this type and modules, project types and the catalog's types
        // (BasicIntelliSense adds keywords, snippets and words).
        foreach (var local in Locals(text, start, languageId)) items.Add(Entry(local.Name, local.Type ?? "Local variable", 6));
        var container = EnclosingType(start, text, symbols, languageId);
        foreach (var symbol in symbols.Where(s => !s.IsType && s.Kind != SymbolKind.Namespace))
        {
            if (symbol.Container == container || symbol.Container == null ||
                (languageId == "vb" && symbols.Any(m => m.Kind == SymbolKind.Module && m.Name == symbol.Container)))
                items.Add(Entry(symbol.Name, symbol.Signature, symbol.CompletionKind));
        }
        items.AddRange(symbols.Where(s => s.IsType).Select(s => Entry(s.Name, $"{s.KindTitle} {s.QualifiedName}", s.CompletionKind)));
        items.AddRange(DotNetCatalog.Types.Select(t => Entry(t.Name, $"{t.Kind} {t.QualifiedName}", KindNumber(t.Kind))));
        return items;
    }

    private static int KindNumber(SymbolKind kind) => new CodeSymbol("", kind, null, null, "", null, false, 0, null).CompletionKind;

    private static List<(string Label, string Detail, int Kind)> Members(List<string> expression, string text, int offset, string languageId, IReadOnlyList<CodeSymbol> symbols)
    {
        var items = new List<(string Label, string Detail, int Kind)>();
        switch (Resolve(expression, text, offset, languageId, symbols))
        {
            case NamespaceTarget(var name):
                var prefix = name + ".";
                foreach (var child in DotNetCatalog.Namespaces.Where(c => c.StartsWith(prefix) && !c[prefix.Length..].Contains('.')))
                    items.Add((child[prefix.Length..], "Namespace", 9));
                foreach (var type in DotNetCatalog.Types.Where(t => t.Namespace == name)) items.Add((type.Name, $"{type.Kind} {type.QualifiedName}", KindNumber(type.Kind)));
                foreach (var symbol in symbols.Where(s => s.IsType && s.Namespace == name)) items.Add((symbol.Name, $"{symbol.KindTitle} {symbol.QualifiedName}", symbol.CompletionKind));
                break;
            case TypeTarget or Instance:
                var target = Resolve(expression, text, offset, languageId, symbols)!;
                var wantsShared = target is TypeTarget;
                var typeName = DotNetCatalog.BaseName(target is TypeTarget t ? t.Type : ((Instance)target).Type);
                var projectMembers = symbols.Where(s => string.Equals(s.Container, typeName, StringComparison.OrdinalIgnoreCase) && !s.IsType).ToList();
                if (projectMembers.Count > 0 || symbols.Any(s => s.IsType && s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)))
                {
                    var isModule = symbols.Any(s => s.Kind == SymbolKind.Module && s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                    // Private members are visible only inside their own type.
                    var inside = string.Equals(EnclosingType(offset, text, symbols, languageId), typeName, StringComparison.OrdinalIgnoreCase);
                    foreach (var member in projectMembers.Where(m => m.Kind != SymbolKind.Constructor && (isModule || m.IsShared == wantsShared) &&
                                                                     (inside || !m.Signature.StartsWith("private", StringComparison.OrdinalIgnoreCase))))
                        items.Add((member.Name, member.Signature, member.CompletionKind));
                    if (!wantsShared && DotNetCatalog.Type("Object") is { } obj)
                        items.AddRange(obj.Members.Select(m => (m.Name, m.Signature, m.IsMethod ? 2 : 10)));
                }
                else if (DotNetCatalog.Type(typeName) is { } type)
                {
                    var seen = new HashSet<string>();
                    foreach (var member in type.Members.Where(m => m.IsShared == wantsShared && seen.Add(m.Name)))
                        items.Add((member.Name, member.Signature, member.IsMethod ? 2 : 10));
                }
                break;
        }
        return items.GroupBy(i => i.Label.ToLowerInvariant()).Select(g => g.First()).OrderBy(i => i.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Works out the type of <c>a.b.c</c> step by step.</summary>
    private static Target? Resolve(List<string> expression, string text, int offset, string languageId, IReadOnlyList<CodeSymbol> symbols)
    {
        if (expression.Count == 0) return null;
        var comparison = languageId == "vb" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool Same(string? a, string? b) => string.Equals(a, b, comparison);
        var first = expression[0];
        Target? target;
        var container = EnclosingType(offset, text, symbols, languageId);
        if (first.ToLowerInvariant() is "me" or "this" or "self" or "mybase")
            target = container != null ? new Instance(container) : null;
        else if (Locals(text, offset, languageId).LastOrDefault(l => Same(l.Name, first)) is { Type: { } localType })
            target = new Instance(localType);
        else if (symbols.FirstOrDefault(s => !s.IsType && Same(s.Name, first) && s.Type != null && s.Container == container) is { } field)
            target = new Instance(field.Type!);
        else if (symbols.FirstOrDefault(s => s.IsType && Same(s.Name, first)) is { } projectType)
            target = new TypeTarget(projectType.Name);
        else if ((DotNetCatalog.Types.FirstOrDefault(t => Same(t.Name, first)) ?? (DotNetCatalog.Aliases.ContainsKey(first.ToLowerInvariant()) ? DotNetCatalog.Type(first) : null)) is { } catalogType)
            target = new TypeTarget(catalogType.Name);
        else if (DotNetCatalog.Namespaces.FirstOrDefault(n => Same(n, first)) is { } ns)
            target = new NamespaceTarget(ns);
        else
            return null;
        foreach (var part in expression.Skip(1))
        {
            switch (target)
            {
                case NamespaceTarget(var name):
                    var qualified = name + "." + part;
                    if (DotNetCatalog.Namespaces.FirstOrDefault(n => Same(n, qualified)) is { } inner) target = new NamespaceTarget(inner);
                    else if (DotNetCatalog.Types.FirstOrDefault(t => t.Namespace == name && Same(t.Name, part)) is { } type) target = new TypeTarget(type.Name);
                    else return null;
                    break;
                case TypeTarget or Instance:
                    var typeName = target is TypeTarget tt ? tt.Type : ((Instance)target).Type;
                    if (symbols.FirstOrDefault(s => Same(s.Container ?? "", DotNetCatalog.BaseName(typeName)) && Same(s.Name, part)) is { } member)
                    {
                        if (member.Kind == SymbolKind.EnumMember) target = new Instance(typeName);
                        else if (member.Type != null) target = new Instance(member.Type);
                        else return null;
                    }
                    else if (DotNetCatalog.Type(typeName)?.Members.FirstOrDefault(m => Same(m.Name, part)) is { ReturnType: { } returns })
                    {
                        // A generic List(Of T) item is unknown; stop rather than guess.
                        if (returns is "T" or "TKey" or "TValue" or "TResult") return null;
                        target = new Instance(returns);
                    }
                    else return null;
                    break;
                default:
                    return null;
            }
        }
        return target;
    }

    /// <summary>The identifiers of <c>a.b(1).c</c> before a dot, without call arguments.</summary>
    public static List<string> MemberExpression(string text)
    {
        var parts = new List<string>();
        var current = "";
        var depth = 0;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            var c = text[i];
            if (depth > 0)
            {
                if (c == ')') depth++;
                if (c == '(') depth--;
                continue;
            }
            if (c == ')') { depth = 1; continue; }
            if (char.IsLetterOrDigit(c) || c == '_') current = c + current;
            else if (c == '.' && current.Length > 0) { parts.Insert(0, current); current = ""; }
            else break;
        }
        if (current.Length > 0) parts.Insert(0, current);
        return parts;
    }

    /// <summary>Variables and parameters declared before an offset, in the method around it.</summary>
    public static List<Local> Locals(string text, int offset, string languageId)
    {
        var language = Languages.ById(languageId);
        var head = text[..Math.Min(offset, text.Length)].Replace("\r\n", "\n");
        var lines = ExtractMethod.Mask(head, language).Split('\n');
        var originals = head.Split('\n');
        var start = 0;
        if (Flavor.For(languageId) is { } flavor)
        {
            for (var index = lines.Length - 1; index >= 0; index--)
                if (flavor.IsHeader(lines[index])) { start = index; break; }
        }
        string[] patterns = languageId switch
        {
            "vb" =>
            [
                @"\b(?:Dim|Static|Const|Using)\s+(\w+)(?:\(\))?(?:\s+As\s+(?:New\s+)?([\w.]+(?:\(Of[^)]*\))?(?:\(\))?))?(?:\s*=\s*(.*))?",
                @"\bFor\s+(?:Each\s+)?(\w+)(?:\s+As\s+([\w.]+(?:\(Of[^)]*\))?))?",
                @"(?:\(|,)\s*(?:(?:ByVal|ByRef|Optional|ParamArray)\s+)*(\w+)(?:\(\))?\s+As\s+([\w.]+(?:\(Of[^)]*\))?(?:\(\))?)",
                @"\bCatch\s+(\w+)\s+As\s+([\w.]+)",
            ],
            _ => [@"\b(var|[A-Z]\w*(?:<[^;=()]*?>)?(?:\[\])?|int|long|string|double|bool|decimal|char|float|object|byte)\s+([A-Za-z_]\w*)\s*(?:=\s*(.*?);|;|\bin\b)"],
        };
        var options = languageId == "vb" ? RegexOptions.IgnoreCase : RegexOptions.None;
        var result = new List<Local>();
        for (var index = start; index < lines.Length; index++)
        {
            var line = originals[index];
            var masked = lines[index];
            foreach (var pattern in patterns)
            {
                foreach (Match match in Regex.Matches(line, pattern, options))
                {
                    // Not inside strings or comments.
                    if (match.Index >= masked.Length || masked[match.Index] != line[match.Index]) continue;
                    string? Group(int i) => i < match.Groups.Count && match.Groups[i].Success && match.Groups[i].Value.Trim().Length > 0 ? match.Groups[i].Value.Trim() : null;
                    string? name, type, value;
                    if (languageId != "vb") { name = Group(2); type = Group(1) == "var" ? null : Group(1); value = Group(3); }
                    else { name = Group(1); type = Group(2); value = Group(3); }
                    if (name == null || name.ToLowerInvariant() is "if" or "for" or "while" or "return") continue;
                    // `Dim calc As New Calculator()`: the parentheses are the constructor call, not an array.
                    if (type != null && type.EndsWith("()") && Regex.IsMatch(match.Value, @"\bNew\s", RegexOptions.IgnoreCase)) type = type[..^2];
                    if (type == null && value != null) type = InferType(value, languageId);
                    result.Add(new Local(name, type, index + 1));
                }
            }
        }
        return result;
    }

    /// <summary>The type a value starts out as: <c>New List(Of String)</c>, <c>"text"</c>, <c>42</c>.</summary>
    public static string? InferType(string value, string languageId)
    {
        var text = value.Trim();
        if (Regex.Match(text, @"^(?:New|new)\s+([\w.]+(?:\(Of[^)]*\)|<[^>]*>)?)") is { Success: true } created) return created.Groups[1].Value;
        if (text.StartsWith('"') || text.StartsWith("$\"")) return "String";
        if (Regex.IsMatch(text, @"^-?\d+;?$")) return "Integer";
        if (Regex.IsMatch(text, @"^-?\d+\.\d+;?$")) return "Double";
        if (text.TrimEnd(';').ToLowerInvariant() is "true" or "false") return "Boolean";
        if (Regex.IsMatch(text, @"^(?:DateTime\.Now|Now)\b")) return "DateTime";
        return null;
    }

    /// <summary>The type whose body holds an offset, from the file's own declarations.</summary>
    private static string? EnclosingType(int offset, string text, IReadOnlyList<CodeSymbol> symbols, string? languageId = null)
    {
        var line = new TextLines(text).Position(offset).Line + 1;
        var own = SymbolParser.Symbols(text, languageId ?? (symbols.Any(s => s.Signature.Contains(" As ")) ? "vb" : "csharp"));
        return own.Where(s => s.IsType && s.Line <= line).MaxBy(s => s.Line)?.Name;
    }

    /// <summary>Quick Info: a member of a .NET or project type, a local, or a project symbol.</summary>
    public static string? Hover(string text, int offset, string languageId, IReadOnlyList<CodeSymbol> symbols)
    {
        if (Word(text, offset) is not { } found) return null;
        var (word, start, qualifier) = found;
        var comparison = languageId == "vb" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool Same(string? a, string? b) => string.Equals(a, b, comparison);
        if (qualifier.Count > 0)
        {
            switch (Resolve(qualifier, text, start, languageId, symbols))
            {
                case TypeTarget or Instance:
                    var target = Resolve(qualifier, text, start, languageId, symbols)!;
                    var name = DotNetCatalog.BaseName(target is TypeTarget t ? t.Type : ((Instance)target).Type);
                    if (symbols.FirstOrDefault(s => Same(s.Container, name) && Same(s.Name, word)) is { } member)
                        return $"{member.Signature}\n\n{member.KindTitle} in {member.Container}";
                    if (DotNetCatalog.Type(name) is { } type && type.Members.FirstOrDefault(m => Same(m.Name, word)) is { } catalogMember)
                        return (catalogMember.IsShared ? "Shared " : "") + catalogMember.Signature + "\n\n" + type.QualifiedName;
                    return null;
                case NamespaceTarget(var ns):
                    return DotNetCatalog.Types.FirstOrDefault(t => t.Namespace == ns && Same(t.Name, word)) is { } nsType
                        ? $"{nsType.Kind} {nsType.QualifiedName}" : $"Namespace {ns}.{word}";
                default:
                    return null;
            }
        }
        if (Locals(text, start + word.Length, languageId).LastOrDefault(l => Same(l.Name, word)) is { } local)
            return (languageId == "vb" ? $"Dim {local.Name}" + (local.Type != null ? $" As {local.Type}" : "") : local.Name + (local.Type != null ? $": {local.Type}" : "")) + "\n\nLocal variable";
        if (symbols.FirstOrDefault(s => Same(s.Name, word)) is { } symbol)
            return $"{symbol.Signature}\n\n{symbol.KindTitle}" + (symbol.Container != null ? $" in {symbol.Container}" : "") +
                   (symbol.Path != null ? $" — {Path.GetFileName(symbol.Path)}, line {symbol.Line}" : "");
        if (DotNetCatalog.Type(word) is { } known && (DotNetCatalog.Aliases.ContainsKey(word.ToLowerInvariant()) || Same(known.Name, word)))
            return $"{known.Kind} {known.QualifiedName}";
        return null;
    }

    /// <summary>Where a name is declared: a project symbol (with its file) or a local variable in this file (no file).</summary>
    public static (string? Path, int Line)? Definition(string text, int offset, string languageId, IReadOnlyList<CodeSymbol> symbols)
    {
        if (Word(text, offset) is not { } found) return null;
        var (word, start, qualifier) = found;
        var comparison = languageId == "vb" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool Same(string? a, string? b) => string.Equals(a, b, comparison);
        if (qualifier.Count > 0)
        {
            var target = Resolve(qualifier, text, start, languageId, symbols);
            var name = target switch { TypeTarget t => t.Type, Instance i => DotNetCatalog.BaseName(i.Type), _ => null };
            return name != null && symbols.FirstOrDefault(s => Same(s.Container, name) && Same(s.Name, word)) is { } member ? (member.Path, member.Line) : null;
        }
        if (Locals(text, start, languageId).LastOrDefault(l => Same(l.Name, word)) is { } local) return (null, local.Line);
        var container = EnclosingType(offset, text, symbols, languageId);
        var matches = symbols.Where(s => Same(s.Name, word)).ToList();
        var symbol = matches.FirstOrDefault(s => s.Container == container) ?? matches.FirstOrDefault(s => s.IsType) ?? matches.FirstOrDefault();
        return symbol != null ? (symbol.Path, symbol.Line) : null;
    }

    private static (string Word, int Start, List<string> Qualifier)? Word(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var start = BasicIntelliSense.WordStart(text, offset);
        var end = offset;
        while (end < text.Length && BasicIntelliSense.IsIdentifier(text[end])) end++;
        if (end <= start) return null;
        var lineStart = start > 0 ? text.LastIndexOf('\n', start - 1) + 1 : 0;
        var before = text[lineStart..start];
        return (text[start..end], start, before.EndsWith('.') ? MemberExpression(before[..^1]) : []);
    }
}

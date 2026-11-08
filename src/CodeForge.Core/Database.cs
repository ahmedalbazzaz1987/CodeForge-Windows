using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeForge.Core;

public enum DataConnectionKind { Sqlite, SqlServer, Postgres, MySql }

/// <summary>
/// A Server Explorer data connection. SQLite files are opened directly (with the SQLite that comes with Windows,
/// winsqlite3.dll); SQL Server (LocalDB included), PostgreSQL and MySQL are reached through their command-line
/// clients — sqlcmd, psql, mysql — as in the Mac version.
/// </summary>
/// <param name="Location">SQLite: the file. Servers: the host, e.g. <c>(localdb)\MSSQLLocalDB</c>.</param>
public sealed record DataConnection(string Id, string Name, DataConnectionKind Kind, string Location, string Port = "", string Database = "", string User = "")
{
    public static string Title(DataConnectionKind kind) => kind switch
    {
        DataConnectionKind.Sqlite => "SQLite database file",
        DataConnectionKind.SqlServer => "Microsoft SQL Server (LocalDB, Express…)",
        DataConnectionKind.Postgres => "PostgreSQL",
        _ => "MySQL / MariaDB",
    };

    /// <summary>The command-line client a server connection needs.</summary>
    public static string? Client(DataConnectionKind kind) => kind switch
    {
        DataConnectionKind.SqlServer => "sqlcmd",
        DataConnectionKind.Postgres => "psql",
        DataConnectionKind.MySql => "mysql",
        _ => null,
    };

    public static string InstallHint(DataConnectionKind kind) => kind switch
    {
        DataConnectionKind.SqlServer => "Install sqlcmd with: winget install Microsoft.Sqlcmd (LocalDB comes with Visual Studio or SQL Server Express).",
        DataConnectionKind.Postgres => "Install the PostgreSQL client with: winget install PostgreSQL.PostgreSQL",
        DataConnectionKind.MySql => "Install the MySQL client with: winget install Oracle.MySQL",
        _ => "",
    };

    /// <summary>The environment variable a client reads the password from (never the command line).</summary>
    public static string? PasswordVariable(DataConnectionKind kind) => kind switch
    {
        DataConnectionKind.SqlServer => "SQLCMDPASSWORD",
        DataConnectionKind.Postgres => "PGPASSWORD",
        DataConnectionKind.MySql => "MYSQL_PWD",
        _ => null,
    };
}

public sealed record QueryResult(List<string> Columns, List<string?[]> Rows, int Changes = 0, string Message = "")
{
    public static QueryResult Text(string message) => new([], [], Message: message);
}

public sealed class DatabaseException(string message) : Exception(message);

/// <summary>A table or view and its columns, for the Server Explorer tree.</summary>
public sealed record DatabaseTable(string Name, bool IsView, List<(string Name, string Type, bool IsKey)> Columns);

/// <summary>A SQLite database file, through the sqlite3 C API.</summary>
public sealed class SqliteDatabase : IDisposable
{
    private IntPtr handle;

    static SqliteDatabase()
    {
        // Windows has its own SQLite (winsqlite3.dll); elsewhere (the tests on Linux and macOS) use the system's.
        NativeLibrary.SetDllImportResolver(typeof(SqliteDatabase).Assembly, (name, assembly, path) =>
        {
            if (name != Library || OperatingSystem.IsWindows()) return IntPtr.Zero;
            foreach (var candidate in new[] { "libsqlite3.so.0", "libsqlite3.so", "libsqlite3.dylib", "/usr/lib/libsqlite3.dylib" })
                if (NativeLibrary.TryLoad(candidate, out var loaded)) return loaded;
            return IntPtr.Zero;
        });
    }

    /// <summary>Opens a database file; <paramref name="create"/> makes a new empty one when it doesn't exist.</summary>
    public SqliteDatabase(string path, bool create = false)
    {
        int result;
        try
        {
            result = sqlite3_open_v2(Utf8(path), out handle, OpenReadWrite | (create ? OpenCreate : 0), IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            throw new DatabaseException("SQLite isn't available on this computer.");
        }
        if (result != Ok)
        {
            var message = handle != IntPtr.Zero ? Error() : "Couldn't open the database.";
            sqlite3_close(handle);
            handle = IntPtr.Zero;
            throw new DatabaseException(message);
        }
        sqlite3_busy_timeout(handle, 2000);
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero) sqlite3_close(handle);
        handle = IntPtr.Zero;
    }

    public List<DatabaseTable> Tables()
    {
        var list = Execute("SELECT name, type FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%' ORDER BY type, name");
        return list.Rows.Select(row =>
        {
            var name = row[0] ?? "";
            var info = Execute($"PRAGMA table_info({Quote(name)})");
            return new DatabaseTable(name, row[1] == "view", info.Rows.Select(c => (c[1] ?? "", c[2] ?? "", (c[5] ?? "0") != "0")).ToList());
        }).ToList();
    }

    /// <summary>Runs one or more statements. The result is the last statement that returned rows, or the change count.</summary>
    public QueryResult Execute(string sql)
    {
        var columns = new List<string>();
        var rows = new List<string?[]>();
        var changes = 0;
        var hadRows = false;
        var bytes = Utf8(sql);
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var tail = buffer;
            while (Marshal.ReadByte(tail) != 0)
            {
                if (sqlite3_prepare_v2(handle, tail, -1, out var statement, out var next) != Ok) throw new DatabaseException(Error());
                tail = next;
                if (statement == IntPtr.Zero) continue;   // only whitespace or a comment was left
                try
                {
                    var count = sqlite3_column_count(statement);
                    var found = new List<string?[]>();
                    while (true)
                    {
                        var step = sqlite3_step(statement);
                        if (step == Done) break;
                        if (step != Row) throw new DatabaseException(Error());
                        var values = new string?[count];
                        for (var i = 0; i < count; i++)
                            values[i] = sqlite3_column_type(statement, i) == Null ? null : Text(sqlite3_column_text(statement, i));
                        found.Add(values);
                        if (found.Count >= 10_000) break;
                    }
                    if (count > 0)
                    {
                        hadRows = true;
                        columns = Enumerable.Range(0, count).Select(i => Text(sqlite3_column_name(statement, i)) ?? "").ToList();
                        rows = found;
                    }
                    else
                    {
                        changes += sqlite3_changes(handle);
                    }
                }
                finally
                {
                    sqlite3_finalize(statement);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        var message = hadRows ? $"{rows.Count} row{(rows.Count == 1 ? "" : "s")}" + (rows.Count >= 10_000 ? " (first 10,000)" : "")
            : $"Command completed. {changes} row{(changes == 1 ? "" : "s")} affected.";
        return new QueryResult(columns, rows, changes, message);
    }

    public static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    private string Error() => Text(sqlite3_errmsg(handle)) ?? "SQLite error";

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");

    private static string? Text(IntPtr pointer) => pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);

    private const string Library = "winsqlite3";
    private const int Ok = 0, Row = 100, Done = 101, Null = 5, OpenReadWrite = 0x2, OpenCreate = 0x4;

    [DllImport(Library)] private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Library)] private static extern int sqlite3_close(IntPtr db);
    [DllImport(Library)] private static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);
    [DllImport(Library)] private static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport(Library)] private static extern int sqlite3_prepare_v2(IntPtr db, IntPtr sql, int bytes, out IntPtr statement, out IntPtr tail);
    [DllImport(Library)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport(Library)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport(Library)] private static extern int sqlite3_column_count(IntPtr statement);
    [DllImport(Library)] private static extern int sqlite3_column_type(IntPtr statement, int column);
    [DllImport(Library)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport(Library)] private static extern IntPtr sqlite3_column_name(IntPtr statement, int column);
    [DllImport(Library)] private static extern int sqlite3_changes(IntPtr db);
}

/// <summary>Builds client commands for server databases and reads their output (the same as the Mac version).</summary>
public static class DatabaseClient
{
    public static string TablesQuery(DataConnectionKind kind) => kind switch
    {
        DataConnectionKind.Sqlite => "SELECT name, type FROM sqlite_master WHERE type IN ('table','view') ORDER BY name",
        DataConnectionKind.Postgres => "SELECT table_schema || '.' || table_name, table_type FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema') ORDER BY 1",
        DataConnectionKind.MySql => "SELECT table_name, table_type FROM information_schema.tables WHERE table_schema = DATABASE() ORDER BY 1",
        _ => "SET NOCOUNT ON; SELECT TABLE_SCHEMA + '.' + TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES ORDER BY 1",
    };

    /// <summary>The SQL that lists a table's columns (name, type, is primary key).</summary>
    public static string ColumnsQuery(DataConnectionKind kind, string table)
    {
        var parts = table.Split('.', 2);
        var name = parts[^1].Replace("'", "''");
        var schema = parts.Length > 1 ? parts[0].Replace("'", "''") : null;
        switch (kind)
        {
            case DataConnectionKind.Sqlite:
                return $"PRAGMA table_info('{name}')";
            case DataConnectionKind.MySql:
                return $"SELECT column_name, column_type, IF(column_key = 'PRI', 'YES', 'NO') FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = '{name}' ORDER BY ordinal_position";
            default:
                var nocount = kind == DataConnectionKind.SqlServer ? "SET NOCOUNT ON; " : "";
                return nocount +
                    "SELECT c.column_name, c.data_type, CASE WHEN k.column_name IS NULL THEN 'NO' ELSE 'YES' END " +
                    "FROM information_schema.columns c LEFT JOIN (SELECT ku.table_schema, ku.table_name, ku.column_name " +
                    "FROM information_schema.table_constraints tc JOIN information_schema.key_column_usage ku " +
                    "ON tc.constraint_name = ku.constraint_name AND tc.table_schema = ku.table_schema " +
                    "WHERE tc.constraint_type = 'PRIMARY KEY') k ON k.table_schema = c.table_schema AND k.table_name = c.table_name " +
                    $"AND k.column_name = c.column_name WHERE c.table_name = '{name}'" + (schema != null ? $" AND c.table_schema = '{schema}'" : "") +
                    " ORDER BY c.ordinal_position";
        }
    }

    /// <summary>A SELECT of the first rows of a table, in the server's dialect.</summary>
    public static string TopRowsQuery(DataConnectionKind kind, string table, int count = 200)
    {
        var quoted = string.Join(".", table.Split('.').Select(part => kind switch
        {
            DataConnectionKind.MySql => "`" + part.Replace("`", "``") + "`",
            DataConnectionKind.SqlServer => "[" + part.Replace("]", "]]") + "]",
            _ => "\"" + part.Replace("\"", "\"\"") + "\"",
        }));
        return kind == DataConnectionKind.SqlServer ? $"SELECT TOP {count} * FROM {quoted}" : $"SELECT * FROM {quoted} LIMIT {count}";
    }

    /// <summary>The client and its arguments for running SQL on a server.</summary>
    public static (string Executable, List<string> Arguments)? Command(DataConnection connection, string sql)
    {
        var arguments = new List<string>();
        switch (connection.Kind)
        {
            case DataConnectionKind.Postgres:
                arguments.AddRange(["-X", "--csv", "-v", "ON_ERROR_STOP=1", "-h", connection.Location]);
                if (connection.Port.Length > 0) arguments.AddRange(["-p", connection.Port]);
                if (connection.User.Length > 0) arguments.AddRange(["-U", connection.User]);
                if (connection.Database.Length > 0) arguments.AddRange(["-d", connection.Database]);
                arguments.AddRange(["-c", sql]);
                return ("psql", arguments);
            case DataConnectionKind.MySql:
                arguments.AddRange(["--batch", "--raw", "-h", connection.Location]);
                if (connection.Port.Length > 0) arguments.AddRange(["-P", connection.Port]);
                if (connection.User.Length > 0) arguments.AddRange(["-u", connection.User]);
                if (connection.Database.Length > 0) arguments.AddRange(["-D", connection.Database]);
                arguments.AddRange(["-e", sql]);
                return ("mysql", arguments);
            case DataConnectionKind.SqlServer:
                // -s: an unlikely column separator; -W: no padding; -b: fail on errors; -f 65001: UTF-8.
                arguments.AddRange(["-S", connection.Location + (connection.Port.Length > 0 ? "," + connection.Port : ""), "-s", "\u001F", "-W", "-b", "-f", "65001"]);
                if (connection.User.Length > 0) arguments.AddRange(["-U", connection.User]);
                else arguments.Add("-E");   // Windows sign-in, as LocalDB expects
                if (connection.Database.Length > 0) arguments.AddRange(["-d", connection.Database]);
                arguments.AddRange(["-Q", sql]);
                return ("sqlcmd", arguments);
            default:
                return null;
        }
    }

    /// <summary>Reads a client's output: CSV (psql --csv), tab-separated (mysql --batch) or unit-separated (sqlcmd).</summary>
    public static QueryResult Parse(string output, DataConnectionKind kind)
    {
        var text = output.Replace("\r\n", "\n");
        List<string?[]> lines = kind switch
        {
            DataConnectionKind.MySql => text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Split('\t').Select(v => v == "NULL" ? null : v.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\\\", "\\")).ToArray()).ToList(),
            DataConnectionKind.SqlServer => text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !(l.StartsWith('(') && l.TrimEnd().EndsWith("affected)")))
                .Where(l => !l.All(c => c is '-' or '\u001F'))
                .Select(l => l.Split('\u001F').Select(v => v == "NULL" ? null : v).ToArray()).ToList(),
            _ => ParseCsv(text),
        };
        if (lines.Count == 0 || (lines[0].Length <= 1 && lines.Count <= 1))
            return QueryResult.Text(text.Trim().Length == 0 ? "Command completed." : text.Trim());
        var columns = lines[0].Select(c => c ?? "").ToList();
        var rows = lines.Skip(1).Select(row => row.Length >= columns.Count ? row[..columns.Count] : [.. row, .. new string?[columns.Count - row.Length]]).ToList();
        return new QueryResult(columns, rows, Message: $"{rows.Count} row{(rows.Count == 1 ? "" : "s")}");
    }

    internal static List<string?[]> ParseCsv(string text)
    {
        var rows = new List<string?[]>();
        var row = new List<string?>();
        var field = new StringBuilder();
        bool quoted = false, wasQuoted = false;
        if (!text.EndsWith('\n')) text += "\n";
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else quoted = false;
                }
                else field.Append(c);
            }
            else if (c == '"') { quoted = true; wasQuoted = true; }
            else if (c is ',' or '\n')
            {
                // psql writes NULL as an empty, unquoted field.
                row.Add(field.Length == 0 && !wasQuoted ? null : field.ToString());
                field.Clear();
                wasQuoted = false;
                if (c == '\n')
                {
                    if (!(row.Count == 1 && row[0] == null)) rows.Add([.. row]);
                    row.Clear();
                }
            }
            else field.Append(c);
        }
        return rows;
    }

    /// <summary>Runs SQL on a connection: SQLite directly, servers through their client (with the password in its environment).</summary>
    public static QueryResult Run(DataConnection connection, string sql, string? password = null)
    {
        if (connection.Kind == DataConnectionKind.Sqlite)
        {
            using var database = new SqliteDatabase(connection.Location);
            return database.Execute(sql);
        }
        var (executable, arguments) = Command(connection, sql)!.Value;
        if (ToolCheck.Find(executable) is not { } program) throw new DatabaseException($"{executable} isn't installed. {DataConnection.InstallHint(connection.Kind)}");
        var info = new System.Diagnostics.ProcessStartInfo(program)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (password != null && DataConnection.PasswordVariable(connection.Kind) is { } variable) info.Environment[variable] = password;
        using var process = System.Diagnostics.Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(60_000)) { process.Kill(true); throw new DatabaseException("The query took longer than a minute."); }
        if (process.ExitCode != 0) throw new DatabaseException((error.Result + output).Trim() is { Length: > 0 } message ? message : $"{executable} failed.");
        return Parse(output, connection.Kind);
    }

    /// <summary>Tables and views, with their columns for SQLite (servers load columns when a table is opened).</summary>
    public static List<DatabaseTable> Tables(DataConnection connection, string? password = null)
    {
        if (connection.Kind == DataConnectionKind.Sqlite)
        {
            using var database = new SqliteDatabase(connection.Location);
            return database.Tables();
        }
        return Run(connection, TablesQuery(connection.Kind), password).Rows
            .Select(r => new DatabaseTable(r[0] ?? "", (r.Length > 1 ? r[1] ?? "" : "").Contains("VIEW", StringComparison.OrdinalIgnoreCase), [])).ToList();
    }

    public static List<(string Name, string Type, bool IsKey)> Columns(DataConnection connection, string table, string? password = null) =>
        Run(connection, ColumnsQuery(connection.Kind, table), password).Rows
            .Select(r => (r[0] ?? "", r.Length > 1 ? r[1] ?? "" : "", r.Length > 2 && r[2] == "YES")).ToList();
}

using CodeForge.Core;

namespace CodeForge.Core.Tests;

public class DatabaseTests
{
    [Fact]
    public void SqliteCreatesQueriesAndListsTables()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("cf-db").FullName, "shop.db");
        using (var database = new SqliteDatabase(file, create: true))
        {
            var created = database.Execute("CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT, price REAL); INSERT INTO products (name, price) VALUES ('قهوة', 3.5), ('Tea', NULL);");
            Assert.Equal(2, created.Changes);
            var rows = database.Execute("SELECT name, price FROM products ORDER BY id");
            Assert.Equal("name|price", string.Join("|", rows.Columns));
            Assert.Equal("قهوة", rows.Rows[0][0]);
            Assert.Null(rows.Rows[1][1]);
            Assert.Equal("2 rows", rows.Message);
            database.Execute("CREATE VIEW cheap AS SELECT * FROM products WHERE price < 5");
            var tables = database.Tables();
            Assert.Equal("products cheap", string.Join(" ", tables.Select(t => t.Name)));
            Assert.True(tables[0].Columns[0].IsKey);
            Assert.True(tables[1].IsView);
            Assert.Throws<DatabaseException>(() => database.Execute("SELECT * FROM missing"));
        }
        Assert.Throws<DatabaseException>(() => new SqliteDatabase(file + ".none"));
    }

    [Fact]
    public void ReadsClientOutput()
    {
        var sqlcmd = DatabaseClient.Parse("name\u001Fage\n----\u001F---\nAli\u001F30\nSara\u001FNULL\n\n(2 rows affected)\n", DataConnectionKind.SqlServer);
        Assert.Equal("name|age", string.Join("|", sqlcmd.Columns));
        Assert.Equal(2, sqlcmd.Rows.Count);
        Assert.Null(sqlcmd.Rows[1][1]);
        var csv = DatabaseClient.Parse("id,note\n1,\"a, \"\"b\"\"\"\n2,\n", DataConnectionKind.Postgres);
        Assert.Equal("a, \"b\"", csv.Rows[0][1]);
        Assert.Null(csv.Rows[1][1]);
        Assert.Equal("Command completed.", DatabaseClient.Parse("", DataConnectionKind.MySql).Message);
        Assert.Equal("SELECT TOP 200 * FROM [dbo].[Orders]", DatabaseClient.TopRowsQuery(DataConnectionKind.SqlServer, "dbo.Orders"));
        var command = DatabaseClient.Command(new DataConnection("1", "Local", DataConnectionKind.SqlServer, @"(localdb)\MSSQLLocalDB", Database: "Shop"), "SELECT 1")!.Value;
        Assert.Equal("sqlcmd", command.Executable);
        Assert.Contains("-E", command.Arguments);
        Assert.Equal("SELECT 1", command.Arguments[^1]);
    }
}

using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class SubqueryExecutionTests
{
    [Fact]
    public async Task Correlated_exists_and_in_subquery_execute()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
                CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL, total REAL NOT NULL);
                CREATE TABLE audits (id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL, kind TEXT NOT NULL);
                INSERT INTO users VALUES (1, 'Edin'), (2, 'Guest'), (3, 'No Audit');
                INSERT INTO orders VALUES (1, 1, 150), (2, 2, 20), (3, 3, 200);
                INSERT INTO audits VALUES (1, 1, 'login'), (2, 3, 'logout');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var orderUsers = Sql.From("orders o")
            .Select("o.user_id")
            .Where(Exp.Col("o.total") > 100);
        var loginAudit = Sql.From("audits a")
            .Select("a.id")
            .Where(
                (Exp.Col("a.user_id") == Exp.Col("u.id"))
                & (Exp.Col("a.kind") == "login"));

        var rows = await session
            .From<UserRow>("users u")
            .Select("u.id", "u.name")
            .Where(Exp.Col("u.id").In(orderUsers) & Exp.Exists(loginAudit))
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal(new UserRow(1, "Edin"), row);
    }

    private sealed record UserRow(long Id, string Name);
}

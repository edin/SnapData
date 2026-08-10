using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class JoinExecutionTests
{
    [Fact]
    public async Task Typed_from_override_executes_projected_join()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE Books (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, AuthorId INTEGER NOT NULL);
                CREATE TABLE Authors (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);
                INSERT INTO Authors VALUES (1, 'Ursula Le Guin');
                INSERT INTO Books VALUES (10, 'A Wizard of Earthsea', 1);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);

        var rows = await session
            .From<BookWithAuthor>("Books b")
            .Join("Authors a ON a.Id = b.AuthorId")
            .Select("b.Id", "b.Title", "a.Name AS AuthorName")
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal(new BookWithAuthor(10, "A Wizard of Earthsea", "Ursula Le Guin"), row);
    }

    [Fact]
    public async Task Joined_query_executes_and_maps_aliased_projection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL, role_id INTEGER);
                CREATE TABLE roles (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
                INSERT INTO roles VALUES (1, 'Admin');
                INSERT INTO users VALUES (10, 'Edin', 1), (11, 'Guest', NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var users = Sql.Table("users").As("u");
        var roles = Sql.Table("roles").As("r");
        var query = Sql
            .Select(
                users.Col("id"),
                users.Col("name"),
                roles.Col("name").As("role_name"))
            .From(users)
            .LeftJoin(roles, users.Col("role_id") == roles.Col("id"))
            .OrderBy(users.Col("id"));

        var rows = await session.QueryAsync<UserRole>(query);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new UserRole(10, "Edin", "Admin"), rows[0]);
        Assert.Equal(new UserRole(11, "Guest", null), rows[1]);
    }

    private sealed record UserRole(
        long Id,
        string Name,
        [property: Column("role_name")] string? RoleName);

    private sealed record BookWithAuthor(long Id, string Title, string AuthorName);
}

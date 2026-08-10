using System.Data;
using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class SessionTests
{
    [Fact]
    public async Task Raw_sql_binds_anonymous_parameters_and_maps_records()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection);

        await session.ExecuteAsync(
            "INSERT INTO users (name, active) VALUES (@Name, @Active)",
            new { Name = "Edin", Active = true });

        var user = await session.QuerySingleOrDefaultAsync<User>(
            "SELECT id, name, active FROM users WHERE name = @Name",
            new { Name = "Edin" });

        Assert.NotNull(user);
        Assert.Equal("Edin", user.Name);
        Assert.True(user.Active);
    }

    [Fact]
    public async Task Borrowed_connection_is_not_disposed_with_session()
    {
        await using var connection = await OpenConnectionAsync();

        await using (var session = DbSession.Borrow(connection))
        {
            Assert.Equal(0, await session.ScalarAsync<int>("SELECT COUNT(*) FROM users"));
        }

        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(1, await ExecuteScalarAsync<long>(connection, "SELECT 1"));
    }

    [Fact]
    public async Task Borrowed_closed_connection_is_restored_to_closed_state()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");

        await using (var session = DbSession.Borrow(connection))
        {
            Assert.Equal(1, await session.ScalarAsync<int>("SELECT 1"));
            Assert.Equal(ConnectionState.Open, connection.State);
        }

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task Transaction_object_executes_on_same_connection_and_commits()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection);

        await using (var transaction = await session.BeginTransactionAsync())
        {
            await transaction.ExecuteAsync(
                "INSERT INTO users (name, active) VALUES (@Name, @Active)",
                new { Name = "Committed", Active = true });

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.ScalarAsync<int>("SELECT COUNT(*) FROM users"));

            Assert.Equal(
                1,
                await transaction.ScalarAsync<int>("SELECT COUNT(*) FROM users"));

            await transaction.CommitAsync();
        }

        Assert.Equal(1, await session.ScalarAsync<int>("SELECT COUNT(*) FROM users"));
    }

    [Fact]
    public async Task Disposing_uncommitted_transaction_rolls_back()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection);

        await using (var transaction = await session.BeginTransactionAsync())
        {
            await transaction.ExecuteAsync(
                "INSERT INTO users (name, active) VALUES ('Rolled back', 1)");
        }

        Assert.Equal(0, await session.ScalarAsync<int>("SELECT COUNT(*) FROM users"));
    }

    [Fact]
    public async Task Builder_queries_use_the_same_execution_path()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection);
        await session.ExecuteAsync(
            "INSERT INTO users (name, active) VALUES ('Active', 1), ('Inactive', 0)");

        var query = Sql
            .Select("id", "name", "active")
            .From("users")
            .Where(Exp.Col("active") == true);

        var users = await session.QueryAsync<User>(query);

        var user = Assert.Single(users);
        Assert.Equal("Active", user.Name);
    }

    [Fact]
    public async Task Mutation_builders_execute_and_return_values()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection);

        var id = await session.ScalarAsync<long>(
            Sql.InsertInto("users")
                .Values(new { name = "Edin", active = true })
                .Returning("id"));

        var changed = await session.QuerySingleOrDefaultAsync<User>(
            Sql.Update("users")
                .Set("active", false)
                .Where(Exp.Col("id") == id)
                .Returning("id", "name", "active"));

        var deletedId = await session.ScalarAsync<long>(
            Sql.DeleteFrom("users")
                .Where(Exp.Col("id") == id)
                .Returning("id"));

        Assert.NotNull(changed);
        Assert.False(changed.Active);
        Assert.Equal(id, deletedId);
        Assert.Equal(0, await session.ScalarAsync<int>("SELECT COUNT(*) FROM users"));
    }

    [Fact]
    public async Task Materialization_uses_attribute_column_mapping_for_records()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection);
        await session.ExecuteAsync(
            "INSERT INTO users (name, active) VALUES ('Mapped', 1)");

        var user = await session.QuerySingleOrDefaultAsync<AttributedUser>(
            "SELECT id AS user_id, name AS display_name, active AS is_active FROM users");

        Assert.NotNull(user);
        Assert.Equal(1, user.Id);
        Assert.Equal("Mapped", user.Name);
        Assert.True(user.Active);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                active INTEGER NOT NULL
            )
            """;
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        SqliteConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private sealed record User(long Id, string Name, bool Active);

    private sealed record AttributedUser(
        [property: Key, Column("user_id")] long Id,
        [property: Column("display_name")] string Name,
        [property: Column("is_active")] bool Active);
}

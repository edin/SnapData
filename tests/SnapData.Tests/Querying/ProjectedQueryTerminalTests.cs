using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class ProjectedQueryTerminalTests
{
    [Fact]
    public async Task First_and_single_terminals_use_projected_result_type()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);

        var first = await Query(session).OrderBy("id").FirstAsync();
        var single = await Query(session).Where("id = @id", new { id = 2 }).SingleAsync();
        var missing = await Query(session).Where("id = @id", new { id = 9 })
            .SingleOrDefaultAsync();

        Assert.Equal(new UserRow(1, "Edin"), first);
        Assert.Equal(new UserRow(2, "Sara"), single);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Projected_terminal_cardinality_errors_match_entity_queries()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Query(session).SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Query(session)
            .Where("id = @id", new { id = 9 })
            .FirstAsync());
    }

    [Fact]
    public async Task Any_count_and_page_do_not_consume_projected_builder()
    {
        await using var connection = await OpenConnectionAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var query = Query(session).OrderBy("id");

        Assert.True(await query.AnyAsync());
        Assert.Equal(2, await query.CountAsync());
        var page = await query.PageAsync(1, 1);
        Assert.Single(page.Items);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, (await query.ToListAsync()).Count);
    }

    private static ProjectedQuery<UserRow> Query(DbSession session) =>
        session.From("users").Select<UserRow>("id", "name");

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
            INSERT INTO users VALUES (1, 'Edin'), (2, 'Sara');
            """;
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private sealed record UserRow(long Id, string Name);
}

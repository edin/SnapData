using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class MutationReturningTests
{
    [Fact]
    public async Task Typed_mutations_return_scalar_and_dto_values_without_hydrating_input()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var observer = new Observer();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance, commandObserver: observer);
        await session.ExecuteAsync("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT UNIQUE, revision INTEGER GENERATED ALWAYS AS (length(name)) STORED)");
        var input = new User { Name = "first" };
        var id = await session.InsertInto<User>().Values(input).Returning<long>(u => u.Id).SingleAsync();
        Assert.True(id > 0);
        Assert.Equal(0, input.Id);
        var changed = await session.Update<User>().Set(u => u.Name, "changed").Where(u => u.Id == id)
            .Returning<UserResult>(u => u.Id, u => u.Name, u => u.Revision).SingleAsync();
        Assert.Equal(new UserResult(id, "changed", 7), changed);
        var sameId = await session.UpsertInto<User>().Values(new User { Name = "changed" })
            .OnConflict(u => u.Name).DoNothing().Returning<long?>(u => u.Id).SingleOrDefaultAsync();
        Assert.Null(sameId);
        var deleted = await session.DeleteFrom<User>().Where(u => u.Id == id)
            .Returning<long>(u => u.Id).ToListAsync();
        Assert.Equal(id, Assert.Single(deleted));
        Assert.Contains(observer.Events, e => e.Kind == CommandExecutionKind.Query && e.ResultCount == 0);
        Assert.Contains(observer.Events, e => e.Kind == CommandExecutionKind.Query && e.ResultCount == 1);
    }

    [Fact]
    public async Task Upsert_returns_inserted_and_updated_rows_and_respects_rollback()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        await session.ExecuteAsync("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT UNIQUE, revision INTEGER GENERATED ALWAYS AS (length(name)) STORED)");
        await using var transaction = await session.BeginTransactionAsync();
        var inserted = await transaction.QuerySingleOrDefaultAsync<UserResult>(
            Sql.UpsertInto("users").Values(new { id = 1, name = "first" }).OnConflict("id").UpdateColumns("name").Returning("id", "name", "revision"));
        Assert.Equal(new UserResult(1, "first", 5), inserted);
        var updated = await transaction.QuerySingleOrDefaultAsync<UserResult>(
            Sql.UpsertInto("users").Values(new { id = 1, name = "updated" }).OnConflict("id").UpdateColumns("name").Returning("id", "name", "revision"));
        Assert.Equal(new UserResult(1, "updated", 7), updated);
        await transaction.RollbackAsync();
        Assert.Equal(0, await session.From<User>().CountAsync());
    }

    [Fact]
    public async Task Cardinality_errors_do_not_reexecute_or_implicitly_rollback_the_write()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        await session.ExecuteAsync("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, revision INTEGER); INSERT INTO users(name) VALUES ('a'),('b')");
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Update<User>().Set(u => u.Name, "changed").AllRows()
            .Returning<long>(u => u.Id).SingleAsync());
        Assert.Equal(2, await session.From<User>().Where(u => u.Name == "changed").CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.DeleteFrom<User>().Where(u => u.Id == -1)
            .Returning<long>(u => u.Id).SingleAsync());
        await using var transaction = await session.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.DeleteFrom<User>().AllRows()
            .Returning<long>(u => u.Id).SingleOrDefaultAsync());
        await transaction.RollbackAsync();
        Assert.Equal(2, await session.From<User>().CountAsync());
    }

    [Fact]
    public async Task Unsupported_returning_is_rejected_before_opening_connection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var session = DbSession.Borrow(connection, MySqlQueryCompiler.Instance);
        await Assert.ThrowsAsync<NotSupportedException>(() => session.InsertInto<User>().Values(new User { Name = "a" })
            .Returning<long>(u => u.Id).SingleAsync());
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.Throws<ArgumentException>(() => session.InsertInto<User>().Returning<long>());
        Assert.Throws<ArgumentException>(() => session.InsertInto<User>().Returning<long>(u => u.Id, u => u.Id));
    }

    [Fact]
    public async Task Returning_forwards_cancellation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        await session.ExecuteAsync("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, revision INTEGER)");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.InsertInto<User>().Values(new User { Name = "a" })
            .Returning<long>(u => u.Id).SingleAsync(new QueryOptions { CommandTimeout = 2 }, cancellation.Token));
        Assert.Equal(0, await session.From<User>().CountAsync());
    }

    private sealed class Observer : CommandObserver
    {
        public List<CommandExecutedContext> Events { get; } = [];
        public override void Executed(CommandExecutedContext context) => Events.Add(context);
    }

    [Table("users")]
    private sealed class User
    {
        [Key, Column("id"), DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
        [Column("name")] public string Name { get; set; } = "";
        [Column("revision"), DatabaseGenerated(DatabaseGeneratedOption.Computed)] public int Revision { get; set; }
    }
    private sealed record UserResult([property: Column("id")] long Id,
        [property: Column("name")] string Name, [property: Column("revision")] int Revision);
}

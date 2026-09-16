using Microsoft.Data.SqlClient;

namespace SnapData.IntegrationTests;

public sealed class SqlServerUpsertTests
{
    [SqlServerFact]
    public async Task Returning_maps_insert_update_skip_and_delete_results()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var session = await fixture.Database.OpenSessionAsync();
        await using var transaction = await session.BeginTransactionAsync();
        var account = new Account { Tenant = 1, Email = "returning", Name = "first", Note = "keep" };
        var id = await Upsert(transaction, account).Returning<long>(a => a.Id).SingleAsync();
        Assert.True(id > 0);
        Assert.Equal(0, account.Id);
        account.Name = "updated";
        Assert.Equal(id, await Upsert(transaction, account).Returning<long>(a => a.Id).SingleAsync());
        Assert.Null(await Upsert(transaction, account).DoNothing().Returning<long?>(a => a.Id).SingleOrDefaultAsync());
        var changed = await transaction.Update<Account>().Set(a => a.Name, "changed").Where(a => a.Id == id)
            .Returning<AccountResult>(a => a.Id, a => a.Name).SingleAsync();
        Assert.Equal(new AccountResult(id, "changed"), changed);
        Assert.Equal(id, await transaction.DeleteFrom<Account>().Where(a => a.Id == id).Returning<long>(a => a.Id).SingleAsync());
        Assert.True(await transaction.InsertInto<Account>().Values(account).Returning<long>(a => a.Id).SingleAsync() > 0);
        await transaction.RollbackAsync();
        Assert.Equal(0, await session.From<Account>().CountAsync());
    }

    private sealed record AccountResult([property: Column("id")] long Id, [property: Column("display_name")] string Name);

    [SqlServerFact]
    public async Task Inserts_updates_selected_columns_and_skips_composite_conflicts()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var session = await fixture.Database.OpenSessionAsync();
        var account = new Account { Tenant = 1, Email = "a@b", Name = "first", Note = "keep" };
        Assert.Equal(1, await Upsert(session, account).ExecuteAsync());
        Assert.Equal(0, account.Id);
        account.Name = "O'Reilly";
        account.Note = "replace";
        Assert.Equal(1, await Upsert(session, account).ExecuteAsync());
        Assert.Equal(0, await Upsert(session, account).DoNothing().ExecuteAsync());
        var stored = await session.From<Account>().SingleAsync();
        Assert.Equal("O'Reilly", stored.Name);
        Assert.Equal("keep", stored.Note);
        Assert.True(stored.Id > 0);
        account.Tenant = 2;
        account.Name = "another tenant";
        Assert.Equal(1, await Upsert(session, account).ExecuteAsync());
        Assert.Equal(2, await session.From<Account>().CountAsync());
    }

    [SqlServerFact]
    public async Task Null_conflict_values_match_sql_server_unique_constraint_semantics()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var session = await fixture.Database.OpenSessionAsync();
        var account = new Account { Tenant = 1, Email = null, Name = "first" };
        await Upsert(session, account).ExecuteAsync();
        account.Name = "updated";
        Assert.Equal(1, await Upsert(session, account).ExecuteAsync());
        Assert.Equal("updated", (await session.From<Account>().SingleAsync()).Name);
    }

    [SqlServerFact]
    public async Task Unrelated_unique_and_not_null_errors_are_not_ignored()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var session = await fixture.Database.OpenSessionAsync();
        await Upsert(session, new Account { Tenant = 1, Email = "first", Name = "unique" }).ExecuteAsync();
        var error = await Assert.ThrowsAsync<SqlException>(() => Upsert(session,
            new Account { Tenant = 2, Email = "second", Name = "unique" }).DoNothing().ExecuteAsync());
        Assert.Contains(error.Number, new[] { 2601, 2627 });
        var nullError = await Assert.ThrowsAsync<SqlException>(() => Upsert(session,
            new Account { Tenant = 3, Email = "third", Name = null! }).DoNothing().ExecuteAsync());
        Assert.Equal(515, nullError.Number);
        Assert.Equal(1, await session.From<Account>().CountAsync());
    }

    [SqlServerFact]
    public async Task Caller_transaction_rollback_reverts_insert_and_update()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var session = await fixture.Database.OpenSessionAsync();
        var account = new Account { Tenant = 1, Email = "existing", Name = "before" };
        await Upsert(session, account).ExecuteAsync();
        await using (var transaction = await session.BeginTransactionAsync())
        {
            account.Name = "after";
            await Upsert(transaction, account).ExecuteAsync();
            await Upsert(transaction, new Account { Tenant = 2, Email = "new", Name = "new" }).ExecuteAsync();
            await transaction.RollbackAsync();
        }
        Assert.Equal("before", (await session.From<Account>().SingleAsync()).Name);
    }

    [SqlServerFact]
    public Task Concurrent_insert_then_update() => ContentionAsync(false, false, false, false);
    [SqlServerFact]
    public Task Concurrent_insert_then_skip() => ContentionAsync(true, false, false, false);
    [SqlServerFact]
    public Task Concurrent_insert_after_first_writer_rolls_back() => ContentionAsync(false, true, false, false);
    [SqlServerFact]
    public Task Concurrent_updates_to_existing_row() => ContentionAsync(false, false, true, false);
    [SqlServerFact]
    public Task Cancel_blocked_writer_without_changing_first_writer() => ContentionAsync(false, false, false, true);

    private static async Task ContentionAsync(bool skip, bool rollback, bool existing, bool cancel)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var contenderCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var token = deadline.Token;
        await using var fixture = await Fixture.CreateAsync();
        await using var first = await fixture.Database.OpenSessionAsync(token);
        await using var second = await fixture.Database.OpenSessionAsync(token);
        await using var observer = await fixture.Database.OpenSessionAsync(token);
        if (existing)
            await Upsert(first, new Account { Tenant = 1, Email = "same", Name = "original", Note = "keep" }).ExecuteAsync(cancellationToken: token);
        var firstId = await first.ScalarAsync<int>("SELECT @@SPID", cancellationToken: token);
        var secondId = await second.ScalarAsync<int>("SELECT @@SPID", cancellationToken: token);
        await using var transaction = await first.BeginTransactionAsync(cancellationToken: token);
        await Upsert(transaction, new Account { Tenant = 1, Email = "same", Name = "first", Note = "keep" }).ExecuteAsync(cancellationToken: token);
        var contender = Upsert(second, new Account { Tenant = 1, Email = "same", Name = "second", Note = "incoming" });
        if (skip) contender.DoNothing();
        var pending = contender.ExecuteAsync(cancellationToken: contenderCancellation.Token);
        try
        {
            while (await observer.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id = @secondId AND blocking_session_id = @firstId",
                new { firstId, secondId }, cancellationToken: token) == 0)
            {
                Assert.False(pending.IsCompleted, "The contender did not wait for the first writer.");
                await Task.Delay(25, token);
            }
            if (cancel)
            {
                await contenderCancellation.CancelAsync();
                var error = await Record.ExceptionAsync(async () => await pending);
                // SqlClient can surface server attention as SqlException rather than a canceled task.
                Assert.True(error is OperationCanceledException || error is SqlException sqlError
                    && sqlError.Errors.Cast<SqlError>().Any(item => item.Number == 0), error?.ToString());
            }
            if (rollback) await transaction.RollbackAsync(token);
            else await transaction.CommitAsync(token);
            if (!cancel) Assert.Equal(skip && !rollback ? 0 : 1, await pending);
            var stored = await observer.From<Account>().SingleAsync(cancellationToken: token);
            Assert.Equal(skip || cancel ? "first" : "second", stored.Name);
            Assert.Equal(rollback ? "incoming" : "keep", stored.Note);
        }
        finally
        {
            await contenderCancellation.CancelAsync();
            try { await pending; } catch (Exception) when (pending.IsCompleted) { }
        }
    }

    private static EntityUpsert<Account> Upsert(IDbExecutor executor, Account value) =>
        executor.UpsertInto<Account>().Values(value).OnConflict(a => a.Tenant, a => a.Email).UpdateColumns(a => a.Name);

    private sealed class Account
    {
        [Key, Column("id"), DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
        [Column("tenant_id")] public int Tenant { get; set; }
        [Column("email")] public string? Email { get; set; }
        [Column("display_name")] public string Name { get; set; } = "";
        [Column("note")] public string Note { get; set; } = "";
    }

    private sealed class Fixture(SqlConnection anchor, string table, SnapDatabase database) : IAsyncDisposable
    {
        public SnapDatabase Database { get; } = database;
        public static async Task<Fixture> CreateAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable("SNAPDATA_SQLSERVER_CONNECTION")!;
            var anchor = new SqlConnection(connectionString);
            try { await anchor.OpenAsync(); }
            catch { await anchor.DisposeAsync(); throw; }
            var table = "upsert_" + Guid.NewGuid().ToString("N");
            var mapping = new EntityMappingProvider(new MappingOptions { TableName = _ => table });
            var fixture = new Fixture(anchor, table, new SnapDatabase(SqlClientFactory.Instance, connectionString, SqlServerQueryCompiler.Instance, mapping));
            try
            {
                await using var command = anchor.CreateCommand();
                command.CommandText = $"""
                    CREATE TABLE [dbo].[{table}] (
                        id BIGINT IDENTITY PRIMARY KEY,
                        tenant_id INT NOT NULL,
                        email NVARCHAR(200) NULL,
                        display_name NVARCHAR(200) NOT NULL UNIQUE,
                        note NVARCHAR(200) NULL,
                        UNIQUE (tenant_id, email)
                    )
                    """;
                await command.ExecuteNonQueryAsync();
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = anchor.CreateCommand();
                command.CommandText = $"DROP TABLE IF EXISTS [dbo].[{table}]";
                await command.ExecuteNonQueryAsync();
            }
            finally { await anchor.DisposeAsync(); }
        }
    }
}

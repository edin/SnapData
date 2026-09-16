using Npgsql;

namespace SnapData.IntegrationTests;

public sealed class PostgresUpsertTests
{
    [PostgresFact]
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

    [PostgresFact]
    public async Task Typed_upsert_updates_only_selected_columns_and_does_not_hydrate_identity()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var session = await fixture.Database.OpenSessionAsync();
        var account = new Account { Tenant = 1, Email = "a@example.com", Name = "first", Note = "keep" };
        Assert.Equal(1, await Upsert(session, account).ExecuteAsync());
        Assert.Equal(0, account.Id);
        account.Name = "O'Reilly";
        account.Note = "replace";
        Assert.Equal(1, await Upsert(session, account).ExecuteAsync());
        var stored = await session.From<Account>().SingleAsync();
        Assert.True(stored.Id > 0);
        Assert.Equal("O'Reilly", stored.Name);
        Assert.Equal("keep", stored.Note);
        Assert.Equal(0, await Upsert(session, account).DoNothing().ExecuteAsync());
        account.Tenant = 2;
        Assert.Equal(1, await Upsert(session, account).ExecuteAsync());
        Assert.Equal(2, await session.From<Account>().CountAsync());
    }

    [PostgresFact]
    public async Task Do_nothing_does_not_suppress_other_constraint_failures()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var session = await fixture.Database.OpenSessionAsync();
        await session.ExecuteAsync(Sql.UpsertInto("accounts")
            .Values(new { id = 10, tenant_id = 1, email = "first", display_name = "first" })
            .OnConflict("tenant_id", "email").DoNothing());
        var error = await Assert.ThrowsAsync<PostgresException>(() => session.ExecuteAsync(
            Sql.UpsertInto("accounts")
                .Values(new { id = 10, tenant_id = 2, email = "other", display_name = "other" })
                .OnConflict("tenant_id", "email").DoNothing()));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        var nullError = await Assert.ThrowsAsync<PostgresException>(() => session.ExecuteAsync(
            Sql.UpsertInto("accounts")
                .Values(new { tenant_id = 3, email = "null", display_name = (string?)null })
                .OnConflict("tenant_id", "email").DoNothing()));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, nullError.SqlState);
        Assert.Equal(1, await session.From<Account>().CountAsync());
    }

    [PostgresFact]
    public async Task Rollback_reverts_both_insert_and_update()
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
        var stored = await session.From<Account>().SingleAsync();
        Assert.Equal("before", stored.Name);
    }

    [PostgresFact]
    public Task Concurrent_upsert_waits_for_uncommitted_insert_then_updates() => ConcurrentConflictAsync(false);

    [PostgresFact]
    public Task Concurrent_do_nothing_waits_then_preserves_the_insert() => ConcurrentConflictAsync(true);

    private static async Task ConcurrentConflictAsync(bool doNothing)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        await using var fixture = await Fixture.CreateAsync();
        await using var first = await fixture.Database.OpenSessionAsync(token);
        await using var second = await fixture.Database.OpenSessionAsync(token);
        await using var observer = await fixture.Database.OpenSessionAsync(token);
        var secondPid = await second.ScalarAsync<int>("SELECT pg_backend_pid()", cancellationToken: token);
        var firstPid = await first.ScalarAsync<int>("SELECT pg_backend_pid()", cancellationToken: token);
        await using var transaction = await first.BeginTransactionAsync(cancellationToken: token);
        await Upsert(transaction, new Account { Tenant = 1, Email = "same", Name = "first", Note = "keep" })
            .ExecuteAsync(cancellationToken: token);
        var contender = Upsert(second, new Account { Tenant = 1, Email = "same", Name = "second", Note = "replace" });
        if (doNothing) contender.DoNothing();
        var pending = contender.ExecuteAsync(cancellationToken: token);
        try
        {
            // Confirm real lock contention instead of relying on two tasks starting at similar times.
            while (!await observer.ScalarAsync<bool>(
                "SELECT @firstPid = ANY(pg_blocking_pids(@secondPid))",
                new { firstPid, secondPid }, cancellationToken: token))
            {
                Assert.False(pending.IsCompleted, "The competing upsert completed without waiting for the uncommitted insert.");
                await Task.Delay(25, token);
            }
            await transaction.CommitAsync(token);
            Assert.Equal(doNothing ? 0 : 1, await pending);
            var stored = await observer.From<Account>().SingleAsync(cancellationToken: token);
            Assert.Equal(doNothing ? "first" : "second", stored.Name);
            Assert.Equal("keep", stored.Note);
        }
        finally
        {
            // Always finish the pending command before sessions and the fixture are disposed.
            await timeout.CancelAsync();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }

    private static EntityUpsert<Account> Upsert(IDbExecutor executor, Account account) =>
        executor.UpsertInto<Account>().Values(account)
            .OnConflict(a => a.Tenant, a => a.Email).UpdateColumns(a => a.Name);

    [Table("accounts")]
    private sealed class Account
    {
        [Key, Column("id"), DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        [Column("tenant_id")] public int Tenant { get; set; }
        [Column("email")] public string Email { get; set; } = "";
        [Column("display_name")] public string Name { get; set; } = "";
        [Column("note")] public string Note { get; set; } = "";
    }

    private sealed class Fixture(NpgsqlConnection anchor, string schema, SnapDatabase database) : IAsyncDisposable
    {
        public SnapDatabase Database { get; } = database;

        public static async Task<Fixture> CreateAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable("SNAPDATA_POSTGRES_CONNECTION")!;
            var anchor = new NpgsqlConnection(connectionString);
            await anchor.OpenAsync();
            var schema = "upsert_test_" + Guid.NewGuid().ToString("N");
            var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema, Pooling = false };
            var fixture = new Fixture(anchor, schema, new SnapDatabase(NpgsqlFactory.Instance, scoped.ConnectionString, PostgresQueryCompiler.Instance));
            try
            {
                await using var command = anchor.CreateCommand();
                command.CommandText = $"""
                    CREATE SCHEMA "{schema}";
                    CREATE TABLE "{schema}".accounts (
                        id BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        tenant_id INTEGER NOT NULL,
                        email TEXT NOT NULL,
                        display_name TEXT NOT NULL,
                        note TEXT,
                        UNIQUE (tenant_id, email)
                    );
                    """;
                await command.ExecuteNonQueryAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = anchor.CreateCommand();
                command.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
                await command.ExecuteNonQueryAsync();
            }
            finally { await anchor.DisposeAsync(); }
        }
    }
}
